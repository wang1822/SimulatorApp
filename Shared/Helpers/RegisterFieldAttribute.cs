namespace SimulatorApp.Shared.Helpers;

/// <summary>
/// 标注 ViewModel 属性对应的 Modbus 寄存器元数据。
/// 供 ExcelHelper.ExportDeviceViewModel 生成含中文名、类型、地址、值范围的规范表格。
///
/// 用法（CommunityToolkit.Mvvm 字段上，[property:] 转发到生成属性）：
/// <code>
/// [ObservableProperty]
/// [property: RegisterField("环境温度", 4109, "S16×0.1 ℃", Min = -40, Max = 80)]
/// private double _temperature = 25.0;
/// </code>
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class RegisterFieldAttribute : Attribute
{
    /// <summary>字段中文名称（导出 Excel 必填列）</summary>
    public string ChineseName { get; }

    /// <summary>寄存器绝对地址（Holding Register，0-based）</summary>
    public int Address { get; }

    /// <summary>
    /// 数据类型及比例描述，例如：
    /// "S16×0.1 ℃" / "U16×0.001 A" / "U32 h" / "float32 kW" / "bitmask"
    /// </summary>
    public string DataType { get; }

    /// <summary>工程量最小值（物理值）；double.NaN 表示不限</summary>
    public double Min { get; init; } = double.NaN;

    /// <summary>工程量最大值（物理值）；double.NaN 表示不限</summary>
    public double Max { get; init; } = double.NaN;

    public RegisterFieldAttribute(string chineseName, int address, string dataType)
    {
        ChineseName = chineseName;
        Address     = address;
        DataType    = dataType;
    }
}
