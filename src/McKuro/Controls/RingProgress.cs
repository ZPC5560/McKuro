using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace McKuro.Controls;

/// <summary>
/// 环形进度:画整圆轨道 + 按 <see cref="Value"/>(0-100) 从顶部顺时针画弧,
/// 弧长随进度从 0 逐渐增长到完整圆(参考预下载环样式)。中心可选显示百分比文本。
/// </summary>
public sealed class RingProgress : Control
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<RingProgress, double>(nameof(Value));

    public static readonly StyledProperty<double> ThicknessProperty =
        AvaloniaProperty.Register<RingProgress, double>(nameof(Thickness), 5);

    public static readonly StyledProperty<IBrush> TrackBrushProperty =
        AvaloniaProperty.Register<RingProgress, IBrush>(nameof(TrackBrush),
            new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)));

    public static readonly StyledProperty<IBrush> ForegroundBrushProperty =
        AvaloniaProperty.Register<RingProgress, IBrush>(nameof(ForegroundBrush),
            new SolidColorBrush(Color.FromRgb(64, 158, 255)));

    public static readonly StyledProperty<bool> ShowTextProperty =
        AvaloniaProperty.Register<RingProgress, bool>(nameof(ShowText), true);

    public static readonly StyledProperty<double> FontSizeProperty =
        AvaloniaProperty.Register<RingProgress, double>(nameof(FontSize), 14);

    public static readonly StyledProperty<IBrush?> TextBrushProperty =
        AvaloniaProperty.Register<RingProgress, IBrush?>(nameof(TextBrush));

    /// <summary>
    /// 不确定态:不画进度弧与百分比,改为一段绕圈旋转的弧。
    /// <para>
    /// 用于「任务已开始但还不知道总量」的阶段(对齐 Haiyu 1.4.5 的 <c>IsTransferPreparing</c>):
    /// 弱网下「获取资源列表」可能耗时十几秒到几分钟,此时 <see cref="Value"/> 恒为 0,
    /// 若仍画成 0% 的死环,用户会以为程序卡死。
    /// </para>
    /// </summary>
    public static readonly StyledProperty<bool> IsIndeterminateProperty =
        AvaloniaProperty.Register<RingProgress, bool>(nameof(IsIndeterminate));

    /// <summary>进度 0-100。</summary>
    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>环宽。</summary>
    public double Thickness
    {
        get => GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    /// <summary>轨道颜色(底部全圆)。</summary>
    public IBrush TrackBrush
    {
        get => GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    /// <summary>前景弧颜色。</summary>
    public IBrush ForegroundBrush
    {
        get => GetValue(ForegroundBrushProperty);
        set => SetValue(ForegroundBrushProperty, value);
    }

    /// <summary>是否显示中心百分比。</summary>
    public bool ShowText
    {
        get => GetValue(ShowTextProperty);
        set => SetValue(ShowTextProperty, value);
    }

    public double FontSize
    {
        get => GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public IBrush? TextBrush
    {
        get => GetValue(TextBrushProperty);
        set => SetValue(TextBrushProperty, value);
    }

    /// <summary>是否为不确定态(见 <see cref="IsIndeterminateProperty"/>)。</summary>
    public bool IsIndeterminate
    {
        get => GetValue(IsIndeterminateProperty);
        set => SetValue(IsIndeterminateProperty, value);
    }

    static RingProgress()
    {
        AffectsRender<RingProgress>(
            ValueProperty, ThicknessProperty, TrackBrushProperty,
            ForegroundBrushProperty, ShowTextProperty, FontSizeProperty, TextBrushProperty,
            IsIndeterminateProperty);
        ClipToBoundsProperty.OverrideDefaultValue<RingProgress>(true);
    }

    private static readonly Typeface TextTypeface = new(FontFamily.Default);

    // Render 缓存:进度每 ~200ms 重绘一次,Pen/FormattedText 按输入引用缓存,
    // 值不变(尺寸/颜色/文本未变)时零分配。
    private Pen? _trackPen;
    private IBrush? _trackPenBrush;
    private double _trackPenThickness = double.NaN;
    private Pen? _fgPen;
    private IBrush? _fgPenBrush;
    private double _fgPenThickness = double.NaN;
    private FormattedText? _formatted;
    private string? _formattedText;
    private IBrush? _formattedBrush;
    private double _formattedSize = double.NaN;

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var radius = Math.Min(Bounds.Width, Bounds.Height) / 2 - Thickness / 2;
        if (radius < 1)
        {
            return;
        }

        // 轨道整圆
        if (_trackPen is null || !ReferenceEquals(_trackPenBrush, TrackBrush) || _trackPenThickness != Thickness)
        {
            _trackPen = new Pen(TrackBrush, Thickness);
            _trackPenBrush = TrackBrush;
            _trackPenThickness = Thickness;
        }
        context.DrawEllipse(null, _trackPen, center, radius, radius);

        // ---------- 不确定态:绕圈旋转的弧,不显示百分比 ----------
        // 弱网「获取资源列表」阶段总量未知(Value 恒 0),死画一个 0% 环会像卡死。
        if (IsIndeterminate)
        {
            EnsureIndeterminateAnimation();
            if (_fgPen is null || !ReferenceEquals(_fgPenBrush, ForegroundBrush) || _fgPenThickness != Thickness)
            {
                _fgPen = new Pen(ForegroundBrush, Thickness);
                _fgPenBrush = ForegroundBrush;
                _fgPenThickness = Thickness;
            }

            // 固定 90° 弧长,整体绕圈匀速旋转(与轨道同色圆环形成"正在转"的观感)
            const double spinnerSweep = 90.0;
            var spinnerStart = _spinnerAngle;
            var spinnerEnd = spinnerStart + spinnerSweep;
            var spinnerGeo = new StreamGeometry();
            using (var g = spinnerGeo.Open())
            {
                g.BeginFigure(PointOnCircle(center, radius, spinnerStart), false);
                g.ArcTo(
                    PointOnCircle(center, radius, spinnerEnd),
                    new Size(radius, radius),
                    0,
                    spinnerSweep > 180,
                    SweepDirection.Clockwise);
                g.EndFigure(false);
            }
            context.DrawGeometry(null, _fgPen, spinnerGeo);
            return;
        }

        StopIndeterminateAnimation();

        // 前景弧(从顶部 12 点方向顺时针,按 Value 0-100 画弧)
        var value = Math.Clamp(Value, 0, 100);
        if (value <= 0)
        {
            value = 0;
        }
        var sweep = value / 100.0 * 360.0;
        if (sweep < 0.5)
        {
            sweep = 0;
        }

        if (sweep > 0)
        {
            if (_fgPen is null || !ReferenceEquals(_fgPenBrush, ForegroundBrush) || _fgPenThickness != Thickness)
            {
                _fgPen = new Pen(ForegroundBrush, Thickness);
                _fgPenBrush = ForegroundBrush;
                _fgPenThickness = Thickness;
            }
            var fgPen = _fgPen;
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                // 12 点方向为 0 度,顺时针
                var startAngle = -90.0;
                var endAngle = startAngle + sweep;
                var start = PointOnCircle(center, radius, startAngle);
                var end = PointOnCircle(center, radius, endAngle);
                var isLarge = sweep > 180;
                g.BeginFigure(start, false);
                g.ArcTo(end, new Size(radius, radius), 0, isLarge, SweepDirection.Clockwise);
                g.EndFigure(false);
            }
            context.DrawGeometry(null, fgPen, geo);
        }

        // 中心百分比
        if (ShowText)
        {
            var text = $"{value:0}%";
            var brush = TextBrush ?? ForegroundBrush;
            if (_formatted is null || _formattedText != text
                || !ReferenceEquals(_formattedBrush, brush) || _formattedSize != FontSize)
            {
                _formatted = new FormattedText(
                    text, System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight, TextTypeface, FontSize, brush);
                _formattedText = text;
                _formattedBrush = brush;
                _formattedSize = FontSize;
            }
            var formatted = _formatted;
            var textPos = new Point(
                center.X - formatted.Width / 2,
                center.Y - formatted.Height / 2);
            context.DrawText(formatted, textPos);
        }
    }

    private static Point PointOnCircle(Point center, double radius, double angleDeg)
    {
        var rad = angleDeg * Math.PI / 180.0;
        return new Point(
            center.X + radius * Math.Cos(rad),
            center.Y + radius * Math.Sin(rad));
    }

    // ---------- 不确定态旋转驱动 ----------
    // 用一个仅在不确定态期间存活的 DispatcherTimer 推进角度;
    // 离开不确定态必须停掉,否则控件被卸载后仍在无效重绘。
    private DispatcherTimer? _spinnerTimer;
    private double _spinnerAngle = -90;

    /// <summary>每帧旋转角度(60ms/帧 × 6° ≈ 100°/秒,足够醒目又不晃眼)。</summary>
    private const double SpinnerStepDegrees = 6.0;

    private void EnsureIndeterminateAnimation()
    {
        if (_spinnerTimer is not null)
        {
            return;
        }

        _spinnerTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(60),
        };
        _spinnerTimer.Tick += (_, _) =>
        {
            _spinnerAngle += SpinnerStepDegrees;
            if (_spinnerAngle >= 360)
            {
                _spinnerAngle -= 360;
            }
            InvalidateVisual();
        };
        _spinnerTimer.Start();
    }

    private void StopIndeterminateAnimation()
    {
        if (_spinnerTimer is null)
        {
            return;
        }

        _spinnerTimer.Stop();
        _spinnerTimer = null;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        // 控件被移出可视树时停表,避免"看不见还在转"的白耗 CPU。
        StopIndeterminateAnimation();
        base.OnDetachedFromVisualTree(e);
    }
}
