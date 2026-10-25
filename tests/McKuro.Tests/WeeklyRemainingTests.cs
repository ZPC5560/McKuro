using McKuro.Core.Models.User;

namespace McKuro.Tests;

/// <summary>
/// 战歌同奏(周本)「剩余次数 → 已完成次数」换算测试。
/// 接口(PC SDK Base.WeeklyInstCount / 数据中心 weeklyInstCount / getData weeklyData.cur)
/// 的原始语义是本周<b>剩余</b>次数(2026-10 实测:打完=0、没打=3),换算错误会同时产生
/// 首页空进度(0/3)与周日误报"还剩 3 次"两类问题,因此逐边界覆盖。
/// </summary>
public class WeeklyRemainingTests
{
    [Theory]
    [InlineData(3, 3, 0)]   // 一次没打:剩余=上限 → 已完成 0
    [InlineData(2, 3, 1)]   // 打了 1 次
    [InlineData(1, 3, 2)]   // 打了 2 次
    [InlineData(0, 3, 3)]   // 全部打完:剩余=0 → 已完成=上限(首页满进度、提醒不再弹)
    public void RemainingToDone_NormalBoundaries(int remaining, int limit, int expectedDone)
    {
        Assert.Equal(expectedDone, UserProfile.WeeklyRemainingToDone(remaining, limit));
    }

    [Fact]
    public void RemainingToDone_ZeroLimit_Uses_Fallback()
    {
        // 上限未知(数据中心 weeklyInstCountLimit=0):回退兜底 3
        Assert.Equal(3, UserProfile.WeeklyRemainingToDone(0, 0));
        Assert.Equal(0, UserProfile.WeeklyRemainingToDone(3, 0));
    }

    [Fact]
    public void RemainingToDone_NegativeRemaining_Clamps_To_Full()
    {
        // 防御:负值(异常数据)视为全部完成
        Assert.Equal(3, UserProfile.WeeklyRemainingToDone(-1, 3));
    }

    [Fact]
    public void RemainingToDone_RemainingExceedsLimit_Clamps_To_Zero()
    {
        // 防御:剩余 > 上限(上限字段与计数不同步)不产生负的已完成
        Assert.Equal(0, UserProfile.WeeklyRemainingToDone(4, 3));
    }

    [Fact]
    public void RemainingToDone_CustomLimit()
    {
        // 数据中心实测出现过 limit=2 的账号:按实际上限换算
        Assert.Equal(1, UserProfile.WeeklyRemainingToDone(1, 2));
        Assert.Equal(2, UserProfile.WeeklyRemainingToDone(0, 2));
    }

    [Fact]
    public void FallbackLimit_Matches_Home_And_Reminder()
    {
        // 兜底上限必须与首页/提醒的历史默认一致,换算口径漂移会导致三处不一致
        Assert.Equal(3, UserProfile.WeeklyFallbackLimit);
    }
}
