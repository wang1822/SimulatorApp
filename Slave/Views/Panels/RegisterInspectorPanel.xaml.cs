using SimulatorApp.Slave.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace SimulatorApp.Slave.Views.Panels;

/// <summary>
/// 寄存器检视器 — code-behind：处理按钮 Click（DataGrid 行内按钮无法直接绑定 Command+Parameter）
/// </summary>
public partial class RegisterInspectorPanel : UserControl
{
    public RegisterInspectorPanel() => InitializeComponent();

    private RegisterInspectorViewModel? Vm => DataContext as RegisterInspectorViewModel;

    /// <summary>"写入"按钮：将该行当前 Dec 值写入 RegisterBank</summary>
    private void WriteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: InspectorRow row })
            row.RequestWrite();
    }

    /// <summary>"删除"按钮：从列表移除该行</summary>
    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: InspectorRow row })
            Vm?.RemoveRowCommand.Execute(row);
    }
}
