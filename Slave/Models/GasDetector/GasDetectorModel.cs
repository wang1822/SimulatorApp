using SimulatorApp.Shared.Services;

namespace SimulatorApp.Slave.Models.GasDetector;

/// <summary>
/// 多合一气体检测仪数据模型（基地址 53760）。
/// ⚠️ 协议文档待补充，当前为占位实现。
/// </summary>
public class GasDetectorModel : DeviceModelBase
{
    public override string DeviceName => "气体检测";
    public override int BaseAddress    => 53760;
    public override int RegisterCount  => 16;

    // 气体浓度（占位，待协议确认）
    public double H2Concentration  { get; set; } = 0.0;  // H2 浓度 ppm
    public double CO2Concentration { get; set; } = 400.0; // CO2 浓度 ppm
    public double O2Concentration  { get; set; } = 20.9;  // O2 浓度 %
    public double COConcentration  { get; set; } = 0.0;   // CO 浓度 ppm

    // 状态
    public int RunState { get; set; } = 0; // 0=正常，1=低报，2=高报
    // 告警
    public ushort AlarmWord { get; set; }

    public override void ToRegisters(RegisterBank bank)
    {
        int b = BaseAddress;
        bank.WriteFloat32(b + 0, (float)(H2Concentration  / 0.1));
        bank.WriteFloat32(b + 2, (float)(CO2Concentration / 0.1));
        bank.WriteFloat32(b + 4, (float)(O2Concentration  / 0.01));
        bank.WriteFloat32(b + 6, (float)(COConcentration  / 0.1));
        bank.Write(b + 8, (ushort)RunState);
        bank.Write(b + 9, AlarmWord);
    }

    public override void FromRegisters(RegisterBank bank)
    {
        int b = BaseAddress;
        H2Concentration  = Math.Round(bank.ReadFloat32(b + 0) * 0.1,  2);
        CO2Concentration = Math.Round(bank.ReadFloat32(b + 2) * 0.1,  2);
        O2Concentration  = Math.Round(bank.ReadFloat32(b + 4) * 0.01, 3);
        COConcentration  = Math.Round(bank.ReadFloat32(b + 6) * 0.1,  2);
        RunState         = bank.Read(b + 8);
        AlarmWord        = bank.Read(b + 9);
    }
}
