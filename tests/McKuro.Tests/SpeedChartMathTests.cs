using McKuro.Services;

namespace McKuro.Tests;

/// <summary>
/// 下载速度曲线纵轴治理的测试(用户需求:网速波动要做自适应放大,同时曲线要能看出抖动)。
/// <para>
/// 回归背景(2026-10):旧控件纵轴取 <c>max(span*1.25, max*0.1)</c> 且以峰值为锚,
/// 一段 2→8MB/s 的波动被压成几乎一条直线;这些用例把"波动必须填满画面、稳定时不得放大噪声、
/// 不得出现负速度区间"三条不变量钉住。
/// </para>
/// </summary>
public class SpeedChartMathTests
{
    // ---------- 区间 ----------

    [Fact]
    public void Range_EmptyOrNull_Returns_Zeros()
    {
        Assert.Equal((0.0, 0.0), SpeedChartMath.Range(null));
        Assert.Equal((0.0, 0.0), SpeedChartMath.Range([]));
    }

    [Fact]
    public void Range_Sanitizes_NegativeAndNonFinite()
    {
        // 冷启动/暂停回填可能塞进 0、负数、NaN:它们不该拉低极小值,也不该拉高极大值
        var (min, max) = SpeedChartMath.Range([-3, double.NaN, 5, double.PositiveInfinity, 2]);
        Assert.Equal(0, min);
        Assert.Equal(5, max);
    }

    [Fact]
    public void Peak_Is_MaxOfSanitizedValues()
    {
        Assert.Equal(0, SpeedChartMath.Peak([]));
        Assert.Equal(7.5, SpeedChartMath.Peak([0, 7.5, 3]));
    }

    // ---------- 自适应放大 ----------

    [Fact]
    public void Bounds_EmptyCollection_Returns_MinimalWindow()
    {
        var b = SpeedChartMath.ComputeBounds([]);
        Assert.Equal(0, b.Bottom);
        Assert.Equal(SpeedChartMath.MinVisualSpanMbps, b.Top);
        Assert.True(b.IsUsable);
    }

    [Fact]
    public void Bounds_LargeFluctuation_FillsMostOfCanvas()
    {
        // 2→8MB/s 的波动:窗口应约为波动幅度 × FluctuationGain = 8.1,
        // 于是这段数据的占比 (8-2)/8.1 ≈ 74% —— 远高于旧实现(窗口 max 10,占比 60% 但锚点错位后只剩 ~20%)
        var values = new[] { 2.0, 8.0, 2.0 };
        var b = SpeedChartMath.ComputeBounds(values);

        var spanned = (b.Ratio(8.0) - b.Ratio(2.0));
        Assert.InRange(spanned, 0.70, 0.80);
        Assert.InRange(b.Span, 8.0, 8.2);
    }

    [Fact]
    public void Bounds_StableSpeed_DoesNotBlowUpNoise()
    {
        // 稳定在 30MB/s、抖动只有 ±0.3:放大上限 = 峰值 30.3 × NoiseFloorRatio(0.25) = 7.575,
        // 于是 0.6 的抖动只占 8% 画布高度 —— 是"轻微起伏",而不是满屏锯齿。
        var values = new[] { 29.7, 30.0, 30.3, 30.0, 29.8 };
        var b = SpeedChartMath.ComputeBounds(values);

        var spanned = b.Ratio(30.3) - b.Ratio(29.7);
        Assert.InRange(spanned, 0.05, 0.15);
        Assert.InRange(b.Span, 7.5, 7.7);
    }

    [Fact]
    public void Bounds_AllZero_Stays_Usable()
    {
        // 暂停/刚开始:全 0 时不能算出 0 高度窗口(除零 → NaN 坐标 → 画不出来)
        var b = SpeedChartMath.ComputeBounds([0, 0, 0]);
        Assert.True(b.IsUsable);
        Assert.Equal(0, b.Bottom);
        Assert.True(b.Top > 0);
    }

    [Fact]
    public void Bounds_Never_Produces_NegativeBottom()
    {
        // 数据整体很低(0~0.05MB/s)时,以中点为中心的窗口会下探到负数 → 必须整体上移,
        // 且上移后高度不变(仍能完整容纳数据)
        var b = SpeedChartMath.ComputeBounds([0, 0.05]);
        Assert.Equal(0, b.Bottom);
        Assert.InRange(b.Top, 0.09, 0.11);
        Assert.InRange(b.Ratio(0.05) - b.Ratio(0), 0.3, 0.6);
    }

    [Fact]
    public void Bounds_Is_Centered_On_Midpoint_When_Possible()
    {
        // 10~20MB/s:窗口以 15 为中心 → 上下各留一半
        var b = SpeedChartMath.ComputeBounds([10.0, 20.0]);
        Assert.Equal(15.0, (b.Bottom + b.Top) / 2, 3);
    }

    [Fact]
    public void Ratio_Clamps_OutOfWindow_Values()
    {
        var b = new SpeedChartBounds(0, 10);
        Assert.Equal(0, b.Ratio(-100));
        Assert.Equal(1, b.Ratio(100));
        Assert.Equal(0.5, b.Ratio(5), 6);
    }

    [Fact]
    public void Ratio_DegenerateBounds_Returns_Zero_Instead_Of_NaN()
    {
        var b = new SpeedChartBounds(5, 5);
        Assert.False(b.IsUsable);
        Assert.Equal(0, b.Ratio(5));
    }

    // ---------- 平滑 ----------

    [Fact]
    public void Smooth_EmptyOrNull_Returns_Empty()
    {
        Assert.Empty(SpeedChartMath.Smooth(null));
        Assert.Empty(SpeedChartMath.Smooth([]));
    }

    [Fact]
    public void Smooth_SigmaZero_JustSanitizes()
    {
        var result = SpeedChartMath.Smooth([1, -2, double.NaN, 4], sigma: 0);
        Assert.Equal([1.0, 0.0, 0.0, 4.0], result);
    }

    [Fact]
    public void Smooth_KeepsPeakAboveNeighbours()
    {
        // 窄峰 (0,10,0) 经高斯平滑后仍应是峰:中心 > 两侧。
        // 这是「不能用等权移动平均」的回归点 —— 等权在边界截断会算成 (5,3.33,5),
        // 把峰掏成一个凹口,曲线看上去就像"网速突然掉到底"。
        var result = SpeedChartMath.Smooth([0, 10, 0], SpeedChartMath.DefaultSigma);
        Assert.Equal(3, result.Length);
        Assert.True(result[1] > result[0]);
        Assert.True(result[1] > result[2]);
        Assert.Equal(result[0], result[2], 6);   // 对称
        Assert.InRange(result[1], 2.0, 5.0);     // 被磨圆但没被抹平
    }

    [Fact]
    public void Smooth_PreservesLength_AndConstantSeries()
    {
        var result = SpeedChartMath.Smooth([5, 5, 5, 5, 5]);
        Assert.Equal(5, result.Length);
        Assert.All(result, v => Assert.Equal(5, v, 6));
    }

    [Fact]
    public void Smooth_Edges_UseOnlyExistingSamples()
    {
        // 边界按实际存在的样本归一化:不能把数组外当成 0,否则首尾点会被硬拉向 0
        var result = SpeedChartMath.Smooth([10, 10, 10, 10], sigma: 1.5);
        Assert.All(result, v => Assert.Equal(10, v, 6));

        // 纯下降序列的首点必须仍是最大值(不被数组外的 0 拉低)
        var falling = SpeedChartMath.Smooth([10, 5, 0, 0, 0], sigma: 1.5);
        Assert.True(falling[0] >= falling[1]);
        Assert.True(falling[0] > 5);
    }

    [Fact]
    public void Smooth_SingleSample_Returns_Itself()
    {
        var result = SpeedChartMath.Smooth([7], sigma: 1.5);
        Assert.Single(result);
        Assert.Equal(7, result[0]);
    }

    [Fact]
    public void Smooth_ReducesJaggedness()
    {
        // 交替 (10,0,10,0…) 是最尖的锯齿:平滑后相邻差应显著变小
        var raw = new List<double> { 10, 0, 10, 0, 10, 0, 10, 0, 10, 0, 10, 0 };
        var smoothed = SpeedChartMath.Smooth(raw);
        var rawJitter = Jitter(raw);
        var smoothJitter = Jitter(smoothed);
        Assert.True(smoothJitter < rawJitter * 0.6,
            $"平滑后抖动应显著下降: raw={rawJitter:0.##}, smoothed={smoothJitter:0.##}");

        static double Jitter(IReadOnlyList<double> v)
        {
            var sum = 0.0;
            for (var i = 1; i < v.Count; i++)
            {
                sum += Math.Abs(v[i] - v[i - 1]);
            }
            return sum;
        }
    }

    // ---------- 切线斜率(贝塞尔控制点夹取) ----------

    [Fact]
    public void Slopes_EdgesAreZero()
    {
        var s = SpeedChartMath.Slopes([1, 2, 3]);
        Assert.Equal(0, s[0]);
        Assert.Equal(0, s[^1]);
    }

    [Fact]
    public void Slopes_MiddleIsCentralDifference()
    {
        var s = SpeedChartMath.Slopes([0, 10, 20]);
        Assert.Equal(10, s[1], 6);   // (20-0)/2,未触到 ±3×10=30 的夹取上限
    }

    [Fact]
    public void Slopes_AreClamped_SoCurveStaysNonNegative()
    {
        // 陡降点 0.5 → 0.1:原始斜率 (0.1-2)/2 = -0.95,远小于 -3×0.1 = -0.3,
        // 不夹取的话贝塞尔控制点会把曲线拽到 0 以下(画到基线外边)。
        var s = SpeedChartMath.Slopes([2.0, 0.1, 0.5]);
        Assert.Equal(-0.3, s[1], 6);
    }

    [Fact]
    public void Slopes_AllZero_CannotDivideByZero()
    {
        var s = SpeedChartMath.Slopes([0, 0, 0]);
        Assert.All(s, v => Assert.Equal(0, v, 6));
    }

    [Fact]
    public void Slopes_NegativeValues_DoNotThrow()
    {
        // 回归:调用方曾直接传"屏幕 y 的相反数"(全负数),limit = 3×值 为负,
        // Math.Clamp(raw, -limit, limit) 抛 ArgumentException('x cannot be greater than -x')
        // → 下载卡一出现就崩掉整个进程。夹取半径必须取绝对值。
        var s = SpeedChartMath.Slopes([-100, -50, -120]);
        Assert.Equal(3, s.Length);
        Assert.All(s, v => Assert.True(double.IsFinite(v)));
        Assert.InRange(s[1], -150, 150);
    }

    [Fact]
    public void Slopes_MixedSigns_StayFinite()
    {
        Assert.All(SpeedChartMath.Slopes([5, -5, 5, -5]),
            v => Assert.True(double.IsFinite(v)));
    }

    [Fact]
    public void Slopes_Empty_Returns_Empty()
    {
        Assert.Empty(SpeedChartMath.Slopes(null));
        Assert.Empty(SpeedChartMath.Slopes([]));
    }

    // ---------- 与真实波形的联调口径 ----------

    [Fact]
    public void Realistic_Jittery_Wave_Uses_MostOfTheHeight()
    {
        // 一段"网速在 8~16MB/s 之间抖"的真实形态:自适应后应占满 70%+ 画布
        var values = new List<double>();
        for (var i = 0; i < 60; i++)
        {
            values.Add(12 + Math.Sin(i * 0.4) * 3 + Math.Sin(i * 0.13) * 1.5);
        }
        var smoothed = SpeedChartMath.Smooth(values);
        var b = SpeedChartMath.ComputeBounds(smoothed);

        var min = smoothed.Min();
        var max = smoothed.Max();
        Assert.InRange(b.Ratio(max) - b.Ratio(min), 0.65, 0.85);
    }
}
