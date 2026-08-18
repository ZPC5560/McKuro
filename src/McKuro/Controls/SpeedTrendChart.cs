using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace McKuro.Controls;

/// <summary>
/// 实时下载速度波动曲线(参考 Haiyu 的 DownloadSpeedPoints 波形)。
/// 输入 <see cref="Values"/>(单位 MB/s,每秒一点),自绘折线 + 渐变填充,横轴为最近 N 点、纵轴按最大值自适应。
/// </summary>
public sealed class SpeedTrendChart : Control
{
    public static readonly StyledProperty<IReadOnlyList<double>> ValuesProperty =
        AvaloniaProperty.Register<SpeedTrendChart, IReadOnlyList<double>>(nameof(Values));

    public static readonly StyledProperty<IBrush> LineBrushProperty =
        AvaloniaProperty.Register<SpeedTrendChart, IBrush>(nameof(LineBrush),
            new SolidColorBrush(Color.FromRgb(64, 158, 255)));

    public static readonly StyledProperty<IBrush> FillBrushProperty =
        AvaloniaProperty.Register<SpeedTrendChart, IBrush>(nameof(FillBrush),
            new LinearGradientBrush
            {
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(90, 64, 158, 255), 0),
                    new GradientStop(Color.FromArgb(0, 64, 158, 255), 1),
                },
            });

    public static readonly StyledProperty<int> MaxPointsProperty =
        AvaloniaProperty.Register<SpeedTrendChart, int>(nameof(MaxPoints), 60);

    /// <summary>速度历史值(单位 MB/s,最近在前?最近在后)。</summary>
    public IReadOnlyList<double> Values
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

    /// <summary>保留点数。</summary>
    public int MaxPoints
    {
        get => GetValue(MaxPointsProperty);
        set => SetValue(MaxPointsProperty, value);
    }

    static SpeedTrendChart()
    {
        AffectsRender<SpeedTrendChart>(ValuesProperty, LineBrushProperty, FillBrushProperty, MaxPointsProperty);
        ClipToBoundsProperty.OverrideDefaultValue<SpeedTrendChart>(true);
        // 值集合变化(InotifyPropertyChanged / ObservableCollection)时重绘
        ValuesProperty.Changed.AddClassHandler<SpeedTrendChart>((o, _) => o.OnValuesChanged());
    }

    private void OnValuesChanged()
    {
        if (Values is ObservableCollection<double> oc)
        {
            oc.CollectionChanged += (_, _) => Dispatcher.UIThread.Post(InvalidateVisual);
        }
        Dispatcher.UIThread.Post(InvalidateVisual);
    }

    public SpeedTrendChart()
    {
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width <= 2 || height <= 2 || Values is null || Values.Count < 2)
        {
            return;
        }

        // 纵轴范围:取波动幅度(点集合 max-min),稳定时(波动小)放大显示,让微小上下起伏清晰可见
        var minMbps = double.MaxValue;
        var maxMbps = 0.0;
        foreach (var v in Values)
        {
            if (v < minMbps)
            {
                minMbps = v;
            }
            if (v > maxMbps)
            {
                maxMbps = v;
            }
        }
        if (minMbps == double.MaxValue)
        {
            minMbps = 0;
        }
        var span = maxMbps - minMbps;
        // 范围至少取波动幅度的 1.25 倍,并保证非零;稳定时自动缩小范围以放大波动
        var scale = Math.Max(span * 1.25, maxMbps * 0.1);
        if (scale <= 0)
        {
            scale = 1.0;
        }
        var yBottom = maxMbps - scale * 0.15; // 顶部留 15% margin
        if (yBottom < 0)
        {
            yBottom = 0;
        }
        var yTop = yBottom + scale;

        // 底部留 4px 边距
        var drawHeight = height - 4;
        var n = Values.Count;
        var stepX = width / Math.Max(n - 1, 1);

        var padLeft = 2.0;
        var points = new List<Point>(n);
        for (int i = 0; i < n; i++)
        {
            var x = padLeft + i * stepX;
            // 值映射到 [yBottom, yTop] 范围(波动范围自适应,稳定时放大微小起伏)
            var ratio = Math.Clamp((Values[i] - yBottom) / (yTop - yBottom), 0, 1);
            var y = drawHeight - drawHeight * ratio;
            points.Add(new Point(x, y));
        }

        // 填充区域(曲线到底部闭合)
        if (points.Count >= 2)
        {
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.BeginFigure(points[0], false);
                for (int i = 1; i < points.Count; i++)
                {
                    g.LineTo(points[i]);
                }
                g.LineTo(new Point(points[^1].X, drawHeight));
                g.LineTo(new Point(points[0].X, drawHeight));
                g.EndFigure(true);
            }
            context.DrawGeometry(FillBrush, null, geo);
        }

        // 折线
        var pen = new Pen(LineBrush, 2);
        for (int i = 1; i < points.Count; i++)
        {
            context.DrawLine(pen, points[i - 1], points[i]);
        }
    }
}
