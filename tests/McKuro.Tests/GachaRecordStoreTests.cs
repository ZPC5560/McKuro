using McKuro.Core.Infrastructure;
using McKuro.Core.Models.Gacha;
using McKuro.Core.Services.Gacha;

namespace McKuro.Tests;

public class GachaRecordStoreTests : IDisposable
{
    private readonly string _tmpDir;
    private readonly AppDatabase _db;
    private readonly GachaRecordStore _store;

    public GachaRecordStoreTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "McKuro_test_" + Guid.NewGuid().ToString("N"));
        _db = new AppDatabase(_tmpDir);
        _store = new GachaRecordStore(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        try
        {
            Directory.Delete(_tmpDir, recursive: true);
        }
        catch (Exception)
        {
            // 忽略
        }
    }

    private static GachaRecord Make(string name, string time, int pool = 1, int quality = 5) =>
        new()
        {
            PlayerId = "p1",
            CardPoolType = CardPoolTypeValues.GetDisplayName((CardPoolType)pool),
            ResourceId = 100,
            QualityLevel = quality,
            ResourceType = "角色",
            Name = name,
            Count = 1,
            Time = time,
        };

    [Fact]
    public void Insert_KeepsAllRecords()
    {
        var a = Make("SSR", "2024-01-01 10:00:00");
        var b = Make("SSR", "2024-01-01 10:00:00"); // 同秒同角色:10连两次出金均保留

        _store.InsertRecords("p1", [a, b]);
        var records = _store.GetRecords("p1");
        Assert.Equal(2, records.Count);
    }

    [Fact]
    public void GetRecords_SameTimeBatch_ReturnsTruePullOrder()
    {
        // 官方接口按"新→旧"返回,入库 id 递增即新→旧;
        // 读取按 time ASC, id DESC 还原真实抽取顺序(旧→新,5星位于批次末尾)。
        var batch = new List<GachaRecord>
        {
            Make("SSR", "2024-01-01 10:00:00"), // API 第1条 = 最新抽(真实最后)
            Make("A", "2024-01-01 10:00:00"),
            Make("B", "2024-01-01 10:00:00"),
            Make("C", "2024-01-01 10:00:00"),
        };
        _store.InsertRecords("p1", batch);

        var records = _store.GetRecords("p1");
        Assert.Equal(4, records.Count);
        // 同秒内 id 倒序:后插入的(更早的抽取)在前,5星(最后抽取)在后
        Assert.Equal("C", records[0].Name);
        Assert.Equal("B", records[1].Name);
        Assert.Equal("A", records[2].Name);
        Assert.Equal("SSR", records[3].Name);
    }

    [Fact]
    public void DeletePlayerPool_ClearsOnlyThatPool()
    {
        _store.InsertRecords("p1",
        [
            Make("SSR", "2024-01-01 10:00:00", pool: 1),
            Make("SSR", "2024-01-01 10:00:00", pool: 2),
        ]);
        _store.DeletePlayerPool("p1", CardPoolType.RoleActivity);
        Assert.Empty(_store.GetRecords("p1", CardPoolType.RoleActivity));
        Assert.Single(_store.GetRecords("p1", CardPoolType.WeaponsActivity));
    }

    [Fact]
    public void Insert_KeepsDistinctTimes()
    {
        _store.InsertRecords("p1", [Make("SSR", "2024-01-01 10:00:00")]);
        _store.InsertRecords("p1", [Make("SSR", "2024-01-02 10:00:00")]);

        Assert.Equal(2, _store.GetRecords("p1").Count);
    }

    [Fact]
    public void GetRecords_FiltersByPool()
    {
        _store.InsertRecords("p1",
        [
            Make("R1", "2024-01-01 10:00:00", pool: 1),
            Make("R2", "2024-01-02 10:00:00", pool: 2),
        ]);

        var pool1 = _store.GetRecords("p1", CardPoolType.RoleActivity);
        Assert.Single(pool1);
        Assert.Equal("R1", pool1[0].Name);
    }

    [Fact]
    public void GetAllPlayerIds_And_Delete()
    {
        _store.InsertRecords("p1", [Make("SSR", "2024-01-01 10:00:00")]);
        _store.InsertRecords("p2", [Make("SSR", "2024-01-01 10:00:00")]);

        var ids = _store.GetAllPlayerIds();
        Assert.Equal(2, ids.Count);

        _store.DeletePlayer("p1");
        ids = _store.GetAllPlayerIds();
        Assert.Single(ids);
        Assert.Equal("p2", ids[0]);
    }
}
