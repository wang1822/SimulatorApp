using Microsoft.Extensions.DependencyInjection;
using SimulatorApp.Master.Services;
using SimulatorApp.Master.ViewModels;
using SimulatorApp.Shared.Services;
using SimulatorApp.Shared.Views;
using SimulatorApp.Slave.Services;
using SimulatorApp.Slave.ViewModels;
using SimulatorApp.ViewModels;
using System.Windows;

namespace SimulatorApp;

/// <summary>
/// 应用程序入口，负责 DI 容器注册和全局异常处理。
/// </summary>
public partial class App : Application
{
    /// <summary>DI 服务提供者（全局静态，便于在 XAML 代码后台获取服务）</summary>
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        // 注册全局未处理异常
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        Current.DispatcherUnhandledException      += OnDispatcherUnhandledException;

        // 构建 DI 容器（必须在 base.OnStartup 之前完成，因为 base 会触发 Startup 事件 → App_OnStartup）
        Services = BuildServiceProvider();

        base.OnStartup(e);

        // 初始化 NLog（读取配置文件）
        Shared.Logging.AppLogger.Info("EMS设备故障模拟器 启动");
    }

    // App.xaml 中配置 Startup="App_OnStartup"，由此事件创建并显示主窗口（注入 DI ViewModel）
    private void App_OnStartup(object sender, StartupEventArgs e)
    {
        var mainVm = Services.GetRequiredService<ViewModels.MainViewModel>();
        var window = new Views.MainWindow(mainVm);
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // 停止 REST API 服务
        var restApi = Services.GetService<RestApiService>();
        restApi?.StopAsync().GetAwaiter().GetResult();

        Shared.Logging.AppLogger.Info("应用程序退出");
        NLog.LogManager.Shutdown();
        base.OnExit(e);
    }

    // ----------------------------------------------------------------
    // DI 注册
    // ----------------------------------------------------------------

    private static IServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();

        // ---- 共享服务（单例）----
        services.AddSingleton<RegisterBank>();
        services.AddSingleton<RestApiService>();

        // ---- 从站服务 ----
        services.AddTransient<TcpSlaveService>();
        services.AddTransient<RtuSlaveService>();
        services.AddSingleton<RegisterMapService>();

        // ---- 主站服务 ----
        services.AddTransient<TcpMasterService>();
        services.AddTransient<RtuMasterService>();

        // ---- 从站 ViewModel（各设备，单例，整个程序生命周期） ----
        services.AddSingleton<PcsViewModel>();
        services.AddSingleton<BmsViewModel>();
        services.AddSingleton<MpptViewModel>();
        services.AddSingleton<AirConditionerViewModel>();
        services.AddSingleton<DehumidifierViewModel>();
        services.AddSingleton<ExternalMeterViewModel>();
        services.AddSingleton<StorageMeterViewModel>();
        services.AddSingleton<StsInstrumentViewModel>();
        services.AddSingleton<StsControlViewModel>();
        services.AddSingleton<DIDOControllerViewModel>();
        services.AddSingleton<DieselGeneratorViewModel>();
        services.AddSingleton<GasDetectorViewModel>();
        services.AddSingleton<RegisterInspectorViewModel>();
        services.AddSingleton<SlaveViewModel>();

        // ---- 主站 ViewModel ----
        services.AddSingleton<MasterViewModel>();

        // ---- 主 ViewModel ----
        services.AddSingleton<MainViewModel>();

        return services.BuildServiceProvider();
    }

    // ----------------------------------------------------------------
    // 全局异常处理
    // ----------------------------------------------------------------

    private void OnDispatcherUnhandledException(object sender,
        System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        Shared.Logging.AppLogger.Error("UI 线程未处理异常", e.Exception);
        ThemedMessageBox.Show($"发生未处理异常：\n{e.Exception.Message}\n\n请查看日志文件。",
            "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true; // 防止崩溃
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            Shared.Logging.AppLogger.Error("非 UI 线程未处理异常", ex);
    }
}
