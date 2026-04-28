using CommunityToolkit.Mvvm.ComponentModel;
using SimulatorApp.Shared.Services;
using SimulatorApp.Slave.Models;
using SimulatorApp.Slave.Models.Pcs;
using SimulatorApp.Slave.Services;
using System.Collections.ObjectModel;

namespace SimulatorApp.Slave.ViewModels;

/// <summary>
/// PCS 储能变流器 ViewModel。
/// 字段与设计文档第 8.1 节保持一致。
/// </summary>
public partial class PcsViewModel : DeviceViewModelBase
{
    private readonly PcsModel _model = new();

    public override string      DeviceName => "PCS 储能变流器";
    protected override DeviceModelBase Model => _model;

    public PcsViewModel(RegisterBank bank, RegisterMapService mapService)
        : base(bank, mapService)
    {
        // 初始化告警/故障 CheckBox 列表（bitmask，OR 合并）
        InitAlarmItems();
        // 首次刷新
        FlushToRegisters();
    }

    // ----------------------------------------------------------------
    // 遥测字段（[ObservableProperty] 生成属性，变更时自动调用 OnXxxChanged）
    // ----------------------------------------------------------------

    [ObservableProperty] private double _dcVoltage              = 750.0;
    [ObservableProperty] private double _dcCurrent              = 0.0;
    [ObservableProperty] private double _dcPower                = 0.0;
    [ObservableProperty] private double _pcsPhaseAVolt          = 220.0;
    [ObservableProperty] private double _pcsPhaseBVolt          = 220.0;
    [ObservableProperty] private double _pcsPhaseCVolt          = 220.0;
    [ObservableProperty] private double _gridPhaseAVolt         = 220.0;
    [ObservableProperty] private double _gridPhaseBVolt         = 220.0;
    [ObservableProperty] private double _gridPhaseCVolt         = 220.0;
    [ObservableProperty] private double _gridPhaseACurrent      = 0.0;
    [ObservableProperty] private double _gridPhaseBCurrent      = 0.0;
    [ObservableProperty] private double _gridPhaseCCurrent      = 0.0;
    [ObservableProperty] private double _gridFrequency          = 50.00;
    [ObservableProperty] private double _gridPowerFactor        = 1.000;
    [ObservableProperty] private double _pcsTotalActPower       = 0.0;
    [ObservableProperty] private double _pcsPhaseAActPower      = 0.0;
    [ObservableProperty] private double _pcsPhaseBActPower      = 0.0;
    [ObservableProperty] private double _pcsPhaseCActPower      = 0.0;
    [ObservableProperty] private double _pcsTotalReactPower     = 0.0;
    [ObservableProperty] private double _pcsTemp1               = 25.0;
    [ObservableProperty] private double _pcsTemp2               = 25.0;
    [ObservableProperty] private double _dailyChargedEnergy     = 0.0;
    [ObservableProperty] private double _dailyDischargedEnergy  = 0.0;

    // 状态（ComboBox）
    [ObservableProperty] private int _chargingState   = 0;
    [ObservableProperty] private int _operatingState  = 0;

    // 属性变更 → 刷新寄存器（每个字段都需要）
    partial void OnDcVoltageChanged(double v)              => FlushToRegisters();
    partial void OnDcCurrentChanged(double v)              => FlushToRegisters();
    partial void OnDcPowerChanged(double v)                => FlushToRegisters();
    partial void OnPcsPhaseAVoltChanged(double v)          => FlushToRegisters();
    partial void OnPcsPhaseBVoltChanged(double v)          => FlushToRegisters();
    partial void OnPcsPhaseCVoltChanged(double v)          => FlushToRegisters();
    partial void OnGridPhaseAVoltChanged(double v)         => FlushToRegisters();
    partial void OnGridPhaseBVoltChanged(double v)         => FlushToRegisters();
    partial void OnGridPhaseCVoltChanged(double v)         => FlushToRegisters();
    partial void OnGridPhaseACurrentChanged(double v)      => FlushToRegisters();
    partial void OnGridPhaseBCurrentChanged(double v)      => FlushToRegisters();
    partial void OnGridPhaseCCurrentChanged(double v)      => FlushToRegisters();
    partial void OnGridFrequencyChanged(double v)          => FlushToRegisters();
    partial void OnGridPowerFactorChanged(double v)        => FlushToRegisters();
    partial void OnPcsTotalActPowerChanged(double v)       => FlushToRegisters();
    partial void OnPcsPhaseAActPowerChanged(double v)      => FlushToRegisters();
    partial void OnPcsPhaseBActPowerChanged(double v)      => FlushToRegisters();
    partial void OnPcsPhaseCActPowerChanged(double v)      => FlushToRegisters();
    partial void OnPcsTotalReactPowerChanged(double v)     => FlushToRegisters();
    partial void OnPcsTemp1Changed(double v)               => FlushToRegisters();
    partial void OnPcsTemp2Changed(double v)               => FlushToRegisters();
    partial void OnDailyChargedEnergyChanged(double v)     => FlushToRegisters();
    partial void OnDailyDischargedEnergyChanged(double v)  => FlushToRegisters();
    partial void OnChargingStateChanged(int v)             => FlushToRegisters();
    partial void OnOperatingStateChanged(int v)            => FlushToRegisters();

    // ----------------------------------------------------------------
    // 状态枚举选项（ComboBox 数据源）
    // ----------------------------------------------------------------

    public IReadOnlyList<ComboItem> ChargingStateItems { get; } = new[]
    {
        new ComboItem(0, "静置"),
        new ComboItem(1, "充电"),
        new ComboItem(2, "放电")
    };

    public IReadOnlyList<ComboItem> OperatingStateItems { get; } = new[]
    {
        new ComboItem(0, "待机"),
        new ComboItem(1, "自检"),
        new ComboItem(2, "运行"),
        new ComboItem(3, "告警"),
        new ComboItem(4, "故障")
    };

    // ----------------------------------------------------------------
    // 故障/告警 CheckBox 列表
    // ----------------------------------------------------------------

    public ObservableCollection<AlarmItem> Alarm1Items { get; } = new();
    public ObservableCollection<AlarmItem> Alarm2Items { get; } = new();
    public ObservableCollection<AlarmItem> Fault1Items { get; } = new();
    public ObservableCollection<AlarmItem> Fault2Items { get; } = new();

    private void InitAlarmItems()
    {
        // 告警字1（偏移 +224）—— 各bit含义待协议附件确认，占位描述
        var alarm1Defs = new[]
        {
            (0x0001, "告警1-Bit0"), (0x0002, "告警1-Bit1"), (0x0004, "告警1-Bit2"),
            (0x0008, "告警1-Bit3"), (0x0010, "告警1-Bit4"), (0x0020, "告警1-Bit5"),
            (0x0040, "告警1-Bit6"), (0x0080, "告警1-Bit7"),
        };
        foreach (var (bit, label) in alarm1Defs)
        {
            var item = new AlarmItem(label, bit);
            item.CheckedChanged += OnAlarmChanged;
            Alarm1Items.Add(item);
        }

        // 告警字2
        var alarm2Defs = new[]
        {
            (0x0001, "告警2-Bit0"), (0x0002, "告警2-Bit1"), (0x0004, "告警2-Bit2"),
            (0x0008, "告警2-Bit3"), (0x0010, "告警2-Bit4"), (0x0020, "告警2-Bit5"),
        };
        foreach (var (bit, label) in alarm2Defs)
        {
            var item = new AlarmItem(label, bit);
            item.CheckedChanged += OnAlarmChanged;
            Alarm2Items.Add(item);
        }

        // 故障字1（偏移 +228）
        var fault1Defs = new[]
        {
            (0x0001, "故障1-Bit0"), (0x0002, "故障1-Bit1"), (0x0004, "故障1-Bit2"),
            (0x0008, "故障1-Bit3"), (0x0010, "故障1-Bit4"), (0x0020, "故障1-Bit5"),
            (0x0040, "故障1-Bit6"), (0x0080, "故障1-Bit7"),
        };
        foreach (var (bit, label) in fault1Defs)
        {
            var item = new AlarmItem(label, bit);
            item.CheckedChanged += OnAlarmChanged;
            Fault1Items.Add(item);
        }

        // 故障字2
        var fault2Defs = new[]
        {
            (0x0001, "故障2-Bit0"), (0x0002, "故障2-Bit1"), (0x0004, "故障2-Bit2"),
            (0x0008, "故障2-Bit3"),
        };
        foreach (var (bit, label) in fault2Defs)
        {
            var item = new AlarmItem(label, bit);
            item.CheckedChanged += OnAlarmChanged;
            Fault2Items.Add(item);
        }
    }

    private void OnAlarmChanged() => FlushToRegisters();

    // ----------------------------------------------------------------
    // SyncToModel：将 ViewModel 字段同步到 Model
    // ----------------------------------------------------------------

    protected override void SyncToModel()
    {
        _model.DcVoltage             = DcVoltage;
        _model.DcCurrent             = DcCurrent;
        _model.DcPower               = DcPower;
        _model.PcsPhaseAVolt         = PcsPhaseAVolt;
        _model.PcsPhaseBVolt         = PcsPhaseBVolt;
        _model.PcsPhaseCVolt         = PcsPhaseCVolt;
        _model.GridPhaseAVolt        = GridPhaseAVolt;
        _model.GridPhaseBVolt        = GridPhaseBVolt;
        _model.GridPhaseCVolt        = GridPhaseCVolt;
        _model.GridPhaseACurrent     = GridPhaseACurrent;
        _model.GridPhaseBCurrent     = GridPhaseBCurrent;
        _model.GridPhaseCCurrent     = GridPhaseCCurrent;
        _model.GridFrequency         = GridFrequency;
        _model.GridPowerFactor       = GridPowerFactor;
        _model.PcsTotalActPower      = PcsTotalActPower;
        _model.PcsPhaseAActPower     = PcsPhaseAActPower;
        _model.PcsPhaseBActPower     = PcsPhaseBActPower;
        _model.PcsPhaseCActPower     = PcsPhaseCActPower;
        _model.PcsTotalReactPower    = PcsTotalReactPower;
        _model.PcsTemp1              = PcsTemp1;
        _model.PcsTemp2              = PcsTemp2;
        _model.DailyChargedEnergy    = DailyChargedEnergy;
        _model.DailyDischargedEnergy = DailyDischargedEnergy;
        _model.ChargingState         = ChargingState;
        _model.OperatingState        = OperatingState;
        _model.Alarm1 = CalcBitmask(Alarm1Items);
        _model.Alarm2 = CalcBitmask(Alarm2Items);
        _model.Fault1 = CalcBitmask(Fault1Items);
        _model.Fault2 = CalcBitmask(Fault2Items);
    }

    // ----------------------------------------------------------------
    // 一键生成随机数据
    // ----------------------------------------------------------------

    public override void GenerateData()
    {
        var rnd = new Random();
        DcVoltage          = Math.Round(700 + rnd.NextDouble() * 100, 1);
        DcCurrent          = Math.Round(-200 + rnd.NextDouble() * 400, 1);
        PcsPhaseAVolt      = Math.Round(215 + rnd.NextDouble() * 10, 1);
        PcsPhaseBVolt      = Math.Round(215 + rnd.NextDouble() * 10, 1);
        PcsPhaseCVolt      = Math.Round(215 + rnd.NextDouble() * 10, 1);
        GridPhaseAVolt     = Math.Round(215 + rnd.NextDouble() * 10, 1);
        GridFrequency      = Math.Round(49.9 + rnd.NextDouble() * 0.2, 2);
        GridPowerFactor    = Math.Round(0.95 + rnd.NextDouble() * 0.05, 3);
        PcsTemp1           = Math.Round(20 + rnd.NextDouble() * 30, 1);
        PcsTemp2           = Math.Round(20 + rnd.NextDouble() * 30, 1);
        OperatingState     = 2; // 运行
        base.GenerateData();
    }

    // ----------------------------------------------------------------
    // 清除所有告警
    // ----------------------------------------------------------------

    public override void ClearAlarms()
    {
        foreach (var item in Alarm1Items) item.IsChecked = false;
        foreach (var item in Alarm2Items) item.IsChecked = false;
        foreach (var item in Fault1Items) item.IsChecked = false;
        foreach (var item in Fault2Items) item.IsChecked = false;
        base.ClearAlarms();
    }
}
