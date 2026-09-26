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
    /// <summary>甘特条左缘(px,相对时间轴左端)。</summary>
    public required double BarLeftPx { get; init; }
    /// <summary>甘特条宽(px)。</summary>
    public required double BarWidthPx { get; init; }
    /// <summary>已完成段宽(px,实色部分,右缘=今日线;0 = 尚未开始)。</summary>
    public required double DoneWidthPx { get; init; }
    /// <summary>未进行段左缘(px,= 今日线位置)。</summary>
    public required double RemainLeftPx { get; init; }
    /// <summary>未进行段宽(px)。</summary>
    public required double RemainWidthPx { get; init; }
    /// <summary>行背景轨宽(px = 时间轴宽)。</summary>
    public required double RowWidthPx { get; init; }
    /// <summary>进度文本左缘(px,条右端内侧)。</summary>
    public required double LabelLeftPx { get; init; }
    /// <summary>是否渲染已完成段(now 尚未越过开始时刻的活动无此段)。</summary>
    public required bool HasDone { get; init; }
    /// <summary>是否渲染未进行段(now 未越过结束时刻的活动无此段)。</summary>
    public required bool HasRemain { get; init; }
    /// <summary>是否正在进行(未结束)。</summary>
    public required bool IsOngoing { get; init; }
    /// <summary>活动主色(从活动图取色;失败回退主题色)。</summary>
    public required Avalonia.Media.IBrush BarBrush { get; init; }
    /// <summary>当前进度(0-100):now 在 [Start,End] 区间的位置;未开始=0,已结束=100。</summary>
    public required double ProgressPercent { get; init; }
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

/// <summary>甘特图时间轴日期刻度(均匀分布,保证任意滚动位置都有日期参照)。</summary>
public sealed class GanttTick
{
    /// <summary>刻度左缘(px,相对时间轴左端)。</summary>
    public required double LeftPx { get; init; }

    /// <summary>刻度文本(MM-dd)。</summary>
    public required string Text { get; init; }
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

    /// <summary>甘特图时间轴窗口(活动范围 + 固定右侧前瞻;视口初始滚动到"今天"居中)。</summary>
    public DateTime GanttStart { get; private set; }
    public DateTime GanttEnd { get; private set; }
    /// <summary>今天日期标签(如 今天 08-16)。</summary>
    public string GanttTodayLabel { get; private set; } = "";

    /// <summary>时间轴区域宽度(px;40px/天,最低 720 —— 内容宽于视口才能横向滚动回看历史)。</summary>
    public double GanttTimelineWidth { get; private set; }

    /// <summary>今日线在时间轴内的横向偏移(px;视图用于初始滚动居中)。</summary>
    public double GanttTodayOffsetPx { get; private set; }

    /// <summary>今日线 X 坐标(px;线宽 2,故减 1 居中于分界)。</summary>
    public double TodayLineX { get; private set; }

    /// <summary>甘特条区总高(px;行数×48−8,供今日线贯穿全部行)。</summary>
    public double RowsHeightPx { get; private set; }

    /// <summary>时间轴日期刻度(约 8 个均匀分布)。</summary>
    public ObservableCollection<GanttTick> GanttTicks { get; } = [];

    /// <summary>甘特数据就绪(视图创建晚于数据加载时可立即居中)。</summary>
    public bool GanttDataReady { get; private set; }

    /// <summary>甘特数据加载日期(视图挂载时跨天则自动刷新,避免"今天"位置过期)。</summary>
    public DateTime? GanttLoadedOnDate { get; private set; }

    /// <summary>甘特数据就绪(视图订阅后把视口滚动到今天居中)。</summary>
    public event Action? GanttLoaded;

    /// <summary>时间轴横向比例(px/天;28px/天时默认视口约 ±14 天)。
    /// 与最小时间轴宽 720 配合:活动范围很短时按 720/天数 反推放大。</summary>
    public const double PxPerDay = 28;

    /// <summary>时间轴最小宽度(px;窄于视口时无法拖动回看历史)。</summary>
    private const double MinTimelineWidth = 720;

    /// <summary>
    /// 时间轴右侧固定前瞻(天):只留这么多"未来",不镜像整段历史。
    /// 镜像补白会把时间轴拉长约一倍(全是空白未来),这是"甘特图太长"的根因;
    /// 前瞻 × PxPerDay = 392px ≈ 常规视口半宽,"今天"正好还能居中(更宽的窗口会被钳到最右,今日线略偏右)。
    /// </summary>
    public const int LookaheadDays = 14;

    /// <summary>
    /// 时间轴窗口(纯函数,供单测):起点 = 最早活动开始日;终点 = max(最晚活动结束日 + 1 天, 今天 + 前瞻天数)。
    /// 关键:不镜像历史。镜像补白会把大片空白未来画进时间轴(活动 09-30 结束却画到 11-03),
    /// 使拖动条长度近乎翻倍 —— 这是"甘特图太长"的根因;固定前瞻既能保证"今天"可居中,又不虚增长度。
    /// </summary>
    public static (DateTime Start, DateTime End) ComputeWindow(
        DateTime earliestStart, DateTime latestEnd, DateTime now, int lookaheadDays = LookaheadDays)
    {
        var start = earliestStart.Date;
        var end = latestEnd.Date.AddDays(1);
        var lookaheadEnd = now.Date.AddDays(Math.Max(0, lookaheadDays));
        if (lookaheadEnd > end)
        {
            end = lookaheadEnd;
        }
        if (end <= start)
        {
            end = start.AddDays(1);
        }
        return (start, end);
    }

    /// <summary>
    /// 甘特条像素布局(纯函数,供单测):全部以"时间轴左端为原点、pxPerDay 折算"成像素。
    /// 返回:条左、已完成段宽(实色,右缘=今日线)、未进行段左、未进行段宽、今日线X。
    /// 分界一致性:DoneLeft + DoneWidth == TodayX —— 与条内两段式渲染共用,保证今日线与分界对齐。
    /// </summary>
    public static (double BarLeft, double DoneWidth, double RemainLeft, double RemainWidth, double TodayX) ComputeBarPixels(
        DateTime start, DateTime end, DateTime now, DateTime ganttStart, double pxPerDay)
    {
        var leftDays = Math.Max(0, (start - ganttStart).TotalDays);
        var lenDays = Math.Max(0.02, (end - start).TotalDays);
        var barLeft = leftDays * pxPerDay;
        var barWidth = lenDays * pxPerDay;
        var nowDays = Math.Max(0, (now - ganttStart).TotalDays);
        var todayX = nowDays * pxPerDay;
        var doneW = Math.Max(0, Math.Min(todayX - barLeft, barWidth));
        var remainLeft = barLeft + doneW;
        var remainW = Math.Max(0, barWidth - doneW);
        return (barLeft, doneW, remainLeft, remainW, todayX);
    }

    /// <summary>今日线在时间轴上的百分比(与 <see cref="ComputeBarLayout"/> 同一窗口/同一 now,分母一致保证对齐)。</summary>
    public static double TodayPercentOfWindow(DateTime now, DateTime ganttStart, double windowSpanSeconds)
        => Math.Clamp((now - ganttStart).TotalSeconds / windowSpanSeconds * 100, 0, 100);

    /// <summary>时间轴末尾刻度的右缘预留(px,约一个 MM-dd 文本宽,避免末刻度被视口右缘裁掉)。</summary>
    private const double TickEndReservePx = 30;

    /// <summary>
    /// 时间轴日期刻度(纯函数,供单测):从时间轴起点起按整天均匀取样约 <paramref name="targetCount"/> 个。
    /// 居中视图下起止日期标签必然落在视口外(时间轴宽可达视口数倍),均匀刻度保证任意滚动位置
    /// 都能读到日期;末个刻度右缘预留 <see cref="TickEndReservePx"/> 以免文本被裁。
    /// </summary>
    public static List<GanttTick> ComputeTicks(DateTime ganttStart, double spanDays, double pxPerDay,
        double timelineWidth, int targetCount = 8)
    {
        var ticks = new List<GanttTick>();
        if (spanDays <= 0 || pxPerDay <= 0)
        {
            return ticks;
        }
        // 整天步长(≥1 天;约 targetCount 个刻度)
        var step = Math.Max(1, (int)Math.Ceiling(spanDays / Math.Max(1, targetCount)));
        // 末刻度左缘上限:文本落在时间轴内
        var maxLeft = Math.Max(0, timelineWidth - TickEndReservePx);
        for (var d = 0; d <= spanDays; d += step)
        {
            ticks.Add(new GanttTick
            {
                LeftPx = Math.Min(d * pxPerDay, maxLeft),
                Text = ganttStart.AddDays(d).ToString("MM-dd"),
            });
        }
        return ticks;
    }

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
        System.Console.Error.WriteLine("MCKURO-GANTT load begin");
        try
        {
            VersionActivities.Clear();
            PoolActivities.Clear();

            var now = DateTime.Now;
            // 版本活动先收集到本地:甘特图时间轴由活动数据驱动(起点=最早活动开始=新版开服,终点=最晚活动结束),
            // 需先知道全部活动的起止范围,才能计算每条甘特条的定位。
            var rawHots = new List<(string Title, DateTime Start, DateTime End, Avalonia.Media.IBrush Brush, bool IsOngoing, double Progress, string? ImageUrl)>();

            // 1. 版本活动(hot-content-side)
            var hots = await AppServices.Wiki.GetEventDataAsync(WikiType.Waves).WaitAsync(TimeSpan.FromSeconds(15));
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

            // 甘特图时间轴:活动范围 + 固定右侧前瞻(不镜像历史,见 ComputeWindow/LookaheadDays)
            if (rawHots.Count > 0)
            {
                (GanttStart, GanttEnd) = ComputeWindow(
                    rawHots.Min(a => a.Start), rawHots.Max(a => a.End), now);
            }
            else
            {
                // 无活动数据:回退 7 天 ~ 8 天后
                GanttStart = now.Date.AddDays(-7);
                GanttEnd = now.Date.AddDays(8);
            }
            var windowSpan = Math.Max(1, (GanttEnd - GanttStart).TotalSeconds);
            var spanDays = Math.Max(1, (GanttEnd - GanttStart).TotalDays);
            // 时间轴宽度:40px/天(默认视口约 ±10 天,完成段自然被视口藏住一部分),不低于 720px
            var pxPerDay = Math.Max(PxPerDay, MinTimelineWidth / spanDays);
            GanttTimelineWidth = spanDays * pxPerDay;
            OnPropertyChanged(nameof(GanttTimelineWidth));
            // 今日线精确位置(与条内已完成/未进行分界同一公式,保证线与进度分界对齐)
            var todayPct = TodayPercentOfWindow(now, GanttStart, windowSpan);
            GanttTodayOffsetPx = todayPct / 100 * GanttTimelineWidth;
            OnPropertyChanged(nameof(GanttTodayOffsetPx));
            TodayLineX = GanttTodayOffsetPx - 1;
            OnPropertyChanged(nameof(TodayLineX));
            GanttTodayLabel = LanguageService.Format("Activity.Today", now.ToString("MM-dd"));
            OnPropertyChanged(nameof(GanttTodayLabel));

            foreach (var item in rawHots)
            {
                // 甘特条像素定位:全部按 pxPerDay 折算(pxPerDay 已含最低宽度补偿)
                var (barLeft, doneW, remainLeft, remainW, todayX) = ComputeBarPixels(
                    item.Start, item.End, now, GanttStart, pxPerDay);
                var expiringSoon = item.IsOngoing && (item.End - now).TotalDays <= ExpiringSoonDays;
                System.Console.Error.WriteLine(
                    $"MCKURO-GANTT item {item.Title}: barLeft={barLeft:F0} barW={remainLeft + remainW - barLeft:F0} " +
                    $"doneW={doneW:F0} todayX={todayX:F0}");
                VersionActivities.Add(new ActivityGanttItem
                {
                    Title = item.Title,
                    Key = $"{item.Title}|{item.Start:yyyyMMddHHmm}|{item.End:yyyyMMddHHmm}",
                    TimeRangeText = $"{item.Start:MM-dd} ~ {item.End:MM-dd}",
                    Start = item.Start,
                    End = item.End,
                    BarLeftPx = barLeft,
                    BarWidthPx = remainLeft + remainW - barLeft,
                    DoneWidthPx = doneW,
                    RemainLeftPx = remainLeft,
                    RemainWidthPx = remainW,
                    RowWidthPx = GanttTimelineWidth,
                    // 百分比文本画在条右端内侧
                    LabelLeftPx = Math.Max(barLeft + 2, remainLeft + remainW - 34),
                    HasDone = doneW > 0.5,
                    HasRemain = remainW > 0.5,
                    IsOngoing = item.IsOngoing,
                    BarBrush = item.Brush,
                    ProgressPercent = item.Progress,
                    ImageUrl = item.ImageUrl,
                    IsExpiringSoon = expiringSoon,
                    AlertTitle = expiringSoon ? LanguageService.Format("Activity.AlertRemaining", item.Title, FormatRemaining(item.End - now)) : item.Title,
                });
            }
            GanttDataReady = true;
            GanttLoadedOnDate = now.Date;
            RowsHeightPx = Math.Max(0, VersionActivities.Count * 48 - 8);
            OnPropertyChanged(nameof(RowsHeightPx));

            // 时间轴日期刻度:约 8 个均匀分布的 MM-dd 标签(居中视图下起止日期标签都在视口外,
            // 均匀刻度保证任何滚动位置都有日期参照)
            GanttTicks.Clear();
            foreach (var tick in ComputeTicks(GanttStart, spanDays, pxPerDay, GanttTimelineWidth))
            {
                GanttTicks.Add(tick);
            }
            OnPropertyChanged(nameof(GanttTicks));
            OnPropertyChanged(nameof(GanttDataReady));
            System.Console.Error.WriteLine(
                $"MCKURO-GANTT axis: start={GanttStart:yyyy-MM-dd} end={GanttEnd:yyyy-MM-dd} " +
                $"spanDays={spanDays:F1} pxPerDay={pxPerDay:F1} width={GanttTimelineWidth:F0} " +
                $"ticks={string.Join(' ', GanttTicks.Select(t => $"{t.Text}@{t.LeftPx:F0}"))}");
            GanttLoaded?.Invoke();

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
            var eventsList = await AppServices.Wiki.GetEventTabDataListAsync(WikiType.Waves).WaitAsync(TimeSpan.FromSeconds(15));
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
            System.Console.Error.WriteLine($"MCKURO-GANTT load failed: {ex.Message}");
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

    /// <summary>临期剩余时间短文本(如 "3天" / "14小时");通知调度器复用同一文案。</summary>
    internal static string FormatRemaining(TimeSpan remaining)
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