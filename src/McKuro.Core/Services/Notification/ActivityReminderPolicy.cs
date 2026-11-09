using System.Globalization;

namespace McKuro.Core.Services.Notification;

/// <summary>
/// 活动临期提醒的判定口径与稳定 Key(纯函数,提醒调度器与活动页共用)。
/// <para>
/// 版本活动与换取活动(卡池)共用<b>同一份</b>忽略名单
/// (<c>AppSettings.IgnoredEndingActivityIds</c>),所以 Key 的拼法必须只有一处实现 ——
/// 此前两边各写一份 <c>$"{标题}|{开始:yyyyMMddHHmm}|{结束:yyyyMMddHHmm}"</c>,
/// 卡池那条链路干脆没接忽略判定,表现为"在活动页把活动忽略了,重新打开软件照样弹
/// 《活动即将结束》"。
/// </para>
/// <para>
/// <b>Key 里带的是"已本地化的显示名"</b>(调用方负责把空标题替换成 fallback 文案),
/// 不是原始字段:历史忽略名单就是按这个格式落盘的,改成原始字段会让老用户的忽略记录
/// 全部失效、把早就忽略过的活动重新弹一遍。代价是切换界面语言后 Key 会变,这与
/// Key 引入时(1.3.x)的行为一致,本次不动。
/// </para>
/// </summary>
public static class ActivityReminderPolicy
{
    /// <summary>临期阈值:距结束 ≤3 天(活动页标红、提醒调度器、深塔赛季临期三处同口径)。</summary>
    public static readonly TimeSpan ExpiringWindow = TimeSpan.FromDays(3);

    /// <summary>卡池 Key 前缀(与版本活动共用一份忽略名单,靠前缀区分两类)。</summary>
    public const string PoolKeyPrefix = "pool:";

    /// <summary>
    /// 是否临期:尚未结束(end ≥ now)且剩余时长不超过 <paramref name="window"/>。
    /// <para>刚结束那一刻(end == now)仍算临期 —— 与调度器原判据 <c>end &lt; now → 跳过</c> 一致。</para>
    /// </summary>
    public static bool IsExpiring(DateTime end, DateTime now, TimeSpan? window = null)
        => end >= now && end - now <= (window ?? ExpiringWindow);

    /// <summary>版本活动 Key(标题 + 起止时间;活动页甘特条与提醒调度器必须一致)。</summary>
    public static string VersionKey(string? displayTitle, DateTime start, DateTime end)
        => Compose(displayTitle, start, end);

    /// <summary>换取活动(卡池)Key(名称 + 起止时间,带 <c>pool:</c> 前缀)。</summary>
    public static string PoolKey(string? displayName, DateTime start, DateTime end)
        => PoolKeyPrefix + Compose(displayName, start, end);

    /// <summary>该 Key 是否已在忽略名单内(<paramref name="ignored"/> 为 null 视为空名单)。</summary>
    public static bool IsIgnored(IEnumerable<string>? ignored, string key)
        => ignored is not null && ignored.Contains(key, StringComparer.Ordinal);

    /// <summary>
    /// 清掉「活动已经结束一段时间」的忽略记录,返回新列表(顺序保持,重复项去除)。
    /// <para>
    /// 判据只看 Key 里自带的结束时刻(<c>名称|开始|结束</c> 的末段),<b>不依赖当前页看到了什么</b> ——
    /// 旧实现按"当前页是否可见"裁剪,一旦接口某次少返回一条、或只加载到其中一类数据,
    /// 用户的忽略记录就被永久抹掉,下次又弹。这类失败对用户是无声的,所以宁可让记录多留几天。
    /// </para>
    /// <para>无法解析的 Key 一律保留:宁可留垃圾,也不要误删用户刚点下的忽略。</para>
    /// </summary>
    public static List<string> PruneExpired(IEnumerable<string>? ignored, DateTime now, TimeSpan? retention = null)
    {
        var kept = new List<string>();
        if (ignored is null)
        {
            return kept;
        }
        var cutoff = now - (retention ?? DefaultIgnoreRetention);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in ignored)
        {
            if (string.IsNullOrWhiteSpace(key) || !seen.Add(key))
            {
                continue;
            }
            // 解析不出结束时刻就保留(见 summary)
            if (TryReadEnd(key, out var end) && end < cutoff)
            {
                continue;
            }
            kept.Add(key);
        }
        return kept;
    }

    /// <summary>忽略记录在活动结束后继续保留的时长(活动结束后本就再无提醒,留几天只是容错)。</summary>
    public static readonly TimeSpan DefaultIgnoreRetention = TimeSpan.FromDays(7);

    /// <summary>从 Key 末段读取活动结束时刻(<c>名称|开始|结束</c>;名称本身可能含分隔符,故从尾部取)。</summary>
    private static bool TryReadEnd(string key, out DateTime end)
    {
        end = default;
        var lastSep = key.LastIndexOf('|');
        if (lastSep < 0 || lastSep + 1 >= key.Length)
        {
            return false;
        }
        return DateTime.TryParseExact(key[(lastSep + 1)..], "yyyyMMddHHmm",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out end);
    }

    private static string Compose(string? displayName, DateTime start, DateTime end)
        => $"{displayName ?? ""}|{Stamp(start)}|{Stamp(end)}";

    private static string Stamp(DateTime time)
        => time.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture);
}
