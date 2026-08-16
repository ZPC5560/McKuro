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
            CardPoolType = pool,
            ResourceId = 100,
            QualityLevel = quality,
            ResourceType = "角色",
            Name = name,
            Count = 1,
            Time = time,
        };

    [Fact]
    public void Upsert_Deduplicates()
    {
        var a = Make("SSR", "2024-01-01 10:00:00");
        var b = Make("SSR", "2024-01-01 10:00:00"); // 完全重复

        _store.UpsertRecords("p1", [a, b]);
        var records = _store.GetRecords("p1");
        Assert.Single(records);
    }

    [Fact]
    public void Upsert_KeepsDistinctTimes()
    {
        _store.UpsertRecords("p1", [Make("SSR", "2024-01-01 10:00:00")]);
        _store.UpsertRecords("p1", [Make("SSR", "2024-01-02 10:00:00")]);

        Assert.Equal(2, _store.GetRecords("p1").Count);
    }

    [Fact]
    public void GetRecords_FiltersByPool()
    {
        _store.UpsertRecords("p1",
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
        _store.UpsertRecords("p1", [Make("SSR", "2024-01-01 10:00:00")]);
        _store.UpsertRecords("p2", [Make("SSR", "2024-01-01 10:00:00")]);

        var ids = _store.GetAllPlayerIds();
        Assert.Equal(2, ids.Count);

        _store.DeletePlayer("p1");
        ids = _store.GetAllPlayerIds();
        Assert.Single(ids);
        Assert.Equal("p2", ids[0]);
    }
}
