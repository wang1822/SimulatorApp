using CommunityToolkit.Mvvm.ComponentModel;
using SimulatorApp.Shared.Services;
using SimulatorApp.Slave.Models;
using SimulatorApp.Slave.Models.Bms;
using SimulatorApp.Slave.Services;
using System.Collections.ObjectModel;

namespace SimulatorApp.Slave.ViewModels;

/// <summary>BMS 电池管理系统 ViewModel</summary>
public partial class BmsViewModel : DeviceViewModelBase
{
    private readonly BmsModel _model = new();
    public override string DeviceName => "BMS 电池管理系统";
    protected override DeviceModelBase Model => _model;

    public BmsViewModel(RegisterBank bank, RegisterMapService mapService) : base(bank, mapService)
    {
        InitAlarmItems();
        FlushToRegisters();
    }

    [ObservableProperty] private double _insideAddedTotalVolt     = 500.0;
    [ObservableProperty] private double _combinedCurrent          = 0.0;
    [ObservableProperty] private double _soc                      = 80.0;
    [ObservableProperty] private double _soh                      = 100.0;
    [ObservableProperty] private double _allowableChargingCurrent  = 100.0;
    [ObservableProperty] private double _allowableDischargeCurrent = 100.0;
    [ObservableProperty] private double _maxCellVolt              = 3.400;
    [ObservableProperty] private double _minCellVolt              = 3.300;
    [ObservableProperty] private double _systemVoltDifferential   = 0.100;
    [ObservableProperty] private int    _maxCellTemp              = 30;
    [ObservableProperty] private int    _minCellTemp              = 20;
    [ObservableProperty] private int    _systemTempDifference     = 10;
    [ObservableProperty] private int    _systemInsulationValue    = 1000;
    [ObservableProperty] private int    _systemState              = 0;
    [ObservableProperty] private int    _chargeProhibitedSign     = 0;
    [ObservableProperty] private int    _dischargeProhibitedSign  = 0;

    partial void OnInsideAddedTotalVoltChanged(double v)      => FlushToRegisters();
    partial void OnCombinedCurrentChanged(double v)           => FlushToRegisters();
    partial void OnSocChanged(double v)                       => FlushToRegisters();
    partial void OnSohChanged(double v)                       => FlushToRegisters();
    partial void OnAllowableChargingCurrentChanged(double v)  => FlushToRegisters();
    partial void OnAllowableDischargeCurrentChanged(double v) => FlushToRegisters();
    partial void OnMaxCellVoltChanged(double v)               => FlushToRegisters();
    partial void OnMinCellVoltChanged(double v)               => FlushToRegisters();
    partial void OnSystemVoltDifferentialChanged(double v)    => FlushToRegisters();
    partial void OnMaxCellTempChanged(int v)                  => FlushToRegisters();
    partial void OnMinCellTempChanged(int v)                  => FlushToRegisters();
    partial void OnSystemTempDifferenceChanged(int v)         => FlushToRegisters();
    partial void OnSystemInsulationValueChanged(int v)        => FlushToRegisters();
    partial void OnSystemStateChanged(int v)                  => FlushToRegisters();
    partial void OnChargeProhibitedSignChanged(int v)         => FlushToRegisters();
    partial void OnDischargeProhibitedSignChanged(int v)      => FlushToRegisters();

    public IReadOnlyList<ComboItem> SystemStateItems { get; } = new[]
    {
        new ComboItem(0, "静置"), new ComboItem(1, "充电"), new ComboItem(2, "放电")
    };
    public IReadOnlyList<ComboItem> ProhibitItems { get; } = new[]
    {
        new ComboItem(0, "允许"), new ComboItem(1, "禁止")
    };

    public ObservableCollection<AlarmItem> Alarm1Items { get; } = new();
    public ObservableCollection<AlarmItem> Alarm2Items { get; } = new();
    public ObservableCollection<AlarmItem> Fault1Items { get; } = new();
    public ObservableCollection<AlarmItem> Fault2Items { get; } = new();
    public ObservableCollection<AlarmItem> Fault3Items { get; } = new();
    public ObservableCollection<AlarmItem> Fault4Items { get; } = new();

    private void InitAlarmItems()
    {
        void AddItems(ObservableCollection<AlarmItem> col, (int, string)[] defs)
        {
            foreach (var (bit, label) in defs)
            {
                var item = new AlarmItem(label, bit);
                item.CheckedChanged += () => FlushToRegisters();
                col.Add(item);
            }
        }

        AddItems(Alarm1Items, new[]
        {
            (0x80, "单体欠压"), (0x40, "总压欠压"), (0x20, "单体过压"), (0x10, "总压过压"),
            (0x08, "放电过流"), (0x04, "充电过流"), (0x02, "温度过高"), (0x01, "温度过低")
        });
        AddItems(Alarm2Items, new[]
        {
            (0x80, "充电低温"), (0x40, "放电低温"), (0x20, "压差过大"), (0x10, "温差过大"),
            (0x08, "绝缘低"),   (0x04, "SOC低"),    (0x02, "SOC高"),    (0x01, "内短路")
        });
        AddItems(Fault1Items, new[]
        {
            (0x80, "单体欠压保护"), (0x40, "总压欠压保护"), (0x20, "单体过压保护"), (0x10, "总压过压保护"),
            (0x08, "放电过流保护"), (0x04, "充电过流保护"), (0x02, "过温保护"),     (0x01, "低温保护")
        });
        AddItems(Fault2Items, new[]
        {
            (0x80, "充电低温保护"), (0x40, "放电低温保护"), (0x20, "压差过大保护"),
            (0x10, "温差过大保护"), (0x08, "绝缘故障"),     (0x04, "SOC过低保护"), (0x02, "内短路保护")
        });
        AddItems(Fault3Items, new[]
        {
            (0x80, "内总压采集故障"), (0x40, "Fuse总压故障"), (0x20, "PCS总压故障"),
            (0x10, "加热总压故障"),   (0x08, "电流采集故障")
        });
        AddItems(Fault4Items, new[]
        {
            (0x80, "主正继电器粘连"), (0x40, "充电继电器粘连"), (0x20, "加热继电器粘连"),
            (0x10, "主正继电器断路"), (0x08, "充电继电器断路")
        });
    }

    protected override void SyncToModel()
    {
        _model.InsideAddedTotalVolt      = InsideAddedTotalVolt;
        _model.CombinedCurrent           = CombinedCurrent;
        _model.Soc                       = Soc;
        _model.Soh                       = Soh;
        _model.AllowableChargingCurrent  = AllowableChargingCurrent;
        _model.AllowableDischargeCurrent = AllowableDischargeCurrent;
        _model.MaxCellVolt               = MaxCellVolt;
        _model.MinCellVolt               = MinCellVolt;
        _model.SystemVoltDifferential    = SystemVoltDifferential;
        _model.MaxCellTemp               = MaxCellTemp;
        _model.MinCellTemp               = MinCellTemp;
        _model.SystemTempDifference      = SystemTempDifference;
        _model.SystemInsulationValue     = SystemInsulationValue;
        _model.SystemState               = SystemState;
        _model.ChargeProhibitedSign      = ChargeProhibitedSign;
        _model.DischargeProhibitedSign   = DischargeProhibitedSign;
        _model.Alarm1 = CalcBitmask(Alarm1Items);
        _model.Alarm2 = CalcBitmask(Alarm2Items);
        _model.Fault1 = CalcBitmask(Fault1Items);
        _model.Fault2 = CalcBitmask(Fault2Items);
        _model.Fault3 = CalcBitmask(Fault3Items);
        _model.Fault4 = CalcBitmask(Fault4Items);
    }

    public override void GenerateData()
    {
        var rnd = new Random();
        Soc                      = Math.Round(20 + rnd.NextDouble() * 70, 1);
        Soh                      = Math.Round(80 + rnd.NextDouble() * 20, 1);
        InsideAddedTotalVolt     = Math.Round(450 + rnd.NextDouble() * 100, 1);
        MaxCellVolt              = Math.Round(3.3 + rnd.NextDouble() * 0.2, 3);
        MinCellVolt              = Math.Round(3.1 + rnd.NextDouble() * 0.2, 3);
        MaxCellTemp              = (int)(20 + rnd.NextDouble() * 20);
        MinCellTemp              = (int)(15 + rnd.NextDouble() * 10);
        SystemInsulationValue    = (int)(500 + rnd.NextDouble() * 500);
        base.GenerateData();
    }

    public override void ClearAlarms()
    {
        foreach (var col in new[] { Alarm1Items, Alarm2Items, Fault1Items, Fault2Items, Fault3Items, Fault4Items })
            foreach (var item in col) item.IsChecked = false;
        base.ClearAlarms();
    }
}
