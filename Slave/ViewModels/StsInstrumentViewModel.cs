using CommunityToolkit.Mvvm.ComponentModel;
using SimulatorApp.Shared.Services;
using SimulatorApp.Slave.Models;
using SimulatorApp.Slave.Models.StsInstrument;
using SimulatorApp.Slave.Services;
using System.Collections.ObjectModel;

namespace SimulatorApp.Slave.ViewModels;

/// <summary>
/// GS215STS ViewModel，字段依据 STS_500_Modbus协议.xlsx 的 5000-5233 寄存器表。
/// </summary>
public partial class StsInstrumentViewModel : DeviceViewModelBase
{
    private readonly StsInstrumentModel _model = new();

    public override string DeviceName => "GS215STS";
    protected override DeviceModelBase Model => _model;

    public StsInstrumentViewModel(RegisterBank bank, RegisterMapService mapService)
        : base(bank, mapService)
    {
        InitAlarmItems();
        FlushToRegisters();
    }

    [ObservableProperty] private int _testStateSet = 0;
    [ObservableProperty] private int _factoryTestEntry = 0;
    [ObservableProperty] private int _gridVoltageCalibration = 0;
    [ObservableProperty] private int _generatorVoltageCalibration = 0;
    [ObservableProperty] private int _inverterVoltageCalibration = 0;
    [ObservableProperty] private int _gridCurrentCalibration = 0;
    [ObservableProperty] private int _generatorCurrentCalibration = 0;
    [ObservableProperty] private int _energyCalibration = 0;
    [ObservableProperty] private int _fanTest = 0;
    [ObservableProperty] private int _operatingState = 2;
    [ObservableProperty] private double _gridVoltageA = 220.0;
    [ObservableProperty] private double _gridVoltageB = 220.0;
    [ObservableProperty] private double _gridVoltageC = 220.0;
    [ObservableProperty] private double _gridVoltageAB = 380.0;
    [ObservableProperty] private double _gridVoltageBC = 380.0;
    [ObservableProperty] private double _gridVoltageCA = 380.0;
    [ObservableProperty] private double _gridCurrentA = 0.0;
    [ObservableProperty] private double _gridCurrentB = 0.0;
    [ObservableProperty] private double _gridCurrentC = 0.0;
    [ObservableProperty] private double _gridFrequency = 50.0;
    [ObservableProperty] private double _generatorVoltageA = 0.0;
    [ObservableProperty] private double _generatorVoltageB = 0.0;
    [ObservableProperty] private double _generatorVoltageC = 0.0;
    [ObservableProperty] private double _generatorVoltageAB = 0.0;
    [ObservableProperty] private double _generatorVoltageBC = 0.0;
    [ObservableProperty] private double _generatorVoltageCA = 0.0;
    [ObservableProperty] private double _generatorCurrentA = 0.0;
    [ObservableProperty] private double _generatorCurrentB = 0.0;
    [ObservableProperty] private double _generatorCurrentC = 0.0;
    [ObservableProperty] private double _generatorFrequency = 0.0;
    [ObservableProperty] private double _inverterVoltageA = 220.0;
    [ObservableProperty] private double _inverterVoltageB = 220.0;
    [ObservableProperty] private double _inverterVoltageC = 220.0;
    [ObservableProperty] private double _inverterVoltageAB = 380.0;
    [ObservableProperty] private double _inverterVoltageBC = 380.0;
    [ObservableProperty] private double _inverterVoltageCA = 380.0;
    [ObservableProperty] private double _inverterFrequency = 50.0;
    [ObservableProperty] private double _temperature1 = 25.0;
    [ObservableProperty] private double _temperature2 = 25.0;
    [ObservableProperty] private double _temperature3 = 25.0;
    [ObservableProperty] private ushort _breakerStatus = 0;
    [ObservableProperty] private ushort _internalFanAd = 0;
    [ObservableProperty] private ushort _externalFanAd = 0;
    [ObservableProperty] private int _testState = 0;
    [ObservableProperty] private ushort _rsd1 = 0;
    [ObservableProperty] private ushort _rsd2 = 0;
    [ObservableProperty] private ushort _rsd3 = 0;
    [ObservableProperty] private ushort _rsd4 = 0;
    [ObservableProperty] private ushort _rsd5 = 0;
    [ObservableProperty] private double _parallelVoltageA = 220.0;
    [ObservableProperty] private double _parallelVoltageB = 220.0;
    [ObservableProperty] private double _parallelVoltageC = 220.0;
    [ObservableProperty] private double _parallelVoltageAB = 380.0;
    [ObservableProperty] private double _parallelVoltageBC = 380.0;
    [ObservableProperty] private double _parallelVoltageCA = 380.0;
    [ObservableProperty] private double _parallelCurrentA = 0.0;
    [ObservableProperty] private double _parallelCurrentB = 0.0;
    [ObservableProperty] private double _parallelCurrentC = 0.0;
    [ObservableProperty] private double _parallelFrequency = 50.0;
    [ObservableProperty] private double _gridTotalPower = 0.0;
    [ObservableProperty] private double _gridPhaseAPower = 0.0;
    [ObservableProperty] private double _gridPhaseBPower = 0.0;
    [ObservableProperty] private double _gridPhaseCPower = 0.0;
    [ObservableProperty] private double _gridTotalReactivePower = 0.0;
    [ObservableProperty] private double _gridPhaseAReactivePower = 0.0;
    [ObservableProperty] private double _gridPhaseBReactivePower = 0.0;
    [ObservableProperty] private double _gridPhaseCReactivePower = 0.0;
    [ObservableProperty] private double _gridDailySoldEnergy = 0.0;
    [ObservableProperty] private double _gridDailyPurchasedEnergy = 0.0;
    [ObservableProperty] private double _gridTotalSoldEnergy = 0.0;
    [ObservableProperty] private double _gridTotalPurchasedEnergy = 0.0;
    [ObservableProperty] private double _generatorTotalPower = 0.0;
    [ObservableProperty] private double _generatorPhaseAPower = 0.0;
    [ObservableProperty] private double _generatorPhaseBPower = 0.0;
    [ObservableProperty] private double _generatorPhaseCPower = 0.0;
    [ObservableProperty] private double _generatorTotalReactivePower = 0.0;
    [ObservableProperty] private double _generatorPhaseAReactivePower = 0.0;
    [ObservableProperty] private double _generatorPhaseBReactivePower = 0.0;
    [ObservableProperty] private double _generatorPhaseCReactivePower = 0.0;
    [ObservableProperty] private double _generatorDailySoldEnergy = 0.0;
    [ObservableProperty] private double _generatorDailyPurchasedEnergy = 0.0;
    [ObservableProperty] private double _generatorTotalSoldEnergy = 0.0;
    [ObservableProperty] private double _generatorTotalPurchasedEnergy = 0.0;
    [ObservableProperty] private uint _gridRelayStatus = 0;
    [ObservableProperty] private uint _generatorRelayStatus = 0;
    [ObservableProperty] private uint _parallelRelayStatus = 0;
    [ObservableProperty] private double _parallelTotalPower = 0.0;
    [ObservableProperty] private double _parallelPhaseAPower = 0.0;
    [ObservableProperty] private double _parallelPhaseBPower = 0.0;
    [ObservableProperty] private double _parallelPhaseCPower = 0.0;
    [ObservableProperty] private double _parallelTotalReactivePower = 0.0;
    [ObservableProperty] private double _parallelPhaseAReactivePower = 0.0;
    [ObservableProperty] private double _parallelPhaseBReactivePower = 0.0;
    [ObservableProperty] private double _parallelPhaseCReactivePower = 0.0;
    [ObservableProperty] private double _parallelDailySoldEnergy = 0.0;
    [ObservableProperty] private double _parallelDailyPurchasedEnergy = 0.0;
    [ObservableProperty] private double _parallelTotalSoldEnergy = 0.0;
    [ObservableProperty] private double _parallelTotalPurchasedEnergy = 0.0;

    partial void OnTestStateSetChanged(int value) => FlushToRegisters();
    partial void OnFactoryTestEntryChanged(int value) => FlushToRegisters();
    partial void OnGridVoltageCalibrationChanged(int value) => FlushToRegisters();
    partial void OnGeneratorVoltageCalibrationChanged(int value) => FlushToRegisters();
    partial void OnInverterVoltageCalibrationChanged(int value) => FlushToRegisters();
    partial void OnGridCurrentCalibrationChanged(int value) => FlushToRegisters();
    partial void OnGeneratorCurrentCalibrationChanged(int value) => FlushToRegisters();
    partial void OnEnergyCalibrationChanged(int value) => FlushToRegisters();
    partial void OnFanTestChanged(int value) => FlushToRegisters();
    partial void OnOperatingStateChanged(int value) => FlushToRegisters();
    partial void OnGridVoltageAChanged(double value) => FlushToRegisters();
    partial void OnGridVoltageBChanged(double value) => FlushToRegisters();
    partial void OnGridVoltageCChanged(double value) => FlushToRegisters();
    partial void OnGridVoltageABChanged(double value) => FlushToRegisters();
    partial void OnGridVoltageBCChanged(double value) => FlushToRegisters();
    partial void OnGridVoltageCAChanged(double value) => FlushToRegisters();
    partial void OnGridCurrentAChanged(double value) => FlushToRegisters();
    partial void OnGridCurrentBChanged(double value) => FlushToRegisters();
    partial void OnGridCurrentCChanged(double value) => FlushToRegisters();
    partial void OnGridFrequencyChanged(double value) => FlushToRegisters();
    partial void OnGeneratorVoltageAChanged(double value) => FlushToRegisters();
    partial void OnGeneratorVoltageBChanged(double value) => FlushToRegisters();
    partial void OnGeneratorVoltageCChanged(double value) => FlushToRegisters();
    partial void OnGeneratorVoltageABChanged(double value) => FlushToRegisters();
    partial void OnGeneratorVoltageBCChanged(double value) => FlushToRegisters();
    partial void OnGeneratorVoltageCAChanged(double value) => FlushToRegisters();
    partial void OnGeneratorCurrentAChanged(double value) => FlushToRegisters();
    partial void OnGeneratorCurrentBChanged(double value) => FlushToRegisters();
    partial void OnGeneratorCurrentCChanged(double value) => FlushToRegisters();
    partial void OnGeneratorFrequencyChanged(double value) => FlushToRegisters();
    partial void OnInverterVoltageAChanged(double value) => FlushToRegisters();
    partial void OnInverterVoltageBChanged(double value) => FlushToRegisters();
    partial void OnInverterVoltageCChanged(double value) => FlushToRegisters();
    partial void OnInverterVoltageABChanged(double value) => FlushToRegisters();
    partial void OnInverterVoltageBCChanged(double value) => FlushToRegisters();
    partial void OnInverterVoltageCAChanged(double value) => FlushToRegisters();
    partial void OnInverterFrequencyChanged(double value) => FlushToRegisters();
    partial void OnTemperature1Changed(double value) => FlushToRegisters();
    partial void OnTemperature2Changed(double value) => FlushToRegisters();
    partial void OnTemperature3Changed(double value) => FlushToRegisters();
    partial void OnBreakerStatusChanged(ushort value) => FlushToRegisters();
    partial void OnInternalFanAdChanged(ushort value) => FlushToRegisters();
    partial void OnExternalFanAdChanged(ushort value) => FlushToRegisters();
    partial void OnTestStateChanged(int value) => FlushToRegisters();
    partial void OnRsd1Changed(ushort value) => FlushToRegisters();
    partial void OnRsd2Changed(ushort value) => FlushToRegisters();
    partial void OnRsd3Changed(ushort value) => FlushToRegisters();
    partial void OnRsd4Changed(ushort value) => FlushToRegisters();
    partial void OnRsd5Changed(ushort value) => FlushToRegisters();
    partial void OnParallelVoltageAChanged(double value) => FlushToRegisters();
    partial void OnParallelVoltageBChanged(double value) => FlushToRegisters();
    partial void OnParallelVoltageCChanged(double value) => FlushToRegisters();
    partial void OnParallelVoltageABChanged(double value) => FlushToRegisters();
    partial void OnParallelVoltageBCChanged(double value) => FlushToRegisters();
    partial void OnParallelVoltageCAChanged(double value) => FlushToRegisters();
    partial void OnParallelCurrentAChanged(double value) => FlushToRegisters();
    partial void OnParallelCurrentBChanged(double value) => FlushToRegisters();
    partial void OnParallelCurrentCChanged(double value) => FlushToRegisters();
    partial void OnParallelFrequencyChanged(double value) => FlushToRegisters();
    partial void OnGridTotalPowerChanged(double value) => FlushToRegisters();
    partial void OnGridPhaseAPowerChanged(double value) => FlushToRegisters();
    partial void OnGridPhaseBPowerChanged(double value) => FlushToRegisters();
    partial void OnGridPhaseCPowerChanged(double value) => FlushToRegisters();
    partial void OnGridTotalReactivePowerChanged(double value) => FlushToRegisters();
    partial void OnGridPhaseAReactivePowerChanged(double value) => FlushToRegisters();
    partial void OnGridPhaseBReactivePowerChanged(double value) => FlushToRegisters();
    partial void OnGridPhaseCReactivePowerChanged(double value) => FlushToRegisters();
    partial void OnGridDailySoldEnergyChanged(double value) => FlushToRegisters();
    partial void OnGridDailyPurchasedEnergyChanged(double value) => FlushToRegisters();
    partial void OnGridTotalSoldEnergyChanged(double value) => FlushToRegisters();
    partial void OnGridTotalPurchasedEnergyChanged(double value) => FlushToRegisters();
    partial void OnGeneratorTotalPowerChanged(double value) => FlushToRegisters();
    partial void OnGeneratorPhaseAPowerChanged(double value) => FlushToRegisters();
    partial void OnGeneratorPhaseBPowerChanged(double value) => FlushToRegisters();
    partial void OnGeneratorPhaseCPowerChanged(double value) => FlushToRegisters();
    partial void OnGeneratorTotalReactivePowerChanged(double value) => FlushToRegisters();
    partial void OnGeneratorPhaseAReactivePowerChanged(double value) => FlushToRegisters();
    partial void OnGeneratorPhaseBReactivePowerChanged(double value) => FlushToRegisters();
    partial void OnGeneratorPhaseCReactivePowerChanged(double value) => FlushToRegisters();
    partial void OnGeneratorDailySoldEnergyChanged(double value) => FlushToRegisters();
    partial void OnGeneratorDailyPurchasedEnergyChanged(double value) => FlushToRegisters();
    partial void OnGeneratorTotalSoldEnergyChanged(double value) => FlushToRegisters();
    partial void OnGeneratorTotalPurchasedEnergyChanged(double value) => FlushToRegisters();
    partial void OnGridRelayStatusChanged(uint value) => FlushToRegisters();
    partial void OnGeneratorRelayStatusChanged(uint value) => FlushToRegisters();
    partial void OnParallelRelayStatusChanged(uint value) => FlushToRegisters();
    partial void OnParallelTotalPowerChanged(double value) => FlushToRegisters();
    partial void OnParallelPhaseAPowerChanged(double value) => FlushToRegisters();
    partial void OnParallelPhaseBPowerChanged(double value) => FlushToRegisters();
    partial void OnParallelPhaseCPowerChanged(double value) => FlushToRegisters();
    partial void OnParallelTotalReactivePowerChanged(double value) => FlushToRegisters();
    partial void OnParallelPhaseAReactivePowerChanged(double value) => FlushToRegisters();
    partial void OnParallelPhaseBReactivePowerChanged(double value) => FlushToRegisters();
    partial void OnParallelPhaseCReactivePowerChanged(double value) => FlushToRegisters();
    partial void OnParallelDailySoldEnergyChanged(double value) => FlushToRegisters();
    partial void OnParallelDailyPurchasedEnergyChanged(double value) => FlushToRegisters();
    partial void OnParallelTotalSoldEnergyChanged(double value) => FlushToRegisters();
    partial void OnParallelTotalPurchasedEnergyChanged(double value) => FlushToRegisters();

    public IReadOnlyList<ComboItem> StsOperatingModeItems => ModeItems;

    public IReadOnlyList<ComboItem> ModeItems { get; } = new[]
    {
        new ComboItem(0, "待机"),
        new ComboItem(1, "自检"),
        new ComboItem(2, "正常"),
        new ComboItem(3, "告警"),
        new ComboItem(4, "故障")
    };

    public ObservableCollection<AlarmItem> Alarm1Items { get; } = new();
    public ObservableCollection<AlarmItem> Alarm2Items { get; } = new();
    public ObservableCollection<AlarmItem> Fault1Items { get; } = new();
    public ObservableCollection<AlarmItem> Fault2Items { get; } = new();
    public ObservableCollection<AlarmItem> Fault3Items { get; } = new();
    public ObservableCollection<AlarmItem> Fault4Items { get; } = new();

    // 兼容旧界面绑定。
    public ObservableCollection<AlarmItem> AlarmItems => Alarm1Items;
    public ObservableCollection<AlarmItem> FaultItems => Fault1Items;

    private void InitAlarmItems()
    {
        AddBitItems(Alarm1Items, "STS告警1");
        AddBitItems(Alarm2Items, "STS告警2");
        AddBitItems(Fault1Items, "STS故障1");
        AddBitItems(Fault2Items, "STS故障2");
        AddBitItems(Fault3Items, "STS故障3");
        AddBitItems(Fault4Items, "STS故障4");
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
        _model.TestStateSet = TestStateSet;
        _model.FactoryTestEntry = FactoryTestEntry;
        _model.GridVoltageCalibration = GridVoltageCalibration;
        _model.GeneratorVoltageCalibration = GeneratorVoltageCalibration;
        _model.InverterVoltageCalibration = InverterVoltageCalibration;
        _model.GridCurrentCalibration = GridCurrentCalibration;
        _model.GeneratorCurrentCalibration = GeneratorCurrentCalibration;
        _model.EnergyCalibration = EnergyCalibration;
        _model.FanTest = FanTest;
        _model.OperatingState = OperatingState;
        _model.GridVoltageA = GridVoltageA;
        _model.GridVoltageB = GridVoltageB;
        _model.GridVoltageC = GridVoltageC;
        _model.GridVoltageAB = GridVoltageAB;
        _model.GridVoltageBC = GridVoltageBC;
        _model.GridVoltageCA = GridVoltageCA;
        _model.GridCurrentA = GridCurrentA;
        _model.GridCurrentB = GridCurrentB;
        _model.GridCurrentC = GridCurrentC;
        _model.GridFrequency = GridFrequency;
        _model.GeneratorVoltageA = GeneratorVoltageA;
        _model.GeneratorVoltageB = GeneratorVoltageB;
        _model.GeneratorVoltageC = GeneratorVoltageC;
        _model.GeneratorVoltageAB = GeneratorVoltageAB;
        _model.GeneratorVoltageBC = GeneratorVoltageBC;
        _model.GeneratorVoltageCA = GeneratorVoltageCA;
        _model.GeneratorCurrentA = GeneratorCurrentA;
        _model.GeneratorCurrentB = GeneratorCurrentB;
        _model.GeneratorCurrentC = GeneratorCurrentC;
        _model.GeneratorFrequency = GeneratorFrequency;
        _model.InverterVoltageA = InverterVoltageA;
        _model.InverterVoltageB = InverterVoltageB;
        _model.InverterVoltageC = InverterVoltageC;
        _model.InverterVoltageAB = InverterVoltageAB;
        _model.InverterVoltageBC = InverterVoltageBC;
        _model.InverterVoltageCA = InverterVoltageCA;
        _model.InverterFrequency = InverterFrequency;
        _model.Temperature1 = Temperature1;
        _model.Temperature2 = Temperature2;
        _model.Temperature3 = Temperature3;
        _model.BreakerStatus = BreakerStatus;
        _model.InternalFanAd = InternalFanAd;
        _model.ExternalFanAd = ExternalFanAd;
        _model.TestState = TestState;
        _model.Rsd1 = Rsd1;
        _model.Rsd2 = Rsd2;
        _model.Rsd3 = Rsd3;
        _model.Rsd4 = Rsd4;
        _model.Rsd5 = Rsd5;
        _model.ParallelVoltageA = ParallelVoltageA;
        _model.ParallelVoltageB = ParallelVoltageB;
        _model.ParallelVoltageC = ParallelVoltageC;
        _model.ParallelVoltageAB = ParallelVoltageAB;
        _model.ParallelVoltageBC = ParallelVoltageBC;
        _model.ParallelVoltageCA = ParallelVoltageCA;
        _model.ParallelCurrentA = ParallelCurrentA;
        _model.ParallelCurrentB = ParallelCurrentB;
        _model.ParallelCurrentC = ParallelCurrentC;
        _model.ParallelFrequency = ParallelFrequency;
        _model.GridTotalPower = GridTotalPower;
        _model.GridPhaseAPower = GridPhaseAPower;
        _model.GridPhaseBPower = GridPhaseBPower;
        _model.GridPhaseCPower = GridPhaseCPower;
        _model.GridTotalReactivePower = GridTotalReactivePower;
        _model.GridPhaseAReactivePower = GridPhaseAReactivePower;
        _model.GridPhaseBReactivePower = GridPhaseBReactivePower;
        _model.GridPhaseCReactivePower = GridPhaseCReactivePower;
        _model.GridDailySoldEnergy = GridDailySoldEnergy;
        _model.GridDailyPurchasedEnergy = GridDailyPurchasedEnergy;
        _model.GridTotalSoldEnergy = GridTotalSoldEnergy;
        _model.GridTotalPurchasedEnergy = GridTotalPurchasedEnergy;
        _model.GeneratorTotalPower = GeneratorTotalPower;
        _model.GeneratorPhaseAPower = GeneratorPhaseAPower;
        _model.GeneratorPhaseBPower = GeneratorPhaseBPower;
        _model.GeneratorPhaseCPower = GeneratorPhaseCPower;
        _model.GeneratorTotalReactivePower = GeneratorTotalReactivePower;
        _model.GeneratorPhaseAReactivePower = GeneratorPhaseAReactivePower;
        _model.GeneratorPhaseBReactivePower = GeneratorPhaseBReactivePower;
        _model.GeneratorPhaseCReactivePower = GeneratorPhaseCReactivePower;
        _model.GeneratorDailySoldEnergy = GeneratorDailySoldEnergy;
        _model.GeneratorDailyPurchasedEnergy = GeneratorDailyPurchasedEnergy;
        _model.GeneratorTotalSoldEnergy = GeneratorTotalSoldEnergy;
        _model.GeneratorTotalPurchasedEnergy = GeneratorTotalPurchasedEnergy;
        _model.GridRelayStatus = GridRelayStatus;
        _model.GeneratorRelayStatus = GeneratorRelayStatus;
        _model.ParallelRelayStatus = ParallelRelayStatus;
        _model.ParallelTotalPower = ParallelTotalPower;
        _model.ParallelPhaseAPower = ParallelPhaseAPower;
        _model.ParallelPhaseBPower = ParallelPhaseBPower;
        _model.ParallelPhaseCPower = ParallelPhaseCPower;
        _model.ParallelTotalReactivePower = ParallelTotalReactivePower;
        _model.ParallelPhaseAReactivePower = ParallelPhaseAReactivePower;
        _model.ParallelPhaseBReactivePower = ParallelPhaseBReactivePower;
        _model.ParallelPhaseCReactivePower = ParallelPhaseCReactivePower;
        _model.ParallelDailySoldEnergy = ParallelDailySoldEnergy;
        _model.ParallelDailyPurchasedEnergy = ParallelDailyPurchasedEnergy;
        _model.ParallelTotalSoldEnergy = ParallelTotalSoldEnergy;
        _model.ParallelTotalPurchasedEnergy = ParallelTotalPurchasedEnergy;
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
        GridVoltageA = Math.Round(215 + rnd.NextDouble() * 15, 1);
        GridVoltageB = Math.Round(215 + rnd.NextDouble() * 15, 1);
        GridVoltageC = Math.Round(215 + rnd.NextDouble() * 15, 1);
        GridVoltageAB = Math.Round(375 + rnd.NextDouble() * 15, 1);
        GridVoltageBC = Math.Round(375 + rnd.NextDouble() * 15, 1);
        GridVoltageCA = Math.Round(375 + rnd.NextDouble() * 15, 1);
        GridCurrentA = Math.Round(rnd.NextDouble() * 80, 1);
        GridCurrentB = Math.Round(rnd.NextDouble() * 80, 1);
        GridCurrentC = Math.Round(rnd.NextDouble() * 80, 1);
        GridFrequency = Math.Round(49.8 + rnd.NextDouble() * 0.4, 2);

        GeneratorVoltageA = Math.Round(215 + rnd.NextDouble() * 15, 1);
        GeneratorVoltageB = Math.Round(215 + rnd.NextDouble() * 15, 1);
        GeneratorVoltageC = Math.Round(215 + rnd.NextDouble() * 15, 1);
        GeneratorVoltageAB = Math.Round(375 + rnd.NextDouble() * 15, 1);
        GeneratorVoltageBC = Math.Round(375 + rnd.NextDouble() * 15, 1);
        GeneratorVoltageCA = Math.Round(375 + rnd.NextDouble() * 15, 1);
        GeneratorCurrentA = Math.Round(rnd.NextDouble() * 60, 1);
        GeneratorCurrentB = Math.Round(rnd.NextDouble() * 60, 1);
        GeneratorCurrentC = Math.Round(rnd.NextDouble() * 60, 1);
        GeneratorFrequency = Math.Round(49.8 + rnd.NextDouble() * 0.4, 2);

        InverterVoltageA = GridVoltageA;
        InverterVoltageB = GridVoltageB;
        InverterVoltageC = GridVoltageC;
        InverterVoltageAB = GridVoltageAB;
        InverterVoltageBC = GridVoltageBC;
        InverterVoltageCA = GridVoltageCA;
        InverterFrequency = GridFrequency;

        ParallelVoltageA = GridVoltageA;
        ParallelVoltageB = GridVoltageB;
        ParallelVoltageC = GridVoltageC;
        ParallelVoltageAB = GridVoltageAB;
        ParallelVoltageBC = GridVoltageBC;
        ParallelVoltageCA = GridVoltageCA;
        ParallelCurrentA = GridCurrentA;
        ParallelCurrentB = GridCurrentB;
        ParallelCurrentC = GridCurrentC;
        ParallelFrequency = GridFrequency;

        GridPhaseAPower = Math.Round(GridVoltageA * GridCurrentA, 0);
        GridPhaseBPower = Math.Round(GridVoltageB * GridCurrentB, 0);
        GridPhaseCPower = Math.Round(GridVoltageC * GridCurrentC, 0);
        GridTotalPower = GridPhaseAPower + GridPhaseBPower + GridPhaseCPower;
        GeneratorPhaseAPower = Math.Round(GeneratorVoltageA * GeneratorCurrentA, 0);
        GeneratorPhaseBPower = Math.Round(GeneratorVoltageB * GeneratorCurrentB, 0);
        GeneratorPhaseCPower = Math.Round(GeneratorVoltageC * GeneratorCurrentC, 0);
        GeneratorTotalPower = GeneratorPhaseAPower + GeneratorPhaseBPower + GeneratorPhaseCPower;
        ParallelPhaseAPower = GridPhaseAPower;
        ParallelPhaseBPower = GridPhaseBPower;
        ParallelPhaseCPower = GridPhaseCPower;
        ParallelTotalPower = GridTotalPower;

        GridDailyPurchasedEnergy = Math.Round(rnd.NextDouble() * 200, 1);
        GridTotalPurchasedEnergy = Math.Round(GridTotalPurchasedEnergy + GridDailyPurchasedEnergy, 1);
        GeneratorDailyPurchasedEnergy = Math.Round(rnd.NextDouble() * 100, 1);
        GeneratorTotalPurchasedEnergy = Math.Round(GeneratorTotalPurchasedEnergy + GeneratorDailyPurchasedEnergy, 1);
        ParallelDailyPurchasedEnergy = GridDailyPurchasedEnergy;
        ParallelTotalPurchasedEnergy = GridTotalPurchasedEnergy;
        Temperature1 = Math.Round(25 + rnd.NextDouble() * 25, 1);
        Temperature2 = Math.Round(25 + rnd.NextDouble() * 25, 1);
        Temperature3 = Math.Round(25 + rnd.NextDouble() * 25, 1);
        InternalFanAd = (ushort)rnd.Next(0, 4096);
        ExternalFanAd = (ushort)rnd.Next(0, 4096);
        OperatingState = 2;
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
