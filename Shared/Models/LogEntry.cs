namespace SimulatorApp.Shared.Models;

/// <summary>
/// 日志级别
/// </summary>
public enum LogLevel
{
    Info,
    Warn,
    Error
}

/// <summary>
/// 界面日志条目（最多保留 500 条，自动滚动到最新）
/// </summary>
public class LogEntry
{
    /// <summary>日志时间戳，格式 HH:mm:ss.fff</summary>
    public string Time { get; init; } = string.Empty;

    /// <summary>日志级别</summary>
    public LogLevel Level { get; init; }

    /// <summary>日志消息内容</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>完整显示文本</summary>
    public string DisplayText => $"{Time}  [{Level.ToString().ToUpper()}]  {Message}";

    /// <summary>
    /// 工厂方法：创建一条日志
    /// </summary>
    public static LogEntry Create(LogLevel level, string message) => new()
    {
        Time = DateTime.Now.ToString("HH:mm:ss.fff"),
        Level = level,
        Message = message
    };
}
