using System.Windows.Controls;
using System.Windows.Input;
using SimulatorApp.Slave.ViewModels;

namespace SimulatorApp.Slave.Views.Panels;

public partial class ImportedDevicePanel
{
    private void RegisterDataGrid_BeginningEdit_GuardCurrentValue(object sender, DataGridBeginningEditEventArgs e)
    {
        if (e.EditingEventArgs is MouseButtonEventArgs mouseArgs && mouseArgs.ClickCount < 2)
        {
            e.Cancel = true;
            return;
        }

        if (e.Column == CurrentValueColumn
            && e.Row.DataContext is ImportedRegisterRow row
            && !row.CanWriteCurrentValue)
        {
            e.Cancel = true;
        }
    }
}
