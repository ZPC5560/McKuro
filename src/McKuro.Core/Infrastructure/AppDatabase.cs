using Microsoft.Data.Sqlite;

namespace McKuro.Core.Infrastructure;

/// <summary>
/// 本地 SQLite 数据库(玩家抽卡记录、角色缓存等)。
/// 数据库文件位于应用数据目录。
/// </summary>
public sealed class AppDatabase : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnection _connection;

    public AppDatabase(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        _dbPath = Path.Combine(dataDirectory, "McKuro.db");
        _connection = new SqliteConnection($"Data Source={_dbPath};Default Timeout=30");
        _connection.Open();
        Initialize();
    }

    public SqliteConnection Connection => _connection;

    private void Initialize()
    {
        const int maxAttempts = 6;
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                InitializeCore();
                return;
            }
            catch (SqliteException ex) when
                ((ex.SqliteErrorCode == 5 || ex.SqliteErrorCode == 6) && attempt < maxAttempts - 1)
            {
                // 另一实例正在短暂提交 WAL/迁移时,等待后重试整个可重入初始化。
                Thread.Sleep(250 * (attempt + 1));
            }
        }
    }

    private void InitializeCore()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText =
            """
            PRAGMA journal_mode=WAL;

            CREATE TABLE IF NOT EXISTS gacha_records (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                player_id TEXT NOT NULL,
                card_pool_type INTEGER NOT NULL,
                resource_id INTEGER NOT NULL,
                quality_level INTEGER NOT NULL,
                resource_type TEXT NOT NULL DEFAULT '',
                name TEXT NOT NULL DEFAULT '',
                count INTEGER NOT NULL DEFAULT 1,
                time TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_gacha_player_pool
                ON gacha_records(player_id, card_pool_type);

            CREATE TABLE IF NOT EXISTS players (
                player_id TEXT PRIMARY KEY,
                player_name TEXT NOT NULL DEFAULT '',
                last_sync_time TEXT NULL
            );

            CREATE TABLE IF NOT EXISTS role_cache (
                account_id TEXT NOT NULL DEFAULT '',
                player_id TEXT NOT NULL,
                json TEXT NOT NULL,
                update_time TEXT NOT NULL,
                PRIMARY KEY (account_id, player_id)
            );

            CREATE TABLE IF NOT EXISTS game_time (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                role_id TEXT NOT NULL DEFAULT '',
                game_date TEXT NOT NULL,
                start_time TEXT NOT NULL,
                end_time TEXT NOT NULL,
                duration_sec INTEGER NOT NULL DEFAULT 0
            );

            CREATE INDEX IF NOT EXISTS idx_game_time_date
                ON game_time(game_date, role_id);

            CREATE TABLE IF NOT EXISTS installed_versions (
                game_root TEXT PRIMARY KEY,
                version TEXT NOT NULL,
                update_time TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS new_tower_history (
                role_id TEXT NOT NULL,
                end_time INTEGER NOT NULL,
                json TEXT NOT NULL,
                update_time TEXT NOT NULL,
                PRIMARY KEY (role_id, end_time)
            );
            """;
        cmd.ExecuteNonQuery();

        // 迁移过程必须先关闭 PRAGMA 查询的 reader,再执行 ALTER/DROP,否则 SQLite 会认为表仍被读取并返回 SQLITE_BUSY.
        bool TableExists(string tableName)
        {
            using var c = _connection.CreateCommand();
            c.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=$name LIMIT 1";
            c.Parameters.AddWithValue("$name", tableName);
            return c.ExecuteScalar() is not null;
        }

        bool HasColumn(string tableName, string columnName)
        {
            using var c = _connection.CreateCommand();
            c.CommandText = $"PRAGMA table_info({tableName})";
            using var reader = c.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        bool IndexExists(string indexName)
        {
            using var c = _connection.CreateCommand();
            c.CommandText = "SELECT 1 FROM sqlite_master WHERE type='index' AND name=$name LIMIT 1";
            c.Parameters.AddWithValue("$name", indexName);
            return c.ExecuteScalar() is not null;
        }

        long RowCount(string tableName, SqliteTransaction? tx = null)
        {
            using var c = _connection.CreateCommand();
            c.Transaction = tx;
            c.CommandText = $"SELECT COUNT(*) FROM {tableName}";
            return Convert.ToInt64(c.ExecuteScalar());
        }

        void Run(SqliteTransaction tx, string sql)
        {
            using var c = _connection.CreateCommand();
            c.Transaction = tx;
            c.CommandText = sql;
            c.ExecuteNonQuery();
        }

        // 处理上次异常退出可能留下的 role_cache_old。新表已有数据时只清理旧副本,
        // 新表为空时先恢复旧数据,保证迁移可重入且不丢缓存。
        bool roleCacheOldExists = TableExists("role_cache_old");
        bool roleCacheHasAccount = HasColumn("role_cache", "account_id");
        if (roleCacheOldExists && roleCacheHasAccount)
        {
            using var tx = _connection.BeginTransaction();
            if (RowCount("role_cache", tx) == 0)
            {
                Run(tx, "INSERT INTO role_cache (account_id, player_id, json, update_time) SELECT account_id, player_id, json, update_time FROM role_cache_old");
            }
            Run(tx, "DROP TABLE role_cache_old");
            tx.Commit();
        }
        else if (!roleCacheHasAccount)
        {
            // 旧表结构:player_id 为主键;新建带账号列的表并拷贝数据。
            using var tx = _connection.BeginTransaction();
            Run(tx, "ALTER TABLE role_cache RENAME TO role_cache_old");
            Run(tx, """
                CREATE TABLE role_cache (
                    account_id TEXT NOT NULL DEFAULT '',
                    player_id TEXT NOT NULL,
                    json TEXT NOT NULL,
                    update_time TEXT NOT NULL,
                    PRIMARY KEY (account_id, player_id)
                )
                """);
            Run(tx, "INSERT INTO role_cache (account_id, player_id, json, update_time) SELECT '', player_id, json, update_time FROM role_cache_old");
            Run(tx, "DROP TABLE role_cache_old");
            tx.Commit();
        }

        // 处理上次异常退出可能留下的 gacha_records_old,并移除旧版唯一约束。
        bool gachaOldExists = TableExists("gacha_records_old");
        bool gachaHasUniqueIndex = IndexExists("sqlite_autoindex_gacha_records_1");
        if (gachaOldExists)
        {
            using var tx = _connection.BeginTransaction();
            if (RowCount("gacha_records", tx) == 0)
            {
                Run(tx, "INSERT INTO gacha_records (id, player_id, card_pool_type, resource_id, quality_level, resource_type, name, count, time) SELECT id, player_id, card_pool_type, resource_id, quality_level, resource_type, name, count, time FROM gacha_records_old");
            }
            Run(tx, "DROP TABLE gacha_records_old");
            Run(tx, "CREATE INDEX IF NOT EXISTS idx_gacha_player_pool ON gacha_records(player_id, card_pool_type)");
            tx.Commit();
        }
        else if (gachaHasUniqueIndex)
        {
            using var tx = _connection.BeginTransaction();
            Run(tx, "ALTER TABLE gacha_records RENAME TO gacha_records_old");
            Run(tx, """
                CREATE TABLE gacha_records (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    player_id TEXT NOT NULL,
                    card_pool_type INTEGER NOT NULL,
                    resource_id INTEGER NOT NULL,
                    quality_level INTEGER NOT NULL,
                    resource_type TEXT NOT NULL DEFAULT '',
                    name TEXT NOT NULL DEFAULT '',
                    count INTEGER NOT NULL DEFAULT 1,
                    time TEXT NOT NULL
                )
                """);
            Run(tx, "INSERT INTO gacha_records (id, player_id, card_pool_type, resource_id, quality_level, resource_type, name, count, time) SELECT id, player_id, card_pool_type, resource_id, quality_level, resource_type, name, count, time FROM gacha_records_old");
            Run(tx, "DROP TABLE gacha_records_old");
            Run(tx, "CREATE INDEX IF NOT EXISTS idx_gacha_player_pool ON gacha_records(player_id, card_pool_type)");
            tx.Commit();
        }
    }

    /// <summary>保存终焉矩阵一期历史(同角色同期 UPSERT,对齐 WutheringWavesTool GameNewTowerService.saveToDB)。</summary>
    public void UpsertNewTowerHistory(string roleId, long endTimeMillis, string json)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO new_tower_history (role_id, end_time, json, update_time)
            VALUES ($role, $end, $json, $time)
            ON CONFLICT(role_id, end_time) DO UPDATE SET
                json = excluded.json,
                update_time = excluded.update_time
            """;
        cmd.Parameters.AddWithValue("$role", roleId);
        cmd.Parameters.AddWithValue("$end", endTimeMillis);
        cmd.Parameters.AddWithValue("$json", json);
        cmd.Parameters.AddWithValue("$time", DateTime.Now.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    /// <summary>某角色的终焉矩阵历史赛季结束时间列表(降序,对齐 getEndTimesByRoleId)。</summary>
    public List<long> GetNewTowerHistoryEndTimes(string roleId)
    {
        var result = new List<long>();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT end_time FROM new_tower_history WHERE role_id = $role ORDER BY end_time DESC";
        cmd.Parameters.AddWithValue("$role", roleId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(reader.GetInt64(0));
        }
        return result;
    }

    /// <summary>读取某角色某期的终焉矩阵模式详情 JSON(无记录返回 null)。</summary>
    public string? GetNewTowerHistory(string roleId, long endTimeMillis)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT json FROM new_tower_history WHERE role_id = $role AND end_time = $end LIMIT 1";
        cmd.Parameters.AddWithValue("$role", roleId);
        cmd.Parameters.AddWithValue("$end", endTimeMillis);
        return cmd.ExecuteScalar() as string;
    }

    /// <summary>读取某游戏目录的已安装版本(无记录返回 null)。</summary>
    public string? GetInstalledVersion(string gameRoot)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT version FROM installed_versions WHERE game_root = $root";
        cmd.Parameters.AddWithValue("$root", NormalizeRoot(gameRoot));
        return cmd.ExecuteScalar() as string;
    }

    /// <summary>记录(或更新)某游戏目录的已安装版本。单条 UPSERT,原子提交。</summary>
    public void SetInstalledVersion(string gameRoot, string version)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO installed_versions (game_root, version, update_time)
            VALUES ($root, $version, $time)
            ON CONFLICT(game_root) DO UPDATE SET
                version = excluded.version,
                update_time = excluded.update_time
            """;
        cmd.Parameters.AddWithValue("$root", NormalizeRoot(gameRoot));
        cmd.Parameters.AddWithValue("$version", version);
        cmd.Parameters.AddWithValue("$time", DateTime.Now.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    /// <summary>规范化目录 key:绝对路径 + 去尾部斜杠 + 统一大小写(消除路径漂移导致记录失效)。</summary>
    internal static string NormalizeRoot(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            return root;
        }
        try
        {
            var full = Path.GetFullPath(root).TrimEnd('\\', '/');
            return OperatingSystem.IsWindows() ? full.ToLowerInvariant() : full;
        }
        catch (Exception)
        {
            return root.TrimEnd('\\', '/');
        }
    }

    public void Dispose() => _connection.Dispose();
}
