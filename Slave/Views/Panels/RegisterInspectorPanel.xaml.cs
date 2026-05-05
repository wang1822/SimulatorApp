using SimulatorApp.Slave.ViewModels;
using SimulatorApp.Master.Views;
using System.Linq;
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

    private void DeleteSelectedRows_Click(object sender, RoutedEventArgs e)
    {
        InlineProtocolDraftGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        InlineProtocolDraftGrid.CommitEdit(DataGridEditingUnit.Row, true);

        var selectedRows = InlineProtocolDraftGrid.SelectedCells
            .Select(cell => cell.Item)
            .OfType<InlineProtocolDraftRow>()
            .Concat(InlineProtocolDraftGrid.SelectedItems.OfType<InlineProtocolDraftRow>())
            .Distinct()
            .ToList();

        if (selectedRows.Count == 0 && InlineProtocolDraftGrid.CurrentItem is InlineProtocolDraftRow currentRow)
        {
            selectedRows.Add(currentRow);
        }

        if (selectedRows.Count == 0)
        {
            InlineSaveErrorTextBlock.Text = "请先选中要删除的记录行。";
            return;
        }

        var removedCount = Vm?.RemoveRowsByAddresses(selectedRows.Select(row => row.Address)) ?? 0;
        InlineProtocolDraftGrid.SelectedCells.Clear();
        InlineProtocolDraftGrid.SelectedItems.Clear();

        InlineSaveErrorTextBlock.Text = removedCount > 0
            ? $"已删除选中记录 {removedCount} 条。"
            : "未找到可删除的选中记录。";
    }

    private void ClearRowsWithPassword_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new PasswordDialog { Owner = Window.GetWindow(this) ?? Application.Current.MainWindow };
        dlg.ShowDialog();
        if (!dlg.Confirmed) return;

        Vm?.ClearRowsCommand.Execute(null);
    }
}
