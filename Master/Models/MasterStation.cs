namespace SimulatorApp.Master.Models;

/// <summary>
/// 主站站点配置（对应数据库 MasterStations 表）
/// </summary>
public class MasterStation
{
    public int      Id             { get; set; }
    public string   Name           { get; set; } = string.Empty;
    public int      Protocol       { get; set; } = 0;   // 0=TCP, 1=RTU
    public string   Host           { get; set; } = "172.168.3.100";
    public int      Port           { get; set; } = 502;
    public string   PortName       { get; set; } = "COM3";
    public int      BaudRate       { get; set; } = 9600;
    public byte     SlaveId        { get; set; } = 1;
    public int      PollIntervalMs { get; set; } = 1000;
    public DateTime CreatedAt      { get; set; }

    /// <summary>true = 内存临时站点（未持久化到数据库，Id 为负数）</summary>
    public bool IsTemporary => Id < 0;

    public override string ToString() => Name;
}
