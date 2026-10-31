using Microsoft.Data.Sqlite;
using McKuro.Core.Infrastructure;
using McKuro.Core.Models.Gacha;

namespace McKuro.Core.Services.Gacha;

/// <summary>
/// 抽卡记录的本地 SQLite 存储与合并。
/// <para>
/// 全部数据库访问都经 <see cref="AppDatabase.Execute{T}"/> —— 共享连接不是线程安全的,
/// 而本存储既被 UI 线程(抽卡页/分析)调用、也被后台线程(同步完成后写库)调用。
/// </para>
/// </summary>
public sealed class GachaRecordStore
{
    private readonly AppDatabase _db;

    public GachaRecordStore(AppDatabase db)
    {
        _db = db;
    }

    /// <summary>
    /// 批量写入记录(无去重:修复10连同秒UP角色丢记录的问题)。卡池按每条记录的 <see cref="GachaRecord.PoolType"/> 写入。
    /// 调用方应在写入前按池删除旧记录,避免重复累积;同步场景请用 <see cref="ReplacePlayerPool"/>。
    /// </summary>
    public int InsertRecords(string playerId, IEnumerable<GachaRecord> records)
        => _db.Execute(conn =>
        {
            int inserted;
            using (var tx = conn.BeginTransaction())
            {
                inserted = InsertCore(conn, tx, playerId, records, poolOverride: null);
                tx.Commit();
            }
            UpsertPlayerSyncTime(conn, playerId);
            return inserted;
        });

    /// <summary>
    /// 批量写入记录,全部记到查询用的卡池 <paramref name="poolType"/> 下(不按资源类型重新推导)。
    /// <para>
    /// 角色常驻池(3)会返回 3 星武器;若按 <see cref="GachaRecord.PoolType"/> 推导会写进武器常驻池(4),
    /// 而同步循环到池 4 时又先清空池 4,于是这些记录每次同步都被抹掉。卡池必须以查询时的池号为准。
    /// </para>
    /// </summary>
    public int InsertRecords(string playerId, IEnumerable<GachaRecord> records, CardPoolType poolType)
        => _db.Execute(conn =>
        {
            int inserted;
            using (var tx = conn.BeginTransaction())
            {
                inserted = InsertCore(conn, tx, playerId, records, poolType);
                tx.Commit();
            }
            UpsertPlayerSyncTime(conn, playerId);
            return inserted;
        });

    /// <summary>
    /// 用一次查询结果覆盖某玩家某卡池:删除旧记录 + 写入新记录在<b>同一事务</b>内完成,
    /// 中途失败整体回滚,不会留下被清空的卡池。<paramref name="records"/> 为空时等于清空该池。
    /// </summary>
    /// <returns>写入的行数。</returns>
    public int ReplacePlayerPool(string playerId, CardPoolType poolType, IReadOnlyCollection<GachaRecord> records)
        => _db.Execute(conn =>
        {
            int inserted;
            using (var tx = conn.BeginTransaction())
            {
                using (var del = conn.CreateCommand())
                {
                    del.Transaction = tx;
                    del.CommandText =
                        "DELETE FROM gacha_records WHERE player_id = $playerId AND card_pool_type = $pool";
                    del.Parameters.AddWithValue("$playerId", playerId);
                    del.Parameters.AddWithValue("$pool", (int)poolType);
                    del.ExecuteNonQuery();
                }

                inserted = records.Count > 0 ? InsertCore(conn, tx, playerId, records, poolType) : 0;
                tx.Commit();
            }

            if (records.Count > 0)
            {
                UpsertPlayerSyncTime(conn, playerId);
            }
            return inserted;
        });

    private static int InsertCore(
        SqliteConnection conn,
        SqliteTransaction tx,
        string playerId,
        IEnumerable<GachaRecord> records,
        CardPoolType? poolOverride)
    {
        int inserted = 0;
        using var cmd = conn.CreateCommand();
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
            pPool.Value = (int)(poolOverride ?? r.PoolType);
            pResource.Value = r.ResourceId;
            pQuality.Value = r.QualityLevel;
            pType.Value = r.ResourceType;
            pName.Value = r.Name;
            pCount.Value = r.Count;
            pTime.Value = r.Time;
            inserted += cmd.ExecuteNonQuery();
        }
        return inserted;
    }

    /// <summary>更新玩家最近同步时间。</summary>
    private static void UpsertPlayerSyncTime(SqliteConnection conn, string playerId)
    {
        using var upsert = conn.CreateCommand();
        upsert.CommandText =
            """
            INSERT INTO players(player_id, player_name, last_sync_time)
            VALUES ($playerId, '', $time)
            ON CONFLICT(player_id) DO UPDATE SET last_sync_time = $time
            """;
        upsert.Parameters.AddWithValue("$playerId", playerId);
        upsert.Parameters.AddWithValue("$time", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        upsert.ExecuteNonQuery();
    }

    /// <summary>删除某玩家某卡池类型的全部记录(同步前按池清空再重写)。</summary>
    public void DeletePlayerPool(string playerId, CardPoolType poolType)
        => _db.Execute(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "DELETE FROM gacha_records WHERE player_id = $playerId AND card_pool_type = $pool";
            cmd.Parameters.AddWithValue("$playerId", playerId);
            cmd.Parameters.AddWithValue("$pool", (int)poolType);
            cmd.ExecuteNonQuery();
        });

    /// <summary>某玩家的记录总数(同步前后求差得出新增数)。</summary>
    public int CountRecords(string playerId)
        => _db.Execute(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM gacha_records WHERE player_id = $playerId";
            cmd.Parameters.AddWithValue("$playerId", playerId);
            return Convert.ToInt32(cmd.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
        });

    /// <summary>读取某玩家的全部记录(按时间从旧→新;同秒内按 id 倒序还原真实抽取顺序,因 API 按新→旧返回)。</summary>
    public List<GachaRecord> GetRecords(string playerId, CardPoolType? poolType = null)
        => _db.Execute(conn =>
        {
            var list = new List<GachaRecord>();
            using var cmd = conn.CreateCommand();
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
                var pool = (CardPoolType)reader.GetInt32(0);
                list.Add(new GachaRecord
                {
                    PlayerId = playerId,
                    // 显示名只供界面展示;卡池以库里的整数为准(见 StoredPoolType 的说明)
                    CardPoolType = CardPoolTypeValues.GetDisplayName(pool),
                    StoredPoolType = pool,
                    ResourceId = reader.GetInt32(1),
                    QualityLevel = reader.GetInt32(2),
                    ResourceType = reader.GetString(3),
                    Name = reader.GetString(4),
                    Count = reader.GetInt32(5),
                    Time = reader.GetString(6),
                });
            }
            return list;
        });

    /// <summary>读取所有玩家的全部记录(按时间从旧→新;同秒内按 id 倒序还原真实抽取顺序)。</summary>
    public List<GachaRecord> GetAllRecords()
        => _db.Execute(conn =>
        {
            var list = new List<GachaRecord>();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                """
                SELECT player_id, card_pool_type, resource_id, quality_level, resource_type, name, count, time
                FROM gacha_records
                ORDER BY time ASC, id DESC
                """;

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var pool = (CardPoolType)reader.GetInt32(1);
                list.Add(new GachaRecord
                {
                    PlayerId = reader.GetString(0),
                    CardPoolType = CardPoolTypeValues.GetDisplayName(pool),
                    StoredPoolType = pool,
                    ResourceId = reader.GetInt32(2),
                    QualityLevel = reader.GetInt32(3),
                    ResourceType = reader.GetString(4),
                    Name = reader.GetString(5),
                    Count = reader.GetInt32(6),
                    Time = reader.GetString(7),
                });
            }
            return list;
        });

    /// <summary>
    /// 所有有抽卡记录的玩家 ID,按该玩家最近一条抽卡记录的时间从旧到新排序,
    /// 即 <c>[^1]</c> 是最近抽过卡的玩家(调用方据此默认选中"最后同步的玩家")。时间相同按 ID 排。
    /// </summary>
    public List<string> GetAllPlayerIds()
        => _db.Execute(conn =>
        {
            var list = new List<string>();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                """
                SELECT player_id FROM gacha_records
                GROUP BY player_id
                ORDER BY MAX(time) ASC, player_id ASC
                """;
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(reader.GetString(0));
            }
            return list;
        });

    /// <summary>删除某玩家的抽卡记录。</summary>
    public int DeletePlayer(string playerId)
        => _db.Execute(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM gacha_records WHERE player_id = $playerId";
            cmd.Parameters.AddWithValue("$playerId", playerId);
            return cmd.ExecuteNonQuery();
        });
}
