namespace SimulatorApp.Slave.ViewModels;

/// <summary>
/// ComboBox 选项（枚举状态下拉）。
/// Value 写入寄存器，Display 显示给用户。
/// </summary>
public class ComboItem
{
    public int    Value   { get; init; }
    public string Display { get; init; } = string.Empty;

    public ComboItem(int value, string display)
    {
        Value   = value;
        Display = display;
    }

    public override string ToString() => Display;
}
