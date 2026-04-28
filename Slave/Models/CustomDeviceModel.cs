namespace SimulatorApp.Slave.Models;

/// <summary>
/// 通过 Excel 导入的自定义设备寄存器定义
/// </summary>
public class CustomRegisterDef
{
    public int    Address      { get; set; }
    public string FieldName    { get; set; } = string.Empty;
    public string Unit         { get; set; } = string.Empty;
    public ushort DefaultValue { get; set; } = 0;
}

/// <summary>
/// 自定义设备数据模型（无需继承 DeviceModelBase，直接持有寄存器定义列表）
/// </summary>
public class CustomDeviceModel : DeviceModelBase
{
    private readonly string _deviceName;

    public override string DeviceName  => _deviceName;
    public override int    BaseAddress => Rows.Count > 0 ? Rows[0].Address : 0;

    public List<CustomRegisterDef> Rows { get; } = new();

    public CustomDeviceModel(string deviceName) => _deviceName = deviceName;

    public override void ToRegisters(Shared.Services.RegisterBank bank)
    {
        foreach (var row in Rows)
            bank.Write(row.Address, row.DefaultValue);
    }

    public override void FromRegisters(Shared.Services.RegisterBank bank)
    {
        foreach (var row in Rows)
            row.DefaultValue = bank.Read(row.Address);
    }
}
