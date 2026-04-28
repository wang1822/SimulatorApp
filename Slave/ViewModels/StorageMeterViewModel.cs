using SimulatorApp.Shared.Services;
using SimulatorApp.Slave.Models.StorageMeter;
using SimulatorApp.Slave.Services;

namespace SimulatorApp.Slave.ViewModels;

/// <summary>
/// 储能电表 ViewModel（字段与外部电表完全相同，基地址不同）。
/// 通过传入 StorageMeterModel 实现基地址差异。
/// </summary>
public class StorageMeterViewModel : ExternalMeterViewModel
{
    public StorageMeterViewModel(RegisterBank bank, RegisterMapService mapService)
        : base(bank, mapService, new StorageMeterModel())
    {
    }
}
