using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using SimulatorApp.Shared.Helpers;
using SimulatorApp.Shared.Logging;
using SimulatorApp.Shared.Models;
using SimulatorApp.Shared.Services;
using SimulatorApp.Shared.Views;
using SimulatorApp.Slave.Models;
using SimulatorApp.Slave.Services;
using SimulatorApp.Slave.Views;
using SimulatorApp.Slave.Views.Panels;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO.Ports;
using System.Net;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SimulatorApp.Master.Views;

// Row type alias for protocol import tuples
using ProtocolRow = (string ChineseName, string EnglishName, int Address, string ReadWrite, string Range, string Unit, string Note);

namespace SimulatorApp.Slave.ViewModels;

/// <summary>
/// 从站主 ViewModel：管理多条监听配置、所有设备 ViewModel、设备面板路由。
/// </summary>
public partial class SlaveViewModel : ObservableObject
{
    private readonly IServiceProvider _services;
    private readonly RegisterBank     _bank;
    private DispatcherTimer?          _simTimer;
    private DispatcherTimer?          _tcpConnTimer;
    private int                       _runningCount;
    private readonly Dictionary<SlaveListenerConfig, Action<byte, int, int, string>> _requestHandlers = new();
    private readonly Dictionary<SlaveListenerConfig, Dictionary<string, TcpState>> _tcpPeerSnapshots = new();
    private readonly Dictionary<SlaveListenerConfig, DateTime> _tcpNoClientLogAt = new();
    private readonly Dictionary<SlaveListenerConfig, DateTime> _lastRequestAt = new();
    private readonly Dictionary<SlaveListenerConfig, DateTime> _idleMonitorLogAt = new();
    private readonly Dictionary<SlaveListenerConfig, DateTime> _rtuNoTrafficWarnAt = new();
    private readonly Dictionary<string, DateTime> _deviceNoResponseWarnAt = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _deviceLastRequestAt = new(StringComparer.OrdinalIgnoreCase);
    private const string BuiltinDeviceKeyPrefix = "builtin:";
    private const string ImportedDeviceKeyPrefix = "imported:";
    private readonly Dictionary<DeviceViewModelBase, PropertyChangedEventHandler> _deviceSimHandlers = new();
    private readonly Dictionary<DeviceViewModelBase, PropertyChangedEventHandler> _deviceValueHandlers = new();
    private readonly Dictionary<SlaveListenerConfig, PropertyChangedEventHandler> _listenerConfigHandlers = new();
    private readonly Dictionary<int, SlaveDeviceConfig> _importedDeviceConfigByDbId = new();
    private readonly ModbusPacketCaptureViewModel _packetCaptureVm = new();
    private ModbusPacketCaptureWindow? _packetCaptureWindow;
    private int _listenerProfileVersion = 0;
    private bool _suppressSimGuard = false;
    private bool _suppressListenerBindingRefresh = false;
    private bool _isRefreshingListenerProtocolOptions = false;
    private bool _suppressListenerEnvironmentSelectionChanged = false;
    private bool _isApplyingListenerEnvironment = false;

    private static void LogSys(string message) => AppLogger.Info($"[SYS] {message}");

    private const string DefaultDbCs =
        "Server=10.181.200.153,1433;Database=ModBusT;User Id=sa;Password=Ls-sa-2023;" +
        "Encrypt=True;TrustServerCertificate=True;Connect Timeout=10;";

    // ----------------------------------------------------------------
    // 监听配置集合
    // ----------------------------------------------------------------

    /// <summary>所有监听端点配置，支持多条同时运行</summary>
    public ObservableCollection<SlaveListenerConfig> Listeners { get; } = new();
    public ObservableCollection<SlaveListenerEnvironment> ListenerEnvironments { get; } = new();
    public IRelayCommand AddListenerCommand { get; }

    [ObservableProperty] private SlaveListenerEnvironment? _selectedListenerEnvironment;
    public bool HasListenerEnvironments => ListenerEnvironments.Count > 0;

    partial void OnSelectedListenerEnvironmentChanged(SlaveListenerEnvironment? oldValue, SlaveListenerEnvironment? newValue)
    {
        if (_suppressListenerEnvironmentSelectionChanged || _isApplyingListenerEnvironment || newValue == null)
            return;

        _ = ApplySelectedListenerEnvironmentAsync(oldValue, newValue);
    }

    /// <summary>任意一条监听处于运行状态即为 true</summary>
    public bool IsRunning => _runningCount > 0;

    /// <summary>请求总计数（所有监听合计）</summary>
    [ObservableProperty] private long _requestCount = 0;

    // ----------------------------------------------------------------
    // 设备列表
    // ----------------------------------------------------------------

    public ObservableCollection<DeviceViewModelBase> DeviceList { get; } = new();
    public ObservableCollection<ImportedDeviceViewModel> ImportedDevices { get; } = new();
    public bool HasImportedDevices => ImportedDevices.Count > 0;
    public ObservableCollection<DeviceViewModelBase> BuiltinDevices { get; } = new();
    public IReadOnlyList<DeviceViewModelBase> InspectorList { get; private set; } = [];

    // ── DB 持久化 ────────────────────────────────────────────────────
    [ObservableProperty] private bool   _isSlaveDbConnected = false;
    [ObservableProperty] private string _slaveDbStatusText  = "未连接数据库";
    private ISlaveProtocolDbService? _slaveDbService;

    [ObservableProperty] private DeviceViewModelBase? _selectedDevice;
    public bool CanToggleDeviceSimulation => SelectedDevice is not RegisterInspectorViewModel;
    public bool IsInspectorDeviceSelected => SelectedDevice is RegisterInspectorViewModel;
    public bool IsProtocolBindingVisible => !IsInspectorDeviceSelected;

    public UserControl? SelectedDevicePanel => SelectedDevice == null ? null
        : _panelCache.GetValueOrDefault(SelectedDevice);
    public bool HasSelectedDevice => SelectedDevicePanel != null;

    partial void OnSelectedDeviceChanged(DeviceViewModelBase? value)
    {
        OnPropertyChanged(nameof(SelectedDevicePanel));
        OnPropertyChanged(nameof(HasSelectedDevice));
        OnPropertyChanged(nameof(CanToggleDeviceSimulation));
        OnPropertyChanged(nameof(IsInspectorDeviceSelected));
        OnPropertyChanged(nameof(IsProtocolBindingVisible));
        RefreshListenerDeviceProtocolOptions();

        if (value is RegisterInspectorViewModel)
            ClearAllDeviceSimulationSelections();

        // 切换设备协议只切换右侧编辑面板；监听配置保持用户当前设置。
    }

    // 以实例为键，支持同类型多设备
    private readonly Dictionary<DeviceViewModelBase, UserControl> _panelCache = new();
    // 面板工厂，导入时用于为新实例创建面板（key=VM类型）
    private readonly Dictionary<Type, Func<DeviceViewModelBase, UserControl>> _panelFactories = new();

    // 快速访问各设备 ViewModel
    public PcsViewModel             PcsVm       { get; }
    public BmsViewModel             BmsVm       { get; }
    public MpptViewModel            MpptVm      { get; }
    public AirConditionerViewModel  AirVm       { get; }
    public DehumidifierViewModel    DehumVm     { get; }
    public ExternalMeterViewModel   ExtMeterVm  { get; }
    public StorageMeterViewModel    StorMeterVm { get; }
    public StsInstrumentViewModel   StsInstVm   { get; }
    public StsControlViewModel      StsCtrlVm   { get; }
    public DIDOControllerViewModel  DiDoVm      { get; }
    public DieselGeneratorViewModel DieselVm    { get; }
    public GasDetectorViewModel     GasVm       { get; }

    // ----------------------------------------------------------------
    // 可用地址 / 串口（DataTemplate 通过 RelativeSource 绑定）
    // ----------------------------------------------------------------

    public ObservableCollection<string> AvailableTcpAddresses { get; } = new();
    public ObservableCollection<string> AvailableComPorts     { get; } = new();
    public IReadOnlyList<int>           BaudRateOptions       { get; } = [4800, 9600, 19200, 38400, 115200];

    // ----------------------------------------------------------------
    // 日志
    // ----------------------------------------------------------------

    public ObservableCollection<LogEntry> LogEntries { get; } = new();

    // ----------------------------------------------------------------
    // 构造
    // ----------------------------------------------------------------

    public SlaveViewModel(
        IServiceProvider         services,
        PcsViewModel             pcsVm,
        BmsViewModel             bmsVm,
        MpptViewModel            mpptVm,
        AirConditionerViewModel  airVm,
        DehumidifierViewModel    dehumVm,
        ExternalMeterViewModel   extMeterVm,
        StorageMeterViewModel    storMeterVm,
        StsInstrumentViewModel   stsInstVm,
        StsControlViewModel      stsCtrlVm,
        DIDOControllerViewModel  diDoVm,
        DieselGeneratorViewModel dieselVm,
        GasDetectorViewModel     gasVm,
        RegisterInspectorViewModel inspectorVm)
    {
        _services = services;
        _bank     = services.GetRequiredService<RegisterBank>();
        _packetCaptureVm.HasRunningListener = () => IsRunning;

        PcsVm       = pcsVm;
        BmsVm       = bmsVm;
        MpptVm      = mpptVm;
        AirVm       = airVm;
        DehumVm     = dehumVm;
        ExtMeterVm  = extMeterVm;
        StorMeterVm = storMeterVm;
        StsInstVm   = stsInstVm;
        StsCtrlVm   = stsCtrlVm;
        DiDoVm      = diDoVm;
        DieselVm    = dieselVm;
        GasVm       = gasVm;

        // 注册设备及面板（lambda 接收 vm 参数，面板工厂可复用于动态导入）
        RegisterDevice(pcsVm,       vm => new PcsPanel              { DataContext = vm });
        RegisterDevice(bmsVm,       vm => new BmsPanel              { DataContext = vm });
        RegisterDevice(mpptVm,      vm => new MpptPanel             { DataContext = vm });
        RegisterDevice(airVm,       vm => new AirConditionerPanel   { DataContext = vm });
        RegisterDevice(dehumVm,     vm => new DehumidifierPanel     { DataContext = vm });
        RegisterDevice(extMeterVm,  vm => new ExternalMeterPanel    { DataContext = vm });
        RegisterDevice(storMeterVm, vm => new StorageMeterPanel     { DataContext = vm });
        RegisterDevice(stsInstVm,   vm => new StsInstrumentPanel    { DataContext = vm });
        RegisterDevice(stsCtrlVm,   vm => new StsControlPanel       { DataContext = vm });
        RegisterDevice(diDoVm,      vm => new DIDOControllerPanel   { DataContext = vm });
        RegisterDevice(dieselVm,    vm => new DieselGeneratorPanel  { DataContext = vm });
        RegisterDevice(gasVm,       vm => new GasDetectorPanel      { DataContext = vm });
        RegisterDevice(inspectorVm, vm => new RegisterInspectorPanel{ DataContext = vm });
        InspectorList = [inspectorVm];
        foreach (var vm in BuiltinDevices)
            vm.IsSimulating = false;

        SelectedDevice = null;

        // 默认添加一条 TCP 监听配置
        var defaultListener = new SlaveListenerConfig { IsEnabled = true };
        AddListenerConfig(defaultListener);
        RefreshListenerDeviceProtocolOptions();

        RefreshTcpAddresses();
        RefreshComPorts();

        AppLogger.OnUiLog += (level, message) =>
        {
            var logLevel = level switch { "WARN" => LogLevel.Warn, "ERROR" => LogLevel.Error, _ => LogLevel.Info };
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                if (LogEntries.Count >= 500) LogEntries.RemoveAt(0);
                LogEntries.Add(LogEntry.Create(logLevel, message));
            });
        };

        AddListenerCommand = new RelayCommand(AddListener);
        ListenerEnvironments.CollectionChanged += (_, _) =>
            OnPropertyChanged(nameof(HasListenerEnvironments));
    }

    private void RegisterDevice(DeviceViewModelBase vm, Func<DeviceViewModelBase, UserControl> panelFactory)
    {
        DeviceList.Add(vm);
        if (vm is not RegisterInspectorViewModel)
            BuiltinDevices.Add(vm);
        AttachDeviceSimulationObserver(vm);
        AttachDeviceValueObserver(vm);
        RefreshListenerDeviceProtocolOptions();
        _panelFactories[vm.GetType()] = panelFactory;
        try   { _panelCache[vm] = panelFactory(vm); }
        catch (Exception ex) { AppLogger.Error($"[RegisterDevice] 面板创建失败：{vm.DeviceName} — {ex.Message}", ex); }
    }

    private void AttachDeviceSimulationObserver(DeviceViewModelBase vm)
    {
        if (_deviceSimHandlers.ContainsKey(vm))
            return;

        PropertyChangedEventHandler handler = (sender, e) =>
        {
            if (!string.Equals(e.PropertyName, nameof(DeviceViewModelBase.IsSimulating), StringComparison.Ordinal))
                return;

            if (_suppressSimGuard)
                return;

            if (IsInspectorBypassEnabled()
                && sender is DeviceViewModelBase changedVm
                && changedVm is not RegisterInspectorViewModel
                && changedVm.IsSimulating)
            {
                _suppressSimGuard = true;
                try
                {
                    changedVm.IsSimulating = false;
                    LogSys($"inspector-lock blocked-check device={changedVm.DeviceName}");
                }
                finally
                {
                    _suppressSimGuard = false;
                }
                return;
            }

            _ = Application.Current?.Dispatcher.InvokeAsync(async () =>
            {
                await ReconcileListenerActivationAsync();
            });
        };

        vm.PropertyChanged += handler;
        _deviceSimHandlers[vm] = handler;
    }

    private void AttachDeviceValueObserver(DeviceViewModelBase vm)
    {
        if (_deviceValueHandlers.ContainsKey(vm))
            return;

        PropertyChangedEventHandler handler = (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(DeviceViewModelBase.IsSimulating), StringComparison.Ordinal))
                return;

            if (!Listeners.Any(l => l.IsRunning && ReferenceEquals(ResolveBoundDevice(l), vm)))
                return;

            _ = Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                FlushDeviceToRegistersForListening(vm);
                PushRunningListenerSnapshotsForDevice(vm);
            });
        };

        vm.PropertyChanged += handler;
        _deviceValueHandlers[vm] = handler;
    }

    private void DetachDeviceSimulationObserver(DeviceViewModelBase vm)
    {
        if (!_deviceSimHandlers.TryGetValue(vm, out var handler))
            return;

        vm.PropertyChanged -= handler;
        _deviceSimHandlers.Remove(vm);
    }

    private void DetachDeviceValueObserver(DeviceViewModelBase vm)
    {
        if (!_deviceValueHandlers.TryGetValue(vm, out var handler))
            return;

        vm.PropertyChanged -= handler;
        _deviceValueHandlers.Remove(vm);
    }

    private void ClearAllDeviceSimulationSelections()
    {
        foreach (var vm in BuiltinDevices)
        {
            if (vm.IsSimulating)
                vm.IsSimulating = false;
        }

        foreach (var vm in ImportedDevices)
        {
            if (vm.IsSimulating)
                vm.IsSimulating = false;
        }
    }

    private async Task ReconcileListenerActivationAsync()
    {
        foreach (var listener in Listeners.ToList())
        {
            if (listener.IsRunning && !ShouldListenerBeActive(listener))
            {
                await StopListenerCoreAsync(listener);
            }
            else if (!listener.IsRunning && !ShouldListenerBeActive(listener))
            {
                listener.StatusText = GetListenerNotReadyReason(listener);
            }
        }
    }

    private void AddListenerConfig(SlaveListenerConfig listener, bool refreshProtocolOptions = true)
    {
        Listeners.Add(listener);
        AttachListenerConfigObserver(listener);
        if (refreshProtocolOptions)
            RefreshListenerDeviceProtocolOptions();
    }

    private void InsertListenerConfig(int index, SlaveListenerConfig listener, bool refreshProtocolOptions = true)
    {
        Listeners.Insert(index, listener);
        AttachListenerConfigObserver(listener);
        if (refreshProtocolOptions)
            RefreshListenerDeviceProtocolOptions();
    }

    private void RemoveListenerConfig(SlaveListenerConfig listener)
    {
        DetachListenerConfigObserver(listener);
        Listeners.Remove(listener);
    }

    private void ClearListenerConfigs()
    {
        foreach (var listener in Listeners.ToList())
            DetachListenerConfigObserver(listener);
        Listeners.Clear();
    }

    private void AttachListenerConfigObserver(SlaveListenerConfig listener)
    {
        if (_listenerConfigHandlers.ContainsKey(listener))
            return;

        PropertyChangedEventHandler handler = (sender, e) =>
        {
            if (_suppressListenerBindingRefresh || _isRefreshingListenerProtocolOptions)
                return;

            if (string.Equals(e.PropertyName, nameof(SlaveListenerConfig.BoundDeviceKey), StringComparison.Ordinal))
            {
                ClearSelectedListenerEnvironmentForManualChange();
                RefreshOtherListenerDeviceProtocolOptions(listener);
                return;
            }

            if (e.PropertyName is nameof(SlaveListenerConfig.Protocol)
                or nameof(SlaveListenerConfig.ListenAddress)
                or nameof(SlaveListenerConfig.Port)
                or nameof(SlaveListenerConfig.ComPort)
                or nameof(SlaveListenerConfig.BaudRate)
                or nameof(SlaveListenerConfig.SlaveId)
                or nameof(SlaveListenerConfig.FunctionCode)
                or nameof(SlaveListenerConfig.IsEnabled))
            {
                ClearSelectedListenerEnvironmentForManualChange();
            }
        };

        listener.PropertyChanged += handler;
        _listenerConfigHandlers[listener] = handler;
    }

    private void DetachListenerConfigObserver(SlaveListenerConfig listener)
    {
        if (!_listenerConfigHandlers.TryGetValue(listener, out var handler))
            return;

        listener.PropertyChanged -= handler;
        _listenerConfigHandlers.Remove(listener);
    }
    // ----------------------------------------------------------------
    // 命令：监听配置管理
    // ----------------------------------------------------------------

    public void AddListener()
    {
        var usedPorts = Listeners
            .Where(l => l != null && l.Port >= 1 && l.Port <= 65535)
            .Select(l => l.Port)
            .ToHashSet();
        int port = 502;
        while (usedPorts.Contains(port) && port < 65535)
            port++;

        if (port < 1 || port > 65535 || usedPorts.Contains(port))
        {
            AppLogger.Warn("新增监听失败：未找到可用端口");
            return;
        }

        var listener = new SlaveListenerConfig { Port = port, IsEnabled = true };
        AddListenerConfig(listener);
        ClearSelectedListenerEnvironmentForManualChange();
        LogSys($"listener-added count={Listeners.Count} port={port}");
        OnPropertyChanged(nameof(Listeners));
    }

    [RelayCommand]
    public void RemoveListener(SlaveListenerConfig config)
    {
        if (config.IsRunning) return;   // 运行中不可删除，按钮已在界面禁用
        RemoveListenerConfig(config);
        ClearSelectedListenerEnvironmentForManualChange();
        RefreshListenerDeviceProtocolOptions();
    }

    [RelayCommand]
    public async Task ClearListenersAsync()
    {
        if (!await ConfirmAndPauseRunningListenersAsync())
            return;

        ClearListenerConfigs();
        AddListenerConfig(CreateDefaultListener(), refreshProtocolOptions: false);
        RefreshListenerDeviceProtocolOptions();
        SetSelectedListenerEnvironmentSilently(null);
        OnPropertyChanged(nameof(Listeners));
        LogSys("listeners-cleared restored-default");
    }

    [RelayCommand]
    public async Task SaveListenerEnvironmentAsync()
    {
        if (!await EnsureSlaveDbConnectedAsync())
            return;

        var dialog = new SnapshotNameDialog(
            $"监听环境_{DateTime.Now:yyyyMMdd_HHmmss}",
            "保存监听环境",
            "请输入监听环境名称")
        {
            Owner = Application.Current?.MainWindow
        };
        if (dialog.ShowDialog() != true)
            return;

        var name = dialog.SnapshotName;
        var items = BuildListenerEnvironmentItems();

        try
        {
            var savedId = await _slaveDbService!.SaveListenerEnvironmentAsync(name, items);
            await LoadListenerEnvironmentsAsync(selectId: savedId);
            SlaveDbStatusText = $"监听环境已保存：{name}";
            LogSys($"listener-environment-saved id={savedId} name={name} items={items.Count}");
        }
        catch (Exception ex)
        {
            AppLogger.Error($"保存监听环境失败：{ex.Message}", ex);
            ThemedMessageBox.Show($"保存监听环境失败：\n{ex.Message}", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task ApplySelectedListenerEnvironmentAsync(
        SlaveListenerEnvironment? previousEnvironment,
        SlaveListenerEnvironment environment)
    {
        if (!await ConfirmAndPauseRunningListenersAsync())
        {
            SetSelectedListenerEnvironmentSilently(previousEnvironment);
            return;
        }

        _isApplyingListenerEnvironment = true;
        try
        {
            ClearListenerConfigs();
            var items = environment.Items.OrderBy(i => i.SortOrder).ToList();
            if (items.Count == 0)
            {
                AddListenerConfig(CreateDefaultListener(), refreshProtocolOptions: false);
            }
            else
            {
                foreach (var item in items)
                    AddListenerConfig(CreateListenerFromEnvironmentItem(item), refreshProtocolOptions: false);
            }

            RefreshListenerDeviceProtocolOptions();
            OnPropertyChanged(nameof(Listeners));
            SlaveDbStatusText = $"已切换监听环境：{environment.Name}";
            LogSys($"listener-environment-applied id={environment.Id} name={environment.Name} items={items.Count}");
        }
        finally
        {
            _isApplyingListenerEnvironment = false;
        }
    }

    [RelayCommand]
    public async Task DeleteListenerEnvironmentAsync(SlaveListenerEnvironment? environment)
    {
        if (environment == null)
            return;
        if (!VerifyPassword())
            return;
        if (!await ConfirmAndPauseRunningListenersAsync())
            return;
        if (!await EnsureSlaveDbConnectedAsync())
            return;

        try
        {
            await _slaveDbService!.DeleteListenerEnvironmentAsync(environment.Id);
            ListenerEnvironments.Remove(environment);
            if (ReferenceEquals(SelectedListenerEnvironment, environment))
                SetSelectedListenerEnvironmentSilently(ListenerEnvironments.FirstOrDefault());
            SlaveDbStatusText = $"监听环境已删除：{environment.Name}";
        }
        catch (Exception ex)
        {
            AppLogger.Error($"删除监听环境失败：{ex.Message}", ex);
            ThemedMessageBox.Show($"删除监听环境失败：\n{ex.Message}", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ----------------------------------------------------------------
    // 命令：单条启停（Toggle）
    // ----------------------------------------------------------------

    [RelayCommand]
    public async Task ToggleListenerAsync(SlaveListenerConfig config)
    {
        if (config.IsRunning) await StopListenerCoreAsync(config);
        else
        {
            // 手动点“启动”时，视为用户明确要启动该监听，自动置为启用。
            if (!config.IsEnabled)
                config.IsEnabled = true;

            EnsureListenerHasDefaultProtocolBinding(config);
            if (!ShouldListenerBeActive(config))
            {
                var activeForListener = GetActiveDevicesForListener(config);
                LogSys(
                    $"start-blocked listenerDbId={config.DbId} enabled={config.IsEnabled} " +
                    $"bound={config.BoundDeviceKey ?? "(none)"} activeForListener={activeForListener.Count}");
                config.StatusText = GetListenerNotReadyReason(config);
                return;
            }
            await StartListenerCoreAsync(config);
        }
    }

    // ----------------------------------------------------------------
    // 命令：全部启动 / 全部停止
    // ----------------------------------------------------------------

    [RelayCommand]
    public async Task StartAllListenersAsync()
    {
        var snapshot = Listeners.ToList();
        foreach (var cfg in snapshot)
        {
            if (!cfg.IsEnabled)
                cfg.IsEnabled = true;
            EnsureListenerHasDefaultProtocolBinding(cfg);
            bool shouldRun = ShouldListenerBeActive(cfg);
            if (shouldRun && !cfg.IsRunning)
                await StartListenerCoreAsync(cfg);
            else if (!shouldRun && cfg.IsRunning)
                await StopListenerCoreAsync(cfg);
        }
    }

    [RelayCommand]
    public async Task StopAllListenersAsync()
    {
        await StopAllRunningListenersAsync();
    }

    public async Task<bool> ConfirmAndPauseRunningListenersAsync()
    {
        if (!Listeners.Any(l => l.IsRunning))
            return true;

        var result = MessageBox.Show(
            Application.Current?.MainWindow,
            "当前设备运行中，操作会导致当前监听暂停。是否继续？",
            "监听运行中",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
            return false;

        await StopAllRunningListenersAsync();
        return true;
    }

    private async Task StopAllRunningListenersAsync()
    {
        foreach (var cfg in Listeners.Where(c => c.IsRunning).ToList())
            await StopListenerCoreAsync(cfg);
    }

    // ----------------------------------------------------------------
    // 核心启停逻辑
    // ----------------------------------------------------------------

    private async Task StartListenerCoreAsync(SlaveListenerConfig config)
    {
        if (config.IsRunning) return;
        try
        {
            if (!ShouldListenerBeActive(config))
            {
                config.StatusText = GetListenerNotReadyReason(config);
                return;
            }

            ISlaveService svc;
            if (config.Protocol == ProtocolType.Tcp)
            {
                var tcpSvc = _services.GetRequiredService<TcpSlaveService>();
                tcpSvc.ListenAddress = config.ListenAddress;
                tcpSvc.Port          = config.Port;
                tcpSvc.FunctionCode  = NormalizeListenerFunctionCode(config.FunctionCode);
                tcpSvc.RegisterAddressFilter = address => ListenerContainsAddress(config, address);
                svc = tcpSvc;
            }
            else
            {
                var rtuSvc = _services.GetRequiredService<RtuSlaveService>();
                rtuSvc.PortName  = config.ComPort;
                rtuSvc.BaudRate  = config.BaudRate;
                rtuSvc.FunctionCode = NormalizeListenerFunctionCode(config.FunctionCode);
                rtuSvc.RegisterAddressFilter = address => ListenerContainsAddress(config, address);
                svc = rtuSvc;
            }

            var handler = BuildRequestHandler(config);
            svc.OnRequest += handler;
            _requestHandlers[config] = handler;

            bool inspectorOnlyMode = IsInspectorSession(config);

            // 启动前刷新绑定设备的当前值，服务启动后再灌入该监听自己的 DataStore。
            if (!inspectorOnlyMode)
            {
                foreach (var vm in GetActiveDevicesForListener(config))
                    FlushDeviceToRegistersForListening(vm);
            }

            await svc.StartAsync(config.SlaveId);
            PushListenerSnapshot(config);
            config.Service    = svc;
            config.IsRunning  = true;
            config.StatusText = config.Protocol == ProtocolType.Tcp
                ? $"监听中  {config.ListenAddress}:{config.Port}"
                : $"监听中  {config.ComPort}@{config.BaudRate}";

            _runningCount++;
            OnPropertyChanged(nameof(IsRunning));

            _lastRequestAt[config] = DateTime.UtcNow;
            _idleMonitorLogAt[config] = DateTime.MinValue;
            _rtuNoTrafficWarnAt[config] = DateTime.MinValue;
            EnsureTcpConnectionMonitorRunning();

            if (config.Protocol == ProtocolType.Tcp)
            {
                if (!_tcpPeerSnapshots.ContainsKey(config))
                    _tcpPeerSnapshots[config] = new Dictionary<string, TcpState>(StringComparer.OrdinalIgnoreCase);
                _tcpNoClientLogAt[config] = DateTime.MinValue;
                LogSys($"TCP monitor enabled listen={config.ListenAddress}:{config.Port}");
            }

            // 第一条监听启动时开启模拟定时器
            if (_runningCount == 1)
            {
                _simTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                _simTimer.Tick += (_, _) =>
                {
                    foreach (var vm in GetActiveDevicesForRunningListeners())
                        vm.GenerateData();
                    PushRunningListenerSnapshots();
                };
                _simTimer.Start();
            }

            LogSys($"Listener started protocol={config.Protocol} slaveId={config.SlaveId}");
            if (config.Protocol == ProtocolType.Tcp)
            {
                LogSys($"TCP listen endpoint={config.ListenAddress}:{config.Port}");
                LogTcpListenerBindings(config);
            }
            else
                LogSys($"RTU listen port={config.ComPort}@{config.BaudRate}");
        }
        catch (Exception ex)
        {
            config.StatusText = $"启动失败：{ex.Message}";
            AppLogger.Error("监听启动失败", ex);
            ThemedMessageBox.Show($"监听启动失败：\n{ex.Message}", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task StopListenerCoreAsync(SlaveListenerConfig config)
    {
        if (!config.IsRunning || config.Service == null) return;
        if (_requestHandlers.TryGetValue(config, out var handler))
        {
            config.Service.OnRequest -= handler;
            _requestHandlers.Remove(config);
        }
        await config.Service.StopAsync();
        config.Service    = null;
        config.IsRunning  = false;
        config.StatusText = "已停止";
        _tcpPeerSnapshots.Remove(config);
        _tcpNoClientLogAt.Remove(config);
        _lastRequestAt.Remove(config);
        _idleMonitorLogAt.Remove(config);
        _rtuNoTrafficWarnAt.Remove(config);
        _deviceNoResponseWarnAt.Clear();
        _deviceLastRequestAt.Clear();
        MaybeStopTcpConnectionMonitor();

        _runningCount = Math.Max(0, _runningCount - 1);
        OnPropertyChanged(nameof(IsRunning));

        // 最后一条停止时关闭模拟定时器
        if (_runningCount == 0)
        {
            _simTimer?.Stop();
            _simTimer = null;
        }

        LogSys($"Listener stopped protocol={config.Protocol}");
    }

    // ----------------------------------------------------------------
    // 命令：刷新串口 / IP 列表
    // ----------------------------------------------------------------

    [RelayCommand]
    public void RefreshComPorts()
    {
        try
        {
            var ports = SerialPort.GetPortNames()
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();

            AvailableComPorts.Clear();
            foreach (var p in ports)
                AvailableComPorts.Add(p);

            foreach (var listener in Listeners.Where(l => l.Protocol == ProtocolType.Rtu))
            {
                if (ports.Count == 0)
                {
                    listener.ComPort = string.Empty;
                }
                else if (string.IsNullOrWhiteSpace(listener.ComPort)
                         || !ports.Contains(listener.ComPort, StringComparer.OrdinalIgnoreCase))
                {
                    listener.ComPort = ports[0];
                }
            }
        }
        catch (Exception ex)
        {
            AvailableComPorts.Clear();
            foreach (var listener in Listeners.Where(l => l.Protocol == ProtocolType.Rtu))
                listener.ComPort = string.Empty;
            AppLogger.Warn($"刷新串口列表失败：{ex.Message}");
        }
    }

    [RelayCommand]
    public void RefreshTcpAddresses()
    {
        AvailableTcpAddresses.Clear();
        AvailableTcpAddresses.Add("0.0.0.0");
        AvailableTcpAddresses.Add("127.0.0.1");
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                foreach (var addr in nic.GetIPProperties().UnicastAddresses)
                    if (addr.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        AvailableTcpAddresses.Add(addr.Address.ToString());
            }
        }
        catch (Exception ex) { AppLogger.Warn($"枚举本地 IP 失败：{ex.Message}"); }

        RefreshComPorts();
    }

    // ----------------------------------------------------------------
    // 命令：刷新 DB（连接并加载协议设备）
    // ----------------------------------------------------------------

    [RelayCommand]
    public async Task RefreshDbAsync()
    {
        try
        {
            if (!await ConfirmAndPauseRunningListenersAsync())
                return;
            SlaveDbStatusText = "连接中…";
            var svc = new SlaveProtocolDbService(DefaultDbCs);
            await svc.InitializeAsync();
            _slaveDbService    = svc;
            IsSlaveDbConnected = true;
            SlaveDbStatusText  = "数据库已连接";
            LogSys("从站协议 DB connected");
            await LoadProtocolDevicesFromDbAsync();
            await LoadListenerEnvironmentsAsync();
        }
        catch (Exception ex)
        {
            IsSlaveDbConnected = false;
            SlaveDbStatusText  = $"连接失败：{ex.Message}";
            AppLogger.Error($"从站协议 DB 连接失败：{ex.Message}", ex);
        }
    }

    private async Task LoadProtocolDevicesFromDbAsync()
    {
        if (_slaveDbService == null) return;
        try
        {
            // 先清空已有导入设备，避免重连时重复追加
            foreach (var vm in ImportedDevices.ToList())
            {
                DetachDeviceSimulationObserver(vm);
                DetachDeviceValueObserver(vm);
                _panelCache.Remove(vm);
                DeviceList.Remove(vm);
            }
            // 移除来自 DB 的监听配置
            var dbListeners = Listeners.Where(l => l.DbId > 0).ToList();
            foreach (var l in dbListeners)
            {
                if (l.IsRunning) await StopListenerCoreAsync(l);
                RemoveListenerConfig(l);
            }
            ImportedDevices.Clear();
            _importedDeviceConfigByDbId.Clear();
            if (SelectedDevice is ImportedDeviceViewModel)
                SelectedDevice = DeviceList.FirstOrDefault();
            OnPropertyChanged(nameof(HasImportedDevices));

            var configs = await _slaveDbService.GetAllDeviceConfigsAsync();
            foreach (var (cfg, rows, currentValues, verifiedValues) in configs)
            {
                if (cfg.Id > 0)
                    _importedDeviceConfigByDbId[cfg.Id] = CloneDeviceConfig(cfg, cfg.Id);
                await AddProtocolDeviceAsync(cfg, rows, addListener: false, saveToDb: false, currentValues: currentValues, verifiedValues: verifiedValues, selectAfterAdd: false);
            }

            if (configs.Count > 0)
                LogSys($"Loaded {configs.Count} protocol devices from DB");
        }
        catch (Exception ex)
        {
            AppLogger.Error($"加载协议设备失败：{ex.Message}", ex);
        }
    }

    private async Task LoadListenerEnvironmentsAsync(int selectId = 0)
    {
        if (_slaveDbService == null) return;

        try
        {
            var selectedId = selectId > 0 ? selectId : SelectedListenerEnvironment?.Id ?? 0;
            var environments = await _slaveDbService.GetListenerEnvironmentsAsync();

            ListenerEnvironments.Clear();
            foreach (var environment in environments)
                ListenerEnvironments.Add(environment);

            SetSelectedListenerEnvironmentSilently(selectedId > 0
                ? ListenerEnvironments.FirstOrDefault(e => e.Id == selectedId)
                : null);

            if (environments.Count > 0)
                LogSys($"Loaded {environments.Count} listener environments from DB");
        }
        catch (Exception ex)
        {
            AppLogger.Error($"加载监听环境失败：{ex.Message}", ex);
        }
    }

    private void SetSelectedListenerEnvironmentSilently(SlaveListenerEnvironment? environment)
    {
        _suppressListenerEnvironmentSelectionChanged = true;
        try
        {
            SelectedListenerEnvironment = environment;
        }
        finally
        {
            _suppressListenerEnvironmentSelectionChanged = false;
        }
    }

    private void ClearSelectedListenerEnvironmentForManualChange()
    {
        if (_isApplyingListenerEnvironment || SelectedListenerEnvironment == null)
            return;

        SetSelectedListenerEnvironmentSilently(null);
    }

    private async Task<bool> EnsureSlaveDbConnectedAsync()
    {
        if (_slaveDbService != null && IsSlaveDbConnected)
            return true;

        try
        {
            SlaveDbStatusText = "连接中…";
            var svc = new SlaveProtocolDbService(DefaultDbCs);
            await svc.InitializeAsync();
            _slaveDbService = svc;
            IsSlaveDbConnected = true;
            SlaveDbStatusText = "数据库已连接";
            await LoadListenerEnvironmentsAsync();
        }
        catch (Exception ex)
        {
            IsSlaveDbConnected = false;
            SlaveDbStatusText = $"连接失败：{ex.Message}";
            AppLogger.Error($"从站协议 DB 连接失败：{ex.Message}", ex);
        }

        return _slaveDbService != null && IsSlaveDbConnected;
    }

    /// <summary>创建协议导入设备 ViewModel，并根据参数决定是否添加监听端点和持久化</summary>
    private async Task<ImportedDeviceViewModel> AddProtocolDeviceAsync(
        SlaveDeviceConfig        config,
        IEnumerable<ProtocolRow> rows,
        bool                     addListener = true,
        bool                     saveToDb    = true,
        Dictionary<int, ushort>? currentValues = null,
        Dictionary<int, bool>?   verifiedValues = null,
        bool                     selectAfterAdd = true)
    {
        var rowList = rows.OrderBy(r => r.Address).ToList();
        var bank    = _services.GetRequiredService<RegisterBank>();
        var mapSvc  = _services.GetRequiredService<RegisterMapService>();
        var vm      = new ImportedDeviceViewModel(bank, mapSvc, config.Name, rowList) { DbId = config.Id };
        vm.IsSimulating = false;
        vm.DbService = _slaveDbService;
        vm.PasswordVerifier = VerifyPassword;
        vm.GetActivePeerValueForAddress = GetActiveImportedPeerValueForAddress;
        vm.RegisterValueChanged = () => PushRunningListenerSnapshotsForDevice(vm);
        AttachDeviceSimulationObserver(vm);
        AttachDeviceValueObserver(vm);
        if (currentValues != null) vm.RestoreCurrentValues(currentValues);
        if (verifiedValues != null) vm.RestoreVerifiedValues(verifiedValues);
        _panelCache[vm] = new ImportedDevicePanel { DataContext = vm };
        DeviceList.Add(vm);
        ImportedDevices.Add(vm);
        OnPropertyChanged(nameof(HasImportedDevices));
        RefreshListenerDeviceProtocolOptions();

        SlaveListenerConfig? listener = null;
        if (addListener)
        {
            listener = new SlaveListenerConfig
            {
                Protocol      = (ProtocolType)config.Protocol,
                ListenAddress = config.Host,
                Port          = config.Port,
                ComPort       = config.PortName,
                BaudRate      = config.BaudRate,
                SlaveId       = config.SlaveId,
                FunctionCode  = 3,
                DbId          = config.Id,
                IsEnabled     = true,
                BoundDeviceKey = config.Id > 0 ? BuildImportedDeviceKey(config.Id) : null,
            };
            AddListenerConfig(listener);
            RefreshDeviceProtocolOptions(listener);
        }

        if (saveToDb && _slaveDbService != null)
        {
            var savedId = await _slaveDbService.SaveDeviceConfigAsync(config, rowList);
            vm.DbId = savedId;
            if (listener != null) listener.DbId = savedId;
            if (savedId > 0)
                _importedDeviceConfigByDbId[savedId] = CloneDeviceConfig(config, savedId);
        }
        else if (config.Id > 0)
        {
            _importedDeviceConfigByDbId[config.Id] = CloneDeviceConfig(config, config.Id);
        }

        if (selectAfterAdd)
            SelectedDevice = vm;

        return vm;
    }

    [RelayCommand]
    public async Task PasteImportProtocolAsync()
    {
        var text = Clipboard.GetText();
        if (string.IsNullOrWhiteSpace(text))
        {
            ThemedMessageBox.Show(
                "剪贴板为空。\n请先在 Excel 协议文档中选中寄存器行（含标题行）并复制，再点此按钮。",
                "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            var rows = ExcelHelper.ParseProtocolRowsFromClipboard(text);
            if (rows.Count == 0)
            {
                ThemedMessageBox.Show(
                    "未解析到有效数据行。\n请确认复制了含「Addr」标题行的协议表格（格式：Addr | 中文名 | 英文名 | R/W | 范围 | 单位 | 备注）。",
                    "解析失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var config = new SlaveDeviceConfig { Name = "协议导入" };
            await AddProtocolDeviceAsync(config, rows, addListener: false);
            LogSys($"Imported protocol rows from clipboard count={rows.Count}");
        }
        catch (Exception ex)
        {
            AppLogger.Error($"粘贴协议导入失败：{ex.Message}", ex);
            ThemedMessageBox.Show($"粘贴协议导入失败：\n{ex.Message}", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    public async Task ImportProtocolExcelAsync()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Excel 文件|*.xlsx;*.xls",
            Title  = "选择协议文档 Excel（如 MPPT_Modbus_V1.0.xlsx）"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var (deviceName, rows) = ExcelHelper.ParseProtocolRowsFromFile(dlg.FileName);
            if (rows.Count == 0)
            {
                ThemedMessageBox.Show(
                    "文件中未找到有效协议数据行。\n请确认文件中存在「Addr | 中文名 | 英文名 | R/W | 范围 | 单位 | 备注」格式的寄存器表。",
                    "导入失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var config = new SlaveDeviceConfig { Name = deviceName };
            await AddProtocolDeviceAsync(config, rows, addListener: false);
            LogSys($"Imported protocol file device={deviceName} rows={rows.Count} file={dlg.FileName}");
        }
        catch (Exception ex)
        {
            AppLogger.Error($"协议 Excel 导入失败：{ex.Message}", ex);
            ThemedMessageBox.Show($"协议 Excel 导入失败：\n{ex.Message}", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ----------------------------------------------------------------
    // 命令：从对话框添加设备（dialog confirm 后由 SlavePanel code-behind 调用）
    // ----------------------------------------------------------------

    /// <summary>
    /// 根据「新建协议」对话框的寄存器内容创建协议导入设备。
    /// 方法为 async Task，调用方应 await 以确保 DbId 在返回前已赋值。
    /// </summary>
    public async Task AddDeviceFromDialogAsync(NewProtocolDialogViewModel dlgVm)
    {
        if (await DeviceNameExistsAsync(dlgVm.DeviceName))
            throw new InvalidOperationException("已有该设备名称。");

        var config = new SlaveDeviceConfig { Name = dlgVm.DeviceName };
        await AddProtocolDeviceAsync(config, dlgVm.GetProtocolRows(), addListener: false);
    }

    public async Task<bool> DeviceNameExistsAsync(string deviceName, int excludeId = 0)
    {
        var name = (deviceName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
            return false;

        return _slaveDbService is not null && await _slaveDbService.DeviceNameExistsAsync(name, excludeId);
    }

    public async Task SaveImportedDeviceSnapshotAsync(ImportedDeviceViewModel source, string snapshotName)
    {
        var name = (snapshotName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("快照名称不能为空。");

        if (_slaveDbService is null || !IsSlaveDbConnected)
            throw new InvalidOperationException("数据库未连接，无法保存快照。");

        if (source.Rows.All(r => r.IsPending))
            throw new InvalidOperationException("当前协议没有可保存的寄存器行。");

        if (await _slaveDbService.DeviceNameExistsAsync(name))
            throw new InvalidOperationException("快照名称已存在，请重新填写。");

        var rows = source.Rows
            .Where(r => !r.IsPending)
            .OrderBy(r => r.Address)
            .Select(r => (r.ChineseName, r.EnglishName, r.Address, r.ReadWrite, r.Range, r.Unit, r.Note))
            .ToList();
        var currentValues = new Dictionary<int, ushort>();
        var verifiedValues = new Dictionary<int, bool>();
        foreach (var row in source.Rows.Where(r => !r.IsPending))
        {
            currentValues[row.Address] = row.CurrentValueRaw;
            verifiedValues[row.Address] = row.IsVerified;
        }

        var config = new SlaveDeviceConfig { Name = name };
        var savedId = await _slaveDbService.SaveDeviceConfigAsync(config, rows);

        foreach (var kv in currentValues)
            await _slaveDbService.UpdateRowCurrentValueAsync(savedId, kv.Key, kv.Value);
        foreach (var kv in verifiedValues.Where(kv => kv.Value))
            await _slaveDbService.UpdateRowIsVerifiedAsync(savedId, kv.Key, true);

        config.Id = savedId;
        await AddProtocolDeviceAsync(
            config,
            rows,
            addListener: false,
            saveToDb: false,
            currentValues: currentValues,
            verifiedValues: verifiedValues);

        LogSys($"protocol-snapshot-saved sourceId={source.DbId} newId={savedId} name={name} rows={rows.Count}");
    }

    public async Task BeginEditImportedProtocolAsync(ImportedDeviceViewModel imported)
    {
        if (imported.DbId <= 0)
        {
            MessageBox.Show("该协议还没有保存到数据库，不能进行替换编辑。", "编辑协议", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var inspectorVm = InspectorList.OfType<RegisterInspectorViewModel>().FirstOrDefault();
        if (inspectorVm is null)
        {
            MessageBox.Show("未找到寄存器检视区域，无法编辑协议。", "编辑协议", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!await ConfirmAndPauseRunningListenersAsync())
            return;

        if (inspectorVm.Rows.Count > 0)
        {
            var result = MessageBox.Show(
                $"寄存器检视中已有 {inspectorVm.Rows.Count} 条地址内容。\n继续编辑“{imported.DeviceName}”会覆盖当前寄存器检视内容，是否继续？",
                "确认覆盖寄存器检视",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
                return;
        }

        SelectedDevice = inspectorVm;
        if (_panelCache.GetValueOrDefault(inspectorVm) is RegisterInspectorPanel panel)
            panel.LoadImportedDeviceForEdit(imported);
    }

    public async Task ReplaceImportedDeviceFromInspectorAsync(
        int dbId,
        NewProtocolDialogViewModel dlgVm,
        IReadOnlyDictionary<int, ushort> currentValues)
    {
        if (_slaveDbService is null)
            throw new InvalidOperationException("数据库未连接，无法替换协议。");

        var existingConfig = _importedDeviceConfigByDbId.TryGetValue(dbId, out var savedConfig)
            ? savedConfig
            : new SlaveDeviceConfig { Id = dbId };

        if (await DeviceNameExistsAsync(dlgVm.DeviceName, dbId))
            throw new InvalidOperationException("已有该设备名称。");

        var config = CloneDeviceConfig(existingConfig, dbId);
        config.Name = dlgVm.DeviceName;

        var rows = dlgVm.GetProtocolRows().ToList();
        var rowAddressSet = rows.Select(r => r.Address).ToHashSet();
        await _slaveDbService.SaveDeviceConfigAsync(config, rows);

        foreach (var kv in currentValues.Where(kv => rowAddressSet.Contains(kv.Key)))
            await _slaveDbService.UpdateRowCurrentValueAsync(dbId, kv.Key, kv.Value);

        var oldVm = ImportedDevices.FirstOrDefault(v => v.DbId == dbId);
        var wasSelected = ReferenceEquals(SelectedDevice, oldVm);
        var wasSimulating = oldVm?.IsSimulating == true;
        var importedIndex = oldVm is null ? -1 : ImportedDevices.IndexOf(oldVm);
        var deviceIndex = oldVm is null ? -1 : DeviceList.IndexOf(oldVm);

        if (oldVm is not null)
        {
            if (oldVm.IsSimulating)
                oldVm.IsSimulating = false;
            DetachDeviceSimulationObserver(oldVm);
            DetachDeviceValueObserver(oldVm);
            _panelCache.Remove(oldVm);
            DeviceList.Remove(oldVm);
            ImportedDevices.Remove(oldVm);
        }

        var restoredValues = currentValues
            .Where(kv => rowAddressSet.Contains(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value);
        var newVm = await AddProtocolDeviceAsync(
            config,
            rows,
            addListener: false,
            saveToDb: false,
            currentValues: restoredValues,
            selectAfterAdd: false);

        if (importedIndex >= 0 && importedIndex < ImportedDevices.Count - 1)
            ImportedDevices.Move(ImportedDevices.Count - 1, importedIndex);
        if (deviceIndex >= 0 && deviceIndex < DeviceList.Count - 1)
            DeviceList.Move(DeviceList.Count - 1, deviceIndex);

        _importedDeviceConfigByDbId[dbId] = CloneDeviceConfig(config, dbId);
        newVm.IsSimulating = wasSimulating;
        if (wasSelected)
            SelectedDevice = newVm;

        OnPropertyChanged(nameof(HasImportedDevices));
        RefreshListenerDeviceProtocolOptions();
        LogSys($"protocol-replaced id={dbId} name={config.Name} rows={rows.Count}");
    }

    // ----------------------------------------------------------------
    // 命令：删除导入协议设备（从列表和数据库）
    // ----------------------------------------------------------------

    private static bool VerifyPassword()
    {
        var dlg = new PasswordDialog { Owner = Application.Current.MainWindow };
        dlg.ShowDialog();
        return dlg.Confirmed;
    }

    [RelayCommand]
    public async Task RemoveImportedDeviceAsync(ImportedDeviceViewModel vm)
    {
        if (!VerifyPassword()) return;
        if (!await ConfirmAndPauseRunningListenersAsync())
            return;

        DetachDeviceSimulationObserver(vm);
        DetachDeviceValueObserver(vm);
        _panelCache.Remove(vm);
        DeviceList.Remove(vm);
        ImportedDevices.Remove(vm);
        OnPropertyChanged(nameof(HasImportedDevices));
        RefreshListenerDeviceProtocolOptions();
        if (SelectedDevice == vm)
            SelectedDevice = ImportedDevices.FirstOrDefault() ?? DeviceList.FirstOrDefault();

        // 同步移除关联的监听端点
        if (vm.DbId > 0)
        {
            _importedDeviceConfigByDbId.Remove(vm.DbId);
            var dbListener = Listeners.FirstOrDefault(l => l.DbId == vm.DbId);
            if (dbListener != null)
            {
                if (dbListener.IsRunning) await StopListenerCoreAsync(dbListener);
                RemoveListenerConfig(dbListener);
            }
        }

        if (_slaveDbService != null && vm.DbId > 0)
        {
            try { await _slaveDbService.DeleteDeviceConfigAsync(vm.DbId); }
            catch (Exception ex) { AppLogger.Warn($"从数据库删除协议设备失败：{ex.Message}"); }
        }
    }

    // ----------------------------------------------------------------
    // 命令：清空日志
    // ----------------------------------------------------------------

    [RelayCommand]
    public void ClearLog() => LogEntries.Clear();

    [RelayCommand]
    public void OpenPacketCapture()
    {
        if (!IsRunning)
        {
            ThemedMessageBox.Show("请先开启监听后再抓取报文。", "报文抓取",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_packetCaptureWindow == null)
        {
            _packetCaptureWindow = new ModbusPacketCaptureWindow
            {
                Owner = Application.Current?.MainWindow,
                DataContext = _packetCaptureVm
            };
            _packetCaptureWindow.Closed += (_, _) => _packetCaptureWindow = null;
        }

        _packetCaptureWindow.Show();
        _packetCaptureWindow.Activate();
        _packetCaptureVm.TryStart();
    }

    // ----------------------------------------------------------------
    // 请求计数回调
    // ----------------------------------------------------------------

    private Action<byte, int, int, string> BuildRequestHandler(SlaveListenerConfig listener)
        => (fc, addr, qty, source) => OnRequest(listener, fc, addr, qty, source);

    private void OnRequest(SlaveListenerConfig listener, byte fc, int addr, int qty, string source)
    {
        if (!ShouldHandleRequest(listener))
            return;

        var sourceText = string.IsNullOrWhiteSpace(source)
            ? (listener.Protocol == ProtocolType.Tcp
                ? $"{listener.ListenAddress}:{listener.Port}"
                : listener.ComPort)
            : source;

        if (!RequestMatchesActiveDevice(listener, addr, qty))
            return;

        PushListenerSnapshot(listener);
        RequestCount++;
        var nowUtc = DateTime.UtcNow;
        _lastRequestAt[listener] = nowUtc;
        MarkDeviceRequestActivity(listener, addr, qty, nowUtc);
        CaptureRequestFrames(listener, fc, addr, qty, sourceText);
    }

    private void CaptureRequestFrames(SlaveListenerConfig listener, byte fc, int addr, int qty, string source)
    {
        if (!_packetCaptureVm.IsCapturing || _packetCaptureVm.IsPaused)
            return;

        var now = DateTime.Now;
        var protocolText = listener.Protocol == ProtocolType.Tcp ? "TCP" : "RTU";
        _packetCaptureVm.Append(new ModbusPacketCaptureEntry(
            now,
            "Rx",
            source,
            BuildCaptureLine(now, "Rx", protocolText, source, BuildRxNumbers(listener, fc, addr, qty))));

        _packetCaptureVm.Append(new ModbusPacketCaptureEntry(
            now,
            "Tx",
            source,
            BuildCaptureLine(now, "Tx", protocolText, source, BuildTxNumbers(listener, fc, addr, qty))));
    }

    private static string BuildCaptureLine(DateTime timestamp, string direction, string protocol, string source, IEnumerable<int> numbers)
        => $"{timestamp:HH:mm:ss.fff}-{direction}: {protocol} src={source}  {string.Join(' ', numbers)}";

    private IEnumerable<int> BuildRxNumbers(SlaveListenerConfig listener, byte fc, int addr, int qty)
    {
        yield return listener.SlaveId;
        yield return fc;
        yield return addr;
        yield return qty;

        if (fc == 16 && qty > 0)
        {
            var safeQty = Math.Clamp(qty, 0, 123);
            yield return safeQty * 2;

            foreach (var value in ReadCaptureValues(listener, addr, safeQty))
                yield return value;
        }
    }

    private IEnumerable<int> BuildTxNumbers(SlaveListenerConfig listener, byte fc, int addr, int qty)
    {
        yield return listener.SlaveId;
        yield return fc;

        if (fc == 3 || fc == 4)
        {
            var safeQty = Math.Clamp(qty, 0, 125);
            yield return safeQty * 2;

            foreach (var value in ReadCaptureValues(listener, addr, safeQty))
                yield return value;
        }
        else
        {
            yield return addr;
            yield return qty;
        }
    }

    private ushort[] ReadCaptureValues(SlaveListenerConfig? listener, int addr, int qty)
    {
        try
        {
            if (listener?.Service is IRegisterSnapshotSlaveService snapshotService)
                return snapshotService.ReadSnapshotValues(addr, qty);

            return qty > 0 ? _bank.ReadRange(addr, qty) : [];
        }
        catch
        {
            return [];
        }
    }

    private void EnsureTcpConnectionMonitorRunning()
    {
        if (_tcpConnTimer != null) return;

        _tcpConnTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _tcpConnTimer.Tick += (_, _) => PollListenerDiagnostics();
        _tcpConnTimer.Start();
    }

    private void MaybeStopTcpConnectionMonitor()
    {
        if (_tcpConnTimer == null) return;
        if (Listeners.Any(l => l.IsRunning)) return;

        _tcpConnTimer.Stop();
        _tcpConnTimer = null;
        _tcpPeerSnapshots.Clear();
        _lastRequestAt.Clear();
        _idleMonitorLogAt.Clear();
        _tcpNoClientLogAt.Clear();
        _rtuNoTrafficWarnAt.Clear();
        _deviceNoResponseWarnAt.Clear();
        _deviceLastRequestAt.Clear();
    }

    private void PollListenerDiagnostics()
    {
        var runningListeners = Listeners
            .Where(l => l.IsRunning)
            .ToList();

        if (runningListeners.Count == 0)
        {
            MaybeStopTcpConnectionMonitor();
            return;
        }

        var now = DateTime.UtcNow;
        foreach (var listener in runningListeners)
        {
            if (!ShouldListenerBeActive(listener))
            {
                _ = Application.Current?.Dispatcher.InvokeAsync(async () =>
                {
                    if (listener.IsRunning)
                        await StopListenerCoreAsync(listener);
                });
                continue;
            }

            _lastRequestAt.TryGetValue(listener, out var lastReqAt);
            _idleMonitorLogAt.TryGetValue(listener, out var lastIdleLogAt);
            if (lastReqAt == DateTime.MinValue)
            {
                lastReqAt = now;
                _lastRequestAt[listener] = lastReqAt;
            }

            var protocolText = listener.Protocol == ProtocolType.Tcp ? "TCP" : "RTU";
            var endpoint = listener.Protocol == ProtocolType.Tcp
                ? $"{listener.ListenAddress}:{listener.Port}"
                : $"{listener.ComPort}@{listener.BaudRate}";
            var idleSec = Math.Max(0, (int)(now - lastReqAt).TotalSeconds);

            if (now - lastReqAt >= TimeSpan.FromSeconds(5)
                && now - lastIdleLogAt >= TimeSpan.FromSeconds(5))
            {
                LogSys($"{protocolText} monitor no-request idleSec={idleSec} listen={endpoint}");
                _idleMonitorLogAt[listener] = now;

                // RTU 专项告警：监听已启动但长期无帧，常见于端子接错(A/B)、串口参数不一致、或主站未发送
                if (listener.Protocol == ProtocolType.Rtu)
                {
                    _rtuNoTrafficWarnAt.TryGetValue(listener, out var lastWarnAt);
                    bool warnDue = now - lastWarnAt >= TimeSpan.FromSeconds(10);
                    bool idleLongEnough = now - lastReqAt >= TimeSpan.FromSeconds(8);
                    if (warnDue && idleLongEnough)
                    {
                        var availablePorts = SerialPort.GetPortNames();
                        var hasPort = availablePorts.Any(p => string.Equals(p, listener.ComPort, StringComparison.OrdinalIgnoreCase));
                        var portState = hasPort ? "present" : "missing";
                        AppLogger.Warn(
                            $"[RTU-ERR] no-frame-detected idleSec={idleSec} port={listener.ComPort}({portState}) baud={listener.BaudRate} " +
                            "possible-cause=wrong-terminals-or-serial-mismatch hint=check-A/B-wiring-and-baud-parity-stopbits");
                        _rtuNoTrafficWarnAt[listener] = now;
                    }
                }
            }

            // 设备级判定独立于监听级：一个设备有流量，不应掩盖其它设备的无响应。
            LogDeviceNoResponseWarnings(listener, endpoint, now);
        }

        var runningTcpListeners = Listeners
            .Where(l => l.IsRunning && l.Protocol == ProtocolType.Tcp)
            .ToList();

        if (runningTcpListeners.Count == 0)
        {
            return;
        }

        TcpConnectionInformation[] activeConnections;
        try
        {
            activeConnections = IPGlobalProperties.GetIPGlobalProperties()
                .GetActiveTcpConnections()
                .Where(c => c.State != TcpState.Closed)
                .ToArray();
        }
        catch
        {
            return;
        }

        foreach (var listener in runningTcpListeners)
        {
            if (!_tcpPeerSnapshots.TryGetValue(listener, out var snapshot))
            {
                snapshot = new Dictionary<string, TcpState>(StringComparer.OrdinalIgnoreCase);
                _tcpPeerSnapshots[listener] = snapshot;
            }

            var currentStates = activeConnections
                .Where(c => IsConnectionForListener(c, listener))
                .GroupBy(c => $"{c.RemoteEndPoint.Address}:{c.RemoteEndPoint.Port}", StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(x => (int)x.State).First().State,
                    StringComparer.OrdinalIgnoreCase);

            if (currentStates.Count == 0)
            {
                var nowUtc = DateTime.UtcNow;
                _tcpNoClientLogAt.TryGetValue(listener, out var lastAt);
                if (nowUtc - lastAt >= TimeSpan.FromSeconds(5))
                {
                    LogSys($"TCP monitor no-client listen={listener.ListenAddress}:{listener.Port}");
                    _tcpNoClientLogAt[listener] = nowUtc;
                }
            }
            else
            {
                _tcpNoClientLogAt[listener] = DateTime.UtcNow;
            }

            foreach (var kv in currentStates)
            {
                if (!snapshot.TryGetValue(kv.Key, out var prevState))
                {
                    LogSys($"TCP client connected src={kv.Key} state={kv.Value} listen={listener.ListenAddress}:{listener.Port}");
                    continue;
                }

                if (prevState != kv.Value)
                    LogSys($"TCP client state-change src={kv.Key} {prevState}->{kv.Value} listen={listener.ListenAddress}:{listener.Port}");
            }

            foreach (var peer in snapshot.Keys.Except(currentStates.Keys, StringComparer.OrdinalIgnoreCase).ToList())
                LogSys($"TCP client disconnected src={peer} listen={listener.ListenAddress}:{listener.Port}");

            snapshot.Clear();
            foreach (var kv in currentStates)
                snapshot[kv.Key] = kv.Value;
        }
    }

    private void LogDeviceNoResponseWarnings(
        SlaveListenerConfig listener,
        string endpoint,
        DateTime nowUtc)
    {
        if (IsInspectorSession(listener))
            return;

        var activeDevices = GetActiveDevicesForListener(listener);
        if (activeDevices.Count == 0)
            return;

        foreach (var vm in activeDevices)
        {
            var key = BuildDeviceActivityKey(listener, vm);
            if (!_deviceLastRequestAt.TryGetValue(key, out var lastReqAt))
            {
                _deviceLastRequestAt[key] = nowUtc;
                continue;
            }

            var idleSec = Math.Max(0, (int)(nowUtc - lastReqAt).TotalSeconds);
            if (idleSec < 5)
                continue;

            _deviceNoResponseWarnAt.TryGetValue(key, out var lastWarnAt);
            if (nowUtc - lastWarnAt < TimeSpan.FromSeconds(10))
                continue;

            AppLogger.Warn(
                $"[DEV-ERR] device={vm.DeviceName} protocol={listener.Protocol} listen={endpoint} " +
                $"no-response idleSec={idleSec} hint=check-master-query-and-link");
            _deviceNoResponseWarnAt[key] = nowUtc;
        }
    }

    private void MarkDeviceRequestActivity(SlaveListenerConfig listener, int addr, int qty, DateTime nowUtc)
    {
        if (IsInspectorSession(listener))
            return;

        int requestStart = addr;
        int requestEnd = qty <= 0 ? addr : addr + qty - 1;
        if (requestEnd < requestStart) requestEnd = requestStart;

        foreach (var vm in GetActiveDevicesForListener(listener))
        {
            if (!DeviceMatchesRequest(vm, requestStart, requestEnd))
                continue;

            var key = BuildDeviceActivityKey(listener, vm);
            _deviceLastRequestAt[key] = nowUtc;
        }
    }

    private static string BuildDeviceActivityKey(SlaveListenerConfig listener, DeviceViewModelBase vm)
        => $"{RuntimeHelpers.GetHashCode(listener)}|{RuntimeHelpers.GetHashCode(vm)}";

    private bool RequestMatchesActiveDevice(SlaveListenerConfig listener, int addr, int qty)
    {
        int requestStart = addr;
        int requestEnd = qty <= 0 ? addr : addr + qty - 1;
        if (requestEnd < requestStart) requestEnd = requestStart;

        bool inspectorMatch = IsInspectorBypassEnabled()
            && SelectedDevice is RegisterInspectorViewModel inspectorVm
            && InspectorMatchesRequest(inspectorVm, requestStart, requestEnd);

        // 寄存器检视模式下仅按检视地址命中，避免未勾选协议参与。
        if (IsInspectorSession(listener))
            return inspectorMatch;

        var activeDevices = GetActiveDevicesForListener(listener);
        if (activeDevices.Count == 0)
            return inspectorMatch;

        return inspectorMatch || activeDevices.Any(vm => DeviceMatchesRequest(vm, requestStart, requestEnd));
    }

    private bool ListenerContainsAddress(SlaveListenerConfig listener, int address)
    {
        if (IsInspectorSession(listener))
        {
            return SelectedDevice is RegisterInspectorViewModel inspectorVm
                   && InspectorMatchesRequest(inspectorVm, address, address);
        }

        return GetActiveDevicesForListener(listener)
            .Any(vm => DeviceMatchesRequest(vm, address, address));
    }
    private static bool InspectorMatchesRequest(RegisterInspectorViewModel inspectorVm, int requestStart, int requestEnd)
    {
        try
        {
            return inspectorVm.Rows.Any(r => r.Address >= requestStart && r.Address <= requestEnd);
        }
        catch
        {
            return false;
        }
    }

    private static bool DeviceMatchesRequest(DeviceViewModelBase vm, int requestStart, int requestEnd)
    {
        if (vm is ImportedDeviceViewModel imported)
        {
            return imported.Rows
                .Where(r => !r.IsPending)
                .Any(r => r.Address >= requestStart && r.Address <= requestEnd);
        }

        // 内置设备通过反射取内部 Model.BaseAddress，再按窗口匹配。
        // 失败时按不命中处理，避免未勾选设备被误算为命中。
        if (!TryGetBuiltinBaseAddress(vm, out int start))
            return false;

        const int builtinAddressWindow = 512;
        int end = start + builtinAddressWindow - 1;
        if (end < start) end = start;
        return requestEnd >= start && requestStart <= end;
    }

    private void FlushDeviceToRegistersForListening(DeviceViewModelBase vm)
    {
        try
        {
            if (vm is ImportedDeviceViewModel imported)
                imported.FlushCurrentValuesToRegisters();
            else
                vm.FlushToRegisters();
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"刷新监听设备寄存器失败：device={vm.DeviceName}, {ex.Message}");
        }
    }

    private void PushRunningListenerSnapshots()
    {
        foreach (var listener in Listeners.Where(l => l.IsRunning).ToList())
            PushListenerSnapshot(listener);
    }

    private void PushRunningListenerSnapshotsForDevice(DeviceViewModelBase vm)
    {
        FlushDeviceToRegistersForListening(vm);
        foreach (var listener in Listeners
                     .Where(l => l.IsRunning && ReferenceEquals(ResolveBoundDevice(l), vm))
                     .ToList())
        {
            PushListenerSnapshot(listener);
        }
    }

    private void PushListenerSnapshot(SlaveListenerConfig listener)
    {
        if (listener.Service is not IRegisterSnapshotSlaveService snapshotService)
            return;

        var values = BuildListenerRegisterSnapshot(listener);
        snapshotService.FunctionCode = NormalizeListenerFunctionCode(listener.FunctionCode);
        snapshotService.ReplaceSnapshotValues(values);
    }

    private IReadOnlyDictionary<int, ushort> BuildListenerRegisterSnapshot(SlaveListenerConfig listener)
    {
        var result = new Dictionary<int, ushort>();
        if (IsInspectorSession(listener))
            return result;

        foreach (var vm in GetActiveDevicesForListener(listener))
        {
            foreach (var (address, value) in EnumerateDeviceRegisterValues(vm))
                result[address] = value;
        }

        return result;
    }

    private IEnumerable<KeyValuePair<int, ushort>> EnumerateDeviceRegisterValues(DeviceViewModelBase vm)
    {
        if (vm is ImportedDeviceViewModel imported)
        {
            foreach (var row in imported.Rows.Where(r => !r.IsPending))
                yield return new KeyValuePair<int, ushort>(row.Address, row.CurrentValueRaw);
            yield break;
        }

        if (!TryGetBuiltinAddressRange(vm, out var start, out var count) || count <= 0)
            yield break;

        ushort[] values;
        try
        {
            values = _bank.ReadRange(start, count);
        }
        catch
        {
            yield break;
        }

        for (var i = 0; i < values.Length; i++)
            yield return new KeyValuePair<int, ushort>(start + i, values[i]);
    }

    private static bool TryGetBuiltinBaseAddress(DeviceViewModelBase vm, out int baseAddress)
    {
        if (TryGetBuiltinAddressRange(vm, out baseAddress, out _))
            return true;

        baseAddress = 0;
        return false;
    }

    private static bool TryGetBuiltinAddressRange(DeviceViewModelBase vm, out int baseAddress, out int registerCount)
    {
        baseAddress = 0;
        registerCount = 0;

        try
        {
            var modelProp = vm.GetType().GetProperty(
                "Model",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            var model = modelProp?.GetValue(vm);
            if (model == null)
                return false;

            var baseProp = model.GetType().GetProperty(
                "BaseAddress",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (baseProp?.GetValue(model) is int value)
            {
                baseAddress = value;
                var countProp = model.GetType().GetProperty(
                    "RegisterCount",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (countProp?.GetValue(model) is int count)
                    registerCount = count;
                return true;
            }
        }
        catch
        {
            // ignore
        }

        return false;
    }

    private bool IsInspectorSession(SlaveListenerConfig listener)
        => listener.DbId <= 0
           && IsInspectorBypassEnabled()
           && SelectedDevice is RegisterInspectorViewModel;

    private List<DeviceViewModelBase> GetActiveDevicesForListener(SlaveListenerConfig listener)
    {
        var device = ResolveBoundDevice(listener);
        return device is null ? [] : [device];
    }

    private List<DeviceViewModelBase> GetActiveDevicesForRunningListeners()
        => Listeners
            .Where(l => l.IsRunning && !IsInspectorSession(l))
            .SelectMany(GetActiveDevicesForListener)
            .Distinct()
            .ToList();

    private DeviceViewModelBase? ResolveBoundDevice(SlaveListenerConfig listener)
    {
        if (string.IsNullOrWhiteSpace(listener.BoundDeviceKey) && listener.DbId > 0)
            listener.BoundDeviceKey = BuildImportedDeviceKey(listener.DbId);

        var key = listener.BoundDeviceKey;
        if (string.IsNullOrWhiteSpace(key))
            return null;

        if (key.StartsWith(BuiltinDeviceKeyPrefix, StringComparison.Ordinal))
        {
            var indexText = key[BuiltinDeviceKeyPrefix.Length..];
            return int.TryParse(indexText, out var index) && index >= 0 && index < BuiltinDevices.Count
                ? BuiltinDevices[index]
                : null;
        }

        if (key.StartsWith(ImportedDeviceKeyPrefix, StringComparison.Ordinal))
        {
            var idText = key[ImportedDeviceKeyPrefix.Length..];
            return int.TryParse(idText, out var dbId)
                ? ImportedDevices.FirstOrDefault(v => v.DbId == dbId)
                : null;
        }

        return null;
    }

    private static string BuildBuiltinDeviceKey(int index) => $"{BuiltinDeviceKeyPrefix}{index}";
    private static string BuildImportedDeviceKey(int dbId) => $"{ImportedDeviceKeyPrefix}{dbId}";

    private void EnsureListenerHasDefaultProtocolBinding(SlaveListenerConfig listener)
    {
        if (IsInspectorBypassEnabled())
            return;

        if (!string.IsNullOrWhiteSpace(listener.BoundDeviceKey) && ResolveBoundDevice(listener) is not null)
            return;

        RefreshListenerDeviceProtocolOptions();
        var first = listener.AvailableDeviceProtocols.FirstOrDefault();
        if (first is not null)
            listener.BoundDeviceKey = first.Key;
    }

    private void RefreshListenerDeviceProtocolOptions()
    {
        if (_isRefreshingListenerProtocolOptions)
            return;

        _isRefreshingListenerProtocolOptions = true;
        try
        {
            var usedKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var listener in Listeners.ToList())
                RefreshDeviceProtocolOptions(listener, usedKeys);
        }
        finally
        {
            _isRefreshingListenerProtocolOptions = false;
        }
    }

    private void RefreshOtherListenerDeviceProtocolOptions(SlaveListenerConfig changedListener)
    {
        if (_isRefreshingListenerProtocolOptions)
            return;

        _isRefreshingListenerProtocolOptions = true;
        try
        {
            var usedKeys = new HashSet<string>(StringComparer.Ordinal);

            if (!string.IsNullOrWhiteSpace(changedListener.BoundDeviceKey))
                usedKeys.Add(changedListener.BoundDeviceKey);

            foreach (var listener in Listeners.Where(l => !ReferenceEquals(l, changedListener)).ToList())
                RefreshDeviceProtocolOptions(listener, usedKeys);
        }
        finally
        {
            _isRefreshingListenerProtocolOptions = false;
        }
    }

    private void RefreshDeviceProtocolOptions(SlaveListenerConfig listener, HashSet<string>? usedKeys = null)
    {
        var selectedKey = listener.BoundDeviceKey;
        var options = BuildDeviceProtocolOptions(listener, usedKeys);

        _suppressListenerBindingRefresh = true;
        try
        {
            listener.AvailableDeviceProtocols.Clear();
            foreach (var option in options)
                listener.AvailableDeviceProtocols.Add(option);

            if (IsInspectorBypassEnabled())
            {
                listener.BoundDeviceKey = selectedKey;
                return;
            }

            if (!string.IsNullOrWhiteSpace(selectedKey)
                && options.Any(o => o.Key == selectedKey))
            {
                listener.BoundDeviceKey = selectedKey;
                usedKeys?.Add(selectedKey);
                return;
            }

            listener.BoundDeviceKey = options.FirstOrDefault()?.Key;
            if (!string.IsNullOrWhiteSpace(listener.BoundDeviceKey))
                usedKeys?.Add(listener.BoundDeviceKey);
        }
        finally
        {
            _suppressListenerBindingRefresh = false;
        }
    }

    private List<DeviceProtocolOption> BuildDeviceProtocolOptions(
        SlaveListenerConfig listener,
        HashSet<string>? usedKeys = null)
    {
        if (IsInspectorBypassEnabled())
            return [];

        usedKeys ??= Listeners
            .Where(l => !ReferenceEquals(l, listener) && !string.IsNullOrWhiteSpace(l.BoundDeviceKey))
            .Select(l => l.BoundDeviceKey!)
            .ToHashSet(StringComparer.Ordinal);

        var options = new List<DeviceProtocolOption>();
        for (var i = 0; i < BuiltinDevices.Count; i++)
        {
            var key = BuildBuiltinDeviceKey(i);
            if (!usedKeys.Contains(key))
                options.Add(new DeviceProtocolOption(key, BuiltinDevices[i].DeviceName));
        }

        foreach (var imported in ImportedDevices.Where(v => v.DbId > 0))
        {
            var key = BuildImportedDeviceKey(imported.DbId);
            if (!usedKeys.Contains(key))
                options.Add(new DeviceProtocolOption(key, imported.DeviceName));
        }

        return options;
    }

    private ushort? GetActiveImportedPeerValueForAddress(ImportedDeviceViewModel owner, int address)
    {
        foreach (var vm in ImportedDevices)
        {
            if (ReferenceEquals(vm, owner) || !vm.IsSimulating)
                continue;

            var row = vm.Rows.FirstOrDefault(r => !r.IsPending && r.Address == address);
            if (row != null)
                return row.CurrentValueRaw;
        }

        return null;
    }

    private static bool IsConnectionForListener(TcpConnectionInformation c, SlaveListenerConfig listener)
    {
        if (c.LocalEndPoint.Port != listener.Port)
            return false;

        if (IsAnyListenAddress(listener.ListenAddress))
            return true;

        if (!IPAddress.TryParse(listener.ListenAddress, out var configuredIp))
            return true;

        return Equals(c.LocalEndPoint.Address, configuredIp);
    }

    private static bool IsAnyListenAddress(string? address)
        => string.IsNullOrWhiteSpace(address)
           || address == "0.0.0.0"
           || address == "::";

    private static void LogTcpListenerBindings(SlaveListenerConfig config)
    {
        try
        {
            var listeners = IPGlobalProperties.GetIPGlobalProperties()
                .GetActiveTcpListeners()
                .Where(ep => ep.Port == config.Port)
                .Select(ep => $"{ep.Address}:{ep.Port}")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var locals = listeners.Length == 0 ? "(none)" : string.Join(", ", listeners);
            LogSys($"TCP local-bindings port={config.Port} locals={locals}");
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"TCP local-bindings scan failed: {ex.Message}");
        }
    }

    private bool ShouldHandleRequest(SlaveListenerConfig listener)
    {
        if (ShouldListenerBeActive(listener))
            return true;

        if (listener.IsRunning)
        {
            _ = Application.Current?.Dispatcher.InvokeAsync(async () =>
            {
                if (listener.IsRunning)
                    await StopListenerCoreAsync(listener);
            });
        }

        return false;
    }

    private bool ShouldListenerBeActive(SlaveListenerConfig listener)
        => IsInspectorBypassEnabled() || ResolveBoundDevice(listener) is not null;

    private bool IsInspectorBypassEnabled()
        => SelectedDevice is RegisterInspectorViewModel;

    private string GetListenerNotReadyReason(SlaveListenerConfig listener)
    {
        if (IsInspectorBypassEnabled())
            return "寄存器检视模式可直接启动";
        return "请选择一个设备协议后再启动监听";
    }

    private async Task ApplyListenerProfileForSelectedDeviceAsync(DeviceViewModelBase? selectedDevice, int profileVersion)
    {
        if (profileVersion != _listenerProfileVersion)
            return;

        var desiredListeners = BuildDesiredListenersForSelectedDevice(selectedDevice);
        if (desiredListeners == null)
            return;

        foreach (var running in Listeners.Where(l => l.IsRunning).ToList())
            await StopListenerCoreAsync(running);

        if (profileVersion != _listenerProfileVersion)
            return;

        ClearListenerConfigs();
        foreach (var listener in desiredListeners)
            AddListenerConfig(listener, refreshProtocolOptions: false);
        RefreshListenerDeviceProtocolOptions();

        OnPropertyChanged(nameof(Listeners));
    }

    private List<SlaveListenerConfig>? BuildDesiredListenersForSelectedDevice(DeviceViewModelBase? selectedDevice)
    {
        if (selectedDevice is ImportedDeviceViewModel imported && imported.DbId > 0)
        {
            if (!_importedDeviceConfigByDbId.TryGetValue(imported.DbId, out var cfg))
                return null;

            return [CreateListenerFromDeviceConfig(cfg)];
        }

        // 设备列表 / 寄存器检视 / 未选中：都维持默认一条监听，由用户自行新增。
        return [CreateDefaultListener()];
    }

    private static SlaveListenerConfig CreateDefaultListener()
        => new()
        {
            IsEnabled = true
        };

    private List<SlaveListenerEnvironmentItem> BuildListenerEnvironmentItems()
        => Listeners
            .Select((listener, index) => new SlaveListenerEnvironmentItem
            {
                SortOrder = index,
                IsEnabled = listener.IsEnabled,
                BoundDeviceKey = listener.BoundDeviceKey,
                ListenerDbId = listener.DbId,
                Protocol = (int)listener.Protocol,
                ListenAddress = string.IsNullOrWhiteSpace(listener.ListenAddress) ? "0.0.0.0" : listener.ListenAddress,
                Port = listener.Port,
                ComPort = listener.ComPort ?? string.Empty,
                BaudRate = listener.BaudRate,
                SlaveId = listener.SlaveId,
                FunctionCode = NormalizeListenerFunctionCode(listener.FunctionCode)
            })
            .ToList();

    private static SlaveListenerConfig CreateListenerFromEnvironmentItem(SlaveListenerEnvironmentItem item)
        => new()
        {
            IsEnabled = item.IsEnabled,
            BoundDeviceKey = item.BoundDeviceKey,
            DbId = item.ListenerDbId,
            Protocol = Enum.IsDefined(typeof(ProtocolType), item.Protocol) ? (ProtocolType)item.Protocol : ProtocolType.Tcp,
            ListenAddress = string.IsNullOrWhiteSpace(item.ListenAddress) ? "0.0.0.0" : item.ListenAddress,
            Port = item.Port <= 0 ? 502 : item.Port,
            ComPort = item.ComPort ?? string.Empty,
            BaudRate = item.BaudRate <= 0 ? 9600 : item.BaudRate,
            SlaveId = item.SlaveId == 0 ? (byte)1 : item.SlaveId,
            FunctionCode = NormalizeListenerFunctionCode(item.FunctionCode)
        };

    private static SlaveListenerConfig CreateListenerFromDeviceConfig(SlaveDeviceConfig cfg)
        => new()
        {
            Protocol = (ProtocolType)cfg.Protocol,
            ListenAddress = cfg.Host,
            Port = cfg.Port,
            ComPort = cfg.PortName,
            BaudRate = cfg.BaudRate,
            SlaveId = cfg.SlaveId,
            FunctionCode = 3,
            DbId = cfg.Id,
            IsEnabled = true,
            BoundDeviceKey = cfg.Id > 0 ? BuildImportedDeviceKey(cfg.Id) : null
        };

    private static byte NormalizeListenerFunctionCode(int functionCode)
        => functionCode is 1 or 2 or 3 or 4 ? (byte)functionCode : (byte)3;

    private static SlaveDeviceConfig CloneDeviceConfig(SlaveDeviceConfig cfg, int id)
        => new()
        {
            Id = id,
            Name = cfg.Name,
            Protocol = cfg.Protocol,
            Host = cfg.Host,
            Port = cfg.Port,
            PortName = cfg.PortName,
            BaudRate = cfg.BaudRate,
            SlaveId = cfg.SlaveId
        };

    private SlaveListenerConfig EnsureBuiltinListenerExists()
    {
        var builtin = Listeners.FirstOrDefault(l => l.DbId <= 0);
        if (builtin != null) return builtin;

        builtin = new SlaveListenerConfig { IsEnabled = true };
        InsertListenerConfig(0, builtin);
        return builtin;
    }

    private bool HasAnyActiveSimulationDevice()
        => Listeners.Any(l => ResolveBoundDevice(l) is not null) || IsInspectorBypassEnabled();
}
