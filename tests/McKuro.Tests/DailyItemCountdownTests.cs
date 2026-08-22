using FluentIcons.Common;
using McKuro.ViewModels;

namespace McKuro.Tests;

/// <summary>首页每日数据「预计满」倒计时规则(体力/结晶单质,每 6 分钟恢复 1 点)。</summary>
public class DailyItemCountdownTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0);

    private static DailyItem Make(int cur, int total, int secondsPerPoint = 0, DailyItem? gate = null)
        => new()
        {
            Icon = Icon.Flash,
            Name = "体力",
            ValueText = $"{cur}/{total}",
            Cur = cur,
            Total = total,
            RecoverSecondsPerPoint = secondsPerPoint,
            Gate = gate,
            LoadedAt = T0,
        };

    [Fact]
    public void RemainingSeconds_ComputesFromCurAndRate()
    {
        // 240-100=140 点 × 360s = 50400s = 14 小时
        Assert.Equal(50400, DailyItem.RemainingSeconds(100, 240, 360, TimeSpan.Zero));
    }

    [Fact]
    public void RemainingSeconds_SubtractsElapsed()
    {
        Assert.Equal(49800, DailyItem.RemainingSeconds(100, 240, 360, TimeSpan.FromSeconds(600)));
    }

    [Theory]
    [InlineData(240, 240, 360, 0)] // 已满
    [InlineData(100, 0, 360, 0)]   // 无总量
    [InlineData(100, 240, 0, 0)]   // 不恢复
    [InlineData(239, 240, 360, 400)] // 剩余 360s 已过 400s(算尽)
    public void RemainingSeconds_ReturnsNullWhenNoCountdown(int cur, int total, int rate, int elapsedSec)
    {
        Assert.Null(DailyItem.RemainingSeconds(cur, total, rate, TimeSpan.FromSeconds(elapsedSec)));
    }

    [Fact]
    public void IsFullAt_ChecksRecoveredPoints()
    {
        var item = Make(100, 140, 360);
        Assert.False(DailyItem.IsFullAt(item, TimeSpan.Zero));
        // 40 分钟后恢复 6 点 → 106 < 140,未满
        Assert.False(DailyItem.IsFullAt(item, TimeSpan.FromMinutes(40)));
        // 4 小时后恢复 40 点 → 满
        Assert.True(DailyItem.IsFullAt(item, TimeSpan.FromHours(4)));
    }

    [Fact]
    public void IsFullAt_FullOrNoTotal()
    {
        Assert.True(DailyItem.IsFullAt(Make(240, 240), TimeSpan.Zero));
        Assert.False(DailyItem.IsFullAt(Make(10, 0), TimeSpan.Zero));
    }

    [Theory]
    [InlineData(50400, "14:00:00")]
    [InlineData(46800, "13:00:00")]
    [InlineData(5050, "1:24:10")]
    [InlineData(3600, "1:00:00")]
    [InlineData(1450, "24:10")]
    public void FormatCountdown_FormatsHoursOrMinutes(long seconds, string expected)
    {
        Assert.Equal(expected, DailyItem.FormatCountdown(seconds));
    }

    [Fact]
    public void TickClock_ShowsCountdownText()
    {
        var item = Make(100, 240, 360);
        item.TickClock(T0.AddHours(1)); // 140 点 × 360s - 3600s = 13h
        Assert.Equal("预计 13:00:00 后满", item.CountdownText);
    }

    [Fact]
    public void TickClock_HidesWhenFullOrExhausted()
    {
        var full = Make(240, 240, 360);
        full.TickClock(T0);
        Assert.Null(full.CountdownText);

        var exhausted = Make(239, 240, 360);
        exhausted.TickClock(T0.AddSeconds(371));
        Assert.Null(exhausted.CountdownText);

        var noRegen = Make(100, 240);
        noRegen.TickClock(T0);
        Assert.Null(noRegen.CountdownText);
    }

    [Fact]
    public void TickClock_GatedItem_HiddenWhileGateNotFull()
    {
        // 结晶单质:体力未满(240)时不启动倒计时
        var gate = Make(100, 240, 360);
        var crystal = Make(100, 480, 360, gate);
        crystal.TickClock(T0);
        Assert.Null(crystal.CountdownText);
    }

    [Fact]
    public void TickClock_GatedItem_StartsWhenGateFills()
    {
        var gate = Make(240, 240, 360);
        var crystal = Make(100, 480, 360, gate);
        crystal.TickClock(T0);
        Assert.NotNull(crystal.CountdownText);
        // 380 点 × 360s = 38 小时
        Assert.Equal("预计 38:00:00 后满", crystal.CountdownText);
    }

    [Fact]
    public void TickClock_GatedItem_GateOpensOverTime()
    {
        // 体力 200/240,4 小时后恢复 40 点即满,结晶单质倒计时才启动
        var gate = Make(200, 240, 360);
        var crystal = Make(100, 480, 360, gate);
        crystal.TickClock(T0.AddHours(2)); // 200+20=220 <240,仍未满
        Assert.Null(crystal.CountdownText);
        crystal.TickClock(T0.AddHours(4)); // 200+40=240,已满 → 启动(从门控开启时刻起算)
        Assert.Equal("预计 38:00:00 后满", crystal.CountdownText);
    }
}
