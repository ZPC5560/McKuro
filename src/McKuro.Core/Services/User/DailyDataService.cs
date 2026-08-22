using McKuro.Core.Models.Kuro;
using McKuro.Core.Models.User;
using McKuro.Core.Services.Game;
using McKuro.Core.Services.Kuro;
using McKuro.Core.Services.Roles;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace McKuro.Core.Services.User;

/// <summary>
/// 角色每日数据服务:拉取体力/活跃度/周本/电台(千道门扉)/周度游历。
/// (对齐 WutheringWavesTool UserDailyDataTask + RoleDailyData)
/// </summary>
public sealed class DailyDataService
{
    private readonly KujiequApiClient _api;
    private readonly IKuroClient _kuro;
    private readonly KuroAccountService _accounts;
    private readonly ILogger<DailyDataService> _logger;

    public DailyDataService(
        KujiequApiClient api,
        IKuroClient kuro,
        KuroAccountService accounts,
        ILogger<DailyDataService>? logger = null)
    {
        _api = api;
        _kuro = kuro;
        _accounts = accounts;
        _logger = logger ?? NullLogger<DailyDataService>.Instance;
    }

    /// <summary>拉取当前账号角色每日数据;返回 null 表示失败。</summary>
    public async Task<RoleDailyData?> GetDailyDataAsync(CancellationToken ct = default)
    {
        var account = _accounts.Current;
        if (account is null)
        {
            return null;
        }

        var deviceId = account.DeviceId ?? Guid.NewGuid().ToString("N");
        var gamer = await _kuro.GetGamerAsync(account, (int)KuroGameType.Waves, ct).ConfigureAwait(false);
        if (gamer is not { Code: 200, Data: not null } || gamer.Data.Count == 0)
        {
            return null;
        }
        var role = gamer.Data[0];
        var roleId = role.RoleId ?? "";
        if (string.IsNullOrEmpty(roleId))
        {
            return null;
        }

        _api.PublicIp = _kuro.Ip;
        var accessToken = await _api.GetAccessTokenAsync(
            account.Token, deviceId, roleId, account.UserId ?? "", "android", ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(accessToken))
        {
            return null;
        }

        try
        {
            var daily = await _api.GetRoleDailyDataAsync(
                accessToken, deviceId, roleId, account.UserId ?? "", "android", ct).ConfigureAwait(false);
            if (daily is null)
            {
                return null;
            }

            // 合并账号资料(昵称/等级/头像,来自库街区 gamer 角色条目)
            daily.RoleName = string.IsNullOrWhiteSpace(daily.RoleName) ? role.RoleName : daily.RoleName;
            daily.Level = int.TryParse(role.GameLevel, out var gamerLevel) ? gamerLevel : 0;
            daily.HeadUrl = string.IsNullOrWhiteSpace(role.HeadPhotoUrl) ? role.GameHeadUrl : role.HeadPhotoUrl;

            // 合并数据中心基础资料(游玩天数/注册时间/周本图标;失败不影响每日数据展示)
            var baseData = await _api.GetGamerBaseDataAsync(
                accessToken, deviceId, roleId, "android", ct).ConfigureAwait(false);
            if (baseData is not null)
            {
                daily.ActiveDays = baseData.ActiveDays;
                daily.CreatTime = baseData.CreatTime;
                daily.WeeklyIconUrl = baseData.WeeklyInstIconUrl;
                daily.LivenessLimit = baseData.LivenessMaxCount;
                daily.WeeklyLimit = baseData.WeeklyInstCountLimit;
                if (daily.Level == 0 && baseData.Level > 0)
                {
                    daily.Level = baseData.Level;
                }
            }
            return daily;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "拉取角色每日数据失败");
            return null;
        }
    }
}
