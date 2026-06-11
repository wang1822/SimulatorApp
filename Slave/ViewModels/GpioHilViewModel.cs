using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SimulatorApp.Slave.Models;
using SimulatorApp.Slave.Services;
using System.Collections.ObjectModel;

namespace SimulatorApp.Slave.ViewModels;

public partial class GpioHilViewModel : ObservableObject, IDisposable
{
    private readonly GpioHilSshService _service = new();

    [ObservableProperty] private string _host = "172.168.2.100";
    [ObservableProperty] private string _port = "22";
    [ObservableProperty] private string _username = "";
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private string _startupScriptPath = "/usr/local/app/start.sh";
    [ObservableProperty] private string _statusText = "未连接";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isConnected;

    public ObservableCollection<GpioHilPoint> Points { get; } = new();

    public GpioHilViewModel()
    {
        for (var i = 0; i < 16; i++)
        {
            Points.Add(new GpioHilPoint
            {
                Name = $"DI{i}",
                Position = (495 + i).ToString(),
                CurrentValue = "0"
            });
        }
    }

    partial void OnHostChanged(string value) => ResetConnectionAfterCredentialChange();
    partial void OnPortChanged(string value) => ResetConnectionAfterCredentialChange();
    partial void OnUsernameChanged(string value) => ResetConnectionAfterCredentialChange();
    partial void OnPasswordChanged(string value) => ResetConnectionAfterCredentialChange();

    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (IsBusy) return;

        await RunBusyAsync("连接中...", () =>
        {
            IsConnected = false;
            _service.Disconnect();

            if (string.IsNullOrWhiteSpace(Host))
                throw new InvalidOperationException("请输入 Linux 设备 IP。");

            if (string.IsNullOrWhiteSpace(Username))
                throw new InvalidOperationException("请输入 Linux 登录账号。");

            if (string.IsNullOrWhiteSpace(Password))
                throw new InvalidOperationException("请输入 Linux 登录密码。");

            var port = int.TryParse(Port, out var p) ? p : 22;
            _service.Connect(Host.Trim(), port, Username.Trim(), Password);

            IsConnected = true;
            StatusText = "已连接。请填写主程序启动脚本路径后，手动点击“安装/修复”。";
        }, disconnectOnError: true);
    }

    [RelayCommand]
    private async Task InstallOrRepairAsync()
    {
        await RunBusyAsync("安装/修复中...", () =>
        {
            EnsureConnected();
            _service.InstallOrRepair(StartupScriptPath);
            StatusText = "安装/修复完成，LD_PRELOAD 已写入启动脚本。请点击“重启Linux”后生效。";
        });
    }

    [RelayCommand]
    private async Task ClearScriptAsync()
    {
        await RunBusyAsync("清除脚本中...", () =>
        {
            EnsureConnected();
            StatusText = _service.ClearScript(StartupScriptPath);
        });
    }

    [RelayCommand]
    private async Task RestartLinuxAsync()
    {
        await RunBusyAsync("重启 Linux 中...", () =>
        {
            EnsureConnected();
            StatusText = _service.RestartLinux();
        });
    }

    [RelayCommand]
    private async Task ReadValuesAsync()
    {
        await RunBusyAsync("读取中...", () =>
        {
            EnsureConnected();
            var values = _service.ReadValues(Points.Select(p => p.Position));
            foreach (var point in Points)
            {
                var gpio = NormalizePositionForLookup(point.Position);
                if (gpio != null && values.TryGetValue(gpio, out var value))
                {
                    point.CurrentValue = value is "0" or "1" ? value : "";
                    point.Status = "已读取";
                }
            }

            StatusText = "读取完成";
        });
    }

    [RelayCommand]
    private async Task WritePointAsync(object? parameter)
    {
        await WritePointFromCellAsync(parameter);
    }

    public async Task WritePointFromCellAsync(object? parameter)
    {
        if (parameter is not GpioHilPoint point)
            return;

        if (point is null) return;

        await RunBusyAsync("写入中...", () =>
        {
            EnsureConnected();
            _service.SetValue(point.Position, point.CurrentValue);
            point.Status = "已写入";
            StatusText = $"{point.Name} 位置 {point.Position} 已写入 {point.CurrentValue}";
        });
    }

    [RelayCommand]
    private async Task WriteAllZeroAsync()
    {
        await WriteAllAsync("0");
    }

    [RelayCommand]
    private async Task WriteAllOneAsync()
    {
        await WriteAllAsync("1");
    }

    private async Task WriteAllAsync(string value)
    {
        await RunBusyAsync($"全部写入 {value} 中...", () =>
        {
            EnsureConnected();
            foreach (var point in Points)
            {
                point.CurrentValue = value;
                _service.SetValue(point.Position, value);
                point.Status = "已写入";
            }

            StatusText = $"全部位置已写入 {value}";
        });
    }

    private async Task RunBusyAsync(string busyText, Action action, bool disconnectOnError = false)
    {
        if (IsBusy) return;

        try
        {
            IsBusy = true;
            StatusText = busyText;
            await Task.Run(action);
        }
        catch (Exception ex)
        {
            if (disconnectOnError)
            {
                _service.Disconnect();
                IsConnected = false;
            }
            else if (!_service.IsConnected)
            {
                IsConnected = false;
            }

            StatusText = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string? NormalizePositionForLookup(string position)
    {
        var text = (position ?? "").Trim();
        if (text.StartsWith("GPIO_", StringComparison.OrdinalIgnoreCase))
            text = text[5..];
        else if (text.StartsWith("gpio", StringComparison.OrdinalIgnoreCase))
            text = text[4..];

        return int.TryParse(text, out var gpio) && gpio >= 0 ? gpio.ToString() : null;
    }

    public void Dispose() => _service.Dispose();

    private void EnsureConnected()
    {
        if (!_service.IsConnected)
        {
            IsConnected = false;
            throw new InvalidOperationException("请先输入 Linux 账号密码并连接成功。");
        }
    }

    private void ResetConnectionAfterCredentialChange()
    {
        if (IsBusy)
            return;

        if (_service.IsConnected)
            _service.Disconnect();

        IsConnected = false;
        StatusText = "登录信息已变化，请重新连接。";
    }
}
