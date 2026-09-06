namespace McKuro.Core.Services.Settings;

/// <summary>
/// 每日自动任务触发判定(纯函数,便于测试):
/// 应用每次启动后自动执行一次(启动 15 秒待网络/账号初始化),按天去重 ——
/// 当天已执行过则跳过,反复重启不重复;手动执行(签到页「一键日常」)不受限制。
/// </summary>
public static class DailyAutoRunSchedule
{
    /// <summary>当天日期文本("yyyy-MM-dd",设置 LastDailyAutoRunDate 的取值)。</summary>
    public static string TodayText(DateTime now) => now.ToString("yyyy-MM-dd");

    /// <summary>现在是否应自动执行:今天尚未执行过。</summary>
    public static bool ShouldRunNow(DateTime now, string? lastRunDate)
        => lastRunDate != TodayText(now);

    /// <summary>今日自动任务状态描述(供界面展示)。</summary>
    public static string DescribeToday(DateTime now, string? lastRunDate)
        => lastRunDate == TodayText(now) ? "今日自动任务已完成" : "今日尚未执行,下次启动软件后自动执行";
}
