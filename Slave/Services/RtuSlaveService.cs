using Modbus.Data;
using Modbus.Device;
using Modbus.IO;
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
    private EchoFilteringSerialResource? _streamResource;
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
    public string? BoundDeviceKey { get; set; }
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

            _streamResource = new EchoFilteringSerialResource(_serialPort);
            _slave = ModbusSerialSlave.CreateRtu(slaveId, _streamResource);
            _slave.DataStore = _dataStore;

            _slave.DataStore.DataStoreReadFrom  += (s, e) => OnDataStoreRead(e);
            _slave.DataStore.DataStoreWrittenTo += (s, e) => OnDataStoreWritten(e);

            _bank.OnRegisterWritten += SyncOneRegister;

            AppLogger.Info($"RTU 从站启动：{PortName}  波特率={BaudRate}  SlaveID={slaveId} EchoFilter=on");
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
                    var recentIo = _streamResource?.DescribeRecentIo() ?? "(none)";
                    AppLogger.Warn(
                        $"RTU 从站收到异常帧，已清空缓冲并继续监听：{PortName} " +
                        $"SlaveID={SlaveId} Count={recoverableErrorCount} Error={ex.Message} RecentIo={recentIo}");
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
            if (_streamResource != null)
            {
                _streamResource.DiscardInBuffer();
            }
            else if (_serialPort?.IsOpen == true)
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
        _streamResource = null;
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
            if ((uint)e.StartAddress < 65536)
            {
                var count = Math.Min(regs.Count, 65536 - e.StartAddress);
                if (count > 0)
                    _bank.WriteExternalRange(
                        e.StartAddress,
                        regs.Take(count).ToArray(),
                        RegisterAddressFilter,
                        BoundDeviceKey,
                        PortName);
            }
        }
        OnRequest?.Invoke(16, e.StartAddress, e.Data.B.Count, PortName);
    }

    private sealed class EchoFilteringSerialResource : IStreamResource
    {
        private const int MaxRecentIoEntries = 20;
        private const int MaxPendingEchoFrames = 4;
        private static readonly TimeSpan PendingEchoLifetime = TimeSpan.FromSeconds(3);
        private readonly SerialPort _serialPort;
        private readonly object _syncRoot = new();
        private readonly Queue<byte> _prefetchedBytes = new();
        private readonly Queue<string> _recentIo = new();
        private readonly List<PendingEchoFrame> _pendingEchoFrames = new();

        public EchoFilteringSerialResource(SerialPort serialPort)
        {
            _serialPort = serialPort;
        }

        public int InfiniteTimeout => SerialPort.InfiniteTimeout;
        public int ReadTimeout
        {
            get => _serialPort.ReadTimeout;
            set => _serialPort.ReadTimeout = value;
        }
        public int WriteTimeout
        {
            get => _serialPort.WriteTimeout;
            set => _serialPort.WriteTimeout = value;
        }

        public void DiscardInBuffer()
        {
            lock (_syncRoot)
            {
                _prefetchedBytes.Clear();
            }

            if (_serialPort.IsOpen)
            {
                _serialPort.DiscardInBuffer();
                _serialPort.DiscardOutBuffer();
            }
        }

        public int Read(byte[] buffer, int offset, int count)
        {
            if (count <= 0) return 0;

            var prefetched = DequeuePrefetched(buffer, offset, count);
            if (prefetched > 0)
                return prefetched;

            while (true)
            {
                var captured = ReadSerialBurst(count, out var totalRead);
                if (totalRead <= 0)
                    return 0;

                var skip = DropPendingEchoPrefix(captured, totalRead);
                if (skip >= totalRead)
                    continue;

                var available = totalRead - skip;
                var copied = Math.Min(count, available);
                Array.Copy(captured, skip, buffer, offset, copied);
                RecordIo("Rx", buffer, offset, copied);
                EnqueuePrefetched(captured, skip + copied, available - copied);
                return copied;
            }
        }

        public void Write(byte[] buffer, int offset, int count)
        {
            if (count <= 0) return;

            var written = new byte[count];
            Array.Copy(buffer, offset, written, 0, count);
            RecordIo("Tx", written, 0, written.Length);
            AddPendingEchoFrame(written);

            _serialPort.Write(buffer, offset, count);
            DrainLocalEcho(written);
        }

        public string DescribeRecentIo()
        {
            lock (_syncRoot)
            {
                return _recentIo.Count == 0 ? "(empty)" : string.Join(" | ", _recentIo);
            }
        }

        public void Dispose()
        {
            // The owning service closes and disposes the SerialPort.
        }

        private int DequeuePrefetched(byte[] buffer, int offset, int count)
        {
            lock (_syncRoot)
            {
                var copied = 0;
                while (copied < count && _prefetchedBytes.Count > 0)
                    buffer[offset + copied++] = _prefetchedBytes.Dequeue();

                if (copied > 0)
                    RecordIoLocked("RxPrefetch", buffer, offset, copied);

                return copied;
            }
        }

        private byte[] ReadSerialBurst(int requestedCount, out int totalRead)
        {
            var capacity = Math.Max(requestedCount, 512);
            var captured = new byte[capacity];
            totalRead = _serialPort.Read(captured, 0, Math.Min(requestedCount, capacity));

            var settleUntil = DateTime.UtcNow.AddMilliseconds(8);
            while (totalRead < capacity && (_serialPort.BytesToRead > 0 || DateTime.UtcNow < settleUntil))
            {
                if (_serialPort.BytesToRead <= 0)
                {
                    Thread.Sleep(1);
                    continue;
                }

                var readCount = Math.Min(capacity - totalRead, _serialPort.BytesToRead);
                totalRead += _serialPort.Read(captured, totalRead, readCount);
                settleUntil = DateTime.UtcNow.AddMilliseconds(2);
            }

            return captured;
        }

        private void DrainLocalEcho(byte[] written)
        {
            try
            {
                Thread.Sleep(CalculateDrainDelayMs(written.Length));

                var available = _serialPort.BytesToRead;
                if (available <= 0)
                    return;

                var maxRead = Math.Min(available, Math.Max(written.Length * 2, 512));
                var captured = new byte[maxRead];
                var totalRead = 0;
                while (totalRead < maxRead && _serialPort.BytesToRead > 0)
                {
                    var readCount = Math.Min(maxRead - totalRead, _serialPort.BytesToRead);
                    totalRead += _serialPort.Read(captured, totalRead, readCount);
                }

                if (totalRead <= 0)
                    return;

                if (totalRead >= written.Length && StartsWith(captured, totalRead, written))
                {
                    RecordIo("EchoDrop", written, 0, written.Length);
                    RemovePendingEchoFrame(written);
                    EnqueuePrefetched(captured, written.Length, totalRead - written.Length);
                    return;
                }

                EnqueuePrefetched(captured, 0, totalRead);
            }
            catch (Exception ex)
            {
                RecordText("EchoDrainError", ex.Message);
            }
        }

        private int CalculateDrainDelayMs(int byteCount)
        {
            var stopBits = _serialPort.StopBits switch
            {
                StopBits.One => 1.0,
                StopBits.OnePointFive => 1.5,
                StopBits.Two => 2.0,
                _ => 1.0
            };
            var bitsPerByte = 1 + _serialPort.DataBits + (_serialPort.Parity == Parity.None ? 0 : 1) + stopBits;
            var baudRate = Math.Max(1, _serialPort.BaudRate);
            var ms = (int)Math.Ceiling(byteCount * bitsPerByte * 1000.0 / baudRate) + 5;
            return Math.Clamp(ms, 2, 300);
        }

        private static bool StartsWith(byte[] buffer, int bufferLength, byte[] prefix)
        {
            if (bufferLength < prefix.Length)
                return false;

            for (var i = 0; i < prefix.Length; i++)
            {
                if (buffer[i] != prefix[i])
                    return false;
            }

            return true;
        }

        private int DropPendingEchoPrefix(byte[] captured, int totalRead)
        {
            lock (_syncRoot)
            {
                PrunePendingEchoFramesLocked();
                for (var i = 0; i < _pendingEchoFrames.Count; i++)
                {
                    var frame = _pendingEchoFrames[i];
                    if (totalRead < frame.Bytes.Length || !StartsWith(captured, totalRead, frame.Bytes))
                        continue;

                    _pendingEchoFrames.RemoveAt(i);
                    RecordIoLocked("EchoDropDelayed", frame.Bytes, 0, frame.Bytes.Length);
                    return frame.Bytes.Length;
                }
            }

            return 0;
        }

        private void AddPendingEchoFrame(byte[] bytes)
        {
            lock (_syncRoot)
            {
                PrunePendingEchoFramesLocked();
                while (_pendingEchoFrames.Count >= MaxPendingEchoFrames)
                    _pendingEchoFrames.RemoveAt(0);

                _pendingEchoFrames.Add(new PendingEchoFrame(bytes, DateTime.UtcNow));
            }
        }

        private void RemovePendingEchoFrame(byte[] bytes)
        {
            lock (_syncRoot)
            {
                for (var i = 0; i < _pendingEchoFrames.Count; i++)
                {
                    if (!_pendingEchoFrames[i].Bytes.SequenceEqual(bytes))
                        continue;

                    _pendingEchoFrames.RemoveAt(i);
                    return;
                }
            }
        }

        private void PrunePendingEchoFramesLocked()
        {
            var cutoff = DateTime.UtcNow - PendingEchoLifetime;
            _pendingEchoFrames.RemoveAll(frame => frame.CreatedAtUtc < cutoff);
        }

        private void EnqueuePrefetched(byte[] bytes, int offset, int count)
        {
            if (count <= 0)
                return;

            lock (_syncRoot)
            {
                for (var i = 0; i < count; i++)
                    _prefetchedBytes.Enqueue(bytes[offset + i]);
                RecordIoLocked("RxPrefetch", bytes, offset, count);
            }
        }

        private void RecordIo(string direction, byte[] bytes, int offset, int count)
        {
            lock (_syncRoot)
            {
                RecordIoLocked(direction, bytes, offset, count);
            }
        }

        private void RecordIoLocked(string direction, byte[] bytes, int offset, int count)
        {
            var hex = string.Join(' ', bytes.Skip(offset).Take(count).Select(b => b.ToString("X2")));
            RecordTextLocked(direction, hex);
        }

        private void RecordText(string direction, string text)
        {
            lock (_syncRoot)
            {
                RecordTextLocked(direction, text);
            }
        }

        private void RecordTextLocked(string direction, string text)
        {
            while (_recentIo.Count >= MaxRecentIoEntries)
                _recentIo.Dequeue();
            _recentIo.Enqueue($"{direction}:{text}");
        }

        private sealed record PendingEchoFrame(byte[] Bytes, DateTime CreatedAtUtc);
    }

    public void Dispose() => StopAsync().Wait(2000);
}
