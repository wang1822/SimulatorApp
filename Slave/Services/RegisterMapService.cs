using SimulatorApp.Shared.Services;
using SimulatorApp.Slave.Models;
using System.IO;
using System.Text.Json;

namespace SimulatorApp.Slave.Services;

/// <summary>
/// 寄存器映射服务：负责将所有设备模型数据写入 RegisterBank，以及 JSON 快照的基础读写。
/// </summary>
public class RegisterMapService
{
    private readonly RegisterBank _bank;

    public RegisterMapService(RegisterBank bank)
    {
        _bank = bank;
    }

    /// <summary>
    /// 将单个设备模型的当前字段值刷新到 RegisterBank。
    /// 由 DeviceViewModelBase.FlushToRegisters() 调用。
    /// </summary>
    public void Flush(DeviceModelBase model)
    {
        model.ToRegisters(_bank);
    }

    /// <summary>
    /// 将多个设备模型批量刷新到 RegisterBank。
    /// </summary>
    public void FlushAll(IEnumerable<DeviceModelBase> models)
    {
        foreach (var model in models)
            model.ToRegisters(_bank);
    }

    /// <summary>
    /// 将所有设备的当前字段值序列化为 JSON 文件。
    /// JSON 结构：{ "设备名": { "字段名": 值, ... }, ... }
    /// </summary>
    public void SaveSnapshot(string filePath, IEnumerable<(string DeviceName, object FieldsObject)> deviceSnapshots)
    {
        var dict = deviceSnapshots.ToDictionary(x => x.DeviceName, x => x.FieldsObject);
        var options = new JsonSerializerOptions { WriteIndented = true };
        string json = JsonSerializer.Serialize(dict, options);
        File.WriteAllText(filePath, json, System.Text.Encoding.UTF8);
    }

    /// <summary>
    /// 从 JSON 文件加载快照，返回原始字典（调用方负责将值映射回 ViewModel）。
    /// </summary>
    public Dictionary<string, JsonElement>? LoadSnapshot(string filePath)
    {
        if (!File.Exists(filePath)) return null;
        string json = File.ReadAllText(filePath, System.Text.Encoding.UTF8);
        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
    }
}
