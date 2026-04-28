namespace SimulatorApp.Shared.Models;

/// <summary>
/// 模拟器工作模式：从站模式（模拟设备供 EMS 轮询）或主站模式（主动连接并轮询目标设备）
/// </summary>
public enum ModeType
{
    /// <summary>Modbus 从站，监听 EMS 请求</summary>
    Slave,
    /// <summary>Modbus 主站，主动轮询目标设备</summary>
    Master
}
