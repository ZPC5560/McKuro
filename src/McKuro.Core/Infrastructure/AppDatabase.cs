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
        _connection = new SqliteConnection($"Data Source={_dbPath}");
        _connection.Open();
        Initialize();
    }

    public SqliteConnection Connection => _connection;

    private void Initialize()
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
                time TEXT NOT NULL,
                UNIQUE(player_id, card_pool_type, time, resource_id, name)
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
            """;
        cmd.ExecuteNonQuery();

        // 迁移:旧版 role_cache 无 account_id 列(旧主键 player_id),补列并重建主键
        using (var migCmd = _connection.CreateCommand())
        {
            migCmd.CommandText = "PRAGMA table_info(role_cache)";
            bool hasAccount = false;
            using var reader = migCmd.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), "account_id", StringComparison.OrdinalIgnoreCase))
                {
                    hasAccount = true;
                    break;
                }
            }
            if (!hasAccount)
            {
                // 旧表结构:player_id 为主键;新建带账号列的表并拷贝数据
                using var tx = _connection.BeginTransaction();
                using (var c = _connection.CreateCommand())
                {
                    c.Transaction = tx;
                    c.CommandText =
                        """
                        ALTER TABLE role_cache RENAME TO role_cache_old;
                        CREATE TABLE role_cache (
                            account_id TEXT NOT NULL DEFAULT '',
                            player_id TEXT NOT NULL,
                            json TEXT NOT NULL,
                            update_time TEXT NOT NULL,
                            PRIMARY KEY (account_id, player_id)
                        );
                        INSERT INTO role_cache (account_id, player_id, json, update_time)
                            SELECT '', player_id, json, update_time FROM role_cache_old;
                        DROP TABLE role_cache_old;
                        """;
                    c.ExecuteNonQuery();
                }
                tx.Commit();
            }
        }
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
