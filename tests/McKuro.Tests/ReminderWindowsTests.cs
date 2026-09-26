using McKuro.Core.Services.Notification;

namespace McKuro.Tests;

/// <summary>
/// 提醒时间窗判定测试:周本(周一 4 点重置)最后一天、每日活跃度 19 点窗口、
/// 周周期 Key 归属(周一凌晨 0-4 点仍属上一周期)。判定错误会直接产生误报提醒,
/// 因此逐边界覆盖。
/// </summary>
public class ReminderWindowsTests
{
    private static DateTime At(int year, int month, int day, int hour, int minute = 0)
        => new(year, month, day, hour, minute, 0, DateTimeKind.Local);

    // ---------- IsWeeklyLastDay ----------

    [Theory]
    [InlineData(2026, 9, 20, 8)]   // 周日白天
    [InlineData(2026, 9, 20, 23)]  // 周日深夜
    public void IsWeeklyLastDay_Sunday_True(int y, int m, int d, int h)
    {
        Assert.True(ReminderWindows.IsWeeklyLastDay(At(y, m, d, h)));
    }

    [Fact]
    public void IsWeeklyLastDay_MondayBeforeFour_True()
    {
        Assert.True(ReminderWindows.IsWeeklyLastDay(At(2026, 9, 21, 3, 59)));
    }

    [Fact]
    public void IsWeeklyLastDay_MondayAtFour_False()
    {
        Assert.False(ReminderWindows.IsWeeklyLastDay(At(2026, 9, 21, 4, 0)));
    }

    [Theory]
    [InlineData(2026, 9, 19)]  // 周六
    [InlineData(2026, 9, 22)]  // 周二
    [InlineData(2026, 9, 25)]  // 周五
    public void IsWeeklyLastDay_OtherDays_False(int y, int m, int d)
    {
        Assert.False(ReminderWindows.IsWeeklyLastDay(At(y, m, d, 12)));
    }

    // ---------- IsLivenessReminderTime ----------

    [Theory]
    [InlineData(18, 59, false)]
    [InlineData(19, 0, true)]
    [InlineData(20, 30, true)]
    [InlineData(23, 59, true)]
    public void IsLivenessReminderTime_Boundary(int hour, int minute, bool expected)
    {
        Assert.Equal(expected, ReminderWindows.IsLivenessReminderTime(At(2026, 9, 25, hour, minute)));
    }

    // ---------- IsDailyProgressDue ----------

    [Fact]
    public void IsDailyProgressDue_AfterNineteen_TrueRegardlessOfWeekday()
    {
        Assert.True(ReminderWindows.IsDailyProgressDue(At(2026, 9, 22, 19)));  // 周二 19 点
    }

    [Fact]
    public void IsDailyProgressDue_SundayDaytime_True()
    {
        Assert.True(ReminderWindows.IsDailyProgressDue(At(2026, 9, 20, 10)));  // 周日 10 点(周本检查)
    }

    [Fact]
    public void IsDailyProgressDue_WeekdayDaytime_False()
    {
        Assert.False(ReminderWindows.IsDailyProgressDue(At(2026, 9, 22, 10))); // 周二 10 点
    }

    // ---------- WeekKey ----------

    [Fact]
    public void WeekKey_Sunday_BelongsToPreviousMonday()
    {
        Assert.Equal("2026-09-21", ReminderWindows.WeekKey(At(2026, 9, 27, 12))); // 周日
    }

    [Fact]
    public void WeekKey_Wednesday_BelongsToSameWeekMonday()
    {
        Assert.Equal("2026-09-21", ReminderWindows.WeekKey(At(2026, 9, 23, 12))); // 周三
    }

    [Fact]
    public void WeekKey_MondayBeforeFour_BelongsToPreviousWeek()
    {
        Assert.Equal("2026-09-14", ReminderWindows.WeekKey(At(2026, 9, 21, 3, 59)));
    }

    [Fact]
    public void WeekKey_MondayAfterFour_BelongsToCurrentWeek()
    {
        Assert.Equal("2026-09-21", ReminderWindows.WeekKey(At(2026, 9, 21, 4, 0)));
    }

    [Fact]
    public void WeekKey_SundayMorningAndMondayNight_Differ()
    {
        // 周日深夜(重置前)与周一 5 点(重置后)必须归属不同周期,否则旧提醒跨周残留
        var sundayNight = ReminderWindows.WeekKey(At(2026, 9, 27, 23, 59));
        var mondayNight = ReminderWindows.WeekKey(At(2026, 9, 28, 5, 0));
        Assert.NotEqual(sundayNight, mondayNight);
    }
}
