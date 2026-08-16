using McKuro.Core.Services.Game;
using Xunit;

namespace McKuro.Tests;

/// <summary>滑动窗口速率计测试(注入假时钟,不依赖真实时间)。</summary>
public class SlidingSpeedMeterTests
{
    /// <summary>可推进的假时钟:频率 = 100ns/tick(与 TimeSpan.TicksPerSecond 一致)。</summary>
    private sealed class FakeTimeProvider : TimeProvider
    {
        private long _ticks;

        public override long GetTimestamp() => _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public void Advance(TimeSpan t) => _ticks += t.Ticks;
    }

    private static (SlidingSpeedMeter Meter, FakeTimeProvider Clock) Create(
        TimeSpan? window = null, TimeSpan? interval = null)
    {
        var clock = new FakeTimeProvider();
        return (new SlidingSpeedMeter(window, interval, clock), clock);
    }

    [Fact]
    public void Fewer_Than_Two_Samples_Reports_Zero()
    {
        var (meter, _) = Create();
        meter.Add(0);
        Assert.Equal(0, meter.BytesPerSecond);
    }

    [Fact]
    public void Computes_Rate_Across_Window()
    {
        var (meter, clock) = Create(interval: TimeSpan.FromSeconds(1));

        // 模拟:每秒累计 1024 字节
        meter.Add(0);
        clock.Advance(TimeSpan.FromSeconds(1));
        meter.Add(1024);
        clock.Advance(TimeSpan.FromSeconds(1));
        meter.Add(2048);
        clock.Advance(TimeSpan.FromSeconds(1));
        meter.Add(3072);

        // 3 秒窗口内: (3072 - 0) / 3s = 1024 B/s
        Assert.Equal(1024, meter.BytesPerSecond, precision: 1);
    }

    [Fact]
    public void Rate_Reflects_Current_Speed_Not_Average()
    {
        var (meter, clock) = Create(window: TimeSpan.FromSeconds(3), interval: TimeSpan.FromSeconds(1));

        // 前 2 秒慢(1024 B/s),后 2 秒快(5120 B/s)
        meter.Add(0);
        clock.Advance(TimeSpan.FromSeconds(1));
        meter.Add(1024);
        clock.Advance(TimeSpan.FromSeconds(1));
        meter.Add(2048);
        clock.Advance(TimeSpan.FromSeconds(1));
        meter.Add(7168);
        clock.Advance(TimeSpan.FromSeconds(1));
        meter.Add(12288);

        // t=4s,窗口 3s → 淘汰 t<1s 的采样,保留 t=1..4
        // 窗口内速率 = (12288 - 1024) / 3s ≈ 3754.7 B/s
        // 全程平均是 3072 —— 滑动窗口更接近当前速度(5120)
        Assert.Equal(3754.7, meter.BytesPerSecond, precision: 1);
        Assert.True(meter.BytesPerSecond > 3072, "窗口速率应高于全程平均,体现当前速度");
    }

    [Fact]
    public void Old_Samples_Drop_Out_Of_Window()
    {
        var (meter, clock) = Create(window: TimeSpan.FromSeconds(3), interval: TimeSpan.FromSeconds(1));

        // 第 0-4 秒,每 2 秒推进(确保采样间隔触发)
        for (int i = 0; i <= 4; i++)
        {
            meter.Add(i * 2000);
            clock.Advance(TimeSpan.FromSeconds(1));
        }

        // 第 5 秒加入新采样:窗口只保留 2..5 秒的采样
        meter.Add(5 * 2000);

        // 采样点: t=0(0B),1(2000),2(4000),3(6000),4(8000),5(10000);3s 窗口保留 t>=2
        // 窗口内 (10000-4000)/3s = 2000 B/s
        Assert.Equal(2000, meter.BytesPerSecond, precision: 1);
    }

    [Fact]
    public void SubInterval_Updates_Do_Not_Add_Samples()
    {
        var (meter, clock) = Create(interval: TimeSpan.FromSeconds(1));

        meter.Add(0);
        // 500ms 内的更新不应产生新采样
        clock.Advance(TimeSpan.FromMilliseconds(500));
        meter.Add(500);
        clock.Advance(TimeSpan.FromMilliseconds(500));
        meter.Add(1000);

        // 只有 2 个采样点(0s 和 1s)
        Assert.Equal(1000, meter.BytesPerSecond, precision: 1);
    }
}
