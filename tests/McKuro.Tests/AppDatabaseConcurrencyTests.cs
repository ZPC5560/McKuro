using McKuro.Core.Infrastructure;
using McKuro.Core.Models.Gacha;
using McKuro.Core.Services.Gacha;
using McKuro.Core.Services.Guide;
using McKuro.Core.Services.Roles;
using McKuro.Core.Services.Settings;

namespace McKuro.Tests;

/// <summary>
/// 共享 SQLite 连接的并发访问回归测试(2026-10-25 评审发现)。
/// <para>
/// 缺陷背景:<c>AppDatabase</c> 只持有一条 <c>SqliteConnection</c>,而 UI 线程与后台线程
/// (<c>Task.Run</c> 里的攻略缓存清理、塔历史读取等)<b>会同时访问</b>它。
/// Microsoft.Data.Sqlite 的连接不是线程安全的,并发时可能抛
/// <c>InvalidOperationException</c>/SQLITE_BUSY,且调用点多为静默 catch。
/// </para>
/// <para>
/// 修复:所有访问统一经 <see cref="AppDatabase.Execute{T}"/> 或 <see cref="AppDatabase.Enter"/>
/// 串行化。本测试用多线程同时读写,断言"不抛异常且数据自洽"。
/// </para>
/// </summary>
public class AppDatabaseConcurrencyTests : IDisposable
{
    private readonly string _dir;

    public AppDatabaseConcurrencyTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "McKuro-dbcon-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (Exception)
        {
            // 忽略清理失败(文件可能仍被 SQLite 持有到进程结束)
        }
    }

    private static GachaRecord Record(int i) => new()
    {
        PlayerId = "player-1",
        CardPoolType = CardPoolTypeValues.GetDisplayName(CardPoolType.RoleActivity),
        ResourceId = i,
        QualityLevel = 5,
        ResourceType = "role",
        Name = "测试角色" + i,
        Count = 1,
        Time = "2026-10-25 10:00:00",
    };

    /// <summary>
    /// 多线程同时读+写:串行化后不应出现连接级异常,且写入的记录数对得上。
    /// </summary>
    [Fact]
    public async Task Concurrent_Reads_And_Writes_Do_Not_Throw()
    {
        var db = new AppDatabase(_dir);
        try
        {
            var store = new GachaRecordStore(db);
            const int writers = 4;
            const int perWriter = 15;
            var exceptions = new List<Exception>();
            var gate = new object();

            var tasks = new List<Task>();
            for (var w = 0; w < writers; w++)
            {
                var baseIndex = w * perWriter;
                tasks.Add(Task.Run(() =>
                {
                    try
                    {
                        for (var i = 0; i < perWriter; i++)
                        {
                            store.InsertRecords("player-1", [Record(baseIndex + i)]);
                        }
                    }
                    catch (Exception ex)
                    {
                        lock (gate)
                        {
                            exceptions.Add(ex);
                        }
                    }
                }));
            }

            // 读侧:与写并发(RolesViewModel 的清理、TowerViewModel 的历史读取都是这种形态)
            for (var r = 0; r < writers; r++)
            {
                tasks.Add(Task.Run(() =>
                {
                    try
                    {
                        for (var i = 0; i < 10; i++)
                        {
                            _ = store.GetRecords("player-1");
                            _ = store.GetAllPlayerIds();
                        }
                    }
                    catch (Exception ex)
                    {
                        lock (gate)
                        {
                            exceptions.Add(ex);
                        }
                    }
                }));
            }

            try
            {
                await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(60));
            }
            catch (TimeoutException)
            {
                Assert.Fail("并发读写超时(疑似锁争用或死锁)");
            }
            Assert.True(exceptions.Count == 0,
                "并发访问共享连接抛异常(说明串行化未生效): " +
                string.Join(" | ", exceptions.Select(e => e.GetType().Name + ": " + e.Message).Distinct()));

            var all = store.GetRecords("player-1");
            Assert.Equal(writers * perWriter, all.Count);
        }
        finally
        {
            db.Dispose();
        }
    }

    /// <summary>
    /// 不同服务(抽卡 / 攻略缓存 / 角色缓存)并发访问同一条连接:服务之间没有互相加锁,
    /// 全靠 AppDatabase 的闸门串行化,这条测试覆盖"跨服务"场景。
    /// </summary>
    [Fact]
    public async Task Cross_Service_Concurrent_Access_Is_Serialized()
    {
        var settings = new SettingsService(_dir);
        using var db = new AppDatabase(_dir);
        var gacha = new GachaRecordStore(db);
        var guide = new GuideCacheService(db);
        var exceptions = new List<Exception>();
        var gate = new object();

        var tasks = new List<Task>
        {
            Task.Run(() =>
            {
                try
                {
                    for (var i = 0; i < 20; i++)
                    {
                        gacha.InsertRecords("p", [Record(i)]);
                    }
                }
                catch (Exception ex) { lock (gate) { exceptions.Add(ex); } }
            }),
            Task.Run(() =>
            {
                try
                {
                    for (var i = 0; i < 20; i++)
                    {
                        guide.SaveList("acct", i + 1, [], 0);
                        _ = guide.TryGet("acct", i + 1);
                        guide.PruneExcept("acct", [i + 1]);
                    }
                }
                catch (Exception ex) { lock (gate) { exceptions.Add(ex); } }
            }),
        };

        try
        {
            await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(60));
        }
        catch (TimeoutException)
        {
            Assert.Fail("跨服务并发访问超时");
        }
        Assert.True(exceptions.Count == 0,
            "跨服务并发访问抛异常: " + string.Join(" | ", exceptions.Select(e => e.GetType().Name + ": " + e.Message).Distinct()));
        _ = settings; // 仅用于构造环境(设置目录与库同目录)
    }
}
