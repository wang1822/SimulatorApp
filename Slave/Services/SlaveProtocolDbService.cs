using Microsoft.Data.SqlClient;
using SimulatorApp.Shared.Logging;
using SimulatorApp.Slave.Models;

namespace SimulatorApp.Slave.Services;

using ProtocolRow = (string ChineseName, string EnglishName, int Address, string ReadWrite, string Range, string Unit, string Note);

public interface ISlaveProtocolDbService
{
    Task InitializeAsync();
    Task<int> SaveDeviceConfigAsync(SlaveDeviceConfig config, IEnumerable<ProtocolRow> rows);
    Task DeleteDeviceConfigAsync(int id);
    Task UpdateDeviceNameAsync(int id, string name);
    Task<List<(SlaveDeviceConfig Config, List<ProtocolRow> Rows, Dictionary<int, ushort> CurrentValues)>> GetAllDeviceConfigsAsync();
    Task UpdateRowCurrentValueAsync(int configId, int address, ushort value);
    Task DeleteRowAsync(int configId, int address);
    Task InsertRowAsync(int configId, int sortOrder, string chineseName, string englishName,
                        int address, string readWrite, string range, string unit, string note);
    Task UpdateRowMetadataAsync(int configId, int address, string chineseName, string englishName);
}

/// <summary>
/// 从站协议设备配置持久化到 SQL Server。
/// 主表：SlaveDeviceConfigs（连接参数），从表：SlaveDeviceConfigRows（协议行 + 当前值）。
/// </summary>
public class SlaveProtocolDbService : ISlaveProtocolDbService
{
    private readonly string _cs;
    public SlaveProtocolDbService(string connectionString) => _cs = connectionString;

    public async Task InitializeAsync()
    {
        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        const string ddl =
            "IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE name='SlaveDeviceConfigs' AND type='U')\r\n" +
            "CREATE TABLE SlaveDeviceConfigs (\r\n" +
            "    Id             INT IDENTITY(1,1) PRIMARY KEY,\r\n" +
            "    Name           NVARCHAR(200) NOT NULL,\r\n" +
            "    Protocol       TINYINT       NOT NULL DEFAULT 0,\r\n" +
            "    Host           NVARCHAR(100) NOT NULL DEFAULT '0.0.0.0',\r\n" +
            "    Port           INT           NOT NULL DEFAULT 502,\r\n" +
            "    PortName       NVARCHAR(50)  NOT NULL DEFAULT 'COM3',\r\n" +
            "    BaudRate       INT           NOT NULL DEFAULT 9600,\r\n" +
            "    SlaveId        TINYINT       NOT NULL DEFAULT 1,\r\n" +
            "    PollIntervalMs INT           NOT NULL DEFAULT 1000,\r\n" +
            "    CreatedAt      DATETIME2     NOT NULL DEFAULT GETDATE()\r\n" +
            ");\r\n\r\n" +
            "IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE name='SlaveDeviceConfigRows' AND type='U')\r\n" +
            "CREATE TABLE SlaveDeviceConfigRows (\r\n" +
            "    Id             INT IDENTITY(1,1) PRIMARY KEY,\r\n" +
            "    DeviceConfigId INT            NOT NULL,\r\n" +
            "    SortOrder      INT            NOT NULL DEFAULT 0,\r\n" +
            "    Address        INT            NOT NULL,\r\n" +
            "    ChineseName    NVARCHAR(200)  NOT NULL DEFAULT '',\r\n" +
            "    EnglishName    NVARCHAR(200)  NOT NULL DEFAULT '',\r\n" +
            "    ReadWrite      NVARCHAR(20)   NOT NULL DEFAULT '',\r\n" +
            "    Range          NVARCHAR(2000) NOT NULL DEFAULT '',\r\n" +
            "    Unit           NVARCHAR(50)   NOT NULL DEFAULT '',\r\n" +
            "    Note           NVARCHAR(2000) NOT NULL DEFAULT '',\r\n" +
            "    CurrentValue   INT            NOT NULL DEFAULT 0,\r\n" +
            "    FOREIGN KEY (DeviceConfigId) REFERENCES SlaveDeviceConfigs(Id) ON DELETE CASCADE\r\n" +
            ");\r\n\r\n" +
            "IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('SlaveDeviceConfigRows') AND name='CurrentValue')\r\n" +
            "    ALTER TABLE SlaveDeviceConfigRows ADD CurrentValue INT NOT NULL DEFAULT 0;";
        await using var cmd = new SqlCommand(ddl, conn);
        await cmd.ExecuteNonQueryAsync();
        AppLogger.Info("从站协议数据库初始化完成");
    }

    /// <summary>新增或更新设备配置，返回主表 Id</summary>
    public async Task<int> SaveDeviceConfigAsync(SlaveDeviceConfig config, IEnumerable<ProtocolRow> rows)
    {
        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        await using var tx = conn.BeginTransaction();
        try
        {
            int id;
            if (config.Id <= 0)
            {
                const string ins =
                    "INSERT INTO SlaveDeviceConfigs " +
                    "    (Name, Protocol, Host, Port, PortName, BaudRate, SlaveId, PollIntervalMs) " +
                    "OUTPUT INSERTED.Id " +
                    "VALUES (@name, @proto, @host, @port, @portName, @baud, @slaveId, @pollMs)";
                await using var ins_cmd = new SqlCommand(ins, conn, tx);
                ins_cmd.Parameters.AddWithValue("@name",    config.Name);
                ins_cmd.Parameters.AddWithValue("@proto",   config.Protocol);
                ins_cmd.Parameters.AddWithValue("@host",    config.Host);
                ins_cmd.Parameters.AddWithValue("@port",    config.Port);
                ins_cmd.Parameters.AddWithValue("@portName", config.PortName);
                ins_cmd.Parameters.AddWithValue("@baud",    config.BaudRate);
                ins_cmd.Parameters.AddWithValue("@slaveId", config.SlaveId);
                ins_cmd.Parameters.AddWithValue("@pollMs",  config.PollIntervalMs);
                id = (int)(await ins_cmd.ExecuteScalarAsync())!;
            }
            else
            {
                id = config.Id;
                const string upd =
                    "UPDATE SlaveDeviceConfigs " +
                    "SET Name=@name, Protocol=@proto, Host=@host, Port=@port, " +
                    "    PortName=@portName, BaudRate=@baud, SlaveId=@slaveId, PollIntervalMs=@pollMs " +
                    "WHERE Id=@id";
                await using var upd_cmd = new SqlCommand(upd, conn, tx);
                upd_cmd.Parameters.AddWithValue("@id",      id);
                upd_cmd.Parameters.AddWithValue("@name",    config.Name);
                upd_cmd.Parameters.AddWithValue("@proto",   config.Protocol);
                upd_cmd.Parameters.AddWithValue("@host",    config.Host);
                upd_cmd.Parameters.AddWithValue("@port",    config.Port);
                upd_cmd.Parameters.AddWithValue("@portName", config.PortName);
                upd_cmd.Parameters.AddWithValue("@baud",    config.BaudRate);
                upd_cmd.Parameters.AddWithValue("@slaveId", config.SlaveId);
                upd_cmd.Parameters.AddWithValue("@pollMs",  config.PollIntervalMs);
                await upd_cmd.ExecuteNonQueryAsync();

                const string del = "DELETE FROM SlaveDeviceConfigRows WHERE DeviceConfigId=@id";
                await using var del_cmd = new SqlCommand(del, conn, tx);
                del_cmd.Parameters.AddWithValue("@id", id);
                await del_cmd.ExecuteNonQueryAsync();
            }

            int order = 0;
            const string insRow =
                "INSERT INTO SlaveDeviceConfigRows " +
                "    (DeviceConfigId, SortOrder, Address, ChineseName, EnglishName, ReadWrite, Range, Unit, Note, CurrentValue) " +
                "VALUES (@cfgId, @so, @addr, @cn, @en, @rw, @range, @unit, @note, 0)";
            foreach (var (cn, en, addr, rw, range, unit, note) in rows)
            {
                await using var row_cmd = new SqlCommand(insRow, conn, tx);
                row_cmd.Parameters.AddWithValue("@cfgId", id);
                row_cmd.Parameters.AddWithValue("@so",    order++);
                row_cmd.Parameters.AddWithValue("@addr",  addr);
                row_cmd.Parameters.AddWithValue("@cn",    cn);
                row_cmd.Parameters.AddWithValue("@en",    en);
                row_cmd.Parameters.AddWithValue("@rw",    rw);
                row_cmd.Parameters.AddWithValue("@range", range);
                row_cmd.Parameters.AddWithValue("@unit",  unit);
                row_cmd.Parameters.AddWithValue("@note",  note);
                await row_cmd.ExecuteNonQueryAsync();
            }
            await tx.CommitAsync();
            return id;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    public async Task DeleteDeviceConfigAsync(int id)
    {
        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand("DELETE FROM SlaveDeviceConfigs WHERE Id=@id", conn);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
        AppLogger.Info($"协议设备配置已从数据库删除：Id={id}");
    }

    public async Task UpdateDeviceNameAsync(int id, string name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (id <= 0 || string.IsNullOrWhiteSpace(trimmed))
            return;

        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand("UPDATE SlaveDeviceConfigs SET Name=@name WHERE Id=@id", conn);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@name", trimmed);
        await cmd.ExecuteNonQueryAsync();
        AppLogger.Info($"协议设备名称已更新：Id={id}, Name={trimmed}");
    }

    public async Task<List<(SlaveDeviceConfig Config, List<ProtocolRow> Rows, Dictionary<int, ushort> CurrentValues)>> GetAllDeviceConfigsAsync()
    {
        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();

        var configs = new List<SlaveDeviceConfig>();
        const string sqlCfg =
            "SELECT Id, Name, Protocol, Host, Port, PortName, BaudRate, SlaveId, PollIntervalMs, CreatedAt " +
            "FROM SlaveDeviceConfigs " +
            "ORDER BY Id";
        await using (var cmd = new SqlCommand(sqlCfg, conn))
        await using (var rdr = await cmd.ExecuteReaderAsync())
        {
            while (await rdr.ReadAsync())
            {
                configs.Add(new SlaveDeviceConfig
                {
                    Id             = rdr.GetInt32(0),
                    Name           = rdr.GetString(1),
                    Protocol       = rdr.GetByte(2),
                    Host           = rdr.GetString(3),
                    Port           = rdr.GetInt32(4),
                    PortName       = rdr.GetString(5),
                    BaudRate       = rdr.GetInt32(6),
                    SlaveId        = rdr.GetByte(7),
                    PollIntervalMs = rdr.GetInt32(8),
                    CreatedAt      = rdr.GetDateTime(9),
                });
            }
        }

        var rowDict   = new Dictionary<int, List<ProtocolRow>>();
        var valueDict = new Dictionary<int, Dictionary<int, ushort>>();
        const string sqlRows =
            "SELECT DeviceConfigId, ChineseName, EnglishName, Address, ReadWrite, Range, Unit, Note, CurrentValue " +
            "FROM SlaveDeviceConfigRows " +
            "ORDER BY DeviceConfigId, SortOrder";
        await using (var cmd = new SqlCommand(sqlRows, conn))
        await using (var rdr = await cmd.ExecuteReaderAsync())
        {
            while (await rdr.ReadAsync())
            {
                var cfgId  = rdr.GetInt32(0);
                var row    = ((string)rdr[1], (string)rdr[2], (int)rdr[3],
                              (string)rdr[4], (string)rdr[5], (string)rdr[6], (string)rdr[7]);
                var curVal = (ushort)(int)rdr[8];
                if (!rowDict.ContainsKey(cfgId))   rowDict[cfgId]   = new();
                if (!valueDict.ContainsKey(cfgId)) valueDict[cfgId] = new();
                rowDict[cfgId].Add(row);
                valueDict[cfgId][(int)rdr[3]] = curVal;
            }
        }

        return configs
            .Select(c => (
                c,
                rowDict.GetValueOrDefault(c.Id)   ?? new List<ProtocolRow>(),
                valueDict.GetValueOrDefault(c.Id) ?? new Dictionary<int, ushort>()
            ))
            .ToList();
    }

    /// <summary>实时更新单行当前寄存器值（用户写入后立即调用）</summary>
    public async Task UpdateRowCurrentValueAsync(int configId, int address, ushort value)
    {
        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        const string sql =
            "UPDATE SlaveDeviceConfigRows SET CurrentValue=@val " +
            "WHERE DeviceConfigId=@cfgId AND Address=@addr";
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@val",   (int)value);
        cmd.Parameters.AddWithValue("@cfgId", configId);
        cmd.Parameters.AddWithValue("@addr",  address);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>删除单条寄存器行</summary>
    public async Task DeleteRowAsync(int configId, int address)
    {
        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        const string sql =
            "DELETE FROM SlaveDeviceConfigRows WHERE DeviceConfigId=@cfgId AND Address=@addr";
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@cfgId", configId);
        cmd.Parameters.AddWithValue("@addr",  address);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>插入新寄存器行</summary>
    public async Task InsertRowAsync(int configId, int sortOrder, string chineseName, string englishName,
                                     int address, string readWrite, string range, string unit, string note)
    {
        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        const string sql =
            "INSERT INTO SlaveDeviceConfigRows " +
            "    (DeviceConfigId, SortOrder, Address, ChineseName, EnglishName, ReadWrite, Range, Unit, Note, CurrentValue) " +
            "VALUES (@cfgId, @so, @addr, @cn, @en, @rw, @range, @unit, @note, 0)";
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@cfgId", configId);
        cmd.Parameters.AddWithValue("@so",    sortOrder);
        cmd.Parameters.AddWithValue("@addr",  address);
        cmd.Parameters.AddWithValue("@cn",    chineseName);
        cmd.Parameters.AddWithValue("@en",    englishName);
        cmd.Parameters.AddWithValue("@rw",    readWrite);
        cmd.Parameters.AddWithValue("@range", range);
        cmd.Parameters.AddWithValue("@unit",  unit);
        cmd.Parameters.AddWithValue("@note",  note);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>更新行的中英文名称（用户内联编辑后调用）</summary>
    public async Task UpdateRowMetadataAsync(int configId, int address, string chineseName, string englishName)
    {
        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        const string sql =
            "UPDATE SlaveDeviceConfigRows SET ChineseName=@cn, EnglishName=@en " +
            "WHERE DeviceConfigId=@cfgId AND Address=@addr";
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@cn",    chineseName);
        cmd.Parameters.AddWithValue("@en",    englishName);
        cmd.Parameters.AddWithValue("@cfgId", configId);
        cmd.Parameters.AddWithValue("@addr",  address);
        await cmd.ExecuteNonQueryAsync();
    }
}
