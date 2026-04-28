using SimulatorApp.Shared.Helpers;
using SimulatorApp.Shared.Services;

namespace SimulatorApp.Slave.Models.Pcs;

/// <summary>
/// PCS 储能变流器数据模型（基地址 7296）。
/// 字段定义参考：GS215 EMS对外 Modbus通讯协议0225，第 8.1 节。
/// 物理值 = 寄存器值 × 比例系数。
/// </summary>
public class PcsModel : DeviceModelBase
{
    public override string DeviceName => "PCS 储能变流器";
    public override int BaseAddress    => 7296;
    public override int RegisterCount  => 345;

    // ----------------------------------------------------------------
    // 遥测字段（物理值，界面直接显示）
    // ----------------------------------------------------------------

    /// <summary>直流电压 [V]，比例 ×0.1，偏移 +170</summary>
    public double DcVoltage { get; set; } = 750.0;
    /// <summary>直流电流 [A]，比例 ×-0.1，偏移 +171</summary>
    public double DcCurrent { get; set; } = 0.0;
    /// <summary>直流功率 [kW]，比例 ×-0.1，偏移 +174</summary>
    public double DcPower { get; set; } = 0.0;

    /// <summary>逆变A相电压 [V]，比例 ×0.1，偏移 +175</summary>
    public double PcsPhaseAVolt { get; set; } = 220.0;
    /// <summary>逆变B相电压 [V]，比例 ×0.1，偏移 +176</summary>
    public double PcsPhaseBVolt { get; set; } = 220.0;
    /// <summary>逆变C相电压 [V]，比例 ×0.1，偏移 +177</summary>
    public double PcsPhaseCVolt { get; set; } = 220.0;

    /// <summary>电网A相电压 [V]，比例 ×0.1，偏移 +178</summary>
    public double GridPhaseAVolt { get; set; } = 220.0;
    /// <summary>电网B相电压 [V]，比例 ×0.1，偏移 +179</summary>
    public double GridPhaseBVolt { get; set; } = 220.0;
    /// <summary>电网C相电压 [V]，比例 ×0.1，偏移 +180</summary>
    public double GridPhaseCVolt { get; set; } = 220.0;

    /// <summary>电网A相电流 [A]，比例 ×0.1，偏移 +187</summary>
    public double GridPhaseACurrent { get; set; } = 0.0;
    /// <summary>电网B相电流 [A]，比例 ×0.1，偏移 +188</summary>
    public double GridPhaseBCurrent { get; set; } = 0.0;
    /// <summary>电网C相电流 [A]，比例 ×0.1，偏移 +189</summary>
    public double GridPhaseCCurrent { get; set; } = 0.0;

    /// <summary>电网频率 [Hz]，比例 ×0.01，偏移 +190</summary>
    public double GridFrequency { get; set; } = 50.00;
    /// <summary>电网功率因数，比例 ×0.001，偏移 +191</summary>
    public double GridPowerFactor { get; set; } = 1.000;

    /// <summary>逆变器总有功功率 [kW]，比例 ×-0.001，偏移 +200</summary>
    public double PcsTotalActPower { get; set; } = 0.0;
    /// <summary>逆变器A相有功 [kW]，比例 ×-0.001，偏移 +201</summary>
    public double PcsPhaseAActPower { get; set; } = 0.0;
    /// <summary>逆变器B相有功 [kW]，比例 ×-0.001，偏移 +202</summary>
    public double PcsPhaseBActPower { get; set; } = 0.0;
    /// <summary>逆变器C相有功 [kW]，比例 ×-0.001，偏移 +203</summary>
    public double PcsPhaseCActPower { get; set; } = 0.0;
    /// <summary>逆变器总无功功率 [kW]，比例 ×-0.001，偏移 +212</summary>
    public double PcsTotalReactPower { get; set; } = 0.0;

    /// <summary>温度1 [℃]，比例 ×0.1，偏移 +263</summary>
    public double PcsTemp1 { get; set; } = 25.0;
    /// <summary>温度2 [℃]，比例 ×0.1，偏移 +264</summary>
    public double PcsTemp2 { get; set; } = 25.0;

    /// <summary>日充电量 [kWh]，比例 ×0.1，偏移 +245</summary>
    public double DailyChargedEnergy { get; set; } = 0.0;
    /// <summary>日放电量 [kWh]，比例 ×0.1，偏移 +246</summary>
    public double DailyDischargedEnergy { get; set; } = 0.0;

    // ----------------------------------------------------------------
    // 运行状态（枚举，ComboBox 选择）
    // ----------------------------------------------------------------

    /// <summary>充放电状态，偏移 +232：0=静置, 1=充电, 2=放电</summary>
    public int ChargingState { get; set; } = 0;
    /// <summary>运行状态，偏移 +233：0=待机, 1=自检, 2=运行, 3=告警, 4=故障</summary>
    public int OperatingState { get; set; } = 0;

    // ----------------------------------------------------------------
    // 故障/告警字（bitmask，CheckBox 多选 OR 合并）
    // ----------------------------------------------------------------

    /// <summary>告警字1，偏移 +224</summary>
    public ushort Alarm1 { get; set; }
    /// <summary>告警字2，偏移 +225</summary>
    public ushort Alarm2 { get; set; }
    /// <summary>告警字3，偏移 +226</summary>
    public ushort Alarm3 { get; set; }
    /// <summary>告警字4，偏移 +227</summary>
    public ushort Alarm4 { get; set; }
    /// <summary>故障字1，偏移 +228</summary>
    public ushort Fault1 { get; set; }
    /// <summary>故障字2，偏移 +229</summary>
    public ushort Fault2 { get; set; }
    /// <summary>故障字3，偏移 +230</summary>
    public ushort Fault3 { get; set; }
    /// <summary>故障字4，偏移 +231</summary>
    public ushort Fault4 { get; set; }

    // ----------------------------------------------------------------
    // ToRegisters：物理值 → 寄存器（写入 RegisterBank）
    // ----------------------------------------------------------------

    public override void ToRegisters(RegisterBank bank)
    {
        int b = BaseAddress;

        // 遥测（float32，AB CD，物理值除以比例系数得到寄存器值）
        bank.WriteFloat32(b + 170, (float)(DcVoltage   / 0.1));
        bank.WriteFloat32(b + 171, (float)(DcCurrent   / 0.1));  // 注意比例为 -0.1，负号在显示层处理
        bank.WriteFloat32(b + 174, (float)(DcPower     / 0.1));

        bank.WriteFloat32(b + 175, (float)(PcsPhaseAVolt / 0.1));
        bank.WriteFloat32(b + 176, (float)(PcsPhaseBVolt / 0.1));
        bank.WriteFloat32(b + 177, (float)(PcsPhaseCVolt / 0.1));

        bank.WriteFloat32(b + 178, (float)(GridPhaseAVolt / 0.1));
        bank.WriteFloat32(b + 179, (float)(GridPhaseBVolt / 0.1));
        bank.WriteFloat32(b + 180, (float)(GridPhaseCVolt / 0.1));

        bank.WriteFloat32(b + 187, (float)(GridPhaseACurrent / 0.1));
        bank.WriteFloat32(b + 188, (float)(GridPhaseBCurrent / 0.1));
        bank.WriteFloat32(b + 189, (float)(GridPhaseCCurrent / 0.1));

        bank.WriteFloat32(b + 190, (float)(GridFrequency   / 0.01));
        bank.WriteFloat32(b + 191, (float)(GridPowerFactor / 0.001));

        bank.WriteFloat32(b + 200, (float)(PcsTotalActPower   / 0.001));
        bank.WriteFloat32(b + 201, (float)(PcsPhaseAActPower  / 0.001));
        bank.WriteFloat32(b + 202, (float)(PcsPhaseBActPower  / 0.001));
        bank.WriteFloat32(b + 203, (float)(PcsPhaseCActPower  / 0.001));
        bank.WriteFloat32(b + 212, (float)(PcsTotalReactPower / 0.001));

        bank.WriteFloat32(b + 245, (float)(DailyChargedEnergy    / 0.1));
        bank.WriteFloat32(b + 246, (float)(DailyDischargedEnergy / 0.1));
        bank.WriteFloat32(b + 263, (float)(PcsTemp1 / 0.1));
        bank.WriteFloat32(b + 264, (float)(PcsTemp2 / 0.1));

        // 状态
        bank.Write(b + 232, (ushort)ChargingState);
        bank.Write(b + 233, (ushort)OperatingState);

        // 告警/故障
        bank.Write(b + 224, Alarm1);
        bank.Write(b + 225, Alarm2);
        bank.Write(b + 226, Alarm3);
        bank.Write(b + 227, Alarm4);
        bank.Write(b + 228, Fault1);
        bank.Write(b + 229, Fault2);
        bank.Write(b + 230, Fault3);
        bank.Write(b + 231, Fault4);
    }

    // ----------------------------------------------------------------
    // FromRegisters：寄存器 → 物理值（导入配置时用）
    // ----------------------------------------------------------------

    public override void FromRegisters(RegisterBank bank)
    {
        int b = BaseAddress;

        DcVoltage   = Math.Round(bank.ReadFloat32(b + 170) * 0.1,  2);
        DcCurrent   = Math.Round(bank.ReadFloat32(b + 171) * 0.1,  2);
        DcPower     = Math.Round(bank.ReadFloat32(b + 174) * 0.1,  2);

        PcsPhaseAVolt = Math.Round(bank.ReadFloat32(b + 175) * 0.1, 2);
        PcsPhaseBVolt = Math.Round(bank.ReadFloat32(b + 176) * 0.1, 2);
        PcsPhaseCVolt = Math.Round(bank.ReadFloat32(b + 177) * 0.1, 2);

        GridPhaseAVolt = Math.Round(bank.ReadFloat32(b + 178) * 0.1, 2);
        GridPhaseBVolt = Math.Round(bank.ReadFloat32(b + 179) * 0.1, 2);
        GridPhaseCVolt = Math.Round(bank.ReadFloat32(b + 180) * 0.1, 2);

        GridPhaseACurrent = Math.Round(bank.ReadFloat32(b + 187) * 0.1, 2);
        GridPhaseBCurrent = Math.Round(bank.ReadFloat32(b + 188) * 0.1, 2);
        GridPhaseCCurrent = Math.Round(bank.ReadFloat32(b + 189) * 0.1, 2);

        GridFrequency   = Math.Round(bank.ReadFloat32(b + 190) * 0.01,  3);
        GridPowerFactor = Math.Round(bank.ReadFloat32(b + 191) * 0.001, 3);

        PcsTotalActPower   = Math.Round(bank.ReadFloat32(b + 200) * 0.001, 3);
        PcsPhaseAActPower  = Math.Round(bank.ReadFloat32(b + 201) * 0.001, 3);
        PcsPhaseBActPower  = Math.Round(bank.ReadFloat32(b + 202) * 0.001, 3);
        PcsPhaseCActPower  = Math.Round(bank.ReadFloat32(b + 203) * 0.001, 3);
        PcsTotalReactPower = Math.Round(bank.ReadFloat32(b + 212) * 0.001, 3);

        DailyChargedEnergy    = Math.Round(bank.ReadFloat32(b + 245) * 0.1, 2);
        DailyDischargedEnergy = Math.Round(bank.ReadFloat32(b + 246) * 0.1, 2);
        PcsTemp1 = Math.Round(bank.ReadFloat32(b + 263) * 0.1, 2);
        PcsTemp2 = Math.Round(bank.ReadFloat32(b + 264) * 0.1, 2);

        ChargingState  = bank.Read(b + 232);
        OperatingState = bank.Read(b + 233);

        Alarm1 = bank.Read(b + 224);
        Alarm2 = bank.Read(b + 225);
        Alarm3 = bank.Read(b + 226);
        Alarm4 = bank.Read(b + 227);
        Fault1 = bank.Read(b + 228);
        Fault2 = bank.Read(b + 229);
        Fault3 = bank.Read(b + 230);
        Fault4 = bank.Read(b + 231);
    }
}
