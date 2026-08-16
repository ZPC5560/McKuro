using McKuro.Core.Models.Gacha;
using McKuro.Core.Services.Gacha;

namespace McKuro.Tests;

public class GachaAnalysisServiceTests
{
    private static GachaRecord R(int pool, int resourceId, int quality, string name, string time) =>
        new()
        {
            PlayerId = "p1",
            CardPoolType = pool,
            ResourceId = resourceId,
            QualityLevel = quality,
            ResourceType = quality == 5 ? "角色" : "角色",
            Name = name,
            Count = 1,
            Time = time,
        };

    [Fact]
    public void Analyze_ComputesPityAndCurrentPity()
    {
        // 时间从旧到新:3 个非 5 星 → 5 星 (pity=4) → 2 个非 5 星 (当前垫 2)
        var records = new List<GachaRecord>
        {
            R(1, 101, 4, "A", "2024-01-01 10:00:00"),
            R(1, 102, 4, "B", "2024-01-02 10:00:00"),
            R(1, 103, 4, "C", "2024-01-03 10:00:00"),
            R(1, 900, 5, "SSR", "2024-01-04 10:00:00"),
            R(1, 104, 4, "D", "2024-01-05 10:00:00"),
            R(1, 105, 4, "E", "2024-01-06 10:00:00"),
        };

        var result = new GachaAnalysisService().Analyze("p1", records);
        var pool = result[CardPoolType.RoleActivity];
        Assert.NotNull(pool);
        Assert.Equal(6, pool!.TotalPulls);
        Assert.Equal(1, pool.FiveStarCount);
        Assert.Equal(4, pool.FiveStarEntries[0].Pity); // 含本次共 4 抽
        Assert.Equal(2, pool.CurrentPity);              // 垫 2 抽
        Assert.Equal(4.0, pool.AveragePity!.Value, 2);
        Assert.Equal(6, result.TotalPulls);
        Assert.Equal(1, result.TotalFiveStars);
    }

    [Fact]
    public void Analyze_OffBannerRate_WithUpData()
    {
        // 三个五星:UP(小保底不歪) → 歪(小保底) → UP(大保底)
        // 小保底 2 个,歪 1 个 → 歪率 50%
        var records = new List<GachaRecord>
        {
            R(1, 900, 5, "SSR1", "2024-01-01 10:00:00"), // UP
            R(1, 901, 5, "SSR2", "2024-01-02 10:00:00"), // 歪
            R(1, 900, 5, "SSR3", "2024-01-03 10:00:00"), // 大保底 UP
        };

        var upIds = new Dictionary<CardPoolType, HashSet<int>>
        {
            [CardPoolType.RoleActivity] = [900],
        };

        var result = new GachaAnalysisService().Analyze("p1", records, upIds);
        var pool = result[CardPoolType.RoleActivity];
        Assert.NotNull(pool);
        Assert.Equal(0.5, pool!.OffBannerRate!.Value, 2); // 2 个小保底中 1 个歪
        Assert.False(pool.FiveStarEntries[0].IsOffBanner);
        Assert.True(pool.FiveStarEntries[1].IsOffBanner);
        Assert.False(pool.FiveStarEntries[2].IsOffBanner);
    }

    [Fact]
    public void Analyze_NoUpData_OffBannerIsNull()
    {
        var records = new List<GachaRecord>
        {
            R(1, 900, 5, "SSR", "2024-01-01 10:00:00"),
        };

        var result = new GachaAnalysisService().Analyze("p1", records);
        var pool = result[CardPoolType.RoleActivity];
        Assert.NotNull(pool);
        Assert.Null(pool!.OffBannerRate);
        Assert.Null(pool.FiveStarEntries[0].IsOffBanner);
    }

    [Fact]
    public void Analyze_SeparatesPools()
    {
        var records = new List<GachaRecord>
        {
            R(1, 900, 5, "RoleSSR", "2024-01-01 10:00:00"),
            R(2, 500, 5, "WeaponSSR", "2024-01-01 11:00:00"),
            R(3, 300, 4, "Resident", "2024-01-01 12:00:00"),
        };

        var result = new GachaAnalysisService().Analyze("p1", records);
        Assert.Equal(3, result.Pools.Count);
        Assert.Equal(1, result[CardPoolType.RoleActivity]!.TotalPulls);
        Assert.Equal(1, result[CardPoolType.WeaponsActivity]!.TotalPulls);
        Assert.Equal(1, result[CardPoolType.RoleResident]!.TotalPulls);
        Assert.Equal(2, result.TotalFiveStars);
    }

    [Fact]
    public void Analyze_Score_AlwaysInRange()
    {
        var records = new List<GachaRecord>
        {
            R(1, 900, 5, "SSR", "2024-01-01 10:00:00"),
        };

        var result = new GachaAnalysisService().Analyze("p1", records);
        Assert.InRange(result.Score, 0, 100);
    }
}
