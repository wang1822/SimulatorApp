using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SimulatorApp.Shared.Helpers;

namespace SimulatorApp.Master.Models;

/// <summary>读取值的解析/显示模式（均为原始寄存器值，不含比例系数）</summary>
public enum ValueDisplayMode { UInt, Int, String }

/// <summary>
/// 主站数据显示行，绑定到遥测/遥控 DataGrid。
/// </summary>
public partial class RegisterDisplayRow : ObservableObject
{
    // ── 配置元数据（来自 DB，只读）──────────────────────────────────
    public int    RegisterConfigId { get; init; }
    public int    StartAddress     { get; init; }
    public int    Quantity         { get; init; }
    public string Unit             { get; init; } = string.Empty;
    public string DataType         { get; init; } = "uint16";
    public string ReadWrite        { get; init; } = "R";
    public double ScaleFactor      { get; init; } = 1.0;
    public double Offset           { get; init; } = 0.0;
    public string ValueRange       { get; init; } = string.Empty;
    public string Description      { get; init; } = string.Empty;
    public int    Category         { get; init; } = 0;
    // ── 可编辑字段（用户可内联修改，实时写 DB）────────────────────
    [ObservableProperty] private string _chineseName  = string.Empty;
    [ObservableProperty] private string _variableName = string.Empty;

    // ── 实时数据（轮询更新，UI 线程写）────────────────────────────
    [ObservableProperty] private string _rawDisplay    = "—";
    [ObservableProperty] private string _physicalValue = "—";
    [ObservableProperty] private string _lastUpdated   = string.Empty;

    // ── 解析显示模式（右键切换）────────────────────────────────────
    [ObservableProperty] private ValueDisplayMode _displayMode = ValueDisplayMode.UInt;

    partial void OnDisplayModeChanged(ValueDisplayMode value)
    {
        OnPropertyChanged(nameof(IsUIntMode));
        OnPropertyChanged(nameof(IsIntMode));
        OnPropertyChanged(nameof(IsStringMode));
        OnPropertyChanged(nameof(DisplayModeLabel));
        RefreshPhysicalValue();
    }

    public bool   IsUIntMode    => DisplayMode == ValueDisplayMode.UInt;
    public bool   IsIntMode     => DisplayMode == ValueDisplayMode.Int;
    public bool   IsStringMode  => DisplayMode == ValueDisplayMode.String;
    public string DisplayModeLabel => DisplayMode switch
    {
        ValueDisplayMode.Int    => "int（有符号整数）",
        ValueDisplayMode.String => "字符串（ASCII）",
        _                       => "uint（无符号整数）"
    };

    // ── 比对结果（手动或立即比对）────────────────────────────────────
    [ObservableProperty] private bool _isVerified = false;

    // ── 遥控写入值（仅 Category=1 可用）──────────────────────────────
    [ObservableProperty] private string _writeValue = string.Empty;

    /// <summary>
    /// 含比例系数的物理值，专供 API 比对使用，与界面显示模式无关。
    /// </summary>
    internal double LastPhysicalRaw { get; private set; } = 0.0;

    // ── 原始寄存器缓存（切换模式时无需重新轮询）─────────────────────
    private ushort[] _lastRegs = Array.Empty<ushort>();

    // ─────────────────────────────────────────────────────────────────

    /// <summary>切换解析模式命令（ContextMenu 绑定，参数："uint" / "int" / "str"）</summary>
    [RelayCommand]
    public void SetDisplayMode(string? modeKey)
    {
        DisplayMode = modeKey switch
        {
            "int" => ValueDisplayMode.Int,
            "str" => ValueDisplayMode.String,
            _     => ValueDisplayMode.UInt
        };
    }

    /// <summary>
    /// 从数据库保存的字符串恢复上次写入值（启动时调用，仅遥控行）。
    /// 同时预填 WriteValue，让用户进入编辑时直接看到上次写入的值。
    /// </summary>
    public void InitFromSaved(string rawRegisters, string physicalValue)
    {
        if (string.IsNullOrWhiteSpace(rawRegisters)) return;
        var regs = ParseRawDisplay(rawRegisters);
        if (regs.Length == 0) return;
        _lastRegs       = regs;
        RawDisplay      = rawRegisters;
        PhysicalValue   = physicalValue;
        WriteValue      = physicalValue;   // 预填编辑框
        LastPhysicalRaw = ComputePhysicalWithScale(regs); // 供 API 比对使用
        LastUpdated     = "(上次写入)";
    }

    /// <summary>将 "0x0000 0x0002 ..." 字符串解析回 ushort 数组</summary>
    private static ushort[] ParseRawDisplay(string raw)
    {
        try
        {
            return raw.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                      .Select(s => Convert.ToUInt16(s.Replace("0x", "").Replace("0X", ""), 16))
                      .ToArray();
        }
        catch { return Array.Empty<ushort>(); }
    }

    /// <summary>根据轮询到的原始寄存器数组更新显示值（UI 线程调用）</summary>
    public void UpdateFromRaw(ushort[] regs)
    {
        if (regs.Length == 0) return;
        _lastRegs   = regs;
        RawDisplay  = string.Join(" ", regs.Select(r => $"0x{r:X4}"));
        // LastPhysicalRaw 始终保持含比例系数的物理值，供 API 比对
        LastPhysicalRaw = ComputePhysicalWithScale(regs);
        RefreshPhysicalValue();
        LastUpdated = DateTime.Now.ToString("HH:mm:ss.fff");
    }

    /// <summary>用当前缓存的寄存器按当前显示模式刷新 PhysicalValue（切换模式时调用）</summary>
    private void RefreshPhysicalValue()
    {
        if (_lastRegs.Length == 0) return;

        PhysicalValue = DisplayMode switch
        {
            // uint：原始无符号十进制，不含比例系数
            ValueDisplayMode.UInt   => RawUnsigned(_lastRegs).ToString(),
            // int：原始有符号十进制，不含比例系数
            ValueDisplayMode.Int    => RawSigned(_lastRegs).ToString(),
            // string：寄存器字节 → ASCII
            ValueDisplayMode.String => RegsToString(_lastRegs),
            _                       => RawUnsigned(_lastRegs).ToString()
        };
    }

    // ─────────────────────────────────────────────────────────────────
    // 解析辅助
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// 含比例系数的物理值（DataType 感知），仅用于 LastPhysicalRaw / API 比对。
    /// </summary>
    private double ComputePhysicalWithScale(ushort[] regs)
    {
        double raw = DataType.ToLowerInvariant() switch
        {
            "float"  when regs.Length >= 2 => FloatRegisterHelper.FromRegisters(regs[0], regs[1]),
            "uint32" when regs.Length >= 2 => (double)((uint)regs[0] << 16 | regs[1]),
            "int32"  when regs.Length >= 2 => (double)(int)((uint)regs[0] << 16 | regs[1]),
            "int16"                        => (double)(short)regs[0],
            _                              => (double)regs[0]
        };
        return raw * ScaleFactor + Offset;
    }

    /// <summary>
    /// 原始无符号整数：所有寄存器由高到低拼接，不受 DataType 约束。
    /// 最多取 4 个寄存器（64 位），超出部分忽略。
    /// </summary>
    private static ulong RawUnsigned(ushort[] regs)
    {
        ulong result = 0;
        int count = Math.Min(regs.Length, 4);
        for (int i = 0; i < count; i++)
            result = (result << 16) | regs[i];
        return result;
    }

    /// <summary>
    /// 原始有符号整数：所有寄存器由高到低拼接后按总位宽做二进制补码解析。
    /// 最多取 4 个寄存器（64 位），超出部分忽略。
    /// </summary>
    private static long RawSigned(ushort[] regs)
    {
        int count = Math.Min(regs.Length, 4);
        ulong raw = 0;
        for (int i = 0; i < count; i++)
            raw = (raw << 16) | regs[i];

        int bits = count * 16;
        if (bits >= 64) return (long)raw;           // 64 位直接强转，ulong→long 保留补码

        ulong signBit = 1UL << (bits - 1);
        if ((raw & signBit) != 0)
        {
            // 符号位为 1，将高位全部填 1（二进制补码符号扩展）
            ulong mask = ~((1UL << bits) - 1);
            return (long)(raw | mask);
        }
        return (long)raw;
    }

    /// <summary>
    /// 将寄存器字节解释为 ASCII 字符串。
    /// 每个 ushort = 2 字节（高字节在前），遇 0x00 截断。
    /// 若含不可打印字符则回退为 "HH HH ..." 十六进制格式。
    /// </summary>
    private static string RegsToString(ushort[] regs)
    {
        var bytes = regs.SelectMany(r => new[] { (byte)(r >> 8), (byte)(r & 0xFF) })
                        .ToArray();
        int nullIdx = Array.IndexOf(bytes, (byte)0);
        if (nullIdx >= 0) bytes = bytes[..nullIdx];
        if (bytes.Length == 0) return string.Empty;

        bool allPrintable = bytes.All(b => b is >= 0x20 and <= 0x7E);
        return allPrintable
            ? System.Text.Encoding.ASCII.GetString(bytes)
            : BitConverter.ToString(bytes).Replace("-", " ");
    }
}
