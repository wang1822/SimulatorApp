using SimulatorApp.Slave.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace SimulatorApp.Slave.Views;

public partial class GpioHilWindow : Window
{
    public GpioHilWindow()
    {
        InitializeComponent();
    }

    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is GpioHilViewModel vm)
            vm.Password = PasswordBox.Password;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void AutoWriteTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (DataContext is not GpioHilViewModel vm)
            return;

        if (sender is not FrameworkElement { DataContext: { } row })
            return;

        await vm.WritePointFromCellAsync(row);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is IDisposable disposable)
            disposable.Dispose();

        base.OnClosed(e);
    }
}
