using McKuro.Core.Models.Tower;
using McKuro.Core.Services.Notification;

namespace McKuro.Tests;

/// <summary>
/// 深塔/海墟/矩阵「赛季剩余 ≤3 天」提醒判定测试。
/// <para>
/// 回归背景:三个接口的结束时间字段都是<b>剩余毫秒</b>(不是绝对时间戳),而"剩余 3 天内"必须
/// 在每次拉取后重新折算;判定写错会直接产生两类误报 —— 赛季刚开就提醒(阈值算成绝对值)、
/// 以及已经打满的赛季末天天提醒(完成度没参与判定)。
/// </para>
/// </summary>
public class TowerReminderPolicyTests
{
    private static readonly DateTime Now = new(2026, 10, 26, 20, 0, 0, DateTimeKind.Local);

    private static long Millis(TimeSpan span) => (long)span.TotalMilliseconds;

    [Theory]
    [InlineData(0, 0, 1, true)]        // 只剩 1 分钟:仍在窗口内
    [InlineData(2, 23, 0, true)]
    [InlineData(0, 1, 0, true)]        // 只剩 1 小时
    [InlineData(3, 0, 0, true)]        // 边界:恰好 3 天 → 仍在提醒窗口内(含端点)
    [InlineData(3, 0, 1, false)]       // 3 天零 1 秒 → 还没到窗口
    [InlineData(14, 0, 0, false)]      // 赛季刚开(14 天制)
    [InlineData(28, 0, 0, false)]      // 28 天制
    public void Evaluate_WindowBoundary(int days, int hours, int minutes, bool expected)
    {
        var remaining = new TimeSpan(days, hours, minutes, 0);
        var expiry = TowerReminderPolicy.Evaluate(TowerKind.DeepTower, Now, Millis(remaining), null);
        Assert.Equal(expected, expiry is not null);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(-86_400_000L)] // 上一期残留(库街区在没打新一期时会这么返回)
    public void Evaluate_NonPositiveOrMissing_Millis_NeverReminds(long? remainingMillis)
    {
        // 赛季已结束时的数据是上一期残留,提醒"即将刷新"是纯误报
        Assert.Null(TowerReminderPolicy.Evaluate(TowerKind.DeepTower, Now, remainingMillis, null));
    }

    [Fact]
    public void Evaluate_EndTime_IsNowPlusRemaining()
    {
        var expiry = TowerReminderPolicy.Evaluate(TowerKind.Slash, Now, Millis(TimeSpan.FromHours(5)), null);
        Assert.NotNull(expiry);
        Assert.Equal(Now.AddHours(5), expiry!.EndAt);
        Assert.Equal(TimeSpan.FromHours(5), expiry.Remaining);
    }

    [Fact]
    public void Evaluate_FullyCompleted_Suppresses()
    {
        // 满额完成:赛季怎么刷新都无事可做,提醒只会是噪音
        var progress = new TowerProgress(180, 180, TowerProgressUnit.Stars);
        Assert.Null(TowerReminderPolicy.Evaluate(TowerKind.DeepTower, Now, Millis(TimeSpan.FromHours(2)), progress));
    }

    [Fact]
    public void Evaluate_PartiallyCompleted_Reminds()
    {
        var progress = new TowerProgress(179, 180, TowerProgressUnit.Stars);
        Assert.NotNull(TowerReminderPolicy.Evaluate(TowerKind.DeepTower, Now, Millis(TimeSpan.FromHours(2)), progress));
    }

    [Fact]
    public void Evaluate_UnknownProgress_StillReminds()
    {
        // 接口没给总量(完成度不可判定)时不能静默 —— 宁可多提醒一次,也不能漏
        Assert.NotNull(TowerReminderPolicy.Evaluate(TowerKind.Matrix, Now, Millis(TimeSpan.FromHours(1)), null));
    }

    [Fact]
    public void Progress_IsComplete_RequiresKnownTotal()
    {
        Assert.False(new TowerProgress(0, 0, TowerProgressUnit.Score).IsComplete);
        Assert.False(new TowerProgress(5, -1, TowerProgressUnit.Score).IsComplete);
        Assert.True(new TowerProgress(0, 0, TowerProgressUnit.Score) is { IsComplete: false });
    }

    // ---------- 完成度换算 ----------

    [Fact]
    public void DeepTowerStars_SumsAllDifficulties()
    {
        var data = new TowerSeasonData
        {
            DifficultyList =
            [
                new TowerSeasonDifficulty
                {
                    Difficulty = 1,
                    TowerAreaList =
                    [
                        new TowerSeasonArea { MaxStar = 12, Star = 12 },
                        new TowerSeasonArea { MaxStar = 12, Star = 9 },
                    ],
                },
                new TowerSeasonDifficulty
                {
                    Difficulty = 3,
                    TowerAreaList = [new TowerSeasonArea { MaxStar = 12, Star = 3 }],
                },
            ],
        };

        var progress = TowerReminderPolicy.DeepTowerStars(data);
        Assert.NotNull(progress);
        Assert.Equal(24, progress!.Done);
        Assert.Equal(36, progress.Total);
        Assert.Equal(TowerProgressUnit.Stars, progress.Unit);
        Assert.False(progress.IsComplete);
    }

    [Fact]
    public void DeepTowerStars_ClampsOverflowStars()
    {
        // 实机偶发单区 star 超过 maxStar(三星楼层显示 4):不钳会让进度像"超额完成"
        var data = new TowerSeasonData
        {
            DifficultyList =
            [
                new TowerSeasonDifficulty
                {
                    Difficulty = 1,
                    TowerAreaList = [new TowerSeasonArea { MaxStar = 12, Star = 15 }],
                },
            ],
        };

        var progress = TowerReminderPolicy.DeepTowerStars(data);
        Assert.Equal(12, progress!.Done);
        Assert.True(progress.IsComplete);
    }

    [Theory]
    [InlineData(0)] // 没有 maxStar:完成度不可判定
    public void DeepTowerStars_UnknownTotal_ReturnsNull(int maxStar)
    {
        var data = new TowerSeasonData
        {
            DifficultyList =
            [
                new TowerSeasonDifficulty
                {
                    Difficulty = 1,
                    TowerAreaList = [new TowerSeasonArea { MaxStar = maxStar, Star = 3 }],
                },
            ],
        };
        Assert.Null(TowerReminderPolicy.DeepTowerStars(data));
    }

    [Fact]
    public void DeepTowerStars_EmptyData_ReturnsNull()
    {
        Assert.Null(TowerReminderPolicy.DeepTowerStars(null));
        Assert.Null(TowerReminderPolicy.DeepTowerStars(new TowerSeasonData()));
    }

    [Fact]
    public void SlashScore_UsesRegressionSeaOnly()
    {
        // 只取再生海域(difficulty=1):湍渊是独立挑战,并列进来会让"总积分"口径与页面对不上
        var data = new SlashData
        {
            DifficultyList =
            [
                new SlashDifficulty { Difficulty = 1, AllScore = 9000, MaxScore = 12000 },
                new SlashDifficulty { Difficulty = 2, AllScore = 3000, MaxScore = 4500 },
            ],
        };

        var progress = TowerReminderPolicy.SlashScore(data);
        Assert.Equal(9000, progress!.Done);
        Assert.Equal(12000, progress.Total);
        Assert.Equal(TowerProgressUnit.Score, progress.Unit);
    }

    [Fact]
    public void SlashScore_NoRegressionSea_ReturnsNull()
    {
        var data = new SlashData
        {
            DifficultyList = [new SlashDifficulty { Difficulty = 2, AllScore = 3000, MaxScore = 4500 }],
        };
        Assert.Null(TowerReminderPolicy.SlashScore(data));
        Assert.Null(TowerReminderPolicy.SlashScore(null));
    }

    [Fact]
    public void MatrixModes_CountsRecordedModes()
    {
        var data = new NewTowerData
        {
            ModeDetails =
            [
                new NewTowerModeDetail { ModeId = 0, HasRecord = true, Score = 44065 },
                new NewTowerModeDetail { ModeId = 1, HasRecord = false, Score = 0 },
                // hasRecord 但 0 分:没打完,不算完成
                new NewTowerModeDetail { ModeId = 2, HasRecord = true, Score = 0 },
            ],
        };

        var progress = TowerReminderPolicy.MatrixModes(data);
        Assert.Equal(1, progress!.Done);
        Assert.Equal(3, progress.Total);
        Assert.Equal(TowerProgressUnit.Modes, progress.Unit);
    }

    [Fact]
    public void MatrixModes_NoModes_ReturnsNull()
    {
        Assert.Null(TowerReminderPolicy.MatrixModes(null));
        Assert.Null(TowerReminderPolicy.MatrixModes(new NewTowerData()));
    }

    // ---------- 去重 Key ----------

    [Fact]
    public void ReminderKey_StableWithinTheSameDay_DespiteDriftingRemainingMillis()
    {
        // 回归背景:剩余毫秒每轮检查都在减小,Key 曾把「结束时刻精确到小时」拼进去,
        // 剩余量漂到整点边界附近时归整结果会翻到下一个小时 → 同一天换出新 Key → 弹第二遍。
        // 两次检查相差 5 小时(同日),Key 必须一模一样。
        var morning = new DateTime(2026, 10, 26, 9, 0, 0, DateTimeKind.Local);
        var later = morning.AddHours(5);
        var a = TowerReminderPolicy.Evaluate(TowerKind.Slash, morning, Millis(TimeSpan.FromHours(60)), null);
        var b = TowerReminderPolicy.Evaluate(TowerKind.Slash, later, Millis(TimeSpan.FromHours(55)), null);

        Assert.NotNull(a);
        Assert.NotNull(b);
        // 折算出的绝对结束时刻相同(that's the point),因此 Key 相同
        Assert.Equal(a!.EndAt, b!.EndAt);
        Assert.Equal(TowerReminderPolicy.ReminderKey(a), TowerReminderPolicy.ReminderKey(b));
    }

    [Fact]
    public void ReminderKey_DiffersPerKindAndPerSeason()
    {
        var slash = TowerReminderPolicy.Evaluate(TowerKind.Slash, Now, Millis(TimeSpan.FromHours(10)), null)!;
        var matrix = TowerReminderPolicy.Evaluate(TowerKind.Matrix, Now, Millis(TimeSpan.FromHours(10)), null)!;
        // 玩法不同:同一天两条提醒互不覆盖
        Assert.NotEqual(
            TowerReminderPolicy.ReminderKey(slash),
            TowerReminderPolicy.ReminderKey(matrix));

        // 赛季翻新:结束日变了 Key 必须跟着变,否则上一期的旧台账会把新一期吞掉
        var nextSeason = TowerReminderPolicy.Evaluate(
            TowerKind.Slash, Now.AddDays(28), Millis(TimeSpan.FromHours(10)), null)!;
        Assert.NotEqual(
            TowerReminderPolicy.ReminderKey(slash),
            TowerReminderPolicy.ReminderKey(nextSeason));
    }

    [Fact]
    public void ReminderKey_Uses_Server_Date_Of_End_Time()
    {
        // 回归(L16):赛季在服务器 04:00 结束 = UTC 前一日 20:00。Key 的日期必须取服务器日期,
        // 否则按调用方时区格式化,非 UTC+8 用户在本机午夜附近会因剩余毫秒抖动换出两个 Key。
        var endUtc = new DateTime(2026, 10, 29, 20, 0, 0, DateTimeKind.Utc);
        var a = new TowerSeasonExpiry(TowerKind.Slash, TimeSpan.FromHours(1), endUtc.AddSeconds(-30), null);
        var b = new TowerSeasonExpiry(TowerKind.Slash, TimeSpan.FromHours(1), endUtc.AddSeconds(30), null);
        Assert.Equal("tower:Slash:20261030", TowerReminderPolicy.ReminderKey(a));
        Assert.Equal(TowerReminderPolicy.ReminderKey(a), TowerReminderPolicy.ReminderKey(b));
    }
}
