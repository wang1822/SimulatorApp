using SimulatorApp.Shared.Services;

namespace SimulatorApp.Slave.Models.StsInstrument;

/// <summary>
/// STS 仪表数据模型（基地址 1408）。
/// 字段定义参考设计文档第 8.4 节。
/// </summary>
public class StsInstrumentModel : DeviceModelBase
{
    public override string DeviceName => "STS 仪表";
    public override int BaseAddress    => 1408;
    public override int RegisterCount  => 161;

    public double GridVoltA         { get; set; } = 220.0; // ×0.1 V，偏移 +20
    public double GridVoltB         { get; set; } = 220.0;
    public double GridVoltC         { get; set; } = 220.0;
    public double GridCurrentA      { get; set; } = 0.0;   // ×0.1 A，偏移 +26
    public double GridCurrentB      { get; set; } = 0.0;
    public double GridCurrentC      { get; set; } = 0.0;
    public double StsGridFrequency  { get; set; } = 50.0;  // ×0.1 Hz，偏移 +29
    public double GeneratorVoltA    { get; set; } = 0.0;
    public double GeneratorVoltB    { get; set; } = 0.0;
    public double GeneratorVoltC    { get; set; } = 0.0;
    public double GeneratorFrequency{ get; set; } = 0.0;   // 偏移 +39
    public double InverterVoltA     { get; set; } = 220.0; // 偏移 +40
    public double InverterFrequency { get; set; } = 50.0;  // 偏移 +46

    public int StsOperatingMode { get; set; } = 0; // 偏移 +19：0=待机,1=自检,2=正常

    public ushort STSAlarm1 { get; set; }
    public ushort STSAlarm2 { get; set; }
    public ushort STSFault1 { get; set; }
    public ushort STSFault2 { get; set; }
    public ushort STSFault3 { get; set; }
    public ushort STSFault4 { get; set; }

    public override void ToRegisters(RegisterBank bank)
    {
        int b = BaseAddress;
        bank.Write(b + 19, (ushort)StsOperatingMode);
        void WF(int off, double v) => bank.WriteFloat32(b + off, (float)(v / 0.1));
        WF(20, GridVoltA); WF(21, GridVoltB); WF(22, GridVoltC);
        WF(26, GridCurrentA); WF(27, GridCurrentB); WF(28, GridCurrentC);
        WF(29, StsGridFrequency);
        WF(30, GeneratorVoltA); WF(31, GeneratorVoltB); WF(32, GeneratorVoltC);
        WF(39, GeneratorFrequency);
        WF(40, InverterVoltA);
        WF(46, InverterFrequency);
        bank.Write(b + 140, STSAlarm1); bank.Write(b + 141, STSAlarm2);
        bank.Write(b + 142, STSFault1); bank.Write(b + 143, STSFault2);
        bank.Write(b + 144, STSFault3); bank.Write(b + 145, STSFault4);
    }

    public override void FromRegisters(RegisterBank bank)
    {
        int b = BaseAddress;
        StsOperatingMode  = bank.Read(b + 19);
        double R(int off) => Math.Round(bank.ReadFloat32(b + off) * 0.1, 2);
        GridVoltA = R(20); GridVoltB = R(21); GridVoltC = R(22);
        GridCurrentA = R(26); GridCurrentB = R(27); GridCurrentC = R(28);
        StsGridFrequency  = R(29);
        GeneratorVoltA    = R(30); GeneratorVoltB = R(31); GeneratorVoltC = R(32);
        GeneratorFrequency= R(39);
        InverterVoltA     = R(40);
        InverterFrequency = R(46);
        STSAlarm1 = bank.Read(b + 140); STSAlarm2 = bank.Read(b + 141);
        STSFault1 = bank.Read(b + 142); STSFault2 = bank.Read(b + 143);
        STSFault3 = bank.Read(b + 144); STSFault4 = bank.Read(b + 145);
    }
}
