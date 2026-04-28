namespace SimulatorApp.Slave.Models;

/// <summary>
/// 浠庣珯璁惧閰嶇疆锛堝搴旀暟鎹簱 SlaveDeviceConfigs 琛級
/// </summary>
public class SlaveDeviceConfig
{
    public int      Id             { get; set; }
    public string   Name           { get; set; } = string.Empty;
    public int      Protocol       { get; set; } = 0;   // 0=TCP, 1=RTU
    public string   Host           { get; set; } = "0.0.0.0";
    public int      Port           { get; set; } = 502;
    public string   PortName       { get; set; } = "COM3";
    public int      BaudRate       { get; set; } = 9600;
    public byte     SlaveId        { get; set; } = 1;
    public int      PollIntervalMs { get; set; } = 1000;
    public DateTime CreatedAt      { get; set; }
}
