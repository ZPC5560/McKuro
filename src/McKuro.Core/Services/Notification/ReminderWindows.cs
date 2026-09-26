using System.Globalization;

namespace McKuro.Core.Services.Notification;

/// <summary>
/// 提醒时间窗判定(纯函数,供提醒调度器与测试使用)。
/// 鸣潮周期:每日活跃度按自然日;周本(战歌重奏)每周一凌晨 4 点重置。
/// </summary>
public static class ReminderWindows
{
    /// <summary>
    /// 是否为周本周期的最后一天:周日全天,或周一凌晨 0-4 点(重置前)。
    /// </summary>
    public static bool IsWeeklyLastDay(DateTime now)
        => now.DayOfWeek == DayOfWeek.Sunday
           || (now.DayOfWeek == DayOfWeek.Monday && now.Hour < 4);

    /// <summary>每日活跃度提醒时间窗:19 点(含)之后。</summary>
    public static bool IsLivenessReminderTime(DateTime now) => now.Hour >= 19;

    /// <summary>每日进度(活跃度/周本)检查是否需要执行:活跃度提醒窗口或周本最后一天。</summary>
    public static bool IsDailyProgressDue(DateTime now)
        => IsLivenessReminderTime(now) || IsWeeklyLastDay(now);

    /// <summary>
    /// 当前时刻所属周本周期的标识(周一日期,yyyy-MM-dd):
    /// 周一 0-4 点(重置前)仍归属上一个周期,周日的归属日为 6 天前的周一。
    /// </summary>
    public static string WeekKey(DateTime now)
    {
        var date = now.Date;
        switch (date.DayOfWeek)
        {
            case DayOfWeek.Sunday:
                date = date.AddDays(-6);
                break;
            case DayOfWeek.Monday:
                if (now.Hour < 4)
                {
                    date = date.AddDays(-7);
                }
                break;
            default:
                date = date.AddDays(-(int)(date.DayOfWeek - DayOfWeek.Monday));
                break;
        }
        return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}
