using CommunityToolkit.Mvvm.ComponentModel;
using SimulatorApp.Shared.Services;
using SimulatorApp.Slave.Models;
using SimulatorApp.Slave.Models.StsInstrument;
using SimulatorApp.Slave.Services;
using System.Collections.ObjectModel;

namespace SimulatorApp.Slave.ViewModels;

/// <summary>STS 仪表 ViewModel</summary>
public partial class StsInstrumentViewModel : DeviceViewModelBase
{
    private readonly StsInstrumentModel _model = new();
    public override string DeviceName => "STS 仪表";
    protected override DeviceModelBase Model => _model;

    public StsInstrumentViewModel(RegisterBank bank, RegisterMapService mapService)
        : base(bank, mapService) { InitAlarmItems(); FlushToRegisters(); }

    [ObservableProperty] private double _gridVoltA          = 220.0;
    [ObservableProperty] private double _gridVoltB          = 220.0;
    [ObservableProperty] private double _gridVoltC          = 220.0;
    [ObservableProperty] private double _gridCurrentA       = 0.0;
    [ObservableProperty] private double _gridCurrentB       = 0.0;
    [ObservableProperty] private double _gridCurrentC       = 0.0;
    [ObservableProperty] private double _stsGridFrequency   = 50.0;
    [ObservableProperty] private double _generatorVoltA     = 0.0;
    [ObservableProperty] private double _generatorVoltB     = 0.0;
    [ObservableProperty] private double _generatorVoltC     = 0.0;
    [ObservableProperty] private double _generatorFrequency = 0.0;
    [ObservableProperty] private double _inverterVoltA      = 220.0;
    [ObservableProperty] private double _inverterFrequency  = 50.0;
    [ObservableProperty] private int    _stsOperatingMode   = 0;

    partial void OnGridVoltAChanged(double v)         => FlushToRegisters();
    partial void OnGridVoltBChanged(double v)         => FlushToRegisters();
    partial void OnGridVoltCChanged(double v)         => FlushToRegisters();
    partial void OnGridCurrentAChanged(double v)      => FlushToRegisters();
    partial void OnGridCurrentBChanged(double v)      => FlushToRegisters();
    partial void OnGridCurrentCChanged(double v)      => FlushToRegisters();
    partial void OnStsGridFrequencyChanged(double v)  => FlushToRegisters();
    partial void OnGeneratorVoltAChanged(double v)    => FlushToRegisters();
    partial void OnGeneratorVoltBChanged(double v)    => FlushToRegisters();
    partial void OnGeneratorVoltCChanged(double v)    => FlushToRegisters();
    partial void OnGeneratorFrequencyChanged(double v)=> FlushToRegisters();
    partial void OnInverterVoltAChanged(double v)     => FlushToRegisters();
    partial void OnInverterFrequencyChanged(double v) => FlushToRegisters();
    partial void OnStsOperatingModeChanged(int v)     => FlushToRegisters();

    public IReadOnlyList<ComboItem> ModeItems { get; } = new[]
    { new ComboItem(0,"待机"), new ComboItem(1,"自检"), new ComboItem(2,"正常") };

    public ObservableCollection<AlarmItem> AlarmItems { get; } = new();
    public ObservableCollection<AlarmItem> FaultItems { get; } = new();

    private void InitAlarmItems()
    {
        for (int i = 0; i < 8; i++)
        {
            var a = new AlarmItem($"STS告警-Bit{i}", 1 << i);
            a.CheckedChanged += () => FlushToRegisters();
            AlarmItems.Add(a);
            var f = new AlarmItem($"STS故障-Bit{i}", 1 << i);
            f.CheckedChanged += () => FlushToRegisters();
            FaultItems.Add(f);
        }
    }

    protected override void SyncToModel()
    {
        _model.GridVoltA          = GridVoltA;
        _model.GridVoltB          = GridVoltB;
        _model.GridVoltC          = GridVoltC;
        _model.GridCurrentA       = GridCurrentA;
        _model.GridCurrentB       = GridCurrentB;
        _model.GridCurrentC       = GridCurrentC;
        _model.StsGridFrequency   = StsGridFrequency;
        _model.GeneratorVoltA     = GeneratorVoltA;
        _model.GeneratorVoltB     = GeneratorVoltB;
        _model.GeneratorVoltC     = GeneratorVoltC;
        _model.GeneratorFrequency = GeneratorFrequency;
        _model.InverterVoltA      = InverterVoltA;
        _model.InverterFrequency  = InverterFrequency;
        _model.StsOperatingMode   = StsOperatingMode;
        _model.STSAlarm1 = CalcBitmask(AlarmItems);
        _model.STSFault1 = CalcBitmask(FaultItems);
    }

    public override void ClearAlarms()
    {
        foreach (var item in AlarmItems) item.IsChecked = false;
        foreach (var item in FaultItems) item.IsChecked = false;
        base.ClearAlarms();
    }
}
