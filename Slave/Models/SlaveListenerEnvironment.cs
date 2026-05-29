namespace SimulatorApp.Slave.Models;

public class SlaveListenerEnvironment
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<SlaveListenerEnvironmentItem> Items { get; } = new();
}

public class SlaveListenerEnvironmentItem
{
    public int SortOrder { get; set; }
    public bool IsEnabled { get; set; } = true;
    public string? BoundDeviceKey { get; set; }
    public int ListenerDbId { get; set; }
    public int Protocol { get; set; }
    public string ListenAddress { get; set; } = "0.0.0.0";
    public int Port { get; set; } = 502;
    public string ComPort { get; set; } = string.Empty;
    public int BaudRate { get; set; } = 9600;
    public byte SlaveId { get; set; } = 1;
    public int FunctionCode { get; set; } = 3;
}
