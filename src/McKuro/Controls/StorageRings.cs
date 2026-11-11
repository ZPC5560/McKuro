using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace McKuro.Controls;

/// <summary>
/// 多级同心圆占比图(参考 Apple 运动健康的三环)。
/// <para>
/// 每一环 = 一个分类占整体的比例(0-1),从<b>外到内</b>依次为
/// 正式资源 / 预下载 / 日志 / 截图 / 启动器临时文件。
/// 与"一个环切成五段"的饼图相比,同心环的好处是每段都能独立按 360° 读比例,
/// 小占比也不会压成一条看不见的细缝。
/// </para>
/// <para>
/// 画法对齐 Apple 的三条规则:① 轨道用环色的低透明度版本(不是灰色),
/// 于是"用掉了多少"与"还剩多少"是同一种颜色的深浅;② 圆弧从 12 点方向顺时针,
/// 端点圆头;③ 打开时从 0 长到目标值(缓出),让人看清每一环是谁。
/// </para>
/// <para>
/// 环色做成 5 个独立属性而不是一个调色板数组:它们在 XAML 里各绑一个
/// <c>DynamicResource</c>,主题切换时能跟着变;数组属性没法带主题。
/// </para>
/// </summary>
public sealed class StorageRings : Control
{
    /// <summary>各环比例(0-1,按外→内排列)。多余 5 个的项忽略,缺的环不画。</summary>
    public static readonly StyledProperty<IReadOnlyList<double>?> RatiosProperty =
        AvaloniaProperty.Register<StorageRings, IReadOnlyList<double>?>(nameof(Ratios));

    /// <summary>环宽(DIP)。</summary>
    public static readonly StyledProperty<double> RingThicknessProperty =
        AvaloniaProperty.Register<StorageRings, double>(nameof(RingThickness), 13);

    /// <summary>环与环之间的空隙(DIP)。</summary>
    public static readonly StyledProperty<double> RingGapProperty =
        AvaloniaProperty.Register<StorageRings, double>(nameof(RingGap), 5);

    /// <summary>1~5 环的颜色(外→内)。</summary>
    public static readonly StyledProperty<IBrush> Ring1BrushProperty =
        AvaloniaProperty.Register<StorageRings, IBrush>(nameof(Ring1Brush),
            new SolidColorBrush(Color.FromRgb(0x5A, 0x8D, 0xEE)));

    public static readonly StyledProperty<IBrush> Ring2BrushProperty =
        AvaloniaProperty.Register<StorageRings, IBrush>(nameof(Ring2Brush),
            new SolidColorBrush(Color.FromRgb(0x4F, 0xD0, 0xFF)));

    public static readonly StyledProperty<IBrush> Ring3BrushProperty =
        AvaloniaProperty.Register<StorageRings, IBrush>(nameof(Ring3Brush),
            new SolidColorBrush(Color.FromRgb(0xF7, 0xB5, 0x3C)));

    public static readonly StyledProperty<IBrush> Ring4BrushProperty =
        AvaloniaProperty.Register<StorageRings, IBrush>(nameof(Ring4Brush),
            new SolidColorBrush(Color.FromRgb(0xB3, 0x7F, 0xEB)));

    public static readonly StyledProperty<IBrush> Ring5BrushProperty =
        AvaloniaProperty.Register<StorageRings, IBrush>(nameof(Ring5Brush),
            new SolidColorBrush(Color.FromRgb(0x8A, 0x96, 0xA8)));

    /// <summary>
    /// 轨道的不透明度(0-1,只对纯色环生效)。
    /// <para>
    /// 默认 0.26:这是<b>同时适配明暗两套主题</b>的取值。亮色卡片接近纯白,0.20 的浅染几乎看不见
    /// (实测亮色下第 4/5 环的轨道基本消失,用户会以为"只有三环");暗色卡片上 0.26 也仍然
    /// 只是个安静的底环,不会盖过前景弧。
    /// </para>
    /// </summary>
    public static readonly StyledProperty<double> TrackOpacityProperty =
        AvaloniaProperty.Register<StorageRings, double>(nameof(TrackOpacity), 0.26);

    /// <summary>轨道色(渐变环没有"同色调暗"的语义,只能用它)。</summary>
    public static readonly StyledProperty<IBrush> TrackFallbackBrushProperty =
        AvaloniaProperty.Register<StorageRings, IBrush>(nameof(TrackFallbackBrush),
            new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)));

    /// <summary>动画进度(0-1),由控件内部推进;外部不需要设置。</summary>
    public static readonly StyledProperty<double> RevealProgressProperty =
        AvaloniaProperty.Register<StorageRings, double>(nameof(RevealProgress), 1);

    public IReadOnlyList<double>? Ratios
    {
        get => GetValue(RatiosProperty);
        set => SetValue(RatiosProperty, value);
    }

    public double RingThickness
    {
        get => GetValue(RingThicknessProperty);
        set => SetValue(RingThicknessProperty, value);
    }

    public double RingGap
    {
        get => GetValue(RingGapProperty);
        set => SetValue(RingGapProperty, value);
    }

    public IBrush Ring1Brush
    {
        get => GetValue(Ring1BrushProperty);
        set => SetValue(Ring1BrushProperty, value);
    }

    public IBrush Ring2Brush
    {
        get => GetValue(Ring2BrushProperty);
        set => SetValue(Ring2BrushProperty, value);
    }

    public IBrush Ring3Brush
    {
        get => GetValue(Ring3BrushProperty);
        set => SetValue(Ring3BrushProperty, value);
    }

    public IBrush Ring4Brush
    {
        get => GetValue(Ring4BrushProperty);
        set => SetValue(Ring4BrushProperty, value);
    }

    public IBrush Ring5Brush
    {
        get => GetValue(Ring5BrushProperty);
        set => SetValue(Ring5BrushProperty, value);
    }

    /// <summary>轨道不透明度(见 <see cref="TrackOpacityProperty"/>)。</summary>
    public double TrackOpacity
    {
        get => GetValue(TrackOpacityProperty);
        set => SetValue(TrackOpacityProperty, value);
    }

    public IBrush TrackFallbackBrush
    {
        get => GetValue(TrackFallbackBrushProperty);
        set => SetValue(TrackFallbackBrushProperty, value);
    }

    public double RevealProgress
    {
        get => GetValue(RevealProgressProperty);
        set => SetValue(RevealProgressProperty, value);
    }

    static StorageRings()
    {
        AffectsRender<StorageRings>(
            RatiosProperty, RingThicknessProperty, RingGapProperty,
            Ring1BrushProperty, Ring2BrushProperty, Ring3BrushProperty,
            Ring4BrushProperty, Ring5BrushProperty, TrackFallbackBrushProperty,
            TrackOpacityProperty, RevealProgressProperty);
        ClipToBoundsProperty.OverrideDefaultValue<StorageRings>(true);
    }

    /// <summary>同时最多画 5 环(第 6 类以后再细分就换成可滚动图例)。</summary>
    private const int MaxRings = 5;

    /// <summary>打开时从 0 长到目标值,缓出 700ms(Apple 环的入场节奏)。</summary>
    private const double RevealDurationMs = 700;

    private DispatcherTimer? _revealTimer;
    private readonly System.Diagnostics.Stopwatch _revealClock = new();

    // ---------- Render 缓存 ----------
    // 缓存键 = (颜色引用, 是否轨道)。每一帧都 new Pen/StreamGeometry 会在
    // 入场动画的 40 帧里产生上千次分配,而这只是画 5 个圆弧。
    private readonly Dictionary<(object Brush, bool IsTrack, int Alpha), Pen> _penCache = [];

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var ratios = Ratios;
        if (ratios is null || ratios.Count == 0)
        {
            return;
        }

        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var outerRadius = Math.Min(Bounds.Width, Bounds.Height) / 2 - RingThickness / 2;
        if (outerRadius <= 0)
        {
            return;
        }

        var step = RingThickness + RingGap;
        var reveal = Math.Clamp(RevealProgress, 0, 1);
        // 缓出:三次方收尾,起步快、停得稳
        var eased = 1 - Math.Pow(1 - reveal, 3);

        var count = Math.Min(ratios.Count, MaxRings);
        for (var i = 0; i < count; i++)
        {
            var radius = outerRadius - i * step;
            if (radius <= RingThickness / 2)
            {
                break;
            }

            var brush = BrushForOffset(i);

            // 轨道:同色低透明度(不是灰色)-> "已用"与"剩余"是同一种颜色的深浅
            context.DrawEllipse(null, PenFor(brush, track: true), center, radius, radius);

            var ratio = Math.Clamp(ratios[i], 0, 1) * eased;
            if (ratio <= 0)
            {
                continue;
            }

            var sweep = ratio * 360.0;
            if (sweep < 0.3)
            {
                // 极小的占比画不出圆头弧(会退化成一个点),直接跳过
                continue;
            }

            // 满环不用 ArcTo:起终点重合的弧在某些半径下会画不出来,规规矩矩画整圆
            if (sweep >= 359.9)
            {
                context.DrawEllipse(null, PenFor(brush, track: false), center, radius, radius);
                continue;
            }

            const double startAngle = -90.0;
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                g.BeginFigure(PointOnCircle(center, radius, startAngle), false);
                g.ArcTo(
                    PointOnCircle(center, radius, startAngle + sweep),
                    new Size(radius, radius),
                    0,
                    sweep > 180,
                    SweepDirection.Clockwise);
                g.EndFigure(false);
            }
            context.DrawGeometry(null, PenFor(brush, track: false), geometry);
        }
    }

    private IBrush BrushForOffset(int index) => index switch
    {
        0 => Ring1Brush,
        1 => Ring2Brush,
        2 => Ring3Brush,
        3 => Ring4Brush,
        _ => Ring5Brush,
    };

    /// <summary>
    /// 取(或建)画笔。纯色按有无轨道区分:轨道取同色 20% 透明度,
    /// 非纯色(渐变)只能用 <see cref="TrackFallbackBrush"/> —— 渐变本身没法"调暗一点"。
    /// </summary>
    private Pen PenFor(IBrush brush, bool track)
    {
        // 缓存键要带上轨道透明度:同一个环色在两套主题下会给出不同 alpha 的轨道画笔,
        // 只按 (画刷, 是否轨道) 缓存的话,主题切换后仍是旧的透明笔。
        var key = ((object)brush, track, track ? (int)Math.Round(TrackOpacity * 255) : 0);
        if (_penCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        IBrush actual = brush;
        if (track)
        {
            actual = brush is ISolidColorBrush solid
                ? new SolidColorBrush(Color.FromArgb(
                    (byte)Math.Clamp(Math.Round(TrackOpacity * 255), 0, 255),
                    solid.Color.R, solid.Color.G, solid.Color.B))
                : TrackFallbackBrush;
        }

        var pen = new Pen(actual, RingThickness, lineCap: PenLineCap.Round);
        _penCache[key] = pen;
        return pen;
    }

    private static Point PointOnCircle(Point center, double radius, double angleDeg)
    {
        var rad = angleDeg * Math.PI / 180.0;
        return new Point(center.X + radius * Math.Cos(rad), center.Y + radius * Math.Sin(rad));
    }

    // ---------- 入场动画 ----------
    // 只在第一次拿到比例时播一次;换数据(重新扫描)不再重播 ——
    // 用户已经知道哪环是谁,再让它从 0 长一遍只是拖慢读数。
    private bool _revealed;

    /// <summary>数据到来后调用,播放一次从 0 到目标的入场动画。</summary>
    public void PlayReveal()
    {
        if (_revealed)
        {
            RevealProgress = 1;
            return;
        }

        _revealed = true;
        _revealClock.Restart();
        RevealProgress = 0;

        _revealTimer ??= new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16),
        };
        _revealTimer.Tick -= OnRevealTick;
        _revealTimer.Tick += OnRevealTick;
        _revealTimer.Start();
    }

    private void OnRevealTick(object? sender, EventArgs e)
    {
        var progress = _revealClock.Elapsed.TotalMilliseconds / RevealDurationMs;
        if (progress >= 1)
        {
            RevealProgress = 1;
            StopReveal();
            return;
        }

        RevealProgress = progress;
    }

    private void StopReveal()
    {
        _revealTimer?.Stop();
        if (_revealTimer is not null)
        {
            _revealTimer.Tick -= OnRevealTick;
        }
        _revealClock.Stop();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        // 控件离开可视树必须停表,否则对话框关掉之后还在无效重绘
        StopReveal();
        base.OnDetachedFromVisualTree(e);
    }
}
