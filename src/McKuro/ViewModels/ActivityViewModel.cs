using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using McKuro.Core.Models.Wiki;
using McKuro.Services;

namespace McKuro.ViewModels;

/// <summary>甘特图活动条目(当前版本活动,过期已剔除)。</summary>
public sealed partial class ActivityGanttItem : ObservableObject
{
    public required string Title { get; init; }
    /// <summary>活动唯一 Key(标题+起止时间,临期忽略提醒的持久化标识)。</summary>
    public required string Key { get; init; }
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
    /// <summary>进度层宽度(占时间轴宽度百分比)= 条宽 × 进度,让进度层相对条自身绘制。</summary>
    public required double ProgressWidthPercent { get; init; }
    /// <summary>活动图 URL(甘特图左列 logo)。</summary>
    public string? ImageUrl { get; init; }
    /// <summary>是否临期(距结束 ≤3 天,进行中);临期且未忽略时标题标红提醒。</summary>
    public required bool IsExpiringSoon { get; init; }
    /// <summary>临期标题文本(标题 + 剩余时间,标红显示;非临期与 Title 相同)。</summary>
    public required string AlertTitle { get; init; }

    /// <summary>用户是否已忽略该活动的临期提醒(点击忽略/取消忽略切换,持久化到设置)。</summary>
    [ObservableProperty]
    private bool _isIgnored;

    /// <summary>标题是否标红(临期且未忽略)。</summary>
    public bool IsAlertVisible => IsExpiringSoon && !IsIgnored;

    /// <summary>忽略按钮文本(忽略 ↔ 取消忽略)。</summary>
    public string IgnoreButtonText => LanguageService.Format(IsIgnored ? "Activity.Unignore" : "Activity.Ignore");

    partial void OnIsIgnoredChanged(bool value)
    {
        OnPropertyChanged(nameof(IsAlertVisible));
        OnPropertyChanged(nameof(IgnoreButtonText));
    }
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
    /// <summary>卡池全部内容图(5★/4★ 角色武器,对齐 Haiyu 显示 4 张)。</summary>
    public List<string> Images { get; init; } = [];
    /// <summary>卡池 4★ 内容图(首张 5★ 大图之后的内容,如 4★ 角色/武器小图)。</summary>
    public List<string> FourStarImages => Images.Skip(1).ToList();
    /// <summary>是否有 4★ 内容(首图之后还有图)。</summary>
    public bool HasFourStar => Images.Count > 1;
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
    /// <summary>今天日期标签(如 今天 08-16)。</summary>
    public string GanttTodayLabel { get; private set; } = "";

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
        StatusText = LanguageService.Format("Activity.StatusLoading");
        try
        {
            VersionActivities.Clear();
            PoolActivities.Clear();

            var now = DateTime.Now;
            // 版本活动先收集到本地:甘特图时间轴由活动数据驱动(起点=最早活动开始=新版开服,终点=最晚活动结束),
            // 需先知道全部活动的起止范围,才能计算每条甘特条的定位。
            var rawHots = new List<(string Title, DateTime Start, DateTime End, Avalonia.Media.IBrush Brush, bool IsOngoing, double Progress, string? ImageUrl)>();

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
                    rawHots.Add((hot.Title ?? LanguageService.Format("Activity.FallbackTitle"), start, end, brush, end >= now, progress, hot.ContentUrl));
                }
            }

            // 甘特图时间轴:数据驱动 —— 起点=当前版本最早活动开始(新版开服日),终点=最晚活动结束。
            if (rawHots.Count > 0)
            {
                GanttStart = rawHots.Min(a => a.Start).Date;
                GanttEnd = rawHots.Max(a => a.End).Date;
                if (GanttEnd <= GanttStart)
                {
                    // 所有活动集中在同一天:至少保留一天窗口,防止除零/退化
                    GanttEnd = GanttStart.AddDays(1);
                }
            }
            else
            {
                // 无活动数据:回退最近 14 天 ~ 14 天后
                GanttStart = now.AddDays(-14).Date;
                GanttEnd = now.AddDays(14).Date;
            }
            OnPropertyChanged(nameof(GanttStartText));
            OnPropertyChanged(nameof(GanttEndText));
            var windowSpan = Math.Max(1, (GanttEnd - GanttStart).TotalSeconds);
            // 今天在时间轴的位置(当前日期竖线)
            GanttTodayPercent = Math.Clamp((now - GanttStart).TotalSeconds / windowSpan * 100, 0, 100);
            OnPropertyChanged(nameof(GanttTodayPercent));
            GanttTodayLabel = LanguageService.Format("Activity.Today", now.ToString("MM-dd"));
            OnPropertyChanged(nameof(GanttTodayLabel));

            foreach (var item in rawHots)
            {
                // 甘特条定位:左端=start 位置,右端=end 位置
                var leftPos = Math.Clamp((item.Start - GanttStart).TotalSeconds / windowSpan * 100, 0, 100);
                var rightPos = Math.Clamp((item.End - GanttStart).TotalSeconds / windowSpan * 100, 0, 100);
                var widthPct = Math.Max(0.5, rightPos - leftPos);
                var expiringSoon = item.IsOngoing && (item.End - now).TotalDays <= ExpiringSoonDays;
                VersionActivities.Add(new ActivityGanttItem
                {
                    Title = item.Title,
                    Key = $"{item.Title}|{item.Start:yyyyMMddHHmm}|{item.End:yyyyMMddHHmm}",
                    TimeRangeText = $"{item.Start:MM-dd} ~ {item.End:MM-dd}",
                    Start = item.Start,
                    End = item.End,
                    LeftPercent = leftPos,
                    WidthPercent = widthPct,
                    IsOngoing = item.IsOngoing,
                    BarBrush = item.Brush,
                    ProgressPercent = item.Progress,
                    // 进度层相对条自身宽度绘制(条宽×进度)
                    ProgressWidthPercent = widthPct * item.Progress / 100,
                    ImageUrl = item.ImageUrl,
                    IsExpiringSoon = expiringSoon,
                    AlertTitle = expiringSoon ? LanguageService.Format("Activity.AlertRemaining", item.Title, FormatRemaining(item.End - now)) : item.Title,
                });
            }

            // 恢复已忽略的临期提醒(仅保留当前仍展示的活动 Key:新活动刷新后旧忽略项自动失效)
            var ignoredIds = AppServices.Settings.Current.IgnoredEndingActivityIds ?? [];
            var currentKeys = VersionActivities.Select(a => a.Key).ToHashSet();
            foreach (var gantt in VersionActivities)
            {
                gantt.IsIgnored = ignoredIds.Contains(gantt.Key);
            }
            var prunedIds = ignoredIds.Where(currentKeys.Contains).ToList();
            if (prunedIds.Count != ignoredIds.Count)
            {
                AppServices.Settings.Current.IgnoredEndingActivityIds = prunedIds;
                AppServices.Settings.Save();
            }

            // 2. 换取活动(events-side:角色池 / 武器池,每个 events-side 一个池)
            var eventsList = await AppServices.Wiki.GetEventTabDataListAsync(WikiType.Waves);
            if (eventsList is not null)
            {
                for (int poolIdx = 0; poolIdx < eventsList.Count; poolIdx++)
                {
                    var category = poolIdx == 0 ? LanguageService.Format("Activity.CatRole") : poolIdx == 1 ? LanguageService.Format("Activity.CatWeapon") : LanguageService.Format("Activity.CatPool", poolIdx + 1);
                    var events = eventsList[poolIdx];
                    if (events?.Tabs is null)
                    {
                        continue;
                    }
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
                            Name = tab.Name ?? LanguageService.Format("Activity.PoolFallback"),
                            Category = category,
                            CountdownText = FormatCountdown(end - now),
                            TimeRangeText = $"{start:MM-dd} {start:HH:mm} ~ {end:MM-dd} {end:HH:mm}",
                            End = end,
                            ImageUrl = tab.Images?.FirstOrDefault()?.Image,
                            Images = tab.Images?.Select(i => i.Image).Where(u => !string.IsNullOrWhiteSpace(u)).Select(u => u!).ToList() ?? [],
                        });
                    }
                }
            }

            HasData = VersionActivities.Count > 0 || PoolActivities.Count > 0;
            StatusText = LanguageService.Format("Activity.StatusLoaded", VersionActivities.Count, PoolActivities.Count);
        }
        catch (Exception ex)
        {
            StatusText = LanguageService.Format("Status.LoadFailedWith", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>下载活动图并提取主色(甘特条颜色);失败回退主题强调色(#f8f05c)。
    /// 取出现次数前 5 候选色中「最鲜明」的颜色(高饱和主题色优先,避免大面积暗灰背景色)。
    /// 注意:Avalonia Brush 必须在 UI 线程创建,否则渲染时跨线程访问崩溃。</summary>
    private async Task<Avalonia.Media.IBrush> LoadActivityColorAsync(string? url)
    {
        var fallbackColor = Avalonia.Media.Color.Parse("#f8f05c");
        try
        {
            if (!string.IsNullOrWhiteSpace(url))
            {
                using var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, url);
                req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36");
                var bytes = await AppServices.Http.SendAsync(req).ConfigureAwait(false) is { IsSuccessStatusCode: true } resp
                    ? await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false)
                    : [];
                if (bytes.Length > 0)
                {
                    using var ms = new System.IO.MemoryStream(bytes, writable: false);
                    var bmp = new Avalonia.Media.Imaging.Bitmap(ms);
                    var vivid = McKuro.Services.ColorThiefHelper.GetVividDominantColor(bmp);
                    if (vivid is { } color)
                    {
                        fallbackColor = color;
                    }
                }
            }
        }
        catch (Exception)
        {
            // 下载/解码失败:用回退色
        }
        // 回到 UI 线程创建 Brush(Avalonia Brush 线程亲和)
        return await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
            () => (Avalonia.Media.IBrush)new Avalonia.Media.SolidColorBrush(fallbackColor));
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

    /// <summary>临期提醒阈值:距结束 ≤3 天的进行中活动标题标红。</summary>
    private const double ExpiringSoonDays = 3;

    /// <summary>临期剩余时间短文本(如 "3天" / "14小时")。</summary>
    private static string FormatRemaining(TimeSpan remaining)
    {
        if (remaining.TotalDays >= 1)
        {
            return LanguageService.Format("Activity.DaysShort", remaining.Days);
        }
        if (remaining.TotalHours >= 1)
        {
            return LanguageService.Format("Activity.HoursShort", (int)remaining.TotalHours);
        }
        return LanguageService.Format("Activity.MinutesShort", Math.Max(1, (int)remaining.TotalMinutes));
    }

    /// <summary>
    /// 切换活动临期提醒的忽略状态(忽略 ↔ 取消忽略,持久化到设置)。
    /// 仅保留当前展示活动的 Key:新版本活动刷新后旧忽略项自动失效。
    /// </summary>
    [RelayCommand]
    private void ToggleActivityIgnore(ActivityGanttItem? item)
    {
        if (item is null)
        {
            return;
        }
        item.IsIgnored = !item.IsIgnored;
        AppServices.Settings.Current.IgnoredEndingActivityIds = VersionActivities
            .Where(static a => a.IsIgnored)
            .Select(static a => a.Key)
            .ToList();
        AppServices.Settings.Save();
    }

    private static string FormatCountdown(TimeSpan remaining)
    {
        if (remaining <= TimeSpan.Zero)
        {
            return LanguageService.Format("Activity.Ended");
        }
        if (remaining.TotalDays >= 1)
        {
            return LanguageService.Format("Activity.RemainDH", remaining.Days, remaining.Hours);
        }
        if (remaining.TotalHours >= 1)
        {
            return LanguageService.Format("Activity.RemainHM", remaining.Hours, remaining.Minutes);
        }
        return LanguageService.Format("Activity.RemainM", remaining.Minutes);
    }
}

/// <summary>甘特条宽度:百分比(0-100) → 像素(参考宽 660)。</summary>
public sealed class GanttWidthConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly GanttWidthConverter Instance = new();
    private const double RefWidth = 940;
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
    private const double RefWidth = 940;
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        double pct = value is double d ? Math.Clamp(d, 0, 100) : 0;
        return new Avalonia.Thickness(Math.Max(0, pct * RefWidth / 100.0), 0, 0, 0);
    }
    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>今天日期标签左偏移:百分比(0-100) → Margin.Left(参考宽 940,防止右侧溢出)。</summary>
public sealed class GanttTodayLabelMarginConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly GanttTodayLabelMarginConverter Instance = new();
    private const double RefWidth = 940;
    private const double LabelWidth = 70;   // 估计"今天 MM-dd"芯片宽度,用于右侧防溢出
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        double pct = value is double d ? Math.Clamp(d, 0, 100) : 0;
        var left = Math.Clamp(pct * RefWidth / 100.0, 0, RefWidth - LabelWidth);
        return new Avalonia.Thickness(left, 0, 0, 0);
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