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
public class TcpSlaveService : ISlaveService
{
    private readonly RegisterBank        _bank;
    private TcpListener?                 _tcpListener;
    private ModbusTcpSlave?              _slave;
    private CancellationTokenSource?     _cts;
    private Task?                        _listenTask;
    private DataStore?                   _dataStore;

    public bool         IsRunning { get; private set; }
    public byte         SlaveId   { get; private set; }
    public ProtocolType Protocol  => ProtocolType.Tcp;

    public string ListenAddress { get; set; } = "0.0.0.0";
    public int    Port          { get; set; } = 502;

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
        if (_dataStore != null && (uint)address < 65536)
            _dataStore.HoldingRegisters[(ushort)(address + 1)] = value;
    }

    /// <summary>将 RegisterBank 当前值同步到 NModbus4 DataStore（HoldingRegisters 从索引 1 开始）</summary>
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
        OnRequest?.Invoke(3, e.StartAddress, count, $"{ListenAddress}:{Port}");
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
        OnRequest?.Invoke(16, e.StartAddress, e.Data.B.Count, "TCP客户端");
    }

    public void Dispose() => StopAsync().Wait(2000);
}
