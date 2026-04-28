using NLog;
using NLog.Config;
using NLog.Targets;

namespace SimulatorApp.Shared.Logging;

/// <summary>
/// 应用程序日志工具类。
/// 同时输出到：
///   1. 文件日志（NLog）：logs/app-yyyy-MM-dd.log，保留 30 天
///   2. 界面日志（事件回调）：由 MainViewModel 订阅后追加到 LogEntries
/// </summary>
public static class AppLogger
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    /// <summary>界面日志回调（参数：级别, 消息）</summary>
    public static event Action<string, string>? OnUiLog;

    // ----------------------------------------------------------------
    // 静态初始化 NLog（如果未使用配置文件）
    // ----------------------------------------------------------------

    static AppLogger()
    {
        // 尝试从文件加载配置；如果不存在，使用代码配置
        if (LogManager.Configuration == null)
        {
            ConfigureNLogInCode();
        }
    }

    private static void ConfigureNLogInCode()
    {
        var config = new LoggingConfiguration();

        // 文件目标：按日期滚动，保留 30 天
        var fileTarget = new FileTarget("file")
        {
            FileName = "logs/app-${shortdate}.log",
            Layout   = "${longdate} [${level:uppercase=true}] ${logger:shortName=true} - ${message} ${exception:format=tostring}",
            ArchiveEvery     = FileArchivePeriod.Day,
            MaxArchiveFiles  = 30,
            Encoding         = System.Text.Encoding.UTF8
        };

        config.AddRule(LogLevel.Info, LogLevel.Fatal, fileTarget, "*");
        LogManager.Configuration = config;
    }

    // ----------------------------------------------------------------
    // 公共日志方法
    // ----------------------------------------------------------------

    public static void Info(string message)
    {
        _logger.Info(message);
        OnUiLog?.Invoke("INFO", message);
    }

    public static void Warn(string message)
    {
        _logger.Warn(message);
        OnUiLog?.Invoke("WARN", message);
    }

    public static void Error(string message, Exception? ex = null)
    {
        if (ex != null) _logger.Error(ex, message);
        else            _logger.Error(message);
        OnUiLog?.Invoke("ERROR", ex != null ? $"{message}: {ex.Message}" : message);
    }

    public static void Debug(string message) => _logger.Debug(message);

    /// <summary>
    /// 格式化 Modbus 请求日志（从站模式下记录每条请求）
    /// </summary>
    public static void ModbusRequest(byte fc, int startAddr, int qty, byte slaveId, string source)
    {
        string msg = $"FC{fc:D2}  addr={startAddr}  qty={qty}  slaveId={slaveId}  来源={source}";
        Info(msg);
    }
}
