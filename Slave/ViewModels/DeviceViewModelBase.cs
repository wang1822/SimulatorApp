using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SimulatorApp.Shared.Logging;
using SimulatorApp.Shared.Services;
using SimulatorApp.Slave.Models;
using SimulatorApp.Slave.Services;

namespace SimulatorApp.Slave.ViewModels;

/// <summary>
/// 从站设备 ViewModel 抽象基类。
/// 提供：
///   - IsExpanded：Expander 折叠状态
///   - FlushToRegisters()：将 ViewModel 字段同步到 RegisterBank
///   - GenerateData()：一键生成随机测试数据
///   - ClearAlarms()：清除所有故障/告警
///   - ExportExcel/ImportExcel：单设备 Excel 导入导出
/// 子类通过 [ObservableProperty] + partial void OnXxxChanged => FlushToRegisters() 实现实时同步。
/// </summary>
public abstract partial class DeviceViewModelBase : ObservableObject
{
    protected readonly RegisterBank    _bank;
    protected readonly RegisterMapService _mapService;

    [ObservableProperty]
    private bool _isExpanded;

    /// <summary>
    /// 是否启用此设备：勾选后启动监听时寄存器才写入，定时自动模拟才运行，
    /// 导出快照/设备 Excel 时才包含此设备。
    /// </summary>
    [ObservableProperty]
    private bool _isSimulating = false;

    /// <summary>
    /// 勾选 → 立刻将当前字段值刷入 RegisterBank；
    /// 取消勾选 → 立刻将该设备的寄存器范围清零，
    /// 确保 EMS 在监听期间读取到全 0 而不是残留数据。
    /// </summary>
    partial void OnIsSimulatingChanged(bool value)
    {
        if (value)
        {
            FlushToRegisters();
        }
        else if (Model.RegisterCount > 0)
        {
            _bank.WriteRange(Model.BaseAddress, new ushort[Model.RegisterCount]);
        }
    }

    /// <summary>设备中文名（显示在左侧列表和 Expander 标题）</summary>
    public abstract string DeviceName { get; }

    /// <summary>true 表示该设备由协议文档导入，左侧列表将其放入折叠区</summary>
    public virtual bool IsImported => false;

    /// <summary>该设备对应的 Model 实例（用于批量刷新寄存器）</summary>
    protected abstract DeviceModelBase Model { get; }

    protected DeviceViewModelBase(RegisterBank bank, RegisterMapService mapService)
    {
        _bank       = bank;
        _mapService = mapService;
    }

    // ----------------------------------------------------------------
    // 核心操作
    // ----------------------------------------------------------------

    /// <summary>
    /// 将当前所有 ViewModel 字段值同步到 Model，再刷新到 RegisterBank。
    /// 每次 [ObservableProperty] 属性变更时调用。
    /// </summary>
    public virtual void FlushToRegisters()
    {
        try
        {
            SyncToModel();
            _mapService.Flush(Model);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"{DeviceName} FlushToRegisters 异常", ex);
        }
    }

    /// <summary>将 ViewModel 字段值写入 Model（子类实现）</summary>
    protected abstract void SyncToModel();

    // ----------------------------------------------------------------
    // 辅助方法：值范围检查（超量程时截断并记录 WARN）
    // ----------------------------------------------------------------

    protected double Clamp(double value, double min, double max, string fieldName)
    {
        if (value < min)
        {
            AppLogger.Warn($"{DeviceName}.{fieldName} 值 {value} 低于最小值 {min}，已截断");
            return min;
        }
        if (value > max)
        {
            AppLogger.Warn($"{DeviceName}.{fieldName} 值 {value} 超过最大值 {max}，已截断");
            return max;
        }
        return value;
    }

    protected int ClampInt(int value, int min, int max, string fieldName)
    {
        if (value < min) { AppLogger.Warn($"{DeviceName}.{fieldName}={value} < {min}，已截断"); return min; }
        if (value > max) { AppLogger.Warn($"{DeviceName}.{fieldName}={value} > {max}，已截断"); return max; }
        return value;
    }

    // ----------------------------------------------------------------
    // AlarmItem 工厂与 bitmask 计算
    // ----------------------------------------------------------------

    /// <summary>从 AlarmItem 列表计算 bitmask 合并值（OR 合并）</summary>
    protected static ushort CalcBitmask(IEnumerable<AlarmItem> items)
        => (ushort)items.Where(x => x.IsChecked).Aggregate(0, (acc, x) => acc | x.BitMask);

    /// <summary>根据 bitmask 值反向设置 AlarmItem 的 IsChecked 状态</summary>
    protected static void SetBitmask(IEnumerable<AlarmItem> items, ushort mask)
    {
        foreach (var item in items)
            item.IsChecked = (mask & item.BitMask) != 0;
    }

    // ----------------------------------------------------------------
    // 命令：一键生成数据、清除告警
    // ----------------------------------------------------------------

    [RelayCommand]
    public virtual void GenerateData()
    {
        // 子类重写，生成合理的随机数据
        AppLogger.Info($"{DeviceName} 已生成随机测试数据");
        FlushToRegisters();
    }

    [RelayCommand]
    public virtual void ClearAlarms()
    {
        // 子类重写，将所有 AlarmItem.IsChecked = false
        AppLogger.Info($"{DeviceName} 告警/故障已全部清除");
        FlushToRegisters();
    }
}
