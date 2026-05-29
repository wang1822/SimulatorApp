using Modbus.Data;
using Modbus.Device;
using SimulatorApp.Shared.Logging;
using SimulatorApp.Shared.Services;
using System.IO.Ports;
using ProtocolType = SimulatorApp.Shared.Models.ProtocolType;

namespace SimulatorApp.Slave.Services;

/// <summary>
/// Modbus RTU 从站服务（NModbus4 2.1.0）。
/// 通过串口监听 RTU 帧，DataStore 与 RegisterBank 保持同步。
/// </summary>
public class RtuSlaveService : ISlaveService, IRegisterSnapshotSlaveService
{
    private readonly RegisterBank        _bank;
    private SerialPort?                  _serialPort;
    private ModbusSerialSlave?           _slave;
    private CancellationTokenSource?     _cts;
    private Task?                        _listenTask;
    private DataStore?                   _dataStore;
    private readonly HashSet<int>        _snapshotAddresses = new();
    private readonly object              _dataStoreSyncRoot = new();

    public bool         IsRunning { get; private set; }
    public byte         SlaveId   { get; private set; }
    public ProtocolType Protocol  => ProtocolType.Rtu;

    public string   PortName  { get; set; } = "COM3";
    public int      BaudRate  { get; set; } = 9600;
    public byte     FunctionCode { get; set; } = 3;
    public Func<int, bool>? RegisterAddressFilter { get; set; }
    public int      DataBits  { get; set; } = 8;
    public StopBits StopBits  { get; set; } = StopBits.One;
    public Parity   Parity    { get; set; } = Parity.None;

    public event Action<byte, int, int, string>? OnRequest;

    public RtuSlaveService(RegisterBank bank)
    {
        _bank = bank;
    }

    public async Task StartAsync(byte slaveId, CancellationToken cancellationToken = default)
    {
        if (IsRunning) return;
        SlaveId = slaveId;
        _cts    = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            _serialPort = new SerialPort(PortName, BaudRate, Parity, DataBits, StopBits)
            {
                ReadTimeout  = 1000,
                WriteTimeout = 1000
            };
            _serialPort.Open();

            _dataStore = DataStoreFactory.CreateDefaultDataStore();
            SyncBankToDataStore();

            _slave = ModbusSerialSlave.CreateRtu(slaveId, _serialPort);
            _slave.DataStore = _dataStore;

            _slave.DataStore.DataStoreReadFrom  += (s, e) => OnDataStoreRead(e);
            _slave.DataStore.DataStoreWrittenTo += (s, e) => OnDataStoreWritten(e);

            _bank.OnRegisterWritten += SyncOneRegister;

            AppLogger.Info($"RTU 从站启动：{PortName}  波特率={BaudRate}  SlaveID={slaveId}");
            IsRunning = true;

            var token = _cts.Token;
            _listenTask = Task.Run(() =>
            {
                ListenWithRecovery(token);
            });

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            IsRunning = false;
            AppLogger.Error($"RTU 从站启动失败：{ex.Message}", ex);
            throw;
        }
    }

    private void ListenWithRecovery(CancellationToken token)
    {
        var recoverableErrorCount = 0;
        var lastRecoverableLogAt = DateTime.MinValue;

        while (!token.IsCancellationRequested)
        {
            try
            {
                _slave?.Listen();
                if (!token.IsCancellationRequested)
                    AppLogger.Warn($"RTU 从站监听已返回：{PortName}");
                break;
            }
            catch (Exception ex)
            {
                if (token.IsCancellationRequested || !IsRunning)
                    break;

                if (!IsRecoverableRtuFrameException(ex))
                {
                    AppLogger.Error("RTU 从站监听异常", ex);
                    break;
                }

                recoverableErrorCount++;
                ClearSerialBuffers();

                var now = DateTime.UtcNow;
                if (recoverableErrorCount <= 3 || now - lastRecoverableLogAt >= TimeSpan.FromSeconds(5))
                {
                    AppLogger.Warn(
                        $"RTU 从站收到异常帧，已清空缓冲并继续监听：{PortName} " +
                        $"SlaveID={SlaveId} Count={recoverableErrorCount} Error={ex.Message}");
                    lastRecoverableLogAt = now;
                }

                try
                {
                    Task.Delay(50, token).Wait(token);
                }
                catch
                {
                    break;
                }
            }
        }
    }

    private static bool IsRecoverableRtuFrameException(Exception ex)
    {
        if (ex is NotImplementedException)
            return true;

        if (ex is ArgumentOutOfRangeException argEx
            && string.Equals(argEx.ParamName, "NumberOfPoints", StringComparison.Ordinal))
            return true;

        if (ex is FormatException formatEx
            && formatEx.Message.Contains("even number of bytes", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private void ClearSerialBuffers()
    {
        try
        {
            if (_serialPort?.IsOpen == true)
            {
                _serialPort.DiscardInBuffer();
                _serialPort.DiscardOutBuffer();
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"RTU 从站清空串口缓冲失败：{PortName} {ex.Message}");
        }
    }

    public async Task StopAsync()
    {
        if (!IsRunning) return;
        IsRunning = false;
        _bank.OnRegisterWritten -= SyncOneRegister;
        _cts?.Cancel();
        _slave?.Dispose();
        _serialPort?.Close();
        _serialPort?.Dispose();

        if (_listenTask != null)
        {
            try { await _listenTask.WaitAsync(TimeSpan.FromSeconds(3)); }
            catch { }
        }
        AppLogger.Info("RTU 从站已停止");
    }

    private void SyncOneRegister(int address, ushort value)
    {
        lock (_dataStoreSyncRoot)
        {
            if (_dataStore != null
                && (uint)address < 65536
                && (RegisterAddressFilter?.Invoke(address) ?? true))
            {
                SyncSnapshotValue(address, value);
            }
        }
    }

    public void ReplaceSnapshotValues(IReadOnlyDictionary<int, ushort> values)
    {
        lock (_dataStoreSyncRoot)
        {
            if (_dataStore == null) return;

            foreach (var address in _snapshotAddresses.Except(values.Keys).ToList())
            {
                if ((uint)address < 65536)
                    SyncSnapshotValue(address, 0);
            }

            _snapshotAddresses.Clear();
            foreach (var (address, value) in values)
            {
                if ((uint)address < 65536)
                {
                    SyncSnapshotValue(address, value);
                    _snapshotAddresses.Add(address);
                }
            }
        }
    }

    public ushort[] ReadSnapshotValues(int startAddress, int count)
    {
        lock (_dataStoreSyncRoot)
        {
            if (_dataStore == null || count <= 0)
                return [];

            var result = new ushort[count];
            for (var i = 0; i < count; i++)
            {
                var address = startAddress + i;
                if ((uint)address < 65536)
                    result[i] = ReadSnapshotValue(address);
            }

            return result;
        }
    }

    private void SyncBankToDataStore()
    {
        lock (_dataStoreSyncRoot)
        {
            if (_dataStore == null) return;
            for (int i = 0; i < 65535; i++)
            {
                if (RegisterAddressFilter?.Invoke(i) ?? true)
                    SyncSnapshotValue(i, _bank.Read(i));
            }
        }
    }

    private void SyncSnapshotValue(int address, ushort value)
    {
        var dataStoreIndex = (ushort)(address + 1);
        switch (FunctionCode)
        {
            case 1:
                _dataStore!.CoilDiscretes[dataStoreIndex] = value != 0;
                break;
            case 2:
                _dataStore!.InputDiscretes[dataStoreIndex] = value != 0;
                break;
            case 4:
                _dataStore!.InputRegisters[dataStoreIndex] = value;
                break;
            default:
                _dataStore!.HoldingRegisters[dataStoreIndex] = value;
                break;
        }
    }

    private ushort ReadSnapshotValue(int address)
    {
        var dataStoreIndex = (ushort)(address + 1);
        return FunctionCode switch
        {
            1 => _dataStore!.CoilDiscretes[dataStoreIndex] ? (ushort)1 : (ushort)0,
            2 => _dataStore!.InputDiscretes[dataStoreIndex] ? (ushort)1 : (ushort)0,
            4 => _dataStore!.InputRegisters[dataStoreIndex],
            _ => _dataStore!.HoldingRegisters[dataStoreIndex]
        };
    }

    private void OnDataStoreRead(DataStoreEventArgs e)
    {
        byte functionCode = e.ModbusDataType switch
        {
            ModbusDataType.Coil => 1,
            ModbusDataType.Input => 2,
            ModbusDataType.InputRegister => 4,
            _ => 3
        };
        int count = functionCode is 3 or 4 ? e.Data.B.Count : e.Data.A.Count;
        OnRequest?.Invoke(functionCode, e.StartAddress, count, PortName);
    }

    private void OnDataStoreWritten(DataStoreEventArgs e)
    {
        if (e.ModbusDataType == ModbusDataType.HoldingRegister)
        {
            var regs = e.Data.B; // ReadOnlyCollection<ushort>
            for (int i = 0; i < regs.Count; i++)
            {
                int addr = e.StartAddress + i; // e.StartAddress 是 PDU 地址（0-based），与 bank 地址相同
                if ((uint)addr < 65536 && (RegisterAddressFilter?.Invoke(addr) ?? true))
                    _bank.Write(addr, regs[i]);
            }
        }
        OnRequest?.Invoke(16, e.StartAddress, e.Data.B.Count, PortName);
    }

    public void Dispose() => StopAsync().Wait(2000);
}
