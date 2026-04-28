using Modbus.Device;
using SimulatorApp.Master.Models;
using SimulatorApp.Shared.Logging;
using System.Net.Sockets;

namespace SimulatorApp.Master.Services;

/// <summary>
/// Modbus TCP 主站服务（NModbus4 2.1.0）。
/// 建立连接后，由外部（MasterViewModel）驱动轮询。
/// </summary>
public class TcpMasterService : IMasterService
{
    private TcpClient? _client;
    private IModbusMaster? _master;
    private SlaveEndpoint? _endpoint;
    // 轮询和写入共享同一条 TCP 连接，必须串行访问，避免事务号错位/并发帧冲突。
    private readonly SemaphoreSlim _lock = new(1, 1);

    public bool IsConnected => _client?.Connected == true;

    public async Task ConnectAsync(SlaveEndpoint endpoint, CancellationToken ct = default)
    {
        _endpoint = endpoint;
        _client = new TcpClient();
        await _client.ConnectAsync(endpoint.Host, endpoint.Port, ct);

        _master = ModbusIpMaster.CreateIp(_client);
        _master.Transport.ReadTimeout = 3000;
        _master.Transport.WriteTimeout = 3000;
        _master.Transport.Retries = 2;

        AppLogger.Info($"TCP 主站已连接 → {endpoint.Host}:{endpoint.Port}  SlaveId={endpoint.SlaveId}");
    }

    // 读寄存器
    public async Task<ushort[]> ReadRegistersAsync(int startAddress, int quantity)
    {
        // 检查连接状态和参数合法性，避免在锁内执行无效操作导致长时间占用连接。
        EnsureConnected();
        // 检查地址 数量
        ValidateAddressAndQuantity(startAddress, quantity, 125, "FC03 读取");
        /*ConfigureAwait(false) 就是告诉它：不用回原上下文，在哪个线程可用就在哪继续。
        好处：
        少一次上下文切换，性能更好
        降低“同步阻塞 + await”造成死锁的风险
        适合库 / 服务层代码（不依赖 UI 控件）*/
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            // 获取寄存器值 !的意思是我确定 _master 这里不是 null，编译器别再给我可空警告
            // 获取的是一段寄存器值 起始地址 + 数量
            return await Task.Run(() =>
                _master!.ReadHoldingRegistersAsync(_endpoint!.SlaveId, (ushort)startAddress, (ushort)quantity))
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw BuildReadException(startAddress, quantity, ex);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task WriteSingleRegisterAsync(int address, ushort value)
    {
        EnsureConnected();
        ValidateAddressAndQuantity(address, 1, 1, "FC06 写入");

        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            await Task.Run(() =>
                _master!.WriteSingleRegister(_endpoint!.SlaveId, (ushort)address, value))
                .ConfigureAwait(false);
            AppLogger.Info($"TCP FC06 写寄存器  addr={address}  value=0x{value:X4}");
        }
        catch (Exception ex)
        {
            throw BuildWriteException(address, 1, 0x06, ex);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task WriteMultipleRegistersAsync(int address, ushort[] values)
    {
        EnsureConnected();
        ArgumentNullException.ThrowIfNull(values);
        // FC16 单帧最多 123 个寄存器。
        ValidateAddressAndQuantity(address, values.Length, 123, "FC16 写入");

        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            await Task.Run(() =>
                _master!.WriteMultipleRegisters(_endpoint!.SlaveId, (ushort)address, values))
                .ConfigureAwait(false);
            AppLogger.Info($"TCP FC16 写多寄存器  addr={address}  count={values.Length}" +
                           $"  [{string.Join(" ", values.Select(v => $"0x{v:X4}"))}]");
        }
        catch (Exception ex)
        {
            throw BuildWriteException(address, values.Length, 0x10, ex);
        }
        finally
        {
            _lock.Release();
        }
    }

    public Task DisconnectAsync()
    {
        _master?.Dispose();
        _client?.Close();
        _client?.Dispose();
        _master = null;
        _client = null;
        _endpoint = null;
        AppLogger.Info("TCP 主站已断开");
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await DisconnectAsync();

    // 未连接时抛出异常，避免执行无效操作并提供明确错误信息。
    private void EnsureConnected()
    {
        if (_master == null || _endpoint == null || _client == null)
            throw new InvalidOperationException("尚未连接");
    }

    // 验证地址和数量参数，确保在 Modbus 协议允许的范围内，避免无效请求和潜在的通信错误。
    private static void ValidateAddressAndQuantity(int startAddress, int quantity, int maxQuantity, string operation)
    {
        if (startAddress < 0 || startAddress > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(startAddress), $"{operation}失败：起始地址必须在 0~65535。");
        if (quantity <= 0 || quantity > maxQuantity)
            throw new ArgumentOutOfRangeException(nameof(quantity), $"{operation}失败：数量必须在 1~{maxQuantity}。");
        if (startAddress + quantity - 1 > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(quantity), $"{operation}失败：地址范围超出 0~65535。");
    }

    private InvalidOperationException BuildReadException(int startAddress, int quantity, Exception ex)
    {
        if (IsSlaveException(ex))
        {
            string fc = TryGetFunctionCode(ex) is byte fn ? $"，功能码=0x{fn:X2}" : string.Empty;
            return new InvalidOperationException(
                $"读取保持寄存器失败：从站返回异常（SlaveId={_endpoint!.SlaveId}，起始地址={startAddress}，数量={quantity}{fc}）。" +
                "请确认地址映射与读权限（FC03）。", ex);
        }

        if (IsTransactionIdMismatch(ex))
        {
            return new InvalidOperationException(
                $"读取保持寄存器失败：收到错位响应（Transaction ID 不一致，{EndpointTag()}，SlaveId={_endpoint!.SlaveId}）。" +
                "请检查从站/网关是否存在延迟或粘包，系统会继续重试。", ex);
        }

        if (IsConnectionBroken(ex))
        {
            return new InvalidOperationException(
                $"重新建立读取连接时连接中断：{ex.Message}（{EndpointTag()}，SlaveId={_endpoint!.SlaveId}）。" +
                "请检查从站程序、防火墙或杀毒软件，系统会继续自动重试。", ex);
        }

        return new InvalidOperationException(
            $"读取保持寄存器失败：{ex.Message}（{EndpointTag()}，SlaveId={_endpoint!.SlaveId}，起始地址={startAddress}，数量={quantity}）。", ex);
    }

    private InvalidOperationException BuildWriteException(int address, int quantity, byte functionCode, Exception ex)
    {
        if (IsSlaveException(ex))
        {
            return new InvalidOperationException(
                $"写入寄存器失败：从站返回异常（SlaveId={_endpoint!.SlaveId}，起始地址={address}，数量={quantity}，功能码=0x{functionCode:X2}）。",
                ex);
        }

        if (IsConnectionBroken(ex))
        {
            return new InvalidOperationException(
                $"写入寄存器失败：连接中断（{EndpointTag()}，SlaveId={_endpoint!.SlaveId}）。请检查从站连接后重试。", ex);
        }

        return new InvalidOperationException(
            $"写入寄存器失败：{ex.Message}（{EndpointTag()}，SlaveId={_endpoint!.SlaveId}，起始地址={address}，数量={quantity}）。", ex);
    }

    private string EndpointTag() => _endpoint == null ? "未知终端" : $"{_endpoint.Host}:{_endpoint.Port}";

    private static bool IsSlaveException(Exception ex) =>
        string.Equals(ex.GetType().Name, "SlaveException", StringComparison.Ordinal);

    private static byte? TryGetFunctionCode(Exception ex)
    {
        try
        {
            var prop = ex.GetType().GetProperty("FunctionCode");
            if (prop?.GetValue(ex) is byte fn) return fn;
        }
        catch
        {
            // 忽略反射失败，继续返回 null。
        }
        return null;
    }

    private static bool IsTransactionIdMismatch(Exception ex) =>
        ex.Message.Contains("transaction ID", StringComparison.OrdinalIgnoreCase);

    private static bool IsConnectionBroken(Exception ex)
    {
        string msg = ex.Message;
        return msg.Contains("Read resulted in 0 bytes returned", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("Unable to write data to the transport connection", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("Unable to read data from the transport connection", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("non-connected sockets", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("actively refused", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("forcibly closed", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("目标计算机拒绝连接", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("积极拒绝", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("中止了一个已建立的连接", StringComparison.OrdinalIgnoreCase);
    }
}
