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
public class RtuSlaveService : ISlaveService
{
    private readonly RegisterBank        _bank;
    private SerialPort?                  _serialPort;
    private ModbusSerialSlave?           _slave;
    private CancellationTokenSource?     _cts;
    private Task?                        _listenTask;
    private DataStore?                   _dataStore;

    public bool         IsRunning { get; private set; }
    public byte         SlaveId   { get; private set; }
    public ProtocolType Protocol  => ProtocolType.Rtu;

    public string   PortName  { get; set; } = "COM3";
    public int      BaudRate  { get; set; } = 9600;
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
                try { _slave.Listen(); }
                catch (Exception ex)
                {
                    if (!token.IsCancellationRequested)
                        AppLogger.Error("RTU 从站监听异常", ex);
                }
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
        if (_dataStore != null && (uint)address < 65536)
            _dataStore.HoldingRegisters[(ushort)(address + 1)] = value;
    }

    private void SyncBankToDataStore()
    {
        if (_dataStore == null) return;
        for (int i = 0; i < 65535; i++)
            _dataStore.HoldingRegisters[(ushort)(i + 1)] = _bank.Read(i);
    }

    private void OnDataStoreRead(DataStoreEventArgs e)
    {
        int count = e.ModbusDataType == ModbusDataType.HoldingRegister
            ? e.Data.B.Count
            : e.Data.A.Count;
        OnRequest?.Invoke(3, e.StartAddress, count, PortName);
    }

    private void OnDataStoreWritten(DataStoreEventArgs e)
    {
        if (e.ModbusDataType == ModbusDataType.HoldingRegister)
        {
            var regs = e.Data.B; // ReadOnlyCollection<ushort>
            for (int i = 0; i < regs.Count; i++)
            {
                int addr = e.StartAddress + i; // e.StartAddress 是 PDU 地址（0-based），与 bank 地址相同
                if ((uint)addr < 65536)
                    _bank.Write(addr, regs[i]);
            }
        }
        OnRequest?.Invoke(16, e.StartAddress, e.Data.B.Count, PortName);
    }

    public void Dispose() => StopAsync().Wait(2000);
}
