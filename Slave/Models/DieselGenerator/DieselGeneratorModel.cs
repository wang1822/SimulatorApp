using SimulatorApp.Shared.Services;

namespace SimulatorApp.Slave.Models.DieselGenerator;

/// <summary>
/// 柴油发电机组数据模型（基地址 53504）。
/// ⚠️ 协议文档待补充，当前为占位实现，提供基本遥测字段框架。
/// </summary>
public class DieselGeneratorModel : DeviceModelBase
{
    public override string DeviceName => "柴发";
    public override int BaseAddress    => 53504;
    public override int RegisterCount  => 64;

    // 基本遥测（字段待协议文档确认）
    public double PhaseAVolt    { get; set; } = 220.0; // A相电压 V
    public double PhaseBVolt    { get; set; } = 220.0;
    public double PhaseCVolt    { get; set; } = 220.0;
    public double PhaseACurrent { get; set; } = 0.0;   // A相电流 A
    public double Frequency     { get; set; } = 50.0;  // 频率 Hz
    public double ActivePower   { get; set; } = 0.0;   // 有功功率 kW
    public double OilPressure   { get; set; } = 0.0;   // 油压 kPa
    public double CoolantTemp   { get; set; } = 25.0;  // 冷却水温 ℃
    public double BatteryVolt   { get; set; } = 24.0;  // 启动电池电压 V
    public int    RunHours      { get; set; } = 0;      // 运行小时数

    // 状态
    public int RunState { get; set; } = 0; // 0=停止,1=启动,2=运行,3=故障

    // 故障（占位）
    public ushort Fault1 { get; set; }
    public ushort Alarm1 { get; set; }

    public override void ToRegisters(RegisterBank bank)
    {
        int b = BaseAddress;
        // 占位实现：将物理值以 ×0.1 比例写入（待协议确认后修正）
        bank.WriteFloat32(b + 0,  (float)(PhaseAVolt    / 0.1));
        bank.WriteFloat32(b + 2,  (float)(PhaseBVolt    / 0.1));
        bank.WriteFloat32(b + 4,  (float)(PhaseCVolt    / 0.1));
        bank.WriteFloat32(b + 6,  (float)(PhaseACurrent / 0.1));
        bank.WriteFloat32(b + 8,  (float)(Frequency     / 0.01));
        bank.WriteFloat32(b + 10, (float)(ActivePower   / 0.1));
        bank.WriteFloat32(b + 12, (float)(OilPressure   / 0.1));
        bank.WriteFloat32(b + 14, (float)(CoolantTemp   / 0.1));
        bank.WriteFloat32(b + 16, (float)(BatteryVolt   / 0.1));
        bank.Write(b + 18, (ushort)RunHours);
        bank.Write(b + 20, (ushort)RunState);
        bank.Write(b + 21, Fault1);
        bank.Write(b + 22, Alarm1);
    }

    public override void FromRegisters(RegisterBank bank)
    {
        int b = BaseAddress;
        PhaseAVolt    = Math.Round(bank.ReadFloat32(b + 0)  * 0.1,  2);
        PhaseBVolt    = Math.Round(bank.ReadFloat32(b + 2)  * 0.1,  2);
        PhaseCVolt    = Math.Round(bank.ReadFloat32(b + 4)  * 0.1,  2);
        PhaseACurrent = Math.Round(bank.ReadFloat32(b + 6)  * 0.1,  2);
        Frequency     = Math.Round(bank.ReadFloat32(b + 8)  * 0.01, 3);
        ActivePower   = Math.Round(bank.ReadFloat32(b + 10) * 0.1,  2);
        OilPressure   = Math.Round(bank.ReadFloat32(b + 12) * 0.1,  2);
        CoolantTemp   = Math.Round(bank.ReadFloat32(b + 14) * 0.1,  2);
        BatteryVolt   = Math.Round(bank.ReadFloat32(b + 16) * 0.1,  2);
        RunHours      = bank.Read(b + 18);
        RunState      = bank.Read(b + 20);
        Fault1        = bank.Read(b + 21);
        Alarm1        = bank.Read(b + 22);
    }
}
