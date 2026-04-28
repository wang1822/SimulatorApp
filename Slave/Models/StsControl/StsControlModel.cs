using SimulatorApp.Shared.Services;

namespace SimulatorApp.Slave.Models.StsControl;

/// <summary>
/// STS 控制IO卡数据模型（基地址 1920）。
/// 电表部分字段 + DI/DO 状态，参考设计文档第 8.5 节。
/// </summary>
public class StsControlModel : DeviceModelBase
{
    public override string DeviceName => "STS 控制IO卡";
    public override int BaseAddress    => 1920;
    public override int RegisterCount  => 194;

    // 电表遥测（float32，×0.001）
    public double L1PhaseVoltage { get; set; } = 220.0; // 偏移 +0
    public double L2PhaseVoltage { get; set; } = 220.0;
    public double L3PhaseVoltage { get; set; } = 220.0;
    public double L1Current      { get; set; } = 0.0;   // 偏移 +6
    public double L2Current      { get; set; } = 0.0;
    public double L3Current      { get; set; } = 0.0;
    public double TolActivePower { get; set; } = 0.0;   // 偏移 +48
    public double Frequency      { get; set; } = 50.0;  // 偏移 +58
    public double PosActiveCharge{ get; set; } = 0.0;   // 偏移 +60

    // DI/DO 状态
    public int GridDisconnect       { get; set; } = 0; // 偏移 +178
    public int GeneratorDisconnect  { get; set; } = 0; // 偏移 +179
    public int GridFeedback         { get; set; } = 0; // 偏移 +186
    public int GeneratorFeedback    { get; set; } = 0; // 偏移 +187

    public override void ToRegisters(RegisterBank bank)
    {
        int b = BaseAddress;
        void W(int off, double v) => bank.WriteFloat32(b + off, (float)(v / 0.001));
        W(0, L1PhaseVoltage); W(2, L2PhaseVoltage); W(4, L3PhaseVoltage);
        W(6, L1Current); W(8, L2Current); W(10, L3Current);
        W(48, TolActivePower);
        W(58, Frequency);
        W(60, PosActiveCharge);
        bank.Write(b + 178, (ushort)GridDisconnect);
        bank.Write(b + 179, (ushort)GeneratorDisconnect);
        bank.Write(b + 186, (ushort)GridFeedback);
        bank.Write(b + 187, (ushort)GeneratorFeedback);
    }

    public override void FromRegisters(RegisterBank bank)
    {
        int b = BaseAddress;
        double R(int off) => Math.Round(bank.ReadFloat32(b + off) * 0.001, 3);
        L1PhaseVoltage = R(0); L2PhaseVoltage = R(2); L3PhaseVoltage = R(4);
        L1Current = R(6); L2Current = R(8); L3Current = R(10);
        TolActivePower = R(48);
        Frequency      = R(58);
        PosActiveCharge= R(60);
        GridDisconnect      = bank.Read(b + 178);
        GeneratorDisconnect = bank.Read(b + 179);
        GridFeedback        = bank.Read(b + 186);
        GeneratorFeedback   = bank.Read(b + 187);
    }
}
