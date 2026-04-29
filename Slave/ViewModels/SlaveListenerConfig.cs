using CommunityToolkit.Mvvm.ComponentModel;
using SimulatorApp.Shared.Models;
using SimulatorApp.Slave.Services;

namespace SimulatorApp.Slave.ViewModels;

/// <summary>
/// 单条 Modbus 从站监听端点配置。
/// SlaveViewModel 维护一个此类的集合，支持同时运行多条监听。
/// </summary>
public partial class SlaveListenerConfig : ObservableObject
{
    // ---- 运行状态（只读，由 SlaveViewModel 写入） ----

    [ObservableProperty] private bool   _isRunning  = false;
    [ObservableProperty] private string _statusText = "未启动";

    // ---- 配置（用户可编辑，停止状态下有效） ----

    [ObservableProperty] private bool _isEnabled = true;

    [ObservableProperty] private ProtocolType _protocol = ProtocolType.Tcp;

    // TCP
    [ObservableProperty] private string _listenAddress = "0.0.0.0";
    [ObservableProperty] private int    _port          = 502;

    // RTU
    [ObservableProperty] private string _comPort   = string.Empty;
    [ObservableProperty] private int    _baudRate  = 9600;

    // 公共
    [ObservableProperty] private byte _slaveId = 1;

    // ---- 协议辅助属性 ----

    public bool IsTcpMode => Protocol == ProtocolType.Tcp;
    public bool IsRtuMode => Protocol == ProtocolType.Rtu;

    public IReadOnlyList<string> ProtocolNames { get; } = ["TCP", "RTU"];

    public int ProtocolIndex
    {
        get => (int)Protocol;
        set => Protocol = (ProtocolType)value;
    }

    partial void OnProtocolChanged(ProtocolType value)
    {
        OnPropertyChanged(nameof(IsTcpMode));
        OnPropertyChanged(nameof(IsRtuMode));
        OnPropertyChanged(nameof(ProtocolIndex));
    }

    /// <summary>当前激活的 Slave 服务实例，由 SlaveViewModel 在启动时赋值</summary>
    public int DbId { get; set; } = 0;

    internal ISlaveService? Service { get; set; }
}
