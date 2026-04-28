using SimulatorApp.Master.Models;
using SimulatorApp.Master.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace SimulatorApp.Master.Views;

/// <summary>
/// 主站面板 code-behind — DataContext 为 MasterViewModel
/// </summary>
public partial class MasterPanel : UserControl
{
    public MasterPanel()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MasterViewModel vm)
            vm.ScrollRequested += OnScrollRequested;
    }

    /// <summary>
    /// 响应 ViewModel 的滚动请求：选中行并 ScrollIntoView，焦点跟随。
    /// </summary>
    private void OnScrollRequested(RegisterDisplayRow row)
    {
        if (DataContext is not MasterViewModel vm) return;
        var grid = vm.ActiveTabIndex == 0 ? TelGrid : CtrlGrid;
        grid.SelectedItem = row;
        grid.ScrollIntoView(row);
        grid.Focus();
    }

    /// <summary>遥控"当前值" TextBox 获焦时全选，方便直接覆盖输入。</summary>
    private void CtrlWriteCell_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb) tb.SelectAll();
    }

    /// <summary>
    /// 遥控"当前值" TextBox 失焦时触发 FC16 写入。
    /// 使用常驻 TextBox（非 DataGrid 编辑模式），完全绕开 DataGrid 的
    /// PreviewKeyDown / CellEditEnding 时序问题：LostFocus 时 WriteValue
    /// 已由 UpdateSourceTrigger=PropertyChanged 实时同步到 ViewModel，
    /// 直接调用命令即可拿到最新值。
    /// </summary>
    private void CtrlWriteCell_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox tb) return;
        if (string.IsNullOrWhiteSpace(tb.Text)) return;
        if (tb.DataContext is not RegisterDisplayRow row) return;
        if (DataContext is not MasterViewModel vm) return;
        vm.WriteControlRowCommand.Execute(row);
    }
}
