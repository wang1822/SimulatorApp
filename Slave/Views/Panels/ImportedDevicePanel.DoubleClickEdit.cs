using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace SimulatorApp.Slave.Views.Panels;

public partial class ImportedDevicePanel
{
    private void RegisterDataGrid_BeginningEdit_DoubleClickOnly(object sender, DataGridBeginningEditEventArgs e)
    {
        // Keep the same trigger style as "当前值": mouse edit must be double-click.
        if (e.EditingEventArgs is MouseButtonEventArgs mouseArgs && mouseArgs.ClickCount < 2)
        {
            e.Cancel = true;
        }
    }

    private void RegisterDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid grid)
        {
            return;
        }

        if (e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        var cell = FindParent<DataGridCell>(source);
        if (cell is null)
        {
            return;
        }

        var row = FindParent<DataGridRow>(source);
        if (row is null || row.Item is null)
        {
            return;
        }

        if (cell.Column is null || cell.Column.IsReadOnly)
        {
            return;
        }

        grid.CurrentCell = new DataGridCellInfo(row.Item, cell.Column);
        if (!cell.IsEditing)
        {
            grid.BeginEdit(e);
            e.Handled = true;
        }
    }

    private void RegisterDataGrid_PreparingCellForEdit_CaretToEnd(object sender, DataGridPreparingCellForEditEventArgs e)
    {
        MoveCaretToEndWhenReady(e.EditingElement);
    }

    private static void MoveCaretToEndWhenReady(FrameworkElement editingElement)
    {
        static void MoveCaret(TextBox textBox)
        {
            textBox.Focus();
            var textLength = textBox.Text?.Length ?? 0;
            textBox.SelectionLength = 0;
            textBox.CaretIndex = textLength;
        }

        static TextBox? ResolveTextBox(FrameworkElement root)
            => root as TextBox ?? FindDescendant<TextBox>(root);

        // First pass: soon after edit starts.
        editingElement.Dispatcher.BeginInvoke(new Action(() =>
        {
            var first = ResolveTextBox(editingElement);
            if (first is not null)
            {
                MoveCaret(first);
            }
        }), DispatcherPriority.Background);

        // Second pass: after focus/click handlers run, force caret to tail.
        editingElement.Dispatcher.BeginInvoke(new Action(() =>
        {
            var second = ResolveTextBox(editingElement);
            if (second is not null)
            {
                MoveCaret(second);
            }
        }), DispatcherPriority.ApplicationIdle);
    }

    private static T? FindParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T target)
            {
                return target;
            }

            child = VisualTreeHelper.GetParent(child);
        }

        return null;
    }

    private static T? FindDescendant<T>(DependencyObject? root) where T : DependencyObject
    {
        if (root is null)
        {
            return null;
        }

        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T target)
            {
                return target;
            }

            var nested = FindDescendant<T>(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }
}
