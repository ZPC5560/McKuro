using System.Globalization;

namespace McKuro.Core.Services.Notification;

/// <summary>
/// 提醒时间窗判定(纯函数,供提醒调度器与测试使用)。
/// 鸣潮周期:每日活跃度按自然日;战歌同奏(周本)每周一凌晨 4 点重置。
/// <para>
/// <b>重置时刻是服务器时间</b>(评审反馈):周本/每日重置都是服务器时间 04:00,旧实现直接拿本机
/// <c>DateTime.Now</c> 判定,非 UTC+8 时区的用户提醒窗口整体错位,时区边界附近还会产生重复提醒。
/// 现在周期判定与去重 Key 一律先折算到服务器挂钟(<see cref="ToServerTime"/>)再计算。
/// </para>
/// <para>
/// 服务器偏移:提醒数据全部来自库街区数据中心,而库街区只服务国服(官服/B 服)账号,国服为固定 UTC+8
/// (无夏令时)。国际服各区的重置偏移无法在此核实,且当前没有国际服数据源,故不做区分 ——
/// 将来接入国际服数据时需按区服传入各自偏移。
/// </para>
/// </summary>
public static class ReminderWindows
{
    /// <summary>国服服务器时间相对 UTC 的固定偏移(UTC+8,无夏令时)。</summary>
    public static readonly TimeSpan ServerUtcOffset = TimeSpan.FromHours(8);

    /// <summary>每日/每周重置的服务器挂钟小时(04:00)。</summary>
    public const int ResetHour = 4;

    /// <summary>
    /// 把时刻折算为服务器挂钟时间(返回 <see cref="DateTimeKind.Unspecified"/>)。
    /// <para><see cref="DateTimeKind.Local"/>(如 <c>DateTime.Now</c>)与 <see cref="DateTimeKind.Utc"/>
    /// 按真实时刻换算;<see cref="DateTimeKind.Unspecified"/> 视为<b>已经是服务器挂钟时间</b>,原样返回
    /// (供测试与重放直接构造服务器时刻)。</para>
    /// </summary>
    public static DateTime ToServerTime(DateTime time)
    {
        if (time.Kind == DateTimeKind.Unspecified)
        {
            return time;
        }
        var utc = time.Kind == DateTimeKind.Utc ? time : time.ToUniversalTime();
        return DateTime.SpecifyKind(utc + ServerUtcOffset, DateTimeKind.Unspecified);
    }

    /// <summary>
    /// 是否为战歌同奏(周本)周期的最后一天(服务器时间):周日全天,或周一凌晨 0-4 点(重置前)。
    /// </summary>
    public static bool IsWeeklyLastDay(DateTime now)
    {
        var server = ToServerTime(now);
        return server.DayOfWeek == DayOfWeek.Sunday
               || (server.DayOfWeek == DayOfWeek.Monday && server.Hour < ResetHour);
    }

    /// <summary>
    /// 每日活跃度提醒时间窗:19 点(含)之后。
    /// <para>按<b>传入时刻自身的挂钟</b>判定(调度器传本机时间):这是"用户自己的晚上"提醒,不是重置边界。</para>
    /// </summary>
    public static bool IsLivenessReminderTime(DateTime now) => now.Hour >= 19;

    /// <summary>每日进度(活跃度/战歌同奏)检查是否需要执行:活跃度提醒窗口或周本最后一天。</summary>
    public static bool IsDailyProgressDue(DateTime now)
        => IsLivenessReminderTime(now) || IsWeeklyLastDay(now);

    /// <summary>
    /// 当前时刻所属战歌同奏(周本)周期的标识(服务器时间的周一日期,yyyy-MM-dd):
    /// 周一 0-4 点(重置前)仍归属上一个周期,周日的归属日为 6 天前的周一。
    /// </summary>
    public static string WeekKey(DateTime now)
    {
        var server = ToServerTime(now);
        var date = server.Date;
        switch (date.DayOfWeek)
        {
            case DayOfWeek.Sunday:
                date = date.AddDays(-6);
                break;
            case DayOfWeek.Monday:
                if (server.Hour < ResetHour)
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

    /// <summary>
    /// 当前时刻所属"游戏日"的标识(yyyyMMdd):服务器时间 04:00 起算,04:00 前仍归属前一天。
    /// <para>供每日类提醒做去重 Key:用本机自然日做 Key 时,非 UTC+8 用户一个游戏日会跨两个本机日期,
    /// 同一游戏日内可能重复提醒。</para>
    /// </summary>
    public static string DayKey(DateTime now)
    {
        var server = ToServerTime(now);
        var day = server.Hour < ResetHour ? server.Date.AddDays(-1) : server.Date;
        return day.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
    }
}
