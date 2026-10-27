using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using McKuro.Services;

namespace McKuro.Controls;

/// <summary>
/// 实时下载速度波动曲线(对齐 FlClash 仪表盘的上传/下载波形)。
/// 输入 <see cref="Values"/>(单位 MB/s),自绘平滑曲线 + 渐变面积 + 端点光点。
/// <para>
/// 纵轴口径由 <see cref="SpeedChartMath.ComputeBounds"/> 决定(纯函数、有单测):
/// 按窗口内<b>真实波动幅度</b>自适应放大,弱网下的细小抖动也能填满画面;
/// 同时对稳定态设放大上限,避免把采样噪声画成锯齿。
/// </para>
/// <para>回归背景(2026-10 用户反馈):旧实现 ① 用直线连点、无平滑;② 纵轴取
/// <c>max(span*1.25, max*0.1)</c> 且以 max 为锚 → 波动大时曲线被压成一条直线;
/// ③ 画在 38px 高的细条里,没有面积填充 → "看不出网速在抖"。</para>
/// </summary>
public sealed class SpeedTrendChart : Control
{
    public static readonly StyledProperty<IReadOnlyList<double>?> ValuesProperty =
        AvaloniaProperty.Register<SpeedTrendChart, IReadOnlyList<double>?>(nameof(Values));

    public static readonly StyledProperty<IBrush> LineBrushProperty =
        AvaloniaProperty.Register<SpeedTrendChart, IBrush>(nameof(LineBrush),
            new SolidColorBrush(Color.FromRgb(64, 158, 255)));

    public static readonly StyledProperty<IBrush> FillBrushProperty =
        AvaloniaProperty.Register<SpeedTrendChart, IBrush>(nameof(FillBrush),
            new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(120, 64, 158, 255), 0),
                    new GradientStop(Color.FromArgb(0, 64, 158, 255), 1),
                },
            });

    public static readonly StyledProperty<int> MaxPointsProperty =
        AvaloniaProperty.Register<SpeedTrendChart, int>(nameof(MaxPoints), 60);

    /// <summary>线宽(DIP)。细曲线在玻璃卡片上会"糊掉",默认给到 2.2。</summary>
    public static readonly StyledProperty<double> LineThicknessProperty =
        AvaloniaProperty.Register<SpeedTrendChart, double>(nameof(LineThickness), 2.2);

    /// <summary>是否在曲线末端画一个当前值光点(一眼看出"现在在哪")。</summary>
    public static readonly StyledProperty<bool> ShowLastPointProperty =
        AvaloniaProperty.Register<SpeedTrendChart, bool>(nameof(ShowLastPoint), true);

    /// <summary>是否画一条水平基线(给曲线一个视觉参照,避免"悬空")。</summary>
    public static readonly StyledProperty<bool> ShowBaselineProperty =
        AvaloniaProperty.Register<SpeedTrendChart, bool>(nameof(ShowBaseline), true);

    public static readonly StyledProperty<IBrush?> BaselineBrushProperty =
        AvaloniaProperty.Register<SpeedTrendChart, IBrush?>(nameof(BaselineBrush));

    /// <summary>速度历史(单位 MB/s,旧 → 新)。</summary>
    public IReadOnlyList<double>? Values
    {
        get => GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    /// <summary>折线颜色。</summary>
    public IBrush LineBrush
    {
        get => GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    /// <summary>曲线下方渐变填充。</summary>
    public IBrush FillBrush
    {
        get => GetValue(FillBrushProperty);
        set => SetValue(FillBrushProperty, value);
    }

    /// <summary>最多使用的点数(超出时只画最近 N 点)。</summary>
    public int MaxPoints
    {
        get => GetValue(MaxPointsProperty);
        set => SetValue(MaxPointsProperty, value);
    }

    public double LineThickness
    {
        get => GetValue(LineThicknessProperty);
        set => SetValue(LineThicknessProperty, value);
    }

    public bool ShowLastPoint
    {
        get => GetValue(ShowLastPointProperty);
        set => SetValue(ShowLastPointProperty, value);
    }

    public bool ShowBaseline
    {
        get => GetValue(ShowBaselineProperty);
        set => SetValue(ShowBaselineProperty, value);
    }

    public IBrush? BaselineBrush
    {
        get => GetValue(BaselineBrushProperty);
        set => SetValue(BaselineBrushProperty, value);
    }

    static SpeedTrendChart()
    {
        AffectsRender<SpeedTrendChart>(
            ValuesProperty, LineBrushProperty, FillBrushProperty, MaxPointsProperty,
            LineThicknessProperty, ShowLastPointProperty, ShowBaselineProperty, BaselineBrushProperty);
        ClipToBoundsProperty.OverrideDefaultValue<SpeedTrendChart>(true);
        ValuesProperty.Changed.AddClassHandler<SpeedTrendChart>((o, _) => o.OnValuesChanged());
    }

    private INotifyCollectionChanged? _observed;

    private void OnValuesChanged()
    {
        // 集合实例换了才重订阅,避免同一集合被多次赋值时叠出一堆事件处理器。
        var collection = Values as INotifyCollectionChanged;
        if (!ReferenceEquals(_observed, collection))
        {
            if (_observed is not null)
            {
                _observed.CollectionChanged -= OnCollectionChanged;
            }
            _observed = collection;
            if (_observed is not null)
            {
                _observed.CollectionChanged += OnCollectionChanged;
            }
        }
        Dispatcher.UIThread.Post(InvalidateVisual);
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => Dispatcher.UIThread.Post(InvalidateVisual);

    private Pen? _linePen;
    private IBrush? _linePenBrush;
    private double _linePenThickness = double.NaN;
    private Pen? _baselinePen;
    private IBrush? _baselinePenBrush;
    private readonly List<Point> _points = new();

    /// <summary>基线默认色:压在玻璃卡片上的一层极淡白线(用户主题不参与,始终"若有若无")。</summary>
    private static readonly IBrush FallbackBaselineBrush = new SolidColorBrush(Color.FromArgb(46, 255, 255, 255));

    /// <summary>
    /// 在画布内平滑曲线时,上下各留的呼吸边距(DIP):曲线与端点光点不会被裁掉。
    /// </summary>
    private const double VerticalPadding = 4.0;

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var width = Bounds.Width;
        var height = Bounds.Height;
        var source = Values;
        if (width <= 2 || height <= 2 || source is null || source.Count == 0)
        {
            return;
        }

        // 只取最近 MaxPoints 点(集合本身也做了裁剪,这里再护一层:外部直接塞长数组也不卡)
        var count = source.Count;
        var take = MaxPoints > 1 ? Math.Min(count, MaxPoints) : count;
        var offset = count - take;
        var raw = new double[take];
        for (var i = 0; i < take; i++)
        {
            raw[i] = source[offset + i];
        }

        var smoothed = SpeedChartMath.Smooth(raw);
        var bounds = SpeedChartMath.ComputeBounds(smoothed);
        var peak = SpeedChartMath.Peak(smoothed);

        var drawHeight = height - VerticalPadding * 2;
        var baselineY = height - VerticalPadding;
        if (drawHeight <= 1)
        {
            return;
        }

        var stepX = take > 1 ? width / (take - 1) : width;

        // 末端光点有半径,若最后一个点正好压在画布右沿会被 ClipToBounds 切掉半边(看起来像一条断线);
        // 贝塞尔控制点也可能轻微超出上下边距。这里左右各让出光晕半径,上下留 VerticalPadding。
        var rightInset = ShowLastPoint ? 5.0 : 0.0;
        var usableWidth = Math.Max(1, width - rightInset);
        stepX = take > 1 ? usableWidth / (take - 1) : usableWidth;

        var points = _points;
        points.Clear();
        points.EnsureCapacity(take);
        for (var i = 0; i < take; i++)
        {
            var x = take > 1 ? i * stepX : Math.Max(0, usableWidth - 1);
            var ratio = bounds.Ratio(smoothed[i]);
            // 峰值为 0(还没开始/已暂停)时贴底画平线,不要悬在中间
            var y = peak <= 0 ? baselineY : baselineY - drawHeight * ratio;
            points.Add(new Point(x, y));
        }

        // ---------- 面积(曲线 → 底边闭合) ----------
        if (points.Count >= 2)
        {
            var area = BuildCurve(points, baselineY, closeToBottom: baselineY);
            context.DrawGeometry(FillBrush, null, area);
        }

        // ---------- 基线(虚线参照线,按输入引用缓存 Pen) ----------
        if (ShowBaseline)
        {
            var brush = BaselineBrush ?? FallbackBaselineBrush;
            if (_baselinePen is null || !ReferenceEquals(_baselinePenBrush, brush))
            {
                _baselinePen = new Pen(brush, 1)
                {
                    DashStyle = new DashStyle([3, 3], 0),
                };
                _baselinePenBrush = brush;
            }
            context.DrawLine(_baselinePen, new Point(0, baselineY - 0.5), new Point(width, baselineY - 0.5));
        }

        // ---------- 曲线 ----------
        if (_linePen is null || !ReferenceEquals(_linePenBrush, LineBrush) || _linePenThickness != LineThickness)
        {
            _linePen = new Pen(LineBrush, LineThickness)
            {
                LineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round,
            };
            _linePenBrush = LineBrush;
            _linePenThickness = LineThickness;
        }
        if (points.Count >= 2)
        {
            context.DrawGeometry(null, _linePen, BuildCurve(points, baselineY, closeToBottom: null));
        }

        // ---------- 末端光点 ----------
        if (ShowLastPoint && points.Count >= 2)
        {
            var last = points[^1];
            // 外圈光晕是线色的淡染;按线色引用缓存,避免每帧新建画刷
            if (_lastPointHalo is null || !ReferenceEquals(_lastPointHaloSource, LineBrush))
            {
                _lastPointHalo = MakeHalo(LineBrush);
                _lastPointHaloSource = LineBrush;
            }
            context.DrawEllipse(_lastPointHalo, null, last, 5.5, 5.5);
            context.DrawEllipse(LineBrush, null, last, 2.6, 2.6);
        }
    }

    private IBrush? _lastPointHalo;
    private IBrush? _lastPointHaloSource;

    /// <summary>线段色的淡染版本(纯色才能算;渐变就退回半透明白)。</summary>
    private static IBrush MakeHalo(IBrush line)
    {
        if (line is ISolidColorBrush { Color: var c })
        {
            return new SolidColorBrush(Color.FromArgb(70, c.R, c.G, c.B));
        }
        return new SolidColorBrush(Color.FromArgb(70, 255, 255, 255));
    }

    /// <summary>
    /// 把点序列转成平滑曲线(Catmull-Rom 切线 → 三次贝塞尔,与 FlClash 的 LineChart 同一套算法),
    /// 可选闭合到基线用于面积填充。
    /// <para>
    /// 切线在「相对基线的高度」空间计算(越高的点数值越大,恒为正),这样
    /// <see cref="SpeedChartMath.Slopes"/> 的 ±3×高度 夹取才有物理意义:控制点不会
    /// 把曲线拽到基线以下。屏幕 y 轴朝下,控制点要按 <c>y - slope/3</c> 反号加上去。
    /// </para>
    /// </summary>
    /// <param name="points">屏幕坐标点列(旧 → 新)。</param>
    /// <param name="heightBase">基线 y(通常就是画布底);用它与点的 y 差得到"离地高度"。</param>
    /// <param name="closeToBottom">非空表示曲线要闭合到该 y 形成面积。</param>
    private static StreamGeometry BuildCurve(List<Point> points, double heightBase, double? closeToBottom)
    {
        var heights = new double[points.Count];
        for (var i = 0; i < points.Count; i++)
        {
            // 离地高度:恒为正,量纲与速度一致;两侧对称的曲线在这里才有正确的峰谷方向
            heights[i] = heightBase - points[i].Y;
        }
        var slopes = SpeedChartMath.Slopes(heights);

        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(points[0], closeToBottom is not null);
            for (var i = 1; i < points.Count; i++)
            {
                var prev = points[i - 1];
                var cur = points[i];
                var dx = (cur.X - prev.X) / 3.0;
                // 高度空间 → 屏幕空间反号:高度上升 = 屏幕上移(y 变小)
                var c1 = new Point(prev.X + dx, prev.Y - slopes[i - 1] / 3.0);
                var c2 = new Point(cur.X - dx, cur.Y + slopes[i] / 3.0);
                g.CubicBezierTo(c1, c2, cur);
            }
            if (closeToBottom is { } bottom)
            {
                g.LineTo(new Point(points[^1].X, bottom));
                g.LineTo(new Point(points[0].X, bottom));
            }
            g.EndFigure(closeToBottom is not null);
        }
        return geo;
    }
}
