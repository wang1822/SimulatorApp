using CommunityToolkit.Mvvm.ComponentModel;
using SimulatorApp.Shared.Services;
using SimulatorApp.Slave.Models;
using SimulatorApp.Slave.Models.GasDetector;
using SimulatorApp.Slave.Services;
using System.Collections.ObjectModel;

namespace SimulatorApp.Slave.ViewModels;

/// <summary>气体检测 ViewModel（占位）</summary>
public partial class GasDetectorViewModel : DeviceViewModelBase
{
    private readonly GasDetectorModel _model = new();
    public override string DeviceName => "气体检测";
    protected override DeviceModelBase Model => _model;

    public GasDetectorViewModel(RegisterBank bank, RegisterMapService mapService)
        : base(bank, mapService) { InitAlarmItems(); FlushToRegisters(); }

    [ObservableProperty] private double _h2Concentration  = 0.0;
    [ObservableProperty] private double _cO2Concentration = 400.0;
    [ObservableProperty] private double _o2Concentration  = 20.9;
    [ObservableProperty] private double _cOConcentration  = 0.0;
    [ObservableProperty] private int    _runState         = 0;

    partial void OnH2ConcentrationChanged(double v)  => FlushToRegisters();
    partial void OnCO2ConcentrationChanged(double v) => FlushToRegisters();
    partial void OnO2ConcentrationChanged(double v)  => FlushToRegisters();
    partial void OnCOConcentrationChanged(double v)  => FlushToRegisters();
    partial void OnRunStateChanged(int v)            => FlushToRegisters();

    public IReadOnlyList<ComboItem> RunStateItems { get; } = new[]
    { new ComboItem(0,"正常"), new ComboItem(1,"低报"), new ComboItem(2,"高报") };

    public ObservableCollection<AlarmItem> AlarmItems { get; } = new();

    private void InitAlarmItems()
    {
        string[] names = { "H2低报", "H2高报", "CO2低报", "CO2高报", "O2低报", "O2高报", "CO低报", "CO高报" };
        for (int i = 0; i < names.Length; i++)
        {
            var item = new AlarmItem(names[i], 1 << i);
            item.CheckedChanged += () => FlushToRegisters();
            AlarmItems.Add(item);
        }
    }

    protected override void SyncToModel()
    {
        _model.H2Concentration  = H2Concentration;
        _model.CO2Concentration = CO2Concentration;
        _model.O2Concentration  = O2Concentration;
        _model.COConcentration  = COConcentration;
        _model.RunState         = RunState;
        _model.AlarmWord        = CalcBitmask(AlarmItems);
    }

    public override void GenerateData()
    {
        var rnd = new Random();
        H2Concentration  = Math.Round(rnd.NextDouble() * 50, 1);
        CO2Concentration = Math.Round(400 + rnd.NextDouble() * 200, 1);
        O2Concentration  = Math.Round(19 + rnd.NextDouble() * 3, 2);
        COConcentration  = Math.Round(rnd.NextDouble() * 20, 1);
        base.GenerateData();
    }

    public override void ClearAlarms()
    {
        foreach (var item in AlarmItems) item.IsChecked = false;
        base.ClearAlarms();
    }
}
