using CommunityToolkit.Mvvm.ComponentModel;
using SimulatorApp.Shared.Services;
using SimulatorApp.Slave.Models;
using SimulatorApp.Slave.Models.Pcs;
using SimulatorApp.Slave.Services;
using System.Collections.ObjectModel;

namespace SimulatorApp.Slave.ViewModels;

/// <summary>
/// PCS 储能变流器 ViewModel，字段依据 PCS_Modbus协议V102.xlsx 的 3000-3136 遥测区。
/// </summary>
public partial class PcsViewModel : DeviceViewModelBase
{
    private readonly PcsModel _model = new();

    public override string DeviceName => "GS215PCS";
    protected override DeviceModelBase Model => _model;

    public PcsViewModel(RegisterBank bank, RegisterMapService mapService)
        : base(bank, mapService)
    {
        InitAlarmItems();
        FlushToRegisters();
    }

    [ObservableProperty] private double _dcVoltage = 750.0;
    [ObservableProperty] private double _dcCurrent = 0.0;
    [ObservableProperty] private double _dcPower = 0.0;
    [ObservableProperty] private double _pcsPhaseAVolt = 220.0;
    [ObservableProperty] private double _pcsPhaseBVolt = 220.0;
    [ObservableProperty] private double _pcsPhaseCVolt = 220.0;
    [ObservableProperty] private double _gridPhaseAVolt = 220.0;
    [ObservableProperty] private double _gridPhaseBVolt = 220.0;
    [ObservableProperty] private double _gridPhaseCVolt = 220.0;
    [ObservableProperty] private double _gridPhaseACurrent = 0.0;
    [ObservableProperty] private double _gridPhaseBCurrent = 0.0;
    [ObservableProperty] private double _gridPhaseCCurrent = 0.0;
    [ObservableProperty] private double _gridFrequency = 50.00;
    [ObservableProperty] private double _gridPowerFactor = 1.000;
    [ObservableProperty] private double _pcsTotalActPower = 0.0;
    [ObservableProperty] private double _pcsPhaseAActPower = 0.0;
    [ObservableProperty] private double _pcsPhaseBActPower = 0.0;
    [ObservableProperty] private double _pcsPhaseCActPower = 0.0;
    [ObservableProperty] private double _pcsTotalReactPower = 0.0;
    [ObservableProperty] private double _pcsPhaseAReactPower = 0.0;
    [ObservableProperty] private double _pcsPhaseBReactPower = 0.0;
    [ObservableProperty] private double _pcsPhaseCReactPower = 0.0;
    [ObservableProperty] private double _gridSideTotalActPower = 0.0;
    [ObservableProperty] private double _gridSidePhaseAActPower = 0.0;
    [ObservableProperty] private double _gridSidePhaseBActPower = 0.0;
    [ObservableProperty] private double _gridSidePhaseCActPower = 0.0;
    [ObservableProperty] private double _dailyChargedEnergy = 0.0;
    [ObservableProperty] private double _dailyDischargedEnergy = 0.0;
    [ObservableProperty] private double _cumulativeChargedEnergy = 0.0;
    [ObservableProperty] private double _cumulativeDischargedEnergy = 0.0;
    [ObservableProperty] private double _dailyPurchasedEnergy = 0.0;
    [ObservableProperty] private double _dailySoldEnergy = 0.0;
    [ObservableProperty] private double _cumulativePurchasedEnergy = 0.0;
    [ObservableProperty] private double _cumulativeSoldEnergy = 0.0;
    [ObservableProperty] private double _pcsTemp1 = 25.0;
    [ObservableProperty] private double _pcsTemp2 = 25.0;
    [ObservableProperty] private double _dailyChargingTime = 0.0;
    [ObservableProperty] private double _dailyDischargingTime = 0.0;
    [ObservableProperty] private double _cumulativeChargingTime = 0.0;
    [ObservableProperty] private double _cumulativeDischargingTime = 0.0;
    [ObservableProperty] private double _exportLimitTotalActPower = 0.0;
    [ObservableProperty] private double _exportLimitPhaseAActPower = 0.0;
    [ObservableProperty] private double _exportLimitPhaseBActPower = 0.0;
    [ObservableProperty] private double _exportLimitPhaseCActPower = 0.0;
    [ObservableProperty] private double _exportLimitTotalReactPower = 0.0;
    [ObservableProperty] private double _exportLimitPhaseAReactPower = 0.0;
    [ObservableProperty] private double _exportLimitPhaseBReactPower = 0.0;
    [ObservableProperty] private double _exportLimitPhaseCReactPower = 0.0;
    [ObservableProperty] private double _loadTotalActPower = 0.0;
    [ObservableProperty] private double _loadPhaseAActPower = 0.0;
    [ObservableProperty] private double _loadPhaseBActPower = 0.0;
    [ObservableProperty] private double _loadPhaseCActPower = 0.0;
    [ObservableProperty] private double _loadTotalReactPower = 0.0;
    [ObservableProperty] private double _loadPhaseAReactPower = 0.0;
    [ObservableProperty] private double _loadPhaseBReactPower = 0.0;
    [ObservableProperty] private double _loadPhaseCReactPower = 0.0;
    [ObservableProperty] private double _meterSoldEnergy = 0.0;
    [ObservableProperty] private double _meterPurchasedEnergy = 0.0;
    [ObservableProperty] private int _chargingState = 2;
    [ObservableProperty] private int _operatingState = 0;
    [ObservableProperty] private int _operatingMode = 0;
    [ObservableProperty] private int _relayStatus = 0;
    [ObservableProperty] private int _selfCheckingCountdown = 0;
    [ObservableProperty] private int _exportLimitMode = 0;

    partial void OnDcVoltageChanged(double value) => FlushToRegisters();
    partial void OnDcCurrentChanged(double value) => FlushToRegisters();
    partial void OnDcPowerChanged(double value) => FlushToRegisters();
    partial void OnPcsPhaseAVoltChanged(double value) => FlushToRegisters();
    partial void OnPcsPhaseBVoltChanged(double value) => FlushToRegisters();
    partial void OnPcsPhaseCVoltChanged(double value) => FlushToRegisters();
    partial void OnGridPhaseAVoltChanged(double value) => FlushToRegisters();
    partial void OnGridPhaseBVoltChanged(double value) => FlushToRegisters();
    partial void OnGridPhaseCVoltChanged(double value) => FlushToRegisters();
    partial void OnGridPhaseACurrentChanged(double value) => FlushToRegisters();
    partial void OnGridPhaseBCurrentChanged(double value) => FlushToRegisters();
    partial void OnGridPhaseCCurrentChanged(double value) => FlushToRegisters();
    partial void OnGridFrequencyChanged(double value) => FlushToRegisters();
    partial void OnGridPowerFactorChanged(double value) => FlushToRegisters();
    partial void OnPcsTotalActPowerChanged(double value) => FlushToRegisters();
    partial void OnPcsPhaseAActPowerChanged(double value) => FlushToRegisters();
    partial void OnPcsPhaseBActPowerChanged(double value) => FlushToRegisters();
    partial void OnPcsPhaseCActPowerChanged(double value) => FlushToRegisters();
    partial void OnPcsTotalReactPowerChanged(double value) => FlushToRegisters();
    partial void OnPcsPhaseAReactPowerChanged(double value) => FlushToRegisters();
    partial void OnPcsPhaseBReactPowerChanged(double value) => FlushToRegisters();
    partial void OnPcsPhaseCReactPowerChanged(double value) => FlushToRegisters();
    partial void OnGridSideTotalActPowerChanged(double value) => FlushToRegisters();
    partial void OnGridSidePhaseAActPowerChanged(double value) => FlushToRegisters();
    partial void OnGridSidePhaseBActPowerChanged(double value) => FlushToRegisters();
    partial void OnGridSidePhaseCActPowerChanged(double value) => FlushToRegisters();
    partial void OnDailyChargedEnergyChanged(double value) => FlushToRegisters();
    partial void OnDailyDischargedEnergyChanged(double value) => FlushToRegisters();
    partial void OnCumulativeChargedEnergyChanged(double value) => FlushToRegisters();
    partial void OnCumulativeDischargedEnergyChanged(double value) => FlushToRegisters();
    partial void OnDailyPurchasedEnergyChanged(double value) => FlushToRegisters();
    partial void OnDailySoldEnergyChanged(double value) => FlushToRegisters();
    partial void OnCumulativePurchasedEnergyChanged(double value) => FlushToRegisters();
    partial void OnCumulativeSoldEnergyChanged(double value) => FlushToRegisters();
    partial void OnPcsTemp1Changed(double value) => FlushToRegisters();
    partial void OnPcsTemp2Changed(double value) => FlushToRegisters();
    partial void OnDailyChargingTimeChanged(double value) => FlushToRegisters();
    partial void OnDailyDischargingTimeChanged(double value) => FlushToRegisters();
    partial void OnCumulativeChargingTimeChanged(double value) => FlushToRegisters();
    partial void OnCumulativeDischargingTimeChanged(double value) => FlushToRegisters();
    partial void OnExportLimitTotalActPowerChanged(double value) => FlushToRegisters();
    partial void OnExportLimitPhaseAActPowerChanged(double value) => FlushToRegisters();
    partial void OnExportLimitPhaseBActPowerChanged(double value) => FlushToRegisters();
    partial void OnExportLimitPhaseCActPowerChanged(double value) => FlushToRegisters();
    partial void OnExportLimitTotalReactPowerChanged(double value) => FlushToRegisters();
    partial void OnExportLimitPhaseAReactPowerChanged(double value) => FlushToRegisters();
    partial void OnExportLimitPhaseBReactPowerChanged(double value) => FlushToRegisters();
    partial void OnExportLimitPhaseCReactPowerChanged(double value) => FlushToRegisters();
    partial void OnLoadTotalActPowerChanged(double value) => FlushToRegisters();
    partial void OnLoadPhaseAActPowerChanged(double value) => FlushToRegisters();
    partial void OnLoadPhaseBActPowerChanged(double value) => FlushToRegisters();
    partial void OnLoadPhaseCActPowerChanged(double value) => FlushToRegisters();
    partial void OnLoadTotalReactPowerChanged(double value) => FlushToRegisters();
    partial void OnLoadPhaseAReactPowerChanged(double value) => FlushToRegisters();
    partial void OnLoadPhaseBReactPowerChanged(double value) => FlushToRegisters();
    partial void OnLoadPhaseCReactPowerChanged(double value) => FlushToRegisters();
    partial void OnMeterSoldEnergyChanged(double value) => FlushToRegisters();
    partial void OnMeterPurchasedEnergyChanged(double value) => FlushToRegisters();
    partial void OnChargingStateChanged(int value) => FlushToRegisters();
    partial void OnOperatingStateChanged(int value) => FlushToRegisters();
    partial void OnOperatingModeChanged(int value) => FlushToRegisters();
    partial void OnRelayStatusChanged(int value) => FlushToRegisters();
    partial void OnSelfCheckingCountdownChanged(int value) => FlushToRegisters();
    partial void OnExportLimitModeChanged(int value) => FlushToRegisters();

    public IReadOnlyList<ComboItem> ChargingStateItems { get; } = new[]
    {
        new ComboItem(0, "充电"),
        new ComboItem(1, "放电"),
        new ComboItem(2, "待机")
    };

    public IReadOnlyList<ComboItem> OperatingStateItems { get; } = new[]
    {
        new ComboItem(0, "待机"),
        new ComboItem(1, "自检"),
        new ComboItem(2, "运行"),
        new ComboItem(3, "告警"),
        new ComboItem(4, "故障")
    };

    public IReadOnlyList<ComboItem> OperatingModeItems { get; } = new[]
    {
        new ComboItem(0, "并网"),
        new ComboItem(1, "离网")
    };

    public ObservableCollection<AlarmItem> Alarm1Items { get; } = new();
    public ObservableCollection<AlarmItem> Alarm2Items { get; } = new();
    public ObservableCollection<AlarmItem> Alarm3Items { get; } = new();
    public ObservableCollection<AlarmItem> Alarm4Items { get; } = new();
    public ObservableCollection<AlarmItem> Fault1Items { get; } = new();
    public ObservableCollection<AlarmItem> Fault2Items { get; } = new();
    public ObservableCollection<AlarmItem> Fault3Items { get; } = new();
    public ObservableCollection<AlarmItem> Fault4Items { get; } = new();

    private void InitAlarmItems()
    {
        AddBitItems(Alarm1Items, "告警1");
        AddBitItems(Alarm2Items, "告警2");
        AddBitItems(Alarm3Items, "告警3");
        AddBitItems(Alarm4Items, "告警4");
        AddFaultItems(Fault1Items, 1);
        AddFaultItems(Fault2Items, 17);
        AddFaultItems(Fault3Items, 33);
        AddFaultItems(Fault4Items, 49);
    }

    private void AddBitItems(ObservableCollection<AlarmItem> target, string prefix)
    {
        for (int bit = 0; bit < 16; bit++)
        {
            var item = new AlarmItem($"{prefix}-Bit{bit}", 1 << bit);
            item.CheckedChanged += OnAlarmChanged;
            target.Add(item);
        }
    }

    private void AddFaultItems(ObservableCollection<AlarmItem> target, int startCode)
    {
        for (int bit = 0; bit < 16; bit++)
        {
            int code = startCode + bit;
            var item = new AlarmItem($"F{code:00}", 1 << bit);
            item.CheckedChanged += OnAlarmChanged;
            target.Add(item);
        }
    }

    private void OnAlarmChanged() => FlushToRegisters();

    protected override void SyncToModel()
    {
        _model.DcVoltage = DcVoltage;
        _model.DcCurrent = DcCurrent;
        _model.DcPower = DcPower;
        _model.PcsPhaseAVolt = PcsPhaseAVolt;
        _model.PcsPhaseBVolt = PcsPhaseBVolt;
        _model.PcsPhaseCVolt = PcsPhaseCVolt;
        _model.GridPhaseAVolt = GridPhaseAVolt;
        _model.GridPhaseBVolt = GridPhaseBVolt;
        _model.GridPhaseCVolt = GridPhaseCVolt;
        _model.GridPhaseACurrent = GridPhaseACurrent;
        _model.GridPhaseBCurrent = GridPhaseBCurrent;
        _model.GridPhaseCCurrent = GridPhaseCCurrent;
        _model.GridFrequency = GridFrequency;
        _model.GridPowerFactor = GridPowerFactor;
        _model.PcsTotalActPower = PcsTotalActPower;
        _model.PcsPhaseAActPower = PcsPhaseAActPower;
        _model.PcsPhaseBActPower = PcsPhaseBActPower;
        _model.PcsPhaseCActPower = PcsPhaseCActPower;
        _model.PcsTotalReactPower = PcsTotalReactPower;
        _model.PcsPhaseAReactPower = PcsPhaseAReactPower;
        _model.PcsPhaseBReactPower = PcsPhaseBReactPower;
        _model.PcsPhaseCReactPower = PcsPhaseCReactPower;
        _model.GridSideTotalActPower = GridSideTotalActPower;
        _model.GridSidePhaseAActPower = GridSidePhaseAActPower;
        _model.GridSidePhaseBActPower = GridSidePhaseBActPower;
        _model.GridSidePhaseCActPower = GridSidePhaseCActPower;
        _model.DailyChargedEnergy = DailyChargedEnergy;
        _model.DailyDischargedEnergy = DailyDischargedEnergy;
        _model.CumulativeChargedEnergy = CumulativeChargedEnergy;
        _model.CumulativeDischargedEnergy = CumulativeDischargedEnergy;
        _model.DailyPurchasedEnergy = DailyPurchasedEnergy;
        _model.DailySoldEnergy = DailySoldEnergy;
        _model.CumulativePurchasedEnergy = CumulativePurchasedEnergy;
        _model.CumulativeSoldEnergy = CumulativeSoldEnergy;
        _model.PcsTemp1 = PcsTemp1;
        _model.PcsTemp2 = PcsTemp2;
        _model.DailyChargingTime = DailyChargingTime;
        _model.DailyDischargingTime = DailyDischargingTime;
        _model.CumulativeChargingTime = CumulativeChargingTime;
        _model.CumulativeDischargingTime = CumulativeDischargingTime;
        _model.ExportLimitTotalActPower = ExportLimitTotalActPower;
        _model.ExportLimitPhaseAActPower = ExportLimitPhaseAActPower;
        _model.ExportLimitPhaseBActPower = ExportLimitPhaseBActPower;
        _model.ExportLimitPhaseCActPower = ExportLimitPhaseCActPower;
        _model.ExportLimitTotalReactPower = ExportLimitTotalReactPower;
        _model.ExportLimitPhaseAReactPower = ExportLimitPhaseAReactPower;
        _model.ExportLimitPhaseBReactPower = ExportLimitPhaseBReactPower;
        _model.ExportLimitPhaseCReactPower = ExportLimitPhaseCReactPower;
        _model.LoadTotalActPower = LoadTotalActPower;
        _model.LoadPhaseAActPower = LoadPhaseAActPower;
        _model.LoadPhaseBActPower = LoadPhaseBActPower;
        _model.LoadPhaseCActPower = LoadPhaseCActPower;
        _model.LoadTotalReactPower = LoadTotalReactPower;
        _model.LoadPhaseAReactPower = LoadPhaseAReactPower;
        _model.LoadPhaseBReactPower = LoadPhaseBReactPower;
        _model.LoadPhaseCReactPower = LoadPhaseCReactPower;
        _model.MeterSoldEnergy = MeterSoldEnergy;
        _model.MeterPurchasedEnergy = MeterPurchasedEnergy;
        _model.ChargingState = ChargingState;
        _model.OperatingState = OperatingState;
        _model.OperatingMode = OperatingMode;
        _model.RelayStatus = (ushort)RelayStatus;
        _model.SelfCheckingCountdown = SelfCheckingCountdown;
        _model.ExportLimitMode = (ushort)ExportLimitMode;
        _model.Alarm1 = CalcBitmask(Alarm1Items);
        _model.Alarm2 = CalcBitmask(Alarm2Items);
        _model.Alarm3 = CalcBitmask(Alarm3Items);
        _model.Alarm4 = CalcBitmask(Alarm4Items);
        _model.Fault1 = CalcBitmask(Fault1Items);
        _model.Fault2 = CalcBitmask(Fault2Items);
        _model.Fault3 = CalcBitmask(Fault3Items);
        _model.Fault4 = CalcBitmask(Fault4Items);
    }

    public override void GenerateData()
    {
        var rnd = new Random();
        DcVoltage = Math.Round(700 + rnd.NextDouble() * 100, 1);
        DcCurrent = Math.Round(-200 + rnd.NextDouble() * 400, 1);
        DcPower = Math.Round(-80 + rnd.NextDouble() * 160, 1);
        PcsPhaseAVolt = Math.Round(215 + rnd.NextDouble() * 10, 1);
        PcsPhaseBVolt = Math.Round(215 + rnd.NextDouble() * 10, 1);
        PcsPhaseCVolt = Math.Round(215 + rnd.NextDouble() * 10, 1);
        GridPhaseAVolt = Math.Round(215 + rnd.NextDouble() * 10, 1);
        GridPhaseBVolt = Math.Round(215 + rnd.NextDouble() * 10, 1);
        GridPhaseCVolt = Math.Round(215 + rnd.NextDouble() * 10, 1);
        GridPhaseACurrent = Math.Round(rnd.NextDouble() * 120, 1);
        GridPhaseBCurrent = Math.Round(rnd.NextDouble() * 120, 1);
        GridPhaseCCurrent = Math.Round(rnd.NextDouble() * 120, 1);
        GridFrequency = Math.Round(49.9 + rnd.NextDouble() * 0.2, 2);
        GridPowerFactor = Math.Round(0.95 + rnd.NextDouble() * 0.05, 3);
        PcsTotalActPower = Math.Round(-80 + rnd.NextDouble() * 160, 2);
        PcsPhaseAActPower = Math.Round(PcsTotalActPower / 3, 2);
        PcsPhaseBActPower = Math.Round(PcsTotalActPower / 3, 2);
        PcsPhaseCActPower = Math.Round(PcsTotalActPower / 3, 2);
        PcsTotalReactPower = Math.Round(-20 + rnd.NextDouble() * 40, 2);
        GridSideTotalActPower = PcsTotalActPower;
        GridSidePhaseAActPower = PcsPhaseAActPower;
        GridSidePhaseBActPower = PcsPhaseBActPower;
        GridSidePhaseCActPower = PcsPhaseCActPower;
        DailyChargedEnergy = Math.Round(rnd.NextDouble() * 200, 1);
        DailyDischargedEnergy = Math.Round(rnd.NextDouble() * 200, 1);
        PcsTemp1 = Math.Round(20 + rnd.NextDouble() * 30, 1);
        PcsTemp2 = Math.Round(20 + rnd.NextDouble() * 30, 1);
        ChargingState = rnd.Next(0, 3);
        OperatingState = 2;
        OperatingMode = rnd.Next(0, 2);
        base.GenerateData();
    }

    public override void ClearAlarms()
    {
        foreach (var item in Alarm1Items) item.IsChecked = false;
        foreach (var item in Alarm2Items) item.IsChecked = false;
        foreach (var item in Alarm3Items) item.IsChecked = false;
        foreach (var item in Alarm4Items) item.IsChecked = false;
        foreach (var item in Fault1Items) item.IsChecked = false;
        foreach (var item in Fault2Items) item.IsChecked = false;
        foreach (var item in Fault3Items) item.IsChecked = false;
        foreach (var item in Fault4Items) item.IsChecked = false;
        base.ClearAlarms();
    }
}
