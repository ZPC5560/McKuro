using Microsoft.Data.Sqlite;
using McKuro.Core.Infrastructure;
using McKuro.Core.Models.Gacha;

namespace McKuro.Core.Services.Gacha;

/// <summary>
/// 抽卡记录的本地 SQLite 存储与合并。
/// </summary>
public sealed class GachaRecordStore
{
    private readonly AppDatabase _db;

    public GachaRecordStore(AppDatabase db)
    {
        _db = db;
    }

    /// <summary>
    /// 批量写入记录(无去重:修复10连同秒UP角色丢记录的问题)。
    /// 调用方应在写入前按池删除旧记录,避免重复累积。
    /// </summary>
    public int InsertRecords(string playerId, IEnumerable<GachaRecord> records)
    {
        int inserted = 0;
        using var tx = _db.Connection.BeginTransaction();
        using var cmd = _db.Connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            """
            INSERT INTO gacha_records
                (player_id, card_pool_type, resource_id, quality_level, resource_type, name, count, time)
            VALUES ($playerId, $pool, $resourceId, $quality, $resourceType, $name, $count, $time)
            """;
        var pPlayer = cmd.Parameters.Add("$playerId", SqliteType.Text);
        var pPool = cmd.Parameters.Add("$pool", SqliteType.Integer);
        var pResource = cmd.Parameters.Add("$resourceId", SqliteType.Integer);
        var pQuality = cmd.Parameters.Add("$quality", SqliteType.Integer);
        var pType = cmd.Parameters.Add("$resourceType", SqliteType.Text);
        var pName = cmd.Parameters.Add("$name", SqliteType.Text);
        var pCount = cmd.Parameters.Add("$count", SqliteType.Integer);
        var pTime = cmd.Parameters.Add("$time", SqliteType.Text);

        foreach (var r in records)
        {
            pPlayer.Value = playerId;
            pPool.Value = (int)r.PoolType;
            pResource.Value = r.ResourceId;
            pQuality.Value = r.QualityLevel;
            pType.Value = r.ResourceType;
            pName.Value = r.Name;
            pCount.Value = r.Count;
            pTime.Value = r.Time;
            inserted += cmd.ExecuteNonQuery();
        }

        tx.Commit();

        // 更新玩家最近同步时间
        using var upsert = _db.Connection.CreateCommand();
        upsert.CommandText =
            """
            INSERT INTO players(player_id, player_name, last_sync_time)
            VALUES ($playerId, '', $time)
            ON CONFLICT(player_id) DO UPDATE SET last_sync_time = $time
            """;
        upsert.Parameters.AddWithValue("$playerId", playerId);
        upsert.Parameters.AddWithValue("$time", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        upsert.ExecuteNonQuery();

        return inserted;
    }

    /// <summary>删除某玩家某卡池类型的全部记录(同步前按池清空再重写)。</summary>
    public void DeletePlayerPool(string playerId, CardPoolType poolType)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText =
            "DELETE FROM gacha_records WHERE player_id = $playerId AND card_pool_type = $pool";
        cmd.Parameters.AddWithValue("$playerId", playerId);
        cmd.Parameters.AddWithValue("$pool", (int)poolType);
        cmd.ExecuteNonQuery();
    }

    /// <summary>读取某玩家的全部记录(按时间从旧→新;同秒内按 id 倒序还原真实抽取顺序,因 API 按新→旧返回)。</summary>
    public List<GachaRecord> GetRecords(string playerId, CardPoolType? poolType = null)
    {
        var list = new List<GachaRecord>();
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText =
            """
            SELECT card_pool_type, resource_id, quality_level, resource_type, name, count, time
            FROM gacha_records
            WHERE player_id = $playerId
            """ + (poolType is null ? "" : " AND card_pool_type = $pool") +
            " ORDER BY time ASC, id DESC";
        cmd.Parameters.AddWithValue("$playerId", playerId);
        if (poolType is not null)
        {
            cmd.Parameters.AddWithValue("$pool", (int)poolType.Value);
        }

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new GachaRecord
            {
                PlayerId = playerId,
                CardPoolType = CardPoolTypeValues.GetDisplayName((CardPoolType)reader.GetInt32(0)),
                ResourceId = reader.GetInt32(1),
                QualityLevel = reader.GetInt32(2),
                ResourceType = reader.GetString(3),
                Name = reader.GetString(4),
                Count = reader.GetInt32(5),
                Time = reader.GetString(6),
            });
        }
        return list;
    }

    /// <summary>读取所有玩家的全部记录(按时间从旧→新;同秒内按 id 倒序还原真实抽取顺序)。</summary>
    public List<GachaRecord> GetAllRecords()
    {
        var list = new List<GachaRecord>();
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText =
            """
            SELECT player_id, card_pool_type, resource_id, quality_level, resource_type, name, count, time
            FROM gacha_records
            ORDER BY time ASC, id DESC
            """;

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new GachaRecord
            {
                PlayerId = reader.GetString(0),
                CardPoolType = CardPoolTypeValues.GetDisplayName((CardPoolType)reader.GetInt32(1)),
                ResourceId = reader.GetInt32(2),
                QualityLevel = reader.GetInt32(3),
                ResourceType = reader.GetString(4),
                Name = reader.GetString(5),
                Count = reader.GetInt32(6),
                Time = reader.GetString(7),
            });
        }
        return list;
    }

    /// <summary>所有有抽卡记录的玩家 ID。</summary>
    public List<string> GetAllPlayerIds()
    {
        var list = new List<string>();
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT player_id FROM gacha_records ORDER BY player_id";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(reader.GetString(0));
        }
        return list;
    }

    /// <summary>删除某玩家的抽卡记录。</summary>
    public int DeletePlayer(string playerId)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "DELETE FROM gacha_records WHERE player_id = $playerId";
        cmd.Parameters.AddWithValue("$playerId", playerId);
        return cmd.ExecuteNonQuery();
    }
}
