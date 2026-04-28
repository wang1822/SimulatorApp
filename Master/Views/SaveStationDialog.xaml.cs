using SimulatorApp.Master.ViewModels;
using System.Windows;

namespace SimulatorApp.Master.Views;

/// <summary>
/// 保存站点配置对话框 code-behind。
/// DataContext 为 SaveStationDialogViewModel；
/// 监听 DialogResult 属性关闭窗口。
/// </summary>
public partial class SaveStationDialog : Window
{
    private readonly SaveStationDialogViewModel _vm;

    public SaveStationDialog(SaveStationDialogViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;

        // 监听 DialogResult 变化自动关闭
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SaveStationDialogViewModel.DialogResult))
                Close();
        };
    }
}
