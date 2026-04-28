namespace SimulatorApp.Shared.Models;

/// <summary>
/// Modbus 通信协议类型
/// </summary>
public enum ProtocolType
{
    /// <summary>TCP/IP 以太网通信</summary>
    Tcp,
    /// <summary>RS485 串口 RTU 通信</summary>
    Rtu
}
