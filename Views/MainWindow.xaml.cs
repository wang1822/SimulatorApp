using Microsoft.Extensions.DependencyInjection;
using SimulatorApp.ViewModels;
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
}
