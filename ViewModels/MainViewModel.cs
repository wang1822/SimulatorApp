using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SimulatorApp.Master.ViewModels;
using SimulatorApp.Shared.Logging;
using SimulatorApp.Shared.Models;
using SimulatorApp.Shared.Services;
using SimulatorApp.Slave.ViewModels;
using System.Collections.ObjectModel;

namespace SimulatorApp.ViewModels;

/// <summary>
/// 主窗口 ViewModel — 切换从站/主站模式，管理 REST API 开关
/// </summary>
public partial class MainViewModel : ObservableObject
{
    // ----------------------------------------------------------------
    // 依赖
    // ----------------------------------------------------------------

    public SlaveViewModel   SlaveVm     { get; }
    public MasterViewModel  MasterVm    { get; }
    private readonly RestApiService _restApi;

    // ----------------------------------------------------------------
    // 模式切换
    // ----------------------------------------------------------------

    [ObservableProperty] private ModeType _currentMode = ModeType.Slave;

    public bool IsSlaveMode  => CurrentMode == ModeType.Slave;
    public bool IsMasterMode => CurrentMode == ModeType.Master;

    partial void OnCurrentModeChanged(ModeType value)
    {
        OnPropertyChanged(nameof(IsSlaveMode));
        OnPropertyChanged(nameof(IsMasterMode));
        AppLogger.Info($"切换到{(value == ModeType.Slave ? "从站" : "主站")}模式");
    }

    // ----------------------------------------------------------------
    // REST API 配置
    // ----------------------------------------------------------------

    [ObservableProperty] private bool _isRestApiEnabled = false;
    [ObservableProperty] private int  _restApiPort      = 8765;
    [ObservableProperty] private string _restApiStatus  = "REST API 未启动";

    // ----------------------------------------------------------------
    // 全局日志（AppLogger → UI）
    // ----------------------------------------------------------------

    public ObservableCollection<LogEntry> GlobalLogEntries { get; } = new();

    // ----------------------------------------------------------------
    // 标题栏信息
    // ----------------------------------------------------------------

    public string Title => "EMS设备主从ModBus";

    // ----------------------------------------------------------------
    // 构造
    // ----------------------------------------------------------------

    public MainViewModel(
        SlaveViewModel  slaveVm,
        MasterViewModel masterVm,
        RestApiService  restApi)
    {
        SlaveVm  = slaveVm;
        MasterVm = masterVm;
        _restApi = restApi;

        // 订阅 AppLogger 的 UI 日志事件，追加到全局日志列表
        AppLogger.OnUiLog += (level, message) =>
        {
            var logLevel = level switch
            {
                "WARN"  => LogLevel.Warn,
                "ERROR" => LogLevel.Error,
                _       => LogLevel.Info
            };
            var entry = LogEntry.Create(logLevel, message);
            System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                if (GlobalLogEntries.Count >= 1000)
                    GlobalLogEntries.RemoveAt(0);
                GlobalLogEntries.Add(entry);
            });
        };
    }

    // ----------------------------------------------------------------
    // 命令：切换模式
    // ----------------------------------------------------------------

    [RelayCommand]
    public void SwitchToSlave()  => CurrentMode = ModeType.Slave;

    [RelayCommand]
    public void SwitchToMaster() => CurrentMode = ModeType.Master;

    // ----------------------------------------------------------------
    // 命令：启停 REST API
    // ----------------------------------------------------------------

    [RelayCommand]
    public async Task ToggleRestApiAsync()
    {
        if (_restApi.IsRunning)
        {
            await _restApi.StopAsync();
            IsRestApiEnabled = false;
            RestApiStatus    = "REST API 未启动";
        }
        else
        {
            try
            {
                await _restApi.StartAsync(RestApiPort);
                IsRestApiEnabled = true;
                RestApiStatus    = $"REST API 运行中  http://localhost:{RestApiPort}/";
            }
            catch (Exception ex)
            {
                RestApiStatus = $"REST API 启动失败：{ex.Message}";
                AppLogger.Error("REST API 启动失败", ex);
            }
        }
    }
}
