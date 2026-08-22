using McKuro.Core.Models.Gacha;
using McKuro.Core.Services.Gacha;

namespace McKuro.Tests;

public class GachaAnalysisServiceTests
{
    private static GachaRecord R(int pool, int resourceId, int quality, string name, string time) =>
        new()
        {
            PlayerId = "p1",
            CardPoolType = CardPoolTypeValues.GetDisplayName((CardPoolType)pool),
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
        // 时间从旧到新:3 个非 5 星 → 5 星 (pity=3,不含本次) → 2 个非 5 星 (当前垫 2)
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
        Assert.Equal(3, pool.FiveStarEntries[0].Pity); // 不含本次,前 3 个普通抽
        Assert.Equal(2, pool.CurrentPity);              // 垫 2 抽
        Assert.Equal(4.0, pool.AveragePity!.Value, 2);  // 平均含本次 3+1
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
    public void Analyze_ProvidesFiveStarGapAndCurrentUpPity()
    {
        var records = new List<GachaRecord>
        {
            R(1, 900, 5, "UP", "2024-01-01 10:00:00"),
            R(1, 101, 4, "A", "2024-01-01 10:01:00"),
            R(1, 102, 4, "B", "2024-01-01 10:02:00"),
            R(1, 103, 4, "C", "2024-01-01 10:03:00"),
            R(1, 901, 5, "OFF", "2024-01-01 10:04:00"),
            R(1, 104, 4, "D", "2024-01-01 10:05:00"),
            R(1, 105, 4, "E", "2024-01-01 10:06:00"),
        };
        var upIds = new Dictionary<CardPoolType, HashSet<int>>
        {
            [CardPoolType.RoleActivity] = [900],
        };

        var pool = new GachaAnalysisService().Analyze("p1", records, upIds)[CardPoolType.RoleActivity];

        Assert.NotNull(pool);
        Assert.Equal(1, pool!.UpCount);
        Assert.Equal(3, pool.FiveStarEntries[1].Pity); // 不含本次:两个五星之间 3 个普通抽
        Assert.Equal(3, pool.FiveStarEntries[1].FiveStarGap);
        Assert.Equal("间隔 3 抽", pool.FiveStarEntries[1].FiveStarGapText);
        Assert.Equal(6, pool.CurrentUpPity);
        Assert.Equal(7, pool.PullEntries.Count);
        Assert.Equal("OFF", pool.PullEntries[4].Record.Name);
        Assert.Equal("歪", pool.PullEntries[4].BannerText);
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

    [Fact]
    public void Analyze_ResidentPool_NotJudged_AsOffBanner()
    {
        // 常驻池本身就是常驻卡池,即使 upIds 提供集合也不判定歪/UP(服务层守卫)
        var upIds = new Dictionary<CardPoolType, HashSet<int>>
        {
            [CardPoolType.WeaponsResident] = [21050096],
        };
        var records = new List<GachaRecord>
        {
            R(4, 21010015, 5, "ResidentWeapon", "2024-01-01 10:00:00"),
        };

        var result = new GachaAnalysisService().Analyze("p1", records, upIds);
        var pool = result[CardPoolType.WeaponsResident];
        Assert.NotNull(pool);
        var entry = Assert.Single(pool!.FiveStarEntries);
        Assert.Null(entry.IsOffBanner);
        Assert.Equal(0, pool.UpCount);
        Assert.Null(pool.OffBannerRate);
        Assert.False(pool.CanJudgeUp);
    }

    [Fact]
    public void Analyze_EmptyUpSet_NotJudged_AsOffBanner()
    {
        // 空集合(远程数据缺失兜底)时应全部"不判定",避免所有五星误判为歪
        var upIds = new Dictionary<CardPoolType, HashSet<int>>
        {
            [CardPoolType.RoleActivity] = [],
        };
        var records = new List<GachaRecord>
        {
            R(1, 900, 5, "SSR", "2024-01-01 10:00:00"),
        };

        var result = new GachaAnalysisService().Analyze("p1", records, upIds);
        var pool = result[CardPoolType.RoleActivity];
        Assert.NotNull(pool);
        var entry = Assert.Single(pool!.FiveStarEntries);
        Assert.Null(entry.IsOffBanner);
    }

    [Fact]
    public void Analyze_DailyPulls_IncludePoolBreakdown()
    {
        // 2024-01-01: 角色活动 ×2 + 角色常驻 ×1;2024-01-02: 角色活动 ×1
        var records = new List<GachaRecord>
        {
            R(1, 101, 4, "A", "2024-01-01 10:00:00"),
            R(1, 102, 4, "B", "2024-01-01 10:01:00"),
            R(3, 201, 4, "C", "2024-01-01 10:02:00"),
            R(1, 103, 4, "D", "2024-01-02 10:00:00"),
        };

        var result = new GachaAnalysisService().Analyze("p1", records);
        Assert.Equal(2, result.DailyPulls.Count);

        var day1 = result.DailyPulls[0];
        Assert.Equal(new DateOnly(2024, 1, 1), day1.Date);
        Assert.Equal(3, day1.Count);
        Assert.Equal(2, day1.Pools.Count);
        Assert.Equal(CardPoolType.RoleActivity, day1.Pools[0].PoolType);
        Assert.Equal("角色活动", day1.Pools[0].PoolName);
        Assert.Equal(2, day1.Pools[0].Count);
        Assert.Equal(CardPoolType.RoleResident, day1.Pools[1].PoolType);
        Assert.Equal(1, day1.Pools[1].Count);
        Assert.Equal(day1.Count, day1.Pools.Sum(p => p.Count));

        var day2 = result.DailyPulls[1];
        Assert.Equal(new DateOnly(2024, 1, 2), day2.Date);
        Assert.Equal(1, day2.Count);
        Assert.Single(day2.Pools);
        Assert.Equal("角色活动", day2.Pools[0].PoolName);
    }
}
