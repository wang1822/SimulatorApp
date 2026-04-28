using Microsoft.Data.SqlClient;
using SimulatorApp.Master.Models;
using SimulatorApp.Shared.Logging;

namespace SimulatorApp.Master.Services;

/// <summary>
/// 主站数据库服务实现（SQL Server + ADO.NET，纯参数化查询，无 ORM）。
/// 连接字符串在构造时传入；InitializeAsync() 自动建表。
/// </summary>
public class MasterDbService : IMasterDbService
{
    private readonly string _cs;
    public MasterDbService(string connectionString) => _cs = connectionString;

    // ────────────────────────────────────────────────────────────────────
    // 初始化：建表（若不存在）
    // ────────────────────────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();

        const string ddl = """
            IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE name='MasterStations' AND type='U')
            CREATE TABLE MasterStations (
                Id             INT IDENTITY(1,1) PRIMARY KEY,
                Name           NVARCHAR(100) NOT NULL,
                Protocol       TINYINT       NOT NULL DEFAULT 0,
                Host           NVARCHAR(100) NOT NULL DEFAULT '127.0.0.1',
                Port           INT           NOT NULL DEFAULT 502,
                PortName       NVARCHAR(50)  NOT NULL DEFAULT 'COM3',
                BaudRate       INT           NOT NULL DEFAULT 9600,
                SlaveId        TINYINT       NOT NULL DEFAULT 1,
                PollIntervalMs INT           NOT NULL DEFAULT 1000,
                CreatedAt      DATETIME2     NOT NULL DEFAULT GETDATE(),
                CONSTRAINT UQ_MasterStationName UNIQUE (Name)
            );

            IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE name='MasterRegisterConfigs' AND type='U')
            CREATE TABLE MasterRegisterConfigs (
                Id               INT IDENTITY(1,1) PRIMARY KEY,
                StationId        INT           NOT NULL,
                StartAddress     INT           NOT NULL,
                Quantity         INT           NOT NULL DEFAULT 1,
                VariableName     NVARCHAR(100) NOT NULL DEFAULT '',
                ChineseName      NVARCHAR(200) NOT NULL DEFAULT '',
                ReadWrite        NVARCHAR(10)  NOT NULL DEFAULT 'R',
                Unit             NVARCHAR(50)  NOT NULL DEFAULT '',
                DataType         NVARCHAR(50)  NOT NULL DEFAULT 'uint16',
                RegisterDataType NVARCHAR(50)  NOT NULL DEFAULT 'uint16',
                ScaleFactor      FLOAT         NOT NULL DEFAULT 1.0,
                Offset           FLOAT         NOT NULL DEFAULT 0.0,
                ValueRange       NVARCHAR(200) NOT NULL DEFAULT '',
                Description      NVARCHAR(500) NOT NULL DEFAULT '',
                Category         TINYINT       NOT NULL DEFAULT 0,
                SortOrder        INT           NOT NULL DEFAULT 0,
                FOREIGN KEY (StationId) REFERENCES MasterStations(Id) ON DELETE CASCADE
            );

            """;

        await using var cmd = new SqlCommand(ddl, conn);
        await cmd.ExecuteNonQueryAsync();

        // 字段迁移：按需补加新列（保证旧库升级兼容）
        const string migrate = """
            IF NOT EXISTS (
                SELECT 1 FROM sys.columns
                WHERE object_id = OBJECT_ID('MasterRegisterConfigs') AND name = 'IsVerified'
            )
                ALTER TABLE MasterRegisterConfigs ADD IsVerified BIT NOT NULL DEFAULT 0;

            IF NOT EXISTS (
                SELECT 1 FROM sys.columns
                WHERE object_id = OBJECT_ID('MasterRegisterConfigs') AND name = 'LastRawRegisters'
            )
            BEGIN
                ALTER TABLE MasterRegisterConfigs ADD LastRawRegisters  NVARCHAR(200) NOT NULL DEFAULT '';
                ALTER TABLE MasterRegisterConfigs ADD LastPhysicalValue NVARCHAR(100) NOT NULL DEFAULT '';
            END
            """;
        await using var migCmd = new SqlCommand(migrate, conn);
        await migCmd.ExecuteNonQueryAsync();

        AppLogger.Info("主站数据库初始化完成");
    }

    // ────────────────────────────────────────────────────────────────────
    // 站点
    // ────────────────────────────────────────────────────────────────────

    public async Task<List<MasterStation>> GetAllStationsAsync()
    {
        var list = new List<MasterStation>();
        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        const string sql =
            "SELECT Id,Name,Protocol,Host,Port,PortName,BaudRate,SlaveId,PollIntervalMs,CreatedAt " +
            "FROM MasterStations ORDER BY Name";
        await using var cmd = new SqlCommand(sql, conn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add(MapStation(r));
        return list;
    }

    public async Task<MasterStation?> GetStationAsync(int id)
    {
        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        const string sql =
            "SELECT Id,Name,Protocol,Host,Port,PortName,BaudRate,SlaveId,PollIntervalMs,CreatedAt " +
            "FROM MasterStations WHERE Id=@Id";
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Id", id);
        await using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? MapStation(r) : null;
    }

    public async Task<int> SaveStationAsync(MasterStation s)
    {
        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        if (s.Id == 0)
        {
            const string sql = """
                INSERT INTO MasterStations
                    (Name,Protocol,Host,Port,PortName,BaudRate,SlaveId,PollIntervalMs)
                OUTPUT INSERTED.Id
                VALUES (@Name,@Protocol,@Host,@Port,@PortName,@BaudRate,@SlaveId,@PollIntervalMs)
                """;
            await using var cmd = new SqlCommand(sql, conn);
            AddStationParams(cmd, s);
            int newId = (int)(await cmd.ExecuteScalarAsync())!;
            s.Id = newId;
            return newId;
        }
        else
        {
            const string sql = """
                UPDATE MasterStations
                SET Name=@Name,Protocol=@Protocol,Host=@Host,Port=@Port,
                    PortName=@PortName,BaudRate=@BaudRate,SlaveId=@SlaveId,PollIntervalMs=@PollIntervalMs
                WHERE Id=@Id
                """;
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Id", s.Id);
            AddStationParams(cmd, s);
            await cmd.ExecuteNonQueryAsync();
            return s.Id;
        }
    }

    public async Task DeleteStationAsync(int id)
    {
        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand("DELETE FROM MasterStations WHERE Id=@Id", conn);
        cmd.Parameters.AddWithValue("@Id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteRegisterConfigAsync(int configId)
    {
        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand("DELETE FROM MasterRegisterConfigs WHERE Id=@id", conn);
        cmd.Parameters.AddWithValue("@id", configId);
        await cmd.ExecuteNonQueryAsync();
    }

    // ────────────────────────────────────────────────────────────────────
    // 寄存器配置
    // ────────────────────────────────────────────────────────────────────

    public async Task<List<MasterRegisterConfig>> GetRegisterConfigsAsync(int stationId)
    {
        var list = new List<MasterRegisterConfig>();
        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();

        const string sql = """
            SELECT Id,StationId,StartAddress,Quantity,VariableName,ChineseName,ReadWrite,
                   Unit,DataType,RegisterDataType,ScaleFactor,Offset,ValueRange,Description,
                   Category,SortOrder,IsVerified,LastRawRegisters,LastPhysicalValue
            FROM MasterRegisterConfigs
            WHERE StationId=@StationId
            ORDER BY SortOrder,StartAddress
            """;
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@StationId", stationId);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add(MapConfig(r));
        return list;
    }

    public async Task SaveRegisterConfigsAsync(int stationId, List<MasterRegisterConfig> configs)
    {
        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();

        // 先删除该站所有配置
        await using (var del = new SqlCommand(
            "DELETE FROM MasterRegisterConfigs WHERE StationId=@StationId", conn))
        {
            del.Parameters.AddWithValue("@StationId", stationId);
            await del.ExecuteNonQueryAsync();
        }

        int sort = 0;
        foreach (var cfg in configs)
        {
            cfg.StationId = stationId;
            cfg.SortOrder = sort++;
            const string ins = """
                INSERT INTO MasterRegisterConfigs
                    (StationId,StartAddress,Quantity,VariableName,ChineseName,ReadWrite,Unit,
                     DataType,RegisterDataType,ScaleFactor,Offset,ValueRange,Description,Category,SortOrder)
                OUTPUT INSERTED.Id
                VALUES (@StationId,@StartAddress,@Quantity,@VariableName,@ChineseName,@ReadWrite,@Unit,
                        @DataType,@RegisterDataType,@ScaleFactor,@Offset,@ValueRange,@Description,@Category,@SortOrder)
                """;
            await using var ins_cmd = new SqlCommand(ins, conn);
            AddConfigParams(ins_cmd, cfg);
            int newId = (int)(await ins_cmd.ExecuteScalarAsync())!;
            cfg.Id = newId;
        }
    }

    // ────────────────────────────────────────────────────────────────────
    // 私有映射辅助
    // ────────────────────────────────────────────────────────────────────

    private static MasterStation MapStation(SqlDataReader r) => new()
    {
        Id             = r.GetInt32(0),
        Name           = r.GetString(1),
        Protocol       = r.GetByte(2),
        Host           = r.GetString(3),
        Port           = r.GetInt32(4),
        PortName       = r.GetString(5),
        BaudRate       = r.GetInt32(6),
        SlaveId        = r.GetByte(7),
        PollIntervalMs = r.GetInt32(8),
        CreatedAt      = r.GetDateTime(9)
    };

    private static MasterRegisterConfig MapConfig(SqlDataReader r) => new()
    {
        Id               = r.GetInt32(0),
        StationId        = r.GetInt32(1),
        StartAddress     = r.GetInt32(2),
        Quantity         = r.GetInt32(3),
        VariableName     = r.GetString(4),
        ChineseName      = r.GetString(5),
        ReadWrite        = r.GetString(6),
        Unit             = r.GetString(7),
        DataType         = r.GetString(8),
        RegisterDataType = r.GetString(9),
        ScaleFactor      = r.GetDouble(10),
        Offset           = r.GetDouble(11),
        ValueRange       = r.GetString(12),
        Description      = r.GetString(13),
        Category         = r.GetByte(14),
        SortOrder        = r.GetInt32(15),
        IsVerified       = r.GetBoolean(16),
        LastRawRegisters  = r.IsDBNull(17) ? string.Empty : r.GetString(17),
        LastPhysicalValue = r.IsDBNull(18) ? string.Empty : r.GetString(18)
    };

    private static void AddStationParams(SqlCommand cmd, MasterStation s)
    {
        cmd.Parameters.AddWithValue("@Name",           s.Name);
        cmd.Parameters.AddWithValue("@Protocol",       s.Protocol);
        cmd.Parameters.AddWithValue("@Host",           s.Host);
        cmd.Parameters.AddWithValue("@Port",           s.Port);
        cmd.Parameters.AddWithValue("@PortName",       s.PortName);
        cmd.Parameters.AddWithValue("@BaudRate",       s.BaudRate);
        cmd.Parameters.AddWithValue("@SlaveId",        s.SlaveId);
        cmd.Parameters.AddWithValue("@PollIntervalMs", s.PollIntervalMs);
    }

    public async Task UpdateRegisterNamesAsync(int configId, string chineseName, string variableName)
    {
        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(
            "UPDATE MasterRegisterConfigs SET ChineseName=@cn, VariableName=@vn WHERE Id=@id", conn);
        cmd.Parameters.AddWithValue("@cn",  chineseName);
        cmd.Parameters.AddWithValue("@vn",  variableName);
        cmd.Parameters.AddWithValue("@id",  configId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task UpdateStationNameAsync(int stationId, string name)
    {
        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(
            "UPDATE MasterStations SET Name=@n WHERE Id=@id", conn);
        cmd.Parameters.AddWithValue("@n",  name);
        cmd.Parameters.AddWithValue("@id", stationId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task UpdateIsVerifiedAsync(int configId, bool isVerified)
    {
        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(
            "UPDATE MasterRegisterConfigs SET IsVerified=@v WHERE Id=@id", conn);
        cmd.Parameters.AddWithValue("@v",  isVerified ? 1 : 0);
        cmd.Parameters.AddWithValue("@id", configId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task UpdateLastWrittenAsync(int configId, string rawRegisters, string physicalValue)
    {
        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(
            "UPDATE MasterRegisterConfigs SET LastRawRegisters=@raw, LastPhysicalValue=@phys WHERE Id=@id", conn);
        cmd.Parameters.AddWithValue("@raw",  rawRegisters);
        cmd.Parameters.AddWithValue("@phys", physicalValue);
        cmd.Parameters.AddWithValue("@id",   configId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task ClearAllIsVerifiedAsync(int stationId)
    {
        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(
            "UPDATE MasterRegisterConfigs SET IsVerified=0 WHERE StationId=@sid", conn);
        cmd.Parameters.AddWithValue("@sid", stationId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<Dictionary<int, bool>> GetIsVerifiedMapAsync(IEnumerable<int> configIds)
    {
        var ids = configIds.ToList();
        var result = new Dictionary<int, bool>();
        if (ids.Count == 0) return result;

        // 构造参数化 IN 列表
        var paramNames = ids.Select((_, i) => $"@p{i}").ToList();
        string sql = $"SELECT Id, IsVerified FROM MasterRegisterConfigs WHERE Id IN ({string.Join(',', paramNames)})";

        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        for (int i = 0; i < ids.Count; i++)
            cmd.Parameters.AddWithValue(paramNames[i], ids[i]);

        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            result[r.GetInt32(0)] = r.GetBoolean(1);
        return result;
    }

    private static void AddConfigParams(SqlCommand cmd, MasterRegisterConfig c)
    {
        cmd.Parameters.AddWithValue("@StationId",        c.StationId);
        cmd.Parameters.AddWithValue("@StartAddress",     c.StartAddress);
        cmd.Parameters.AddWithValue("@Quantity",         c.Quantity);
        cmd.Parameters.AddWithValue("@VariableName",     c.VariableName);
        cmd.Parameters.AddWithValue("@ChineseName",      c.ChineseName);
        cmd.Parameters.AddWithValue("@ReadWrite",        c.ReadWrite);
        cmd.Parameters.AddWithValue("@Unit",             c.Unit);
        cmd.Parameters.AddWithValue("@DataType",         c.DataType);
        cmd.Parameters.AddWithValue("@RegisterDataType", c.RegisterDataType);
        cmd.Parameters.AddWithValue("@ScaleFactor",      c.ScaleFactor);
        cmd.Parameters.AddWithValue("@Offset",           c.Offset);
        cmd.Parameters.AddWithValue("@ValueRange",       c.ValueRange);
        cmd.Parameters.AddWithValue("@Description",      c.Description);
        cmd.Parameters.AddWithValue("@Category",         c.Category);
        cmd.Parameters.AddWithValue("@SortOrder",        c.SortOrder);
    }
}
