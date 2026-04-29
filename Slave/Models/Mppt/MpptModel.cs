using SimulatorApp.Shared.Services;

namespace SimulatorApp.Slave.Models.Mppt;

/// <summary>
/// MPPT 光伏最大功率点跟踪器数据模型，依据 MPPT_Modbus_V1.0.xlsx 的 4000-4209 寄存器表。
/// </summary>
public class MpptModel : DeviceModelBase
{
    public override string DeviceName => "GS215MPPT";
    public override int BaseAddress => 4000;
    public override int RegisterCount => 210;

    public int MpptCount { get; set; } = 8;
    public double MaxPvPowerPercent { get; set; } = 110.0;
    public double MaxMpptCurrent { get; set; } = 40.0;
    public int PowerEnable { get; set; } = 1;
    public int FactoryResetEnable { get; set; }
    public int SelfCheckTime { get; set; }
    public int RisoEnable { get; set; } = 1;
    public int SolarArcFaultMode { get; set; }
    public double PvEnergyCorrectionPercent { get; set; } = 100.0;
    public int TestMode { get; set; }
    public ushort MpptFunction { get; set; }

    public int MpptOperatingMode { get; set; } = 2;
    public double OutputVolt { get; set; } = 600.0;
    public double OutputCurrent { get; set; }
    public double OutputPower { get; set; }
    public double PVTotalPower { get; set; }
    public double DCVolt1 { get; set; } = 400.0;
    public double DCCurrent1 { get; set; }
    public double DCPower1 { get; set; }
    public double DCVolt2 { get; set; } = 400.0;
    public double DCCurrent2 { get; set; }
    public double DCPower2 { get; set; }
    public double DCVolt3 { get; set; } = 400.0;
    public double DCCurrent3 { get; set; }
    public double DCPower3 { get; set; }
    public double DCVolt4 { get; set; } = 400.0;
    public double DCCurrent4 { get; set; }
    public double DCPower4 { get; set; }
    public double DCVolt5 { get; set; } = 400.0;
    public double DCCurrent5 { get; set; }
    public double DCPower5 { get; set; }
    public double DCVolt6 { get; set; } = 400.0;
    public double DCCurrent6 { get; set; }
    public double DCPower6 { get; set; }
    public double DCVolt7 { get; set; } = 400.0;
    public double DCCurrent7 { get; set; }
    public double DCPower7 { get; set; }
    public double DCVolt8 { get; set; } = 400.0;
    public double DCCurrent8 { get; set; }
    public double DCPower8 { get; set; }
    public double RelayAfterVoltage { get; set; } = 600.0;

    public double DailyPVGen1 { get; set; }
    public double DailyPVGen2 { get; set; }
    public double DailyPVGen3 { get; set; }
    public double DailyPVGen4 { get; set; }
    public double DailyPVGen5 { get; set; }
    public double DailyPVGen6 { get; set; }
    public double DailyPVGen7 { get; set; }
    public double DailyPVGen8 { get; set; }
    public double DailyPVGenTotal { get; set; }
    public double HistoryPvGenCapacity { get; set; }
    public double HeatSinkTemp { get; set; } = 25.0;
    public int TestState { get; set; }
    public ushort InternalFanAd { get; set; }
    public ushort ExternalFanAd { get; set; }
    public ushort TestData1 { get; set; }
    public ushort TestData2 { get; set; }
    public ushort TestData3 { get; set; }
    public ushort TestData4 { get; set; }
    public ushort TestData5 { get; set; }
    public ushort TestData6 { get; set; }
    public ushort TestData7 { get; set; }
    public ushort TestData8 { get; set; }
    public ushort TestData9 { get; set; }

    public ushort MPPTAlarm1 { get; set; }
    public ushort MPPTAlarm2 { get; set; }
    public ushort MPPTFault1 { get; set; }
    public ushort MPPTFault2 { get; set; }
    public ushort MPPTFault3 { get; set; }
    public ushort MPPTFault4 { get; set; }

    public override void ToRegisters(RegisterBank bank)
    {
        int b = BaseAddress;

        bank.Write(b + 0, 0x0803);
        bank.Write(b + 1, SlaveId);
        bank.Write(b + 2, 0x0100);
        bank.Write(b + 22, (ushort)Math.Clamp(MpptCount, 1, 8));

        WriteU16(bank, b + 35, MaxPvPowerPercent, 0.1);
        WriteU16(bank, b + 36, MaxMpptCurrent, 0.1);
        bank.Write(b + 39, (ushort)Math.Clamp(PowerEnable, 0, 3));
        bank.Write(b + 40, (ushort)Math.Clamp(FactoryResetEnable, 0, 3));
        bank.Write(b + 41, (ushort)Math.Clamp(SelfCheckTime, 0, 1000));
        bank.Write(b + 42, (ushort)Math.Clamp(RisoEnable, 0, 1));
        bank.Write(b + 43, (ushort)Math.Clamp(SolarArcFaultMode, 0, 2));
        WriteU16(bank, b + 44, PvEnergyCorrectionPercent, 1.0);
        bank.Write(b + 45, (ushort)Math.Clamp(TestMode, 0, ushort.MaxValue));
        bank.Write(b + 46, MpptFunction);

        bank.Write(b + 150, (ushort)Math.Clamp(MpptOperatingMode, 0, 5));
        WriteU16(bank, b + 151, OutputVolt, 0.1);
        WriteU16(bank, b + 152, OutputCurrent, 0.1);
        WriteU16(bank, b + 153, OutputPower, 10.0);
        WriteU16(bank, b + 154, PVTotalPower, 10.0);
        WriteU16(bank, b + 155, DCVolt1, 0.1);
        WriteU16(bank, b + 156, DCCurrent1, 0.1);
        WriteU16(bank, b + 157, DCPower1, 1.0);
        WriteU16(bank, b + 158, DCVolt2, 0.1);
        WriteU16(bank, b + 159, DCCurrent2, 0.1);
        WriteU16(bank, b + 160, DCPower2, 1.0);
        WriteU16(bank, b + 161, DCVolt3, 0.1);
        WriteU16(bank, b + 162, DCCurrent3, 0.1);
        WriteU16(bank, b + 163, DCPower3, 1.0);
        WriteU16(bank, b + 164, DCVolt4, 0.1);
        WriteU16(bank, b + 165, DCCurrent4, 0.1);
        WriteU16(bank, b + 166, DCPower4, 1.0);
        WriteU16(bank, b + 167, DCVolt5, 0.1);
        WriteU16(bank, b + 168, DCCurrent5, 0.1);
        WriteU16(bank, b + 169, DCPower5, 1.0);
        WriteU16(bank, b + 170, DCVolt6, 0.1);
        WriteU16(bank, b + 171, DCCurrent6, 0.1);
        WriteU16(bank, b + 172, DCPower6, 1.0);
        WriteU16(bank, b + 173, DCVolt7, 0.1);
        WriteU16(bank, b + 174, DCCurrent7, 0.1);
        WriteU16(bank, b + 175, DCPower7, 1.0);
        WriteU16(bank, b + 176, DCVolt8, 0.1);
        WriteU16(bank, b + 177, DCCurrent8, 0.1);
        WriteU16(bank, b + 178, DCPower8, 1.0);
        WriteU16(bank, b + 179, RelayAfterVoltage, 0.1);

        WriteU16(bank, b + 180, DailyPVGen1, 0.1);
        WriteU16(bank, b + 181, DailyPVGen2, 0.1);
        WriteU16(bank, b + 182, DailyPVGen3, 0.1);
        WriteU16(bank, b + 183, DailyPVGen4, 0.1);
        WriteU16(bank, b + 184, DailyPVGen5, 0.1);
        WriteU16(bank, b + 185, DailyPVGen6, 0.1);
        WriteU16(bank, b + 186, DailyPVGen7, 0.1);
        WriteU16(bank, b + 187, DailyPVGen8, 0.1);
        WriteU16(bank, b + 188, DailyPVGenTotal, 0.1);
        WriteU32LowFirst(bank, b + 189, HistoryPvGenCapacity, 0.1);
        WriteU16(bank, b + 191, HeatSinkTemp + 100.0, 0.1);
        bank.Write(b + 192, (ushort)Math.Clamp(TestState, 0, ushort.MaxValue));
        bank.Write(b + 193, MPPTAlarm1);
        bank.Write(b + 194, MPPTAlarm2);
        bank.Write(b + 195, MPPTFault1);
        bank.Write(b + 196, MPPTFault2);
        bank.Write(b + 197, MPPTFault3);
        bank.Write(b + 198, MPPTFault4);
        bank.Write(b + 199, InternalFanAd);
        bank.Write(b + 200, ExternalFanAd);
        bank.Write(b + 201, TestData1);
        bank.Write(b + 202, TestData2);
        bank.Write(b + 203, TestData3);
        bank.Write(b + 204, TestData4);
        bank.Write(b + 205, TestData5);
        bank.Write(b + 206, TestData6);
        bank.Write(b + 207, TestData7);
        bank.Write(b + 208, TestData8);
        bank.Write(b + 209, TestData9);
    }

    public override void FromRegisters(RegisterBank bank)
    {
        int b = BaseAddress;

        MpptCount = bank.Read(b + 22);
        MaxPvPowerPercent = ReadU16(bank, b + 35, 0.1);
        MaxMpptCurrent = ReadU16(bank, b + 36, 0.1);
        PowerEnable = bank.Read(b + 39);
        FactoryResetEnable = bank.Read(b + 40);
        SelfCheckTime = bank.Read(b + 41);
        RisoEnable = bank.Read(b + 42);
        SolarArcFaultMode = bank.Read(b + 43);
        PvEnergyCorrectionPercent = ReadU16(bank, b + 44, 1.0);
        TestMode = bank.Read(b + 45);
        MpptFunction = bank.Read(b + 46);

        MpptOperatingMode = bank.Read(b + 150);
        OutputVolt = ReadU16(bank, b + 151, 0.1);
        OutputCurrent = ReadU16(bank, b + 152, 0.1);
        OutputPower = ReadU16(bank, b + 153, 10.0);
        PVTotalPower = ReadU16(bank, b + 154, 10.0);
        DCVolt1 = ReadU16(bank, b + 155, 0.1);
        DCCurrent1 = ReadU16(bank, b + 156, 0.1);
        DCPower1 = ReadU16(bank, b + 157, 1.0);
        DCVolt2 = ReadU16(bank, b + 158, 0.1);
        DCCurrent2 = ReadU16(bank, b + 159, 0.1);
        DCPower2 = ReadU16(bank, b + 160, 1.0);
        DCVolt3 = ReadU16(bank, b + 161, 0.1);
        DCCurrent3 = ReadU16(bank, b + 162, 0.1);
        DCPower3 = ReadU16(bank, b + 163, 1.0);
        DCVolt4 = ReadU16(bank, b + 164, 0.1);
        DCCurrent4 = ReadU16(bank, b + 165, 0.1);
        DCPower4 = ReadU16(bank, b + 166, 1.0);
        DCVolt5 = ReadU16(bank, b + 167, 0.1);
        DCCurrent5 = ReadU16(bank, b + 168, 0.1);
        DCPower5 = ReadU16(bank, b + 169, 1.0);
        DCVolt6 = ReadU16(bank, b + 170, 0.1);
        DCCurrent6 = ReadU16(bank, b + 171, 0.1);
        DCPower6 = ReadU16(bank, b + 172, 1.0);
        DCVolt7 = ReadU16(bank, b + 173, 0.1);
        DCCurrent7 = ReadU16(bank, b + 174, 0.1);
        DCPower7 = ReadU16(bank, b + 175, 1.0);
        DCVolt8 = ReadU16(bank, b + 176, 0.1);
        DCCurrent8 = ReadU16(bank, b + 177, 0.1);
        DCPower8 = ReadU16(bank, b + 178, 1.0);
        RelayAfterVoltage = ReadU16(bank, b + 179, 0.1);

        DailyPVGen1 = ReadU16(bank, b + 180, 0.1);
        DailyPVGen2 = ReadU16(bank, b + 181, 0.1);
        DailyPVGen3 = ReadU16(bank, b + 182, 0.1);
        DailyPVGen4 = ReadU16(bank, b + 183, 0.1);
        DailyPVGen5 = ReadU16(bank, b + 184, 0.1);
        DailyPVGen6 = ReadU16(bank, b + 185, 0.1);
        DailyPVGen7 = ReadU16(bank, b + 186, 0.1);
        DailyPVGen8 = ReadU16(bank, b + 187, 0.1);
        DailyPVGenTotal = ReadU16(bank, b + 188, 0.1);
        HistoryPvGenCapacity = ReadU32LowFirst(bank, b + 189, 0.1);
        HeatSinkTemp = ReadU16(bank, b + 191, 0.1) - 100.0;
        TestState = bank.Read(b + 192);
        MPPTAlarm1 = bank.Read(b + 193);
        MPPTAlarm2 = bank.Read(b + 194);
        MPPTFault1 = bank.Read(b + 195);
        MPPTFault2 = bank.Read(b + 196);
        MPPTFault3 = bank.Read(b + 197);
        MPPTFault4 = bank.Read(b + 198);
        InternalFanAd = bank.Read(b + 199);
        ExternalFanAd = bank.Read(b + 200);
        TestData1 = bank.Read(b + 201);
        TestData2 = bank.Read(b + 202);
        TestData3 = bank.Read(b + 203);
        TestData4 = bank.Read(b + 204);
        TestData5 = bank.Read(b + 205);
        TestData6 = bank.Read(b + 206);
        TestData7 = bank.Read(b + 207);
        TestData8 = bank.Read(b + 208);
        TestData9 = bank.Read(b + 209);
    }

    private static void WriteU16(RegisterBank bank, int address, double value, double scale)
        => bank.Write(address, (ushort)Math.Clamp((int)Math.Round(value / scale), 0, ushort.MaxValue));

    private static void WriteU32LowFirst(RegisterBank bank, int address, double value, double scale)
    {
        uint raw = (uint)Math.Clamp(Math.Round(value / scale), 0, uint.MaxValue);
        bank.Write(address, (ushort)(raw & 0xFFFF));
        bank.Write(address + 1, (ushort)(raw >> 16));
    }

    private static double ReadU16(RegisterBank bank, int address, double scale)
        => Math.Round(bank.Read(address) * scale, 3);

    private static double ReadU32LowFirst(RegisterBank bank, int address, double scale)
    {
        uint raw = (uint)(bank.Read(address) | (bank.Read(address + 1) << 16));
        return Math.Round(raw * scale, 3);
    }
}
