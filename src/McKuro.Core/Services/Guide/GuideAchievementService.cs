using McKuro.Core.Models.Guide;
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

        var list = await _api.GetIntroductionListAsync(_settings.Current.GuideToken, gbId, ct).ConfigureAwait(false);
        var top = list.FirstOrDefault();
        return top is null ? null : await _api.GetIntroductionInfoAsync(_settings.Current.GuideToken, gbId, top.Id, ct).ConfigureAwait(false);
    }
}
