using McKuro.Core.Models.Kuro;
using McKuro.Core.Models.User;
using McKuro.Core.Services.Game;
using McKuro.Core.Services.Kuro;
using McKuro.Core.Services.Roles;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace McKuro.Core.Services.User;

/// <summary>
/// 角色每日数据服务:拉取体力/活跃度/战歌重奏(周本)/电台(千道门扉)/周度游历。
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
        => await GetDailyDataAsync(_accounts.Current, ct).ConfigureAwait(false);

    /// <summary>
    /// 拉取指定库街区账号的角色每日数据(首页「绑定账号」可指向非当前登录账号;
    /// account 为 null 直接返回 null 表示失败/未登录)。
    /// </summary>
    public async Task<RoleDailyData?> GetDailyDataAsync(KuroAccount? account, CancellationToken ct = default)
    {
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

            // 战歌重奏(周本):getData 的 weeklyData 与数据中心的 weeklyInstCount 都是本周
            // <b>剩余</b>次数(2026-10 实测:打完=0、没打=3),不是已完成 —— 原样消费会把
            // 0 次显示成 "0/3" 空进度、提醒误报"还剩 3 次"。统一换算为已完成次数:
            // Cur=上限-剩余、Total=上限(数据中心 weeklyInstCountLimit,缺失回退 3),
            // 剩余次数由 Total-Cur 得出,首页与提醒消费端无需各自换算。
            if (daily.WeeklyData is not null)
            {
                var weeklyLimit = baseData?.WeeklyInstCountLimit > 0
                    ? baseData.WeeklyInstCountLimit
                    : daily.WeeklyData.Total > 0 ? daily.WeeklyData.Total : UserProfile.WeeklyFallbackLimit;
                var remaining = daily.WeeklyData.Cur;
                daily.WeeklyData.Total = weeklyLimit;
                daily.WeeklyData.Cur = UserProfile.WeeklyRemainingToDone(remaining, weeklyLimit);
                daily.WeeklyData.Value = $"{daily.WeeklyData.Cur}/{weeklyLimit}";
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
