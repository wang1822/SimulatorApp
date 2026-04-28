using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Irony.Parsing;
using Microsoft.Win32;
using SimulatorApp.Master.Models;
using SimulatorApp.Master.Services;
using SimulatorApp.Master.Views;
using SimulatorApp.Shared.Helpers;
using SimulatorApp.Shared.Logging;
using SimulatorApp.Shared.Models;
using SimulatorApp.Shared.Views;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Ports;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Threading;
using PasswordDialog = SimulatorApp.Master.Views.PasswordDialog;

namespace SimulatorApp.Master.ViewModels;

/// <summary>
/// 主站 ViewModel。
/// 职责：DB 连接 → 站点管理 → Modbus 连接 → 轮询（多地址段）→ 遥测/遥控显示。
/// 使用 [ObservableProperty] 的类必须是 partial class。
/// </summary>
public partial class MasterViewModel : ObservableObject
{
    // ── DB 连接 ───────────────────────────────────────────────────────────
    [ObservableProperty]
    private string _dbConnectionString =
        "Server=10.184.4.153,1433;Database=ModBusT;User Id=sa;Password=000000;Encrypt=True;TrustServerCertificate=True;Connect Timeout=10;";

    [ObservableProperty] private bool   _isDbConnected = false;
    [ObservableProperty] private string _dbStatusText  = "未连接数据库";

    // ── 站点列表 ──────────────────────────────────────────────────────────
    public ObservableCollection<MasterStation> Stations { get; } = new();


    // CommunityToolkit.Mvvm 的源码生成写法：
    // [ObservableProperty] private MasterStation? _selectedStation;
    //
    // 编译后会自动生成等价属性：
    // public MasterStation? SelectedStation
    // {
    //     get => _selectedStation;
    //     set => SetProperty(ref _selectedStation, value);
    // }
    // 绑定界面中的选择设备站点SelectedStation，选中后自动回填连接参数并加载寄存器配置。
    [ObservableProperty] private MasterStation? _selectedStation;
    // 绑定界面中的编辑设备站点名称SelectedStationEditName。
    [ObservableProperty] private string _selectedStationEditName = string.Empty;
    // [ObservableProperty] private MasterStation? _selectedStation; 生成 SelectedStation 属性。
    public bool HasSelectedStation => SelectedStation != null;

    // 钩子，SelectedStation发生变化时自动回填连接参数并加载寄存器配置
    partial void OnSelectedStationChanged(MasterStation? value)
    {
        OnPropertyChanged(nameof(HasSelectedStation));
        if (value == null) return;
        SelectedStationEditName = value.Name;
        // 自动回填连接参数
        Protocol       = (ProtocolType)value.Protocol;
        RemoteHost     = value.Host;
        RemotePort     = value.Port;
        ComPort        = value.PortName;
        BaudRate       = value.BaudRate;
        SlaveId        = value.SlaveId;
        PollIntervalMs = value.PollIntervalMs;
        // 异步加载寄存器配置
        _ = LoadRegisterConfigsAsync(value.Id);
    }

    // 钩子，SelectedStationEditName 发生变化时自动写回 SelectedStation.Name 并更新 DB（如果连接了 DB 且 站点已保存）
    partial void OnSelectedStationEditNameChanged(string value)
    {
        if (SelectedStation == null || string.IsNullOrWhiteSpace(value)) return;
        if (value == SelectedStation.Name) return;
        SelectedStation.Name = value;
        if (_dbService != null && SelectedStation.Id > 0)
            _ = _dbService.UpdateStationNameAsync(SelectedStation.Id, value);
    }

    // ── Modbus 连接参数 ───────────────────────────────────────────────────
    [ObservableProperty] private ProtocolType _protocol       = ProtocolType.Tcp;
    [ObservableProperty] private string  _remoteHost          = "172.168.3.100";
    [ObservableProperty] private int     _remotePort          = 502;
    [ObservableProperty] private byte    _slaveId             = 1;
    [ObservableProperty] private string  _comPort             = "COM3";
    [ObservableProperty] private int     _baudRate            = 9600;
    [ObservableProperty] private int     _pollIntervalMs      = 1000;

    public bool IsTcpMode => Protocol == ProtocolType.Tcp;
    public bool IsRtuMode => Protocol == ProtocolType.Rtu;
    partial void OnProtocolChanged(ProtocolType value)
    {
        OnPropertyChanged(nameof(IsTcpMode));
        OnPropertyChanged(nameof(IsRtuMode));
    }

    // ── API 比对配置 ──────────────────────────────────────────────────
    [ObservableProperty] private string _apiUrl           = string.Empty;
    [ObservableProperty] private string _apiAuthorization = string.Empty;
    [ObservableProperty] private double _verifyTolerance  = 0.5;
    [ObservableProperty] private string _verifyStatusText = string.Empty;
    [ObservableProperty] private int    _verifyFailCount  = 0;

    // ── 连接状态 ──────────────────────────────────────────────────────────
    [ObservableProperty] private bool   _isConnected = false;
    [ObservableProperty] private string _statusText  = "未连接";
    [ObservableProperty] private long   _pollCount   = 0;

    // ── 显示数据 ──────────────────────────────────────────────────────────
    /// <summary>遥测行（只读显示）</summary>
    public ObservableCollection<RegisterDisplayRow> TelemeterRows { get; } = new();
    /// <summary>遥控行（可写入）</summary>
    public ObservableCollection<RegisterDisplayRow> ControlRows   { get; } = new();

    [ObservableProperty] private int _activeTabIndex = 0; // 0=遥测 1=遥控
    partial void OnActiveTabIndexChanged(int value)
    {
        _searchMatchIndex     = -1;
        _unverifiedMatchIndex = -1;
    }

    // ── 搜索 / 定位未通过 ──────────────────────────────────────────────────
    [ObservableProperty] private string _searchText = string.Empty;
    private int _searchMatchIndex     = -1;
    private int _unverifiedMatchIndex = -1;
    /// <summary>通知 code-behind 滚动到指定行（避免 ViewModel 直接引用 View）</summary>
    public event Action<RegisterDisplayRow>? ScrollRequested;

    // ── 日志 ────────────────────────────────────────────────────────────
    public ObservableCollection<LogEntry> LogEntries { get; } = new();

    // ── 辅助 ─────────────────────────────────────────────────────────────
    public ObservableCollection<string> AvailableComPorts { get; } = new();
    /// <summary>本机可用 IP 地址列表（含 127.0.0.1 及所有活跃网卡 IPv4）</summary>
    public ObservableCollection<string> AvailableHosts    { get; } = new();
    public IReadOnlyList<int> BaudRateOptions { get; } = new[] { 4800, 9600, 19200, 38400, 115200 };
    public IReadOnlyList<ComboItem<ProtocolType>> ProtocolItems { get; } = new List<ComboItem<ProtocolType>>
    {
        new("TCP", ProtocolType.Tcp),
        new("RTU", ProtocolType.Rtu),
    };

    // ── 私有成员 ──────────────────────────────────────────────────────────
    private IMasterService?    _service;
    private IMasterDbService?  _dbService;
    /*常见用法：
    你创建 cts = new CancellationTokenSource()
    把 cts.Token 传给异步任务/循环
    需要停止时调用 cts.Cancel()
    任务里检测到 token.IsCancellationRequested 或 ThrowIfCancellationRequested() 后退出
    */
    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _syncCts;   // 绿点 DB 同步循环独立 CTS
    private Task?              _pollTask;
    private List<PollGroup>    _pollGroups = new();
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;

    /// <summary>无 DB 连接时的内存临时站点（key = 临时负数 ID）</summary>
    private readonly Dictionary<int, (MasterStation Station, List<MasterRegisterConfig> Configs)>
        _inMemoryStations = new();
    private int _nextTempId = -1;

    /// <summary>构造函数,刷新Port IP</summary>
    public MasterViewModel()
    {
        RefreshComPorts();
        RefreshAvailableHosts();
    }

    // ====================================================================
    // 命令：DB 连接
    // ====================================================================

    [RelayCommand]
    public async Task ConnectDbAsync()
    {
        if (string.IsNullOrWhiteSpace(DbConnectionString))
        {
            DbStatusText = "请输入连接字符串";
            return;
        }
        try
        {
            DbStatusText  = "连接中...";
            // 连接数据库并初始化表（如果尚未初始化）
            _dbService = new MasterDbService(DbConnectionString);
            await _dbService.InitializeAsync();
            IsDbConnected = true;
            DbStatusText  = "数据库已连接";
            AddLog(LogLevel.Info, "数据库连接成功，表已初始化");
            await LoadStationsAsync();
        }
        catch (Exception ex)
        {
            IsDbConnected = false;
            DbStatusText  = $"DB 连接失败：{ex.Message}";
            AddLog(LogLevel.Error, $"DB 连接失败：{ex.Message}");
        }
    }

    // ====================================================================
    // 命令：Modbus 连接/断开
    // ====================================================================

    [RelayCommand]
    public async Task ConnectAsync()
    {
        if (IsConnected) return;
        // 遥测行（_pollGroups）和遥控行都为空才算真正没有配置
        if (_pollGroups.Count == 0 && ControlRows.Count == 0)
        {
            AddLog(LogLevel.Warn, "当前站点无寄存器配置，请先选择站点或保存配置");
            return;
        }
        try
        {
            var endpoint = BuildEndpoint();
            _service = Protocol == ProtocolType.Tcp ? new TcpMasterService() : (IMasterService)new RtuMasterService();


            // 取消信号发射器
            _cts = new CancellationTokenSource();
            // I/O 密集型 -> 异步 async / await
            // CPU 密集型(计算) -> 多线程 Task.Run
            // 异步，连接时保证UI线程不被阻塞 详情看 \知识积累\多线程与异步理解.md
            await _service.ConnectAsync(endpoint, _cts.Token);
            IsConnected = true;
            StatusText  = Protocol == ProtocolType.Tcp
                ? $"已连接 {RemoteHost}:{RemotePort}"
                : $"已连接 {ComPort}@{BaudRate}";
            AddLog(LogLevel.Info, StatusText);
            // 用 Task.Run 将轮询循环推到线程池，避免 Dispatcher 同步上下文捕获
            // 导致 SlaveException 沿 async 状态机在 UI 线程上重新抛出并触发全局 Dialog
            _pollTask = Task.Run(() => PollLoopAsync(_cts.Token));
        }
        catch (Exception ex)
        {
            StatusText = $"连接失败：{ex.Message}";
            AddLog(LogLevel.Error, $"连接失败：{ex.Message}");
            ThemedMessageBox.Show($"主站连接失败：\n{ex.Message}", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    public async Task DisconnectAsync()
    {
        if (!IsConnected || _service == null) return;
        _cts?.Cancel();
        if (_pollTask != null)
        {
            try { await _pollTask; }
            catch (OperationCanceledException) { /* 正常取消，忽略 */ }
            catch (Exception) { /* 轮询内部已记录，此处忽略 */ }
        }
        await _service.DisconnectAsync();
        await _service.DisposeAsync();
        _service    = null;
        IsConnected = false;
        StatusText  = "已断开";
        AddLog(LogLevel.Info, "主站已断开连接");
    }

    [RelayCommand]
    public async Task ToggleMasterAsync()
    {
        if (IsConnected) await DisconnectAsync();
        else             await ConnectAsync();
    }

    // ====================================================================
    // 命令：站点管理
    // ====================================================================

    /// <summary>新建配置：始终打开空白对话框，不预填任何站点数据</summary>
    [RelayCommand]
    public async Task NewStationAsync()
    {
        var vm = new SaveStationDialogViewModel();
        await OpenStationDialogAsync(vm);
    }

    /// <summary>编辑配置：预填当前选中站点的数据</summary>
    [RelayCommand]
    public async Task OpenSaveStationDialogAsync()
    {
        var vm = new SaveStationDialogViewModel();

        if (SelectedStation != null)
        {
            List<MasterRegisterConfig> existingConfigs;
            if (SelectedStation.Id < 0
                && _inMemoryStations.TryGetValue(SelectedStation.Id, out var mem))
            {
                existingConfigs = mem.Configs;
            }
            else if (_dbService != null)
            {
                existingConfigs = await _dbService.GetRegisterConfigsAsync(SelectedStation.Id);
            }
            else
            {
                existingConfigs = new List<MasterRegisterConfig>();
            }
            vm.LoadFromStation(SelectedStation, existingConfigs);
        }

        await OpenStationDialogAsync(vm);
    }

    private Task OpenStationDialogAsync(SaveStationDialogViewModel vm)
    {
        var dlg = new SaveStationDialog(vm)
        {
            Owner = Application.Current.MainWindow
        };
        // 用户不操作则停在这步，同步阻塞
        dlg.ShowDialog();

        // DialogResult 只有用户点击「保存」才为 true，点击「取消」或关闭窗口则为 false
        if (!vm.DialogResult) return Task.CompletedTask;

        var (station, configs) = vm.BuildResult();

        // 编辑模式：名称未改则保留原 ID
        if (SelectedStation != null && vm.StationName == SelectedStation.Name)
            station.Id = SelectedStation.Id;

        _ = SaveStationToDbAsync(station, configs);
        return Task.CompletedTask;
    }

    [RelayCommand]
    public async Task DeleteStationAsync()
    {
        if (SelectedStation == null) return;
        if (!VerifyPassword())
        {
            AddLog(LogLevel.Warn, "密码错误，取消删除操作");
            return;
        }
        var name = SelectedStation.Name;
        var id   = SelectedStation.Id;

        if (id < 0)
        {
            // 内存临时站点
            _inMemoryStations.Remove(id);
            AddLog(LogLevel.Info, $"已删除临时站点：{name}");
        }
        else if (_dbService != null)
        {
            await _dbService.DeleteStationAsync(id);
            AddLog(LogLevel.Info, $"已删除站点：{name}");
        }
        else
        {
            AddLog(LogLevel.Warn, "无法删除：数据库未连接");
            return;
        }
        await LoadStationsAsync();
    }

    // ====================================================================
    // 命令：清除绿点
    // ====================================================================

    [RelayCommand]
    public async Task ClearVerifiedAsync()
    {
        if (!VerifyPassword())
        {
            AddLog(LogLevel.Warn, "密码错误，取消清除操作");
            return;
        }

        foreach (var row in TelemeterRows.Concat(ControlRows))
            row.IsVerified = false;

        VerifyFailCount = TelemeterRows.Count + ControlRows.Count;

        if (_dbService != null && SelectedStation?.Id > 0)
        {
            try
            {
                await _dbService.ClearAllIsVerifiedAsync(SelectedStation.Id);
                AddLog(LogLevel.Info, $"已清除站点「{SelectedStation.Name}」所有绿点标记");
            }
            catch (Exception ex)
            {
                AddLog(LogLevel.Warn, $"DB 清除绿点失败：{ex.Message}");
            }
        }
        else
        {
            AddLog(LogLevel.Info, "已清除当前视图所有绿点标记（内存模式）");
        }
    }

    // ====================================================================
    // 命令：遥控写入
    // ====================================================================

    [RelayCommand]
    public async Task WriteControlRowAsync(RegisterDisplayRow row)
    {
        // 未连接时静默返回（LostFocus 自动触发，不应弹窗干扰用户）
        if (!IsConnected || _service == null) return;

        if (!double.TryParse(row.WriteValue, out double physVal))
        {
            AddLog(LogLevel.Error, $"写入值格式错误：\"{row.WriteValue}\"  [{row.ChineseName}]");
            return;
        }

        try
        {
            // 遥控写入统一使用 FC16（Write Multiple Registers）。
            // 真实设备控制寄存器普遍只开放 FC16，FC06 会被从站拒绝（SlaveException）。
            ushort[] regs = BuildWriteRegisters(row, physVal);
            await _service.WriteMultipleRegistersAsync(row.StartAddress, regs);
            string hexStr = string.Join(" ", regs.Select(r => $"0x{r:X4}"));
            AddLog(LogLevel.Info,
                $"FC16  addr={row.StartAddress}  regs=[{hexStr}]" +
                $"  [{row.ChineseName}]={physVal}{row.Unit}");
            // 写入成功后更新原始寄存器缓存，再用用户输入的物理值覆盖显示
            // （UpdateFromRaw 会按 DisplayMode 解算为原始 uint，对遥控值不直观）
            row.UpdateFromRaw(regs);
            row.PhysicalValue = physVal.ToString();
            row.WriteValue    = physVal.ToString();
            // 持久化到 DB（fire-and-forget），下次打开程序时恢复显示
            if (_dbService != null && row.RegisterConfigId > 0)
            {
                var db = _dbService;
                _ = db.UpdateLastWrittenAsync(row.RegisterConfigId, row.RawDisplay, physVal.ToString());
            }
        }
        catch (Exception ex)
        {
            AddLog(LogLevel.Error, $"写入失败 [{row.ChineseName}]：{ex.Message}");
        }
    }

    /// <summary>
    /// 遥控行"读取"按钮命令：按需 FC03 读取单个控制寄存器当前值。
    /// 设备拒绝 FC03（SlaveException）时记详细警告，不弹窗。
    /// </summary>
    [RelayCommand]
    public async Task ReadControlRowAsync(RegisterDisplayRow row)
    {
        if (!IsConnected || _service == null)
        {
            AddLog(LogLevel.Warn, "请先点击「▶ 开始轮询」建立连接");
            return;
        }
        try
        {
            var regs = await _service.ReadRegistersAsync(row.StartAddress, row.Quantity);
            row.UpdateFromRaw(regs);
            AddLog(LogLevel.Info,
                $"FC03 读取成功  addr={row.StartAddress}  [{row.ChineseName}]={row.PhysicalValue}{row.Unit}");
        }
        catch (Exception ex)
        {
            // 判断是否为设备主动拒绝（SlaveException 被 BuildReadException 包装为 InvalidOperationException）
            bool deviceRejected = ex.InnerException != null &&
                string.Equals(ex.InnerException.GetType().Name, "SlaveException", StringComparison.Ordinal);

            if (deviceRejected)
            {
                AddLog(LogLevel.Warn,
                    $"[{row.ChineseName}] addr={row.StartAddress} 设备拒绝 FC03 读取：" +
                    "该地址为只写寄存器，无法从设备直接获取当前值。" +
                    "请通过「写入」操作下发值，写入成功后【当前值】列将显示回显值。");
            }
            else
            {
                AddLog(LogLevel.Warn,
                    $"FC03 读取失败 [{row.ChineseName}] addr={row.StartAddress}：{ex.Message}");
            }
        }
    }

    /// <summary>
    /// 将物理值按 DataType 逆算为寄存器数组（含比例系数和偏置还原）。
    /// float/int32/uint32 → 2 个寄存器（高字在前，AB CD 字序）。
    /// int16/uint16       → 1 个寄存器。
    /// </summary>
    [RelayCommand]
    public async Task DeleteRegisterRowAsync(RegisterDisplayRow? row)
    {
        if (row == null) return;
        if (!VerifyPassword()) return;
        bool removed = TelemeterRows.Remove(row) || ControlRows.Remove(row);
        if (!removed) return;
        foreach (var pg in _pollGroups) pg.Rows.Remove(row);
        _pollGroups.RemoveAll(pg => pg.Rows.Count == 0);
        if (_dbService != null && row.RegisterConfigId > 0)
        {
            try   { await _dbService.DeleteRegisterConfigAsync(row.RegisterConfigId); }
            catch (Exception ex) { AddLog(LogLevel.Warn, "DB delete failed: " + ex.Message); }
        }
    }

    private static ushort[] BuildWriteRegisters(RegisterDisplayRow row, double physVal)
    {
        double raw = (physVal - row.Offset) / row.ScaleFactor;
        return row.DataType.ToLowerInvariant() switch
        {
            "float"  => FloatToRegs((float)raw),
            "uint32" => SplitU32((uint)Math.Round(raw)),
            "int32"  => SplitU32((uint)(int)Math.Round(raw)),
            "int16"  => new ushort[] { (ushort)(short)Math.Round(raw) },
            _        => new ushort[] { (ushort)Math.Round(raw) }    // uint16
        };
    }

    private static ushort[] FloatToRegs(float v)
    {
        var (h, l) = FloatRegisterHelper.ToRegisters(v);
        return new ushort[] { h, l };
    }

    private static ushort[] SplitU32(uint v)
        => new ushort[] { (ushort)(v >> 16), (ushort)(v & 0xFFFF) };

    // ====================================================================
    // 命令：API 比对
    // ====================================================================

    [RelayCommand]
    public void ToggleVerifyRow(RegisterDisplayRow? row)
    {
        if (row == null) return;
        row.IsVerified = !row.IsVerified;
        if (_dbService != null && row.RegisterConfigId > 0)
            _ = _dbService.UpdateIsVerifiedAsync(row.RegisterConfigId, row.IsVerified);
    }

    [RelayCommand]
    public async Task VerifyOnceAsync()
    {
        if (string.IsNullOrWhiteSpace(ApiUrl))
        {
            AddLog(LogLevel.Warn, "请先填写 API 地址");
            return;
        }
        await RunVerifyAsync(CancellationToken.None);
    }

    // ====================================================================
    // 命令：搜索 / 定位未通过
    // ====================================================================

    [RelayCommand]
    public void SearchNext()
    {
        var keyword = SearchText.Trim();
        if (string.IsNullOrEmpty(keyword)) return;
        var lk = keyword.ToLower();
        var source = GetActiveRows();
        if (source.Count == 0) return;

        int start = (_searchMatchIndex + 1) % source.Count;
        int found = -1;
        for (int i = 0; i < source.Count; i++)
        {
            int idx = (start + i) % source.Count;
            var row = source[idx];
            if ((row.ChineseName?.ToLower().Contains(lk) == true) ||
                (row.VariableName?.ToLower().Contains(lk) == true))
            {
                found = idx;
                break;
            }
        }
        if (found >= 0)
        {
            _searchMatchIndex = found;
            ScrollRequested?.Invoke(source[found]);
        }
        else
        {
            AddLog(LogLevel.Info,
                $"未找到「{keyword}」（{(ActiveTabIndex == 0 ? "遥测" : "遥控")}，共 {source.Count} 行）");
        }
    }

    [RelayCommand]
    public void NextUnverified()
    {
        var source = GetActiveRows();
        if (source.Count == 0) return;

        int start = (_unverifiedMatchIndex + 1) % source.Count;
        int found = -1;
        for (int i = 0; i < source.Count; i++)
        {
            int idx = (start + i) % source.Count;
            if (!source[idx].IsVerified)
            {
                found = idx;
                break;
            }
        }
        if (found >= 0)
        {
            _unverifiedMatchIndex = found;
            ScrollRequested?.Invoke(source[found]);
        }
        else
        {
            AddLog(LogLevel.Info,
                $"{(ActiveTabIndex == 0 ? "遥测" : "遥控")} 所有行均已通过验证");
        }
    }

    private List<RegisterDisplayRow> GetActiveRows()
        => ActiveTabIndex == 0 ? TelemeterRows.ToList() : ControlRows.ToList();

    // ====================================================================
    // 命令：导出/刷新/清空
    // ====================================================================

    [RelayCommand]
    public void ExportPollData()
    {
        var dlg = new SaveFileDialog
        {
            Filter   = "Excel 文件|*.xlsx",
            FileName = $"主站轮询数据_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            ExportToExcel(dlg.FileName);
            AddLog(LogLevel.Info, $"导出成功 → {dlg.FileName}");
        }
        catch (Exception ex)
        {
            AddLog(LogLevel.Error, $"导出失败：{ex.Message}");
        }
    }

    [RelayCommand]
    public void RefreshComPorts()
    {
        AvailableComPorts.Clear();
        foreach (var p in SerialPort.GetPortNames())
            AvailableComPorts.Add(p);
        // 如果有串口，且当前 ComPort 不在新列表里（比如设备拔掉了），就自动切到第一个可用串口。
        if (AvailableComPorts.Count > 0 && !AvailableComPorts.Contains(ComPort))
            ComPort = AvailableComPorts[0];
    }

    [RelayCommand]
    public void RefreshAvailableHosts()
    {
        AvailableHosts.Clear();
        AvailableHosts.Add("172.168.3.100");
        try
        {
            // 枚举所有活跃网卡的 IPv4 地址，排除环回和非活动接口
            foreach (var iface in NetworkInterface.GetAllNetworkInterfaces()
                .Where(i => i.OperationalStatus == OperationalStatus.Up
                         && i.NetworkInterfaceType != NetworkInterfaceType.Loopback))
            {
                foreach (var addr in iface.GetIPProperties().UnicastAddresses
                    .Where(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork))
                {
                    var ip = addr.Address.ToString();
                    if (!AvailableHosts.Contains(ip))
                        AvailableHosts.Add(ip);
                }
            }
        }
        catch (Exception ex)
        {
            AddLog(LogLevel.Warn, $"枚举本机 IP 失败：{ex.Message}");
        }
        if (!AvailableHosts.Contains(RemoteHost))
            AvailableHosts.Add(RemoteHost);
    }

    [RelayCommand]
    public void ClearLog()
    {
        if (!VerifyPassword())
        {
            AddLog(LogLevel.Warn, "密码错误，取消删除操作");
            return;
        }

        // 先清空界面日志
        LogEntries.Clear();

        try
        {
            // 刷新 NLog 写入缓冲，确保所有内容已落盘
            NLog.LogManager.Flush();

            string logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
            int deleted = 0;
            var failed  = new List<string>();

            if (Directory.Exists(logDir))
            {
                foreach (var file in Directory.GetFiles(logDir, "*.log"))
                {
                    try
                    {
                        File.Delete(file);
                        deleted++;
                    }
                    catch
                    {
                        failed.Add(Path.GetFileName(file));
                    }
                }
            }

            if (failed.Count == 0)
                AddLog(LogLevel.Info,
                    deleted > 0
                        ? $"已删除 {deleted} 个日志文件 → {logDir}"
                        : "日志目录为空，无文件需删除");
            else
                AddLog(LogLevel.Warn,
                    $"已删除 {deleted} 个，以下文件被占用无法删除：{string.Join("、", failed)}");
        }
        catch (Exception ex)
        {
            AddLog(LogLevel.Error, $"删除日志文件失败：{ex.Message}");
        }
    }

    [RelayCommand]
    public void ClearLogUi() => LogEntries.Clear();

    // ====================================================================
    // 轮询循环（由 MasterViewModel 驱动，不在 Service 内）
    // ====================================================================

    private async Task PollLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // 快照：防止 LoadRegisterConfigsAsync 在轮询途中替换 _pollGroups
                var groups = _pollGroups;
                foreach (var group in groups)
                {
                    ushort[]? regs = null;
                    try
                    {
                        // 轮询读取当前组地址段的寄存器值
                        regs = await _service!.ReadRegistersAsync(group.StartAddress, group.Length)
                                              .ConfigureAwait(false);
                    }
                    catch (Exception pollEx) when (pollEx is not OperationCanceledException)
                    {
                        // 仅当 _service 已被置 null（DisconnectAsync 已执行完毕）才走断开流程；
                        // IsConnected 依赖 TcpClient.Connected 在并发断开时可能短暂为 false，
                        // 不能单独作为判断依据，否则轮询异常会被误判为连接断开。
                        if (_service == null) throw;

                        // 遥测地址读取瞬时失败（超时/SlaveException）：记日志后跳过本组，继续下一组
                        int    addr   = group.StartAddress;
                        int    len    = group.Length;
                        string exType = pollEx.GetType().Name;
                        string exMsg  = pollEx.Message;
                        _dispatcher.InvokeAsync(() =>
                            AddLog(LogLevel.Warn,
                                $"轮询跳过 addr={addr} len={len}  [{exType}] {exMsg}"));
                        continue;
                    }

                    // fire-and-forget：轮询在线程池运行，UI 更新 post 到 Dispatcher 后立即继续下一组
                    // ReferenceEquals 检测 groups 是否已过期（切站点时会替换 _pollGroups）
                    var capturedRegs  = regs!;
                    var capturedGroup = group;
                    _dispatcher.InvokeAsync(() =>
                    {
                        if (!ReferenceEquals(_pollGroups, groups)) return;
                        foreach (var row in capturedGroup.Rows)
                        {
                            int offset = row.StartAddress - capturedGroup.StartAddress;
                            int end    = offset + row.Quantity;
                            if (offset >= 0 && end <= capturedRegs.Length)
                                row.UpdateFromRaw(capturedRegs[offset..end]);
                        }
                        PollCount++;
                    });

                    AppLogger.ModbusRequest(0x03, group.StartAddress, group.Length,
                        SlaveId, $"{RemoteHost}:{RemotePort}");
                }

                await Task.Delay(PollIntervalMs, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _dispatcher.InvokeAsync(() =>
                {
                    IsConnected = false;
                    StatusText  = $"通信异常：{ex.Message}";
                    AddLog(LogLevel.Error, $"通信异常：{ex.Message}");
                });
                break;
            }
        }
    }

    // ====================================================================
    // API 比对（私有）
    // ====================================================================

    private async Task RunVerifyAsync(CancellationToken ct)
    {
        try
        {
            var apiData = await ApiVerifyService.FetchNumericFieldsAsync(ApiUrl, ApiAuthorization, ct);

            var snapshot = await _dispatcher.InvokeAsync(() =>
                TelemeterRows.Concat(ControlRows)
                             .Select(r => (row: r, varName: r.VariableName, phys: r.LastPhysicalRaw))
                             .ToList());

            var results = snapshot.Select(item =>
            {
                bool ok = ApiVerifyService.TryMatch(apiData, item.varName, out double apiVal)
                          && Math.Abs(apiVal - item.phys) <= VerifyTolerance;
                return (item.row, ok);
            }).ToList();

            await _dispatcher.InvokeAsync(() =>
            {
                int newlyMatched = 0;
                var toWrite = new List<(int id, bool val)>();
                foreach (var (row, ok) in results)
                {
                    if (ok && !row.IsVerified)
                    {
                        row.IsVerified = true;
                        newlyMatched++;
                        if (_dbService != null && row.RegisterConfigId > 0)
                            toWrite.Add((row.RegisterConfigId, true));
                    }
                }
                // 批量写 DB（fire-and-forget，不阻塞 UI）
                if (toWrite.Count > 0 && _dbService != null)
                {
                    var db = _dbService;
                    _ = Task.WhenAll(toWrite.Select(x => db.UpdateIsVerifiedAsync(x.id, x.val)));
                }
                int totalVerified = TelemeterRows.Concat(ControlRows).Count(r => r.IsVerified);
                int totalRows     = TelemeterRows.Count + ControlRows.Count;
                VerifyFailCount  = totalRows - totalVerified;
                VerifyStatusText = $"本次新增 {newlyMatched} 项，累计通过 {totalVerified}/{totalRows}（误差≤{VerifyTolerance}）";
                AddLog(LogLevel.Info, VerifyStatusText);
            });
        }
        catch (OperationCanceledException) { /* 正常取消，忽略 */ }
        catch (Exception ex)
        {
            await _dispatcher.InvokeAsync(() =>
            {
                VerifyStatusText = $"API 比对失败：{ex.Message}";
                AddLog(LogLevel.Warn, VerifyStatusText);
            });
        }
    }

    // ====================================================================
    // 私有辅助
    // ====================================================================

    private async Task LoadStationsAsync()
    {
        Stations.Clear();
        if (_dbService != null)
        {
            var list = await _dbService.GetAllStationsAsync();
            foreach (var s in list) Stations.Add(s);
        }
        // 始终追加内存临时站点（名称后加"(临时)"标识）
        foreach (var (s, _) in _inMemoryStations.Values)
            Stations.Add(s);

        int tmpCount = _inMemoryStations.Count;
        string detail = tmpCount > 0 ? $"（含 {tmpCount} 个临时）" : string.Empty;
        AddLog(LogLevel.Info, $"已加载 {Stations.Count} 个站点{detail}");
    }

    private async Task LoadRegisterConfigsAsync(int stationId)
    {
        // 取消订阅旧行的属性变更事件
        foreach (var r in TelemeterRows.Concat(ControlRows))
            r.PropertyChanged -= OnRowPropertyChanged;

        // 先用新空列表替换 _pollGroups（不 Clear），使后台 poll 快照的旧引用自然过期
        _pollGroups = new List<PollGroup>();
        _searchMatchIndex     = -1;
        _unverifiedMatchIndex = -1;
        TelemeterRows.Clear();
        ControlRows.Clear();

        List<MasterRegisterConfig> configs;
        if (stationId < 0 && _inMemoryStations.TryGetValue(stationId, out var mem))
        {
            configs = mem.Configs;
        }
        else if (_dbService != null)
        {
            configs = await _dbService.GetRegisterConfigsAsync(stationId);
        }
        else
        {
            AddLog(LogLevel.Warn, "请先选择站点或连接数据库");
            return;
        }

        if (configs.Count == 0)
        {
            AddLog(LogLevel.Warn, $"站点 ID={stationId} 暂无寄存器配置");
            return;
        }

        var allRows = new List<RegisterDisplayRow>();
        foreach (var cfg in configs)
        {
            var row = new RegisterDisplayRow
            {
                RegisterConfigId = cfg.Id,
                StartAddress     = cfg.StartAddress,
                Quantity         = cfg.Quantity,
                VariableName     = cfg.VariableName,
                ChineseName      = cfg.ChineseName,
                Unit             = cfg.Unit,
                DataType         = cfg.DataType,
                ReadWrite        = cfg.ReadWrite,
                ScaleFactor      = cfg.ScaleFactor,
                Offset           = cfg.Offset,
                ValueRange       = cfg.ValueRange,
                Description      = cfg.Description,
                Category         = cfg.Category,
                IsVerified       = cfg.IsVerified
            };
            // 遥控行：从 DB 恢复上次写入值（不需要 FC03 轮询即可显示历史值）
            if (cfg.Category == 1)
                row.InitFromSaved(cfg.LastRawRegisters, cfg.LastPhysicalValue);

            allRows.Add(row);
            row.PropertyChanged += OnRowPropertyChanged;
            if (cfg.Category == 0) TelemeterRows.Add(row);
            else                   ControlRows.Add(row);
        }

        // 只将遥测行（Category=0）纳入自动 FC03 轮询。
        // 遥控行（Category=1）不参与轮询：真实设备控制寄存器通常只接受 FC16 写入，
        // FC03 读取会返回 SlaveException。写入成功后由 WriteControlRowAsync 直接回显值；
        // 如需手动读取请点击遥控行的"读取"按钮（FC03 按需触发，失败时记日志不弹窗）。
        _pollGroups = BuildPollGroups(allRows.Where(r => r.Category == 0).ToList());

        int telCount  = allRows.Count(r => r.Category == 0);
        int ctrlCount = allRows.Count - telCount;
        AddLog(LogLevel.Info,
            $"已加载 {configs.Count} 条寄存器配置" +
            $"（遥测 {telCount} 条 → {_pollGroups.Count} 个轮询段，遥控 {ctrlCount} 条仅手动读写）");

        // 重启绿点同步循环（仅 DB 模式）
        RestartSyncLoop();
    }

    private async Task SaveStationToDbAsync(MasterStation station, List<MasterRegisterConfig> configs)
    {
        if (_dbService == null)
        {
            // 无 DB：保存到内存，临时 ID 为负数
            if (station.Id == 0)
            {
                station.Id        = _nextTempId--;
                station.CreatedAt = DateTime.Now;
            }
            _inMemoryStations[station.Id] = (station, configs);
            AddLog(LogLevel.Info,
                $"站点「{station.Name}」已临时保存（内存，共 {configs.Count} 条配置）");
            await LoadStationsAsync();
            SelectedStation = Stations.FirstOrDefault(s => s.Id == station.Id);
            return;
        }

        // 有 DB：若是内存临时站点，重置 ID 交由 DB 分配
        if (station.Id < 0)
        {
            _inMemoryStations.Remove(station.Id);
            station.Id = 0;
        }

        try
        {
            int stationId = await _dbService.SaveStationAsync(station);
            await _dbService.SaveRegisterConfigsAsync(stationId, configs);
            AddLog(LogLevel.Info,
                $"站点「{station.Name}」已保存到数据库（ID={stationId}，共 {configs.Count} 条配置）");
            await LoadStationsAsync();
            SelectedStation = Stations.FirstOrDefault(s => s.Id == stationId);
        }
        catch (Exception ex)
        {
            AddLog(LogLevel.Error, $"保存站点失败：{ex.Message}");
            ThemedMessageBox.Show($"保存失败：\n{ex.Message}", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// 将 RegisterDisplayRow 列表按地址分组，相邻 gap ≤ 20 且总长度 ≤ 125 的合并为一组。
    /// </summary>
    private static List<PollGroup> BuildPollGroups(List<RegisterDisplayRow> rows)
    {
        const int GAP       = 20;
        const int MAX_REGS  = 125;

        var sorted = rows.OrderBy(r => r.StartAddress).ToList();
        var groups = new List<PollGroup>();
        int i = 0;

        while (i < sorted.Count)
        {
            var groupRows = new List<RegisterDisplayRow> { sorted[i] };
            int gStart    = sorted[i].StartAddress;
            // 单行 Quantity 超过 MAX_REGS 时截断，避免 FC03 帧超长
            // IF Quantity > MAX_REGS THEN  FC03 读取会被从站拒绝（SlaveException），需要分多行读
            int gEnd      = sorted[i].StartAddress + Math.Min(sorted[i].Quantity, MAX_REGS);

            // 并入后续行：地址间隔 ≤ GAP，且合并后长度 ≤ MAX_REGS
            while (i + 1 < sorted.Count)
            {
                var next    = sorted[i + 1];
                int nextEnd = next.StartAddress + next.Quantity;
                if (next.StartAddress - gEnd <= GAP && nextEnd - gStart <= MAX_REGS)
                {
                    i++;
                    groupRows.Add(next);
                    gEnd = Math.Max(gEnd, nextEnd);
                }
                else break;
            }
            groups.Add(new PollGroup(gStart, Math.Min(gEnd - gStart, MAX_REGS), groupRows));
            i++;
        }
        return groups;
    }

    private void ExportToExcel(string filePath)
    {
        using var wb = new ClosedXML.Excel.XLWorkbook();

        WriteSheet(wb, "遥测", TelemeterRows);
        WriteSheet(wb, "遥控", ControlRows);
        wb.SaveAs(filePath);
    }

    private static void WriteSheet(ClosedXML.Excel.XLWorkbook wb,
        string sheetName, IEnumerable<RegisterDisplayRow> rows)
    {
        var ws = wb.AddWorksheet(sheetName);
        string[] headers = { "中文名", "变量名", "地址", "数量", "读取值", "单位", "取值范围", "比例系数", "更新时间" };
        for (int c = 0; c < headers.Length; c++)
            ws.Cell(1, c + 1).Value = headers[c];
        ws.Range(1, 1, 1, headers.Length).Style.Font.Bold = true;
        ws.Range(1, 1, 1, headers.Length).Style.Fill
            .BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#2563EB");
        ws.Range(1, 1, 1, headers.Length).Style.Font.FontColor = ClosedXML.Excel.XLColor.White;

        int row = 2;
        foreach (var r in rows)
        {
            ws.Cell(row, 1).Value = r.ChineseName;
            ws.Cell(row, 2).Value = r.VariableName;
            ws.Cell(row, 3).Value = r.StartAddress;
            ws.Cell(row, 4).Value = r.Quantity;
            ws.Cell(row, 5).Value = r.PhysicalValue;
            ws.Cell(row, 6).Value = r.Unit;
            ws.Cell(row, 7).Value = r.ValueRange;
            ws.Cell(row, 8).Value = r.ScaleFactor;
            ws.Cell(row, 9).Value = r.LastUpdated;
            row++;
        }
        ws.Columns().AdjustToContents();
    }

    private SlaveEndpoint BuildEndpoint() => new()
    {
        Name          = SelectedStation?.Name ?? "主站目标",
        Protocol      = Protocol,
        Host          = RemoteHost,
        Port          = RemotePort,
        PortName      = ComPort,
        BaudRate      = BaudRate,
        SlaveId       = SlaveId,
        PollIntervalMs = PollIntervalMs
    };

    /// <summary>
    /// 当 DataGrid 中某一行的属性发生变化时触发（实现内联编辑自动保存）。
    /// </summary>
    private async void OnRowPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // 1. 类型安全校验：确保触发事件的对象是我们预期的 RegisterDisplayRow
        if (sender is not RegisterDisplayRow row) return;

        // 2. 过滤属性：只拦截用户编辑了“中文名”或“变量名”的事件。
        // （非常关键：防止轮询自动更新 PhysicalValue/LastUpdated 时也乱触发写库操作）
        if (e.PropertyName != nameof(RegisterDisplayRow.ChineseName) &&
            e.PropertyName != nameof(RegisterDisplayRow.VariableName)) return;

        // 3. 拦截无效条件：
        // 如果未连接数据库（_dbService == null），或该行属于还没落库的临时记录（RegisterConfigId <= 0），则不执行保存
        if (_dbService == null || row.RegisterConfigId <= 0) return;

        try
        {
            // 4. 异步保存：把修改后的最新名称静默写入到数据库中
            await _dbService.UpdateRegisterNamesAsync(row.RegisterConfigId, row.ChineseName, row.VariableName);
        }
        catch (Exception ex)
        {
            // 5. 异常处理：保存失败时不弹窗打断用户的连续输入，只是回到 UI 线程在底部控制台输出警告日志
            _dispatcher.InvokeAsync(() => AddLog(LogLevel.Warn, $"名称保存失败：{ex.Message}"));
        }
    }

    /// <summary>弹出密码对话框，返回是否验证通过</summary>
    private static bool VerifyPassword()
    {
        var dlg = new PasswordDialog { Owner = Application.Current.MainWindow };
        dlg.ShowDialog();
        return dlg.Confirmed;
    }

    private void AddLog(LogLevel level, string msg)
    {
        if (LogEntries.Count >= 500) LogEntries.RemoveAt(0);
        LogEntries.Add(LogEntry.Create(level, msg));
    }

    /// <summary>取消旧循环并启动新循环（切换站点时调用）</summary>
    private void RestartSyncLoop()
    {
        _syncCts?.Cancel();
        _syncCts?.Dispose();
        if (_dbService == null) return;          // 无 DB 则不启动
        _syncCts = new CancellationTokenSource();
        _ = IsVerifiedSyncLoopAsync(_syncCts.Token);
    }

    /// <summary>每 1 秒从 DB 拉取绿点状态，同步到当前所有行（多客户端共享）</summary>
    private async Task IsVerifiedSyncLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                //3.	支持取消 (ct)：ct 是一个 CancellationToken。如果在这一秒等待期间，
                //用户点击了“断开连接”或者切换了站点，代码里调用了 _syncCts.Cancel()，
                //这个 Task.Delay 会立刻抛出 OperationCanceledException 中断等待，从而退出 while 循环，干脆利落。
                await Task.Delay(1000, ct);

                if (_dbService == null) continue;

                // UI 线程快照：只取已存入 DB 的行（RegisterConfigId > 0）  InvokeAsync 派发回 UI 线程去读，避免跨线程访问冲突
                var snapshot = await _dispatcher.InvokeAsync(() =>
                    TelemeterRows.Concat(ControlRows)
                                 .Where(r => r.RegisterConfigId > 0)
                                 .Select(r => (r.RegisterConfigId, row: r))
                                 .ToList());

                if (snapshot.Count == 0) continue;

                var map = await _dbService.GetIsVerifiedMapAsync(snapshot.Select(x => x.RegisterConfigId));

                await _dispatcher.InvokeAsync(() =>
                {
                    foreach (var (id, row) in snapshot)
                    {
                        if (map.TryGetValue(id, out bool dbVal) && row.IsVerified != dbVal)
                            row.IsVerified = dbVal;
                    }
                    int total    = TelemeterRows.Count + ControlRows.Count;
                    int verified = TelemeterRows.Concat(ControlRows).Count(r => r.IsVerified);
                    VerifyFailCount = total - verified;
                });
            }
            catch (OperationCanceledException) { break; }
            catch { /* DB 临时不可达，忽略本次，下次继续 */ }
        }
    }
}

/// <summary>轮询地址段（连续地址范围 + 属于该段的所有显示行）</summary>
/// internal 限定仅 MasterViewModel 可见，避免外部误用 sealed 不可继承 record 值语义类型（封装字段）
internal sealed record PollGroup(int StartAddress, int Length, List<RegisterDisplayRow> Rows);

/// <summary>通用 ComboBox 数据项</summary>
public record ComboItem<T>(string Display, T Value);
