using SimulatorApp.Slave.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace SimulatorApp.Slave.Views.Panels;

public partial class ImportedDevicePanel : UserControl
{
    public ImportedDevicePanel() => InitializeComponent();

    private void AddRow_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ImportedDeviceViewModel vm) return;
        vm.AddNewEmptyRow();
        // 滚动到末行，让新增的挂起行可见
        if (RegisterDataGrid.Items.Count > 0)
        {
            RegisterDataGrid.UpdateLayout();
            RegisterDataGrid.ScrollIntoView(RegisterDataGrid.Items[^1]);
        }
    }

    private void DeleteRow_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ImportedDeviceViewModel vm) return;
        if (sender is not FrameworkElement fe || fe.Tag is not ImportedRegisterRow row) return;
        vm.TryDeleteRow(row);
    }

    // 挂起行的地址 TextBox 首次加载时自动获焦（仅 AddressText 仍为空时触发，避免滚动回来重复抢焦）
    private void PendingAddressBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox tb) return;
        if (tb.DataContext is not ImportedRegisterRow row || !row.IsPending) return;
        if (!string.IsNullOrEmpty(row.AddressText)) return;
        tb.Dispatcher.BeginInvoke(DispatcherPriority.Input, (Action)(() =>
        {
            if (!tb.IsFocused) tb.Focus();
        }));
    }

    // 挂起行的地址/中文名 TextBox 失焦时尝试提交
    private async void PendingRow_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe) return;
        if (fe.DataContext is not ImportedRegisterRow row || !row.IsPending) return;
        if (DataContext is not ImportedDeviceViewModel vm) return;
        await vm.TryCommitPendingRowAsync(row);
    }

    // Enter 键触发搜索
    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is ImportedDeviceViewModel vm)
        {
            vm.SearchCommand.Execute(null);
            e.Handled = true;
        }
    }

    // 挂起行禁止进入 DataGrid 编辑模式（避免 CellEditingTemplate 覆盖内联 TextBox）
    private void RegisterDataGrid_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
    {
        if (e.Row.DataContext is ImportedRegisterRow row && row.IsPending)
            e.Cancel = true;
    }

    // DataGrid.BeginEdit 有时 MoveFocus 在布局就绪前已执行，GotFocus 未能触发。
    // 在 Input 优先级补一次焦点，若 TextBox 已有焦点则跳过，避免覆盖用户正在输入的内容。
    private void WriteCell_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox tb) return;
        tb.Dispatcher.BeginInvoke(DispatcherPriority.Input, (Action)(() =>
        {
            if (!tb.IsFocused)
                tb.Focus();
        }));
    }

    private void WriteCell_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && tb.DataContext is ImportedRegisterRow row)
        {
            tb.Text = row.CurrentValueDisplay;
            tb.SelectAll();
        }
    }

    private void WriteCell_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && tb.DataContext is ImportedRegisterRow row)
            row.TryCommitWrite();
    }
}
