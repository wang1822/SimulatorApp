using SimulatorApp.Shared.Models;

namespace SimulatorApp.Master.Models;

/// <summary>
/// 主站要连接的从站端点配置（一个从站设备）
/// </summary>
public class SlaveEndpoint
{
    /// <summary>设备别名（显示用）</summary>
    public string Name       { get; set; } = "设备1";

    /// <summary>通信协议</summary>
    public ProtocolType Protocol { get; set; } = ProtocolType.Tcp;

    // ── TCP ──
    public string Host       { get; set; } = "172.168.3.100";
    public int    Port       { get; set; } = 502;

    // ── RTU ──
    public string PortName   { get; set; } = "COM3";
    public int    BaudRate   { get; set; } = 9600;

    /// <summary>Modbus Slave ID（1–247）</summary>
    public byte   SlaveId    { get; set; } = 1;

    /// <summary>起始寄存器地址（Holding Register，FC03）</summary>
    public int    StartAddr  { get; set; } = 0;

    /// <summary>一次读取寄存器数量</summary>
    public int    Quantity   { get; set; } = 10;

    /// <summary>轮询间隔（毫秒）</summary>
    public int    PollIntervalMs { get; set; } = 1000;
}
