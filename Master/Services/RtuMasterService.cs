using Modbus.Device;
using SimulatorApp.Master.Models;
using SimulatorApp.Shared.Logging;
using System.IO.Ports;

namespace SimulatorApp.Master.Services;

/// <summary>
/// Modbus RTU 主站服务（NModbus4 2.1.0）。
/// 建立连接后，由外部（MasterViewModel）驱动轮询。
/// </summary>
public class RtuMasterService : IMasterService
{
    private SerialPort? _port;
    private IModbusMaster? _master;
    private SlaveEndpoint? _endpoint;
    // 串口是半双工，轮询和写入必须串行，否则帧冲突会导致 SlaveException。
    private readonly SemaphoreSlim _lock = new(1, 1);

    public bool IsConnected => _port?.IsOpen == true;

    public Task ConnectAsync(SlaveEndpoint endpoint, CancellationToken ct = default)
    {
        _endpoint = endpoint;
        _port = new SerialPort(endpoint.PortName, endpoint.BaudRate, Parity.None, 8, StopBits.One)
        {
            ReadTimeout = 3000,
            WriteTimeout = 3000
        };
        _port.Open();

        _master = ModbusSerialMaster.CreateRtu(_port);
        _master.Transport.ReadTimeout = 3000;
        _master.Transport.WriteTimeout = 3000;
        _master.Transport.Retries = 2;

        AppLogger.Info($"RTU 主站已连接 → {endpoint.PortName}@{endpoint.BaudRate}  SlaveId={endpoint.SlaveId}");
        return Task.CompletedTask;
    }

    public async Task<ushort[]> ReadRegistersAsync(int startAddress, int quantity)
    {
        EnsureConnected();
        ValidateAddressAndQuantity(startAddress, quantity, 125, "FC03 读取");

        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
                _master!.ReadHoldingRegisters(_endpoint!.SlaveId, (ushort)startAddress, (ushort)quantity))
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
            AppLogger.Info($"RTU FC06 写寄存器  addr={address}  value=0x{value:X4}");
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
        ValidateAddressAndQuantity(address, values.Length, 123, "FC16 写入");

        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            await Task.Run(() =>
                _master!.WriteMultipleRegisters(_endpoint!.SlaveId, (ushort)address, values))
                .ConfigureAwait(false);
            AppLogger.Info($"RTU FC16 写多寄存器  addr={address}  count={values.Length}" +
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
        _port?.Close();
        _port?.Dispose();
        _master = null;
        _port = null;
        _endpoint = null;
        AppLogger.Info("RTU 主站已断开");
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await DisconnectAsync();

    private void EnsureConnected()
    {
        if (_master == null || _endpoint == null || _port == null)
            throw new InvalidOperationException("尚未连接");
    }

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

        if (IsConnectionBroken(ex))
        {
            return new InvalidOperationException(
                $"读取保持寄存器失败：连接中断（{EndpointTag()}，SlaveId={_endpoint!.SlaveId}）。请检查串口连线和从站状态。", ex);
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
                $"写入寄存器失败：连接中断（{EndpointTag()}，SlaveId={_endpoint!.SlaveId}）。请检查串口连线和从站状态。", ex);
        }

        return new InvalidOperationException(
            $"写入寄存器失败：{ex.Message}（{EndpointTag()}，SlaveId={_endpoint!.SlaveId}，起始地址={address}，数量={quantity}）。", ex);
    }

    private string EndpointTag() => _endpoint == null ? "未知串口" : $"{_endpoint.PortName}@{_endpoint.BaudRate}";

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

    private static bool IsConnectionBroken(Exception ex)
    {
        string msg = ex.Message;
        return msg.Contains("Read resulted in 0 bytes returned", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("Unable to write data to the transport connection", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("Unable to read data from the transport connection", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("Port is closed", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("串口", StringComparison.OrdinalIgnoreCase);
    }
}
