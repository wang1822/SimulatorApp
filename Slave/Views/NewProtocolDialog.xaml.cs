using SimulatorApp.Slave.ViewModels;
using System.Windows;

namespace SimulatorApp.Slave.Views;

public partial class NewProtocolDialog : Window
{
    public NewProtocolDialog(NewProtocolDialogViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(NewProtocolDialogViewModel.DialogResult))
                DialogResult = vm.DialogResult;
        };
    }
}
