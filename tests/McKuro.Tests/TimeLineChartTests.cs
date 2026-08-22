using McKuro.Controls;

namespace McKuro.Tests;

/// <summary>
/// 每日抽数面积图纵轴刻度:整数步长(0 起步,上限 ≥ max,最多 6 个刻度),
/// 参照"调用趋势图"的 0/3/6/9/12/15 样式。
/// </summary>
public class TimeLineChartTests
{
    [Fact]
    public void NiceTicks_Max13_Uses_Step3()
    {
        Assert.Equal(new[] { 0, 3, 6, 9, 12, 15 }, TimeLineChart.NiceTicks(13));
    }

    [Fact]
    public void NiceTicks_Max150_Uses_Step30()
    {
        Assert.Equal(new[] { 0, 30, 60, 90, 120, 150 }, TimeLineChart.NiceTicks(150));
    }

    [Fact]
    public void NiceTicks_Max3_Uses_Step1()
    {
        Assert.Equal(new[] { 0, 1, 2, 3 }, TimeLineChart.NiceTicks(3));
    }

    [Fact]
    public void NiceTicks_NonPositive_Keeps_Default_Scale()
    {
        Assert.Equal(new[] { 0, 1, 2, 3 }, TimeLineChart.NiceTicks(0));
        Assert.Equal(new[] { 0, 1, 2, 3 }, TimeLineChart.NiceTicks(-5));
    }

    [Theory]
    [InlineData(1, new[] { 0, 1 })]
    [InlineData(2, new[] { 0, 1, 2 })]
    [InlineData(5, new[] { 0, 1, 2, 3, 4, 5 })]
    [InlineData(30, new[] { 0, 6, 12, 18, 24, 30 })]
    [InlineData(737, new[] { 0, 150, 300, 450, 600, 750 })]
    [InlineData(1200, new[] { 0, 250, 500, 750, 1000, 1250 })]
    public void NiceTicks_Resolution_Covers_Max(int max, int[] expected)
    {
        var ticks = TimeLineChart.NiceTicks(max);
        Assert.Equal(expected, ticks);
        Assert.True(ticks[^1] >= max, "刻度上限应 ≥ 数据最大值");
        Assert.True(ticks.Count <= 7, "刻度数量不宜过多");
    }
}
