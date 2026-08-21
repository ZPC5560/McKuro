using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using McKuro.Core.Services.Game;
using McKuro.Services;

namespace McKuro.ViewModels;

/// <summary>最近 7 天中的某天游玩数据项。</summary>
public sealed class PlayDayItem
{
    public required string Date { get; init; }
    public required string Label { get; init; }       // 周一 / 周二…
    public required string HoursText { get; init; }   // 2.5h
    public required double Minutes { get; init; }     // 用于柱状高度
    public required double BarHeight { get; init; }   // 0-100 相对高度
    public string SessionsText { get; init; } = "";   // 当天每次独立时段(20:00-21:30、22:00-23:00)
}

/// <summary>最近 7 天游玩时间范围报告中的某一天。</summary>
public sealed class PlayTimeReportRow
{
    public required string Label { get; init; }        // 08/16
    public required string Weekday { get; init; }      // 周五
    public required string DayTag { get; init; }       // 今天 / 昨天 / 空
    public bool HasDayTag { get; init; }
    public required string HoursText { get; init; }    // 3.3h
    public required string SessionsText { get; init; } // 15:47-17:51 · 17:27-17:29
    /// <summary>当天游玩时长的相对条宽度(满宽 ≈ 最近 7 天最大值)。</summary>
    public required double DurationWidth { get; init; }
}

/// <summary>7×24 时段热力格。</summary>
public sealed class PlayHourCell
{
    public required int DayIndex { get; init; }
    public required int Hour { get; init; }
    public required long Minutes { get; init; }
    /// <summary>强度 0-1(用于背景色深浅)。</summary>
    public required double Intensity { get; init; }
    public string Tip => $"{Minutes} 分钟";
}

/// <summary>
/// 游玩统计页:只统计游玩时长与时间区间(不统计操作数量)。
/// 解析游戏日志 → 本地库 → 展示总/今日时长、最近 7 天每日时长与 7×24 时段分布。
/// </summary>
public sealed partial class PlayTimeViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _statusText = "尚未分析";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _totalHoursText = "--";

    [ObservableProperty]
    private string _todayHoursText = "--";

    [ObservableProperty]
    private string _recordDaysText = "--";

    /// <summary>最近 7 天游玩时间范围报告的总览(如"共 6 天有游玩")。</summary>
    [ObservableProperty]
    private string _reportSummaryText = "";

    public ObservableCollection<PlayDayItem> Last7Days { get; } = [];

    public ObservableCollection<PlayHourCell> HourlyCells { get; } = [];

    /// <summary>最近 7 天游玩时间范围报告(结构化,每行一天)。</summary>
    public ObservableCollection<PlayTimeReportRow> ReportRows { get; } = [];

    public PlayTimeViewModel()
    {
        RefreshFromDb();
        // 进入页面自动解析日志(页面不再提供"解析日志"按钮)
        _ = AnalyzeAsync();
    }

    /// <summary>重新解析游戏日志并刷新统计。</summary>
    [RelayCommand]
    private async Task AnalyzeAsync()
    {
        if (IsBusy)
        {
            return;
        }
        IsBusy = true;
        StatusText = "正在解析游戏日志…";
        try
        {
            var count = await AppServices.PlayTime.AnalyzeLogAsync();
            RefreshFromDb();
            StatusText = count > 0 ? $"解析完成,本次 {count} 条游玩记录" : "日志解析完成(未发现新记录)";
        }
        catch (Exception ex)
        {
            StatusText = $"解析失败: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RefreshFromDb()
    {
        var a = AppServices.PlayTime.GetAnalysis();

        TotalHoursText = FormatHours(a.TotalSeconds);
        TodayHoursText = FormatHours(a.TodaySeconds);
        RecordDaysText = $"{a.RecordDays} 天";

        // 最近 7 天每日时长(柱状)
        Last7Days.Clear();
        long maxDay = Math.Max(1, a.Last7DaysSeconds.Max());
        for (int i = 0; i < 7; i++)
        {
            long secs = a.Last7DaysSeconds[i];
            // 每天每次独立时段(参考睡眠检测:合并相邻会话)
            var sessions = a.Last7DaysSessions?[i] ?? [];
            var sessionsText = string.Join("、", sessions.Select(s => s.Display));
            Last7Days.Add(new PlayDayItem
            {
                Date = a.Last7DaysDates[i],
                Label = FormatDayLabel(a.Last7DaysDates[i]),
                HoursText = FormatHours(secs),
                Minutes = secs / 60.0,
                BarHeight = secs * 100.0 / maxDay,
                SessionsText = sessionsText,
            });
        }

        // 最近 7 天游玩时间范围报告(结构化):只列出有游玩的日期。
        ReportRows.Clear();
        var today = DateTime.Today;
        double maxReportMin = 1;
        var played = new List<(int Index, long Secs)>();
        for (int i = 0; i < 7; i++)
        {
            long secs = a.Last7DaysSeconds[i];
            if (secs <= 0)
            {
                continue;
            }
            played.Add((i, secs));
            maxReportMin = Math.Max(maxReportMin, secs / 60.0);
        }
        var dayTagCache = new Dictionary<string, string>();
        foreach (var (i, secs) in played)
        {
            var date = a.Last7DaysDates[i];
            var sessions = a.Last7DaysSessions?[i] ?? [];
            var sessionsText = string.Join(" · ", sessions.Select(s => s.Display));
            var dayTag = FormatDayTag(date, today, dayTagCache);
            ReportRows.Add(new PlayTimeReportRow
            {
                Label = FormatDayLabel(date),
                Weekday = FormatWeekday(date),
                DayTag = dayTag,
                HasDayTag = dayTag.Length > 0,
                HoursText = FormatHours(secs),
                SessionsText = sessionsText,
                DurationWidth = Math.Max(4, secs / 60.0 * 160.0 / maxReportMin),
            });
        }
        ReportSummaryText = ReportRows.Count > 0
            ? $"最近 7 天共 {ReportRows.Count} 天有游玩"
            : "最近 7 天暂无游玩记录";

        // 7×24 时段热力
        HourlyCells.Clear();
        long maxCell = 1;
        for (int d = 0; d < 7; d++)
        {
            for (int h = 0; h < 24; h++)
            {
                maxCell = Math.Max(maxCell, a.Last7DaysHourlyMinutes[d, h]);
            }
        }
        for (int d = 0; d < 7; d++)
        {
            for (int h = 0; h < 24; h++)
            {
                long minutes = a.Last7DaysHourlyMinutes[d, h];
                HourlyCells.Add(new PlayHourCell
                {
                    DayIndex = d,
                    Hour = h,
                    Minutes = minutes,
                    Intensity = minutes * 1.0 / maxCell,
                });
            }
        }
    }

    private static string FormatDayLabel(string date)
    {
        return DateTime.TryParse(date, out var day)
            ? day.ToString("MM/dd")
            : date;
    }

    private static string FormatWeekday(string date)
    {
        return DateTime.TryParse(date, out var day)
            ? day.ToString("ddd", System.Globalization.CultureInfo.GetCultureInfo("zh-CN"))
            : "";
    }

    private static string FormatDayTag(string date, DateTime today, Dictionary<string, string> cache)
    {
        if (cache.TryGetValue(date, out var cached))
        {
            return cached;
        }
        string tag = "";
        if (DateTime.TryParse(date, out var day))
        {
            var span = (today.Date - day.Date).Days;
            tag = span switch
            {
                0 => "今天",
                1 => "昨天",
                _ => "",
            };
        }
        cache[date] = tag;
        return tag;
    }

    private static string FormatHours(long totalSeconds)
    {
        if (totalSeconds <= 0)
        {
            return "0h";
        }
        double hours = totalSeconds / 3600.0;
        return hours >= 1 ? $"{hours:0.#}h" : $"{totalSeconds / 60}min";
    }
}

/// <summary>热力强度(0-1) → 背景色(GitHub 活跃热力图绿色分层,从浅绿到深绿)。</summary>
public sealed class IntensityBrushConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly IntensityBrushConverter Instance = new();

    // GitHub contribution graph 4 级绿色(由浅到深)
    private static readonly string[] GreenLevels = ["#9be9a8", "#40c463", "#30a14e", "#216e39"];

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        double intensity = value is double d ? Math.Clamp(d, 0, 1) : 0;
        if (intensity <= 0)
        {
            // 无活动:近透明灰(浅色主题可见)
            return new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromArgb(32, 0, 0, 0));
        }
        // 按强度分 4 级(0.25 一档),对齐 GitHub:低 1/4 → 最浅绿,高 → 最深绿
        int level = Math.Min(3, (int)(intensity * 4));
        return new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(GreenLevels[level]));
    }
    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>int → Grid.Row。</summary>
public sealed class IntToGridRowConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly IntToGridRowConverter Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is int i ? i : 0;
    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>int → Grid.Column。</summary>
public sealed class IntToGridColConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly IntToGridColConverter Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is int i ? i : 0;
    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>百分比(0-100) → 高度像素(容器高 120)。</summary>
public sealed class PercentToHeightConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly PercentToHeightConverter Instance = new();
    private const double MaxHeight = 120;
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        double pct = value is double d ? Math.Clamp(d, 0, 100) : 0;
        return pct * MaxHeight / 100.0;
    }
    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}
