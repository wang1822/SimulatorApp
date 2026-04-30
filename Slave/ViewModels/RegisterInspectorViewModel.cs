using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SimulatorApp.Shared.Logging;
using SimulatorApp.Shared.Services;
using SimulatorApp.Slave.Models;
using SimulatorApp.Slave.Services;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace SimulatorApp.Slave.ViewModels;

/// <summary>
/// 寄存器检视器 ViewModel。
/// 允许用户手动输入任意地址，实时读写 RegisterBank 中的寄存器值，
/// 功能类似 Modbus Slave 工具的寄存器数据表。
/// </summary>
public partial class RegisterInspectorViewModel : DeviceViewModelBase
{
    private const int MaxRegisterAddress = 65535;
    private const int MaxBatchCount = 65535;

    // ---- DeviceViewModelBase 抽象成员（检视器不绑定固定设备 Model）----
    private sealed class NullModel : DeviceModelBase
    {
        public override string DeviceName  => "寄存器检视";
        public override int    BaseAddress => 0;
        public override void ToRegisters(RegisterBank bank)   { }
        public override void FromRegisters(RegisterBank bank) { }
    }
    private static readonly NullModel _nullModel = new();
    public override string      DeviceName => "寄存器检视";
    protected override DeviceModelBase Model => _nullModel;
    protected override void SyncToModel() { } // 直接写 bank，不走 Model

    // ---- 寄存器行集合 ----
    public BulkObservableCollection<InspectorRow> Rows { get; } = new();

    // ---- 添加单行用的地址输入 ----
    [ObservableProperty] private int _newAddress = 0;

    // ---- 批量加载参数 ----
    [ObservableProperty] private int _batchStart = 0;
    [ObservableProperty] private int _batchCount = 16;

    public RegisterInspectorViewModel(RegisterBank bank, RegisterMapService mapService)
        : base(bank, mapService) { }

    // ----------------------------------------------------------------
    // 命令：添加单行
    // ----------------------------------------------------------------

    [RelayCommand]
    public void AddRow()
    {
        if (NewAddress is < 0 or > MaxRegisterAddress) return;
        if (Rows.Any(r => r.Address == NewAddress))
        {
            AppLogger.Warn($"寄存器检视：地址 {NewAddress} 已存在");
            return;
        }
        var row = CreateRow(NewAddress);
        Rows.Add(row);
        NewAddress = Math.Min(NewAddress + 1, MaxRegisterAddress);
    }

    // ----------------------------------------------------------------
    // 命令：批量加载（起始地址 + 数量）
    // ----------------------------------------------------------------

    [RelayCommand]
    public void LoadBatch()
    {
        int start = Math.Clamp(BatchStart, 0, MaxRegisterAddress);
        int maxCountFromStart = MaxRegisterAddress - start + 1;
        int count = Math.Clamp(BatchCount, 1, Math.Min(MaxBatchCount, maxCountFromStart));
        int end = start + count - 1;
        var existingAddresses = Rows.Select(r => r.Address).ToHashSet();
        var toAdd = new List<InspectorRow>();
        for (int i = 0; i < count; i++)
        {
            int addr = start + i;
            if (existingAddresses.Contains(addr)) continue;
            existingAddresses.Add(addr);
            toAdd.Add(CreateRow(addr));
        }
        // 一次性通知 UI，避免批量加载几万行时 DataGrid 重复刷新。
        if (Rows.Count == 0 || Rows[^1].Address <= start)
        {
            Rows.AddRange(toAdd);
        }
        else
        {
            Rows.ReplaceWith(Rows.Concat(toAdd).OrderBy(r => r.Address));
        }

        AppLogger.Info($"寄存器检视：已加载地址 {start}~{end}，共 {toAdd.Count} 行");
    }

    // ----------------------------------------------------------------
    // 命令：刷新全部（从 RegisterBank 读取当前值）
    // ----------------------------------------------------------------

    [RelayCommand]
    public void RefreshAll()
    {
        foreach (var row in Rows)
            row.Value = _bank.Read(row.Address);
    }

    // ----------------------------------------------------------------
    // 命令：删除行
    // ----------------------------------------------------------------

    [RelayCommand]
    public void RemoveRow(InspectorRow? row)
    {
        if (row != null) Rows.Remove(row);
    }

    // ----------------------------------------------------------------
    // 命令：清空所有行
    // ----------------------------------------------------------------

    [RelayCommand]
    public void ClearRows()
    {
        Rows.Clear();
        AppLogger.Info("寄存器检视：已清空");
    }

    public void LoadRowsForProtocolEdit(IEnumerable<(int Address, ushort Value, string Note)> rows)
    {
        var loaded = rows
            .Where(r => r.Address is >= 0 and <= MaxRegisterAddress)
            .GroupBy(r => r.Address)
            .Select(g => g.First())
            .OrderBy(r => r.Address)
            .Select(r =>
            {
                var row = CreateRow(r.Address);
                row.Value = r.Value;
                row.Note = r.Note ?? string.Empty;
                _bank.Write(r.Address, r.Value);
                return row;
            })
            .ToList();

        Rows.ReplaceWith(loaded);
        AppLogger.Info($"寄存器检视：已载入协议编辑行 {loaded.Count} 条");
    }

    // ----------------------------------------------------------------
    // 内部辅助
    // ----------------------------------------------------------------

    private InspectorRow CreateRow(int address)
    {
        var row = new InspectorRow(address);
        row.Value = _bank.Read(address);        // 创建时读入当前值
        row.WriteRequested += () => _bank.Write(row.Address, row.Value);
        return row;
    }

    private void SortRows()
    {
        for (int i = 1; i < Rows.Count; i++)
        {
            if (Rows[i - 1].Address <= Rows[i].Address) continue;

            Rows.ReplaceWith(Rows.OrderBy(r => r.Address));
            return;
        }
    }
}

public sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
    private bool _suppressNotification;

    public void AddRange(IEnumerable<T> items)
    {
        var list = items as ICollection<T> ?? items.ToList();
        if (list.Count == 0) return;

        _suppressNotification = true;
        try
        {
            foreach (var item in list)
                Items.Add(item);
        }
        finally
        {
            _suppressNotification = false;
        }

        RaiseReset();
    }

    public void ReplaceWith(IEnumerable<T> items)
    {
        var list = items as ICollection<T> ?? items.ToList();

        _suppressNotification = true;
        try
        {
            Items.Clear();
            foreach (var item in list)
                Items.Add(item);
        }
        finally
        {
            _suppressNotification = false;
        }

        RaiseReset();
    }

    protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        if (!_suppressNotification)
            base.OnCollectionChanged(e);
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        if (!_suppressNotification)
            base.OnPropertyChanged(e);
    }

    private void RaiseReset()
    {
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}

// ────────────────────────────────────────────────────────────────────
// 单行数据
// ────────────────────────────────────────────────────────────────────

/// <summary>
/// 寄存器检视器中的一行：地址、十六进制地址、十进制值、十六进制值、备注。
/// </summary>
public partial class InspectorRow : ObservableObject
{
    public int    Address { get; }
    public string AddrHex => $"0x{Address:X4}";

    [ObservableProperty] private ushort _value;
    [ObservableProperty] private string _note = string.Empty;

    public string HexValue => $"0x{Value:X4}";

    /// <summary>用户在界面点击"写入"时触发，通知 ViewModel 写 RegisterBank</summary>
    public event Action? WriteRequested;

    public InspectorRow(int address) => Address = address;

    partial void OnValueChanged(ushort value) => OnPropertyChanged(nameof(HexValue));

    /// <summary>触发写入到 RegisterBank</summary>
    public void RequestWrite() => WriteRequested?.Invoke();
}
