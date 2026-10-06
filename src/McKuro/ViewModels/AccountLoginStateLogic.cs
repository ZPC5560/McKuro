using McKuro.Core.Models.Kuro;

namespace McKuro.ViewModels;

/// <summary>
/// 账号页「接口登录状态点」的判定逻辑(纯函数,不依赖 AppServices / UI,便于单测)。
///
/// <para><b>为什么单独抽出来</b>:<see cref="AccountViewModel"/> 依赖静态容器与 Avalonia
/// DispatcherTimer,无法在单元测试里隔离构造;而这里的判定规则正是此前出问题的地方
/// (见 <see cref="ResolveKuroDot"/> 的说明),必须可测。</para>
/// </summary>
public static class AccountLoginStateLogic
{
    /// <summary>
    /// 页面重绘(如 <c>RefreshAccounts</c>)时库街区状态点应显示什么。
    ///
    /// <para><b>关键规则:有已知失效结论时不得刷回绿点。</b></para>
    /// <para>
    /// 曾经的缺陷:该方法等价于「有账号就 Ok」,而账号页加载时 <c>RefreshGameRoleLabelsAsync</c>
    /// 会异步调用 <c>RefreshAccounts</c>。它与会话校验并发,校验刚判出「登录已失效(橙点)」,
    /// 随后这次重绘又把状态无条件写回绿点 —— 表现为"账号页看不出登录已失效,
    /// 直到去角色数据页点同步才发现"。
    /// </para>
    /// </summary>
    /// <param name="currentUserId">当前库街区账号 UserId;<c>null</c> 表示未登录。</param>
    /// <param name="isKnownExpired">该账号是否已被会话校验判定失效。</param>
    public static InterfaceLoginState ResolveKuroDot(string? currentUserId, bool isKnownExpired)
        => string.IsNullOrEmpty(currentUserId)
            ? InterfaceLoginState.NotLoggedIn
            : isKnownExpired ? InterfaceLoginState.Error : InterfaceLoginState.Ok;

    /// <summary>库街区会话探针的三种结果(「失效」与「无法判定」必须区分:后者不得改动现有状态)。</summary>
    public enum KuroSessionProbe
    {
        /// <summary>服务端确认有效(code 200 且 data 非空):可清除失效标记、转绿点。</summary>
        Valid,
        /// <summary>服务端明确拒绝(非 200 / 无 data):判定登录已失效,记橙点。</summary>
        Rejected,
        /// <summary>未拿到响应(null:网络失败/超时/响应体不可解析):无法判定,不应改变现有状态。</summary>
        Unreachable,
    }

    /// <summary>
    /// 库街区会话校验结果的三分判定。
    /// <para>
    /// <see cref="IsKuroSessionExpired"/> 只回答「是否失效」二值问题,调用方若把
    /// 「未拿到响应(null)」也当成「未失效」处理,就会在一次网络抖动后把已判定的
    /// 「会话失效(橙点)」错误地清回绿点 —— 表现为账号页看不到失效提示(2026-10 评审发现)。
    /// 故调用方应使用本方法按三分支处理:<b>只有 <see cref="KuroSessionProbe.Valid"/>
    /// 才允许清除失效标记</b>,<see cref="KuroSessionProbe.Unreachable"/> 不改任何状态。
    /// </para>
    /// </summary>
    /// <param name="gamer">gamer/role/list 的响应;null 表示请求失败(未拿到响应或响应体不可解析)。</param>
    public static KuroSessionProbe ProbeKuroSession(GamerRoil? gamer)
        => gamer is null ? KuroSessionProbe.Unreachable
        : gamer is { Code: 200, Data: not null } ? KuroSessionProbe.Valid
        : KuroSessionProbe.Rejected;

    /// <summary>
    /// 库街区会话校验的结果是否应判定为「登录已失效」。
    /// <para>
    /// 只有服务端<b>明确拒绝</b>才算失效(橙点);网络异常/超时(返回 null 或抛异常)不改状态,
    /// 避免把临时网络问题误报成异常登录。判定为有效时返回 false,调用方据此清除失效标记。
    /// </para>
    /// <para>
    /// ⚠️ 本方法只回答二值问题;调用方需要区分「有效」与「无法判定」时请改用
    /// <see cref="ProbeKuroSession"/>(false 不代表「确认有效」)。
    /// </para>
    /// </summary>
    /// <param name="gamer">gamer/role/list 的响应;null 表示请求失败(未拿到响应)。</param>
    public static bool IsKuroSessionExpired(GamerRoil? gamer)
        => ProbeKuroSession(gamer) == KuroSessionProbe.Rejected;

    /// <summary>
    /// 「同一账号判定」用的单个接口探针:名称 + 手机号 + 是否已知会话失效。
    /// </summary>
    /// <param name="Name">接口显示名(库街区 / 云鸣潮 / mcguide)。</param>
    /// <param name="Phone">该接口记录的手机号;空表示未记录。</param>
    /// <param name="KnownExpired">该接口是否已被判定会话失效。</param>
    public readonly record struct InterfaceProbe(string Name, string Phone, bool KnownExpired);

    /// <summary>
    /// 计算右上角「同一账号判定」的结论。
    ///
    /// <para><b>优先级</b>(用户反馈:登录失效后仍显示"3 个接口均为同一账号"):</para>
    /// <list type="number">
    /// <item><b>有接口会话失效 → <see cref="SameAccountVerdict.SessionExpired"/></b>:
    /// 这是最需要用户处理的状态,必须优先于"同一账号"暴露出来。
    /// 手机号一致只说明"像同一个号",不代表还能用 —— 若被 Same 掩盖,用户就看不到要重新登录。</item>
    /// <item>仅 0~1 个接口登录 → Unknown(无从比较)。</item>
    /// <item>有接口缺手机号 → Unknown(信息不足,无法完整判定)。</item>
    /// <item>手机号全同 → Same;否则 → Different。</item>
    /// </list>
    /// </summary>
    /// <param name="probes">当前已登录的接口。</param>
    public static SameAccountVerdict ResolveVerdict(IReadOnlyList<InterfaceProbe> probes)
    {
        if (probes.Any(p => p.KnownExpired))
        {
            return SameAccountVerdict.SessionExpired;
        }
        if (probes.Count <= 1)
        {
            return SameAccountVerdict.Unknown;
        }
        if (probes.Any(p => string.IsNullOrWhiteSpace(p.Phone)))
        {
            return SameAccountVerdict.Unknown;
        }
        return probes.Select(p => p.Phone).Distinct(StringComparer.Ordinal).Count() == 1
            ? SameAccountVerdict.Same
            : SameAccountVerdict.Different;
    }

    /// <summary>
    /// 会话失效的接口名列表(供徽标文案展示"哪个接口需要重新登录")。
    /// </summary>
    public static IReadOnlyList<string> ExpiredNames(IReadOnlyList<InterfaceProbe> probes)
        => probes.Where(p => p.KnownExpired).Select(p => p.Name).ToList();
}
