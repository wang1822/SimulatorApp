using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SimulatorApp.Shared.Logging;
using SimulatorApp.Shared.Services;
using SimulatorApp.Slave.Models;
using SimulatorApp.Slave.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Threading;

namespace SimulatorApp.Slave.ViewModels;

/// <summary>
/// 从协议文档导入的通用寄存器行数据 ViewModel。
/// 不绑定任何具体设备模型，仅作展示用。
/// </summary>
public partial class ImportedDeviceViewModel : DeviceViewModelBase
{
    // 空模型占位（不写寄存器）
    private sealed class NullModel : DeviceModelBase
    {
        public override string DeviceName  => "";
        public override int    BaseAddress => 0;
        public override void ToRegisters(RegisterBank bank)  { }
        public override void FromRegisters(RegisterBank bank) { }
    }

    private static int _counter = 0;

    private readonly NullModel _nullModel = new();
    protected override DeviceModelBase Model     => _nullModel;
    protected override void            SyncToModel() { }

    private string _deviceName = string.Empty;
    private string _editingDeviceName = string.Empty;
    private string _nameBeforeEdit = string.Empty;
    private bool _isEditingName;

    public override string DeviceName => _deviceName;

    public string EditingDeviceName
    {
        get => _editingDeviceName;
        set => SetProperty(ref _editingDeviceName, value);
    }

    public bool IsEditingName
    {
        get => _isEditingName;
        set => SetProperty(ref _isEditingName, value);
    }

    /// <summary>解析后的寄存器行，供面板 DataGrid 绑定</summary>
    public ObservableCollection<ImportedRegisterRow> Rows { get; } = new();

    /// <summary>过滤后的行集合（DataGrid 实际绑定源）</summary>
    public ICollectionView FilteredRows { get; private set; } = null!;

    /// <summary>搜索关键词（同时匹配中英文名）</summary>
    [ObservableProperty] private string _searchText = "";

    [RelayCommand]
    public void Search() => FilteredRows.Refresh();

    [RelayCommand]
    public void ClearSearch() { SearchText = string.Empty; FilteredRows.Refresh(); }

    // ── 随机生成 ───────────────────────────────────────────────────
    [ObservableProperty] private int _minValue = 0;
    [ObservableProperty] private int _maxValue = 65535;

    /// <summary>全选状态：true=全勾、false=全不勾、null=部分勾选</summary>
    public bool? IsAllChecked
    {
        get
        {
            var nonPending = Rows.Where(r => !r.IsPending).ToList();
            if (nonPending.Count == 0) return false;
            int checkedCount = nonPending.Count(r => r.IsChecked);
            if (checkedCount == 0) return false;
            if (checkedCount == nonPending.Count) return true;
            return null;
        }
        set
        {
            bool check = value ?? true;
            foreach (var row in Rows.Where(r => !r.IsPending))
                row.IsChecked = check;
            OnPropertyChanged(nameof(IsAllChecked));
        }
    }

    [RelayCommand]
    public void GenerateRandom()
    {
        int lo = Math.Clamp(Math.Min(MinValue, MaxValue), 0, 65535);
        int hi = Math.Clamp(Math.Max(MinValue, MaxValue), 0, 65535);
        foreach (var row in Rows)
        {
            if (row.IsPending || !row.IsChecked) continue;
            try { row.WriteValue((ushort)Random.Shared.Next(lo, hi + 1)); }
            catch { }
        }
    }


    private readonly DispatcherTimer _refreshTimer;

    public override bool IsImported => true;
    public int DbId { get; set; } = 0;

    /// <summary>DB 服务引用，由 SlaveViewModel 在创建后注入</summary>
    public ISlaveProtocolDbService? DbService { get; set; }

    /// <summary>密码验证委托，由 SlaveViewModel 注入（返回 true 表示通过）</summary>
    public Func<bool>? PasswordVerifier { get; set; }

    /// <summary>从协议文档格式（地址|中文名|英文名|读写|单位|描述）构建</summary>
    public ImportedDeviceViewModel(
        RegisterBank       bank,
        RegisterMapService mapSvc,
        string             deviceName,
        IEnumerable<(string ChineseName, string EnglishName, int Address, string ReadWrite, string Range, string Unit, string Note)> rows)
        : base(bank, mapSvc)
    {
        int n = System.Threading.Interlocked.Increment(ref _counter);
        SetDeviceName(string.IsNullOrWhiteSpace(deviceName) ? $"协议导入 #{n}" : deviceName.Trim());
        EditingDeviceName = DeviceName;

        foreach (var (chinese, english, addr, rw, range, unit, note) in rows)
            Rows.Add(MakeRow(chinese, english, addr, rw, range, unit, note));

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _refreshTimer.Tick += (_, _) =>
        {
            if (IsSimulating)
            {
                foreach (var row in Rows)
                    row.RefreshFromBank();
            }
        };
        _refreshTimer.Start();
        PropertyChanged += ImportedDeviceViewModel_PropertyChanged;

        FilteredRows = CollectionViewSource.GetDefaultView(Rows);
        FilteredRows.Filter = FilterRow;
    }

    /// <summary>工厂：统一创建行并注入三个 DB 回调</summary>
    private ImportedRegisterRow MakeRow(
        string chineseName, string englishName, int address,
        string readWrite, string range, string unit, string note)
    {
        int capturedAddr = address;
        return new ImportedRegisterRow(chineseName, englishName, address, readWrite, range, unit, note, _bank,
            onCommit: (addr, val) =>
            {
                // 未勾选时不参与发送：写入后立即清零该地址，保留行内值用于后续勾选再下发。
                if (!IsSimulating)
                {
                    try { _bank.Write(addr, 0); } catch { }
                }

                if (DbService != null && DbId > 0)
                    _ = DbService.UpdateRowCurrentValueAsync(DbId, addr, val);
            },
            onMetaCommit: (cn, en) =>
            {
                if (DbService != null && DbId > 0)
                    _ = DbService.UpdateRowMetadataAsync(DbId, capturedAddr, cn, en);
            },
            onCheckedChanged: () => OnPropertyChanged(nameof(IsAllChecked)));
    }

    public void BeginRename()
    {
        if (IsEditingName) return;
        _nameBeforeEdit = DeviceName;
        EditingDeviceName = DeviceName;
        IsEditingName = true;
    }

    public void CancelRename()
    {
        EditingDeviceName = _nameBeforeEdit.Length == 0 ? DeviceName : _nameBeforeEdit;
        IsEditingName = false;
    }

    public async System.Threading.Tasks.Task CommitRenameAsync()
    {
        if (!IsEditingName) return;

        var oldName = _nameBeforeEdit.Length == 0 ? DeviceName : _nameBeforeEdit;
        var newName = (EditingDeviceName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(newName))
        {
            EditingDeviceName = oldName;
            IsEditingName = false;
            return;
        }

        if (string.Equals(newName, DeviceName, StringComparison.Ordinal))
        {
            EditingDeviceName = DeviceName;
            IsEditingName = false;
            return;
        }

        SetDeviceName(newName);
        EditingDeviceName = newName;
        IsEditingName = false;

        if (DbService == null || DbId <= 0)
            return;

        try
        {
            await DbService.UpdateDeviceNameAsync(DbId, newName);
        }
        catch (Exception ex)
        {
            SetDeviceName(oldName);
            EditingDeviceName = oldName;
            AppLogger.Warn($"协议导入设备重命名保存失败：Id={DbId}, {ex.Message}");
        }
    }

    private void SetDeviceName(string value)
    {
        SetProperty(ref _deviceName, value, nameof(DeviceName));
    }

    public override void GenerateData() { }
    public override void ClearAlarms()  { }

    private void ImportedDeviceViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.Equals(e.PropertyName, nameof(IsSimulating), StringComparison.Ordinal))
            return;

        if (IsSimulating) FlushToRegisters();
        else              ClearRegistersInBank();
    }

    private void ClearRegistersInBank()
    {
        foreach (var row in Rows)
        {
            if (row.IsPending) continue;
            try { _bank.Write(row.Address, 0); }
            catch { }
        }
    }

    public override void FlushToRegisters()
    {
        if (!IsSimulating)
        {
            ClearRegistersInBank();
            return;
        }

        foreach (var row in Rows)
        {
            if (row.IsPending) continue;
            try { _bank.Write(row.Address, row.CurrentValueRaw); }
            catch { }
        }
    }

    /// <summary>将保存的当前值批量写入 RegisterBank（从 DB 加载后调用）</summary>
    public void RestoreCurrentValues(System.Collections.Generic.Dictionary<int, ushort> savedValues)
    {
        foreach (var row in Rows)
        {
            if (savedValues.TryGetValue(row.Address, out var v))
                try
                {
                    row.CurrentValueRaw = v;
                    if (IsSimulating) _bank.Write(row.Address, v);
                }
                catch { }
        }
    }

    /// <summary>删除一行（含密码验证）。返回 true 表示已删除，false 表示取消或密码错误。</summary>
    public bool TryDeleteRow(ImportedRegisterRow row)
    {
        if (PasswordVerifier != null && !PasswordVerifier()) return false;
        Rows.Remove(row);
        if (DbService != null && DbId > 0)
            _ = DbService.DeleteRowAsync(DbId, row.Address);
        OnPropertyChanged(nameof(IsAllChecked));
        return true;
    }

    /// <summary>新增一行并写入 DB（需 await 确保插入完成后用户才能编辑名称）</summary>
    public async System.Threading.Tasks.Task AddRowAsync(
        string chineseName, string englishName, int address,
        string readWrite, string range, string unit, string note)
    {
        var row = MakeRow(chineseName, englishName, address, readWrite, range, unit, note);
        Rows.Add(row);
        if (DbService != null && DbId > 0)
            await DbService.InsertRowAsync(DbId, Rows.Count - 1,
                chineseName, englishName, address, readWrite, range, unit, note);
    }

    private bool FilterRow(object item)
    {
        if (item is ImportedRegisterRow r && r.IsPending) return true;
        if (string.IsNullOrWhiteSpace(SearchText)) return true;
        if (item is not ImportedRegisterRow row) return false;
        return row.ChineseName.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
            || row.EnglishName.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>在末尾追加一个挂起的空行，等待用户内联填写地址和中文名。</summary>
    public void AddNewEmptyRow()
    {
        var row = new ImportedRegisterRow(string.Empty, string.Empty, 0, "R/W", string.Empty, string.Empty, string.Empty, _bank) { IsPending = true };
        Rows.Add(row);
    }

    /// <summary>
    /// 验证挂起行，通过后创建正式行、按地址排序插入并持久化到 DB。
    /// 返回 true 表示提交成功，false 表示验证未通过（行保持挂起状态）。
    /// </summary>
    public async System.Threading.Tasks.Task<bool> TryCommitPendingRowAsync(ImportedRegisterRow pendingRow)
    {
        if (!pendingRow.IsPending) return false;

        var addrText = pendingRow.AddressText.Trim();
        int address;
        if (addrText.StartsWith("0x", System.StringComparison.OrdinalIgnoreCase))
        {
            if (!int.TryParse(addrText[2..], System.Globalization.NumberStyles.HexNumber, null, out address))
                return false;
        }
        else if (!int.TryParse(addrText, out address))
            return false;

        if (address < 0 || address > 65535) return false;
        if (string.IsNullOrWhiteSpace(pendingRow.ChineseName)) return false;

        pendingRow.IsPending = false; // 防止 LostFocus 重入

        var committed = MakeRow(pendingRow.ChineseName, pendingRow.EnglishName, address,
                                pendingRow.ReadWrite, pendingRow.Range, pendingRow.Unit, pendingRow.Note);

        Rows.Remove(pendingRow);
        int insertIdx = 0;
        for (int i = Rows.Count - 1; i >= 0; i--)
        {
            if (!Rows[i].IsPending && Rows[i].Address <= address)
            {
                insertIdx = i + 1;
                break;
            }
        }
        Rows.Insert(insertIdx, committed);

        if (DbService != null && DbId > 0)
            await DbService.InsertRowAsync(DbId, insertIdx,
                committed.ChineseName, committed.EnglishName, address,
                committed.ReadWrite, committed.Range, committed.Unit, committed.Note);

        OnPropertyChanged(nameof(IsAllChecked));
        return true;
    }
}

/// <summary>
/// 显示模式：十进制 / 二进制
/// </summary>
public enum RegisterValueDisplayMode { Decimal, Binary }

/// <summary>单条导入寄存器行（支持读写当前值、内联编辑名称）</summary>
public sealed partial class ImportedRegisterRow : ObservableObject
{
    // ── 可编辑元数据 ───────────────────────────────────────────────
    [ObservableProperty] private bool   _isPending;
    [ObservableProperty] private string _addressText = "";

    [ObservableProperty] private string _chineseName = "";
    [ObservableProperty] private string _englishName = "";

    // 名称变更时触发 DB 持久化
    partial void OnChineseNameChanged(string value) { if (!IsPending) _onMetaCommit?.Invoke(value, EnglishName); }
    partial void OnEnglishNameChanged(string value) { if (!IsPending) _onMetaCommit?.Invoke(ChineseName, value); }

    // ── 只读元数据 ─────────────────────────────────────────────────
    public int    Address   { get; set; }
    public string ReadWrite { get; }
    public string Range     { get; }
    public string Unit      { get; }
    public string Note      { get; }

    // ── 当前值显示模式（右键切换）──────────────────────────────────
    [ObservableProperty]
    private RegisterValueDisplayMode _displayMode = RegisterValueDisplayMode.Decimal;

    partial void OnDisplayModeChanged(RegisterValueDisplayMode value)
    {
        OnPropertyChanged(nameof(IsDecimalMode));
        OnPropertyChanged(nameof(IsBinaryMode));
        OnPropertyChanged(nameof(CurrentValueDisplay));
    }

    public bool IsDecimalMode => DisplayMode == RegisterValueDisplayMode.Decimal;
    public bool IsBinaryMode  => DisplayMode == RegisterValueDisplayMode.Binary;

    // ── 当前寄存器原始值（定时从 RegisterBank 刷新）────────────────
    [ObservableProperty]
    private ushort _currentValueRaw;

    partial void OnCurrentValueRawChanged(ushort value)
        => OnPropertyChanged(nameof(CurrentValueDisplay));

    /// <summary>按显示模式格式化的当前值字符串</summary>
    public string CurrentValueDisplay => DisplayMode == RegisterValueDisplayMode.Binary
        ? Convert.ToString(_currentValueRaw, 2).PadLeft(16, '0')
        : _currentValueRaw.ToString();

    // ── 写入输入框文本（编辑时绑定）────────────────────────────────
    [ObservableProperty]
    private string _writeValueText = string.Empty;

    // ── 勾选状态（随机生成时使用）──────────────────────────────────
    [ObservableProperty] private bool _isChecked;

    partial void OnIsCheckedChanged(bool value) => _onCheckedChanged?.Invoke();

    // ── 回调 ───────────────────────────────────────────────────────
    private readonly RegisterBank                _bank;
    private readonly Action<int, ushort>?        _onCommit;
    private readonly Action<string, string>?     _onMetaCommit;
    private readonly Action?                     _onCheckedChanged;

    public ImportedRegisterRow(string chineseName, string englishName, int address,
                                string readWrite, string range, string unit, string note,
                                RegisterBank bank,
                                Action<int, ushort>?    onCommit        = null,
                                Action<string, string>? onMetaCommit    = null,
                                Action?                 onCheckedChanged = null)
    {
        // 直接赋字段，绕过 ObservableProperty setter，避免构造时触发回调
        _chineseName      = chineseName          ?? string.Empty;
        _englishName      = englishName          ?? string.Empty;
        Address           = address;
        ReadWrite         = readWrite            ?? string.Empty;
        Range             = range               ?? string.Empty;
        Unit              = unit                ?? string.Empty;
        Note              = note                ?? string.Empty;
        _bank             = bank;
        _onCommit         = onCommit;
        _onMetaCommit     = onMetaCommit;
        _onCheckedChanged = onCheckedChanged;   // 最后赋值，确保初始化不触发
    }

    /// <summary>将指定值写入 RegisterBank 并触发 DB 持久化（供随机生成调用）。</summary>
    public void WriteValue(ushort val)
    {
        _bank.Write(Address, val);
        CurrentValueRaw = val;          // 使用生成属性触发 PropertyChanged
        _onCommit?.Invoke(Address, val);
    }

    /// <summary>从 RegisterBank 刷新当前值（定时器调用）</summary>
    public void RefreshFromBank()
    {
        try { CurrentValueRaw = _bank.Read(Address); }
        catch { /* 地址越界时忽略 */ }
    }

    /// <summary>右键菜单切换显示模式命令（"dec" = 十进制，"bin" = 二进制）</summary>
    [RelayCommand]
    public void SetDisplayMode(string? key)
        => DisplayMode = key == "bin" ? RegisterValueDisplayMode.Binary : RegisterValueDisplayMode.Decimal;

    /// <summary>
    /// 将 WriteValueText 解析后写入 RegisterBank，并触发 DB 持久化。
    /// 返回 true 表示成功，false 表示解析失败。
    /// </summary>
    public bool TryCommitWrite()
    {
        var text = WriteValueText.Trim();
        if (string.IsNullOrEmpty(text)) return false;

        ushort val;
        if (DisplayMode == RegisterValueDisplayMode.Binary)
        {
            var cleaned = text.Replace(" ", "").Replace("_", "");
            try { val = Convert.ToUInt16(cleaned, 2); }
            catch { return false; }
        }
        else
        {
            if (!ushort.TryParse(text, out val)) return false;
        }

        _bank.Write(Address, val);
        CurrentValueRaw = val;
        _onCommit?.Invoke(Address, val);
        return true;
    }
}

