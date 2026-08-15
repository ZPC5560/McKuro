using Microsoft.Data.Sqlite;

namespace donet.Core.Infrastructure;

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
        _dbPath = Path.Combine(dataDirectory, "donet.db");
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
                player_id TEXT PRIMARY KEY,
                json TEXT NOT NULL,
                update_time TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }

    public void Dispose() => _connection.Dispose();
}
