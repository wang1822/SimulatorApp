using Modbus.Data;
using Modbus.Device;
using SimulatorApp.Shared.Logging;
using SimulatorApp.Shared.Services;
using System.Net;
using System.Net.Sockets;
using ProtocolType = SimulatorApp.Shared.Models.ProtocolType;

namespace SimulatorApp.Slave.Services;

/// <summary>
/// Modbus TCP 从站服务（NModbus4 2.1.0）。
/// 使用 ModbusTcpSlave.CreateTcp(slaveId, tcpListener) + Listen() 模式。
/// DataStore 与 RegisterBank 保持同步。
/// </summary>
public class TcpSlaveService : ISlaveService, IRegisterSnapshotSlaveService
{
    private readonly RegisterBank        _bank;
    private TcpListener?                 _tcpListener;
    private ModbusTcpSlave?              _slave;
    private CancellationTokenSource?     _cts;
    private Task?                        _listenTask;
    private DataStore?                   _dataStore;
    private readonly HashSet<int>        _snapshotAddresses = new();
    private readonly object              _dataStoreSyncRoot = new();

    public bool         IsRunning { get; private set; }
    public byte         SlaveId   { get; private set; }
    public ProtocolType Protocol  => ProtocolType.Tcp;

    public string ListenAddress { get; set; } = "0.0.0.0";
    public int    Port          { get; set; } = 502;
    public byte   FunctionCode  { get; set; } = 3;
    public Func<int, bool>? RegisterAddressFilter { get; set; }

    public event Action<byte, int, int, string>? OnRequest;

    public TcpSlaveService(RegisterBank bank)
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
            _dataStore = DataStoreFactory.CreateDefaultDataStore();
            SyncBankToDataStore();

            _tcpListener = new TcpListener(IPAddress.Parse(ListenAddress), Port);
            _tcpListener.Start();

            // NModbus4 2.1.0：CreateTcp 接收 TcpListener，内部管理所有 TCP 客户端
            _slave = ModbusTcpSlave.CreateTcp(slaveId, _tcpListener);
            _slave.DataStore = _dataStore;

            _slave.DataStore.DataStoreReadFrom  += (s, e) => OnDataStoreRead(e);
            _slave.DataStore.DataStoreWrittenTo += (s, e) => OnDataStoreWritten(e);

            // RegisterBank 写入时实时同步到 DataStore（NModbus4 索引从 1 开始）
            _bank.OnRegisterWritten += SyncOneRegister;

            AppLogger.Info($"TCP 从站启动：{ListenAddress}:{Port}  SlaveID={slaveId}");
            IsRunning = true;

            // Listen() 是同步阻塞调用，放到后台线程。
            // 不传 token 给 Task.Run，避免 Stop 时 token 已取消导致 Task 直接变 Canceled。
            // 用局部变量捕获 token，避免 _cts 被置空时的竞态。
            var token = _cts.Token;
            _listenTask = Task.Run(() =>
            {
                try { _slave.Listen(); }
                catch (Exception ex)
                {
                    // 主动停止时 Listen() 会因 socket 关闭抛异常，属于正常流程，静默忽略。
                    if (!token.IsCancellationRequested)
                        AppLogger.Error("TCP 从站监听异常", ex);
                }
            });

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            IsRunning = false;
            AppLogger.Error($"TCP 从站启动失败：{ex.Message}", ex);
            throw;
        }
    }

    public async Task StopAsync()
    {
        if (!IsRunning) return;
        IsRunning = false;
        _bank.OnRegisterWritten -= SyncOneRegister;
        _cts?.Cancel();
        _slave?.Dispose();
        _tcpListener?.Stop();

        if (_listenTask != null)
        {
            try { await _listenTask.WaitAsync(TimeSpan.FromSeconds(3)); }
            catch { }
        }
        AppLogger.Info("TCP 从站已停止");
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

    /// <summary>将 RegisterBank 当前值同步到 NModbus4 DataStore（DataStore 从索引 1 开始）</summary>
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
        OnRequest?.Invoke(functionCode, e.StartAddress, count, $"{ListenAddress}:{Port}");
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
        OnRequest?.Invoke(16, e.StartAddress, e.Data.B.Count, "TCP客户端");
    }

    public void Dispose() => StopAsync().Wait(2000);
}
