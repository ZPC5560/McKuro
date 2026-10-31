using System.Globalization;
using McKuro.Core.Models.Kuro;
using McKuro.Core.Models.User;
using McKuro.Core.Models.Wiki;
using McKuro.Core.Services.Gacha;
using McKuro.Core.Services.Notification;
using McKuro.Core.Services.Tower;
using McKuro.ViewModels;
using Microsoft.Extensions.Logging;

namespace McKuro.Services;

/// <summary>
/// 提醒调度器:后台周期检查各类提醒,经 <see cref="ReminderNotifier.Raise"/> 弹出悬浮通知
/// (除设置页外全局显示;同 Key 当日只弹一次,类别可在设置页关闭)——
/// <list type="bullet">
/// <item>活动临期:版本活动 + 换取活动(卡池)距结束 ≤3 天提醒,与活动页忽略名单互通(每 6 小时,按天去重)</item>
/// <item>登录状态:库街区/云鸣潮/攻略站会话校验,失效提醒「前往账号页」(每 2 小时,按天去重)</item>
/// <item>战歌同奏(周本):本周最后一天(周日/周一 4 点前)仍有剩余次数提醒(每小时,按周去重)</item>
/// <item>每日活跃度:19 点后仍为 0 提醒(每小时,按天去重)</item>
/// <item>赛季临期:逆境深塔 / 冥歌海墟 / 终焉矩阵剩余 ≤3 天且未满额完成时提醒(每 6 小时,按赛季去重)</item>
/// </list>
/// 检查失败静默(网络异常/风控不告警,避免误报);每日进度本地 SDK 数据优先,失败回退库街区接口。
/// </summary>
public sealed class ReminderScheduler : IDisposable
{
    private static readonly TimeSpan Cycle = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan LoginInterval = TimeSpan.FromHours(2);
    private static readonly TimeSpan ActivityInterval = TimeSpan.FromHours(6);
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromHours(1);

    /// <summary>
    /// 赛季临期检查间隔。比活动临期(6 小时)保持同档:判据是"剩余毫秒",一天内跨过 3 天线的时刻
    /// 不固定,6 小时粒度最坏晚 6 小时提醒 —— 对还剩 3 天的窗口足够。
    /// </summary>
    private static readonly TimeSpan TowerInterval = TimeSpan.FromHours(6);

    /// <summary>活动临期阈值(与活动页 <c>ExpiringSoonDays</c> 一致:距结束 ≤3 天)。</summary>
    private static readonly TimeSpan ExpiringWindow = TimeSpan.FromDays(3);

    /// <summary>等出口 IP 探测完成的上限:超时照常检查(单次失败静默,下一轮再试)。</summary>
    private static readonly TimeSpan NetworkInitTimeout = TimeSpan.FromSeconds(5);

    /// <summary>首轮提醒在窗口打开后至少再等 2 秒:窗口 Opened 时首屏数据仍在加载,
    /// 立刻弹卡片会盖在还没初始化完的界面上。</summary>
    private static readonly TimeSpan FirstRoundDelay = TimeSpan.FromSeconds(2);

    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;
    private DateTime _lastLogin;
    private DateTime _lastActivity;
    private DateTime _lastProgress;
    private DateTime _lastTower;

    /// <summary>
    /// 启动时的出口 IP 探测任务(可为 null = 不等)。库街区接口的 ip 头未就绪时可能触发风控,
    /// 首轮检查前最多等它 <see cref="NetworkInitTimeout"/>;探测失败/超时照常检查。
    /// </summary>
    private readonly Task? _networkInit;

    public ReminderScheduler(Task? networkInit = null)
        => _networkInit = networkInit;

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
            // 首轮由 App 在主窗口 Opened 之后再 Start。窗口 Opened 只是窗口可渲染,
            // 首屏数据(主页/启动页)仍在加载 —— 立刻弹卡片会盖在没初始化完的界面上,
            // 所以先等固定 2 秒让界面稳定;同时等出口 IP 探测(库街区接口的 ip 头)完成,
            // 带超时兜底。签到提醒已取消,原先「等自动签到结束」的依赖一并移除。
            var settle = Task.Delay(FirstRoundDelay, ct);
            if (_networkInit is not null)
            {
                await Task.WhenAny(_networkInit, Task.Delay(NetworkInitTimeout, ct)).ConfigureAwait(false);
            }
            await settle.ConfigureAwait(false);
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
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // 正常退出(只认本调度器自己的取消;HttpClient 超时等其他取消已在 SafeAsync 内吞掉)
        }
        catch (Exception ex)
        {
            // 调度循环异常静默:提醒属辅助功能,不因轮询失败影响主程序
            Log()?.LogWarning(ex, "提醒调度循环异常退出");
        }
    }

    /// <summary>日志(容器未初始化时为 null,例如单测)。</summary>
    private static ILogger? Log()
        => AppServices.LoggerFactory?.CreateLogger<ReminderScheduler>();

    private async Task RunDueChecksAsync(CancellationToken ct)
    {
        var now = DateTime.Now;
        // 总开关关闭:连轮询都不做(省掉这条链路的网络请求与读数盘开销)。
        // 类别开关仍照旧参与判定:总开关重新打开后各类别按自身状态原样恢复。
        if (!AppServices.Settings.Current.NotifEnabled)
        {
            return;
        }
        var settings = AppServices.Settings.Current;
        // 各项检查彼此独立:并发执行,避免某一项的网络耗时拖慢其它提醒
        var due = new List<Task>(4);
        // 各项检查独立吞异常:单项失败(网络抖动)不影响其他提醒
        if (settings.NotifLoginEnabled && now - _lastLogin >= LoginInterval)
        {
            _lastLogin = now;
            due.Add(SafeAsync(() => CheckLoginAsync(ct), nameof(CheckLoginAsync), ct));
        }
        if (settings.NotifActivityEnabled && now - _lastActivity >= ActivityInterval)
        {
            _lastActivity = now;
            due.Add(SafeAsync(() => CheckActivityAsync(ct), nameof(CheckActivityAsync), ct));
        }
        if (ReminderWindows.IsDailyProgressDue(now) && now - _lastProgress >= ProgressInterval)
        {
            _lastProgress = now;
            due.Add(SafeAsync(() => CheckDailyProgressAsync(ct), nameof(CheckDailyProgressAsync), ct));
        }
        // 赛季临期(深塔/海墟/矩阵):不受时间窗限制,任何时刻剩余 ≤3 天都算(与活动临期同口径)
        if (settings.NotifTowerEnabled && now - _lastTower >= TowerInterval)
        {
            _lastTower = now;
            due.Add(SafeAsync(() => CheckTowerSeasonAsync(ct), nameof(CheckTowerSeasonAsync), ct));
        }
        if (due.Count > 0)
        {
            await Task.WhenAll(due).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 单项检查失败静默(记日志)。只有调度器自身取消才向上抛:
    /// HttpClient.Timeout 触发的 <see cref="TaskCanceledException"/> 也是 <see cref="OperationCanceledException"/>,
    /// 若一律重抛,一次网络超时就会让整个提醒循环在本次会话内永久停止。
    /// </summary>
    private static async Task SafeAsync(Func<Task> check, string name, CancellationToken ct)
    {
        try
        {
            await check().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 单项检查失败静默(网络抖动/超时),下一轮再试
            Log()?.LogInformation("提醒检查 {Check} 失败: {Error}", name, ex.Message);
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

    /// <summary>每日进度:活跃度 19 点后为 0 / 战歌同奏(周本)最后一天仍有剩余(本地 SDK 数据优先,省接口)。</summary>
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
                // 按游戏日(服务器 UTC+8、4 点重置)去重,与本机时区无关
                $"progress:liveness:{ReminderWindows.DayKey(now)}",
                NotificationKind.DailyLiveness,
                LanguageService.Format("Notif.LivenessTitle"),
                LanguageService.Format("Notif.LivenessMessage", limit),
                NavigationKeys.Home);
        }

        // 战歌同奏(周本):本周最后一天仍有剩余次数提醒(按周去重)。
        // 数据服务层已把接口的"剩余次数"换算为已完成:Cur=已完成、Total=上限,
        // 剩余 = Total - Cur;剩余 > 0 才算没打完(Cur==Total 即完成,不再打扰)。
        var weekly = data.WeeklyData;
        if (weekly is not null
            && AppServices.Settings.Current.NotifWeeklyEnabled
            && ReminderWindows.IsWeeklyLastDay(now))
        {
            var weeklyTotal = ResolveWeeklyTotal(data, weekly);
            var remaining = weeklyTotal - weekly.Cur;
            if (remaining > 0)
            {
                AppServices.Reminders.Raise(
                    $"progress:weekly:{ReminderWindows.WeekKey(now)}",
                    NotificationKind.WeeklyBoss,
                    LanguageService.Format("Notif.WeeklyTitle"),
                    LanguageService.Format("Notif.WeeklyMessage", remaining),
                    NavigationKeys.Home);
            }
        }
    }

    /// <summary>战歌同奏(周本)次数上限:项自身 total → 数据中心 weeklyInstCountLimit → 回退 3(与首页一致)。</summary>
    private static int ResolveWeeklyTotal(RoleDailyData data, RoleDailyDetail weekly)
        => weekly.Total > 0 ? weekly.Total : data.WeeklyLimit > 0 ? data.WeeklyLimit : UserProfile.WeeklyFallbackLimit;

    /// <summary>
    /// 赛季临期:逆境深塔 / 冥歌海墟 / 终焉矩阵剩余 ≤3 天且未满额完成时各提醒一条。
    /// <para>
    /// 三类共用一次拉取(<see cref="TowerService.GetSeasonSnapshotAsync"/>,只打 3 个赛季接口),
    /// 各自独立判定与去重 —— 一项失败只少那一项的提醒,不会连坐。
    /// </para>
    /// <para>
    /// 去重 Key 用「玩法 + 赛季结束日期」:<b>按天去重本身由台账负责</b>
    /// (判据含当天日期),所以最后 3 天里每天各提醒一次(与活动临期同节奏);
    /// 而赛季一旦翻新,结束日期变了 Key 也就变了,不会被上一期的旧记录吞掉新一期。
    /// </para>
    /// <para>
    /// 曾经把「结束时刻精确到小时」也拼进 Key —— 那是错的:剩余毫秒在两次检查之间会漂移,
    /// 落在整点边界附近时归整结果会翻到下一个小时,同一天内凭空换出第二个 Key、弹第二遍。
    /// </para>
    /// </summary>
    private static async Task CheckTowerSeasonAsync(CancellationToken ct)
    {
        var now = DateTime.Now;
        var snapshot = await AppServices.Tower.GetSeasonSnapshotAsync(ct).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(snapshot.Error))
        {
            // 未登录/令牌失效:静默(登录链路自己会提醒会话失效,这里再报一次是重复噪音)
            return;
        }

        // 每项独立判定:完成度在数据缺失时为 null(不抑制提醒)
        RaiseIfExpiring(
            TowerReminderPolicy.Evaluate(
                TowerKind.DeepTower, now, snapshot.Tower?.SeasonEndTime,
                TowerReminderPolicy.DeepTowerStars(snapshot.Tower)),
            LanguageService.Format("Notif.TowerDeepTitle"));
        RaiseIfExpiring(
            TowerReminderPolicy.Evaluate(
                TowerKind.Slash, now, snapshot.Slash?.SeasonEndTime,
                TowerReminderPolicy.SlashScore(snapshot.Slash)),
            LanguageService.Format("Notif.TowerSlashTitle"));
        RaiseIfExpiring(
            TowerReminderPolicy.Evaluate(
                TowerKind.Matrix, now, snapshot.Matrix?.EndTime,
                TowerReminderPolicy.MatrixModes(snapshot.Matrix)),
            LanguageService.Format("Notif.TowerMatrixTitle"));
    }

    /// <summary>把一条临期判定落成悬浮提醒(判定为 null 时什么都不做)。</summary>
    private static void RaiseIfExpiring(TowerSeasonExpiry? expiry, string title)
    {
        if (expiry is null)
        {
            return;
        }
        // 进度文案:有可判定的完成度才带上"已完成 X/Y",否则只说还剩多久
        var message = expiry.Progress is { } p
            ? LanguageService.Format("Notif.TowerMessageWithProgress",
                FormatTowerRemaining(expiry.Remaining), p.Done, p.Total, TowerProgressUnitSuffix(p.Unit))
            : LanguageService.Format("Notif.TowerMessage", FormatTowerRemaining(expiry.Remaining));
        AppServices.Reminders.Raise(
            TowerReminderKey(expiry),
            NotificationKind.TowerEnding,
            title,
            message,
            NavigationKeys.Tower);
    }

    /// <summary>
    /// 剩余时长短语(自带前后空格与"后",直接嵌进 <c>Notif.TowerMessage*</c> 的 {0})。
    /// <para>
    /// 文案整体由语言包拼装,所以这里只产出「 3 天 2 小时后」「 5 小时后」「不到 1 小时」这样的片段:
    /// 中文模板写成「本期还剩{0}结束」,前导空格由本方法提供 —— 否则会出现"还剩不到 1 小时"
    /// 与"还剩 3 小时后"两处空格数量不一致。
    /// </para>
    /// <para>整点天数不给"0 小时"(「3 天 0 小时后」读起来像 bug),改用 <c>Notif.TowerRemainDaysExact</c>。</para>
    /// </summary>
    internal static string FormatTowerRemaining(TimeSpan remaining)
    {
        if (remaining.TotalDays >= 1)
        {
            var days = (int)remaining.TotalDays;
            return remaining.Hours == 0
                ? LanguageService.Format("Notif.TowerRemainDaysExact", days)
                : LanguageService.Format("Notif.TowerRemainDays", days, remaining.Hours);
        }
        if (remaining.TotalHours >= 1)
        {
            return LanguageService.Format("Notif.TowerRemainHours", (int)remaining.TotalHours);
        }
        return LanguageService.Format("Notif.TowerRemainSoon");
    }

    /// <summary>进度单位文案(星/分/模式)。</summary>
    private static string TowerProgressUnitSuffix(TowerProgressUnit unit)
        => LanguageService.Format(unit switch
        {
            TowerProgressUnit.Stars => "Notif.TowerUnitStars",
            TowerProgressUnit.Score => "Notif.TowerUnitScore",
            _ => "Notif.TowerUnitModes",
        });

    /// <summary>
    /// 赛季临期提醒的去重 Key:只到「天」。
    /// <para>
    /// 两个约束必须同时满足,故放在纯函数层由测试盯住:
    /// ①<b>同一天内稳定</b> —— 剩余毫秒每轮都在减,Key 里一旦带上小时/分钟,
    ///   同一天内会不断换出新 Key,变成每次检查都弹;
    /// ②<b>跨赛季可区分</b> —— 不同期的结束日不同,所以按结束日期做 Key 天然分得开。
    /// 当天的重复弹压在 <see cref="ReminderLedgerService.TryMarkShown"/> 的「Key + 当天日期」判据里。
    /// </para>
    /// </summary>
    private static string TowerReminderKey(TowerSeasonExpiry expiry) => TowerReminderPolicy.ReminderKey(expiry);

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
        // 只取消,不立即 Dispose:后台循环此刻可能仍在 WaitForNextTickAsync / 网络请求里
        // 使用这个 token,释放 CTS 后再触到 token 会抛 ObjectDisposedException(竞态窗口内偶发),
        // 虽然循环里吞掉了异常,但那类异常会掩盖真实的关闭问题。
        // 进程退出时 CTS 由 GC 回收;等到循环真正结束再释放没有收益,故这里不做 Dispose。
        try
        {
            _cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 重复 Dispose:忽略
        }
    }
}
