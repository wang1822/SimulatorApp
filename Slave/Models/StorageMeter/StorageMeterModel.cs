using SimulatorApp.Shared.Services;
using SimulatorApp.Slave.Models.ExternalMeter;

namespace SimulatorApp.Slave.Models.StorageMeter;

/// <summary>
/// 储能电表数据模型（基地址 48256）。
/// 字段结构与外部电表完全相同，仅基地址不同。
/// </summary>
public class StorageMeterModel : ExternalMeterModel
{
    public override string DeviceName => "储能电表";
    public override int BaseAddress    => 48256;
    public override int RegisterCount  => 178;

    // 字段继承自 ExternalMeterModel，无需重写 ToRegisters / FromRegisters
    // 基类方法通过 BaseAddress 属性自动使用 48256
}
