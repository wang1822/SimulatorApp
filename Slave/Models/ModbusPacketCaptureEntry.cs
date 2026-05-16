namespace SimulatorApp.Slave.Models;

public sealed class ModbusPacketCaptureEntry
{
    public ModbusPacketCaptureEntry(DateTime timestamp, string direction, string source, string text)
    {
        Timestamp = timestamp;
        Direction = direction;
        Source = source;
        Text = text;
    }

    public DateTime Timestamp { get; }
    public string Direction { get; }
    public string Source { get; }
    public string Text { get; }
}
