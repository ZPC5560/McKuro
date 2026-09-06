using McKuro.Core.Services.Settings;

namespace McKuro.Tests;

/// <summary>每日自动执行时间解析与当日去重判定(供 DailyTaskScheduler 使用)。</summary>
public class DailyAutoRunScheduleTests
{
    [Theory]
    [InlineData("08:00", 8, 0)]
    [InlineData("4:30", 4, 30)]
    [InlineData("22:05", 22, 5)]
    [InlineData("00:00", 0, 0)]
    [InlineData(" 12:30 ", 12, 30)]
    public void TryParseTime_Accepts_HHmm(string text, int hours, int minutes)
    {
        Assert.True(DailyAutoRunSchedule.TryParseTime(text, out var time));
        Assert.Equal(new TimeSpan(hours, minutes, 0), time);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("8")]
    [InlineData("8点")]
    [InlineData("25:00")]
    [InlineData("08:60")]
    [InlineData("-1:00")]
    [InlineData("ab:cd")]
    [InlineData("08:00:00")]
    public void TryParseTime_Rejects_Invalid(string? text)
    {
        Assert.False(DailyAutoRunSchedule.TryParseTime(text, out _));
    }

    [Fact]
    public void ShouldRunNow_Before_Time_Returns_False()
    {
        var now = new DateTime(2026, 9, 6, 7, 0, 0);
        Assert.False(DailyAutoRunSchedule.ShouldRunNow(now, new TimeSpan(8, 0, 0), lastRunDate: ""));
    }

    [Fact]
    public void ShouldRunNow_After_Time_Never_Ran_Returns_True()
    {
        var now = new DateTime(2026, 9, 6, 9, 0, 0);
        Assert.True(DailyAutoRunSchedule.ShouldRunNow(now, new TimeSpan(8, 0, 0), lastRunDate: ""));
    }

    [Fact]
    public void ShouldRunNow_Already_Ran_Today_Returns_False()
    {
        var now = new DateTime(2026, 9, 6, 9, 0, 0);
        Assert.False(DailyAutoRunSchedule.ShouldRunNow(now, new TimeSpan(8, 0, 0), lastRunDate: "2026-09-06"));
    }

    [Fact]
    public void ShouldRunNow_Ran_Yesterday_Before_Today_Time_Returns_False()
    {
        var now = new DateTime(2026, 9, 7, 7, 0, 0);
        Assert.False(DailyAutoRunSchedule.ShouldRunNow(now, new TimeSpan(8, 0, 0), lastRunDate: "2026-09-06"));
    }

    [Fact]
    public void ShouldRunNow_Ran_Yesterday_After_Today_Time_Returns_True()
    {
        var now = new DateTime(2026, 9, 7, 8, 0, 0);
        Assert.True(DailyAutoRunSchedule.ShouldRunNow(now, new TimeSpan(8, 0, 0), lastRunDate: "2026-09-06"));
    }

    [Fact]
    public void DescribeNext_Before_Time_Today()
    {
        var now = new DateTime(2026, 9, 6, 7, 0, 0);
        Assert.Equal("今天 08:00", DailyAutoRunSchedule.DescribeNext(now, new TimeSpan(8, 0, 0), lastRunDate: ""));
    }

    [Fact]
    public void DescribeNext_After_Time_Tomorrow()
    {
        var now = new DateTime(2026, 9, 6, 9, 0, 0);
        Assert.Equal("明天 08:00", DailyAutoRunSchedule.DescribeNext(now, new TimeSpan(8, 0, 0), lastRunDate: ""));
    }

    [Fact]
    public void DescribeNext_Already_Ran_Today()
    {
        var now = new DateTime(2026, 9, 6, 9, 0, 0);
        Assert.Equal("今天 08:00 已执行", DailyAutoRunSchedule.DescribeNext(now, new TimeSpan(8, 0, 0), "2026-09-06"));
    }
}
