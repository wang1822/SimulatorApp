using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SimulatorApp.Slave.Views.Panels;

public partial class ImportedDevicePanel
{
    private void RegisterDataGrid_CopyMenuItem_Click(object sender, RoutedEventArgs e)
    {
        CopySelectedCells();
    }

    private void CurrentValue_CopyMenuItem_Click(object sender, RoutedEventArgs e)
    {
        CopySelectedCells();
    }

    private void CopySelectedCells()
    {
        if (RegisterDataGrid is null)
        {
            return;
        }

        // Ensure in-place edited value is committed before copy.
        RegisterDataGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        RegisterDataGrid.CommitEdit(DataGridEditingUnit.Row, true);
        RegisterDataGrid.Focus();

        if (ApplicationCommands.Copy.CanExecute(null, RegisterDataGrid))
        {
            ApplicationCommands.Copy.Execute(null, RegisterDataGrid);
        }
    }
}
