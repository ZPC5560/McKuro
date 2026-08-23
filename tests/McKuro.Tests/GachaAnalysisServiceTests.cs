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
            new()
            {
                PlayerId = "p1",
                CardPoolType = "武器常驻",
                ResourceId = 21010015,
                QualityLevel = 5,
                ResourceType = "武器",
                Name = "ResidentWeapon",
                Count = 1,
                Time = "2024-01-01 10:00:00",
            },
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

    // ---- "预计下一抽"概率模型(统计卡片) ----

    [Fact]
    public void FiveStarRateModel_Curve()
    {
        Assert.Equal(0.018, GachaRateModel.FiveStarRateAtPity(0), 6);   // 刚出金
        Assert.Equal(0.018, GachaRateModel.FiveStarRateAtPity(64), 6);  // 第 65 抽仍基础
        Assert.Equal(0.058, GachaRateModel.FiveStarRateAtPity(65), 6);  // 第 66 抽软保底 +4%
        Assert.Equal(0.578, GachaRateModel.FiveStarRateAtPity(78), 6);  // 第 79 抽
        Assert.Equal(1.0, GachaRateModel.FiveStarRateAtPity(79), 6);    // 第 80 抽硬保底
        Assert.Equal(1.0, GachaRateModel.FiveStarRateAtPity(120), 6);   // 超保底数据封顶
    }

    [Fact]
    public void Analyze_NextPullUpChance_RolePool_SmallGuarantee()
    {
        // 上一金 UP(小保底状态),垫 2 抽:UP 概率 = 5★ 率 × 50%
        var records = new List<GachaRecord>
        {
            R(1, 900, 5, "UP", "2024-01-01 10:00:00"),
            R(1, 101, 4, "A", "2024-01-01 10:01:00"),
            R(1, 102, 4, "B", "2024-01-01 10:02:00"),
        };
        var upIds = new Dictionary<CardPoolType, HashSet<int>> { [CardPoolType.RoleActivity] = [900] };

        var pool = new GachaAnalysisService().Analyze("p1", records, upIds)[CardPoolType.RoleActivity];

        Assert.NotNull(pool);
        Assert.Equal(2, pool!.CurrentPity);
        Assert.False(pool.IsGuaranteedUp);
        Assert.True(pool.IsFiftyFifty);
        Assert.Equal(0.9, pool.NextPullUpPercent, 6);           // 0.018*100*0.5
        Assert.Equal("小保底", pool.GuaranteeBadgeText);
        Assert.Equal("fifty", pool.GuaranteeBadgeKind);
        Assert.Equal("预计下一抽 UP", pool.UpChanceCaption);
    }

    [Fact]
    public void Analyze_NextPullUpChance_RolePool_GuaranteedUp()
    {
        // 上一金歪(大保底状态):下一金必 UP,概率 = 5★ 率 × 100%
        var records = new List<GachaRecord>
        {
            R(1, 900, 5, "UP", "2024-01-01 10:00:00"),
            R(1, 901, 5, "OFF", "2024-01-02 10:00:00"), // 歪了 → 大保底
        };
        var upIds = new Dictionary<CardPoolType, HashSet<int>> { [CardPoolType.RoleActivity] = [900] };

        var pool = new GachaAnalysisService().Analyze("p1", records, upIds)[CardPoolType.RoleActivity];

        Assert.NotNull(pool);
        Assert.True(pool!.IsGuaranteedUp);
        Assert.Equal(1.8, pool.NextPullUpPercent, 6); // 0.018*100
        Assert.Equal("大保底", pool.GuaranteeBadgeText);
        Assert.Equal("guaranteed", pool.GuaranteeBadgeKind);
    }

    [Fact]
    public void Analyze_NextPullUpChance_WeaponPool_NeverOff()
    {
        // 武器活动池:不歪,出金即 UP;80 抽保底
        var records = new List<GachaRecord>
        {
            R(2, 910, 5, "W-UP", "2024-01-01 10:00:00"),
            R(2, 101, 4, "A", "2024-01-01 10:01:00"),
            R(2, 102, 4, "B", "2024-01-01 10:02:00"),
            R(2, 103, 4, "C", "2024-01-01 10:03:00"),
        };
        var upIds = new Dictionary<CardPoolType, HashSet<int>> { [CardPoolType.WeaponsActivity] = [910] };

        var pool = new GachaAnalysisService().Analyze("p1", records, upIds)[CardPoolType.WeaponsActivity];

        Assert.NotNull(pool);
        Assert.Equal(3, pool!.CurrentPity);
        Assert.False(pool.IsFiftyFifty);
        Assert.Equal(1.8, pool.NextPullUpPercent, 6); // 0.018*100,不 ×50%
        Assert.Equal("必UP", pool.GuaranteeBadgeText);
        Assert.Equal("always", pool.GuaranteeBadgeKind);
    }

    [Fact]
    public void Analyze_NextPullUpChance_Collaboration_SameAsRole()
    {
        // 联动池与当期 UP 池一样会歪(50/50 小保底)
        var records = new List<GachaRecord>
        {
            R(10, 900, 5, "UP", "2024-01-01 10:00:00"),
            R(10, 101, 4, "A", "2024-01-01 10:01:00"),
        };
        var upIds = new Dictionary<CardPoolType, HashSet<int>> { [CardPoolType.CharacterCollaboration] = [900] };

        var pool = new GachaAnalysisService().Analyze("p1", records, upIds)[CardPoolType.CharacterCollaboration];

        Assert.NotNull(pool);
        Assert.True(pool!.IsFiftyFifty);
        Assert.False(pool.IsGuaranteedUp);
        Assert.Equal(0.9, pool.NextPullUpPercent, 6);
    }

    [Fact]
    public void Analyze_NextPullUpChance_ResidentPool_NoUpTarget()
    {
        // 常驻池无 UP:目标为任意 5★,概率 = 5★ 率;徽标"无UP"
        var records = new List<GachaRecord>
        {
            R(3, 300, 5, "ResidentSSR", "2024-01-01 10:00:00"),
        };

        var pool = new GachaAnalysisService().Analyze("p1", records)[CardPoolType.RoleResident];

        Assert.NotNull(pool);
        Assert.False(pool!.HasUpTarget);
        Assert.False(pool.IsFiftyFifty);
        Assert.Equal(1.8, pool.NextPullUpPercent, 6);
        Assert.Equal("预计下一抽 5★", pool.UpChanceCaption);
        Assert.Equal("无UP", pool.GuaranteeBadgeText);
        Assert.Equal("none", pool.GuaranteeBadgeKind);
    }

    [Fact]
    public void Analyze_NextPullUpChance_NearHardPity()
    {
        // 垫 79 抽(下一抽必为第 80 抽必出金):小保底时 UP 概率 = 100% × 50%
        var records = new List<GachaRecord>
        {
            R(1, 900, 5, "UP", "2024-01-01 10:00:00"),
        };
        for (int i = 0; i < 79; i++)
        {
            records.Add(R(1, 101 + i, 4, $"A{i}", $"2024-01-{(i / 10) + 2:00} 10:00:00"));
        }
        var upIds = new Dictionary<CardPoolType, HashSet<int>> { [CardPoolType.RoleActivity] = [900] };

        var pool = new GachaAnalysisService().Analyze("p1", records, upIds)[CardPoolType.RoleActivity];

        Assert.NotNull(pool);
        Assert.Equal(79, pool!.CurrentPity);
        Assert.False(pool.IsGuaranteedUp);
        Assert.Equal(50.0, pool.NextPullUpPercent, 6); // 必出金 × 小保底 50%
        Assert.Equal(79.0, pool.PityProgressValue, 6);
    }

    [Fact]
    public void Analyze_NextPullUpChance_OverHardPity_CapsProgress()
    {
        // 超保底垫抽(异常数据):进度封顶 80,概率仍按第 80 抽必出金计算
        var records = new List<GachaRecord>
        {
            R(1, 900, 5, "UP", "2024-01-01 10:00:00"),
        };
        for (int i = 0; i < 82; i++)
        {
            records.Add(R(1, 101 + i, 4, $"A{i}", $"2024-01-{(i / 10) + 2:00} 10:00:00"));
        }
        var upIds = new Dictionary<CardPoolType, HashSet<int>> { [CardPoolType.RoleActivity] = [900] };

        var pool = new GachaAnalysisService().Analyze("p1", records, upIds)[CardPoolType.RoleActivity];

        Assert.NotNull(pool);
        Assert.Equal(82, pool!.CurrentPity);
        Assert.Equal(80.0, pool.PityProgressValue, 6); // 封顶 80
        Assert.Equal(50.0, pool.NextPullUpPercent, 6);
    }
}
