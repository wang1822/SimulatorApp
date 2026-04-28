using SimulatorApp.Shared.Services;

namespace SimulatorApp.Slave.Models.AirConditioner;

/// <summary>
/// 空调（机房精密空调）数据模型（基地址 52352）。
/// 字段定义参考设计文档第 8.6 节。
/// </summary>
public class AirConditionerModel : DeviceModelBase
{
    public override string DeviceName => "空调";
    public override int BaseAddress    => 52352;
    public override int RegisterCount  => 73;

    // 遥测
    public double AirOutTemp1         { get; set; } = 25.0;  // ×0.1 ℃，偏移 +6
    public double AirInterTemp1       { get; set; } = 22.0;  // ×0.1 ℃，偏移 +9
    public int    AirInterRh1         { get; set; } = 50;    // ×1 %RH，偏移 +11
    public double AirInterCoilTemp    { get; set; } = 18.0;  // ×0.1 ℃，偏移 +13
    public double AirInputCurrent     { get; set; } = 0.0;   // ×0.001 A，偏移 +14
    public int    AirACVoltage        { get; set; } = 220;   // ×1 V，偏移 +15
    public double AirCompressorCurrent{ get; set; } = 0.0;   // ×0.001 A，偏移 +17
    public int    AirExterFanSpeed    { get; set; } = 0;     // ×1 r/min，偏移 +18
    public int    AirInterFanSpeed    { get; set; } = 0;     // ×1 r/min，偏移 +19
    public int    AirCompressorFre    { get; set; } = 0;     // ×1 Hz，偏移 +20
    public int    AirCoolPoint        { get; set; } = 26;    // ×1 ℃，偏移 +23
    public int    AirHeatPoint        { get; set; } = 10;    // ×1 ℃（int16），偏移 +25
    public int    AirRHDiffPoint      { get; set; } = 65;    // ×1 %RH，偏移 +27

    // 状态
    /// <summary>整机运行状态，偏移 +1：0=停, 1=制冷, 2=制热, 3=除湿, 4=送风, 5=单独加热</summary>
    public int AirAllRunState      { get; set; } = 0;
    public int AirExterRunState    { get; set; } = 0; // 外风机，偏移 +2
    public int AirInterRunState    { get; set; } = 0; // 内风机，偏移 +3
    public int AirCompressRunState { get; set; } = 0; // 压缩机，偏移 +4
    public int AirHeatRunState     { get; set; } = 0; // 电加热，偏移 +5
    public int SpecificPattern     { get; set; } = 0; // 特定控制模式，偏移 +38

    // 故障/告警（各单独寄存器，0=无，1=有）
    public ushort AirHighTempAlarm      { get; set; } // 偏移 +41
    public ushort AirLowTempAlarm       { get; set; } // 偏移 +42
    public ushort AirHighRHAlarm        { get; set; } // 偏移 +43
    public ushort AirCompressorAlarm    { get; set; } // 偏移 +44
    public ushort AirFanAlarm           { get; set; } // 偏移 +45
    public ushort AirHighTempFault      { get; set; } // 偏移 +49
    public ushort AirLowTempFault       { get; set; } // 偏移 +50
    public ushort AirCompressorFault    { get; set; } // 偏移 +51
    public ushort AirFanFault           { get; set; } // 偏移 +52

    public override void ToRegisters(RegisterBank bank)
    {
        int b = BaseAddress;

        bank.Write(b + 1,  (ushort)AirAllRunState);
        bank.Write(b + 2,  (ushort)AirExterRunState);
        bank.Write(b + 3,  (ushort)AirInterRunState);
        bank.Write(b + 4,  (ushort)AirCompressRunState);
        bank.Write(b + 5,  (ushort)AirHeatRunState);

        bank.WriteFloat32(b + 6,  (float)(AirOutTemp1          / 0.1));
        bank.WriteFloat32(b + 9,  (float)(AirInterTemp1        / 0.1));
        bank.Write(b + 11, (ushort)AirInterRh1);
        bank.WriteFloat32(b + 13, (float)(AirInterCoilTemp     / 0.1));
        bank.WriteFloat32(b + 14, (float)(AirInputCurrent      / 0.001));
        bank.Write(b + 15, (ushort)AirACVoltage);
        bank.WriteFloat32(b + 17, (float)(AirCompressorCurrent / 0.001));
        bank.Write(b + 18, (ushort)AirExterFanSpeed);
        bank.Write(b + 19, (ushort)AirInterFanSpeed);
        bank.Write(b + 20, (ushort)AirCompressorFre);
        bank.Write(b + 23, (ushort)AirCoolPoint);
        bank.WriteInt16(b + 25, (short)AirHeatPoint);
        bank.Write(b + 27, (ushort)AirRHDiffPoint);
        bank.Write(b + 38, (ushort)SpecificPattern);

        bank.Write(b + 41, AirHighTempAlarm);
        bank.Write(b + 42, AirLowTempAlarm);
        bank.Write(b + 43, AirHighRHAlarm);
        bank.Write(b + 44, AirCompressorAlarm);
        bank.Write(b + 45, AirFanAlarm);
        bank.Write(b + 49, AirHighTempFault);
        bank.Write(b + 50, AirLowTempFault);
        bank.Write(b + 51, AirCompressorFault);
        bank.Write(b + 52, AirFanFault);
    }

    public override void FromRegisters(RegisterBank bank)
    {
        int b = BaseAddress;
        AirAllRunState      = bank.Read(b + 1);
        AirExterRunState    = bank.Read(b + 2);
        AirInterRunState    = bank.Read(b + 3);
        AirCompressRunState = bank.Read(b + 4);
        AirHeatRunState     = bank.Read(b + 5);
        AirOutTemp1         = Math.Round(bank.ReadFloat32(b + 6)  * 0.1,   2);
        AirInterTemp1       = Math.Round(bank.ReadFloat32(b + 9)  * 0.1,   2);
        AirInterRh1         = bank.Read(b + 11);
        AirInterCoilTemp    = Math.Round(bank.ReadFloat32(b + 13) * 0.1,   2);
        AirInputCurrent     = Math.Round(bank.ReadFloat32(b + 14) * 0.001, 3);
        AirACVoltage        = bank.Read(b + 15);
        AirCompressorCurrent= Math.Round(bank.ReadFloat32(b + 17) * 0.001, 3);
        AirExterFanSpeed    = bank.Read(b + 18);
        AirInterFanSpeed    = bank.Read(b + 19);
        AirCompressorFre    = bank.Read(b + 20);
        AirCoolPoint        = bank.Read(b + 23);
        AirHeatPoint        = (short)bank.Read(b + 25);
        AirRHDiffPoint      = bank.Read(b + 27);
        SpecificPattern     = bank.Read(b + 38);
        AirHighTempAlarm    = bank.Read(b + 41);
        AirLowTempAlarm     = bank.Read(b + 42);
        AirHighRHAlarm      = bank.Read(b + 43);
        AirCompressorAlarm  = bank.Read(b + 44);
        AirFanAlarm         = bank.Read(b + 45);
        AirHighTempFault    = bank.Read(b + 49);
        AirLowTempFault     = bank.Read(b + 50);
        AirCompressorFault  = bank.Read(b + 51);
        AirFanFault         = bank.Read(b + 52);
    }
}
