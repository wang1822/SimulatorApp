using SimulatorApp.Shared.Models;

namespace SimulatorApp.Slave.Services;

/// <summary>
/// Modbus 从站服务接口（TCP 和 RTU 实现共用此接口）。
/// </summary>
public interface ISlaveService : IDisposable
{
    /// <summary>当前是否正在监听</summary>
    bool IsRunning { get; }

    /// <summary>从站 ID（1~247）</summary>
    byte SlaveId { get; }

    /// <summary>协议类型（Tcp / Rtu）</summary>
    ProtocolType Protocol { get; }

    /// <summary>
    /// 收到 Modbus 请求时触发（参数：功能码, 起始地址, 数量, 来源IP/串口）
    /// </summary>
    event Action<byte, int, int, string>? OnRequest;

    /// <summary>启动从站监听（异步，不阻塞 UI 线程）</summary>
    Task StartAsync(byte slaveId, CancellationToken cancellationToken = default);

    /// <summary>停止从站监听</summary>
    Task StopAsync();
}
