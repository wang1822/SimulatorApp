using SimulatorApp.Master.Models;

namespace SimulatorApp.Master.Services;

/// <summary>
/// 主站 Modbus 服务接口。
/// 只负责连接/断开/单次读写；轮询循环由 MasterViewModel 驱动。
/// </summary>
public interface IMasterService : IAsyncDisposable
{
    bool IsConnected { get; }

    /// <summary>连接从站（不启动轮询）</summary>
    Task ConnectAsync(SlaveEndpoint endpoint, CancellationToken ct = default);

    /// <summary>断开连接</summary>
    Task DisconnectAsync();

    /// <summary>FC03 读 Holding Register，返回原始 ushort 数组</summary>
    Task<ushort[]> ReadRegistersAsync(int startAddress, int quantity);

    /// <summary>FC06 写单个寄存器</summary>
    Task WriteSingleRegisterAsync(int address, ushort value);

    /// <summary>FC16 写多个连续寄存器（float/int32/uint32 等多寄存器类型使用）</summary>
    Task WriteMultipleRegistersAsync(int address, ushort[] values);
}
