using McKuro.Core.Infrastructure;
using McKuro.Core.Models.Gacha;
using McKuro.Core.Services.Gacha;
using Xunit;

namespace McKuro.Tests;

/// <summary>
/// 跨玩家聚合分析测试:空 playerId 应聚合全部玩家的记录。
/// </summary>
public class CrossPlayerAnalysisTests : IDisposable
{
    private readonly string _tmpDir;
    private readonly AppDatabase _db;
    private readonly GachaRecordStore _store;
    private readonly GachaAnalysisService _service;

    public CrossPlayerAnalysisTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "McKuro_xp_" + Guid.NewGuid().ToString("N"));
        _db = new AppDatabase(_tmpDir);
        _store = new GachaRecordStore(_db);
        _service = new GachaAnalysisService();
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_tmpDir, recursive: true); } catch (Exception) { }
    }

    private static GachaRecord Record(string playerId, string time, bool fiveStar, string name = "武器", int resourceId = 0)
        => new()
        {
            PlayerId = playerId,
            CardPoolType = CardPoolTypeValues.GetDisplayName(CardPoolType.RoleActivity),
            ResourceId = resourceId,
            QualityLevel = fiveStar ? 5 : 4,
            ResourceType = "角色",
            Name = name,
            Count = 1,
            Time = time,
        };

    [Fact]
    public void Empty_PlayerId_Aggregates_All_Players()
    {
        var records = new List<GachaRecord>
        {
            Record("P1", "2025-01-01 10:00:00", fiveStar: true, name: "卡卡罗", resourceId: 1),
            Record("P1", "2025-01-02 10:00:00", fiveStar: false),
            Record("P2", "2025-01-03 10:00:00", fiveStar: true, name: "忌炎", resourceId: 2),
            Record("P2", "2025-01-04 10:00:00", fiveStar: false),
        };

        var result = _service.Analyze("", records);

        Assert.Equal(4, result.TotalPulls);
        Assert.Equal(2, result.TotalFiveStars);
    }

    [Fact]
    public void Specific_Player_Only_Counts_That_Player()
    {
        var records = new List<GachaRecord>
        {
            Record("P1", "2025-01-01 10:00:00", fiveStar: true, name: "卡卡罗", resourceId: 1),
            Record("P2", "2025-01-03 10:00:00", fiveStar: true, name: "忌炎", resourceId: 2),
        };

        var result = _service.Analyze("P1", records);

        Assert.Equal(1, result.TotalPulls);
        Assert.Equal(1, result.TotalFiveStars);
    }

    [Fact]
    public void GetAllRecords_Returns_Everything()
    {
        _store.UpsertRecords("P1", [Record("P1", "2025-01-01 10:00:00", false)]);
        _store.UpsertRecords("P2", [Record("P2", "2025-01-02 10:00:00", true, name: "忌炎", resourceId: 2)]);

        var all = _store.GetAllRecords();

        Assert.Equal(2, all.Count);
        Assert.Contains(all, r => r.PlayerId == "P1");
        Assert.Contains(all, r => r.PlayerId == "P2");
    }
}
