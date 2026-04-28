using CommunityToolkit.Mvvm.ComponentModel;
using SimulatorApp.Shared.Services;
using SimulatorApp.Slave.Models;
using SimulatorApp.Slave.Models.DieselGenerator;
using SimulatorApp.Slave.Services;
using System.Collections.ObjectModel;

namespace SimulatorApp.Slave.ViewModels;

/// <summary>柴发 ViewModel（占位，待协议文档到位后补充）</summary>
public partial class DieselGeneratorViewModel : DeviceViewModelBase
{
    private readonly DieselGeneratorModel _model = new();
    public override string DeviceName => "柴发";
    protected override DeviceModelBase Model => _model;

    public DieselGeneratorViewModel(RegisterBank bank, RegisterMapService mapService)
        : base(bank, mapService) { InitAlarmItems(); FlushToRegisters(); }

    [ObservableProperty] private double _phaseAVolt    = 220.0;
    [ObservableProperty] private double _phaseBVolt    = 220.0;
    [ObservableProperty] private double _phaseCVolt    = 220.0;
    [ObservableProperty] private double _phaseACurrent = 0.0;
    [ObservableProperty] private double _frequency     = 50.0;
    [ObservableProperty] private double _activePower   = 0.0;
    [ObservableProperty] private double _oilPressure   = 0.0;
    [ObservableProperty] private double _coolantTemp   = 25.0;
    [ObservableProperty] private double _batteryVolt   = 24.0;
    [ObservableProperty] private int    _runHours      = 0;
    [ObservableProperty] private int    _runState      = 0;

    partial void OnPhaseAVoltChanged(double v)    => FlushToRegisters();
    partial void OnPhaseBVoltChanged(double v)    => FlushToRegisters();
    partial void OnPhaseCVoltChanged(double v)    => FlushToRegisters();
    partial void OnPhaseACurrentChanged(double v) => FlushToRegisters();
    partial void OnFrequencyChanged(double v)     => FlushToRegisters();
    partial void OnActivePowerChanged(double v)   => FlushToRegisters();
    partial void OnOilPressureChanged(double v)   => FlushToRegisters();
    partial void OnCoolantTempChanged(double v)   => FlushToRegisters();
    partial void OnBatteryVoltChanged(double v)   => FlushToRegisters();
    partial void OnRunHoursChanged(int v)         => FlushToRegisters();
    partial void OnRunStateChanged(int v)         => FlushToRegisters();

    public IReadOnlyList<ComboItem> RunStateItems { get; } = new[]
    { new ComboItem(0,"停止"), new ComboItem(1,"启动"), new ComboItem(2,"运行"), new ComboItem(3,"故障") };

    public ObservableCollection<AlarmItem> FaultItems { get; } = new();
    public ObservableCollection<AlarmItem> AlarmItems { get; } = new();

    private void InitAlarmItems()
    {
        for (int i = 0; i < 8; i++)
        {
            var f = new AlarmItem($"故障-Bit{i}", 1 << i);
            f.CheckedChanged += () => FlushToRegisters();
            FaultItems.Add(f);
            var a = new AlarmItem($"告警-Bit{i}", 1 << i);
            a.CheckedChanged += () => FlushToRegisters();
            AlarmItems.Add(a);
        }
    }

    protected override void SyncToModel()
    {
        _model.PhaseAVolt    = PhaseAVolt;
        _model.PhaseBVolt    = PhaseBVolt;
        _model.PhaseCVolt    = PhaseCVolt;
        _model.PhaseACurrent = PhaseACurrent;
        _model.Frequency     = Frequency;
        _model.ActivePower   = ActivePower;
        _model.OilPressure   = OilPressure;
        _model.CoolantTemp   = CoolantTemp;
        _model.BatteryVolt   = BatteryVolt;
        _model.RunHours      = RunHours;
        _model.RunState      = RunState;
        _model.Fault1 = CalcBitmask(FaultItems);
        _model.Alarm1 = CalcBitmask(AlarmItems);
    }

    public override void ClearAlarms()
    {
        foreach (var item in FaultItems) item.IsChecked = false;
        foreach (var item in AlarmItems) item.IsChecked = false;
        base.ClearAlarms();
    }
}
