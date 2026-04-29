using Microsoft.Extensions.DependencyInjection;
using SimulatorApp.Slave.ViewModels;
using SimulatorApp.ViewModels;
using System.ComponentModel;
using System.Linq;
using System.Windows;

namespace SimulatorApp.Views;

/// <summary>
/// 主窗口 code-behind — DataContext 在 App.xaml.cs 中由 DI 容器注入
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(MainViewModel vm) : this()
    {
        DataContext = vm;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            var inspector = vm.SlaveVm.InspectorList
                .OfType<RegisterInspectorViewModel>()
                .FirstOrDefault();

            var addressCount = inspector?.Rows.Count ?? 0;
            if (addressCount > 0)
            {
                var result = MessageBox.Show(
                    $"寄存器检视中已添加 {addressCount} 个地址，退出后这些临时检视地址不会保留。\n\n确定要退出程序吗？",
                    "确认退出",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No);

                if (result != MessageBoxResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
            }
        }

        base.OnClosing(e);
    }
}
