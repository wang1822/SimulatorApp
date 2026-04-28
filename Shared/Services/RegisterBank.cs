using SimulatorApp.Shared.Helpers;

namespace SimulatorApp.Shared.Services;

/// <summary>
/// 共享寄存器内存池（65536 个 Holding Register）。
/// 从站模式：响应 EMS 的 FC03 轮询请求时从此读取数据。
/// 主站模式：将轮询结果写入此处后刷新 UI。
/// 所有读写均持有 lock，保证多线程安全。
/// </summary>
public class RegisterBank
{
    private readonly ushort[] _regs = new ushort[65536];
    private readonly object _lock = new();

    /// <summary>
    /// 每次写寄存器后触发，参数为（地址, 新值）。
    /// 从站服务订阅此事件，将变更同步到 NModbus4 DataStore。
    /// </summary>
    public event Action<int, ushort>? OnRegisterWritten;

    // ----------------------------------------------------------------
    // 基础读写
    // ----------------------------------------------------------------

    /// <summary>读取单个寄存器</summary>
    public ushort Read(int address)
    {
        ValidateAddress(address);
        lock (_lock) { return _regs[address]; }
    }

    /// <summary>写入单个寄存器</summary>
    public void Write(int address, ushort value)
    {
        ValidateAddress(address);
        lock (_lock) { _regs[address] = value; }
        OnRegisterWritten?.Invoke(address, value);
    }

    /// <summary>读取连续多个寄存器</summary>
    public ushort[] ReadRange(int startAddress, int count)
    {
        ValidateAddress(startAddress);
        ValidateAddress(startAddress + count - 1);
        lock (_lock)
        {
            var result = new ushort[count];
            Array.Copy(_regs, startAddress, result, 0, count);
            return result;
        }
    }

    /// <summary>写入连续多个寄存器</summary>
    public void WriteRange(int startAddress, ushort[] values)
    {
        if (values == null || values.Length == 0) return;
        ValidateAddress(startAddress);
        ValidateAddress(startAddress + values.Length - 1);
        lock (_lock)
        {
            Array.Copy(values, 0, _regs, startAddress, values.Length);
        }
        for (int i = 0; i < values.Length; i++)
            OnRegisterWritten?.Invoke(startAddress + i, values[i]);
    }

    // ----------------------------------------------------------------
    // 类型化写入便捷方法
    // ----------------------------------------------------------------

    /// <summary>
    /// 写入 float32（AB CD 大端字序，高字写低地址），占用 2 个寄存器。
    /// 物理值传入，不含比例系数换算，由调用方换算后传入。
    /// </summary>
    public void WriteFloat32(int address, float value)
    {
        ValidateAddress(address + 1);
        var (hi, lo) = FloatRegisterHelper.ToRegisters(value);
        lock (_lock)
        {
            _regs[address]     = hi;
            _regs[address + 1] = lo;
        }
        OnRegisterWritten?.Invoke(address,     hi);
        OnRegisterWritten?.Invoke(address + 1, lo);
    }

    /// <summary>读取 float32（AB CD 大端字序）</summary>
    public float ReadFloat32(int address)
    {
        ValidateAddress(address + 1);
        lock (_lock)
        {
            return FloatRegisterHelper.FromRegisters(_regs[address], _regs[address + 1]);
        }
    }

    /// <summary>
    /// 写入 uint32（高 16 位在低地址），占用 2 个寄存器。
    /// </summary>
    public void WriteUInt32(int address, uint value)
    {
        ValidateAddress(address + 1);
        ushort hi = (ushort)(value >> 16);
        ushort lo = (ushort)(value & 0xFFFF);
        lock (_lock)
        {
            _regs[address]     = hi;
            _regs[address + 1] = lo;
        }
        OnRegisterWritten?.Invoke(address,     hi);
        OnRegisterWritten?.Invoke(address + 1, lo);
    }

    /// <summary>写入 int16（有符号 16 位）</summary>
    public void WriteInt16(int address, short value)
    {
        ValidateAddress(address);
        lock (_lock) { _regs[address] = (ushort)value; }
        OnRegisterWritten?.Invoke(address, (ushort)value);
    }

    /// <summary>写入 uint8（低 8 位有效）</summary>
    public void WriteUInt8(int address, byte value)
    {
        ValidateAddress(address);
        lock (_lock) { _regs[address] = value; }
        OnRegisterWritten?.Invoke(address, value);
    }

    // ----------------------------------------------------------------
    // NModbus4 DataStore 适配（供 Slave 服务使用）
    // ----------------------------------------------------------------

    /// <summary>
    /// 将内存寄存器池的值同步到 NModbus4 DefaultSlaveDataStore。
    /// 在从站启动前调用，确保初始寄存器值正确。
    /// </summary>
    public void SyncToDataStore(Modbus.Data.DataStore dataStore, int startAddress, int count)
    {
        var values = ReadRange(startAddress, count);
        for (int i = 0; i < count; i++)
        {
            // NModbus4 的地址从 1 开始
            dataStore.HoldingRegisters[(ushort)(startAddress + i + 1)] = values[i];
        }
    }

    /// <summary>
    /// 获取内部寄存器数组的只读副本（供 NModbus4 适配使用）。
    /// 注意：此副本在获取时为快照，不保证实时性。
    /// </summary>
    public ushort[] GetSnapshot(int startAddress, int count)
    {
        return ReadRange(startAddress, count);
    }

    // ----------------------------------------------------------------
    // 工具
    // ----------------------------------------------------------------

    /// <summary>
    /// 将全部 65536 个寄存器清零（不触发 OnRegisterWritten 事件）。
    /// 在从站启动前调用，确保未启用设备的旧数据不被 NModbus4 DataStore 读取。
    /// </summary>
    public void ClearAll()
    {
        lock (_lock) { Array.Clear(_regs, 0, _regs.Length); }
    }

    private static void ValidateAddress(int address)
    {
        if (address < 0 || address > 65535)
            throw new ArgumentOutOfRangeException(nameof(address), $"寄存器地址越界: {address}");
    }
}
