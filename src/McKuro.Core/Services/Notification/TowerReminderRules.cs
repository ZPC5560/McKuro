using System.Globalization;
using McKuro.Core.Models.Tower;

namespace McKuro.Core.Services.Notification;

/// <summary>赛季制挑战玩法(提醒文案与跳转目标按它区分)。</summary>
public enum TowerKind
{
    /// <summary>逆境深塔(towerDataDetail,按星数计进度)。</summary>
    DeepTower,
    /// <summary>冥歌海墟(slashDetail,按积分计进度)。</summary>
    Slash,
    /// <summary>终焉矩阵(newTowerDetail,按已通关模式数计进度)。</summary>
    Matrix,
}

/// <summary>进度单位(决定提醒文案用「星」/「分」/「模式」)。</summary>
public enum TowerProgressUnit
{
    Stars,
    Score,
    Modes,
}

/// <summary>某一期挑战的完成度(Done/Total 语义随 <see cref="Unit"/> 变化)。</summary>
/// <param name="Done">已完成量(星数/积分/已通关模式数)。</param>
/// <param name="Total">满额量;≤0 表示接口没给出总量,完成度不可判定。</param>
public sealed record TowerProgress(int Done, int Total, TowerProgressUnit Unit)
{
    /// <summary>是否已满额完成。总量未知时一律按「未完成」处理 —— 宁可多提醒一次,也不要漏掉。</summary>
    public bool IsComplete => Total > 0 && Done >= Total;
}

/// <summary>一条「赛季即将结束」判定结果。</summary>
/// <param name="Kind">玩法。</param>
/// <param name="Remaining">距赛季结束的剩余时长(恒为正)。</param>
/// <param name="EndAt">赛季结束的绝对时刻(由「当前时刻 + 剩余毫秒」折算)。</param>
/// <param name="Progress">完成度;null = 接口未提供可判定的数据。</param>
public sealed record TowerSeasonExpiry(
    TowerKind Kind,
    TimeSpan Remaining,
    DateTime EndAt,
    TowerProgress? Progress);

/// <summary>
/// 深塔/海墟/矩阵「赛季剩余 3 天内」提醒的判定(纯函数,供提醒调度器与测试共用)。
/// <para>
/// 三个接口的结束时间字段(<c>towerDataDetail.seasonEndTime</c> / <c>slashDetail.seasonEndTime</c> /
/// <c>newTowerDetail.endTime</c>)都是<b>剩余毫秒</b>而不是绝对时间戳,所以绝对结束时刻必须由
/// 「拉取时刻 + 剩余毫秒」折算 —— 本类只做判定,<see cref="TowerSeasonExpiry.EndAt"/> 由调用方
/// 传入的 now 决定,便于单测与重放。
/// </para>
/// <para>
/// 判定口径与「战歌同奏收尾提醒」一致:<b>只剩不到 3 天 且 尚未满额完成</b>才提醒。
/// 已经全部拿满的赛季不再打扰 —— 否则每个赛季末用户都会收到一条"还剩 3 天"却无事可做的通知。
/// 完成度不可判定(接口没给总量)时按未完成处理,宁可多提醒一次也不要漏。
/// </para>
/// </summary>
public static class TowerReminderPolicy
{
    /// <summary>临期阈值:距赛季结束 ≤3 天(与活动临期提醒同口径)。</summary>
    public static readonly TimeSpan ExpiringWindow = TimeSpan.FromDays(3);

    /// <summary>
    /// 判定某一期是否需要提醒:剩余时长落在 (0, <paramref name="window"/>] 内且尚未满额完成。
    /// </summary>
    /// <param name="kind">玩法。</param>
    /// <param name="now">判定时刻(用于把剩余毫秒折算成绝对结束时刻)。</param>
    /// <param name="remainingMillis">接口下发的剩余毫秒;null/≤0 一律不提醒。</param>
    /// <param name="progress">完成度;null = 不可判定(不抑制提醒)。</param>
    /// <param name="window">临期阈值,默认 <see cref="ExpiringWindow"/>。</param>
    public static TowerSeasonExpiry? Evaluate(
        TowerKind kind,
        DateTime now,
        long? remainingMillis,
        TowerProgress? progress,
        TimeSpan? window = null)
    {
        // 剩余毫秒 ≤0:这一期已经结束(库街区此时返回的是上一期残留数据),不该再提醒"即将结束"
        if (remainingMillis is not { } ms || ms <= 0)
        {
            return null;
        }
        var remaining = TimeSpan.FromMilliseconds(ms);
        if (remaining > (window ?? ExpiringWindow))
        {
            return null;
        }
        // 已满额完成:赛季怎么刷新都无事可做,提醒只会变成噪音
        if (progress is { IsComplete: true })
        {
            return null;
        }
        return new TowerSeasonExpiry(kind, remaining, now.Add(remaining), progress);
    }

    /// <summary>
    /// 逆境深塔完成度:全部难度的「已得星 / 满额星」之和。
    /// 接口未给出任何满额星(<c>maxStar</c> 全为 0)时返回 null(完成度不可判定)。
    /// </summary>
    public static TowerProgress? DeepTowerStars(TowerSeasonData? data)
    {
        var areas = data?.DifficultyList?
            .SelectMany(d => d.TowerAreaList ?? [])
            .ToList();
        if (areas is not { Count: > 0 })
        {
            return null;
        }
        var max = areas.Sum(a => a.MaxStar);
        if (max <= 0)
        {
            return null;
        }
        // 已得星钳到满额:接口偶发单区 star 超过 maxStar(三星楼层显示 4),不钳会让进度像"超额完成"
        var done = areas.Sum(a => Math.Clamp(a.Star, 0, Math.Max(a.MaxStar, 0)));
        return new TowerProgress(done, max, TowerProgressUnit.Stars);
    }

    /// <summary>
    /// 冥歌海墟完成度:再生海域(difficulty=1)的「总积分 / 满积分」。
    /// 湍渊(difficulty=2)是独立挑战,这里不并入 —— 与页面「总积分」口径一致。
    /// </summary>
    public static TowerProgress? SlashScore(SlashData? data)
    {
        var regen = data?.DifficultyList?.FirstOrDefault(d => d.Difficulty == 1);
        if (regen is null || regen.MaxScore <= 0)
        {
            return null;
        }
        return new TowerProgress(Math.Clamp(regen.AllScore, 0, regen.MaxScore), regen.MaxScore, TowerProgressUnit.Score);
    }

    /// <summary>
    /// 终焉矩阵完成度:已打完(该模式 boss 全部通关)的模式数 / 模式总数。
    /// 模式列表为空时返回 null。
    /// <para>
    /// <b>完成判据是 <c>passBoss &gt;= bossCount</c>,不是 <c>hasRecord &amp;&amp; score &gt; 0</c></b>:
    /// 后者只说明"这一期打过、有分",不代表打满。实测报文里出现过
    /// <c>[id=1 hasRecord=true score=58454 passBoss=4 bossCount=5 rank=7]</c> ——
    /// 奇点扩张 5 个 boss 只打了 4 个,却因为"有记录有分"被当成已完成,
    /// 于是赛季剩 2 天时整个类别一条提醒都不弹(用户反馈的正是这个)。
    /// </para>
    /// <para>
    /// <c>bossCount</c> 为 0 表示接口没给总量,该模式按<b>未完成</b>计入 —— 与
    /// <see cref="TowerProgress.IsComplete"/>「总量未知一律按未完成」的口径一致:
    /// 宁可多提醒一次,也不要漏掉。
    /// </para>
    /// </summary>
    public static TowerProgress? MatrixModes(NewTowerData? data)
    {
        var modes = data?.ModeDetails;
        if (modes is not { Count: > 0 })
        {
            return null;
        }
        var done = modes.Count(m => m.BossCount > 0 && m.PassBoss >= m.BossCount);
        return new TowerProgress(done, modes.Count, TowerProgressUnit.Modes);
    }

    /// <summary>
    /// 玩法 → 塔页页签序号(0=逆境深塔,1=终焉矩阵,2=海墟)。
    /// <para>
    /// 提醒卡片的「前往」只带一个页面 key(<c>Tower</c>),不带页签信息,于是点开永远停在
    /// 默认的「逆境深塔」页签 —— 用户收到「终焉矩阵即将刷新」,点过去看到的却是深塔。
    /// 把页签序号拼进导航 key(<c>Tower#1</c>)即可直达;页签顺序由
    /// <c>TowerView.axaml</c> 的 TabItem 顺序决定,改动那里必须同步这里。
    /// </para>
    /// </summary>
    public static int TabIndexOf(TowerKind kind) => kind switch
    {
        TowerKind.DeepTower => 0,
        TowerKind.Matrix => 1,
        TowerKind.Slash => 2,
        _ => 0,
    };

    /// <summary>塔页的页面导航 key(与 <c>NavigationKeys.Tower</c> 同值;Core 层不引用 UI 命名空间)。</summary>
    public const string TowerPageKey = "Tower";

    /// <summary>页面 key 与页内定位的分隔符(与 <c>NavigationKeys.TabSeparator</c> 同值)。</summary>
    public const char TabSeparator = '#';

    /// <summary>
    /// 组装「塔页 + 指定玩法页签」的导航 key,如 <c>Tower#1</c>(终焉矩阵)。
    /// 放在 Core 层是为了让测试直接覆盖(UI 层没有 InternalsVisibleTo)。
    /// </summary>
    public static string TowerNavKey(TowerKind kind)
        => $"{TowerPageKey}{TabSeparator}{TabIndexOf(kind)}";

    /// <summary>
    /// 赛季临期提醒的去重 Key:只到「天」。
    /// <para>
    /// 两个约束必须同时满足,故放在纯函数层由测试盯住:
    /// ①<b>同一天内稳定</b> —— 剩余毫秒每次检查都在减,Key 里一旦带上小时/分钟,
    ///   同一天内会不断换出新 Key,变成每次检查都弹一遍;
    /// ②<b>跨赛季可区分</b> —— 不同期的结束日不同,按结束日期做 Key 天然分得开,
    ///   否则上一期的旧台账会把新一期吞掉。
    /// </para>
    /// <para>
    /// 「当天不再重复」由台账的「Key + 当天日期」判据负责
    /// (<see cref="ReminderLedgerService.TryMarkShown"/>),这里只管"哪一期的哪一种玩法"。
    /// </para>
    /// <para>
    /// 日期取<b>服务器时间</b>(<see cref="ReminderWindows.ToServerTime"/>):赛季在服务器 04:00 结束,
    /// 折算到本机时间后,某些时区(如 UTC+4 → 本机 00:00)恰好落在午夜,剩余毫秒的秒级抖动就会让
    /// 本机日期来回翻转,同一期换出两个 Key、重复提醒。服务器日期离 04:00 边界很远,不受抖动影响。
    /// </para>
    /// </summary>
    public static string ReminderKey(TowerSeasonExpiry expiry)
        => string.Create(CultureInfo.InvariantCulture,
            $"tower:{expiry.Kind}:{ReminderWindows.ToServerTime(expiry.EndAt):yyyyMMdd}");
}
