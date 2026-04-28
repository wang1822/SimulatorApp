using CommunityToolkit.Mvvm.ComponentModel;
using SimulatorApp.Shared.Services;
using SimulatorApp.Slave.Models;
using SimulatorApp.Slave.Services;

namespace SimulatorApp.Slave.ViewModels;

/// <summary>
/// 自定义设备 ViewModel（通过 Excel 模板导入）。
/// 每个字段对应一个寄存器地址和原始 ushort 值。
/// </summary>
public partial class CustomDeviceViewModel : DeviceViewModelBase
{
    private readonly CustomDeviceModel _model;

    public override string DeviceName => _model.DeviceName;
    protected override DeviceModelBase Model => _model;

    /// <summary>所有可编辑字段行（地址、名称、值）</summary>
    public IReadOnlyList<CustomRegisterRow> Rows { get; }

    public CustomDeviceViewModel(CustomDeviceModel model,
        RegisterBank bank, RegisterMapService mapService) : base(bank, mapService)
    {
        _model = model;
        Rows   = model.Rows.Select(r => new CustomRegisterRow(r, this)).ToList();
    }

    protected override void SyncToModel()
    {
        // 每行直接写 bank，无需中间 Model
        foreach (var row in Rows)
            _bank.Write(row.Address, row.RawValue);
    }

    public override void GenerateData()
    {
        var rng = new Random();
        foreach (var row in Rows)
            row.RawValue = (ushort)rng.Next(0, 0x7FFF);
        FlushToRegisters();
    }

    public override void ClearAlarms()
    {
        // 自定义设备没有 bitmask，清零所有寄存器
        foreach (var row in Rows)
            row.RawValue = 0;
        FlushToRegisters();
    }
}

/// <summary>
/// 自定义设备中的单个寄存器行（可双向编辑）
/// </summary>
public partial class CustomRegisterRow : ObservableObject
{
    private readonly CustomDeviceViewModel _owner;

    public int    Address     { get; }
    public string FieldName   { get; }
    public string Unit        { get; }

    [ObservableProperty] private ushort _rawValue;

    partial void OnRawValueChanged(ushort value) => _owner.FlushToRegisters();

    public CustomRegisterRow(CustomRegisterDef def, CustomDeviceViewModel owner)
    {
        _owner    = owner;
        Address   = def.Address;
        FieldName = def.FieldName;
        Unit      = def.Unit;
        _rawValue = def.DefaultValue;
    }
}
