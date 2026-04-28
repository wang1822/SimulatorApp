namespace SimulatorApp.Shared.Helpers;

/// <summary>
/// float32 ↔ 两个 uint16 寄存器互转工具。
/// 字序：AB CD（Big-Endian Word Order），高字（AB）写低地址，低字（CD）写高地址。
///
/// 示例：3.14f (IEEE 754: 0x4048F5C3)
///   addr+0 = 0x4048 (字节 A=0x40, B=0x48)
///   addr+1 = 0xF5C3 (字节 C=0xF5, D=0xC3)
/// </summary>
public static class FloatRegisterHelper
{
    /// <summary>
    /// float32 → (高字, 低字)，AB CD 大端字序。
    /// 返回 (high, low)：high 写 addr+0，low 写 addr+1。
    /// </summary>
    public static (ushort high, ushort low) ToRegisters(float value)
    {
        // BitConverter.GetBytes 在 x86/x64 系统上返回小端 [D C B A]
        byte[] bytes = BitConverter.GetBytes(value);
        // 高字 AB: bytes[3]=A, bytes[2]=B
        ushort high = (ushort)((bytes[3] << 8) | bytes[2]);
        // 低字 CD: bytes[1]=C, bytes[0]=D
        ushort low  = (ushort)((bytes[1] << 8) | bytes[0]);
        return (high, low);
    }

    /// <summary>
    /// (高字, 低字) → float32，AB CD 大端字序。
    /// high 来自 addr+0，low 来自 addr+1。
    /// </summary>
    public static float FromRegisters(ushort high, ushort low)
    {
        // 重组为小端字节序 [D C B A]
        byte[] bytes =
        {
            (byte)(low  & 0xFF),   // D
            (byte)(low  >> 8),     // C
            (byte)(high & 0xFF),   // B
            (byte)(high >> 8)      // A
        };
        return BitConverter.ToSingle(bytes, 0);
    }
}
