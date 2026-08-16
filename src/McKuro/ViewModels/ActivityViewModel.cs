using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using McKuro.Core.Models.Wiki;
using McKuro.Services;

namespace McKuro.ViewModels;

/// <summary>甘特图活动条目(当前版本活动,过期已剔除)。</summary>
public sealed class ActivityGanttItem
{
    public required string Title { get; init; }
    public required string TimeRangeText { get; init; }  // MM-dd ~ MM-dd
    public required DateTime Start { get; init; }
    public required DateTime End { get; init; }
    /// <summary>甘特横条左侧偏移(占时间轴宽度百分比 0-100)。</summary>
    public required double LeftPercent { get; init; }
    /// <summary>甘特横条宽度(占时间轴宽度百分比)。</summary>
    public required double WidthPercent { get; init; }
    /// <summary>是否正在进行(未结束)。</summary>
    public required bool IsOngoing { get; init; }
    /// <summary>活动主色(从活动图取色;失败回退主题色)。</summary>
    public required Avalonia.Media.IBrush BarBrush { get; init; }
    /// <summary>当前进度(0-100):now 在 [Start,End] 区间的位置;未开始=0,已结束=100。</summary>
    public required double ProgressPercent { get; init; }
}

/// <summary>换取活动(卡池)条目,带倒计时。</summary>
public sealed class ActivityPoolItem
{
    public required string Name { get; init; }
    public required string Category { get; init; }     // 角色 / 武器
    public required string CountdownText { get; init; } // 剩余时间
    public required string TimeRangeText { get; init; }
    public required DateTime End { get; init; }
    public string? ImageUrl { get; init; }
}

/// <summary>
/// 活动页:鸣潮当前版本活动甘特图展示 + 换取活动(卡池)倒计时。
/// 数据源 = 库街区 wiki 接口(hot-content-side 版本活动 / events-side 卡池),参考 Haiyu WavesWikiViewModel。
/// </summary>
public sealed partial class ActivityViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _hasData;

    /// <summary>版本活动(甘特图,过期剔除)。</summary>
    public ObservableCollection<ActivityGanttItem> VersionActivities { get; } = [];

    /// <summary>换取活动(卡池,带倒计时)。</summary>
    public ObservableCollection<ActivityPoolItem> PoolActivities { get; } = [];

    /// <summary>甘特图时间轴起点(最近 14 天前)与终点(14 天后)。</summary>
    public DateTime GanttStart { get; private set; }
    public DateTime GanttEnd { get; private set; }
    public string GanttStartText => GanttStart.ToString("MM-dd");
    public string GanttEndText => GanttEnd.ToString("MM-dd");
    /// <summary>今天在时间轴的位置(百分比 0-100,用于绘制当前日期竖线)。</summary>
    public double GanttTodayPercent { get; private set; }

    public ActivityViewModel()
    {
        _ = LoadAsync();
    }

    [RelayCommand]
    private Task RefreshAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        if (IsBusy)
        {
            return;
        }
        IsBusy = true;
        StatusText = "正在加载活动…";
        try
        {
            VersionActivities.Clear();
            PoolActivities.Clear();

            var now = DateTime.Now;
            // 甘特图时间轴窗口:最近 14 天前 ~ 14 天后
            GanttStart = now.AddDays(-14).Date;
            GanttEnd = now.AddDays(14).Date;
            OnPropertyChanged(nameof(GanttStartText));
            OnPropertyChanged(nameof(GanttEndText));
            var windowSpan = (GanttEnd - GanttStart).TotalSeconds;
            // 今天在时间轴的位置(当前日期竖线)
            GanttTodayPercent = Math.Clamp((now - GanttStart).TotalSeconds / windowSpan * 100, 0, 100);
            OnPropertyChanged(nameof(GanttTodayPercent));

            // 1. 版本活动(hot-content-side)
            var hots = await AppServices.Wiki.GetEventDataAsync(WikiType.Waves);
            if (hots is not null)
            {
                foreach (var hot in hots.Where(h => h.CountDown?.DateRange is { Count: 2 }))
                {
                    if (!TryParseRange(hot.CountDown!.DateRange!, out var start, out var end))
                    {
                        continue;
                    }
                    // 过期自动剔除
                    if (end < now)
                    {
                        continue;
                    }
                    // 从活动图取主色(甘特条颜色,失败回退主题色)
                    var brush = await LoadActivityColorAsync(hot.ContentUrl);
                    // 当前进度:now 在 [Start,End] 区间的位置(未开始=0,已结束=100)
                    double progress = now <= start ? 0 : now >= end ? 100
                        : (now - start).TotalSeconds / (end - start).TotalSeconds * 100;
                    VersionActivities.Add(new ActivityGanttItem
                    {
                        Title = hot.Title ?? "活动",
                        TimeRangeText = $"{start:MM-dd} ~ {end:MM-dd}",
                        Start = start,
                        End = end,
                        LeftPercent = Math.Clamp((start - GanttStart).TotalSeconds / windowSpan * 100, 0, 100),
                        WidthPercent = Math.Clamp((end - start).TotalSeconds / windowSpan * 100, 2, 100),
                        IsOngoing = end >= now,
                        BarBrush = brush,
                        ProgressPercent = progress,
                    });
                }
            }

            // 2. 换取活动(events-side:角色/武器卡池)
            var events = await AppServices.Wiki.GetEventTabDataAsync(WikiType.Waves);
            if (events?.Tabs is not null)
            {
                var isRole = true;
                foreach (var tab in events.Tabs)
                {
                    if (tab.CountDown?.DateRange is not { Count: 2 })
                    {
                        continue;
                    }
                    if (!TryParseRange(tab.CountDown.DateRange, out var start, out var end))
                    {
                        continue;
                    }
                    // 过期自动剔除
                    if (end < now)
                    {
                        continue;
                    }
                    PoolActivities.Add(new ActivityPoolItem
                    {
                        Name = tab.Name ?? "卡池",
                        Category = isRole ? "角色" : "武器",
                        CountdownText = FormatCountdown(end - now),
                        TimeRangeText = $"{start:MM-dd} {start:HH:mm} ~ {end:MM-dd} {end:HH:mm}",
                        End = end,
                        ImageUrl = tab.Images?.FirstOrDefault()?.Image,
                    });
                    isRole = !isRole;
                }
            }

            HasData = VersionActivities.Count > 0 || PoolActivities.Count > 0;
            StatusText = $"加载完成(版本活动 {VersionActivities.Count} · 换取活动 {PoolActivities.Count})";
        }
        catch (Exception ex)
        {
            StatusText = $"加载失败: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>下载活动图并提取主色(甘特条颜色);失败回退主题强调色(#f8f05c)。</summary>
    private async Task<Avalonia.Media.IBrush> LoadActivityColorAsync(string? url)
    {
        var fallback = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#f8f05c"));
        if (string.IsNullOrWhiteSpace(url))
        {
            return fallback;
        }
        try
        {
            var bytes = await AppServices.Http.GetByteArrayAsync(url).ConfigureAwait(false);
            if (bytes.Length == 0)
            {
                return fallback;
            }
            using var ms = new System.IO.MemoryStream(bytes, writable: false);
            var bmp = new Avalonia.Media.Imaging.Bitmap(ms);
            var colors = McKuro.Services.ColorThiefHelper.GetDominantColors(bmp, 1);
            return colors.Count > 0
                ? new Avalonia.Media.SolidColorBrush(colors[0])
                : fallback;
        }
        catch (Exception)
        {
            return fallback;
        }
    }

    private static bool TryParseRange(IReadOnlyList<string> range, out DateTime start, out DateTime end)
    {
        start = default;
        end = default;
        if (DateTime.TryParse(range[0], System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var s)
            && DateTime.TryParse(range[1], System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var e))
        {
            start = s;
            end = e;
            return true;
        }
        return false;
    }

    private static string FormatCountdown(TimeSpan remaining)
    {
        if (remaining <= TimeSpan.Zero)
        {
            return "已结束";
        }
        if (remaining.TotalDays >= 1)
        {
            return $"剩余 {remaining.Days} 天 {remaining.Hours} 小时";
        }
        if (remaining.TotalHours >= 1)
        {
            return $"剩余 {remaining.Hours} 小时 {remaining.Minutes} 分";
        }
        return $"剩余 {remaining.Minutes} 分钟";
    }
}

/// <summary>甘特条宽度:百分比(0-100) → 像素(参考宽 660)。</summary>
public sealed class GanttWidthConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly GanttWidthConverter Instance = new();
    private const double RefWidth = 660;
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        double pct = value is double d ? Math.Clamp(d, 0, 100) : 0;
        return Math.Max(2, pct * RefWidth / 100.0);
    }
    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>甘特条左偏移:百分比(0-100) → Margin.Left(参考宽 660)。</summary>
public sealed class GanttMarginConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly GanttMarginConverter Instance = new();
    private const double RefWidth = 660;
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        double pct = value is double d ? Math.Clamp(d, 0, 100) : 0;
        return new Avalonia.Thickness(Math.Max(0, pct * RefWidth / 100.0), 0, 0, 0);
    }
    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>活动是否进行中 → 不透明度(进行中=1,未开始=0.6)。</summary>
public sealed class OngoingOpacityConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly OngoingOpacityConverter Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is true ? 1.0 : 0.6;
    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}