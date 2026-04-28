using SimulatorApp.Shared.Services;

namespace SimulatorApp.Slave.Models.DIDOController;

/// <summary>
/// DI/DO 动环控制器数据模型（基地址 60544）。
/// 字段定义参考设计文档第 8.10 节。
/// </summary>
public class DIDOControllerModel : DeviceModelBase
{
    public override string DeviceName => "DI/DO 动环控制器";
    public override int BaseAddress    => 60544;
    public override int RegisterCount  => 41;

    // DI 输入状态（只读/可模拟）
    public int EmergencyStop      { get; set; } = 0; // 偏移 +0：0=未按下,1=按下
    public int QF1GridFeedback    { get; set; } = 0; // 偏移 +1：0=闭合,1=断开
    public int QF2BatFeedback     { get; set; } = 0;
    public int WaterSensor        { get; set; } = 0; // 偏移 +3：0=正常,1=告警
    public int AerosolSensor      { get; set; } = 0;
    public int BmsAccessControl   { get; set; } = 0; // 偏移 +5：0=关门,1=开门
    public int GasLowAlarm        { get; set; } = 0;
    public int GasHighAlarm       { get; set; } = 0;
    public int ElecAccessControl  { get; set; } = 0; // 偏移 +9
    public int LtgProtsignal      { get; set; } = 0; // 偏移 +10
    public int TempSensor         { get; set; } = 0; // 偏移 +12
    public int SmokeSensor        { get; set; } = 0; // 偏移 +13

    // DO 输出控制
    public int QF1GridTripEnable     { get; set; } = 0; // 偏移 +14
    public int QF2BatTripEnable      { get; set; } = 0;
    public int RemoteEmergencyStop   { get; set; } = 0;
    public int FanStartStop          { get; set; } = 0; // 偏移 +20
    public int AcoustoOptic          { get; set; } = 0; // 偏移 +21
    public int ControlMode           { get; set; } = 0; // 偏移 +22

    public override void ToRegisters(RegisterBank bank)
    {
        int b = BaseAddress;
        bank.Write(b + 0,  (ushort)EmergencyStop);
        bank.Write(b + 1,  (ushort)QF1GridFeedback);
        bank.Write(b + 2,  (ushort)QF2BatFeedback);
        bank.Write(b + 3,  (ushort)WaterSensor);
        bank.Write(b + 4,  (ushort)AerosolSensor);
        bank.Write(b + 5,  (ushort)BmsAccessControl);
        bank.Write(b + 6,  (ushort)GasLowAlarm);
        bank.Write(b + 7,  (ushort)GasHighAlarm);
        bank.Write(b + 9,  (ushort)ElecAccessControl);
        bank.Write(b + 10, (ushort)LtgProtsignal);
        bank.Write(b + 12, (ushort)TempSensor);
        bank.Write(b + 13, (ushort)SmokeSensor);
        bank.Write(b + 14, (ushort)QF1GridTripEnable);
        bank.Write(b + 15, (ushort)QF2BatTripEnable);
        bank.Write(b + 16, (ushort)RemoteEmergencyStop);
        bank.Write(b + 20, (ushort)FanStartStop);
        bank.Write(b + 21, (ushort)AcoustoOptic);
        bank.Write(b + 22, (ushort)ControlMode);
    }

    public override void FromRegisters(RegisterBank bank)
    {
        int b = BaseAddress;
        EmergencyStop      = bank.Read(b + 0);
        QF1GridFeedback    = bank.Read(b + 1);
        QF2BatFeedback     = bank.Read(b + 2);
        WaterSensor        = bank.Read(b + 3);
        AerosolSensor      = bank.Read(b + 4);
        BmsAccessControl   = bank.Read(b + 5);
        GasLowAlarm        = bank.Read(b + 6);
        GasHighAlarm       = bank.Read(b + 7);
        ElecAccessControl  = bank.Read(b + 9);
        LtgProtsignal      = bank.Read(b + 10);
        TempSensor         = bank.Read(b + 12);
        SmokeSensor        = bank.Read(b + 13);
        QF1GridTripEnable  = bank.Read(b + 14);
        QF2BatTripEnable   = bank.Read(b + 15);
        RemoteEmergencyStop= bank.Read(b + 16);
        FanStartStop       = bank.Read(b + 20);
        AcoustoOptic       = bank.Read(b + 21);
        ControlMode        = bank.Read(b + 22);
    }
}
