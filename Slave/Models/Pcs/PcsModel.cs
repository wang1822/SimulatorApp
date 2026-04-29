using SimulatorApp.Shared.Services;

namespace SimulatorApp.Slave.Models.Pcs;

/// <summary>
/// PCS 储能变流器数据模型，依据 PCS_Modbus协议V102.xlsx 的 3000-3136 遥测区。
/// 32 位量按协议约定低 16 位在低地址，高 16 位在高地址。
/// </summary>
public class PcsModel : DeviceModelBase
{
    public override string DeviceName => "GS215PCS";
    public override int BaseAddress => 3000;
    public override int RegisterCount => 137;

    public double DcVoltage { get; set; } = 750.0;
    public double DcCurrent { get; set; }
    public double DcPower { get; set; }

    public double PcsPhaseAVolt { get; set; } = 220.0;
    public double PcsPhaseBVolt { get; set; } = 220.0;
    public double PcsPhaseCVolt { get; set; } = 220.0;
    public double GridPhaseAVolt { get; set; } = 220.0;
    public double GridPhaseBVolt { get; set; } = 220.0;
    public double GridPhaseCVolt { get; set; } = 220.0;
    public double GridPhaseACurrent { get; set; }
    public double GridPhaseBCurrent { get; set; }
    public double GridPhaseCCurrent { get; set; }
    public double GridFrequency { get; set; } = 50.00;
    public double GridPowerFactor { get; set; } = 1.000;

    public double PcsTotalActPower { get; set; }
    public double PcsPhaseAActPower { get; set; }
    public double PcsPhaseBActPower { get; set; }
    public double PcsPhaseCActPower { get; set; }
    public double PcsTotalReactPower { get; set; }
    public double PcsPhaseAReactPower { get; set; }
    public double PcsPhaseBReactPower { get; set; }
    public double PcsPhaseCReactPower { get; set; }

    public double GridSideTotalActPower { get; set; }
    public double GridSidePhaseAActPower { get; set; }
    public double GridSidePhaseBActPower { get; set; }
    public double GridSidePhaseCActPower { get; set; }

    public ushort Alarm1 { get; set; }
    public ushort Alarm2 { get; set; }
    public ushort Alarm3 { get; set; }
    public ushort Alarm4 { get; set; }
    public ushort Fault1 { get; set; }
    public ushort Fault2 { get; set; }
    public ushort Fault3 { get; set; }
    public ushort Fault4 { get; set; }

    public int ChargingState { get; set; } = 2;
    public int OperatingState { get; set; }
    public int OperatingMode { get; set; }
    public ushort RelayStatus { get; set; }
    public int SelfCheckingCountdown { get; set; }

    public double DailyChargedEnergy { get; set; }
    public double DailyDischargedEnergy { get; set; }
    public double CumulativeChargedEnergy { get; set; }
    public double CumulativeDischargedEnergy { get; set; }
    public double DailyPurchasedEnergy { get; set; }
    public double DailySoldEnergy { get; set; }
    public double CumulativePurchasedEnergy { get; set; }
    public double CumulativeSoldEnergy { get; set; }

    public double PcsTemp1 { get; set; } = 25.0;
    public double PcsTemp2 { get; set; } = 25.0;
    public double DailyChargingTime { get; set; }
    public double DailyDischargingTime { get; set; }
    public double CumulativeChargingTime { get; set; }
    public double CumulativeDischargingTime { get; set; }

    public ushort ExportLimitMode { get; set; }
    public double ExportLimitTotalActPower { get; set; }
    public double ExportLimitPhaseAActPower { get; set; }
    public double ExportLimitPhaseBActPower { get; set; }
    public double ExportLimitPhaseCActPower { get; set; }
    public double ExportLimitTotalReactPower { get; set; }
    public double ExportLimitPhaseAReactPower { get; set; }
    public double ExportLimitPhaseBReactPower { get; set; }
    public double ExportLimitPhaseCReactPower { get; set; }

    public double LoadTotalActPower { get; set; }
    public double LoadPhaseAActPower { get; set; }
    public double LoadPhaseBActPower { get; set; }
    public double LoadPhaseCActPower { get; set; }
    public double LoadTotalReactPower { get; set; }
    public double LoadPhaseAReactPower { get; set; }
    public double LoadPhaseBReactPower { get; set; }
    public double LoadPhaseCReactPower { get; set; }

    public double MeterSoldEnergy { get; set; }
    public double MeterPurchasedEnergy { get; set; }

    public override void ToRegisters(RegisterBank bank)
    {
        bank.Write(3000, 0x0600);
        bank.Write(3002, 0x0104);

        WriteS16(bank, 3015, DcVoltage, 0.1);
        WriteS16(bank, 3016, DcCurrent, 0.1);
        WriteS32(bank, 3017, DcPower, 0.1);
        WriteU16(bank, 3019, PcsPhaseAVolt, 0.1);
        WriteU16(bank, 3020, PcsPhaseBVolt, 0.1);
        WriteU16(bank, 3021, PcsPhaseCVolt, 0.1);
        WriteU16(bank, 3022, GridPhaseAVolt, 0.1);
        WriteU16(bank, 3023, GridPhaseBVolt, 0.1);
        WriteU16(bank, 3024, GridPhaseCVolt, 0.1);
        WriteS16(bank, 3028, GridPhaseACurrent, 0.1);
        WriteS16(bank, 3029, GridPhaseBCurrent, 0.1);
        WriteS16(bank, 3030, GridPhaseCCurrent, 0.1);
        WriteU16(bank, 3031, GridFrequency, 0.01);
        WriteU16(bank, 3032, GridPowerFactor, 0.001);

        WriteS32(bank, 3033, PcsTotalActPower, 0.001);
        WriteS32(bank, 3035, PcsPhaseAActPower, 0.001);
        WriteS32(bank, 3037, PcsPhaseBActPower, 0.001);
        WriteS32(bank, 3039, PcsPhaseCActPower, 0.001);
        WriteS32(bank, 3041, PcsTotalReactPower, 0.001);
        WriteS32(bank, 3043, PcsPhaseAReactPower, 0.001);
        WriteS32(bank, 3045, PcsPhaseBReactPower, 0.001);
        WriteS32(bank, 3047, PcsPhaseCReactPower, 0.001);
        WriteS32(bank, 3049, GridSideTotalActPower, 0.001);
        WriteS32(bank, 3051, GridSidePhaseAActPower, 0.001);
        WriteS32(bank, 3053, GridSidePhaseBActPower, 0.001);
        WriteS32(bank, 3055, GridSidePhaseCActPower, 0.001);

        bank.Write(3057, Alarm1);
        bank.Write(3058, Alarm2);
        bank.Write(3059, Alarm3);
        bank.Write(3060, Alarm4);
        bank.Write(3061, Fault1);
        bank.Write(3062, Fault2);
        bank.Write(3063, Fault3);
        bank.Write(3064, Fault4);
        bank.Write(3065, (ushort)ChargingState);
        bank.Write(3066, (ushort)OperatingState);
        bank.Write(3067, (ushort)OperatingMode);
        bank.Write(3068, RelayStatus);
        bank.Write(3069, (ushort)SelfCheckingCountdown);

        WriteU16(bank, 3070, DailyChargedEnergy, 0.1);
        WriteU16(bank, 3071, DailyDischargedEnergy, 0.1);
        WriteU32(bank, 3072, CumulativeChargedEnergy, 0.1);
        WriteU32(bank, 3074, CumulativeDischargedEnergy, 0.1);
        WriteU16(bank, 3076, DailyPurchasedEnergy, 0.1);
        WriteU16(bank, 3077, DailySoldEnergy, 0.1);
        WriteU32(bank, 3078, CumulativePurchasedEnergy, 0.1);
        WriteU32(bank, 3080, CumulativeSoldEnergy, 0.1);
        WriteU16(bank, 3090, PcsTemp1, 0.1);
        WriteU16(bank, 3091, PcsTemp2, 0.1);
        WriteU16(bank, 3092, DailyChargingTime, 10.0);
        WriteU16(bank, 3093, DailyDischargingTime, 10.0);
        WriteU32(bank, 3094, CumulativeChargingTime, 10.0);
        WriteU32(bank, 3096, CumulativeDischargingTime, 10.0);

        bank.Write(3100, ExportLimitMode);
        WriteS32(bank, 3101, ExportLimitTotalActPower, 0.001);
        WriteS32(bank, 3103, ExportLimitPhaseAActPower, 0.001);
        WriteS32(bank, 3105, ExportLimitPhaseBActPower, 0.001);
        WriteS32(bank, 3107, ExportLimitPhaseCActPower, 0.001);
        WriteS32(bank, 3109, ExportLimitTotalReactPower, 0.001);
        WriteS32(bank, 3111, ExportLimitPhaseAReactPower, 0.001);
        WriteS32(bank, 3113, ExportLimitPhaseBReactPower, 0.001);
        WriteS32(bank, 3115, ExportLimitPhaseCReactPower, 0.001);
        WriteS32(bank, 3117, LoadTotalActPower, 0.001);
        WriteS32(bank, 3119, LoadPhaseAActPower, 0.001);
        WriteS32(bank, 3121, LoadPhaseBActPower, 0.001);
        WriteS32(bank, 3123, LoadPhaseCActPower, 0.001);
        WriteS32(bank, 3125, LoadTotalReactPower, 0.001);
        WriteS32(bank, 3127, LoadPhaseAReactPower, 0.001);
        WriteS32(bank, 3129, LoadPhaseBReactPower, 0.001);
        WriteS32(bank, 3131, LoadPhaseCReactPower, 0.001);
        WriteU32(bank, 3133, MeterSoldEnergy, 0.01);
        WriteU32(bank, 3135, MeterPurchasedEnergy, 0.01);
    }

    public override void FromRegisters(RegisterBank bank)
    {
        DcVoltage = ReadS16(bank, 3015, 0.1);
        DcCurrent = ReadS16(bank, 3016, 0.1);
        DcPower = ReadS32(bank, 3017, 0.1);
        PcsPhaseAVolt = ReadU16(bank, 3019, 0.1);
        PcsPhaseBVolt = ReadU16(bank, 3020, 0.1);
        PcsPhaseCVolt = ReadU16(bank, 3021, 0.1);
        GridPhaseAVolt = ReadU16(bank, 3022, 0.1);
        GridPhaseBVolt = ReadU16(bank, 3023, 0.1);
        GridPhaseCVolt = ReadU16(bank, 3024, 0.1);
        GridPhaseACurrent = ReadS16(bank, 3028, 0.1);
        GridPhaseBCurrent = ReadS16(bank, 3029, 0.1);
        GridPhaseCCurrent = ReadS16(bank, 3030, 0.1);
        GridFrequency = ReadU16(bank, 3031, 0.01);
        GridPowerFactor = ReadU16(bank, 3032, 0.001);
        PcsTotalActPower = ReadS32(bank, 3033, 0.001);
        PcsPhaseAActPower = ReadS32(bank, 3035, 0.001);
        PcsPhaseBActPower = ReadS32(bank, 3037, 0.001);
        PcsPhaseCActPower = ReadS32(bank, 3039, 0.001);
        PcsTotalReactPower = ReadS32(bank, 3041, 0.001);
        PcsPhaseAReactPower = ReadS32(bank, 3043, 0.001);
        PcsPhaseBReactPower = ReadS32(bank, 3045, 0.001);
        PcsPhaseCReactPower = ReadS32(bank, 3047, 0.001);
        GridSideTotalActPower = ReadS32(bank, 3049, 0.001);
        GridSidePhaseAActPower = ReadS32(bank, 3051, 0.001);
        GridSidePhaseBActPower = ReadS32(bank, 3053, 0.001);
        GridSidePhaseCActPower = ReadS32(bank, 3055, 0.001);
        Alarm1 = bank.Read(3057);
        Alarm2 = bank.Read(3058);
        Alarm3 = bank.Read(3059);
        Alarm4 = bank.Read(3060);
        Fault1 = bank.Read(3061);
        Fault2 = bank.Read(3062);
        Fault3 = bank.Read(3063);
        Fault4 = bank.Read(3064);
        ChargingState = bank.Read(3065);
        OperatingState = bank.Read(3066);
        OperatingMode = bank.Read(3067);
        RelayStatus = bank.Read(3068);
        SelfCheckingCountdown = bank.Read(3069);
        DailyChargedEnergy = ReadU16(bank, 3070, 0.1);
        DailyDischargedEnergy = ReadU16(bank, 3071, 0.1);
        CumulativeChargedEnergy = ReadU32(bank, 3072, 0.1);
        CumulativeDischargedEnergy = ReadU32(bank, 3074, 0.1);
        DailyPurchasedEnergy = ReadU16(bank, 3076, 0.1);
        DailySoldEnergy = ReadU16(bank, 3077, 0.1);
        CumulativePurchasedEnergy = ReadU32(bank, 3078, 0.1);
        CumulativeSoldEnergy = ReadU32(bank, 3080, 0.1);
        PcsTemp1 = ReadU16(bank, 3090, 0.1);
        PcsTemp2 = ReadU16(bank, 3091, 0.1);
        DailyChargingTime = ReadU16(bank, 3092, 10.0);
        DailyDischargingTime = ReadU16(bank, 3093, 10.0);
        CumulativeChargingTime = ReadU32(bank, 3094, 10.0);
        CumulativeDischargingTime = ReadU32(bank, 3096, 10.0);
        ExportLimitMode = bank.Read(3100);
        ExportLimitTotalActPower = ReadS32(bank, 3101, 0.001);
        ExportLimitPhaseAActPower = ReadS32(bank, 3103, 0.001);
        ExportLimitPhaseBActPower = ReadS32(bank, 3105, 0.001);
        ExportLimitPhaseCActPower = ReadS32(bank, 3107, 0.001);
        ExportLimitTotalReactPower = ReadS32(bank, 3109, 0.001);
        ExportLimitPhaseAReactPower = ReadS32(bank, 3111, 0.001);
        ExportLimitPhaseBReactPower = ReadS32(bank, 3113, 0.001);
        ExportLimitPhaseCReactPower = ReadS32(bank, 3115, 0.001);
        LoadTotalActPower = ReadS32(bank, 3117, 0.001);
        LoadPhaseAActPower = ReadS32(bank, 3119, 0.001);
        LoadPhaseBActPower = ReadS32(bank, 3121, 0.001);
        LoadPhaseCActPower = ReadS32(bank, 3123, 0.001);
        LoadTotalReactPower = ReadS32(bank, 3125, 0.001);
        LoadPhaseAReactPower = ReadS32(bank, 3127, 0.001);
        LoadPhaseBReactPower = ReadS32(bank, 3129, 0.001);
        LoadPhaseCReactPower = ReadS32(bank, 3131, 0.001);
        MeterSoldEnergy = ReadU32(bank, 3133, 0.01);
        MeterPurchasedEnergy = ReadU32(bank, 3135, 0.01);
    }

    private static void WriteU16(RegisterBank bank, int address, double value, double scale)
        => bank.Write(address, (ushort)Math.Clamp((int)Math.Round(value / scale), 0, ushort.MaxValue));

    private static void WriteS16(RegisterBank bank, int address, double value, double scale)
        => bank.WriteInt16(address, (short)Math.Clamp((int)Math.Round(value / scale), short.MinValue, short.MaxValue));

    private static void WriteU32(RegisterBank bank, int address, double value, double scale)
    {
        uint raw = (uint)Math.Clamp(Math.Round(value / scale), 0, uint.MaxValue);
        bank.Write(address, (ushort)(raw & 0xFFFF));
        bank.Write(address + 1, (ushort)(raw >> 16));
    }

    private static void WriteS32(RegisterBank bank, int address, double value, double scale)
    {
        int raw = (int)Math.Clamp(Math.Round(value / scale), int.MinValue, int.MaxValue);
        bank.Write(address, (ushort)(raw & 0xFFFF));
        bank.Write(address + 1, (ushort)((uint)raw >> 16));
    }

    private static double ReadU16(RegisterBank bank, int address, double scale)
        => Math.Round(bank.Read(address) * scale, 3);

    private static double ReadS16(RegisterBank bank, int address, double scale)
        => Math.Round((short)bank.Read(address) * scale, 3);

    private static double ReadU32(RegisterBank bank, int address, double scale)
    {
        uint raw = (uint)(bank.Read(address) | (bank.Read(address + 1) << 16));
        return Math.Round(raw * scale, 3);
    }

    private static double ReadS32(RegisterBank bank, int address, double scale)
    {
        int raw = bank.Read(address) | (bank.Read(address + 1) << 16);
        return Math.Round(raw * scale, 3);
    }
}
