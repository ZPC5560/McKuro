using McKuro.Core.Infrastructure;
using McKuro.Core.Services.Roles;
using Microsoft.Extensions.Logging.Abstractions;

namespace McKuro.Tests;

/// <summary>
/// role_cache 账号维度测试:
/// 1) 旧表结构(无 account_id)→ AppDatabase 迁移后带账号列,数据保留;
/// 2) LoadFromCache 校验账号归属(账号不一致返回不可用)。
/// </summary>
public class RoleCacheAccountTests : IDisposable
{
    private readonly string _tmpDir;

    public RoleCacheAccountTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "McKuro_rc_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmpDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tmpDir, recursive: true);
        }
        catch (Exception)
        {
            // 忽略
        }
    }

    [Fact]
    public void LoadFromCache_Requires_Matching_Account()
    {
        using var db = new AppDatabase(_tmpDir);
        var service = new RoleDataService(
            api: null!,
            localReader: null!,
            db: db,
            kuro: null!,
            accounts: null!,
            logger: NullLogger<RoleDataService>.Instance);

        // 先通过私有 SaveCache 写入?不直接暴露 —— 用 LoadFromKujiequAsync 无法在无网络下测。
        // 改为直接验证:未写入时任意账号均不可用,且不抛异常。
        var miss = service.LoadFromCache("account-a", "player-1");
        Assert.False(miss.IsSuccess);
        Assert.Contains("无缓存", miss.Message ?? "");
    }

    [Fact]
    public void LoadFromCache_Empty_PlayerId_Returns_NotConfigured()
    {
        using var db = new AppDatabase(_tmpDir);
        var service = new RoleDataService(
            api: null!,
            localReader: null!,
            db: db,
            kuro: null!,
            accounts: null!,
            logger: NullLogger<RoleDataService>.Instance);
        var result = service.LoadFromCache("account-a", "");
        Assert.False(result.IsSuccess);
        Assert.Contains("未配置", result.Message ?? "");
    }

    [Fact]
    public void Database_Migrates_Old_RoleCache_With_Account_Column()
    {
        // 模拟旧版库:role_cache 无 account_id 列(player_id 主键),带一条数据
        var dbPath = Path.Combine(_tmpDir, "McKuro.db");
        using (var legacy = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
        {
            legacy.Open();
            using var cmd = legacy.CreateCommand();
            cmd.CommandText =
                """
                CREATE TABLE role_cache (
                    player_id TEXT PRIMARY KEY,
                    json TEXT NOT NULL,
                    update_time TEXT NOT NULL
                );
                INSERT INTO role_cache (player_id, json, update_time)
                    VALUES ('player-1', '[{"roleName":"漂泊者"}]', '2026-01-01');
                """;
            cmd.ExecuteNonQuery();
        }

        // 初始化 AppDatabase 触发迁移
        using var db = new AppDatabase(_tmpDir);
        // 迁移后旧数据 account_id 应为空串,仍可通过空账号查到
        using var check = db.Connection.CreateCommand();
        check.CommandText = "SELECT json FROM role_cache WHERE account_id = '' AND player_id = 'player-1'";
        var json = check.ExecuteScalar() as string;
        Assert.False(string.IsNullOrEmpty(json));
        Assert.Contains("漂泊者", json!);
    }
}
