using McKuro.Core.Services.Notification;

namespace McKuro.Tests;

/// <summary>
/// 提醒时间窗判定测试:周本(周一 4 点重置)最后一天、每日活跃度 19 点窗口、
/// 周周期 Key 归属(周一凌晨 0-4 点仍属上一周期)。判定错误会直接产生误报提醒,
/// 因此逐边界覆盖。
/// </summary>
public class ReminderWindowsTests
{
    /// <summary>
    /// 构造<b>服务器挂钟时间</b>(Unspecified = 已是服务器时间,见 <see cref="ReminderWindows.ToServerTime"/>)。
    /// 曾用 Local:周期判定改为按服务器时间后,Local 会按测试机时区换算,非 UTC+8 机器上边界用例会漂移。
    /// </summary>
    private static DateTime At(int year, int month, int day, int hour, int minute = 0)
        => new(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);

    private static DateTime Utc(int year, int month, int day, int hour, int minute = 0)
        => new(year, month, day, hour, minute, 0, DateTimeKind.Utc);

    // ---------- 服务器时间折算(UTC+8) ----------

    [Fact]
    public void ToServerTime_Utc_Adds_Eight_Hours()
    {
        Assert.Equal(new DateTime(2026, 9, 21, 3, 59, 0), ReminderWindows.ToServerTime(Utc(2026, 9, 20, 19, 59)));
        Assert.Equal(DateTimeKind.Unspecified, ReminderWindows.ToServerTime(Utc(2026, 9, 20, 19, 59)).Kind);
    }

    [Fact]
    public void ToServerTime_Unspecified_Is_Already_Server_Time()
    {
        Assert.Equal(At(2026, 9, 21, 3, 59), ReminderWindows.ToServerTime(At(2026, 9, 21, 3, 59)));
    }

    [Fact]
    public void ToServerTime_Local_Matches_Same_Instant_In_Utc()
    {
        // 与测试机时区无关:同一时刻的 Local 与 Utc 表示必须折算出同一个服务器时间
        var utc = Utc(2026, 9, 20, 22, 30);
        Assert.Equal(ReminderWindows.ToServerTime(utc), ReminderWindows.ToServerTime(utc.ToLocalTime()));
    }

    [Fact]
    public void Weekly_Window_Follows_Server_Reset_Not_Utc_Clock()
    {
        // 回归(L16):周一 04:00 服务器时间 = 周日 20:00 UTC。旧实现按传入时刻自身挂钟判定,
        // UTC 时区用户周日 20:00 之后(服务器已重置)仍判为"最后一天",并把 Key 记在上一周期
        Assert.True(ReminderWindows.IsWeeklyLastDay(Utc(2026, 9, 20, 19, 59)));  // 服务器 周一 03:59
        Assert.False(ReminderWindows.IsWeeklyLastDay(Utc(2026, 9, 20, 20, 0)));  // 服务器 周一 04:00
        Assert.Equal("2026-09-14", ReminderWindows.WeekKey(Utc(2026, 9, 20, 19, 59)));
        Assert.Equal("2026-09-21", ReminderWindows.WeekKey(Utc(2026, 9, 20, 20, 0)));
        // 服务器周六 → 不是最后一天(UTC 周六 15:00 = 服务器周六 23:00)
        Assert.False(ReminderWindows.IsWeeklyLastDay(Utc(2026, 9, 19, 15, 0)));
    }

    // ---------- DayKey ----------

    [Fact]
    public void DayKey_Rolls_Over_At_Server_Four_Am()
    {
        Assert.Equal("20260920", ReminderWindows.DayKey(At(2026, 9, 21, 3, 59)));
        Assert.Equal("20260921", ReminderWindows.DayKey(At(2026, 9, 21, 4, 0)));
        Assert.Equal("20260921", ReminderWindows.DayKey(At(2026, 9, 21, 23, 59)));
    }

    [Fact]
    public void DayKey_Is_Stable_Across_Local_Midnight_For_Non_Cn_Timezones()
    {
        // UTC 用户:本机 23:30 与次日 00:30 是两个本机日期,但同属服务器同一游戏日
        // (服务器 07:30 / 08:30),去重 Key 必须相同,否则同一游戏日提醒两次
        Assert.Equal(ReminderWindows.DayKey(Utc(2026, 9, 21, 23, 30)), ReminderWindows.DayKey(Utc(2026, 9, 22, 0, 30)));
    }

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
