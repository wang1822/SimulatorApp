using CommunityToolkit.Mvvm.ComponentModel;
using SimulatorApp.Shared.Services;
using SimulatorApp.Slave.Models;
using SimulatorApp.Slave.Models.AirConditioner;
using SimulatorApp.Slave.Services;
using System.Collections.ObjectModel;

namespace SimulatorApp.Slave.ViewModels;

/// <summary>空调 ViewModel</summary>
public partial class AirConditionerViewModel : DeviceViewModelBase
{
    private readonly AirConditionerModel _model = new();
    public override string DeviceName => "空调";
    protected override DeviceModelBase Model => _model;

    public AirConditionerViewModel(RegisterBank bank, RegisterMapService mapService) : base(bank, mapService)
    { InitAlarmItems(); FlushToRegisters(); }

    [ObservableProperty] private double _airOutTemp1          = 25.0;
    [ObservableProperty] private double _airInterTemp1        = 22.0;
    [ObservableProperty] private int    _airInterRh1          = 50;
    [ObservableProperty] private double _airInterCoilTemp     = 18.0;
    [ObservableProperty] private double _airInputCurrent      = 0.0;
    [ObservableProperty] private int    _airACVoltage         = 220;
    [ObservableProperty] private double _airCompressorCurrent = 0.0;
    [ObservableProperty] private int    _airExterFanSpeed     = 0;
    [ObservableProperty] private int    _airInterFanSpeed     = 0;
    [ObservableProperty] private int    _airCompressorFre     = 0;
    [ObservableProperty] private int    _airCoolPoint         = 26;
    [ObservableProperty] private int    _airHeatPoint         = 10;
    [ObservableProperty] private int    _airRHDiffPoint       = 65;
    [ObservableProperty] private int    _airAllRunState       = 0;
    [ObservableProperty] private int    _airExterRunState     = 0;
    [ObservableProperty] private int    _airInterRunState     = 0;
    [ObservableProperty] private int    _airCompressRunState  = 0;
    [ObservableProperty] private int    _airHeatRunState      = 0;
    [ObservableProperty] private int    _specificPattern      = 0;

    partial void OnAirOutTemp1Changed(double v)         => FlushToRegisters();
    partial void OnAirInterTemp1Changed(double v)       => FlushToRegisters();
    partial void OnAirInterRh1Changed(int v)            => FlushToRegisters();
    partial void OnAirInterCoilTempChanged(double v)    => FlushToRegisters();
    partial void OnAirInputCurrentChanged(double v)     => FlushToRegisters();
    partial void OnAirACVoltageChanged(int v)           => FlushToRegisters();
    partial void OnAirCompressorCurrentChanged(double v)=> FlushToRegisters();
    partial void OnAirExterFanSpeedChanged(int v)       => FlushToRegisters();
    partial void OnAirInterFanSpeedChanged(int v)       => FlushToRegisters();
    partial void OnAirCompressorFreChanged(int v)       => FlushToRegisters();
    partial void OnAirCoolPointChanged(int v)           => FlushToRegisters();
    partial void OnAirHeatPointChanged(int v)           => FlushToRegisters();
    partial void OnAirRHDiffPointChanged(int v)         => FlushToRegisters();
    partial void OnAirAllRunStateChanged(int v)         => FlushToRegisters();
    partial void OnAirExterRunStateChanged(int v)       => FlushToRegisters();
    partial void OnAirInterRunStateChanged(int v)       => FlushToRegisters();
    partial void OnAirCompressRunStateChanged(int v)    => FlushToRegisters();
    partial void OnAirHeatRunStateChanged(int v)        => FlushToRegisters();
    partial void OnSpecificPatternChanged(int v)        => FlushToRegisters();

    public IReadOnlyList<ComboItem> RunStateItems { get; } = new[]
    {
        new ComboItem(0,"停"), new ComboItem(1,"制冷"), new ComboItem(2,"制热"),
        new ComboItem(3,"除湿"), new ComboItem(4,"送风"), new ComboItem(5,"单独加热")
    };
    public IReadOnlyList<ComboItem> OnOffItems { get; } = new[]
    { new ComboItem(0,"停"), new ComboItem(1,"开") };
    public IReadOnlyList<ComboItem> PatternItems { get; } = new[]
    { new ComboItem(0,"正常"), new ComboItem(1,"单独加热"), new ComboItem(2,"单独除湿") };

    public ObservableCollection<AlarmItem> AlarmItems { get; } = new();
    public ObservableCollection<AlarmItem> FaultItems { get; } = new();

    private void InitAlarmItems()
    {
        void Add(ObservableCollection<AlarmItem> col, string label)
        {
            var item = new AlarmItem(label, 1);
            item.CheckedChanged += () => FlushToRegisters();
            col.Add(item);
        }
        Add(AlarmItems, "高温告警"); Add(AlarmItems, "低温告警");
        Add(AlarmItems, "高湿告警"); Add(AlarmItems, "压缩机告警"); Add(AlarmItems, "风机告警");
        Add(FaultItems, "高温故障"); Add(FaultItems, "低温故障");
        Add(FaultItems, "压缩机故障"); Add(FaultItems, "风机故障");
    }

    protected override void SyncToModel()
    {
        _model.AirOutTemp1          = AirOutTemp1;
        _model.AirInterTemp1        = AirInterTemp1;
        _model.AirInterRh1          = AirInterRh1;
        _model.AirInterCoilTemp     = AirInterCoilTemp;
        _model.AirInputCurrent      = AirInputCurrent;
        _model.AirACVoltage         = AirACVoltage;
        _model.AirCompressorCurrent = AirCompressorCurrent;
        _model.AirExterFanSpeed     = AirExterFanSpeed;
        _model.AirInterFanSpeed     = AirInterFanSpeed;
        _model.AirCompressorFre     = AirCompressorFre;
        _model.AirCoolPoint         = AirCoolPoint;
        _model.AirHeatPoint         = AirHeatPoint;
        _model.AirRHDiffPoint       = AirRHDiffPoint;
        _model.AirAllRunState       = AirAllRunState;
        _model.AirExterRunState     = AirExterRunState;
        _model.AirInterRunState     = AirInterRunState;
        _model.AirCompressRunState  = AirCompressRunState;
        _model.AirHeatRunState      = AirHeatRunState;
        _model.SpecificPattern      = SpecificPattern;

        // 各告警/故障为独立寄存器（0/1值）
        var alarmLabels  = new[] {"AirHighTempAlarm","AirLowTempAlarm","AirHighRHAlarm","AirCompressorAlarm","AirFanAlarm"};
        var faultLabels  = new[] {"AirHighTempFault","AirLowTempFault","AirCompressorFault","AirFanFault"};
        _model.AirHighTempAlarm   = (ushort)(AlarmItems.Count > 0 && AlarmItems[0].IsChecked ? 1 : 0);
        _model.AirLowTempAlarm    = (ushort)(AlarmItems.Count > 1 && AlarmItems[1].IsChecked ? 1 : 0);
        _model.AirHighRHAlarm     = (ushort)(AlarmItems.Count > 2 && AlarmItems[2].IsChecked ? 1 : 0);
        _model.AirCompressorAlarm = (ushort)(AlarmItems.Count > 3 && AlarmItems[3].IsChecked ? 1 : 0);
        _model.AirFanAlarm        = (ushort)(AlarmItems.Count > 4 && AlarmItems[4].IsChecked ? 1 : 0);
        _model.AirHighTempFault   = (ushort)(FaultItems.Count > 0 && FaultItems[0].IsChecked ? 1 : 0);
        _model.AirLowTempFault    = (ushort)(FaultItems.Count > 1 && FaultItems[1].IsChecked ? 1 : 0);
        _model.AirCompressorFault = (ushort)(FaultItems.Count > 2 && FaultItems[2].IsChecked ? 1 : 0);
        _model.AirFanFault        = (ushort)(FaultItems.Count > 3 && FaultItems[3].IsChecked ? 1 : 0);
    }

    public override void GenerateData()
    {
        var rnd = new Random();
        AirOutTemp1  = Math.Round(15 + rnd.NextDouble() * 20, 1);
        AirInterTemp1= Math.Round(18 + rnd.NextDouble() * 10, 1);
        AirInterRh1  = (int)(40 + rnd.NextDouble() * 30);
        AirACVoltage = (int)(210 + rnd.NextDouble() * 20);
        AirAllRunState = 1;
        base.GenerateData();
    }

    public override void ClearAlarms()
    {
        foreach (var item in AlarmItems) item.IsChecked = false;
        foreach (var item in FaultItems) item.IsChecked = false;
        base.ClearAlarms();
    }
}
