using McKuro.Core.Services.Settings;
using McKuro.Services;

namespace McKuro.Tests;

/// <summary>
/// 通知门控测试(总开关 × 类别开关)。
/// <para>
/// 「通知」在设置页是二级菜单:总开关之下才是五类具体提醒。判定集中在
/// <see cref="NotificationPreferences"/> 一处,提醒链路有两处独立入口
/// (ReminderScheduler 轮询门控 + ReminderNotifier 出口门控),
/// 任何一处漏掉总开关都会表现为"总开关关了但某一类还在弹",故逐一覆盖。
/// </para>
/// </summary>
public class NotificationPreferencesTests
{
    /// <summary>五类提醒(与设置页二级菜单里的开关一一对应)。</summary>
    public static TheoryData<NotificationKind> AllKinds =>
    [
        NotificationKind.Sign,
        NotificationKind.ActivityEnding,
        NotificationKind.AccountSession,
        NotificationKind.WeeklyBoss,
        NotificationKind.DailyLiveness,
    ];

    [Theory]
    [MemberData(nameof(AllKinds))]
    public void MasterSwitchOff_DisablesEveryKind(NotificationKind kind)
    {
        // 总开关关闭且类别开关全部打开:任何一类都不允许触发
        var settings = new AppSettings { NotifEnabled = false };
        Assert.False(NotificationPreferences.IsEnabled(settings, kind));
    }

    [Theory]
    [MemberData(nameof(AllKinds))]
    public void MasterSwitchOn_CategorySwitchAlone_StillCounts(NotificationKind kind)
    {
        var settings = new AppSettings { NotifEnabled = true };
        Assert.True(NotificationPreferences.IsEnabled(settings, kind));

        // 关掉该类别后应失效(总开关开着也不能绕回 true)
        DisableCategory(settings, kind);
        Assert.False(NotificationPreferences.IsEnabled(settings, kind));
    }

    [Fact]
    public void MasterSwitchOff_TakesPrecedenceOverEnabledCategories()
    {
        // 总开关关闭、五类全开:仍全部不允许(短路优先于类别判定)
        var settings = new AppSettings
        {
            NotifEnabled = false,
            NotifSignEnabled = true,
            NotifActivityEnabled = true,
            NotifLoginEnabled = true,
            NotifWeeklyEnabled = true,
            NotifLivenessEnabled = true,
        };
        Assert.All(Kinds(), kind => Assert.False(NotificationPreferences.IsEnabled(settings, kind)));
    }

    [Fact]
    public void MasterSwitchDefaults_ToEnabled()
    {
        // 默认值契约:旧配置文件没有 NotifEnabled 字段,反序列化后必须仍是"开启",
        // 否则升级后用户会遇到"提醒凭空全部消失"。
        var settings = new AppSettings();
        Assert.True(settings.NotifEnabled);
        Assert.All(Kinds(), kind => Assert.True(NotificationPreferences.IsEnabled(settings, kind)));
    }

    [Fact]
    public void CategorySwitches_KeepStateWhileMasterOff()
    {
        // 总开关只做门控,不修改类别开关:关掉再打开总开关,各类别原样恢复
        var settings = new AppSettings { NotifEnabled = false };
        DisableCategory(settings, NotificationKind.Sign);

        settings.NotifEnabled = true;
        Assert.False(NotificationPreferences.IsEnabled(settings, NotificationKind.Sign));
        Assert.True(NotificationPreferences.IsEnabled(settings, NotificationKind.ActivityEnding));
    }

    [Fact]
    public void RoundTrips_Through_SettingsFile()
    {
        // 持久化契约:总开关关闭后重载仍是关闭(否则重启即失效)
        var dir = Path.Combine(Path.GetTempPath(), "mckuro-notifpref-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var writer = new SettingsService(dir, Microsoft.Extensions.Logging.Abstractions.NullLogger<SettingsService>.Instance);
            writer.Current.NotifEnabled = false;
            writer.Current.NotifLivenessEnabled = false;
            writer.Save();

            var reader = new SettingsService(dir, Microsoft.Extensions.Logging.Abstractions.NullLogger<SettingsService>.Instance);
            Assert.False(reader.Current.NotifEnabled);
            Assert.False(reader.Current.NotifLivenessEnabled);
            Assert.False(NotificationPreferences.IsEnabled(reader.Current, NotificationKind.DailyLiveness));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    private static IEnumerable<NotificationKind> Kinds() =>
    [
        NotificationKind.Sign,
        NotificationKind.ActivityEnding,
        NotificationKind.AccountSession,
        NotificationKind.WeeklyBoss,
        NotificationKind.DailyLiveness,
    ];

    private static void DisableCategory(AppSettings settings, NotificationKind kind)
    {
        switch (kind)
        {
            case NotificationKind.Sign: settings.NotifSignEnabled = false; break;
            case NotificationKind.ActivityEnding: settings.NotifActivityEnabled = false; break;
            case NotificationKind.AccountSession: settings.NotifLoginEnabled = false; break;
            case NotificationKind.WeeklyBoss: settings.NotifWeeklyEnabled = false; break;
            case NotificationKind.DailyLiveness: settings.NotifLivenessEnabled = false; break;
        }
    }
}
