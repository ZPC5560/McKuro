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
public sealed class GuideAchievementService
{
    private readonly CloudGameService _cloud;
    private readonly GuideApiClient _api;
    private readonly ISettingsService _settings;
    private readonly ILogger<GuideAchievementService> _logger;

    public GuideAchievementService(
        CloudGameService cloud,
        GuideApiClient api,
        ISettingsService settings,
        ILogger<GuideAchievementService>? logger = null)
    {
        _cloud = cloud;
        _api = api;
        _settings = settings;
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
            return (false, "发送验证码失败(响应无效)");
        }
        return result.Codes == 0
            ? (true, "验证码已发送,请查收")
            : (false, $"发送失败: {result.ErrorDescription ?? $"code={result.Codes}"}");
    }

    /// <summary>手机号 + 验证码登录:SDK 登录 → guide 换 x-token → 自动选玩家。</summary>
    public async Task<(bool Ok, string? Message)> LoginAsync(string phone, string code, CancellationToken ct = default)
    {
        try
        {
            var login = await _cloud.LoginGuideAsync(phone, code, ct).ConfigureAwait(false);
            if (login is not { Code: 0, Data: not null })
            {
                return (false, login?.Msg ?? "SDK 登录失败");
            }

            var access = await _cloud.GetGuideAccessTokenAsync(login.Data, login.Data.Code ?? "", ct).ConfigureAwait(false);
            if (access is not { Code: 0, Data: not null } || string.IsNullOrEmpty(access.Data.AccessToken))
            {
                return (false, access?.Msg ?? "获取 access_token 失败");
            }

            var cUid = login.Data.Cuid ?? "";
            var cName = login.Data.Username ?? "";
            var token = await _api.LoginSdkAsync(cUid, cName, access.Data.AccessToken!, ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(token))
            {
                return (false, "guide 登录失败(未返回 x-token)");
            }

            var s = _settings.Current;
            s.GuideToken = token;
            s.GuideCUid = cUid;
            s.GuideCName = cName;
            s.GuidePhone = phone; // 记录手机号:账号页表单复用 + 跨接口同账号判定
            _settings.Save();

            var playerOk = await EnsurePlayerAsync(ct).ConfigureAwait(false);
            return playerOk
                ? (true, "登录成功")
                : (true, "登录成功,但自动选择玩家失败(可在角色页重新选择)");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "mcguide 登录失败");
            return (false, $"登录失败: {ex.Message}");
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

    /// <summary>按库街区 cardRoleId 拉取官方养成达成度(取点赞最高的攻略)。</summary>
    public async Task<GuideIntroductionInfo?> GetAchievementAsync(string roleName, int cardRoleId, CancellationToken ct = default)
    {
        // 优先:名称覆盖表(个别角色不一致时登记);默认:cardRoleId 直通 guide roleGbId
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

        try
        {
            var list = await _api.GetIntroductionListAsync(_settings.Current.GuideToken, gbId, ct).ConfigureAwait(false);
            var top = list.FirstOrDefault();
            return top is null ? null : await _api.GetIntroductionInfoAsync(_settings.Current.GuideToken, gbId, top.Id, ct).ConfigureAwait(false);
        }
        catch (GuideApiException ex) when (ex.Code == GuideApiException.SessionExpiredCode)
        {
            // x-token 已失效:清除会话让账号页回到登录表单(保留手机号便于复用),提示重新登录
            ClearExpiredSession();
            throw new GuideApiException("mcguide 登录已过期,请到「账号」页的攻略站区块重新登录", ex.Code);
        }
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
            return (null, "未登录(角色页将隐藏官方评级)");
        }
        try
        {
            // /user/player/list 是最轻的鉴权 GET,足以判定 x-token 有效性
            await _api.GetPlayerListAsync(s.GuideToken, ct).ConfigureAwait(false);
            return (true, string.IsNullOrWhiteSpace(s.GuideCName) ? "已登录" : $"已登录: {s.GuideCName}");
        }
        catch (GuideApiException ex) when (ex.Code == GuideApiException.SessionExpiredCode)
        {
            ClearExpiredSession();
            return (false, "登录已过期,请重新登录");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "mcguide 会话校验失败(不判定过期)");
            return (null, $"会话校验失败: {ex.Message}");
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
                AttributeType = a.IsFinished == true ? "已达标" : "未达标",
                IconUrl = a.PictureUrl ?? "",
            })
            .ToList();

        // 4. 共鸣链:resonanceSequence → ChainNum,isAcquired → IsUnlock
        var chains = (info.RoleResonance?.Items ?? [])
            .Select(c => new ChainInfo
            {
                ChainNum = c.ResonanceSequence,
                ChainName = c.Name ?? "",
                IsUnlock = c.IsAcquired == true,
                Description = c.Description ?? "",
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
