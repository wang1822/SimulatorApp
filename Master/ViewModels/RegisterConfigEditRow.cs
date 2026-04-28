using CommunityToolkit.Mvvm.ComponentModel;
using SimulatorApp.Master.Models;

namespace SimulatorApp.Master.ViewModels;

/// <summary>
/// 保存配置对话框 DataGrid 的可编辑行，对应一条寄存器字段配置。
/// </summary>
public partial class RegisterConfigEditRow : ObservableObject
{
    [ObservableProperty] private int    _startAddress;
    [ObservableProperty] private int    _quantity         = 1;
    [ObservableProperty] private string _variableName     = string.Empty;
    [ObservableProperty] private string _chineseName      = string.Empty;
    [ObservableProperty] private string _readWrite        = "R";
    [ObservableProperty] private string _unit             = string.Empty;
    [ObservableProperty] private string _dataType         = "uint16";
    [ObservableProperty] private string _registerDataType = "uint16";
    [ObservableProperty] private double _scaleFactor      = 1.0;
    [ObservableProperty] private double _offset           = 0.0;
    [ObservableProperty] private string _valueRange       = string.Empty;
    [ObservableProperty] private string _description      = string.Empty;

    /// <summary>DB 主键（0 表示新建）</summary>
    public int Id       { get; set; } = 0;
    /// <summary>0=遥测 1=遥控（由所在 Tab 设置）</summary>
    public int Category { get; set; } = 0;

    // ────────────────────────────────────────────────────────────────────

    public MasterRegisterConfig ToModel(int stationId) => new()
    {
        Id               = Id,
        StationId        = stationId,
        StartAddress     = StartAddress,
        Quantity         = Quantity,
        VariableName     = VariableName,
        ChineseName      = ChineseName,
        ReadWrite        = ReadWrite,
        Unit             = Unit,
        DataType         = DataType,
        RegisterDataType = RegisterDataType,
        ScaleFactor      = ScaleFactor,
        Offset           = Offset,
        ValueRange       = ValueRange,
        Description      = Description,
        Category         = Category
    };

    public static RegisterConfigEditRow FromModel(MasterRegisterConfig m) => new()
    {
        Id               = m.Id,
        StartAddress     = m.StartAddress,
        Quantity         = m.Quantity,
        VariableName     = m.VariableName,
        ChineseName      = m.ChineseName,
        ReadWrite        = m.ReadWrite,
        Unit             = m.Unit,
        DataType         = m.DataType,
        RegisterDataType = m.RegisterDataType,
        ScaleFactor      = m.ScaleFactor,
        Offset           = m.Offset,
        ValueRange       = m.ValueRange,
        Description      = m.Description,
        Category         = m.Category
    };
}
