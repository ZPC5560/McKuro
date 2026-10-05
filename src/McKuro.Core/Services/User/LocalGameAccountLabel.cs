namespace McKuro.Core.Services.User;

/// <summary>
/// 「接口账号 → 游戏角色」显示口径(账号页三张卡片 + 签到页统一走这里)。
/// <para>
/// 背景:库街区/云鸣潮/mcguide 各自返回的是<b>接口账号</b>(库街区 UID、账号名 U123…A、手机号),
/// 用户在界面上要看的却是<b>游戏角色</b>的昵称与游戏角色 UID(与首页「今日数据」账号卡片同一口径,
/// 也与抽卡页显示的 player_id 对齐)。本地官方启动器凭证
/// (<c>KRSDKUserLauncherCache.json</c> + <c>game/queryPlayerInfo</c>)是这三个接口唯一共有的
/// 「游戏角色」来源,故统一按手机号 → 账号名 → 库街区 UID 依次匹配。
/// </para>
/// <para>纯函数、无 IO:便于单测覆盖匹配优先级与格式化边界。</para>
/// </summary>
public static class LocalGameAccountLabel
{
    /// <summary>昵称与游戏角色 UID 的分隔符(用户选定口径:「以椿为鸣 · 103242935」)。</summary>
    public const string Separator = " · ";

    /// <summary>
    /// 格式化游戏角色身份:昵称 + 游戏角色 UID。
    /// 只有 UID 时只显示 UID,只有昵称时只显示昵称,都没有返回空串(绝不编造占位)。
    /// </summary>
    public static string Format(string? roleName, string? roleId)
    {
        var name = roleName?.Trim() ?? "";
        var id = roleId?.Trim() ?? "";
        if (name.Length == 0)
        {
            return id;
        }
        return id.Length == 0 ? name : name + Separator + id;
    }

    /// <summary>
    /// 在本地启动器凭证枚举出的角色里定位「同一个账号」的角色。
    /// <para>匹配优先级(首个命中即返回,全部为精确/忽略大小写比较,不做模糊包含以免串号):</para>
    /// 1. 手机号(三个接口唯一共有且稳定的标识);
    /// 2. 账号名(形如 U536781653A;云鸣潮/mcguide 有,库街区块可能没有);
    /// 3. 库街区 UID(云鸣潮没有,mcguide/库街区有)。
    /// <para>同一凭证可能有多个服务器角色:先用 <paramref name="preferredRoleId"/> 选中当前应用正在用的角色,
    /// 否则取第一个(枚举顺序稳定)。</para>
    /// </summary>
    public static LocalLauncherPlayer? Match(
        IReadOnlyList<LocalLauncherPlayer>? players,
        string? phone = null,
        string? userName = null,
        string? kuroUid = null,
        string? preferredRoleId = null)
    {
        if (players is null || players.Count == 0)
        {
            return null;
        }
        var wanted = Pick(MatchBy(players, phone, static (p, v) => Same(p.Phone, v)), preferredRoleId)
            ?? Pick(MatchBy(players, userName, static (p, v) => Same(p.Username, v)), preferredRoleId)
            ?? Pick(MatchBy(players, kuroUid, static (p, v) => Same(p.KuroUid, v)), preferredRoleId);
        return wanted;
    }

    /// <summary>
    /// 组合入口:匹配 → 格式化为「昵称 · 游戏角色 UID」。
    /// 匹配不到返回 <c>null</c>,由调用方回退到接口自己的显示文案(不显示半截信息)。
    /// </summary>
    public static string? Resolve(
        IReadOnlyList<LocalLauncherPlayer>? players,
        string? phone = null,
        string? userName = null,
        string? kuroUid = null,
        string? preferredRoleId = null)
    {
        var player = Match(players, phone, userName, kuroUid, preferredRoleId);
        if (player is null)
        {
            return null;
        }
        var text = Format(player.RoleName, player.RoleId);
        return text.Length == 0 ? null : text;
    }

    /// <summary>
    /// 显示文案 = 匹配到的游戏角色身份;匹配不到(或角色信息为空)时用 <paramref name="fallback"/>。
    /// 保证调用方拿到的永远是可见文案,且回退分支与匹配分支互斥、不会拼出半截信息。
    /// </summary>
    public static string ResolveOrFallback(
        IReadOnlyList<LocalLauncherPlayer>? players,
        string? phone,
        string? userName,
        string? kuroUid,
        string fallback,
        string? preferredRoleId = null)
        => Resolve(players, phone, userName, kuroUid, preferredRoleId) ?? fallback;

    /// <summary>
    /// 多账号标题栏文案:逐个渲染并以 <paramref name="separator"/> 连接。
    /// 空集合返回空串(由调用方换成「未登录」文案);空白项被跳过,避免出现「A /  / B」。
    /// </summary>
    public static string Join(IEnumerable<string?> parts, string separator = " / ")
    {
        var kept = parts
            .Select(p => p?.Trim() ?? "")
            .Where(p => p.Length > 0);
        return string.Join(separator, kept);
    }

    private static List<LocalLauncherPlayer> MatchBy(
        IReadOnlyList<LocalLauncherPlayer> players,
        string? value,
        Func<LocalLauncherPlayer, string, bool> predicate)
    {
        var hits = new List<LocalLauncherPlayer>();
        var trimmed = value?.Trim() ?? "";
        if (trimmed.Length == 0)
        {
            return hits;
        }
        foreach (var player in players)
        {
            if (predicate(player, trimmed))
            {
                hits.Add(player);
            }
        }
        return hits;
    }

    private static LocalLauncherPlayer? Pick(List<LocalLauncherPlayer> hits, string? preferredRoleId)
        => hits.Count == 0
            ? null
            : !string.IsNullOrWhiteSpace(preferredRoleId)
                ? hits.FirstOrDefault(p => Same(p.RoleId, preferredRoleId!)) ?? hits[0]
                : hits[0];

    private static bool Same(string? a, string b)
        => !string.IsNullOrWhiteSpace(a) && string.Equals(a.Trim(), b, StringComparison.OrdinalIgnoreCase);
}
