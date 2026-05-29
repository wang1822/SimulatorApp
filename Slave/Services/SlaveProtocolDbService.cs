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
    Task<bool> DeviceNameExistsAsync(string name, int excludeId = 0);
    Task UpdateDeviceNameAsync(int id, string name);
    Task<List<SlaveListenerEnvironment>> GetListenerEnvironmentsAsync();
    Task<int> SaveListenerEnvironmentAsync(string name, IEnumerable<SlaveListenerEnvironmentItem> items);
    Task DeleteListenerEnvironmentAsync(int id);
    Task<List<(SlaveDeviceConfig Config, List<ProtocolRow> Rows, Dictionary<int, ushort> CurrentValues, Dictionary<int, bool> VerifiedValues)>> GetAllDeviceConfigsAsync();
    Task UpdateRowCurrentValueAsync(int configId, int address, ushort value);
    Task UpdateRowIsVerifiedAsync(int configId, int address, bool isVerified);
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
            "    Protocol       TINYINT       NULL,\r\n" +
            "    Host           NVARCHAR(100) NULL,\r\n" +
            "    Port           INT           NULL,\r\n" +
            "    PortName       NVARCHAR(50)  NULL,\r\n" +
            "    BaudRate       INT           NULL,\r\n" +
            "    SlaveId        TINYINT       NULL,\r\n" +
            "    PollIntervalMs INT           NULL,\r\n" +
            "    CreatedAt      DATETIME2     NOT NULL DEFAULT GETDATE()\r\n" +
            ");\r\n\r\n" +
            "DECLARE @sql NVARCHAR(MAX) = N'';\r\n" +
            "SELECT @sql = @sql + N'ALTER TABLE SlaveDeviceConfigs DROP CONSTRAINT [' + dc.name + N'];'\r\n" +
            "FROM sys.default_constraints dc\r\n" +
            "JOIN sys.columns c ON c.default_object_id = dc.object_id\r\n" +
            "WHERE dc.parent_object_id = OBJECT_ID('SlaveDeviceConfigs')\r\n" +
            "  AND c.name IN ('Protocol','Host','Port','PortName','BaudRate','SlaveId','PollIntervalMs');\r\n" +
            "IF LEN(@sql) > 0 EXEC sp_executesql @sql;\r\n" +
            "IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('SlaveDeviceConfigs') AND name='Protocol' AND is_nullable=0)\r\n" +
            "    ALTER TABLE SlaveDeviceConfigs ALTER COLUMN Protocol TINYINT NULL;\r\n" +
            "IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('SlaveDeviceConfigs') AND name='Host' AND is_nullable=0)\r\n" +
            "    ALTER TABLE SlaveDeviceConfigs ALTER COLUMN Host NVARCHAR(100) NULL;\r\n" +
            "IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('SlaveDeviceConfigs') AND name='Port' AND is_nullable=0)\r\n" +
            "    ALTER TABLE SlaveDeviceConfigs ALTER COLUMN Port INT NULL;\r\n" +
            "IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('SlaveDeviceConfigs') AND name='PortName' AND is_nullable=0)\r\n" +
            "    ALTER TABLE SlaveDeviceConfigs ALTER COLUMN PortName NVARCHAR(50) NULL;\r\n" +
            "IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('SlaveDeviceConfigs') AND name='BaudRate' AND is_nullable=0)\r\n" +
            "    ALTER TABLE SlaveDeviceConfigs ALTER COLUMN BaudRate INT NULL;\r\n" +
            "IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('SlaveDeviceConfigs') AND name='SlaveId' AND is_nullable=0)\r\n" +
            "    ALTER TABLE SlaveDeviceConfigs ALTER COLUMN SlaveId TINYINT NULL;\r\n" +
            "IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('SlaveDeviceConfigs') AND name='PollIntervalMs' AND is_nullable=0)\r\n" +
            "    ALTER TABLE SlaveDeviceConfigs ALTER COLUMN PollIntervalMs INT NULL;\r\n\r\n" +
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
            "    ALTER TABLE SlaveDeviceConfigRows ADD CurrentValue INT NOT NULL DEFAULT 0;\r\n" +
            "IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('SlaveDeviceConfigRows') AND name='IsVerified')\r\n" +
            "    ALTER TABLE SlaveDeviceConfigRows ADD IsVerified BIT NOT NULL DEFAULT 0;\r\n\r\n" +
            "IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE name='SlaveListenerEnvironments' AND type='U')\r\n" +
            "CREATE TABLE SlaveListenerEnvironments (\r\n" +
            "    Id        INT IDENTITY(1,1) PRIMARY KEY,\r\n" +
            "    Name      NVARCHAR(200) NOT NULL,\r\n" +
            "    CreatedAt DATETIME2     NOT NULL DEFAULT GETDATE(),\r\n" +
            "    UpdatedAt DATETIME2     NOT NULL DEFAULT GETDATE()\r\n" +
            ");\r\n\r\n" +
            "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='UX_SlaveListenerEnvironments_Name' AND object_id=OBJECT_ID('SlaveListenerEnvironments'))\r\n" +
            "    CREATE UNIQUE INDEX UX_SlaveListenerEnvironments_Name ON SlaveListenerEnvironments(Name);\r\n\r\n" +
            "IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE name='SlaveListenerEnvironmentItems' AND type='U')\r\n" +
            "CREATE TABLE SlaveListenerEnvironmentItems (\r\n" +
            "    Id              INT IDENTITY(1,1) PRIMARY KEY,\r\n" +
            "    EnvironmentId   INT           NOT NULL,\r\n" +
            "    SortOrder       INT           NOT NULL DEFAULT 0,\r\n" +
            "    IsEnabled       BIT           NOT NULL DEFAULT 1,\r\n" +
            "    BoundDeviceKey  NVARCHAR(100) NULL,\r\n" +
            "    ListenerDbId    INT           NOT NULL DEFAULT 0,\r\n" +
            "    Protocol        TINYINT       NOT NULL DEFAULT 0,\r\n" +
            "    ListenAddress   NVARCHAR(100) NOT NULL DEFAULT '0.0.0.0',\r\n" +
            "    Port            INT           NOT NULL DEFAULT 502,\r\n" +
            "    ComPort         NVARCHAR(50)  NOT NULL DEFAULT '',\r\n" +
            "    BaudRate        INT           NOT NULL DEFAULT 9600,\r\n" +
            "    SlaveId         TINYINT       NOT NULL DEFAULT 1,\r\n" +
            "    FunctionCode    TINYINT       NOT NULL DEFAULT 3,\r\n" +
            "    FOREIGN KEY (EnvironmentId) REFERENCES SlaveListenerEnvironments(Id) ON DELETE CASCADE\r\n" +
            ");\r\n" +
            "IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('SlaveListenerEnvironmentItems') AND name='FunctionCode')\r\n" +
            "    ALTER TABLE SlaveListenerEnvironmentItems ADD FunctionCode TINYINT NOT NULL DEFAULT 3;\r\n" +
            "IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('SlaveListenerEnvironmentItems') AND name='ResponseDelayMs')\r\n" +
            "BEGIN\r\n" +
            "    DECLARE @dfName NVARCHAR(128);\r\n" +
            "    SELECT @dfName = dc.name\r\n" +
            "    FROM sys.default_constraints dc\r\n" +
            "    INNER JOIN sys.columns c ON c.default_object_id = dc.object_id\r\n" +
            "    WHERE dc.parent_object_id = OBJECT_ID('SlaveListenerEnvironmentItems') AND c.name = 'ResponseDelayMs';\r\n" +
            "    IF @dfName IS NOT NULL EXEC('ALTER TABLE SlaveListenerEnvironmentItems DROP CONSTRAINT [' + @dfName + ']');\r\n" +
            "    ALTER TABLE SlaveListenerEnvironmentItems DROP COLUMN ResponseDelayMs;\r\n" +
            "END;";
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
                    "INSERT INTO SlaveDeviceConfigs (Name) " +
                    "OUTPUT INSERTED.Id " +
                    "VALUES (@name)";
                await using var ins_cmd = new SqlCommand(ins, conn, tx);
                ins_cmd.Parameters.AddWithValue("@name",    config.Name);
                id = (int)(await ins_cmd.ExecuteScalarAsync())!;
            }
            else
            {
                id = config.Id;
                const string upd =
                    "UPDATE SlaveDeviceConfigs SET Name=@name WHERE Id=@id";
                await using var upd_cmd = new SqlCommand(upd, conn, tx);
                upd_cmd.Parameters.AddWithValue("@id",      id);
                upd_cmd.Parameters.AddWithValue("@name",    config.Name);
                await upd_cmd.ExecuteNonQueryAsync();

                const string del = "DELETE FROM SlaveDeviceConfigRows WHERE DeviceConfigId=@id";
                await using var del_cmd = new SqlCommand(del, conn, tx);
                del_cmd.Parameters.AddWithValue("@id", id);
                await del_cmd.ExecuteNonQueryAsync();
            }

            int order = 0;
            const string insRow =
                "INSERT INTO SlaveDeviceConfigRows " +
                "    (DeviceConfigId, SortOrder, Address, ChineseName, EnglishName, ReadWrite, Range, Unit, Note, CurrentValue, IsVerified) " +
                "VALUES (@cfgId, @so, @addr, @cn, @en, @rw, @range, @unit, @note, 0, 0)";
            foreach (var (cn, en, addr, rw, range, unit, note) in rows.OrderBy(r => r.Address))
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

    public async Task<bool> DeviceNameExistsAsync(string name, int excludeId = 0)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return false;

        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(
            "SELECT COUNT(1) FROM SlaveDeviceConfigs WHERE Name=@name AND (@excludeId <= 0 OR Id<>@excludeId)",
            conn);
        cmd.Parameters.AddWithValue("@name", trimmed);
        cmd.Parameters.AddWithValue("@excludeId", excludeId);
        var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        return count > 0;
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

    public async Task<List<SlaveListenerEnvironment>> GetListenerEnvironmentsAsync()
    {
        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();

        var environments = new List<SlaveListenerEnvironment>();
        var byId = new Dictionary<int, SlaveListenerEnvironment>();
        const string sqlEnv =
            "SELECT Id, Name, CreatedAt, UpdatedAt " +
            "FROM SlaveListenerEnvironments " +
            "ORDER BY UpdatedAt DESC, Name";
        await using (var cmd = new SqlCommand(sqlEnv, conn))
        await using (var rdr = await cmd.ExecuteReaderAsync())
        {
            while (await rdr.ReadAsync())
            {
                var env = new SlaveListenerEnvironment
                {
                    Id = rdr.GetInt32(0),
                    Name = rdr.GetString(1),
                    CreatedAt = rdr.GetDateTime(2),
                    UpdatedAt = rdr.GetDateTime(3)
                };
                environments.Add(env);
                byId[env.Id] = env;
            }
        }

        if (environments.Count == 0)
            return environments;

        const string sqlItems =
            "SELECT EnvironmentId, SortOrder, IsEnabled, BoundDeviceKey, ListenerDbId, Protocol, ListenAddress, Port, ComPort, BaudRate, SlaveId, FunctionCode " +
            "FROM SlaveListenerEnvironmentItems " +
            "ORDER BY EnvironmentId, SortOrder, Id";
        await using (var cmd = new SqlCommand(sqlItems, conn))
        await using (var rdr = await cmd.ExecuteReaderAsync())
        {
            while (await rdr.ReadAsync())
            {
                var envId = rdr.GetInt32(0);
                if (!byId.TryGetValue(envId, out var env))
                    continue;

                env.Items.Add(new SlaveListenerEnvironmentItem
                {
                    SortOrder = rdr.GetInt32(1),
                    IsEnabled = rdr.GetBoolean(2),
                    BoundDeviceKey = rdr.IsDBNull(3) ? null : rdr.GetString(3),
                    ListenerDbId = rdr.GetInt32(4),
                    Protocol = rdr.GetByte(5),
                    ListenAddress = rdr.GetString(6),
                    Port = rdr.GetInt32(7),
                    ComPort = rdr.GetString(8),
                    BaudRate = rdr.GetInt32(9),
                    SlaveId = rdr.GetByte(10),
                    FunctionCode = rdr.GetByte(11)
                });
            }
        }

        return environments;
    }

    public async Task<int> SaveListenerEnvironmentAsync(string name, IEnumerable<SlaveListenerEnvironmentItem> items)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            throw new ArgumentException("监听环境名称不能为空。", nameof(name));

        var snapshot = items
            .OrderBy(i => i.SortOrder)
            .ToList();

        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        await using var tx = conn.BeginTransaction();
        try
        {
            int id;
            const string find = "SELECT Id FROM SlaveListenerEnvironments WHERE Name=@name";
            await using (var findCmd = new SqlCommand(find, conn, tx))
            {
                findCmd.Parameters.AddWithValue("@name", trimmed);
                var existingId = await findCmd.ExecuteScalarAsync();
                id = existingId == null || existingId == DBNull.Value ? 0 : Convert.ToInt32(existingId);
            }

            if (id <= 0)
            {
                const string insertEnv =
                    "INSERT INTO SlaveListenerEnvironments (Name, CreatedAt, UpdatedAt) " +
                    "OUTPUT INSERTED.Id VALUES (@name, GETDATE(), GETDATE())";
                await using var insertCmd = new SqlCommand(insertEnv, conn, tx);
                insertCmd.Parameters.AddWithValue("@name", trimmed);
                id = Convert.ToInt32(await insertCmd.ExecuteScalarAsync());
            }
            else
            {
                const string updateEnv =
                    "UPDATE SlaveListenerEnvironments SET UpdatedAt=GETDATE() WHERE Id=@id";
                await using var updateCmd = new SqlCommand(updateEnv, conn, tx);
                updateCmd.Parameters.AddWithValue("@id", id);
                await updateCmd.ExecuteNonQueryAsync();

                const string deleteOld = "DELETE FROM SlaveListenerEnvironmentItems WHERE EnvironmentId=@id";
                await using var deleteCmd = new SqlCommand(deleteOld, conn, tx);
                deleteCmd.Parameters.AddWithValue("@id", id);
                await deleteCmd.ExecuteNonQueryAsync();
            }

            const string insertItem =
                "INSERT INTO SlaveListenerEnvironmentItems " +
                "    (EnvironmentId, SortOrder, IsEnabled, BoundDeviceKey, ListenerDbId, Protocol, ListenAddress, Port, ComPort, BaudRate, SlaveId, FunctionCode) " +
                "VALUES (@envId, @sortOrder, @isEnabled, @boundDeviceKey, @listenerDbId, @protocol, @listenAddress, @port, @comPort, @baudRate, @slaveId, @functionCode)";
            foreach (var item in snapshot)
            {
                await using var itemCmd = new SqlCommand(insertItem, conn, tx);
                itemCmd.Parameters.AddWithValue("@envId", id);
                itemCmd.Parameters.AddWithValue("@sortOrder", item.SortOrder);
                itemCmd.Parameters.AddWithValue("@isEnabled", item.IsEnabled);
                itemCmd.Parameters.AddWithValue("@boundDeviceKey", string.IsNullOrWhiteSpace(item.BoundDeviceKey) ? DBNull.Value : item.BoundDeviceKey);
                itemCmd.Parameters.AddWithValue("@listenerDbId", item.ListenerDbId);
                itemCmd.Parameters.AddWithValue("@protocol", (byte)item.Protocol);
                itemCmd.Parameters.AddWithValue("@listenAddress", item.ListenAddress);
                itemCmd.Parameters.AddWithValue("@port", item.Port);
                itemCmd.Parameters.AddWithValue("@comPort", item.ComPort);
                itemCmd.Parameters.AddWithValue("@baudRate", item.BaudRate);
                itemCmd.Parameters.AddWithValue("@slaveId", item.SlaveId);
                itemCmd.Parameters.AddWithValue("@functionCode", (byte)NormalizeFunctionCode(item.FunctionCode));
                await itemCmd.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();
            AppLogger.Info($"监听环境已保存：Id={id}, Name={trimmed}, Items={snapshot.Count}");
            return id;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    public async Task DeleteListenerEnvironmentAsync(int id)
    {
        if (id <= 0) return;

        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand("DELETE FROM SlaveListenerEnvironments WHERE Id=@id", conn);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
        AppLogger.Info($"监听环境已删除：Id={id}");
    }

    private static int NormalizeFunctionCode(int functionCode)
        => functionCode is 1 or 2 or 3 or 4 ? functionCode : 3;

    public async Task<List<(SlaveDeviceConfig Config, List<ProtocolRow> Rows, Dictionary<int, ushort> CurrentValues, Dictionary<int, bool> VerifiedValues)>> GetAllDeviceConfigsAsync()
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
                    Protocol       = rdr.IsDBNull(2) ? 0 : rdr.GetByte(2),
                    Host           = rdr.IsDBNull(3) ? string.Empty : rdr.GetString(3),
                    Port           = rdr.IsDBNull(4) ? 0 : rdr.GetInt32(4),
                    PortName       = rdr.IsDBNull(5) ? string.Empty : rdr.GetString(5),
                    BaudRate       = rdr.IsDBNull(6) ? 0 : rdr.GetInt32(6),
                    SlaveId        = rdr.IsDBNull(7) ? (byte)0 : rdr.GetByte(7),
                    PollIntervalMs = rdr.IsDBNull(8) ? 0 : rdr.GetInt32(8),
                    CreatedAt      = rdr.GetDateTime(9),
                });
            }
        }

        var rowDict      = new Dictionary<int, List<ProtocolRow>>();
        var valueDict    = new Dictionary<int, Dictionary<int, ushort>>();
        var verifiedDict = new Dictionary<int, Dictionary<int, bool>>();
        const string sqlRows =
            "SELECT DeviceConfigId, ChineseName, EnglishName, Address, ReadWrite, Range, Unit, Note, CurrentValue, IsVerified " +
            "FROM SlaveDeviceConfigRows " +
            "ORDER BY DeviceConfigId, Address, SortOrder";
        await using (var cmd = new SqlCommand(sqlRows, conn))
        await using (var rdr = await cmd.ExecuteReaderAsync())
        {
            while (await rdr.ReadAsync())
            {
                var cfgId    = rdr.GetInt32(0);
                var addr     = (int)rdr[3];
                var row      = ((string)rdr[1], (string)rdr[2], addr,
                                (string)rdr[4], (string)rdr[5], (string)rdr[6], (string)rdr[7]);
                var curVal   = (ushort)(int)rdr[8];
                var verified = rdr.GetBoolean(9);
                if (!rowDict.ContainsKey(cfgId))      rowDict[cfgId]      = new();
                if (!valueDict.ContainsKey(cfgId))    valueDict[cfgId]    = new();
                if (!verifiedDict.ContainsKey(cfgId)) verifiedDict[cfgId] = new();
                rowDict[cfgId].Add(row);
                valueDict[cfgId][addr] = curVal;
                verifiedDict[cfgId][addr] = verified;
            }
        }

        return configs
            .Select(c => (
                c,
                rowDict.GetValueOrDefault(c.Id)      ?? new List<ProtocolRow>(),
                valueDict.GetValueOrDefault(c.Id)    ?? new Dictionary<int, ushort>(),
                verifiedDict.GetValueOrDefault(c.Id) ?? new Dictionary<int, bool>()
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

    /// <summary>实时更新 API 比对通过状态（绿点）。</summary>
    public async Task UpdateRowIsVerifiedAsync(int configId, int address, bool isVerified)
    {
        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        const string sql =
            "UPDATE SlaveDeviceConfigRows SET IsVerified=@val " +
            "WHERE DeviceConfigId=@cfgId AND Address=@addr";
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@val",   isVerified);
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
            "    (DeviceConfigId, SortOrder, Address, ChineseName, EnglishName, ReadWrite, Range, Unit, Note, CurrentValue, IsVerified) " +
            "VALUES (@cfgId, @so, @addr, @cn, @en, @rw, @range, @unit, @note, 0, 0)";
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
