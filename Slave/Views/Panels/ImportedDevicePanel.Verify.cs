using SimulatorApp.Slave.ViewModels;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace SimulatorApp.Slave.Views.Panels;

public partial class ImportedDevicePanel
{
    private int _unverifiedSearchIndex = -1;

    private void NextUnverified_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ImportedDeviceViewModel vm) return;

        var source = vm.FilteredRows.Cast<ImportedRegisterRow>()
                                    .Where(r => !r.IsPending)
                                    .ToList();
        if (source.Count == 0) return;

        int start = (_unverifiedSearchIndex + 1) % source.Count;
        ImportedRegisterRow? target = null;
        int targetIndex = -1;

        for (int i = 0; i < source.Count; i++)
        {
            int idx = (start + i) % source.Count;
            if (!source[idx].IsVerified)
            {
                target = source[idx];
                targetIndex = idx;
                break;
            }
        }

        if (target is null)
        {
            _unverifiedSearchIndex = -1;
            return;
        }

        _unverifiedSearchIndex = targetIndex;
        RegisterDataGrid.SelectedItem = target;
        RegisterDataGrid.CurrentCell = new DataGridCellInfo(target, RegisterDataGrid.Columns[0]);
        RegisterDataGrid.ScrollIntoView(target);
        RegisterDataGrid.Focus();
    }
}
