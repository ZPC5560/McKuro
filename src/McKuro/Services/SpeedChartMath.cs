using System;
using System.Collections.Generic;

namespace McKuro.Services;

/// <summary>
/// 速度曲线的纵向映射窗口(纯数据,<see cref="Bottom"/>..<see cref="Top"/> 对应画布底部..顶部)。
/// </summary>
public readonly record struct SpeedChartBounds(double Bottom, double Top)
{
    /// <summary>窗口高度(单位 MB/s)。</summary>
    public double Span => Top - Bottom;

    /// <summary>窗口可用(高度为正且有限)。</summary>
    public bool IsUsable => Span > 0 && double.IsFinite(Span) && double.IsFinite(Bottom);

    /// <summary>值 → 0(窗口底)..1(窗口顶);越界夹取。</summary>
    public double Ratio(double value)
        => IsUsable ? Math.Clamp((value - Bottom) / Span, 0, 1) : 0;
}

/// <summary>
/// 下载速度曲线的纵轴治理(纯函数,便于单测)。平滑与曲线几何口径对齐 FlClash v0.9.2 的
/// <c>lib/ui/line_chart.dart</c>(自绘 CustomPainter,不是 fl_chart):高斯 σ=1.5 平滑、
/// Catmull-Rom 切线转三次贝塞尔、斜率夹到 ±3×值。
/// <para>
/// <b>纵轴与 FlClash 有意不同</b>:FlClash 把零线钉在画布底边、按峰值归一
/// (<c>top = max(8192 B/s, 峰值)</c>),所以稳定连接下 ±0.3MB/s 的抖动只占 1% 高度 ——
/// 基本是一条直线。而本项目的用户诉求恰恰是"网速波动要做自适应放大",因此这里改成
/// <b>以数据中点为中心的浮动窗口</b>:上下都可能不包含 0,但窗口高度有上限,
/// 不会把采样噪声画成锯齿。
/// </para>
/// <para>
/// 回归背景(用户反馈 2026-10):旧实现纵轴取 <c>max(span*1.25, max*0.1)</c> 且以峰值为锚 + 上移
/// 15%,一段 2→8MB/s 的波动被挤在画布最下面 15% 里,看上去就是一条贴着底边的直线。
/// </para>
/// <para>规则(三条,可测试):</para>
/// <list type="number">
/// <item><b>波动优先</b>:窗口高度 ≥ 波动幅度 × <see cref="FluctuationGain"/>(1.35)→ 真实波动占 74% 画面。</item>
/// <item><b>放大上限</b>:窗口高度 ≥ 峰值 × <see cref="NoiseFloorRatio"/>(0.25)→ 最多放大 4 倍,
/// 稳定在 30MB/s 时 ±0.3 的抖动只占约 8% 高度,是"轻微起伏"而不是"锯齿风暴"。</item>
/// <item><b>不造负速度</b>:窗口底不越过 0(窗口整体上移,高度不变);以数据中点为中心,
/// 波动大时自然贴近下沿,稳定时落在画面中部。</item>
/// </list>
/// </summary>
public static class SpeedChartMath
{
    /// <summary>窗口最小高度(MB/s):全 0(尚未开始/已暂停)时也要有一个合法窗口。</summary>
    public const double MinVisualSpanMbps = 0.1;

    /// <summary>
    /// 放大上限:窗口高度至少为峰值的这个比例(0.25 = 最多放大 4 倍)。
    /// 越小放得越猛、越容易把采样噪声画成锯齿。
    /// </summary>
    public const double NoiseFloorRatio = 0.25;

    /// <summary>真实波动的增益:波动幅度占窗口高度的 1/1.35 ≈ 74%,上下各留一点呼吸。</summary>
    public const double FluctuationGain = 1.35;

    /// <summary>把非有限值/负值(冷启动、暂停回填等)归零,避免污染极值与窗口。</summary>
    private static double Sanitize(double v) => double.IsFinite(v) && v > 0 ? v : 0;

    /// <summary>窗口内的峰值(空集返回 0)。</summary>
    public static double Peak(IReadOnlyList<double>? values)
    {
        var max = 0.0;
        if (values is null)
        {
            return 0;
        }
        for (var i = 0; i < values.Count; i++)
        {
            var v = Sanitize(values[i]);
            if (v > max)
            {
                max = v;
            }
        }
        return max;
    }

    /// <summary>窗口内的极小/极大值(空集返回 (0,0))。</summary>
    public static (double Min, double Max) Range(IReadOnlyList<double>? values)
    {
        if (values is null || values.Count == 0)
        {
            return (0, 0);
        }
        var min = double.MaxValue;
        var max = 0.0;
        for (var i = 0; i < values.Count; i++)
        {
            var v = Sanitize(values[i]);
            if (v < min)
            {
                min = v;
            }
            if (v > max)
            {
                max = v;
            }
        }
        return (min, max);
    }

    /// <summary>
    /// 计算纵向窗口。<paramref name="values"/> 为单位 MB/s 的最近 N 点(顺序无所谓)。
    /// </summary>
    public static SpeedChartBounds ComputeBounds(IReadOnlyList<double>? values)
    {
        var (min, max) = Range(values);
        if (values is null || values.Count == 0)
        {
            return new SpeedChartBounds(0, MinVisualSpanMbps);
        }

        var span = max - min;
        // 放大器:真实波动优先;稳定时退回"峰值的固定比例"作为下限(这就是自适应放大)。
        var window = Math.Max(
            span * FluctuationGain,
            Math.Max(max * NoiseFloorRatio, MinVisualSpanMbps));

        // 以数据中点为中心 → 波动越大占得越满,稳定时落在中部
        var center = (min + max) / 2;
        var bottom = center - window / 2;
        var top = bottom + window;

        // 不制造"负速度"区域:窗口整体上移,高度保持不变
        if (bottom < 0)
        {
            bottom = 0;
            top = window;
        }
        return new SpeedChartBounds(bottom, top);
    }

    /// <summary>
    /// 高斯平滑(对齐 FlClash <c>LineChart</c> 的 <c>_Series._blur</c>:σ=1.5、半径 = ceil(σ×2.5) = 4,
    /// 边界按实际存在的样本重新归一化)。网络速度是逐字节累计算出来的,原始采样天然带锯齿;
    /// 直接画折线会把"抖动"画成噪声。
    /// <para>
    /// 为什么不用等权移动平均:等权窗口在数组首尾会被截断成两点平均,一个窄峰 (0,10,0)
    /// 会算成 (5,3.33,5) —— 中间反而比两端低,峰值被"掏空"成凹口;高斯核天然中心加权,
    /// 同一组数据算出 (2.03,3.28,2.03),峰仍在,只是被磨圆。
    /// </para>
    /// </summary>
    /// <param name="values">原始采样(单位 MB/s);null/空返回空数组。</param>
    /// <param name="sigma">高斯标准差(点);≤0 表示不平滑,只做脏值清洗。</param>
    public static double[] Smooth(IReadOnlyList<double>? values, double sigma = DefaultSigma)
    {
        if (values is null || values.Count == 0)
        {
            return [];
        }
        var n = values.Count;
        var src = new double[n];
        for (var i = 0; i < n; i++)
        {
            src[i] = Sanitize(values[i]);
        }
        if (sigma <= 0 || n == 1)
        {
            return src;
        }

        var radius = (int)Math.Ceiling(sigma * 2.5);
        // 核只算一次(0..radius);index = |j-i|
        var kernel = new double[radius + 1];
        for (var d = 0; d <= radius; d++)
        {
            kernel[d] = Math.Exp(-(d * d) / (2 * sigma * sigma));
        }

        var dst = new double[n];
        for (var i = 0; i < n; i++)
        {
            var sum = 0.0;
            var weight = 0.0;
            var from = Math.Max(i - radius, 0);
            var to = Math.Min(i + radius, n - 1);
            for (var j = from; j <= to; j++)
            {
                var w = kernel[Math.Abs(j - i)];
                sum += w * src[j];
                weight += w;
            }
            dst[i] = weight > 0 ? sum / weight : src[i];
        }
        return dst;
    }

    /// <summary>默认高斯 σ(与 FlClash 一致:1.5 点)。</summary>
    public const double DefaultSigma = 1.5;

    /// <summary>
    /// Catmull-Rom 切线斜率(对齐 FlClash <c>_Series.slopesOf</c>):首尾取 0,
    /// 中间取 (y[i+1]-y[i-1])/2,并夹到 ±3×|y[i]| —— 后者保证三次贝塞尔的
    /// <b>控制点不会把曲线拽到 0 以下</b>(速度是正量,曲线穿到基线下面观感就废了)。
    /// </summary>
    /// <remarks>
    /// <b>必须用 |y| 作为夹取半径</b>:高度可能为 0(速度全 0 时贴基线),也可能因为调用方
    /// 传进来的是"相对某个基准的高度"而整体平移。<c>Math.Clamp(raw, -limit, limit)</c> 在
    /// limit 为负时会抛 <see cref="ArgumentException"/>(它要求 min ≤ max)——曾经因为直接
    /// 传屏幕 y 的相反数(全负数)导致下载卡一出现就崩整进程。
    /// </remarks>
    public static double[] Slopes(IReadOnlyList<double>? smoothed)
    {
        if (smoothed is null || smoothed.Count == 0)
        {
            return [];
        }
        var n = smoothed.Count;
        var slopes = new double[n];
        for (var i = 0; i < n; i++)
        {
            if (i == 0 || i == n - 1)
            {
                slopes[i] = 0;
                continue;
            }
            var raw = (smoothed[i + 1] - smoothed[i - 1]) / 2.0;
            var limit = 3.0 * Math.Abs(smoothed[i]);
            slopes[i] = limit > 0 ? Math.Clamp(raw, -limit, limit) : 0;
        }
        return slopes;
    }
}
