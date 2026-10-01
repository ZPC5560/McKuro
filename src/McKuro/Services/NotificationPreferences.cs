using McKuro.Core.Services.Settings;

namespace McKuro.Services;

/// <summary>
/// 提醒类别的唯一判定入口(通知总开关 × 类别开关)。
/// <para>
/// 设置页把「通知」拆成二级菜单:总开关 <see cref="AppSettings.NotifEnabled"/> 之下才是五类具体提醒。
/// 判定必须集中在这里 —— 提醒链路有两处独立入口(<see cref="ReminderScheduler"/> 的轮询门控与
/// <see cref="ReminderNotifier.Raise"/> 的出口门控),各写一份 <c>&amp;&amp;</c> 早晚会漏掉总开关,
/// 出现"总开关关了但某一类还在弹"的漏网。
/// </para>
/// <para>纯函数(只读 settings 入参):不依赖 AppServices / UI 线程,可直接单元测试。</para>
/// </summary>
public static class NotificationPreferences
{
    /// <summary>该类别当前是否允许触发(总开关关闭时一律不允许)。</summary>
    public static bool IsEnabled(AppSettings settings, NotificationKind kind)
    {
        // 总开关优先短路:关闭时不再关心类别开关,避免任何一条链路绕过总开关
        if (!settings.NotifEnabled)
        {
            return false;
        }
        return kind switch
        {
            NotificationKind.Sign => settings.NotifSignEnabled,
            NotificationKind.ActivityEnding => settings.NotifActivityEnabled,
            NotificationKind.AccountSession => settings.NotifLoginEnabled,
            NotificationKind.WeeklyBoss => settings.NotifWeeklyEnabled,
            NotificationKind.DailyLiveness => settings.NotifLivenessEnabled,
            _ => true,
        };
    }

    /// <summary>便捷重载:读取当前进程设置(生产链路用;测试请用显式入参重载)。</summary>
    public static bool IsEnabled(NotificationKind kind)
        => IsEnabled(AppServices.Settings.Current, kind);
}
