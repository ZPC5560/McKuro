using McKuro.Core.Models.Guide;
using McKuro.Core.Models.Roles;
using McKuro.Core.Services.CloudGame;
using McKuro.Core.Services.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace McKuro.Core.Services.Guide;

/// <summary>
/// mcguide 养成达成度服务:串联 SDK 登录 → guide 换 x-token → 选玩家 → 按角色拉达成度。
/// <para>登录态(GuideToken / CUid / CName / PlayerId / ServerId)持久化到 <see cref="AppSettings"/>。</para>
/// </summary>
public sealed partial class GuideAchievementService
{
    private readonly CloudGameService _cloud;
    private readonly GuideApiClient _api;
    private readonly ISettingsService _settings;
    private readonly GuideCacheService? _cache;
    private readonly ILogger<GuideAchievementService> _logger;

    public GuideAchievementService(
        CloudGameService cloud,
        GuideApiClient api,
        ISettingsService settings,
        ILogger<GuideAchievementService>? logger = null,
        GuideCacheService? cache = null)
    {
        _cloud = cloud;
        _api = api;
        _settings = settings;
        _cache = cache;
        _logger = logger ?? NullLogger<GuideAchievementService>.Instance;
    }

    /// <summary>是否已取得 guide x-token。</summary>
    public bool HasToken => !string.IsNullOrWhiteSpace(_settings.Current.GuideToken);

    /// <summary>发送 mcguide 登录验证码。</summary>
    public async Task<(bool Ok, string? Message)> SendSmsAsync(string phone, CancellationToken ct = default)
    {
        var (result, _) = await _cloud.GetGuidePhoneSMSAsync(phone, ct).ConfigureAwait(false);
        if (result is null)
        {
            return (false, CoreStrings.T("Core.Guide.SendInvalid", "发送验证码失败(响应无效)"));
        }
        return result.Codes == 0
            ? (true, CoreStrings.T("Account.CodeSent", "验证码已发送,请查收"))
            : (false, CoreStrings.F("Account.SendFailed", $"发送失败: {result.ErrorDescription ?? $"code={result.Codes}"}", result.ErrorDescription ?? $"code={result.Codes}"));
    }

    /// <summary>手机号 + 验证码登录:SDK 登录 → guide 换 x-token → 自动选玩家。</summary>
    public async Task<(bool Ok, string? Message)> LoginAsync(string phone, string code, CancellationToken ct = default)
    {
        try
        {
            var login = await _cloud.LoginGuideAsync(phone, code, ct).ConfigureAwait(false);
            if (login is not { Code: 0, Data: not null })
            {
                return (false, login?.Msg ?? CoreStrings.T("Core.Guide.SdkLoginFailed", "SDK 登录失败"));
            }

            var access = await _cloud.GetGuideAccessTokenAsync(login.Data, login.Data.Code ?? "", ct).ConfigureAwait(false);
            if (access is not { Code: 0, Data: not null } || string.IsNullOrEmpty(access.Data.AccessToken))
            {
                return (false, access?.Msg ?? CoreStrings.T("Core.Guide.AccessTokenFailed", "获取 access_token 失败"));
            }

            var cUid = login.Data.Cuid ?? "";
            var cName = login.Data.Username ?? "";
            var token = await _api.LoginSdkAsync(cUid, cName, access.Data.AccessToken!, ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(token))
            {
                return (false, CoreStrings.T("Core.Guide.LoginNoToken", "guide 登录失败(未返回 x-token)"));
            }

            var s = _settings.Current;
            s.GuideToken = token;
            s.GuideCUid = cUid;
            s.GuideCName = cName;
            s.GuidePhone = phone; // 记录手机号:账号页表单复用 + 跨接口同账号判定
            _settings.Save();

            var playerOk = await EnsurePlayerAsync(ct).ConfigureAwait(false);
            return playerOk
                ? (true, CoreStrings.T("Account.LoginSuccess", "登录成功"))
                : (true, CoreStrings.T("Core.Guide.LoginAutoSelectFailed", "登录成功,但自动选择玩家失败(可在角色页重新选择)"));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "mcguide 登录失败");
            return (false, CoreStrings.F("Account.LoginFailed", $"登录失败: {ex.Message}", ex.Message));
        }
    }

    /// <summary>确保已选定玩家;未选时自动取第一个玩家。</summary>
    public async Task<bool> EnsurePlayerAsync(CancellationToken ct = default)
    {
        var s = _settings.Current;
        if (s.GuidePlayerId > 0 && !string.IsNullOrWhiteSpace(s.GuideServerId))
        {
            return true;
        }
        if (string.IsNullOrWhiteSpace(s.GuideToken))
        {
            return false;
        }

        try
        {
            var players = await _api.GetPlayerListAsync(s.GuideToken, ct).ConfigureAwait(false);
            var first = players.FirstOrDefault();
            if (first is null)
            {
                return false;
            }
            var profile = await _api.ChoosePlayerAsync(s.GuideToken, first.PlayerId, first.ServerId ?? "", ct).ConfigureAwait(false);
            var chosen = profile?.Profile?.ChosenPlayer;
            if (chosen is null)
            {
                return false;
            }
            s.GuidePlayerId = chosen.PlayerId;
            s.GuideServerId = chosen.ServerId ?? "";
            _settings.Save();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "自动选择玩家失败");
            return false;
        }
    }

    /// <summary>
    /// 按库街区 cardRoleId 拉取官方养成达成度(取点赞最高的攻略)。
    /// <para>走与详情页相同的缓存入口(评审反馈:旧实现直连 API 绕过缓存,
    /// 角色选中时与按需拉取并发,同一 cardRoleId 重复发 list+info 请求)。</para>
    /// </summary>
    public async Task<GuideIntroductionInfo?> GetAchievementAsync(string roleName, int cardRoleId, CancellationToken ct = default)
    {
        var gbId = GuideRoleMap.TryGetRoleGbId(roleName) ?? GuideRoleMap.TryGetRoleGbId(cardRoleId);
        if (gbId is null)
        {
            _logger.LogInformation("未取得 mcguide roleGbId,跳过: {Role}", roleName);
            return null;
        }
        if (string.IsNullOrWhiteSpace(_settings.Current.GuideToken))
        {
            return null;
        }

        // 多攻略默认取点赞最高的一篇(GetIntroductionListAsync 已按点赞数降序)
        var list = await GetIntroductionListAsync(roleName, cardRoleId, ct).ConfigureAwait(false);
        var top = list.FirstOrDefault();
        return top is null
            ? null
            : await GetAchievementByIdAsync(roleName, cardRoleId, top.Id, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 攻略缓存的账号维度键:CUid + 选中玩家(评审反馈:达成度是 per-account 数据,
    /// 换攻略账号/换绑定玩家后不得命中上一账号的缓存快照)。
    /// </summary>
    private string GuideCacheAccountKey
    {
        get
        {
            var s = _settings.Current;
            return $"{s.GuideCUid}|{s.GuidePlayerId}";
        }
    }

    /// <summary>
    /// 拉取某角色的全部攻略列表(点赞降序;切换攻略选择器数据源)。
    /// <para>优先命中本地缓存(SQLite guide_cache);未命中或过期才走网络,成功后由本方法统一回写
    /// (评审反馈:此前回写散落在各调用方,行为分叉)。</para>
    /// </summary>
    public async Task<List<GuideIntroductionItem>> GetIntroductionListAsync(string roleName, int cardRoleId, CancellationToken ct = default)
    {
        // 1. 缓存优先:24h 内的列表结果(含"确认无攻略"的空列表,负缓存)直接返回,点角色不必等网络
        var cached = _cache?.TryGet(GuideCacheAccountKey, cardRoleId);
        if (_cache?.IsListKnownFresh(cached) == true)
        {
            return [.. cached!.List];
        }

        var gbId = GuideRoleMap.TryGetRoleGbId(roleName) ?? GuideRoleMap.TryGetRoleGbId(cardRoleId);
        if (gbId is null || string.IsNullOrWhiteSpace(_settings.Current.GuideToken))
        {
            return cached is { List.Count: > 0 } ? [.. cached.List] : [];
        }
        try
        {
            var list = await _api.GetIntroductionListAsync(_settings.Current.GuideToken, gbId, ct).ConfigureAwait(false);
            // 统一回写(仅列表列;不碰详情):下次进该角色秒开
            _cache?.SaveList(GuideCacheAccountKey, cardRoleId, list, cached?.SelectedId ?? 0);
            return list;
        }
        catch (GuideApiException ex) when (ex.Code == GuideApiException.SessionExpiredCode)
        {
            ClearExpiredSession();
            throw new GuideApiException(CoreStrings.T("Core.Guide.SessionExpired", "mcguide 登录已过期,请到「账号」页的攻略站区块重新登录"), ex.Code);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && cached is { List.Count: > 0 })
        {
            // 网络失败但有旧缓存:用旧的,不让界面空着。取消必须 rethrow(评审反馈):
            // 吞掉 OCE 会让"已切走/已取消"的请求继续渲染旧数据
            _logger.LogWarning("攻略列表拉取失败,回退本地缓存: cardRoleId={Id}", cardRoleId);
            return [.. cached.List];
        }
    }

    /// <summary>
    /// 按攻略 id 拉取详情(切换攻略时用;title/author 仅作展示,实际按 id 精确切换)。
    /// <para>若与缓存里记录的选中攻略相同且详情缓存新鲜,直接返回缓存详情(含共鸣链图标);
    /// 网络成功后由本方法统一回写详情列。</para>
    /// </summary>
    public async Task<GuideIntroductionInfo?> GetAchievementByIdAsync(string roleName, int cardRoleId, long introductionId, CancellationToken ct = default)
    {
        var cached = _cache?.TryGet(GuideCacheAccountKey, cardRoleId);
        if (_cache?.IsDetailFresh(cached) == true && cached!.SelectedId == introductionId)
        {
            return cached.Detail;
        }

        var gbId = GuideRoleMap.TryGetRoleGbId(roleName) ?? GuideRoleMap.TryGetRoleGbId(cardRoleId);
        if (gbId is null || string.IsNullOrWhiteSpace(_settings.Current.GuideToken))
        {
            return cached?.Detail;
        }
        try
        {
            var info = await _api.GetIntroductionInfoAsync(_settings.Current.GuideToken, gbId, introductionId, ct).ConfigureAwait(false);
            if (info is not null)
            {
                // 统一回写(仅详情列;null 不覆盖旧详情由 SaveDetail 内部保证)
                _cache?.SaveDetail(GuideCacheAccountKey, cardRoleId, info, introductionId);
            }
            return info;
        }
        catch (GuideApiException ex) when (ex.Code == GuideApiException.SessionExpiredCode)
        {
            ClearExpiredSession();
            throw new GuideApiException(CoreStrings.T("Core.Guide.SessionExpired", "mcguide 登录已过期,请到「账号」页的攻略站区块重新登录"), ex.Code);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && cached is { Detail: not null })
        {
            _logger.LogWarning("攻略详情拉取失败,回退本地缓存: cardRoleId={Id}", cardRoleId);
            return cached.Detail;
        }
    }

    /// <summary>
    /// 拉取角色基础资料(role/info):技能演示视频(5 个)+ 角色特点图标。
    /// <para>缓存策略同攻略详情(24h,独立时间列);网络失败时回退缓存,取消则 rethrow。</para>
    /// </summary>
    public async Task<GuideRoleInfoData?> GetRoleInfoDataAsync(string roleName, int cardRoleId, CancellationToken ct = default)
    {
        var cached = _cache?.TryGetRoleInfo(GuideCacheAccountKey, cardRoleId);
        if (_cache?.IsRoleInfoFresh(cached) == true)
        {
            return cached!.Data;
        }

        var gbId = GuideRoleMap.TryGetRoleGbId(roleName) ?? GuideRoleMap.TryGetRoleGbId(cardRoleId);
        if (gbId is null || string.IsNullOrWhiteSpace(_settings.Current.GuideToken))
        {
            return cached?.Data;
        }
        try
        {
            var data = await _api.GetRoleInfoAsync(_settings.Current.GuideToken, gbId, ct).ConfigureAwait(false);
            if (data is not null)
            {
                _cache?.SaveRoleInfo(GuideCacheAccountKey, cardRoleId, data);
            }
            return data;
        }
        catch (GuideApiException ex) when (ex.Code == GuideApiException.SessionExpiredCode)
        {
            ClearExpiredSession();
            throw new GuideApiException(CoreStrings.T("Core.Guide.SessionExpired", "mcguide 登录已过期,请到「账号」页的攻略站区块重新登录"), ex.Code);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 取消必须 rethrow(评审反馈):吞掉 OCE 会让已切走的请求继续回退渲染旧数据
            _logger.LogWarning("角色资料拉取失败,回退缓存: cardRoleId={Id}", cardRoleId);
            return cached?.Data;
        }
    }

    /// <summary>读取攻略缓存(供调用方判断是否有可用缓存)。</summary>
    public GuideCacheEntry? TryGetCachedGuide(int cardRoleId) => _cache?.TryGet(GuideCacheAccountKey, cardRoleId);

    /// <summary>判断攻略详情缓存是否仍新鲜(24h 内且有详情;无缓存服务时恒为 false)。</summary>
    public bool IsGuideCacheFresh(GuideCacheEntry? entry) => _cache?.IsDetailFresh(entry) == true;

    /// <summary>清理孤儿攻略缓存(换攻略账号/删角色后的残留;只保留当前账号 + 当前角色列表)。</summary>
    public void PruneGuideCache(IReadOnlyCollection<int> keepCardRoleIds) => _cache?.PruneExcept(GuideCacheAccountKey, keepCardRoleIds);

    // ==================== 官方推荐判定(角色详情页展示用) ====================

    /// <summary>武器是否为攻略推荐档(status=1 首选 / 2 备选;items 数组内标记)。</summary>
    public static bool IsRecommendedWeapon(GuideWeaponItem w) => w.Status is 1 or 2;

    /// <summary>武器推荐档位文本(1=推荐,2=备选;非推荐返回空)。</summary>
    public static string WeaponRecommendText(GuideWeaponItem w) => w.Status switch
    {
        1 => CoreStrings.T("Roles.Guide.WeaponPrimary", "推荐"),
        2 => CoreStrings.T("Roles.Guide.WeaponAlternate", "备选"),
        _ => "",
    };

    /// <summary>
    /// 按名称归一化匹配「当前佩戴武器」在攻略推荐列表中的条目(未命中返回 null)。
    /// <para>
    /// 名称归一化复用 <see cref="NormalizeName"/>(去空格/间隔符/冒号 + 剥「梦魇」前缀),
    /// 与声骸推荐判定同口径:此前武器这里只做 <c>Replace(" ", "")</c>,
    /// 攻略写「源能臂铠·测肆」而库街区返回「源能臂铠测肆」时会静默判成「有差距」。
    /// </para>
    /// <para>
    /// <paramref name="equippedWeaponName"/> 为空(库街区详情未到/极验风控兜底)时返回 null,
    /// 调用方据此区分「确认不匹配」与「暂时无从判定」——不得把后者渲染成「有差距」。
    /// </para>
    /// </summary>
    public static GuideWeaponItem? MatchEquippedWeapon(string? equippedWeaponName, IReadOnlyList<GuideWeaponItem>? guideWeapons)
    {
        if (string.IsNullOrWhiteSpace(equippedWeaponName) || guideWeapons is not { Count: > 0 })
        {
            return null;
        }
        var norm = NormalizeName(equippedWeaponName);
        foreach (var w in guideWeapons)
        {
            if (!string.IsNullOrWhiteSpace(w.Name) && NormalizeName(w.Name) == norm)
            {
                return w;
            }
        }
        return null;
    }

    /// <summary>
    /// 首位声骸是否为攻略推荐声骸:与推荐配装(echo.main/spare/current)的首件名称一致(归一化比较)。
    /// mcguide 推荐配装的首件即官方推荐主声骸(如椿=无常凶鹭)。
    /// </summary>
    public static bool IsRecommendedPhantom(string? phantomName, GuideEcho? echo) => echo is not null && IsRecommendedPhantom(phantomName, [echo.Main, echo.Spare, echo.Current]);

    /// <summary>推荐声骸比对核心:与任一推荐配装首件名称一致(归一化比较)。</summary>
    public static bool IsRecommendedPhantom(string? phantomName, IReadOnlyList<GuideEchoBuild?> builds)
    {
        if (string.IsNullOrWhiteSpace(phantomName))
        {
            return false;
        }
        var norm = NormalizeName(phantomName);
        foreach (var build in builds)
        {
            var top = build?.EchoProps?.Name;
            if (!string.IsNullOrWhiteSpace(top) && NormalizeName(top) == norm)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 共鸣链推荐标记:从攻略推荐描述里解析被推荐的链号(如「共鸣链2…共鸣链4…共鸣链6」→ [2,4,6])。
    /// <para>支持写法(定死范围,避免过度匹配误报):「共鸣链N」N=1-6,允许全角数字与中间空白;
    /// 其余写法("C2"/"2链" 等)不识别 —— 实测攻略站正文均为「共鸣链N」格式。</para>
    /// 解析不到时返回空集合(不显示推荐标识)。
    /// </summary>
    public static IReadOnlyList<int> ParseRecommendedChains(string? recommendText)
    {
        var result = new List<int>();
        if (string.IsNullOrWhiteSpace(recommendText))
        {
            return result;
        }
        var text = PlainRecommendText(recommendText);
        foreach (System.Text.RegularExpressions.Match m in RecommendedChainPattern().Matches(text))
        {
            var digit = m.Groups[1].Value[0];
            // 全角「１-６」归一到半角再取值
            var n = digit is >= '１' and <= '９' ? digit - '１' + 1 : digit - '0';
            if (n is >= 1 and <= 6 && !result.Contains(n))
            {
                result.Add(n);
            }
        }
        return result;
    }

    /// <summary>共鸣链推荐号(半角/全角 1-6;源生成正则,见 <see cref="HtmlTagPattern"/> 的 AOT 说明)。</summary>
    [System.Text.RegularExpressions.GeneratedRegex("共鸣链\\s*([1-6１-６])")]
    private static partial System.Text.RegularExpressions.Regex RecommendedChainPattern();

    /// <summary>
    /// 技能加点是否达标:当前等级 ≥ 推荐等级(recommendLevel 有值时)。
    /// <para>
    /// <paramref name="liveCurrentLevel"/> = <b>库街区 getRoleDetail 的实时技能等级</b>,达标判定优先用它。
    /// 攻略接口的 currentLevel 是服务端快照,且会被本地缓存 24h —— 玩家在游戏里点完技能后它可能长期滞后,
    /// 于是"已经点满"的技能仍被建议提升(用户反馈:技能加点已经达标还显示需要提升)。
    /// 库街区详情是权威实时值(同页技能弧线徽章本来就显示它),两者不一致时以实时为准。
    /// </para>
    /// <para>传 null(详情未加载/该技能未匹配到)时回退攻略快照,与旧行为一致。</para>
    /// </summary>
    public static bool? IsSkillLevelMet(GuideSkillTarget t, int? liveCurrentLevel = null)
    {
        var rec = t.RecommendLevelValue;
        if (rec <= 0)
        {
            return null; // 攻略未给推荐等级(如"可不点")
        }
        return (liveCurrentLevel ?? t.CurrentLevelValue) >= rec;
    }

    /// <summary>技能推荐等级文本(推荐 Lv.X;无推荐时返回"无需升级")。</summary>
    public static string SkillRecommendText(GuideSkillTarget t)
        => t.RecommendLevelValue > 0
            ? CoreStrings.F("Roles.Guide.SkillRecommend", $"推荐 Lv.{t.RecommendLevelValue}", t.RecommendLevelValue)
            : CoreStrings.T("Roles.Guide.SkillNoNeed", "无需升级");

    /// <summary>
    /// 声骸名称归一化:去空白/间隔符,并剥掉玩家侧的「梦魇」前缀
    /// (攻略推荐写"云闪之鳞",玩家实际持有记录常为"梦魇·云闪之鳞",不剥前缀推荐徽标会静默不亮 —— 评审反馈)。
    /// </summary>
    private static string NormalizeName(string name)
    {
        var s = name.Replace(" ", "").Replace("·", "").Replace(":", "").Replace("：", "").Trim();
        return s.StartsWith("梦魇", StringComparison.Ordinal) ? s[2..] : s;
    }

    /// <summary>HTML 标签剥除(仓库约定 [GeneratedRegex]:AOT 下运行时 Compiled 被忽略,源生成才免反射 —— 见 PlayTimeService 先例)。</summary>
    [System.Text.RegularExpressions.GeneratedRegex("<[^>]+>")]
    private static partial System.Text.RegularExpressions.Regex HtmlTagPattern();

    /// <summary>取攻略推荐描述的纯文本(去 HTML 标签;无内容返回空)。</summary>
    public static string PlainRecommendText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return "";
        }
        // <p>/<br>/&gt; 等实体简单还原,标签全部剥除(推荐描述仅用于展示,不做富文本渲染)
        var text = HtmlTagPattern().Replace(html, "");
        return System.Net.WebUtility.HtmlDecode(text).Trim();
    }

    /// <summary>
    /// 校验 mcguide 会话是否仍有效(账号页加载时调用)。
    /// 返回 valid: null=未登录/校验失败(不判定), true=有效, false=已过期(会话已被清除)。
    /// </summary>
    public async Task<(bool? Valid, string Message)> ValidateSessionAsync(CancellationToken ct = default)
    {
        var s = _settings.Current;
        if (string.IsNullOrWhiteSpace(s.GuideToken))
        {
            return (null, CoreStrings.T("Account.GuideNotLoggedIn", "未登录(角色页将隐藏官方评级)"));
        }
        try
        {
            // /user/player/list 是最轻的鉴权 GET,足以判定 x-token 有效性
            await _api.GetPlayerListAsync(s.GuideToken, ct).ConfigureAwait(false);
            return (true, string.IsNullOrWhiteSpace(s.GuideCName) ? CoreStrings.T("Account.LoggedIn", "已登录") : CoreStrings.F("Account.GuideLoggedIn", $"已登录: {s.GuideCName}", s.GuideCName));
        }
        catch (GuideApiException ex) when (ex.Code == GuideApiException.SessionExpiredCode)
        {
            ClearExpiredSession();
            return (false, CoreStrings.T("Core.Guide.LoginExpired", "登录已过期,请重新登录"));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "mcguide 会话校验失败(不判定过期)");
            return (null, CoreStrings.F("Core.Guide.SessionCheckFailed", $"会话校验失败: {ex.Message}", ex.Message));
        }
    }

    /// <summary>清除失效的 mcguide 会话(保留手机号/账号名便于表单复用与同账号判定)。</summary>
    private void ClearExpiredSession()
    {
        _logger.LogWarning("mcguide 会话已过期(code=1001),清除本地 GuideToken");
        var s = _settings.Current;
        s.GuideToken = "";
        s.GuidePlayerId = 0;
        s.GuideServerId = "";
        _settings.Save();
    }

    /// <summary>
    /// 用 mcguide 攻略站数据构造角色详情(库街区 getRoleDetail 被风控时的兜底数据源)。
    /// <para>返回的角色详情已按库街区 <see cref="RoleDetail"/> 结构映射
    /// (武器/技能/属性/共鸣链/声骸),可直接用于角色详情页展示。</para>
    /// </summary>
    public async Task<RoleDetail?> GetRoleDetailFromGuideAsync(string roleName, int cardRoleId, CancellationToken ct = default)
    {
        var info = await GetAchievementAsync(roleName, cardRoleId, ct).ConfigureAwait(false);
        return info is null ? null : MapRoleDetail(info, cardRoleId);
    }

    /// <summary>把 mcguide <see cref="GuideIntroductionInfo"/> 映射为库街区 <see cref="RoleDetail"/>(纯映射,便于单测)。</summary>
    public static RoleDetail MapRoleDetail(GuideIntroductionInfo info, int cardRoleId)
    {
        var role = info.Role;
        var roleInfo = new RoleInfo
        {
            RoleId = cardRoleId,
            RoleName = role?.Name ?? "",
            StarLevel = role?.Star ?? 0,
        };

        // 1. 武器:优先当前武器,否则取武器列表第一件;mcguide 无等级/突破/精炼,填 0
        WeaponData? weaponData = null;
        var weapon = info.Weapon?.Current ?? info.Weapon?.Items?.FirstOrDefault();
        if (weapon is not null)
        {
            weaponData = new WeaponData
            {
                Level = 0,
                Breach = 0,
                Rank = 0,
                Weapon = new WeaponInfo
                {
                    WeaponName = weapon.Name ?? "",
                    WeaponStarLevel = weapon.Star,
                    WeaponIcon = weapon.PictureUrl ?? "",
                },
            };
        }

        // 2. 技能:mcguide 无实际技能等级,填 0;图标用 pictureUrl、名称用 texts.name
        var skills = (info.RoleSkill?.FixedSkills ?? [])
            .Select(s => new SkillInfo
            {
                SkillLevel = 0,
                Skill = new SkillBase
                {
                    SkillName = s.Name ?? "",
                    IconUrl = s.PictureUrl ?? "",
                    Type = s.TypeName ?? "",
                },
            })
            .ToList();

        // 3. 属性:当前/推荐 拼接,如 "67.5%/60.0%"
        var attributes = (info.RoleAttribute?.Items ?? [])
            .Select(a => new RoleAttribute
            {
                AttributeName = a.Name ?? "",
                AttributeValue = BuildAmountText(a),
                AttributeType = a.IsFinished == true ? CoreStrings.T("Roles.Met", "已达标") : CoreStrings.T("Roles.NotMet", "未达标"),
                IconUrl = a.PictureUrl ?? "",
            })
            .ToList();

        // 4. 共鸣链:resonanceSequence → ChainNum,isAcquired → IsUnlock,pictureUrl → IconUrl
        var chains = (info.RoleResonance?.Items ?? [])
            .Select(c => new ChainInfo
            {
                ChainNum = c.ResonanceSequence,
                ChainName = c.Name ?? "",
                IsUnlock = c.IsAcquired == true,
                Description = c.Description ?? "",
                IconUrl = c.PictureUrl ?? "",
            })
            .ToList();

        // 5. 声骸:推荐配装简化为至少 1 件(名称/图标/星级/套装)
        var phantomData = BuildPhantomData(info.Echo);

        return new RoleDetail
        {
            Role = roleInfo,
            WeaponData = weaponData,
            Skills = skills,
            Attributes = attributes,
            Chains = chains,
            PhantomData = phantomData,
        };
    }

    /// <summary>把 mcguide 声骸推荐配装简化为库街区声骸列表(至少 1 件,含套装名)。</summary>
    private static PhantomData? BuildPhantomData(GuideEcho? echo)
    {
        var echoes = new List<EchoInfo>();
        var build = echo?.Current;
        var props = build?.EchoProps;
        if (props is not null)
        {
            var set = build?.EchoSetEffects?.FirstOrDefault();
            echoes.Add(new EchoInfo
            {
                Level = 0,
                Cost = props.Cost,
                Quality = props.Star,
                PhantomProp = new PhantomPropInfo
                {
                    PhantomName = props.Name ?? "",
                    IconUrl = props.PictureUrl ?? "",
                    Quality = props.Star,
                    Cost = props.Cost,
                },
                FetterDetail = set is null
                    ? null
                    : new EchoFetterDetail { Name = set.Name ?? "" },
            });
        }
        // 各件声骸(等级/主词条视角;无图标/星级时保持默认)
        foreach (var attr in build?.EchoAttributes ?? [])
        {
            var name = attr.Attribute?.Name;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }
            echoes.Add(new EchoInfo
            {
                Level = attr.CurrentLevel ?? 0,
                Cost = attr.Cost,
                PhantomProp = new PhantomPropInfo { PhantomName = name },
            });
        }
        return echoes.Count > 0 ? new PhantomData { Phantoms = echoes } : null;
    }

    /// <summary>属性值文本:当前/推荐 拼接(如 "67.5%/60.0%"),缺失时只保留有值的一侧。</summary>
    private static string BuildAmountText(GuideAttributeItem a)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(a.CurrentAmount))
        {
            parts.Add(a.CurrentAmount!);
        }
        if (!string.IsNullOrWhiteSpace(a.RecommendAmount))
        {
            parts.Add(a.RecommendAmount!);
        }
        return string.Join("/", parts);
    }
}
