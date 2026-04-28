using CommunityToolkit.Mvvm.ComponentModel;
using SimulatorApp.Shared.Services;
using SimulatorApp.Slave.Models;
using SimulatorApp.Slave.Models.StsControl;
using SimulatorApp.Slave.Services;

namespace SimulatorApp.Slave.ViewModels;

/// <summary>STS 控制IO卡 ViewModel</summary>
public partial class StsControlViewModel : DeviceViewModelBase
{
    private readonly StsControlModel _model = new();
    public override string DeviceName => "STS 控制IO卡";
    protected override DeviceModelBase Model => _model;

    public StsControlViewModel(RegisterBank bank, RegisterMapService mapService)
        : base(bank, mapService) { FlushToRegisters(); }

    [ObservableProperty] private double _l1PhaseVoltage  = 220.0;
    [ObservableProperty] private double _l2PhaseVoltage  = 220.0;
    [ObservableProperty] private double _l3PhaseVoltage  = 220.0;
    [ObservableProperty] private double _l1Current       = 0.0;
    [ObservableProperty] private double _l2Current       = 0.0;
    [ObservableProperty] private double _l3Current       = 0.0;
    [ObservableProperty] private double _tolActivePower  = 0.0;
    [ObservableProperty] private double _frequency       = 50.0;
    [ObservableProperty] private double _posActiveCharge = 0.0;
    [ObservableProperty] private int    _gridDisconnect       = 0;
    [ObservableProperty] private int    _generatorDisconnect  = 0;
    [ObservableProperty] private int    _gridFeedback         = 0;
    [ObservableProperty] private int    _generatorFeedback    = 0;

    partial void OnL1PhaseVoltageChanged(double v)     => FlushToRegisters();
    partial void OnL2PhaseVoltageChanged(double v)     => FlushToRegisters();
    partial void OnL3PhaseVoltageChanged(double v)     => FlushToRegisters();
    partial void OnL1CurrentChanged(double v)          => FlushToRegisters();
    partial void OnL2CurrentChanged(double v)          => FlushToRegisters();
    partial void OnL3CurrentChanged(double v)          => FlushToRegisters();
    partial void OnTolActivePowerChanged(double v)     => FlushToRegisters();
    partial void OnFrequencyChanged(double v)          => FlushToRegisters();
    partial void OnPosActiveChargeChanged(double v)    => FlushToRegisters();
    partial void OnGridDisconnectChanged(int v)        => FlushToRegisters();
    partial void OnGeneratorDisconnectChanged(int v)   => FlushToRegisters();
    partial void OnGridFeedbackChanged(int v)          => FlushToRegisters();
    partial void OnGeneratorFeedbackChanged(int v)     => FlushToRegisters();

    public IReadOnlyList<ComboItem> DisconnectItems { get; } = new[]
    { new ComboItem(0,"正常"), new ComboItem(1,"脱扣") };
    public IReadOnlyList<ComboItem> FeedbackItems   { get; } = new[]
    { new ComboItem(0,"闭合"), new ComboItem(1,"断开") };

    protected override void SyncToModel()
    {
        _model.L1PhaseVoltage      = L1PhaseVoltage;
        _model.L2PhaseVoltage      = L2PhaseVoltage;
        _model.L3PhaseVoltage      = L3PhaseVoltage;
        _model.L1Current           = L1Current;
        _model.L2Current           = L2Current;
        _model.L3Current           = L3Current;
        _model.TolActivePower      = TolActivePower;
        _model.Frequency           = Frequency;
        _model.PosActiveCharge     = PosActiveCharge;
        _model.GridDisconnect      = GridDisconnect;
        _model.GeneratorDisconnect = GeneratorDisconnect;
        _model.GridFeedback        = GridFeedback;
        _model.GeneratorFeedback   = GeneratorFeedback;
    }
}
