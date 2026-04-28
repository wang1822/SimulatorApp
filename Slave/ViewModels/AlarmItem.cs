using CommunityToolkit.Mvvm.ComponentModel;

namespace SimulatorApp.Slave.ViewModels;

/// <summary>
/// 故障/告警多选项（bitmask 模式）。
/// UI 中对应一个 CheckBox，勾选后通过 OR 合并到告警字寄存器。
/// </summary>
public partial class AlarmItem : ObservableObject
{
    /// <summary>告警/故障中文名称（显示在 CheckBox 旁边）</summary>
    public string Label { get; }

    /// <summary>对应告警字中的 bit 掩码（如 bit0=1, bit3=8, bit7=128）</summary>
    public int BitMask { get; }

    [ObservableProperty]
    private bool _isChecked;

    /// <summary>勾选变更时触发回调（由 DeviceViewModelBase 订阅，用于重新计算合并值）</summary>
    public event Action? CheckedChanged;

    public AlarmItem(string label, int bitMask)
    {
        Label   = label;
        BitMask = bitMask;
    }

    partial void OnIsCheckedChanged(bool value)
    {
        CheckedChanged?.Invoke();
    }
}
