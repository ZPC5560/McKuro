using McKuro.Core.Models.Kuro;
using McKuro.Core.Models.Tower;
using McKuro.Core.Services.Game;
using McKuro.Core.Services.Kuro;
using McKuro.Core.Services.Roles;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace McKuro.Core.Services.Tower;

/// <summary>
/// 深塔/海墟数据服务:用库街区 accessToken 拉取终焉矩阵与再生海域数据。
/// (对齐 WutheringWavesTool NewTowerDataDetailTask / SlashDataDetailTask)
/// </summary>
public sealed class TowerService
{
    private readonly KujiequApiClient _api;
    private readonly IKuroClient _kuro;
    private readonly KuroAccountService _accounts;
    private readonly ILogger<TowerService> _logger;

    public TowerService(
        KujiequApiClient api,
        IKuroClient kuro,
        KuroAccountService accounts,
        ILogger<TowerService>? logger = null)
    {
        _api = api;
        _kuro = kuro;
        _accounts = accounts;
        _logger = logger ?? NullLogger<TowerService>.Instance;
    }

    /// <summary>拉取深塔与海墟数据;返回 null 表示失败(未登录/无 accessToken/接口异常)。</summary>
    public async Task<(NewTowerData? Tower, SlashData? Slash, string Error)> GetTowerDataAsync(CancellationToken ct = default)
    {
        var account = _accounts.Current;
        if (account is null)
        {
            return (null, null, "请先登录库街区账号");
        }

        // 复用角色数据服务的 accessToken 换取流程
        var deviceId = account.DeviceId ?? Guid.NewGuid().ToString("N");
        var gamer = await _kuro.GetGamerAsync(account, (int)KuroGameType.Waves, ct).ConfigureAwait(false);
        if (gamer is not { Code: 200, Data: not null } || gamer.Data.Count == 0)
        {
            return (null, null, "获取角色列表失败");
        }
        var role = gamer.Data[0];
        var roleId = role.RoleId ?? "";
        if (string.IsNullOrEmpty(roleId))
        {
            return (null, null, "角色 ID 为空");
        }

        _api.PublicIp = _kuro.Ip;
        var accessToken = await _api.GetAccessTokenAsync(
            account.Token, deviceId, roleId, account.UserId ?? "", "android", ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(accessToken))
        {
            return (null, null, "获取访问令牌失败(Token 可能已失效)");
        }

        NewTowerData? tower = null;
        SlashData? slash = null;
        try
        {
            tower = await _api.GetNewTowerAsync(accessToken, deviceId, roleId, "android", ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "拉取深塔数据失败");
        }
        try
        {
            slash = await _api.GetSlashAsync(accessToken, deviceId, roleId, "android", ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "拉取海墟数据失败");
        }

        if (tower is null && slash is null)
        {
            return (null, null, "接口返回空数据(可能受风控)");
        }
        return (tower, slash, "");
    }
}
