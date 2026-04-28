using CommunityToolkit.Mvvm.ComponentModel;
using SimulatorApp.Shared.Services;
using SimulatorApp.Slave.Models;
using SimulatorApp.Slave.Models.DIDOController;
using SimulatorApp.Slave.Services;

namespace SimulatorApp.Slave.ViewModels;

/// <summary>DI/DO 动环控制器 ViewModel</summary>
public partial class DIDOControllerViewModel : DeviceViewModelBase
{
    private readonly DIDOControllerModel _model = new();
    public override string DeviceName => "DI/DO 动环控制器";
    protected override DeviceModelBase Model => _model;

    public DIDOControllerViewModel(RegisterBank bank, RegisterMapService mapService)
        : base(bank, mapService) { FlushToRegisters(); }

    // DI 状态
    [ObservableProperty] private int _emergencyStop      = 0;
    [ObservableProperty] private int _qF1GridFeedback    = 0;
    [ObservableProperty] private int _qF2BatFeedback     = 0;
    [ObservableProperty] private int _waterSensor        = 0;
    [ObservableProperty] private int _aerosolSensor      = 0;
    [ObservableProperty] private int _bmsAccessControl   = 0;
    [ObservableProperty] private int _gasLowAlarm        = 0;
    [ObservableProperty] private int _gasHighAlarm       = 0;
    [ObservableProperty] private int _elecAccessControl  = 0;
    [ObservableProperty] private int _ltgProtsignal      = 0;
    [ObservableProperty] private int _tempSensor         = 0;
    [ObservableProperty] private int _smokeSensor        = 0;
    // DO 状态
    [ObservableProperty] private int _qF1GridTripEnable    = 0;
    [ObservableProperty] private int _qF2BatTripEnable     = 0;
    [ObservableProperty] private int _remoteEmergencyStop  = 0;
    [ObservableProperty] private int _fanStartStop         = 0;
    [ObservableProperty] private int _acoustoOptic         = 0;
    [ObservableProperty] private int _controlMode          = 0;

    partial void OnEmergencyStopChanged(int v)       => FlushToRegisters();
    partial void OnQF1GridFeedbackChanged(int v)     => FlushToRegisters();
    partial void OnQF2BatFeedbackChanged(int v)      => FlushToRegisters();
    partial void OnWaterSensorChanged(int v)         => FlushToRegisters();
    partial void OnAerosolSensorChanged(int v)       => FlushToRegisters();
    partial void OnBmsAccessControlChanged(int v)    => FlushToRegisters();
    partial void OnGasLowAlarmChanged(int v)         => FlushToRegisters();
    partial void OnGasHighAlarmChanged(int v)        => FlushToRegisters();
    partial void OnElecAccessControlChanged(int v)   => FlushToRegisters();
    partial void OnLtgProtsignalChanged(int v)       => FlushToRegisters();
    partial void OnTempSensorChanged(int v)          => FlushToRegisters();
    partial void OnSmokeSensorChanged(int v)         => FlushToRegisters();
    partial void OnQF1GridTripEnableChanged(int v)   => FlushToRegisters();
    partial void OnQF2BatTripEnableChanged(int v)    => FlushToRegisters();
    partial void OnRemoteEmergencyStopChanged(int v) => FlushToRegisters();
    partial void OnFanStartStopChanged(int v)        => FlushToRegisters();
    partial void OnAcoustoOpticChanged(int v)        => FlushToRegisters();
    partial void OnControlModeChanged(int v)         => FlushToRegisters();

    public IReadOnlyList<ComboItem> EmergencyItems { get; } = new[]
    { new ComboItem(0,"未按下"), new ComboItem(1,"按下") };
    public IReadOnlyList<ComboItem> BreakItems { get; } = new[]
    { new ComboItem(0,"闭合"), new ComboItem(1,"断开") };
    public IReadOnlyList<ComboItem> NormalAlarmItems { get; } = new[]
    { new ComboItem(0,"正常"), new ComboItem(1,"告警") };
    public IReadOnlyList<ComboItem> DoorItems { get; } = new[]
    { new ComboItem(0,"关门"), new ComboItem(1,"开门") };
    public IReadOnlyList<ComboItem> TripItems { get; } = new[]
    { new ComboItem(0,"未脱扣"), new ComboItem(1,"脱扣") };
    public IReadOnlyList<ComboItem> RunItems { get; } = new[]
    { new ComboItem(0,"停止"), new ComboItem(1,"运行") };
    public IReadOnlyList<ComboItem> ModeItems { get; } = new[]
    { new ComboItem(0,"自动"), new ComboItem(1,"手动") };

    protected override void SyncToModel()
    {
        _model.EmergencyStop      = EmergencyStop;
        _model.QF1GridFeedback    = QF1GridFeedback;
        _model.QF2BatFeedback     = QF2BatFeedback;
        _model.WaterSensor        = WaterSensor;
        _model.AerosolSensor      = AerosolSensor;
        _model.BmsAccessControl   = BmsAccessControl;
        _model.GasLowAlarm        = GasLowAlarm;
        _model.GasHighAlarm       = GasHighAlarm;
        _model.ElecAccessControl  = ElecAccessControl;
        _model.LtgProtsignal      = LtgProtsignal;
        _model.TempSensor         = TempSensor;
        _model.SmokeSensor        = SmokeSensor;
        _model.QF1GridTripEnable  = QF1GridTripEnable;
        _model.QF2BatTripEnable   = QF2BatTripEnable;
        _model.RemoteEmergencyStop= RemoteEmergencyStop;
        _model.FanStartStop       = FanStartStop;
        _model.AcoustoOptic       = AcoustoOptic;
        _model.ControlMode        = ControlMode;
    }
}
