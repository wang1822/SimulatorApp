using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SimulatorApp.Master.Services;
using SimulatorApp.Shared.Logging;
using SimulatorApp.Shared.Services;
using SimulatorApp.Slave.Models;
using SimulatorApp.Slave.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Globalization;
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

    // API 比对（与主站一致：API + Token，匹配英文名/中文名后点亮绿点）
    [ObservableProperty] private string _apiUrl = "";
    [ObservableProperty] private string _apiAuthorization = "";
    [ObservableProperty] private string _verifyToleranceText = "0.5";
    [ObservableProperty] private string _verifyStatusText = "通过 0 个 / 未通过 0 个";
    [ObservableProperty] private int _verifyFailCount;

    private bool TryGetVerifyTolerance(out double tolerance)
    {
        var text = (VerifyToleranceText ?? string.Empty).Trim();
        if (text.StartsWith(".", StringComparison.Ordinal)) text = "0" + text;
        if (text.StartsWith("-.", StringComparison.Ordinal)) text = "-0" + text[1..];

        if (double.TryParse(text, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.CurrentCulture, out tolerance)
            || double.TryParse(text, System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture, out tolerance))
        {
            tolerance = Math.Abs(tolerance);
            return true;
        }

        tolerance = 0.5;
        return false;
    }

    [RelayCommand]
    public void Search() => FilteredRows.Refresh();

    [RelayCommand]
    public void ClearSearch() { SearchText = string.Empty; FilteredRows.Refresh(); }

    // ── 随机生成 ───────────────────────────────────────────────────
    [ObservableProperty] private int _minValue = 0;
    [ObservableProperty] private int _maxValue = 65535;
    [ObservableProperty] private bool _isRandomGenerating;

    public string RandomGenerateButtonText => IsRandomGenerating ? "⚄ 停止生成" : "⚄ 随机生成";

    partial void OnIsRandomGeneratingChanged(bool value) => OnPropertyChanged(nameof(RandomGenerateButtonText));

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
        if (IsRandomGenerating)
        {
            StopRandomGenerating();
            return;
        }

        StartRandomGenerating();
    }

    private void StartRandomGenerating()
    {
        IsRandomGenerating = true;
        GenerateRandomOnce();
        _randomGenerateTimer.Start();
    }

    private void StopRandomGenerating()
    {
        _randomGenerateTimer.Stop();
        IsRandomGenerating = false;
    }

    private void GenerateRandomOnce()
    {
        int lo = Math.Clamp(Math.Min(MinValue, MaxValue), 0, 65535);
        int hi = Math.Clamp(Math.Max(MinValue, MaxValue), 0, 65535);
        foreach (var row in Rows)
        {
            if (row.IsPending || !row.IsChecked) continue;
            try { row.WriteValue((ushort)Random.Shared.Next(lo, hi + 1)); }
            catch { }
        }
        RegisterValueChanged?.Invoke();
    }


    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _randomGenerateTimer;

    public override bool IsImported => true;
    public int DbId { get; set; } = 0;

    /// <summary>DB 服务引用，由 SlaveViewModel 在创建后注入</summary>
    public ISlaveProtocolDbService? DbService { get; set; }

    /// <summary>密码验证委托，由 SlaveViewModel 注入（返回 true 表示通过）</summary>
    public Func<bool>? PasswordVerifier { get; set; }

    public Func<ImportedDeviceViewModel, int, ushort?>? GetActivePeerValueForAddress { get; set; }
    public Action? RegisterValueChanged { get; set; }

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

        foreach (var (chinese, english, addr, rw, range, unit, note) in rows.OrderBy(r => r.Address))
            Rows.Add(MakeRow(chinese, english, addr, rw, range, unit, note));

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _refreshTimer.Tick += (_, _) =>
        {
            if (IsSimulating)
            {
                foreach (var row in Rows)
                {
                    if (TryGetActivePeerValue(row.Address, out _))
                        continue;

                    row.RefreshFromBank();
                }
            }
        };
        _refreshTimer.Start();
        _randomGenerateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _randomGenerateTimer.Tick += (_, _) => GenerateRandomOnce();
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
                if (DbService != null && DbId > 0)
                    _ = DbService.UpdateRowCurrentValueAsync(DbId, addr, val);

                RegisterValueChanged?.Invoke();
            },
            onMetaCommit: (cn, en) =>
            {
                if (DbService != null && DbId > 0)
                    _ = DbService.UpdateRowMetadataAsync(DbId, capturedAddr, cn, en);
            },
            onCheckedChanged: () => OnPropertyChanged(nameof(IsAllChecked)),
            onVerifyCommit: (addr, verified) =>
            {
                if (DbService != null && DbId > 0)
                    _ = DbService.UpdateRowIsVerifiedAsync(DbId, addr, verified);
            },
            rowResolver: ResolveRowByAddress);
    }

    private ImportedRegisterRow? ResolveRowByAddress(int address)
        => Rows.FirstOrDefault(r => !r.IsPending && r.Address == address);

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
            RestorePeerOrClearAddress(row.Address);
        }
    }

    private bool TryGetActivePeerValue(int address, out ushort value)
    {
        var peerValue = GetActivePeerValueForAddress?.Invoke(this, address);
        if (peerValue.HasValue)
        {
            value = peerValue.Value;
            return true;
        }

        value = 0;
        return false;
    }

    private void RestorePeerOrClearAddress(int address)
    {
        try
        {
            _bank.Write(address, TryGetActivePeerValue(address, out var peerValue) ? peerValue : (ushort)0);
        }
        catch { }
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

    public void FlushCurrentValuesToRegisters()
    {
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
        RegisterValueChanged?.Invoke();
    }

    public void RestoreVerifiedValues(System.Collections.Generic.Dictionary<int, bool> savedValues)
    {
        foreach (var row in Rows)
        {
            if (savedValues.TryGetValue(row.Address, out var v))
                row.RestoreIsVerified(v);
        }
        RefreshVerifySummary();
    }

    [RelayCommand]
    public void ToggleVerifyRow(ImportedRegisterRow? row)
    {
        if (row == null || row.IsPending) return;
        row.IsVerified = !row.IsVerified;
        RefreshVerifySummary();
    }

    [RelayCommand]
    public async System.Threading.Tasks.Task VerifyOnceAsync()
    {
        if (string.IsNullOrWhiteSpace(ApiUrl))
        {
            RefreshVerifySummary();
            AppLogger.Warn("从站协议导入 API 比对失败：未填写 API 地址");
            return;
        }

        await RunVerifyAsync(System.Threading.CancellationToken.None);
    }

    private async System.Threading.Tasks.Task RunVerifyAsync(System.Threading.CancellationToken ct)
    {
        try
        {
            if (!TryGetVerifyTolerance(out var tolerance))
            {
                RefreshVerifySummary();
                AppLogger.Warn($"从站协议导入 API 比对失败：device={DeviceName}, 误差格式无效 value={VerifyToleranceText}");
                return;
            }

            var apiData = await ApiVerifyService.FetchNumericFieldsAsync(ApiUrl, ApiAuthorization, ct);
            var snapshot = Rows.Where(r => !r.IsPending)
                               .Select(r => (row: r, cn: r.ChineseName, en: r.EnglishName, value: (double)r.CurrentValueRaw))
                               .ToList();

            int newlyMatched = 0;
            foreach (var item in snapshot)
            {
                bool matched = ApiVerifyService.TryMatch(apiData, item.en, out double apiVal)
                            || ApiVerifyService.TryMatch(apiData, item.cn, out apiVal);
                bool ok = matched && Math.Abs(apiVal - item.value) <= tolerance;
                if (ok && !item.row.IsVerified)
                {
                    item.row.IsVerified = true;
                    newlyMatched++;
                }
            }

            RefreshVerifySummary();
            AppLogger.Info($"从站协议导入 API 比对：device={DeviceName}, 本次新增={newlyMatched}, 误差≤{tolerance}, {VerifyStatusText}");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            RefreshVerifySummary();
            AppLogger.Warn($"从站协议导入 API 比对失败：device={DeviceName}, {ex.Message}");
        }
    }

    private void RefreshVerifySummary()
    {
        int totalRows = Rows.Count(r => !r.IsPending);
        int totalVerified = Rows.Count(r => !r.IsPending && r.IsVerified);
        VerifyFailCount = totalRows - totalVerified;
        VerifyStatusText = $"通过 {totalVerified} 个 / 未通过 {VerifyFailCount} 个";
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
        int insertIdx = GetSortedInsertIndex(address);
        Rows.Insert(insertIdx, row);
        if (DbService != null && DbId > 0)
            await DbService.InsertRowAsync(DbId, insertIdx,
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
        int insertIdx = GetSortedInsertIndex(address);
        Rows.Insert(insertIdx, committed);

        if (DbService != null && DbId > 0)
            await DbService.InsertRowAsync(DbId, insertIdx,
                committed.ChineseName, committed.EnglishName, address,
                committed.ReadWrite, committed.Range, committed.Unit, committed.Note);

        OnPropertyChanged(nameof(IsAllChecked));
        return true;
    }

    private int GetSortedInsertIndex(int address)
    {
        int insertIdx = 0;
        for (int i = Rows.Count - 1; i >= 0; i--)
        {
            if (Rows[i].IsPending) continue;
            if (Rows[i].Address <= address)
            {
                insertIdx = i + 1;
                break;
            }
        }

        return insertIdx;
    }
}

/// <summary>
/// 显示模式：无符号十进制 / 有符号十进制 / 二进制 / 十六进制 / 字符串
/// </summary>
public enum RegisterValueDisplayMode { UnsignedDecimal, SignedDecimal, Binary, Hexadecimal, String, FloatABCD }

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
    private RegisterValueDisplayMode _displayMode = RegisterValueDisplayMode.UnsignedDecimal;

    partial void OnDisplayModeChanged(RegisterValueDisplayMode value)
    {
        OnPropertyChanged(nameof(IsUnsignedDecimalMode));
        OnPropertyChanged(nameof(IsSignedDecimalMode));
        OnPropertyChanged(nameof(IsBinaryMode));
        OnPropertyChanged(nameof(IsHexMode));
        OnPropertyChanged(nameof(IsStringMode));
        OnPropertyChanged(nameof(IsFloatABCDMode));
        OnPropertyChanged(nameof(CurrentValueDisplay));
        NotifyFloatPeerStateChanged();
    }

    public bool IsUnsignedDecimalMode => DisplayMode == RegisterValueDisplayMode.UnsignedDecimal;
    public bool IsSignedDecimalMode   => DisplayMode == RegisterValueDisplayMode.SignedDecimal;
    public bool IsBinaryMode          => DisplayMode == RegisterValueDisplayMode.Binary;
    public bool IsHexMode             => DisplayMode == RegisterValueDisplayMode.Hexadecimal;
    public bool IsStringMode          => DisplayMode == RegisterValueDisplayMode.String;
    public bool IsFloatABCDMode       => DisplayMode == RegisterValueDisplayMode.FloatABCD;

    // ── 当前寄存器原始值（定时从 RegisterBank 刷新）────────────────
    [ObservableProperty]
    private ushort _currentValueRaw;

    partial void OnCurrentValueRawChanged(ushort value)
    {
        OnPropertyChanged(nameof(CurrentValueDisplay));
        _rowResolver?.Invoke(Address - 1)?.OnPropertyChanged(nameof(CurrentValueDisplay));
    }

    /// <summary>按显示模式格式化的当前值字符串</summary>
    public string CurrentValueDisplay => DisplayMode switch
    {
        RegisterValueDisplayMode.SignedDecimal => unchecked((short)CurrentValueRaw).ToString(),
        RegisterValueDisplayMode.Binary        => Convert.ToString(CurrentValueRaw, 2).PadLeft(16, '0'),
        RegisterValueDisplayMode.Hexadecimal   => $"0x{CurrentValueRaw:X4}",
        RegisterValueDisplayMode.String        => FormatRegisterString(CurrentValueRaw),
        RegisterValueDisplayMode.FloatABCD     => FormatFloatABCD(),
        _ => CurrentValueRaw.ToString()
    };

    // ── 写入输入框文本（编辑时绑定）────────────────────────────────
    private static string FormatRegisterString(ushort value)
    {
        char hi = (char)(value >> 8);
        char lo = (char)(value & 0xFF);
        if (lo == '\0') return hi == '\0' ? string.Empty : hi.ToString();
        return new string(new[] { hi, lo });
    }

    private string FormatFloatABCD()
    {
        var next = _rowResolver?.Invoke(Address + 1);
        if (next == null) return string.Empty;

        var bytes = new[]
        {
            (byte)(CurrentValueRaw >> 8),
            (byte)(CurrentValueRaw & 0xFF),
            (byte)(next.CurrentValueRaw >> 8),
            (byte)(next.CurrentValueRaw & 0xFF)
        };
        if (BitConverter.IsLittleEndian)
            Array.Reverse(bytes);
        return BitConverter.ToSingle(bytes, 0).ToString("G9", CultureInfo.InvariantCulture);
    }

    public bool IsFloatSecondWord => _rowResolver?.Invoke(Address - 1)?.DisplayMode == RegisterValueDisplayMode.FloatABCD;
    public bool CanWriteCurrentValue => !IsPending && !IsFloatSecondWord;
    public string CurrentValueToolTip => IsFloatSecondWord
        ? "上一地址为 Float AB CD，本地址作为低 16 位，禁止单独写入"
        : "双击编辑写入；右键切换显示模式";

    private void NotifyFloatPeerStateChanged()
    {
        OnPropertyChanged(nameof(CanWriteCurrentValue));
        OnPropertyChanged(nameof(CurrentValueToolTip));
        _rowResolver?.Invoke(Address + 1)?.NotifyFloatSecondWordStateChanged();
    }

    private void NotifyFloatSecondWordStateChanged()
    {
        OnPropertyChanged(nameof(IsFloatSecondWord));
        OnPropertyChanged(nameof(CanWriteCurrentValue));
        OnPropertyChanged(nameof(CurrentValueToolTip));
    }

    [ObservableProperty]
    private string _writeValueText = string.Empty;

    // ── 勾选状态（随机生成时使用）──────────────────────────────────
    [ObservableProperty] private bool _isChecked;
    [ObservableProperty] private bool _isVerified;

    private bool _suppressVerifyCommit;

    partial void OnIsCheckedChanged(bool value) => _onCheckedChanged?.Invoke();
    partial void OnIsVerifiedChanged(bool value)
    {
        if (!_suppressVerifyCommit && !IsPending) _onVerifyCommit?.Invoke(Address, value);
    }

    // ── 回调 ───────────────────────────────────────────────────────
    private readonly RegisterBank                _bank;
    private readonly Action<int, ushort>?        _onCommit;
    private readonly Action<string, string>?     _onMetaCommit;
    private readonly Action?                     _onCheckedChanged;
    private readonly Action<int, bool>?          _onVerifyCommit;
    private readonly Func<int, ImportedRegisterRow?>? _rowResolver;

    public ImportedRegisterRow(string chineseName, string englishName, int address,
                                string readWrite, string range, string unit, string note,
                                RegisterBank bank,
                                Action<int, ushort>?    onCommit        = null,
                                Action<string, string>? onMetaCommit    = null,
                                Action?                 onCheckedChanged = null,
                                Action<int, bool>?      onVerifyCommit  = null,
                                Func<int, ImportedRegisterRow?>? rowResolver = null)
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
        _onCheckedChanged = onCheckedChanged;
        _onVerifyCommit   = onVerifyCommit;     // 最后赋值，确保初始化不触发
        _rowResolver      = rowResolver;
    }

    public void RestoreIsVerified(bool value)
    {
        if (IsVerified == value) return;
        _suppressVerifyCommit = true;
        try { IsVerified = value; }
        finally { _suppressVerifyCommit = false; }
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

    /// <summary>右键菜单切换显示模式命令。</summary>
    [RelayCommand]
    public void SetDisplayMode(string? key)
    {
        if (IsFloatSecondWord)
            return;

        if (string.Equals(key, "float", StringComparison.Ordinal) && _rowResolver?.Invoke(Address + 1) == null)
            return;

        DisplayMode = key switch
        {
            "sdec" => RegisterValueDisplayMode.SignedDecimal,
            "bin" => RegisterValueDisplayMode.Binary,
            "hex" => RegisterValueDisplayMode.Hexadecimal,
            "str" => RegisterValueDisplayMode.String,
            "float" => RegisterValueDisplayMode.FloatABCD,
            _ => RegisterValueDisplayMode.UnsignedDecimal
        };

        if (DisplayMode == RegisterValueDisplayMode.FloatABCD)
        {
            var next = _rowResolver?.Invoke(Address + 1);
            if (next != null && next.DisplayMode != RegisterValueDisplayMode.UnsignedDecimal)
                next.DisplayMode = RegisterValueDisplayMode.UnsignedDecimal;
        }
    }

    /// <summary>
    /// 将 WriteValueText 解析后写入 RegisterBank，并触发 DB 持久化。
    /// 返回 true 表示成功，false 表示解析失败。
    /// </summary>
    public bool TryCommitWrite()
    {
        var rawText = WriteValueText ?? string.Empty;
        var text = rawText.Trim();

        if (IsFloatSecondWord)
            return false;

        ushort val;
        if (DisplayMode == RegisterValueDisplayMode.FloatABCD)
        {
            if (string.IsNullOrEmpty(text)) return false;
            var next = _rowResolver?.Invoke(Address + 1);
            if (next == null) return false;
            if (!float.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var floatValue)
                && !float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out floatValue))
                return false;

            var bytes = BitConverter.GetBytes(floatValue);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(bytes);

            val = (ushort)((bytes[0] << 8) | bytes[1]);
            var nextVal = (ushort)((bytes[2] << 8) | bytes[3]);
            WriteValue(val);
            next.WriteValue(nextVal);
            return true;
        }
        if (DisplayMode == RegisterValueDisplayMode.String)
        {
            if (rawText.Length > 2) return false;
            if (rawText.Any(c => c > 0xFF)) return false;
            byte hi = rawText.Length > 0 ? (byte)rawText[0] : (byte)0;
            byte lo = rawText.Length > 1 ? (byte)rawText[1] : (byte)0;
            val = (ushort)((hi << 8) | lo);
        }
        else if (string.IsNullOrEmpty(text)) return false;
        else
        {
            var cleaned = text.Replace(" ", "").Replace("_", "");

            if (cleaned.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
        {
            try { val = Convert.ToUInt16(cleaned[2..], 2); }
            catch { return false; }
        }
        else if (cleaned.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (!ushort.TryParse(cleaned[2..], System.Globalization.NumberStyles.HexNumber, null, out val)) return false;
        }
        else if (DisplayMode == RegisterValueDisplayMode.Binary)
        {
            try { val = Convert.ToUInt16(cleaned, 2); }
            catch { return false; }
        }
        else if (DisplayMode == RegisterValueDisplayMode.Hexadecimal)
        {
            if (!ushort.TryParse(cleaned, System.Globalization.NumberStyles.HexNumber, null, out val)) return false;
        }
        else if (DisplayMode == RegisterValueDisplayMode.SignedDecimal)
        {
            if (!short.TryParse(text, out var signedVal)) return false;
            val = unchecked((ushort)signedVal);
        }
        else
        {
            if (!ushort.TryParse(text, out val)) return false;
        }

        }

        _bank.Write(Address, val);
        CurrentValueRaw = val;
        _onCommit?.Invoke(Address, val);
        return true;
    }
}

