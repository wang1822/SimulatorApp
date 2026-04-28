using SimulatorApp.Shared.Services;

namespace SimulatorApp.Slave.Models.Mppt;

/// <summary>
/// MPPT 光伏最大功率点跟踪器数据模型（基地址 40064）。
/// 字段定义参考设计文档第 8.3 节。
/// </summary>
public class MpptModel : DeviceModelBase
{
    public override string DeviceName => "MPPT 光伏";
    public override int BaseAddress    => 40064;
    public override int RegisterCount  => 157;

    // 遥测
    public double OutputVolt       { get; set; } = 600.0;  // ×0.1 V，偏移 +97
    public double OutputCurrent    { get; set; } = 0.0;    // ×0.1 A，偏移 +98
    public double OutputPower      { get; set; } = 0.0;    // ×0.1 W，偏移 +99
    public double PVTotalPower     { get; set; } = 0.0;    // ×0.01 W，偏移 +100
    public double DCVolt1          { get; set; } = 400.0;  // ×0.1 V，偏移 +101
    public double DCCurrent1       { get; set; } = 0.0;    // ×0.1 A，偏移 +102
    public double DCPower1         { get; set; } = 0.0;    // ×0.001 W，偏移 +103
    public double DCVolt2          { get; set; } = 400.0;  // ×0.1 V，偏移 +104
    public double DCCurrent2       { get; set; } = 0.0;    // ×0.1 A，偏移 +105
    public double DCPower2         { get; set; } = 0.0;    // ×0.001 W，偏移 +106
    public double DailyPVGenTotal  { get; set; } = 0.0;    // ×0.1 kWh，偏移 +134
    public double HistoryPvGenCapacity { get; set; } = 0.0; // ×1 kWh，偏移 +137
    public double HeatSinkTemp     { get; set; } = 25.0;   // ×0.1 ℃，偏移 +138

    // 状态
    /// <summary>运行模式，偏移 +96：0=待机, 1=自检, 2=正常</summary>
    public int MpptOperatingMode { get; set; } = 0;

    // 故障/告警
    public ushort MPPTAlarm1 { get; set; } // 偏移 +140
    public ushort MPPTAlarm2 { get; set; } // 偏移 +141
    public ushort MPPTFault1 { get; set; } // 偏移 +142
    public ushort MPPTFault2 { get; set; } // 偏移 +143
    public ushort MPPTFault3 { get; set; } // 偏移 +144
    public ushort MPPTFault4 { get; set; } // 偏移 +145

    public override void ToRegisters(RegisterBank bank)
    {
        int b = BaseAddress;

        bank.Write(b + 96, (ushort)MpptOperatingMode);

        bank.WriteFloat32(b + 97,  (float)(OutputVolt   / 0.1));
        bank.WriteFloat32(b + 98,  (float)(OutputCurrent / 0.1));
        bank.WriteFloat32(b + 99,  (float)(OutputPower   / 0.1));
        bank.WriteFloat32(b + 100, (float)(PVTotalPower  / 0.01));
        bank.WriteFloat32(b + 101, (float)(DCVolt1       / 0.1));
        bank.WriteFloat32(b + 102, (float)(DCCurrent1    / 0.1));
        bank.WriteFloat32(b + 103, (float)(DCPower1      / 0.001));
        bank.WriteFloat32(b + 104, (float)(DCVolt2       / 0.1));
        bank.WriteFloat32(b + 105, (float)(DCCurrent2    / 0.1));
        bank.WriteFloat32(b + 106, (float)(DCPower2      / 0.001));
        bank.WriteFloat32(b + 134, (float)(DailyPVGenTotal      / 0.1));
        bank.WriteFloat32(b + 137, (float)(HistoryPvGenCapacity / 1.0));
        bank.WriteFloat32(b + 138, (float)(HeatSinkTemp         / 0.1));

        bank.Write(b + 140, MPPTAlarm1);
        bank.Write(b + 141, MPPTAlarm2);
        bank.Write(b + 142, MPPTFault1);
        bank.Write(b + 143, MPPTFault2);
        bank.Write(b + 144, MPPTFault3);
        bank.Write(b + 145, MPPTFault4);
    }

    public override void FromRegisters(RegisterBank bank)
    {
        int b = BaseAddress;
        MpptOperatingMode    = bank.Read(b + 96);
        OutputVolt           = Math.Round(bank.ReadFloat32(b + 97)  * 0.1,   2);
        OutputCurrent        = Math.Round(bank.ReadFloat32(b + 98)  * 0.1,   2);
        OutputPower          = Math.Round(bank.ReadFloat32(b + 99)  * 0.1,   2);
        PVTotalPower         = Math.Round(bank.ReadFloat32(b + 100) * 0.01,  2);
        DCVolt1              = Math.Round(bank.ReadFloat32(b + 101) * 0.1,   2);
        DCCurrent1           = Math.Round(bank.ReadFloat32(b + 102) * 0.1,   2);
        DCPower1             = Math.Round(bank.ReadFloat32(b + 103) * 0.001, 2);
        DCVolt2              = Math.Round(bank.ReadFloat32(b + 104) * 0.1,   2);
        DCCurrent2           = Math.Round(bank.ReadFloat32(b + 105) * 0.1,   2);
        DCPower2             = Math.Round(bank.ReadFloat32(b + 106) * 0.001, 2);
        DailyPVGenTotal      = Math.Round(bank.ReadFloat32(b + 134) * 0.1,   2);
        HistoryPvGenCapacity = Math.Round(bank.ReadFloat32(b + 137) * 1.0,   2);
        HeatSinkTemp         = Math.Round(bank.ReadFloat32(b + 138) * 0.1,   2);
        MPPTAlarm1 = bank.Read(b + 140);
        MPPTAlarm2 = bank.Read(b + 141);
        MPPTFault1 = bank.Read(b + 142);
        MPPTFault2 = bank.Read(b + 143);
        MPPTFault3 = bank.Read(b + 144);
        MPPTFault4 = bank.Read(b + 145);
    }
}
