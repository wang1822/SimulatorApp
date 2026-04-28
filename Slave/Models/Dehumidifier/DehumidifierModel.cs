using SimulatorApp.Shared.Services;

namespace SimulatorApp.Slave.Models.Dehumidifier;

/// <summary>
/// 除湿机数据模型（基地址 4097，即 0x1001）。
/// 字段定义参考 FX 除湿机通讯协议 V1.1。
/// </summary>
public class DehumidifierModel : DeviceModelBase
{
    public override string DeviceName => "除湿机";
    public override int BaseAddress    => 4097;   // 0x1001
    public override int RegisterCount  => 32;

    // 遥测
    public double Temperature  { get; set; } = 25.0;  // S16×0.1 ℃，偏移 +12
    public double Humidity     { get; set; } = 70.0;  // U16×0.1 %RH，偏移 +13
    public double Ntc1Temp     { get; set; } = 25.0;  // S16×0.1 ℃，偏移 +14
    public double Ntc2Temp     { get; set; } = 25.0;  // S16×0.1 ℃，偏移 +15
    public double InputVoltage { get; set; } = 12.0;  // U16×0.01 V，偏移 +16
    public uint   Fan1Runtime  { get; set; } = 0;     // U32 h，偏移 +21
    public uint   Fan2Runtime  { get; set; } = 0;     // U32 h，偏移 +23
    public double Fan1Current  { get; set; } = 0.0;   // U16×0.001 A，偏移 +31

    // 状态字（bitmask，U32，偏移 +8~+9）
    public uint StatusWord { get; set; } = 0;
    // Bit 0=待机, 1=除湿, 2=强制, 3=风机1运行, 4=风机2, 5=风机3, 6=风机4, 7=除湿模块运行, 8=工程模式

    // 故障字（bitmask，U32，偏移 +10~+11）
    public uint FaultWord { get; set; } = 0;
    // Bit 0=MODBUS通讯中断, 1=过电压, 2=欠电压, 3=温度传感器失效, 4=湿度传感器失效,
    //     5=NTC1失效, 6=NTC2失效, 7=除湿模块故障, 8=风机故障1, 9=故障2, 10=故障3, 11=故障4, 12=湿度过大

    public override void ToRegisters(RegisterBank bank)
    {
        int b = BaseAddress;

        // 状态字/故障字（U32，高字在低地址）
        bank.WriteUInt32(b + 8,  StatusWord);
        bank.WriteUInt32(b + 10, FaultWord);

        // 遥测（各类型）
        bank.WriteInt16(b + 12, (short)(Temperature  / 0.1));
        bank.Write(b + 13, (ushort)(Humidity     / 0.1));
        bank.WriteInt16(b + 14, (short)(Ntc1Temp    / 0.1));
        bank.WriteInt16(b + 15, (short)(Ntc2Temp    / 0.1));
        bank.Write(b + 16, (ushort)(InputVoltage / 0.01));
        bank.WriteUInt32(b + 21, Fan1Runtime);
        bank.WriteUInt32(b + 23, Fan2Runtime);
        bank.Write(b + 31, (ushort)(Fan1Current / 0.001));
    }

    public override void FromRegisters(RegisterBank bank)
    {
        int b = BaseAddress;
        StatusWord   = ((uint)bank.Read(b + 8)  << 16) | bank.Read(b + 9);
        FaultWord    = ((uint)bank.Read(b + 10) << 16) | bank.Read(b + 11);
        Temperature  = (short)bank.Read(b + 12) * 0.1;
        Humidity     = bank.Read(b + 13) * 0.1;
        Ntc1Temp     = (short)bank.Read(b + 14) * 0.1;
        Ntc2Temp     = (short)bank.Read(b + 15) * 0.1;
        InputVoltage = bank.Read(b + 16) * 0.01;
        Fan1Runtime  = ((uint)bank.Read(b + 21) << 16) | bank.Read(b + 22);
        Fan2Runtime  = ((uint)bank.Read(b + 23) << 16) | bank.Read(b + 24);
        Fan1Current  = bank.Read(b + 31) * 0.001;
    }
}
