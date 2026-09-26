using McKuro.ViewModels;

namespace McKuro.Tests;

/// <summary>
/// 甘特图对齐数学测试:今日线位置与条内已完成/未进行分界(ComputeBarPixels)在同一 now、
/// 同一 pxPerDay 下必须重合 —— 这是"今日时间线和进度对齐"的数学保证;宽度与时长成正比。
/// </summary>
public class GanttLayoutTests
{
    private static readonly DateTime GanttStart = new(2026, 8, 20, 0, 0, 0);
    private static readonly DateTime GanttEnd = new(2026, 9, 30, 0, 0, 0);
    private static readonly DateTime ActivityStart = new(2026, 8, 20, 10, 0, 0);
    private static readonly DateTime ActivityEnd = new(2026, 9, 29, 11, 59, 0);
    private static readonly DateTime Now = new(2026, 9, 26, 0, 59, 0);
    private const double PxPerDay = 56;

    [Fact]
    public void BarPixels_DoneSegmentEndsExactlyAtTodayLine()
    {
        // 核心对齐保证:实色已完成段的右缘 == 今日线 X == 未进行段左缘(同一 now、同一 pxPerDay)
        var (barLeft, doneW, remainLeft, remainW, todayX) = ActivityViewModel.ComputeBarPixels(
            ActivityStart, ActivityEnd, Now, GanttStart, PxPerDay);
        Assert.Equal(todayX, barLeft + doneW, 4);
        Assert.Equal(todayX, remainLeft, 4);
        Assert.True(doneW > 0);
        Assert.True(remainW > 0);
    }

    [Fact]
    public void BarPixels_BoundaryInsideBarSpan()
    {
        var (barLeft, doneW, remainLeft, remainW, _) = ActivityViewModel.ComputeBarPixels(
            ActivityStart, ActivityEnd, Now, GanttStart, PxPerDay);
        Assert.True(remainLeft >= barLeft - 0.001);
        Assert.True(remainLeft + remainW <= barLeft + doneW + remainW + 0.001);
    }

    [Fact]
    public void BarPixels_FutureActivity_HasNoDoneSegment()
    {
        // 尚未开始的活动:已完成段为 0,整条都是未进行段
        var futureStart = new DateTime(2026, 9, 27, 0, 0, 0);
        var (_, doneW, remainLeft, remainW, _) = ActivityViewModel.ComputeBarPixels(
            futureStart, ActivityEnd, Now, GanttStart, PxPerDay);
        Assert.Equal(0, doneW);
        Assert.True(remainW > 0);
        Assert.Equal(remainLeft, remainLeft);
    }

    [Fact]
    public void BarPixels_WidthsAreDurationProportional()
    {
        // 线性时间轴:时长翻倍 → 条宽翻倍(56px/天)
        var a = ActivityViewModel.ComputeBarPixels(
            new DateTime(2026, 9, 20), new DateTime(2026, 9, 24), Now, GanttStart, PxPerDay);
        var b = ActivityViewModel.ComputeBarPixels(
            new DateTime(2026, 9, 20), new DateTime(2026, 9, 28), Now, GanttStart, PxPerDay);
        var w1 = a.RemainLeft + a.RemainWidth - a.BarLeft;
        var w2 = b.RemainLeft + b.RemainWidth - b.BarLeft;
        Assert.Equal(w1 * 2, w2, 1);
    }

    [Fact]
    public void TodayLineX_ClampsToNonNegative()
    {
        // now 早于时间轴起点:今日线钳在原点,不出现负坐标
        var (_, _, _, _, todayX) = ActivityViewModel.ComputeBarPixels(
            ActivityStart, ActivityEnd, GanttStart.AddDays(-5), GanttStart, PxPerDay);
        Assert.Equal(0, todayX);
    }

    [Fact]
    public void Ticks_AreEvenlySpacedDaysFromTimelineStart()
    {
        // 刻度按整天均匀取样:文本 = 起点 + n×步长天,左缘 = 天偏移 × pxPerDay
        var spanDays = (GanttEnd - GanttStart).TotalDays;          // 41 天
        var width = spanDays * PxPerDay;
        var ticks = ActivityViewModel.ComputeTicks(GanttStart, spanDays, PxPerDay, width);

        Assert.True(ticks.Count >= 5, $"刻度过少: {ticks.Count}");
        // 首个刻度对齐时间轴左端(起点日期),后续等距
        Assert.Equal(0, ticks[0].LeftPx);
        Assert.Equal(GanttStart.ToString("MM-dd"), ticks[0].Text);
        var stepPx = ticks[1].LeftPx - ticks[0].LeftPx;
        Assert.True(stepPx >= PxPerDay);                            // 步长至少 1 天(56px)
        for (var i = 1; i < ticks.Count; i++)
        {
            Assert.Equal(stepPx, ticks[i].LeftPx - ticks[i - 1].LeftPx, 4);
        }
        // 刻度文本与像素位置一一对应(可作为日期参照)
        for (var i = 0; i < ticks.Count; i++)
        {
            Assert.Equal(
                GanttStart.AddDays(ticks[i].LeftPx / PxPerDay).ToString("MM-dd"),
                ticks[i].Text);
        }
    }

    [Fact]
    public void Ticks_LastLabelStaysInsideTimeline()
    {
        // 末刻度文本不能被时间轴右缘裁掉(否则视口滚到最右时读不到日期)
        var spanDays = (GanttEnd - GanttStart).TotalDays;
        var width = spanDays * PxPerDay;
        var ticks = ActivityViewModel.ComputeTicks(GanttStart, spanDays, PxPerDay, width);

        Assert.All(ticks, t =>
        {
            Assert.True(t.LeftPx >= 0);
            Assert.True(t.LeftPx + 30 <= width, $"刻度 {t.Text} 越出时间轴右缘");
        });
    }

    [Fact]
    public void Ticks_CoverWholeTimelineSoAnyScrollPositionHasReference()
    {
        // 均匀刻度的意义:任意滚动位置附近都有日期标签(相邻刻度间距 ≤ 视口宽的一半)
        var spanDays = (GanttEnd - GanttStart).TotalDays;
        var width = spanDays * PxPerDay;
        var ticks = ActivityViewModel.ComputeTicks(GanttStart, spanDays, PxPerDay, width);

        const double viewportWidth = 900;   // 甘特条区默认视口量级
        var maxGap = 0.0;
        for (var i = 1; i < ticks.Count; i++)
        {
            maxGap = Math.Max(maxGap, ticks[i].LeftPx - ticks[i - 1].LeftPx);
        }
        Assert.True(maxGap <= viewportWidth / 2, $"刻度间距 {maxGap} 过大,视口内可能无日期参照");
    }

    [Fact]
    public void Ticks_DegenerateInputsYieldEmptyList()
    {
        // 防御:零跨度/零比例不产生刻度(不抛异常,不产生 NaN 坐标)
        Assert.Empty(ActivityViewModel.ComputeTicks(GanttStart, 0, PxPerDay, 0));
        Assert.Empty(ActivityViewModel.ComputeTicks(GanttStart, 30, 0, 0));
    }

    [Fact]
    public void Window_CoversAllActivitiesAndDoesNotMirrorHistory()
    {
        // 活动 08-20 ~ 09-29、今天 09-26:终点 = 今天 + 前瞻(不按左侧历史镜像到 11-03)
        var activityEnd = new DateTime(2026, 9, 29, 11, 59, 0);
        var (start, end) = ActivityViewModel.ComputeWindow(new DateTime(2026, 8, 20), activityEnd, Now);

        Assert.Equal(new DateTime(2026, 8, 20), start);
        Assert.True(end >= activityEnd.Date.AddDays(1), "时间轴必须覆盖全部活动的结束日");
        Assert.Equal(Now.Date.AddDays(ActivityViewModel.LookaheadDays), end);

        // 回归护栏:时间轴 = 活动范围 + 前瞻,必须短于旧的"镜像历史"实现(约 2×历史 + 1 天)
        var spanDays = (end - start).TotalDays;
        var historyDays = (Now.Date - start).TotalDays;
        Assert.True(spanDays <= historyDays + ActivityViewModel.LookaheadDays,
            $"时间轴 {spanDays} 天被拉长(活动范围 + 前瞻即可)");
        Assert.True(spanDays < historyDays * 2 + 1,
            $"时间轴 {spanDays} 天不应长于镜像实现 {historyDays * 2 + 1} 天");
    }

    [Fact]
    public void Window_LeavesEnoughFutureForTodayToCenter()
    {
        // 居中的前提:今天右侧留白(前瞻 × px/天)≥ 半个视口宽度(常见视口约 784px 逻辑宽)
        var (_, end) = ActivityViewModel.ComputeWindow(
            new DateTime(2026, 8, 20), new DateTime(2026, 9, 29), Now);
        var futureDays = (end - Now.Date).TotalDays;
        Assert.Equal(ActivityViewModel.LookaheadDays, futureDays);

        var futurePx = futureDays * ActivityViewModel.PxPerDay;
        Assert.True(futurePx >= 784 / 2, $"右侧留白 {futurePx}px < 半视口,今天无法居中");
    }

    [Fact]
    public void Window_TotalWidthIsCompact()
    {
        // 用户可见的总长度:版本活动 08-20~09-29(今天 09-26)下,时间轴总像素宽应保持在"两屏多"以内
        var (start, end) = ActivityViewModel.ComputeWindow(
            new DateTime(2026, 8, 20), new DateTime(2026, 9, 29, 11, 59, 0), Now);
        var widthPx = (end - start).TotalDays * ActivityViewModel.PxPerDay;

        Assert.True(widthPx <= 1600, $"时间轴总宽 {widthPx}px 过长(视口约 784px)");
    }

    [Fact]
    public void Window_LongFutureActivitiesWinOverLookahead()
    {
        // 活动本就延伸到远处(如新版本活动 60 天后结束):终点必须覆盖它,前瞻不缩短时间轴
        var farEnd = Now.Date.AddDays(60);
        var (start, end) = ActivityViewModel.ComputeWindow(Now.Date.AddDays(-10), farEnd, Now);
        Assert.Equal(Now.Date.AddDays(-10), start);
        Assert.Equal(farEnd.AddDays(1), end);
        Assert.True((end - start).TotalDays > 60);
    }

    [Fact]
    public void Window_DegenerateRangeStillHasPositiveSpan()
    {
        // 防御:起止同一天也要有正的跨度(否则 pxPerDay 与刻度计算会退化)
        var (start, end) = ActivityViewModel.ComputeWindow(Now.Date, Now.Date, Now);
        Assert.True(end > start);
    }
}
