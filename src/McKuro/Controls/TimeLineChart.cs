using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using McKuro.Core.Models.Gacha;

namespace McKuro.Controls;

/// <summary>
/// 每日抽数平滑面积图(参考调用趋势图):Catmull-Rom 平滑曲线 + 渐变填充,
/// 左侧整数单位刻度(0/3/6/9…自适应),横向网格线,底部稀疏日期标签。
/// 鼠标悬停显示日期/卡池/抽数悬浮卡。自绘实现(AOT 安全,不依赖图表库)。
/// </summary>
public sealed class TimeLineChart : Control
{
    public static readonly StyledProperty<IReadOnlyList<int>?> ValuesProperty =
        AvaloniaProperty.Register<TimeLineChart, IReadOnlyList<int>?>(nameof(Values));

    public static readonly StyledProperty<IReadOnlyList<string>?> LabelsProperty =
        AvaloniaProperty.Register<TimeLineChart, IReadOnlyList<string>?>(nameof(Labels));

    /// <summary>悬浮提示文案(每点一个,多行用 '\n' 分隔;为空时按日期+抽数兜底)。</summary>
    public static readonly StyledProperty<IReadOnlyList<string>?> TipsProperty =
        AvaloniaProperty.Register<TimeLineChart, IReadOnlyList<string>?>(nameof(Tips));

    /// <summary>每日抽数(旧→新)。</summary>
    public IReadOnlyList<int>? Values
    {
        get => GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    /// <summary>日期标签(如 "05-29",与 Values 一一对应;为空时不画日期)。</summary>
    public IReadOnlyList<string>? Labels
    {
        get => GetValue(LabelsProperty);
        set => SetValue(LabelsProperty, value);
    }

    /// <summary>悬浮提示文案(与 Values 一一对应,多行用 '\n' 分隔)。</summary>
    public IReadOnlyList<string>? Tips
    {
        get => GetValue(TipsProperty);
        set => SetValue(TipsProperty, value);
    }

    /// <summary>把某日抽数格式化为悬浮文案:第一行日期,之后每行一个卡池,多卡池时末尾汇总。</summary>
    public static string BuildTip(DailyPull day)
    {
        var sb = new StringBuilder();
        sb.Append(day.Date.ToString("yyyy-MM-dd"));
        if (day.Pools.Count == 0)
        {
            sb.Append('\n').Append("共 ").Append(day.Count).Append(" 抽");
        }
        else
        {
            foreach (var p in day.Pools)
            {
                sb.Append('\n').Append(p.PoolName).Append(" ×").Append(p.Count);
            }
            if (day.Pools.Count > 1)
            {
                sb.Append('\n').Append("共 ").Append(day.Count).Append(" 抽");
            }
        }
        return sb.ToString();
    }

    // ---- 配色(对齐抽卡页浅色底 + 品牌蓝) ----
    private static readonly IBrush CurveBrush = new SolidColorBrush(Color.Parse("#1677FF"));
    private static readonly IBrush GridBrush = new SolidColorBrush(Color.Parse("#E5E6EB"));
    private static readonly IBrush AxisTextBrush = new SolidColorBrush(Color.Parse("#86909C"));
    private static readonly IBrush AxisDateBrush = new SolidColorBrush(Color.Parse("#C9CDD4"));
    private static readonly IBrush EmptyBrush = new SolidColorBrush(Color.Parse("#C9CDD4"));
    private static readonly IBrush FillBrush = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Color.Parse("#661677FF"), 0),
            new GradientStop(Color.Parse("#051677FF"), 1),
        },
    };

    private static readonly int[] NiceSteps =
        [1, 2, 3, 5, 6, 8, 10, 15, 20, 25, 30, 40, 50, 60, 80, 100, 125, 150, 200, 250, 300, 400, 500, 600, 800, 1000];

    /// <summary>
    /// 生成纵轴整数刻度(0 起步,上限 ≥ max,间隔取"整"数):如 13 → 0/3/6/9/12/15。
    /// 最多 6 个刻度;max ≤ 0 时返回 0/1/2/3(空数据也保持刻度可见)。
    /// </summary>
    public static IReadOnlyList<int> NiceTicks(int max)
    {
        if (max <= 0)
        {
            return [0, 1, 2, 3];
        }
        var rough = Math.Max(1, (int)Math.Ceiling(max / 5.0));
        var scale = 1;
        while (rough > 1000)
        {
            rough = (int)Math.Ceiling(rough / 10.0);
            scale *= 10;
        }
        var step = NiceSteps[0];
        foreach (var s in NiceSteps)
        {
            if (s >= rough)
            {
                step = s;
                break;
            }
        }
        step *= scale;
        var top = (int)Math.Ceiling(max / (double)step) * step;
        var ticks = new List<int>();
        for (var v = 0; v <= top; v += step)
        {
            ticks.Add(v);
        }
        return ticks;
    }

    static TimeLineChart()
    {
        AffectsRender<TimeLineChart>(ValuesProperty, LabelsProperty, TipsProperty);
        ClipToBoundsProperty.OverrideDefaultValue<TimeLineChart>(true);
        ValuesProperty.Changed.AddClassHandler<TimeLineChart>((o, _) => o.OnDataChanged());
        LabelsProperty.Changed.AddClassHandler<TimeLineChart>((o, _) => o.OnDataChanged());
        TipsProperty.Changed.AddClassHandler<TimeLineChart>((o, _) => o.OnDataChanged());
    }

    private void OnDataChanged()
    {
        Subscribe(Values);
        Subscribe(Labels);
        Subscribe(Tips);
        _hoverIndex = -1;
        Dispatcher.UIThread.Post(InvalidateVisual);
    }

    private void Subscribe(IEnumerable? collection)
    {
        if (collection is INotifyCollectionChanged oc)
        {
            oc.CollectionChanged += (_, _) => Dispatcher.UIThread.Post(InvalidateVisual);
        }
    }

    private static readonly Typeface LabelTypeface = new(FontFamily.Default);

    private static FormattedText Text(string text, double fontSize, IBrush brush, bool bold = false)
    {
        var typeface = bold
            ? new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold)
            : LabelTypeface;
        return new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, fontSize, brush);
    }

    // ---- 悬浮提示状态 ----
    private static readonly IBrush TipBgBrush = new SolidColorBrush(Color.FromArgb(235, 24, 30, 42));
    private static readonly IBrush TipTextBrush = Brushes.White;
    private int _hoverIndex = -1;

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var idx = IndexAt(e.GetPosition(this).X);
        if (idx != _hoverIndex)
        {
            _hoverIndex = idx;
            InvalidateVisual();
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_hoverIndex != -1)
        {
            _hoverIndex = -1;
            InvalidateVisual();
        }
    }

    /// <summary>按 X 坐标找最近的数据点索引(不在绘图区内返回 -1)。</summary>
    private int IndexAt(double px)
    {
        var values = Values;
        if (values is null || values.Count == 0 || px < 30 || px > Bounds.Width - 4)
        {
            return -1;
        }
        var n = values.Count;
        if (n == 1)
        {
            return 0;
        }
        var plotWidth = Math.Max(1, Bounds.Width - 36 - 8);
        var idx = (int)Math.Round((px - 36) / (plotWidth / (n - 1)));
        return idx >= 0 && idx < n ? idx : -1;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width <= 2 || height <= 2)
        {
            return;
        }

        // 布局:左侧单位刻度栏 + 绘图区,底部日期带
        const double axisLeft = 36;
        const double plotTop = 12;
        const double dateBand = 30;
        var plotBottom = Math.Max(plotTop + 10, height - dateBand);
        var plotWidth = Math.Max(1, width - axisLeft - 8);
        var plotHeight = plotBottom - plotTop;

        var values = Values;
        if (values is null || values.Count == 0)
        {
            var empty = Text("暂无数据", 12, EmptyBrush);
            context.DrawText(empty, new Point((width - empty.Width) / 2, (height - empty.Height) / 2));
            return;
        }

        // 纵轴刻度(整数值)
        var max = 0;
        foreach (var v in values)
        {
            if (v > max)
            {
                max = v;
            }
        }
        var ticks = NiceTicks(max);
        var ceil = ticks[^1];

        double YFor(int v) => plotBottom - v / (double)ceil * plotHeight;

        // 网格线 + 左侧单位刻度
        foreach (var tick in ticks)
        {
            var y = YFor(tick);
            context.DrawLine(new Pen(GridBrush, 1), new Point(axisLeft, y), new Point(width - 8, y));
            var label = Text(tick.ToString(CultureInfo.InvariantCulture), 10, AxisTextBrush);
            var ly = Math.Clamp(y - label.Height / 2, 1, height - label.Height - 1);
            context.DrawText(label, new Point(axisLeft - 6 - label.Width, ly));
        }
        // 底部轴线稍深
        context.DrawLine(new Pen(AxisTextBrush, 1), new Point(axisLeft, plotBottom), new Point(width - 8, plotBottom));

        // 数据点(像素坐标)
        var n = values.Count;
        var points = new List<Point>(n);
        for (var i = 0; i < n; i++)
        {
            var x = n == 1 ? axisLeft + plotWidth / 2 : axisLeft + i * (plotWidth / (n - 1));
            var y = YFor(Math.Clamp(values[i], 0, ceil));
            points.Add(new Point(x, y));
        }

        if (n == 1)
        {
            context.DrawEllipse(CurveBrush, null, points[0], 3, 3);
            return;
        }

        // 平滑曲线(Catmull-Rom → 三次贝塞尔;控制点 Y 限幅防止过冲出界)
        void SmoothFigure(StreamGeometryContext ctx, bool closeToBottom)
        {
            ctx.BeginFigure(points[0], closeToBottom);
            for (var i = 0; i < n - 1; i++)
            {
                var p0 = points[Math.Max(i - 1, 0)];
                var p1 = points[i];
                var p2 = points[i + 1];
                var p3 = points[Math.Min(i + 2, n - 1)];
                var c1x = p1.X + (p2.X - p0.X) / 6;
                var c2x = p2.X - (p3.X - p1.X) / 6;
                var c1y = Math.Clamp(p1.Y + (p2.Y - p0.Y) / 6, plotTop, plotBottom);
                var c2y = Math.Clamp(p2.Y - (p3.Y - p1.Y) / 6, plotTop, plotBottom);
                ctx.CubicBezierTo(new Point(c1x, c1y), new Point(c2x, c2y), p2);
            }
            if (closeToBottom)
            {
                ctx.LineTo(new Point(points[^1].X, plotBottom));
                ctx.LineTo(new Point(points[0].X, plotBottom));
                ctx.EndFigure(true);
            }
            else
            {
                ctx.EndFigure(false);
            }
        }

        var fill = new StreamGeometry();
        using (var g = fill.Open())
        {
            SmoothFigure(g, true);
        }
        context.DrawGeometry(FillBrush, null, fill);

        var line = new StreamGeometry();
        using (var g = line.Open())
        {
            SmoothFigure(g, false);
        }
        context.DrawGeometry(null, new Pen(CurveBrush, 2), line);

        // 底部稀疏日期(最多 6 个,避免拥挤重叠)
        if (Labels is not null && Labels.Count > 0)
        {
            var step = Math.Max(1, (int)Math.Ceiling(n / 6.0));
            for (var i = 0; i < n; i += step)
            {
                var label = Text(Labels[Math.Min(i, Labels.Count - 1)] ?? "", 10, AxisDateBrush);
                var x = Math.Clamp(points[i].X - label.Width / 2, 0, width - label.Width);
                context.DrawText(label, new Point(x, plotBottom + 8));
            }
        }

        // 悬浮提示:标记点 + 日期/卡池/抽数卡片
        if (_hoverIndex >= 0 && _hoverIndex < n)
        {
            var pt = points[_hoverIndex];
            context.DrawEllipse(Brushes.White, new Pen(CurveBrush, 2), pt, 4.5, 4.5);

            var tip = Tips is not null && _hoverIndex < Tips.Count
                ? Tips[_hoverIndex]
                : (Labels is not null && _hoverIndex < Labels.Count
                    ? Labels[_hoverIndex] + "\n" + values[_hoverIndex] + " 抽"
                    : null);
            if (tip is not null)
            {
                var parts = tip.Split('\n');
                var texts = new List<FormattedText>(parts.Length);
                double maxW = 0, totalH = 0;
                for (var li = 0; li < parts.Length; li++)
                {
                    var t = Text(parts[li], 11, TipTextBrush, bold: li == 0);
                    texts.Add(t);
                    maxW = Math.Max(maxW, t.Width);
                    totalH += t.Height + 3;
                }
                totalH -= 3;
                var boxW = maxW + 24;
                var boxH = totalH + 14;
                var bx = Math.Clamp(pt.X - boxW / 2, 6, Math.Max(6, width - boxW - 6));
                var by = pt.Y - boxH - 12;
                if (by < 6)
                {
                    by = pt.Y + 12;
                    if (by > height - boxH - 6)
                    {
                        by = Math.Max(6, height - boxH - 6);
                    }
                }
                context.FillRectangle(TipBgBrush, new Rect(bx, by, boxW, boxH), 6f);
                var ty = by + 7;
                foreach (var t in texts)
                {
                    context.DrawText(t, new Point(bx + 12, ty));
                    ty += t.Height + 3;
                }
            }
        }
    }
}
