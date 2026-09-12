using McKuro.Core.Services.Settings;

namespace McKuro.Tests;

/// <summary>每日自动任务按天去重判定(启动后执行一次;供 DailyTaskScheduler 使用)。</summary>
public class DailyAutoRunScheduleTests
{
    [Fact]
    public void ShouldRunNow_Never_Ran_Returns_True()
    {
        var now = new DateTime(2026, 9, 6, 7, 0, 0);
        Assert.True(DailyAutoRunSchedule.ShouldRunNow(now, lastRunDate: ""));
    }

    [Fact]
    public void ShouldRunNow_Already_Ran_Today_Returns_False()
    {
        var now = new DateTime(2026, 9, 6, 9, 0, 0);
        Assert.False(DailyAutoRunSchedule.ShouldRunNow(now, lastRunDate: "2026-09-06"));
    }

    [Fact]
    public void ShouldRunNow_Ran_Yesterday_Returns_True()
    {
        var now = new DateTime(2026, 9, 7, 8, 0, 0);
        Assert.True(DailyAutoRunSchedule.ShouldRunNow(now, lastRunDate: "2026-09-06"));
    }

    [Fact]
    public void TodayText_Is_Iso_Date()
    {
        Assert.Equal("2026-09-06", DailyAutoRunSchedule.TodayText(new DateTime(2026, 9, 6, 18, 30, 0)));
    }

    [Fact]
    public void HasRunToday_Reflects_Dedup_State()
    {
        var now = new DateTime(2026, 9, 6, 9, 0, 0);
        Assert.False(DailyAutoRunSchedule.HasRunToday(now, lastRunDate: ""));
        Assert.True(DailyAutoRunSchedule.HasRunToday(now, lastRunDate: "2026-09-06"));
    }
}
