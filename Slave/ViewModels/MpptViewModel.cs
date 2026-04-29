using CommunityToolkit.Mvvm.ComponentModel;
using SimulatorApp.Shared.Services;
using SimulatorApp.Slave.Models;
using SimulatorApp.Slave.Models.Mppt;
using SimulatorApp.Slave.Services;
using System.Collections.ObjectModel;

namespace SimulatorApp.Slave.ViewModels;

/// <summary>
/// MPPT 光伏 ViewModel，字段依据 MPPT_Modbus_V1.0.xlsx 的 4000-4209 寄存器表。
/// </summary>
public partial class MpptViewModel : DeviceViewModelBase
{
    private readonly MpptModel _model = new();

    public override string DeviceName => "GS215MPPT";
    protected override DeviceModelBase Model => _model;

    public MpptViewModel(RegisterBank bank, RegisterMapService mapService) : base(bank, mapService)
    {
        InitAlarmItems();
        FlushToRegisters();
    }

    [ObservableProperty] private int _mpptCount = 8;
    [ObservableProperty] private double _maxPvPowerPercent = 110.0;
    [ObservableProperty] private double _maxMpptCurrent = 40.0;
    [ObservableProperty] private int _powerEnable = 1;
    [ObservableProperty] private int _factoryResetEnable = 0;
    [ObservableProperty] private int _selfCheckTime = 0;
    [ObservableProperty] private int _risoEnable = 1;
    [ObservableProperty] private int _solarArcFaultMode = 0;
    [ObservableProperty] private double _pvEnergyCorrectionPercent = 100.0;
    [ObservableProperty] private int _testMode = 0;
    [ObservableProperty] private ushort _mpptFunction = 0;
    [ObservableProperty] private double _outputVolt = 600.0;
    [ObservableProperty] private double _outputCurrent = 0.0;
    [ObservableProperty] private double _outputPower = 0.0;
    [ObservableProperty] private double _pVTotalPower = 0.0;
    [ObservableProperty] private double _dCVolt1 = 400.0;
    [ObservableProperty] private double _dCCurrent1 = 0.0;
    [ObservableProperty] private double _dCPower1 = 0.0;
    [ObservableProperty] private double _dCVolt2 = 400.0;
    [ObservableProperty] private double _dCCurrent2 = 0.0;
    [ObservableProperty] private double _dCPower2 = 0.0;
    [ObservableProperty] private double _dCVolt3 = 400.0;
    [ObservableProperty] private double _dCCurrent3 = 0.0;
    [ObservableProperty] private double _dCPower3 = 0.0;
    [ObservableProperty] private double _dCVolt4 = 400.0;
    [ObservableProperty] private double _dCCurrent4 = 0.0;
    [ObservableProperty] private double _dCPower4 = 0.0;
    [ObservableProperty] private double _dCVolt5 = 400.0;
    [ObservableProperty] private double _dCCurrent5 = 0.0;
    [ObservableProperty] private double _dCPower5 = 0.0;
    [ObservableProperty] private double _dCVolt6 = 400.0;
    [ObservableProperty] private double _dCCurrent6 = 0.0;
    [ObservableProperty] private double _dCPower6 = 0.0;
    [ObservableProperty] private double _dCVolt7 = 400.0;
    [ObservableProperty] private double _dCCurrent7 = 0.0;
    [ObservableProperty] private double _dCPower7 = 0.0;
    [ObservableProperty] private double _dCVolt8 = 400.0;
    [ObservableProperty] private double _dCCurrent8 = 0.0;
    [ObservableProperty] private double _dCPower8 = 0.0;
    [ObservableProperty] private double _relayAfterVoltage = 600.0;
    [ObservableProperty] private double _dailyPVGen1 = 0.0;
    [ObservableProperty] private double _dailyPVGen2 = 0.0;
    [ObservableProperty] private double _dailyPVGen3 = 0.0;
    [ObservableProperty] private double _dailyPVGen4 = 0.0;
    [ObservableProperty] private double _dailyPVGen5 = 0.0;
    [ObservableProperty] private double _dailyPVGen6 = 0.0;
    [ObservableProperty] private double _dailyPVGen7 = 0.0;
    [ObservableProperty] private double _dailyPVGen8 = 0.0;
    [ObservableProperty] private double _dailyPVGenTotal = 0.0;
    [ObservableProperty] private double _historyPvGenCapacity = 0.0;
    [ObservableProperty] private double _heatSinkTemp = 25.0;
    [ObservableProperty] private int _testState = 0;
    [ObservableProperty] private ushort _internalFanAd = 0;
    [ObservableProperty] private ushort _externalFanAd = 0;
    [ObservableProperty] private ushort _testData1 = 0;
    [ObservableProperty] private ushort _testData2 = 0;
    [ObservableProperty] private ushort _testData3 = 0;
    [ObservableProperty] private ushort _testData4 = 0;
    [ObservableProperty] private ushort _testData5 = 0;
    [ObservableProperty] private ushort _testData6 = 0;
    [ObservableProperty] private ushort _testData7 = 0;
    [ObservableProperty] private ushort _testData8 = 0;
    [ObservableProperty] private ushort _testData9 = 0;
    [ObservableProperty] private int _mpptOperatingMode = 2;

    partial void OnMpptCountChanged(int value) => FlushToRegisters();
    partial void OnMaxPvPowerPercentChanged(double value) => FlushToRegisters();
    partial void OnMaxMpptCurrentChanged(double value) => FlushToRegisters();
    partial void OnPowerEnableChanged(int value) => FlushToRegisters();
    partial void OnFactoryResetEnableChanged(int value) => FlushToRegisters();
    partial void OnSelfCheckTimeChanged(int value) => FlushToRegisters();
    partial void OnRisoEnableChanged(int value) => FlushToRegisters();
    partial void OnSolarArcFaultModeChanged(int value) => FlushToRegisters();
    partial void OnPvEnergyCorrectionPercentChanged(double value) => FlushToRegisters();
    partial void OnTestModeChanged(int value) => FlushToRegisters();
    partial void OnMpptFunctionChanged(ushort value) => FlushToRegisters();
    partial void OnOutputVoltChanged(double value) => FlushToRegisters();
    partial void OnOutputCurrentChanged(double value) => FlushToRegisters();
    partial void OnOutputPowerChanged(double value) => FlushToRegisters();
    partial void OnPVTotalPowerChanged(double value) => FlushToRegisters();
    partial void OnDCVolt1Changed(double value) => FlushToRegisters();
    partial void OnDCCurrent1Changed(double value) => FlushToRegisters();
    partial void OnDCPower1Changed(double value) => FlushToRegisters();
    partial void OnDCVolt2Changed(double value) => FlushToRegisters();
    partial void OnDCCurrent2Changed(double value) => FlushToRegisters();
    partial void OnDCPower2Changed(double value) => FlushToRegisters();
    partial void OnDCVolt3Changed(double value) => FlushToRegisters();
    partial void OnDCCurrent3Changed(double value) => FlushToRegisters();
    partial void OnDCPower3Changed(double value) => FlushToRegisters();
    partial void OnDCVolt4Changed(double value) => FlushToRegisters();
    partial void OnDCCurrent4Changed(double value) => FlushToRegisters();
    partial void OnDCPower4Changed(double value) => FlushToRegisters();
    partial void OnDCVolt5Changed(double value) => FlushToRegisters();
    partial void OnDCCurrent5Changed(double value) => FlushToRegisters();
    partial void OnDCPower5Changed(double value) => FlushToRegisters();
    partial void OnDCVolt6Changed(double value) => FlushToRegisters();
    partial void OnDCCurrent6Changed(double value) => FlushToRegisters();
    partial void OnDCPower6Changed(double value) => FlushToRegisters();
    partial void OnDCVolt7Changed(double value) => FlushToRegisters();
    partial void OnDCCurrent7Changed(double value) => FlushToRegisters();
    partial void OnDCPower7Changed(double value) => FlushToRegisters();
    partial void OnDCVolt8Changed(double value) => FlushToRegisters();
    partial void OnDCCurrent8Changed(double value) => FlushToRegisters();
    partial void OnDCPower8Changed(double value) => FlushToRegisters();
    partial void OnRelayAfterVoltageChanged(double value) => FlushToRegisters();
    partial void OnDailyPVGen1Changed(double value) => FlushToRegisters();
    partial void OnDailyPVGen2Changed(double value) => FlushToRegisters();
    partial void OnDailyPVGen3Changed(double value) => FlushToRegisters();
    partial void OnDailyPVGen4Changed(double value) => FlushToRegisters();
    partial void OnDailyPVGen5Changed(double value) => FlushToRegisters();
    partial void OnDailyPVGen6Changed(double value) => FlushToRegisters();
    partial void OnDailyPVGen7Changed(double value) => FlushToRegisters();
    partial void OnDailyPVGen8Changed(double value) => FlushToRegisters();
    partial void OnDailyPVGenTotalChanged(double value) => FlushToRegisters();
    partial void OnHistoryPvGenCapacityChanged(double value) => FlushToRegisters();
    partial void OnHeatSinkTempChanged(double value) => FlushToRegisters();
    partial void OnTestStateChanged(int value) => FlushToRegisters();
    partial void OnInternalFanAdChanged(ushort value) => FlushToRegisters();
    partial void OnExternalFanAdChanged(ushort value) => FlushToRegisters();
    partial void OnTestData1Changed(ushort value) => FlushToRegisters();
    partial void OnTestData2Changed(ushort value) => FlushToRegisters();
    partial void OnTestData3Changed(ushort value) => FlushToRegisters();
    partial void OnTestData4Changed(ushort value) => FlushToRegisters();
    partial void OnTestData5Changed(ushort value) => FlushToRegisters();
    partial void OnTestData6Changed(ushort value) => FlushToRegisters();
    partial void OnTestData7Changed(ushort value) => FlushToRegisters();
    partial void OnTestData8Changed(ushort value) => FlushToRegisters();
    partial void OnTestData9Changed(ushort value) => FlushToRegisters();
    partial void OnMpptOperatingModeChanged(int value) => FlushToRegisters();

    public IReadOnlyList<ComboItem> MpptModeItems { get; } = new[]
    {
        new ComboItem(0, "待机"),
        new ComboItem(1, "自检"),
        new ComboItem(2, "正常"),
        new ComboItem(3, "告警"),
        new ComboItem(4, "故障")
    };

    public IReadOnlyList<ComboItem> PowerEnableItems { get; } = new[]
    {
        new ComboItem(0, "关机"),
        new ComboItem(1, "开机"),
        new ComboItem(3, "重启")
    };

    public IReadOnlyList<ComboItem> EnableItems { get; } = new[]
    {
        new ComboItem(0, "禁用"),
        new ComboItem(1, "启用")
    };

    public IReadOnlyList<ComboItem> FactoryResetItems { get; } = new[]
    {
        new ComboItem(0, "禁用"),
        new ComboItem(1, "启用"),
        new ComboItem(3, "锁定")
    };

    public IReadOnlyList<ComboItem> SolarArcFaultItems { get; } = new[]
    {
        new ComboItem(0, "关闭"),
        new ComboItem(2, "清零")
    };

    public ObservableCollection<AlarmItem> Alarm1Items { get; } = new();
    public ObservableCollection<AlarmItem> Alarm2Items { get; } = new();
    public ObservableCollection<AlarmItem> Fault1Items { get; } = new();
    public ObservableCollection<AlarmItem> Fault2Items { get; } = new();
    public ObservableCollection<AlarmItem> Fault3Items { get; } = new();
    public ObservableCollection<AlarmItem> Fault4Items { get; } = new();

    // 保留旧绑定别名，避免外部模板仍绑定 AlarmItems/FaultItems 时失效。
    public ObservableCollection<AlarmItem> AlarmItems => Alarm1Items;
    public ObservableCollection<AlarmItem> FaultItems => Fault1Items;

    private void InitAlarmItems()
    {
        AddBitItems(Alarm1Items, "告警1");
        AddBitItems(Alarm2Items, "告警2");
        AddBitItems(Fault1Items, "故障1");
        AddBitItems(Fault2Items, "故障2");
        AddBitItems(Fault3Items, "故障3");
        AddBitItems(Fault4Items, "故障4");
    }

    private void AddBitItems(ObservableCollection<AlarmItem> target, string prefix)
    {
        for (int bit = 0; bit < 16; bit++)
        {
            var item = new AlarmItem($"{prefix}-Bit{bit}", 1 << bit);
            item.CheckedChanged += () => FlushToRegisters();
            target.Add(item);
        }
    }

    protected override void SyncToModel()
    {
        _model.MpptCount = MpptCount;
        _model.MaxPvPowerPercent = MaxPvPowerPercent;
        _model.MaxMpptCurrent = MaxMpptCurrent;
        _model.PowerEnable = PowerEnable;
        _model.FactoryResetEnable = FactoryResetEnable;
        _model.SelfCheckTime = SelfCheckTime;
        _model.RisoEnable = RisoEnable;
        _model.SolarArcFaultMode = SolarArcFaultMode;
        _model.PvEnergyCorrectionPercent = PvEnergyCorrectionPercent;
        _model.TestMode = TestMode;
        _model.MpptFunction = MpptFunction;
        _model.OutputVolt = OutputVolt;
        _model.OutputCurrent = OutputCurrent;
        _model.OutputPower = OutputPower;
        _model.PVTotalPower = PVTotalPower;
        _model.DCVolt1 = DCVolt1;
        _model.DCCurrent1 = DCCurrent1;
        _model.DCPower1 = DCPower1;
        _model.DCVolt2 = DCVolt2;
        _model.DCCurrent2 = DCCurrent2;
        _model.DCPower2 = DCPower2;
        _model.DCVolt3 = DCVolt3;
        _model.DCCurrent3 = DCCurrent3;
        _model.DCPower3 = DCPower3;
        _model.DCVolt4 = DCVolt4;
        _model.DCCurrent4 = DCCurrent4;
        _model.DCPower4 = DCPower4;
        _model.DCVolt5 = DCVolt5;
        _model.DCCurrent5 = DCCurrent5;
        _model.DCPower5 = DCPower5;
        _model.DCVolt6 = DCVolt6;
        _model.DCCurrent6 = DCCurrent6;
        _model.DCPower6 = DCPower6;
        _model.DCVolt7 = DCVolt7;
        _model.DCCurrent7 = DCCurrent7;
        _model.DCPower7 = DCPower7;
        _model.DCVolt8 = DCVolt8;
        _model.DCCurrent8 = DCCurrent8;
        _model.DCPower8 = DCPower8;
        _model.RelayAfterVoltage = RelayAfterVoltage;
        _model.DailyPVGen1 = DailyPVGen1;
        _model.DailyPVGen2 = DailyPVGen2;
        _model.DailyPVGen3 = DailyPVGen3;
        _model.DailyPVGen4 = DailyPVGen4;
        _model.DailyPVGen5 = DailyPVGen5;
        _model.DailyPVGen6 = DailyPVGen6;
        _model.DailyPVGen7 = DailyPVGen7;
        _model.DailyPVGen8 = DailyPVGen8;
        _model.DailyPVGenTotal = DailyPVGenTotal;
        _model.HistoryPvGenCapacity = HistoryPvGenCapacity;
        _model.HeatSinkTemp = HeatSinkTemp;
        _model.TestState = TestState;
        _model.InternalFanAd = InternalFanAd;
        _model.ExternalFanAd = ExternalFanAd;
        _model.TestData1 = TestData1;
        _model.TestData2 = TestData2;
        _model.TestData3 = TestData3;
        _model.TestData4 = TestData4;
        _model.TestData5 = TestData5;
        _model.TestData6 = TestData6;
        _model.TestData7 = TestData7;
        _model.TestData8 = TestData8;
        _model.TestData9 = TestData9;
        _model.MpptOperatingMode = MpptOperatingMode;
        _model.MPPTAlarm1 = CalcBitmask(Alarm1Items);
        _model.MPPTAlarm2 = CalcBitmask(Alarm2Items);
        _model.MPPTFault1 = CalcBitmask(Fault1Items);
        _model.MPPTFault2 = CalcBitmask(Fault2Items);
        _model.MPPTFault3 = CalcBitmask(Fault3Items);
        _model.MPPTFault4 = CalcBitmask(Fault4Items);
    }

    public override void GenerateData()
    {
        var rnd = new Random();
        DCVolt1 = Math.Round(300 + rnd.NextDouble() * 250, 1);
        DCCurrent1 = Math.Round(rnd.NextDouble() * 35, 1);
        DCPower1 = Math.Round(DCVolt1 * DCCurrent1, 0);
        DailyPVGen1 = Math.Round(rnd.NextDouble() * 80, 1);
        DCVolt2 = Math.Round(300 + rnd.NextDouble() * 250, 1);
        DCCurrent2 = Math.Round(rnd.NextDouble() * 35, 1);
        DCPower2 = Math.Round(DCVolt2 * DCCurrent2, 0);
        DailyPVGen2 = Math.Round(rnd.NextDouble() * 80, 1);
        DCVolt3 = Math.Round(300 + rnd.NextDouble() * 250, 1);
        DCCurrent3 = Math.Round(rnd.NextDouble() * 35, 1);
        DCPower3 = Math.Round(DCVolt3 * DCCurrent3, 0);
        DailyPVGen3 = Math.Round(rnd.NextDouble() * 80, 1);
        DCVolt4 = Math.Round(300 + rnd.NextDouble() * 250, 1);
        DCCurrent4 = Math.Round(rnd.NextDouble() * 35, 1);
        DCPower4 = Math.Round(DCVolt4 * DCCurrent4, 0);
        DailyPVGen4 = Math.Round(rnd.NextDouble() * 80, 1);
        DCVolt5 = Math.Round(300 + rnd.NextDouble() * 250, 1);
        DCCurrent5 = Math.Round(rnd.NextDouble() * 35, 1);
        DCPower5 = Math.Round(DCVolt5 * DCCurrent5, 0);
        DailyPVGen5 = Math.Round(rnd.NextDouble() * 80, 1);
        DCVolt6 = Math.Round(300 + rnd.NextDouble() * 250, 1);
        DCCurrent6 = Math.Round(rnd.NextDouble() * 35, 1);
        DCPower6 = Math.Round(DCVolt6 * DCCurrent6, 0);
        DailyPVGen6 = Math.Round(rnd.NextDouble() * 80, 1);
        DCVolt7 = Math.Round(300 + rnd.NextDouble() * 250, 1);
        DCCurrent7 = Math.Round(rnd.NextDouble() * 35, 1);
        DCPower7 = Math.Round(DCVolt7 * DCCurrent7, 0);
        DailyPVGen7 = Math.Round(rnd.NextDouble() * 80, 1);
        DCVolt8 = Math.Round(300 + rnd.NextDouble() * 250, 1);
        DCCurrent8 = Math.Round(rnd.NextDouble() * 35, 1);
        DCPower8 = Math.Round(DCVolt8 * DCCurrent8, 0);
        DailyPVGen8 = Math.Round(rnd.NextDouble() * 80, 1);
        PVTotalPower = Math.Round(DCPower1 + DCPower2 + DCPower3 + DCPower4 + DCPower5 + DCPower6 + DCPower7 + DCPower8, 0);
        OutputPower = Math.Round(PVTotalPower * 0.98, 0);
        OutputVolt = Math.Round(580 + rnd.NextDouble() * 60, 1);
        OutputCurrent = Math.Round(OutputVolt <= 0 ? 0 : OutputPower / OutputVolt, 1);
        RelayAfterVoltage = OutputVolt;
        DailyPVGenTotal = Math.Round(DailyPVGen1 + DailyPVGen2 + DailyPVGen3 + DailyPVGen4 + DailyPVGen5 + DailyPVGen6 + DailyPVGen7 + DailyPVGen8, 1);
        HistoryPvGenCapacity = Math.Round(HistoryPvGenCapacity + DailyPVGenTotal, 1);
        HeatSinkTemp = Math.Round(25 + rnd.NextDouble() * 35, 1);
        InternalFanAd = (ushort)rnd.Next(0, 4096);
        ExternalFanAd = (ushort)rnd.Next(0, 4096);
        MpptOperatingMode = 2;
        PowerEnable = 1;
        base.GenerateData();
    }

    public override void ClearAlarms()
    {
        foreach (var item in Alarm1Items) item.IsChecked = false;
        foreach (var item in Alarm2Items) item.IsChecked = false;
        foreach (var item in Fault1Items) item.IsChecked = false;
        foreach (var item in Fault2Items) item.IsChecked = false;
        foreach (var item in Fault3Items) item.IsChecked = false;
        foreach (var item in Fault4Items) item.IsChecked = false;
        base.ClearAlarms();
    }
}
