using CommunityToolkit.Mvvm.ComponentModel;

namespace SimulatorApp.Slave.Models;

public partial class GpioHilPoint : ObservableObject
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _position = "";
    [ObservableProperty] private string _currentValue = "0";
    [ObservableProperty] private string _status = "";
}
