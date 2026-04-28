using CommunityToolkit.Mvvm.ComponentModel;
using SimulatorApp.Shared.Services;
using SimulatorApp.Slave.Models;
using SimulatorApp.Slave.Models.Mppt;
using SimulatorApp.Slave.Services;
using System.Collections.ObjectModel;

namespace SimulatorApp.Slave.ViewModels;

/// <summary>MPPT 光伏 ViewModel</summary>
public partial class MpptViewModel : DeviceViewModelBase
{
    private readonly MpptModel _model = new();
    public override string DeviceName => "MPPT 光伏";
    protected override DeviceModelBase Model => _model;

    public MpptViewModel(RegisterBank bank, RegisterMapService mapService) : base(bank, mapService)
    {
        InitAlarmItems(); FlushToRegisters();
    }

    [ObservableProperty] private double _outputVolt           = 600.0;
    [ObservableProperty] private double _outputCurrent        = 0.0;
    [ObservableProperty] private double _outputPower          = 0.0;
    [ObservableProperty] private double _pVTotalPower         = 0.0;
    [ObservableProperty] private double _dCVolt1              = 400.0;
    [ObservableProperty] private double _dCCurrent1           = 0.0;
    [ObservableProperty] private double _dCPower1             = 0.0;
    [ObservableProperty] private double _dCVolt2              = 400.0;
    [ObservableProperty] private double _dCCurrent2           = 0.0;
    [ObservableProperty] private double _dCPower2             = 0.0;
    [ObservableProperty] private double _dailyPVGenTotal      = 0.0;
    [ObservableProperty] private double _historyPvGenCapacity = 0.0;
    [ObservableProperty] private double _heatSinkTemp         = 25.0;
    [ObservableProperty] private int    _mpptOperatingMode    = 0;

    partial void OnOutputVoltChanged(double v)           => FlushToRegisters();
    partial void OnOutputCurrentChanged(double v)        => FlushToRegisters();
    partial void OnOutputPowerChanged(double v)          => FlushToRegisters();
    partial void OnPVTotalPowerChanged(double v)         => FlushToRegisters();
    partial void OnDCVolt1Changed(double v)              => FlushToRegisters();
    partial void OnDCCurrent1Changed(double v)           => FlushToRegisters();
    partial void OnDCPower1Changed(double v)             => FlushToRegisters();
    partial void OnDCVolt2Changed(double v)              => FlushToRegisters();
    partial void OnDCCurrent2Changed(double v)           => FlushToRegisters();
    partial void OnDCPower2Changed(double v)             => FlushToRegisters();
    partial void OnDailyPVGenTotalChanged(double v)      => FlushToRegisters();
    partial void OnHistoryPvGenCapacityChanged(double v) => FlushToRegisters();
    partial void OnHeatSinkTempChanged(double v)         => FlushToRegisters();
    partial void OnMpptOperatingModeChanged(int v)       => FlushToRegisters();

    public IReadOnlyList<ComboItem> MpptModeItems { get; } = new[]
    {
        new ComboItem(0, "待机"), new ComboItem(1, "自检"), new ComboItem(2, "正常")
    };

    public ObservableCollection<AlarmItem> AlarmItems { get; } = new();
    public ObservableCollection<AlarmItem> FaultItems { get; } = new();

    private void InitAlarmItems()
    {
        for (int i = 0; i < 8; i++)
        {
            var a = new AlarmItem($"告警-Bit{i}", 1 << i);
            a.CheckedChanged += () => FlushToRegisters();
            AlarmItems.Add(a);
            var f = new AlarmItem($"故障-Bit{i}", 1 << i);
            f.CheckedChanged += () => FlushToRegisters();
            FaultItems.Add(f);
        }
    }

    protected override void SyncToModel()
    {
        _model.OutputVolt           = OutputVolt;
        _model.OutputCurrent        = OutputCurrent;
        _model.OutputPower          = OutputPower;
        _model.PVTotalPower         = PVTotalPower;
        _model.DCVolt1              = DCVolt1;
        _model.DCCurrent1           = DCCurrent1;
        _model.DCPower1             = DCPower1;
        _model.DCVolt2              = DCVolt2;
        _model.DCCurrent2           = DCCurrent2;
        _model.DCPower2             = DCPower2;
        _model.DailyPVGenTotal      = DailyPVGenTotal;
        _model.HistoryPvGenCapacity = HistoryPvGenCapacity;
        _model.HeatSinkTemp         = HeatSinkTemp;
        _model.MpptOperatingMode    = MpptOperatingMode;
        _model.MPPTAlarm1 = CalcBitmask(AlarmItems);
        _model.MPPTFault1 = CalcBitmask(FaultItems);
    }

    public override void GenerateData()
    {
        var rnd = new Random();
        DCVolt1         = Math.Round(300 + rnd.NextDouble() * 200, 1);
        DCCurrent1      = Math.Round(rnd.NextDouble() * 20, 1);
        DCPower1        = Math.Round(DCVolt1 * DCCurrent1, 0);
        DCVolt2         = Math.Round(300 + rnd.NextDouble() * 200, 1);
        DCCurrent2      = Math.Round(rnd.NextDouble() * 20, 1);
        DCPower2        = Math.Round(DCVolt2 * DCCurrent2, 0);
        OutputVolt      = Math.Round(580 + rnd.NextDouble() * 40, 1);
        HeatSinkTemp    = Math.Round(25 + rnd.NextDouble() * 30, 1);
        MpptOperatingMode = 2;
        base.GenerateData();
    }

    public override void ClearAlarms()
    {
        foreach (var item in AlarmItems) item.IsChecked = false;
        foreach (var item in FaultItems) item.IsChecked = false;
        base.ClearAlarms();
    }
}
