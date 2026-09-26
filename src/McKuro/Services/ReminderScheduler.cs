using System.Globalization;
using McKuro.Core.Models.Kuro;
using McKuro.Core.Models.User;
using McKuro.Core.Models.Wiki;
using McKuro.Core.Services.Gacha;
using McKuro.Core.Services.Notification;
using McKuro.ViewModels;

namespace McKuro.Services;

/// <summary>
/// 提醒调度器:后台周期检查五类提醒,经 <see cref="ReminderNotifier.Raise"/> 弹出悬浮通知
/// (除设置页外全局显示;同 Key 当日只弹一次,类别可在设置页关闭)——
/// <list type="bullet">
/// <item>游戏签到状态:全部账号逐角色查询,未签到聚合提醒(每 2 小时,按天去重)</item>
/// <item>活动临期:版本活动 + 换取活动(卡池)距结束 ≤3 天提醒,与活动页忽略名单互通(每 6 小时,按天去重)</item>
/// <item>登录状态:库街区/云鸣潮/攻略站会话校验,失效提醒「前往账号页」(每 2 小时,按天去重)</item>
/// <item>周本:本周最后一天(周日/周一 4 点前)仍未打满提醒(每小时,按周去重)</item>
/// <item>每日活跃度:19 点后仍为 0 提醒(每小时,按天去重)</item>
/// </list>
/// 检查失败静默(网络异常/风控不告警,避免误报);每日进度本地 SDK 数据优先,失败回退库街区接口。
/// </summary>
public sealed class ReminderScheduler : IDisposable
{
    private static readonly TimeSpan Cycle = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan SignInterval = TimeSpan.FromHours(2);
    private static readonly TimeSpan LoginInterval = TimeSpan.FromHours(2);
    private static readonly TimeSpan ActivityInterval = TimeSpan.FromHours(6);
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromHours(1);

    /// <summary>活动临期阈值(与活动页 <c>ExpiringSoonDays</c> 一致:距结束 ≤3 天)。</summary>
    private static readonly TimeSpan ExpiringWindow = TimeSpan.FromDays(3);

    /// <summary>等自动签到结束的上限:超过就照常检查签到状态(宁可偶发误报也不让提醒卡住)。</summary>
    private static readonly TimeSpan AutoSignWaitTimeout = TimeSpan.FromSeconds(60);

    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;
    private DateTime _lastSign;
    private DateTime _lastLogin;
    private DateTime _lastActivity;
    private DateTime _lastProgress;

    /// <summary>
    /// 等本次启动的自动签到结束的回调(可为 null = 不等)。由 App 注入 DailyTaskScheduler 的等待方法:
    /// 首轮提醒已提前到 15 秒(与自动签到同期),靠这个显式依赖避免"签到进行中就被报未签到"。
    /// </summary>
    private readonly Func<TimeSpan, Task>? _waitForAutoSign;

    public ReminderScheduler(Func<TimeSpan, Task>? waitForAutoSign = null)
        => _waitForAutoSign = waitForAutoSign;

    public void Start()
    {
        if (_loop is not null)
        {
            return;
        }
        _loop = Task.Run(() => RunAsync(_cts.Token));
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            // 启动 15 秒后首轮(与 DailyTaskScheduler 同期)。原来靠"首轮 75 秒"错开自动签到,
            // 现在改为显式依赖:签到状态检查前先等自动签到跑完(见 CheckSignAsync),
            // 因此首轮可以提前到 15 秒,不必再用固定偏移错峰。
            await Task.Delay(TimeSpan.FromSeconds(15), ct).ConfigureAwait(false);
            using var timer = new PeriodicTimer(Cycle);
            while (true)
            {
                await RunDueChecksAsync(ct).ConfigureAwait(false);
                if (!await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常退出
        }
        catch (Exception)
        {
            // 调度循环异常静默:提醒属辅助功能,不因轮询失败影响主程序
        }
    }

    private async Task RunDueChecksAsync(CancellationToken ct)
    {
        var now = DateTime.Now;
        var settings = AppServices.Settings.Current;
        // 各项检查彼此独立:并发执行,避免某一项(如签到状态要等自动签到结束)拖慢其它提醒
        var due = new List<Task>(4);
        // 各项检查独立吞异常:单项失败(网络抖动)不影响其他提醒
        if (settings.NotifSignEnabled && now - _lastSign >= SignInterval)
        {
            _lastSign = now;
            due.Add(SafeAsync(() => CheckSignAsync(ct)));
        }
        if (settings.NotifLoginEnabled && now - _lastLogin >= LoginInterval)
        {
            _lastLogin = now;
            due.Add(SafeAsync(() => CheckLoginAsync(ct)));
        }
        if (settings.NotifActivityEnabled && now - _lastActivity >= ActivityInterval)
        {
            _lastActivity = now;
            due.Add(SafeAsync(() => CheckActivityAsync(ct)));
        }
        if (ReminderWindows.IsDailyProgressDue(now) && now - _lastProgress >= ProgressInterval)
        {
            _lastProgress = now;
            due.Add(SafeAsync(() => CheckDailyProgressAsync(ct)));
        }
        if (due.Count > 0)
        {
            await Task.WhenAll(due).ConfigureAwait(false);
        }
    }

    private static async Task SafeAsync(Func<Task> check)
    {
        try
        {
            await check().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // 单项检查失败静默
        }
    }

    /// <summary>游戏签到状态:全部账号逐角色查询,未签到聚合为一条提醒(按天去重)。
    /// 先等本次启动的自动签到结束(最多见 <see cref="AutoSignWaitTimeout"/>,超时则照常检查),
    /// 否则刚签完的账号会被误报「未签到」。</summary>
    private async Task CheckSignAsync(CancellationToken ct)
    {
        if (_waitForAutoSign is not null)
        {
            await _waitForAutoSign(AutoSignWaitTimeout).ConfigureAwait(false);
        }

        var accounts = AppServices.KuroAccounts.GetAccounts();
        var unsigned = new List<string>();
        foreach (var account in accounts)
        {
            ct.ThrowIfCancellationRequested();
            var gamer = await AppServices.Kuro.GetGamerAsync(account, (int)KuroGameType.Waves, ct).ConfigureAwait(false);
            // 拉取失败(网络/风控/token 失效)不告警:登录状态由专项检查负责
            if (gamer is not { Code: 200, Data: not null })
            {
                continue;
            }
            foreach (var role in gamer.Data)
            {
                if (string.IsNullOrEmpty(role.RoleId))
                {
                    continue;
                }
                var info = await AppServices.Kuro.GetSignInDataAsync(account, role, ct).ConfigureAwait(false);
                if (info is { Code: 200 } && info.Data?.IsSigIn != true)
                {
                    unsigned.Add(string.IsNullOrWhiteSpace(role.RoleName)
                        ? LanguageService.Format("Sign.UnknownRole")
                        : role.RoleName!);
                }
            }
        }

        if (unsigned.Count > 0)
        {
            AppServices.Reminders.Raise(
                $"sign:{DateTime.Now:yyyyMMdd}",
                NotificationKind.Sign,
                LanguageService.Format("Notif.SignTitle"),
                LanguageService.Format("Notif.SignMessage", unsigned.Count, string.Join("、", unsigned)),
                NavigationKeys.Sign);
        }
    }

    /// <summary>登录状态:三个接口会话校验,失效各提醒一条(Key 含日期,当天只弹一次);
    /// 网络失败(返回 null/抛异常)不告警。</summary>
    private static async Task CheckLoginAsync(CancellationToken ct)
    {
        var today = DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

        // 库街区:当前账号拉角色列表判定(与账号页同一非破坏性检查)
        var kuro = AppServices.KuroAccounts.Current;
        if (kuro is not null)
        {
            var gamer = await AppServices.Kuro.GetGamerAsync(kuro, (int)KuroGameType.Waves, ct).ConfigureAwait(false);
            // 服务端明确拒绝(非 null 但非 200)→ 会话失效;网络异常(含抛错/null)不告警
            if (gamer is not null && gamer is not { Code: 200, Data: not null })
            {
                AppServices.Reminders.Raise(
                    $"login:kuro:{today}",
                    NotificationKind.AccountSession,
                    LanguageService.Format("Notif.LoginKuroTitle"),
                    LanguageService.Format("Notif.LoginSessionExpired", gamer.Msg ?? $"code={gamer.Code}"),
                    NavigationKeys.Account);
            }
        }

        // 云鸣潮:静默校验(服务内部会续期轮换令牌并回存)
        if (AppServices.CloudGacha.HasSavedLogin)
        {
            var (status, msg) = await AppServices.CloudGacha.ValidateSessionAsync(ct).ConfigureAwait(false);
            if (status == CloudGachaStatus.LoginFailed)
            {
                AppServices.Reminders.Raise(
                    $"login:cloud:{today}",
                    NotificationKind.AccountSession,
                    LanguageService.Format("Notif.LoginCloudTitle"),
                    LanguageService.Format("Notif.LoginSessionExpired", msg ?? ""),
                    NavigationKeys.Account);
            }
        }

        // mcguide:轻量鉴权请求判定 x-token
        if (AppServices.Guide.HasToken)
        {
            var (valid, msg) = await AppServices.Guide.ValidateSessionAsync(ct).ConfigureAwait(false);
            if (valid == false)
            {
                AppServices.Reminders.Raise(
                    $"login:guide:{today}",
                    NotificationKind.AccountSession,
                    LanguageService.Format("Notif.LoginGuideTitle"),
                    LanguageService.Format("Notif.LoginSessionExpired", msg ?? ""),
                    NavigationKeys.Account);
            }
        }
    }

    /// <summary>活动临期:版本活动(与活动页忽略名单互通)+ 换取活动(卡池),距结束 ≤3 天提醒(按天去重)。</summary>
    private static async Task CheckActivityAsync(CancellationToken ct)
    {
        var now = DateTime.Now;
        var ignored = AppServices.Settings.Current.IgnoredEndingActivityIds ?? [];

        // 1. 版本活动(hot-content-side;Key 与活动页甘特条一致:标题|开始|结束)
        var hots = await AppServices.Wiki.GetEventDataAsync(WikiType.Waves, ct).ConfigureAwait(false);
        if (hots is not null)
        {
            foreach (var hot in hots.Where(h => h.CountDown?.DateRange is { Count: 2 }))
            {
                if (!TryParseRange(hot.CountDown!.DateRange!, out var start, out var end)
                    || end < now
                    || end - now > ExpiringWindow)
                {
                    continue;
                }
                // Key 与活动页甘特条完全一致(含空标题回退),保证忽略名单互通
                var key = $"{hot.Title ?? LanguageService.Format("Activity.FallbackTitle")}|{start:yyyyMMddHHmm}|{end:yyyyMMddHHmm}";
                if (ignored.Contains(key))
                {
                    continue;
                }
                AppServices.Reminders.Raise(
                    $"act:{key}",
                    NotificationKind.ActivityEnding,
                    LanguageService.Format("Notif.ActivityTitle"),
                    LanguageService.Format("Notif.ActivityMessage",
                        hot.Title ?? "", ActivityViewModel.FormatRemaining(end - now), end.ToString("MM-dd")),
                    NavigationKeys.Activity);
            }
        }

        // 2. 换取活动(events-side:角色池/武器池)
        var pools = await AppServices.Wiki.GetEventTabDataListAsync(WikiType.Waves, ct).ConfigureAwait(false);
        if (pools is not null)
        {
            foreach (var events in pools)
            {
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
                    if (!TryParseRange(tab.CountDown.DateRange, out var start, out var end)
                        || end < now
                        || end - now > ExpiringWindow)
                    {
                        continue;
                    }
                    AppServices.Reminders.Raise(
                        $"act:pool:{tab.Name}|{start:yyyyMMddHHmm}|{end:yyyyMMddHHmm}",
                        NotificationKind.ActivityEnding,
                        LanguageService.Format("Notif.ActivityTitle"),
                        LanguageService.Format("Notif.ActivityMessage",
                            tab.Name ?? "", ActivityViewModel.FormatRemaining(end - now), end.ToString("MM-dd")),
                        NavigationKeys.Activity);
                }
            }
        }
    }

    /// <summary>每日进度:活跃度 19 点后为 0 / 周本最后一天未打满(本地 SDK 数据优先,省接口)。</summary>
    private static async Task CheckDailyProgressAsync(CancellationToken ct)
    {
        var now = DateTime.Now;
        // 本地游戏缓存 + PC 启动器 SDK 优先(与主页同链路,离线也能判定),失败回退库街区接口
        var data = await AppServices.LocalDaily.GetDailyDataAsync(ct).ConfigureAwait(false)
                   ?? (AppServices.KuroAccounts.Current is not null
                       ? await AppServices.DailyData.GetDailyDataAsync(ct).ConfigureAwait(false)
                       : null);
        if (data is null)
        {
            return;
        }

        // 每日活跃度:19 点后仍为 0 提醒(按天去重)
        var liveness = data.LivenessData;
        if (liveness is not null
            && AppServices.Settings.Current.NotifLivenessEnabled
            && ReminderWindows.IsLivenessReminderTime(now)
            && liveness.Cur <= 0)
        {
            var limit = data.LivenessLimit > 0 ? data.LivenessLimit : 100;
            AppServices.Reminders.Raise(
                $"progress:liveness:{now:yyyyMMdd}",
                NotificationKind.DailyLiveness,
                LanguageService.Format("Notif.LivenessTitle"),
                LanguageService.Format("Notif.LivenessMessage", limit),
                NavigationKeys.Home);
        }

        // 周本(战歌重奏):本周最后一天仍未打满提醒(按周去重)
        var weekly = data.WeeklyData;
        if (weekly is not null
            && AppServices.Settings.Current.NotifWeeklyEnabled
            && ReminderWindows.IsWeeklyLastDay(now)
            && weekly.Cur < ResolveWeeklyTotal(data, weekly))
        {
            var remaining = ResolveWeeklyTotal(data, weekly) - weekly.Cur;
            AppServices.Reminders.Raise(
                $"progress:weekly:{ReminderWindows.WeekKey(now)}",
                NotificationKind.WeeklyBoss,
                LanguageService.Format("Notif.WeeklyTitle"),
                LanguageService.Format("Notif.WeeklyMessage", remaining),
                NavigationKeys.Home);
        }
    }

    /// <summary>周本次数上限:接口 total → 数据中心 weeklyInstCountLimit → 回退 3(与主页一致)。</summary>
    private static int ResolveWeeklyTotal(RoleDailyData data, RoleDailyDetail weekly)
        => weekly.Total > 0 ? weekly.Total : data.WeeklyLimit > 0 ? data.WeeklyLimit : 3;

    private static bool TryParseRange(IReadOnlyList<string> range, out DateTime start, out DateTime end)
    {
        start = default;
        end = default;
        if (DateTime.TryParse(range[0], CultureInfo.InvariantCulture, DateTimeStyles.None, out var s)
            && DateTime.TryParse(range[1], CultureInfo.InvariantCulture, DateTimeStyles.None, out var e))
        {
            start = s;
            end = e;
            return true;
        }
        return false;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
