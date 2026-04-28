using CommunityToolkit.Mvvm.ComponentModel;

namespace SimulatorApp.Master.Models;

/// <summary>
/// 主站轮询数据表格中的一行 — 对应一个寄存器地址及其原始值/解析值
/// </summary>
public partial class RegisterRow : ObservableObject
{
    /// <summary>寄存器地址（十进制）</summary>
    [ObservableProperty] private int    _address;

    /// <summary>原始 ushort 值（十进制）</summary>
    [ObservableProperty] private ushort _rawValue;

    /// <summary>十六进制表示</summary>
    public string HexValue => $"0x{RawValue:X4}";

    /// <summary>字段说明（可由 Excel 模板配置）</summary>
    [ObservableProperty] private string _description = string.Empty;

    /// <summary>物理值（解析后，含比例系数和单位）</summary>
    [ObservableProperty] private string _physicalValue = string.Empty;

    /// <summary>最后更新时间</summary>
    [ObservableProperty] private string _lastUpdated = string.Empty;

    partial void OnRawValueChanged(ushort value)
    {
        OnPropertyChanged(nameof(HexValue));
        LastUpdated = DateTime.Now.ToString("HH:mm:ss.fff");
    }
}
