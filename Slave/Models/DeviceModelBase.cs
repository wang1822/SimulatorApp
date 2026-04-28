using SimulatorApp.Shared.Services;

namespace SimulatorApp.Slave.Models;

/// <summary>
/// 从站设备模型抽象基类。
/// 每个设备子类持有协议定义的所有字段，并实现：
///   ToRegisters()：将当前字段值写入 RegisterBank（供从站响应 EMS 轮询）
///   FromRegisters()：从 RegisterBank 读取寄存器值（导入配置时使用）
/// </summary>
public abstract class DeviceModelBase
{
    /// <summary>设备中文名称（显示用）</summary>
    public abstract string DeviceName { get; }

    /// <summary>寄存器基地址（Holding Register，0-based）</summary>
    public abstract int BaseAddress { get; }

    /// <summary>
    /// 该设备占用的寄存器数量，用于取消勾选时清零对应地址范围。
    /// 默认 0 表示不清零（如 RegisterInspector 等特殊设备）。
    /// </summary>
    public virtual int RegisterCount => 0;

    /// <summary>从站 ID（1~247）</summary>
    public byte SlaveId { get; set; } = 1;

    /// <summary>
    /// 将当前所有字段值写入 RegisterBank。
    /// 每次 ViewModel 属性变更后调用，确保寄存器与界面同步。
    /// </summary>
    public abstract void ToRegisters(RegisterBank bank);

    /// <summary>
    /// 从 RegisterBank 读取寄存器值并更新模型字段。
    /// 用于导入配置快照时将寄存器值反解为物理值。
    /// </summary>
    public abstract void FromRegisters(RegisterBank bank);
}
