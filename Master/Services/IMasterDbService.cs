using SimulatorApp.Master.Models;

namespace SimulatorApp.Master.Services;

/// <summary>
/// 主站数据库服务接口（SQL Server）
/// </summary>
public interface IMasterDbService
{
    /// <summary>自动建表（首次运行或数据库为空时调用）</summary>
    Task InitializeAsync();

    // ── 站点 CRUD ──
    Task<List<MasterStation>>        GetAllStationsAsync();
    Task<MasterStation?>             GetStationAsync(int id);
    /// <returns>站点 ID（新建时返回 IDENTITY 值）</returns>
    Task<int>                        SaveStationAsync(MasterStation station);
    Task                             DeleteStationAsync(int id);

    // ── 寄存器配置 CRUD ──
    Task<List<MasterRegisterConfig>> GetRegisterConfigsAsync(int stationId);
    /// <summary>先删除该站所有配置，再批量插入</summary>
    Task                             SaveRegisterConfigsAsync(int stationId, List<MasterRegisterConfig> configs);

    // ── 名称实时编辑 ──
    Task UpdateRegisterNamesAsync(int configId, string chineseName, string variableName);
    Task UpdateStationNameAsync(int stationId, string name);

    // ── 遥控写入值持久化 ──
    /// <summary>保存遥控行上次写入的原始寄存器字符串与物理值</summary>
    Task UpdateLastWrittenAsync(int configId, string rawRegisters, string physicalValue);

    // ── IsVerified 实时共享 ──
    /// <summary>写单行绿点状态到 DB</summary>
    Task UpdateIsVerifiedAsync(int configId, bool isVerified);
    /// <summary>批量读绿点状态，返回 {configId → isVerified}</summary>
    Task<Dictionary<int, bool>> GetIsVerifiedMapAsync(IEnumerable<int> configIds);
    /// <summary>清除该站点所有寄存器的绿点标记</summary>
    Task ClearAllIsVerifiedAsync(int stationId);

    // ── 寄存器配置单行删除 ──
    Task DeleteRegisterConfigAsync(int configId);
}
