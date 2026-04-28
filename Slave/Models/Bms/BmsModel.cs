using SimulatorApp.Shared.Services;

namespace SimulatorApp.Slave.Models.Bms;

/// <summary>
/// BMS 电池管理系统数据模型（基地址 23680）。
/// 字段定义参考设计文档第 8.2 节。
/// </summary>
public class BmsModel : DeviceModelBase
{
    public override string DeviceName => "BMS 电池管理系统";
    public override int BaseAddress    => 23680;
    public override int RegisterCount  => 110;

    // ----------------------------------------------------------------
    // 遥测字段
    // ----------------------------------------------------------------

    /// <summary>累加内总压 [V]，×0.1，偏移 +0</summary>
    public double InsideAddedTotalVolt { get; set; } = 500.0;
    /// <summary>总电流 [A]，×0.1，偏移 +1</summary>
    public double CombinedCurrent { get; set; } = 0.0;
    /// <summary>SOC [%]，×0.1，偏移 +2</summary>
    public double Soc { get; set; } = 80.0;
    /// <summary>SOH [%]，×0.1，偏移 +4</summary>
    public double Soh { get; set; } = 100.0;
    /// <summary>允许充电电流 [A]，×0.1，偏移 +6</summary>
    public double AllowableChargingCurrent { get; set; } = 100.0;
    /// <summary>允许放电电流 [A]，×0.1，偏移 +7</summary>
    public double AllowableDischargeCurrent { get; set; } = 100.0;
    /// <summary>最高电芯电压 [V]，×0.001，偏移 +19</summary>
    public double MaxCellVolt { get; set; } = 3.400;
    /// <summary>最低电芯电压 [V]，×0.001，偏移 +22</summary>
    public double MinCellVolt { get; set; } = 3.300;
    /// <summary>系统压差 [V]，×0.001，偏移 +25</summary>
    public double SystemVoltDifferential { get; set; } = 0.100;
    /// <summary>最高电芯温度 [℃]，int32，偏移 +30</summary>
    public int MaxCellTemp { get; set; } = 30;
    /// <summary>最低电芯温度 [℃]，int32，偏移 +34</summary>
    public int MinCellTemp { get; set; } = 20;
    /// <summary>系统温差 [℃]，uint8，偏移 +38</summary>
    public int SystemTempDifference { get; set; } = 10;
    /// <summary>系统绝缘值 [kΩ]，uint16，偏移 +81</summary>
    public int SystemInsulationValue { get; set; } = 1000;

    // ----------------------------------------------------------------
    // 运行状态
    // ----------------------------------------------------------------

    /// <summary>系统状态，偏移 +8：0=静置, 1=充电, 2=放电</summary>
    public int SystemState { get; set; } = 0;
    /// <summary>禁止充电，偏移 +87：0=允许, 1=禁止</summary>
    public int ChargeProhibitedSign { get; set; } = 0;
    /// <summary>禁止放电，偏移 +88：0=允许, 1=禁止</summary>
    public int DischargeProhibitedSign { get; set; } = 0;

    // ----------------------------------------------------------------
    // 故障/告警字（bitmask）
    // ----------------------------------------------------------------

    public ushort Alarm1 { get; set; } // 偏移 +94
    public ushort Alarm2 { get; set; } // 偏移 +95
    public ushort Alarm3 { get; set; } // 偏移 +96
    public ushort Alarm4 { get; set; } // 偏移 +97
    public ushort Fault1 { get; set; } // 偏移 +98
    public ushort Fault2 { get; set; } // 偏移 +99
    public ushort Fault3 { get; set; } // 偏移 +100
    public ushort Fault4 { get; set; } // 偏移 +101
    public ushort Fault5 { get; set; } // 偏移 +102
    public ushort Fault6 { get; set; } // 偏移 +103
    public ushort Fault7 { get; set; } // 偏移 +104

    public override void ToRegisters(RegisterBank bank)
    {
        int b = BaseAddress;

        bank.WriteFloat32(b + 0,  (float)(InsideAddedTotalVolt        / 0.1));
        bank.WriteFloat32(b + 1,  (float)(CombinedCurrent             / 0.1));
        bank.WriteFloat32(b + 2,  (float)(Soc                         / 0.1));
        bank.WriteFloat32(b + 4,  (float)(Soh                         / 0.1));
        bank.WriteFloat32(b + 6,  (float)(AllowableChargingCurrent    / 0.1));
        bank.WriteFloat32(b + 7,  (float)(AllowableDischargeCurrent   / 0.1));
        bank.WriteFloat32(b + 19, (float)(MaxCellVolt                 / 0.001));
        bank.WriteFloat32(b + 22, (float)(MinCellVolt                 / 0.001));
        bank.WriteFloat32(b + 25, (float)(SystemVoltDifferential      / 0.001));

        // int32：高16位低地址，低16位高地址
        bank.WriteUInt32(b + 30, (uint)MaxCellTemp);
        bank.WriteUInt32(b + 34, (uint)MinCellTemp);
        bank.WriteUInt8(b + 38, (byte)SystemTempDifference);
        bank.Write(b + 81, (ushort)SystemInsulationValue);

        bank.Write(b + 8,  (ushort)SystemState);
        bank.Write(b + 87, (ushort)ChargeProhibitedSign);
        bank.Write(b + 88, (ushort)DischargeProhibitedSign);

        bank.Write(b + 94,  Alarm1);
        bank.Write(b + 95,  Alarm2);
        bank.Write(b + 96,  Alarm3);
        bank.Write(b + 97,  Alarm4);
        bank.Write(b + 98,  Fault1);
        bank.Write(b + 99,  Fault2);
        bank.Write(b + 100, Fault3);
        bank.Write(b + 101, Fault4);
        bank.Write(b + 102, Fault5);
        bank.Write(b + 103, Fault6);
        bank.Write(b + 104, Fault7);
    }

    public override void FromRegisters(RegisterBank bank)
    {
        int b = BaseAddress;

        InsideAddedTotalVolt      = Math.Round(bank.ReadFloat32(b + 0)  * 0.1,   2);
        CombinedCurrent           = Math.Round(bank.ReadFloat32(b + 1)  * 0.1,   2);
        Soc                       = Math.Round(bank.ReadFloat32(b + 2)  * 0.1,   2);
        Soh                       = Math.Round(bank.ReadFloat32(b + 4)  * 0.1,   2);
        AllowableChargingCurrent  = Math.Round(bank.ReadFloat32(b + 6)  * 0.1,   2);
        AllowableDischargeCurrent = Math.Round(bank.ReadFloat32(b + 7)  * 0.1,   2);
        MaxCellVolt               = Math.Round(bank.ReadFloat32(b + 19) * 0.001, 3);
        MinCellVolt               = Math.Round(bank.ReadFloat32(b + 22) * 0.001, 3);
        SystemVoltDifferential    = Math.Round(bank.ReadFloat32(b + 25) * 0.001, 3);

        MaxCellTemp           = (int)bank.Read(b + 30);
        MinCellTemp           = (int)bank.Read(b + 34);
        SystemTempDifference  = bank.Read(b + 38);
        SystemInsulationValue = bank.Read(b + 81);

        SystemState             = bank.Read(b + 8);
        ChargeProhibitedSign    = bank.Read(b + 87);
        DischargeProhibitedSign = bank.Read(b + 88);

        Alarm1 = bank.Read(b + 94);
        Alarm2 = bank.Read(b + 95);
        Alarm3 = bank.Read(b + 96);
        Alarm4 = bank.Read(b + 97);
        Fault1 = bank.Read(b + 98);
        Fault2 = bank.Read(b + 99);
        Fault3 = bank.Read(b + 100);
        Fault4 = bank.Read(b + 101);
        Fault5 = bank.Read(b + 102);
        Fault6 = bank.Read(b + 103);
        Fault7 = bank.Read(b + 104);
    }
}
