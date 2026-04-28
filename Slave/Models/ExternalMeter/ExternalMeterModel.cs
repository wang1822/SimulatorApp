using SimulatorApp.Shared.Services;

namespace SimulatorApp.Slave.Models.ExternalMeter;

/// <summary>
/// 外部电表数据模型（基地址 384）。
/// 字段定义参考设计文档第 8.8 节。
/// 储能电表（基地址 48256）与本模型字段完全相同，通过子类区分。
/// </summary>
public class ExternalMeterModel : DeviceModelBase
{
    public override string DeviceName => "外部电表";
    public override int BaseAddress    => 384;
    public override int RegisterCount  => 178;

    // 遥测（全部 float32，×0.001）
    public double L1PhaseVoltage  { get; set; } = 220.0;
    public double L2PhaseVoltage  { get; set; } = 220.0;
    public double L3PhaseVoltage  { get; set; } = 220.0;
    public double L1Current       { get; set; } = 0.0;
    public double L2Current       { get; set; } = 0.0;
    public double L3Current       { get; set; } = 0.0;
    public double L1ActivePower   { get; set; } = 0.0;
    public double L2ActivePower   { get; set; } = 0.0;
    public double L3ActivePower   { get; set; } = 0.0;
    public double TolActivePower  { get; set; } = 0.0;
    public double TolReactivePower{ get; set; } = 0.0;
    public double TolPowerFactor  { get; set; } = 1.000;
    public double Frequency       { get; set; } = 50.0;
    public double PosActiveCharge { get; set; } = 0.0;
    public double RevActiveCharge { get; set; } = 0.0;
    public double L12LineVoltage  { get; set; } = 380.0;
    public double L23LineVoltage  { get; set; } = 380.0;
    public double L31LineVoltage  { get; set; } = 380.0;

    // 状态
    public int TimeoutFlag { get; set; } = 1; // 偏移 +176，0=离线,1=在线

    public override void ToRegisters(RegisterBank bank)
    {
        int b = BaseAddress;
        void W(int off, double v) => bank.WriteFloat32(b + off, (float)(v / 0.001));

        W(0,  L1PhaseVoltage);
        W(2,  L2PhaseVoltage);
        W(4,  L3PhaseVoltage);
        W(6,  L1Current);
        W(8,  L2Current);
        W(10, L3Current);
        W(12, L1ActivePower);
        W(14, L2ActivePower);
        W(16, L3ActivePower);
        W(48, TolActivePower);
        W(52, TolReactivePower);
        W(54, TolPowerFactor);
        W(58, Frequency);
        W(60, PosActiveCharge);
        W(62, RevActiveCharge);
        W(88, L12LineVoltage);
        W(90, L23LineVoltage);
        W(92, L31LineVoltage);
        bank.Write(b + 176, (ushort)TimeoutFlag);
    }

    public override void FromRegisters(RegisterBank bank)
    {
        int b = BaseAddress;
        double R(int off) => Math.Round(bank.ReadFloat32(b + off) * 0.001, 3);
        L1PhaseVoltage   = R(0);
        L2PhaseVoltage   = R(2);
        L3PhaseVoltage   = R(4);
        L1Current        = R(6);
        L2Current        = R(8);
        L3Current        = R(10);
        L1ActivePower    = R(12);
        L2ActivePower    = R(14);
        L3ActivePower    = R(16);
        TolActivePower   = R(48);
        TolReactivePower = R(52);
        TolPowerFactor   = R(54);
        Frequency        = R(58);
        PosActiveCharge  = R(60);
        RevActiveCharge  = R(62);
        L12LineVoltage   = R(88);
        L23LineVoltage   = R(90);
        L31LineVoltage   = R(92);
        TimeoutFlag      = bank.Read(b + 176);
    }
}
