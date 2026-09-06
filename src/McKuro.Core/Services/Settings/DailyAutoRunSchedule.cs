namespace McKuro.Core.Services.Settings;

/// <summary>
/// 每日自动执行时间与当日去重判定(纯函数,便于测试)。
/// 语义:每天(本地时区)最多自动执行一次 —— 当天尚未执行过即触发;
/// 定时模式需已到达当日设定时间,启动模式(OnStartup)启动即执行、不看时间;
/// 定时模式下应用启动晚于设定时间时补执行一次(错过不跳过)。
/// </summary>
public static class DailyAutoRunSchedule
{
    /// <summary>默认执行时间(08:00)。</summary>
    public const string DefaultTimeText = "08:00";

    /// <summary>解析 "HH:mm" / "H:mm"(24 小时制)。</summary>
    public static bool TryParseTime(string? text, out TimeSpan time)
    {
        time = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }
        var parts = text.Trim().Split(':');
        if (parts.Length != 2
            || !int.TryParse(parts[0], out var hours)
            || !int.TryParse(parts[1], out var minutes)
            || hours is < 0 or > 23
            || minutes is < 0 or > 59)
        {
            return false;
        }
        time = new TimeSpan(hours, minutes, 0);
        return true;
    }

    /// <summary>当日的执行时刻(now 所在日期 + 设定时间)。</summary>
    public static DateTime TodayRunAt(DateTime now, TimeSpan time) => now.Date + time;

    /// <summary>现在是否应自动执行:今天尚未执行过,且(启动模式启动即算,或定时模式已到达当日设定时刻)。</summary>
    public static bool ShouldRunNow(DateTime now, TimeSpan time, string? lastRunDate, bool onStartup = false)
    {
        if (lastRunDate == now.ToString("yyyy-MM-dd"))
        {
            return false;
        }
        return onStartup || now >= TodayRunAt(now, time);
    }

    /// <summary>
    /// 下次自动执行描述(供界面展示,如 "今天 08:00" / "明天 08:00" / "今天 08:00 已执行";
    /// 启动模式为 "启动后立即执行" / "启动后立即执行(今天已执行)")。
    /// </summary>
    public static string DescribeNext(DateTime now, TimeSpan time, string? lastRunDate, bool onStartup = false)
    {
        if (lastRunDate == now.ToString("yyyy-MM-dd"))
        {
            return onStartup ? "启动后立即执行(今天已执行)" : $"今天 {time:hh\\:mm} 已执行";
        }
        return onStartup ? "启动后立即执行" : (now < TodayRunAt(now, time) ? $"今天 {time:hh\\:mm}" : $"明天 {time:hh\\:mm}");
    }
}
