using CommunityToolkit.Mvvm.ComponentModel;
using SimulatorApp.Shared.Services;
using SimulatorApp.Slave.Models;
using SimulatorApp.Slave.Models.ExternalMeter;
using SimulatorApp.Slave.Services;

namespace SimulatorApp.Slave.ViewModels;

/// <summary>外部电表 ViewModel（储能电表复用此基类）</summary>
public partial class ExternalMeterViewModel : DeviceViewModelBase
{
    private readonly ExternalMeterModel _model;
    public override string DeviceName => _model.DeviceName;
    protected override DeviceModelBase Model => _model;

    public ExternalMeterViewModel(RegisterBank bank, RegisterMapService mapService,
        ExternalMeterModel? model = null) : base(bank, mapService)
    {
        _model = model ?? new ExternalMeterModel();
        FlushToRegisters();
    }

    [ObservableProperty] private double _l1PhaseVoltage   = 220.0;
    [ObservableProperty] private double _l2PhaseVoltage   = 220.0;
    [ObservableProperty] private double _l3PhaseVoltage   = 220.0;
    [ObservableProperty] private double _l1Current        = 0.0;
    [ObservableProperty] private double _l2Current        = 0.0;
    [ObservableProperty] private double _l3Current        = 0.0;
    [ObservableProperty] private double _l1ActivePower    = 0.0;
    [ObservableProperty] private double _l2ActivePower    = 0.0;
    [ObservableProperty] private double _l3ActivePower    = 0.0;
    [ObservableProperty] private double _tolActivePower   = 0.0;
    [ObservableProperty] private double _tolReactivePower = 0.0;
    [ObservableProperty] private double _tolPowerFactor   = 1.000;
    [ObservableProperty] private double _frequency        = 50.0;
    [ObservableProperty] private double _posActiveCharge  = 0.0;
    [ObservableProperty] private double _revActiveCharge  = 0.0;
    [ObservableProperty] private double _l12LineVoltage   = 380.0;
    [ObservableProperty] private double _l23LineVoltage   = 380.0;
    [ObservableProperty] private double _l31LineVoltage   = 380.0;
    [ObservableProperty] private int    _timeoutFlag      = 1;

    partial void OnL1PhaseVoltageChanged(double v)   => FlushToRegisters();
    partial void OnL2PhaseVoltageChanged(double v)   => FlushToRegisters();
    partial void OnL3PhaseVoltageChanged(double v)   => FlushToRegisters();
    partial void OnL1CurrentChanged(double v)        => FlushToRegisters();
    partial void OnL2CurrentChanged(double v)        => FlushToRegisters();
    partial void OnL3CurrentChanged(double v)        => FlushToRegisters();
    partial void OnL1ActivePowerChanged(double v)    => FlushToRegisters();
    partial void OnL2ActivePowerChanged(double v)    => FlushToRegisters();
    partial void OnL3ActivePowerChanged(double v)    => FlushToRegisters();
    partial void OnTolActivePowerChanged(double v)   => FlushToRegisters();
    partial void OnTolReactivePowerChanged(double v) => FlushToRegisters();
    partial void OnTolPowerFactorChanged(double v)   => FlushToRegisters();
    partial void OnFrequencyChanged(double v)        => FlushToRegisters();
    partial void OnPosActiveChargeChanged(double v)  => FlushToRegisters();
    partial void OnRevActiveChargeChanged(double v)  => FlushToRegisters();
    partial void OnL12LineVoltageChanged(double v)   => FlushToRegisters();
    partial void OnL23LineVoltageChanged(double v)   => FlushToRegisters();
    partial void OnL31LineVoltageChanged(double v)   => FlushToRegisters();
    partial void OnTimeoutFlagChanged(int v)         => FlushToRegisters();

    public IReadOnlyList<ComboItem> OnlineItems { get; } = new[]
    { new ComboItem(0, "离线"), new ComboItem(1, "在线") };

    protected override void SyncToModel()
    {
        _model.L1PhaseVoltage   = L1PhaseVoltage;
        _model.L2PhaseVoltage   = L2PhaseVoltage;
        _model.L3PhaseVoltage   = L3PhaseVoltage;
        _model.L1Current        = L1Current;
        _model.L2Current        = L2Current;
        _model.L3Current        = L3Current;
        _model.L1ActivePower    = L1ActivePower;
        _model.L2ActivePower    = L2ActivePower;
        _model.L3ActivePower    = L3ActivePower;
        _model.TolActivePower   = TolActivePower;
        _model.TolReactivePower = TolReactivePower;
        _model.TolPowerFactor   = TolPowerFactor;
        _model.Frequency        = Frequency;
        _model.PosActiveCharge  = PosActiveCharge;
        _model.RevActiveCharge  = RevActiveCharge;
        _model.L12LineVoltage   = L12LineVoltage;
        _model.L23LineVoltage   = L23LineVoltage;
        _model.L31LineVoltage   = L31LineVoltage;
        _model.TimeoutFlag      = TimeoutFlag;
    }

    public override void GenerateData()
    {
        var rnd = new Random();
        L1PhaseVoltage = Math.Round(218 + rnd.NextDouble() * 6, 3);
        L2PhaseVoltage = Math.Round(218 + rnd.NextDouble() * 6, 3);
        L3PhaseVoltage = Math.Round(218 + rnd.NextDouble() * 6, 3);
        L1Current      = Math.Round(rnd.NextDouble() * 100, 3);
        L2Current      = Math.Round(rnd.NextDouble() * 100, 3);
        L3Current      = Math.Round(rnd.NextDouble() * 100, 3);
        Frequency      = Math.Round(49.9 + rnd.NextDouble() * 0.2, 3);
        TolPowerFactor = Math.Round(0.9 + rnd.NextDouble() * 0.1, 3);
        TimeoutFlag    = 1;
        base.GenerateData();
    }
}
