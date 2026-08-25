using McKuro.Core.Infrastructure;
using McKuro.Core.Models.Kuro;
using McKuro.Core.Models.Tower;
using McKuro.Core.Services.Game;
using McKuro.Core.Services.Kuro;
using McKuro.Core.Services.Roles;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

namespace McKuro.Core.Services.Tower;

/// <summary>
/// 深塔/海墟数据服务:用库街区 accessToken 拉取逆境深塔、终焉矩阵与再生海域数据。
/// (对齐 WutheringWavesTool TowerDataDetailTask / NewTowerDataDetailTask / SlashDataDetailTask)
/// </summary>
public sealed class TowerService
{
    private readonly KujiequApiClient _api;
    private readonly IKuroClient _kuro;
    private readonly KuroAccountService _accounts;
    private readonly AppDatabase? _database;
    private readonly ILogger<TowerService> _logger;

    public TowerService(
        KujiequApiClient api,
        IKuroClient kuro,
        KuroAccountService accounts,
        ILogger<TowerService>? logger = null,
        AppDatabase? database = null)
    {
        _api = api;
        _kuro = kuro;
        _accounts = accounts;
        _logger = logger ?? NullLogger<TowerService>.Instance;
        _database = database;
    }

    /// <summary>
    /// 拉取深塔与海墟数据;返回 null 表示失败(未登录/无 accessToken/接口异常)。
    /// RoleId 为本次解析的库街区角色条目 ID(矩阵历史按它落库)。
    /// </summary>
    public async Task<(TowerSeasonData? Tower, NewTowerData? NewTower, SlashData? Slash, string Error, string RoleId)> GetTowerDataAsync(CancellationToken ct = default)
    {
        var account = _accounts.Current;
        if (account is null)
        {
            return (null, null, null, "请先登录库街区账号", "");
        }

        // 复用角色数据服务的 accessToken 换取流程
        var deviceId = account.DeviceId ?? Guid.NewGuid().ToString("N");
        var gamer = await _kuro.GetGamerAsync(account, (int)KuroGameType.Waves, ct).ConfigureAwait(false);
        if (gamer is not { Code: 200, Data: not null } || gamer.Data.Count == 0)
        {
            return (null, null, null, "获取角色列表失败", "");
        }
        var role = gamer.Data[0];
        var roleId = role.RoleId ?? "";
        if (string.IsNullOrEmpty(roleId))
        {
            return (null, null, null, "角色 ID 为空", "");
        }

        _api.PublicIp = _kuro.Ip;
        var accessToken = await _api.GetAccessTokenAsync(
            account.Token, deviceId, roleId, account.UserId ?? "", "android", ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(accessToken))
        {
            return (null, null, null, "获取访问令牌失败(Token 可能已失效)", roleId);
        }

        TowerSeasonData? tower = null;
        NewTowerData? newTower = null;
        SlashData? slash = null;
        try
        {
            tower = await _api.GetTowerAsync(accessToken, deviceId, roleId, "android", ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "拉取逆境深塔数据失败");
        }
        try
        {
            newTower = await _api.GetNewTowerAsync(accessToken, deviceId, roleId, "android", ct).ConfigureAwait(false);
            if (newTower is not null)
            {
                SaveNewTowerHistory(roleId, newTower);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "拉取终焉矩阵数据失败");
        }
        try
        {
            slash = await _api.GetSlashAsync(accessToken, deviceId, roleId, "android", ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "拉取海墟数据失败");
        }

        if (tower is null && newTower is null && slash is null)
        {
            return (null, null, null, "接口返回空数据(可能受风控)", roleId);
        }
        return (tower, newTower, slash, "", roleId);
    }

    /// <summary>
    /// 终焉矩阵本期有记录时落库一期历史(key=roleId+赛季结束时间,对齐 WutheringWavesTool saveToDB)。
    /// 赛季结束绝对时间 = 当前时间 + 剩余毫秒,归整到当天 04:00(convertToHourlyTimestamp 同款)。
    /// </summary>
    internal void SaveNewTowerHistory(string roleId, NewTowerData newTower)
    {
        if (_database is null)
        {
            return;
        }
        try
        {
            var modes = newTower.ModeDetails?
                .Where(d => d.HasRecord && d.Score > 0)
                .ToList();
            if (modes is not { Count: > 0 })
            {
                return;
            }
            // 剩余毫秒缺失时退化为"当前时间归整",保证仍能落一条历史
            var remaining = newTower.EndTime ?? 0;
            var end = NormalizeToHour4(DateTime.Now.AddMilliseconds(remaining));
            var json = JsonSerializer.Serialize(modes, TowerJsonContext.Default.ListNewTowerModeDetail);
            _database.UpsertNewTowerHistory(roleId, end, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "终焉矩阵历史落库失败: {RoleId}", roleId);
        }
    }

    /// <summary>归整到当天 04:00(对齐 WutheringWavesTool convertToHourlyTimestamp)。</summary>
    internal static long NormalizeToHour4(DateTime local)
    {
        var atFour = new DateTime(local.Year, local.Month, local.Day, 4, 0, 0, local.Kind);
        return new DateTimeOffset(atFour).ToUnixTimeMilliseconds();
    }
}
