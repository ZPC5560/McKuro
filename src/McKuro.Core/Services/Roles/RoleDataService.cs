using System.Text.Json;
using System.Text.Json.Serialization;
using McKuro.Core.Infrastructure;
using McKuro.Core.Models.Kuro;
using McKuro.Core.Models.Roles;
using McKuro.Core.Services.Kuro;
using Microsoft.Extensions.Logging;

namespace McKuro.Core.Services.Roles;

/// <summary>角色数据来源。</summary>
public enum RoleDataSource
{
    /// <summary>库街区 API(在线)。</summary>
    Kujiequ,
    /// <summary>本地游戏缓存/导入文件。</summary>
    Local,
    None,
}

/// <summary>角色数据加载结果。</summary>
public sealed class RoleDataLoadResult
{
    public required RoleDataSource Source { get; init; }
    public string? Message { get; init; }
    public IReadOnlyList<RoleDetail> Roles { get; init; } = [];
    public bool IsSuccess => Source != RoleDataSource.None;
}

/// <summary>
/// 角色数据服务:整合库街区 API(在线)与本地数据两种来源,并做本地缓存。
/// </summary>
public sealed class RoleDataService : IRoleDataService
{
    private readonly KujiequApiClient _api;
    private readonly LocalRoleDataReader _localReader;
    private readonly AppDatabase _db;
    private readonly KuroClient _kuro;
    private readonly KuroAccountService _accounts;
    private readonly ILogger<RoleDataService> _logger;

    public RoleDataService(
        KujiequApiClient api,
        LocalRoleDataReader localReader,
        AppDatabase db,
        KuroClient kuro,
        KuroAccountService accounts,
        ILogger<RoleDataService>? logger = null)
    {
        _api = api;
        _localReader = localReader;
        _db = db;
        _kuro = kuro;
        _accounts = accounts;
        _logger = logger ?? NullLogger<RoleDataService>.Instance;
    }

    /// <inheritdoc/>
    public async Task<RoleDataLoadResult> LoadFromKujiequAsync(
        string token,
        string roleId,
        bool refreshFirst = true,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return new RoleDataLoadResult { Source = RoleDataSource.None, Message = "未配置库街区 Token" };
        }
        if (string.IsNullOrWhiteSpace(roleId))
        {
            return new RoleDataLoadResult { Source = RoleDataSource.None, Message = "未配置角色 ID" };
        }

        try
        {
            // Devcode 头需要公网 IP(对齐 WutheringWavesTool getDevCode:IP + ", " + UA)
            _api.PublicIp = _kuro.Ip;
            // 1. 通过角色列表接口确认角色条目存在,并取库街区 userId(requestToken 需要)
            //    (对齐 WutheringWavesTool: serverId/gameId 用固定官方值,只需条目 roleId + userId)
            var deviceId = _accounts.Current?.DeviceId ?? Guid.NewGuid().ToString("N");
            var gamer = await _kuro.GetGamerAsync(
                new KuroAccount { Token = token, DeviceId = deviceId },
                (int)KuroGameType.Waves,
                ct).ConfigureAwait(false);
            // token 失效(如账号在其他设备登录)时 Code != 200 → 明确提示重新登录
            if (gamer is not null && gamer.Code != 200)
            {
                _logger.LogWarning("库街区角色列表接口返回非 200(可能 token 失效): code={Code} msg={Msg}",
                    gamer.Code, gamer.Msg);
                return new RoleDataLoadResult
                {
                    Source = RoleDataSource.None,
                    Message = $"登录已失效(账号可能已在其他设备登录),请重新登录 ({gamer.Msg ?? $"code={gamer.Code}"})",
                };
            }
            var item = gamer?.Data?.FirstOrDefault(r => r.RoleId == roleId);
            if (item is null)
            {
                _logger.LogWarning("未找到角色条目(角色 ID 与账号不匹配): roleId={RoleId}", roleId);
                return new RoleDataLoadResult
                {
                    Source = RoleDataSource.None,
                    Message = "未找到该角色条目(请确认角色 ID 与当前账号一致)",
                };
            }
            var userId = item.UserId ?? _accounts.Current?.UserId ?? "";

            // 2. requestToken 换 B-At 令牌(对齐 WutheringWavesTool BaseTask.requestToken)
            var accessToken = await _api.GetAccessTokenAsync(
                token, deviceId, roleId, userId, "android", ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(accessToken))
            {
                _logger.LogWarning("获取角色数据访问令牌失败: roleId={RoleId}", roleId);
                return new RoleDataLoadResult
                {
                    Source = RoleDataSource.None,
                    Message = "获取角色数据访问令牌失败(Token 可能已失效,请重新登录)",
                };
            }

            // 3. 刷新服务器缓存(可选)
            if (refreshFirst)
            {
                await _api.RefreshDataAsync(accessToken, deviceId, roleId, "android", ct).ConfigureAwait(false);
            }

            // 4. 角色列表(roleData) → 每个角色完整详情(getRoleDetail,串行节流;id 传 roleList 项 roleId)
            //    串行 + 小延时:并发批量 getRoleDetail 会触发库街区极验风控(返回 {"geeTest":true})
            var list = await _api.GetRoleDataAsync(
                accessToken, deviceId, roleId, "android", ct).ConfigureAwait(false);

            var roles = new List<RoleDetail>(list.Count);
            var geeTestTriggered = false;
            foreach (var r in list)
            {
                ct.ThrowIfCancellationRequested();
                var detail = await _api.GetRoleDetailAsync(
                    accessToken, deviceId, roleId, r.Role?.RoleId ?? 0, "android", ct).ConfigureAwait(false);
                if (detail is not null)
                {
                    roles.Add(detail);
                }
                else
                {
                    // 若已触发极验(接口返回 {"geeTest":true}),停止后续请求避免进一步风控
                    geeTestTriggered = true;
                    break;
                }
                // 请求间隔,降低触发风控概率
                await Task.Delay(TimeSpan.FromMilliseconds(250), ct).ConfigureAwait(false);
            }
            if (roles.Count == 0)
            {
                // 详情全部失败时至少保留基础列表
                roles.AddRange(list);
            }

            // 详情齐全(未被风控)才写缓存:不完整同步不得覆盖上次完整数据
            if (IsCompleteSync(geeTestTriggered, roles))
            {
                SaveCache(userId, roleId, roles);
                return new RoleDataLoadResult { Source = RoleDataSource.Kujiequ, Roles = roles };
            }

            // 详情受限(极验风控等):优先展示上次同步成功的完整库街区数据(缓存),避免页面空有基础信息
            var cached = LoadCompleteCacheOrNull(userId, roleId);
            if (cached is not null)
            {
                return new RoleDataLoadResult
                {
                    Source = RoleDataSource.Kujiequ,
                    Roles = cached,
                    Message = geeTestTriggered
                        ? "库街区详情接口受极验风控,已展示上次同步的完整数据,可稍后重试"
                        : "详情数据不全,已展示上次同步的完整数据",
                };
            }

            return new RoleDataLoadResult
            {
                Source = RoleDataSource.Kujiequ,
                Roles = roles,
                Message = roles.Count == 0
                    ? "接口返回空数据"
                    : geeTestTriggered
                        ? "部分角色详情受极验风控,仅显示基础信息"
                        : null,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "库街区角色数据请求失败: roleId={RoleId}", roleId);
            return new RoleDataLoadResult
            {
                Source = RoleDataSource.None,
                Message = $"库街区请求失败: {ex.Message}",
            };
        }
    }

    /// <inheritdoc/>
    public RoleDataLoadResult LoadFromLocal()
    {
        var roles = _localReader.ReadFromLocalStorage();
        if (roles.Count > 0)
        {
            return new RoleDataLoadResult { Source = RoleDataSource.Local, Roles = roles };
        }
        return new RoleDataLoadResult { Source = RoleDataSource.None, Message = "本地未找到角色数据" };
    }

    /// <inheritdoc/>
    public RoleDataLoadResult LoadFromCache(string accountId, string playerId)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return new RoleDataLoadResult { Source = RoleDataSource.None, Message = "未配置角色 ID" };
        }
        try
        {
            var roles = ReadCacheRoles(accountId ?? "", playerId);
            if (roles is not null)
            {
                // 当前账号缓存详情不完整(某次同步被风控写入)→ 回退旧版完整缓存
                if (!roles.All(static r => r.IsDetailComplete))
                {
                    var legacy = ReadCompleteCacheRoles("", playerId);
                    if (legacy is not null)
                    {
                        return new RoleDataLoadResult
                        {
                            Source = RoleDataSource.Local, Roles = legacy, Message = "来自本地完整缓存(旧版账号键)",
                        };
                    }
                }
                return new RoleDataLoadResult { Source = RoleDataSource.Local, Roles = roles, Message = "来自本地缓存" };
            }

            // 兼容旧版:早期账号登录态未持久化时缓存以空账号键保存,同一玩家数据仍有效
            var legacyRow = ReadCompleteCacheRoles("", playerId);
            if (legacyRow is not null)
            {
                return new RoleDataLoadResult
                {
                    Source = RoleDataSource.Local, Roles = legacyRow, Message = "来自本地完整缓存(旧版账号键)",
                };
            }
            return new RoleDataLoadResult { Source = RoleDataSource.None, Message = "无缓存(或账号不一致)" };
        }
        catch (Exception)
        {
            return new RoleDataLoadResult { Source = RoleDataSource.None, Message = "缓存读取失败" };
        }
    }

    /// <summary>同步结果是否"详情齐全且未触发极验"——只有这样的结果才允许写缓存。</summary>
    internal static bool IsCompleteSync(bool geeTestTriggered, IReadOnlyList<RoleDetail> roles)
        => !geeTestTriggered && roles.Count > 0 && roles.All(static r => r.IsDetailComplete);

    /// <summary>读缓存行(account_id, player_id);无记录返回 null。</summary>
    private List<RoleDetail>? ReadCacheRoles(string accountId, string playerId)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "SELECT json FROM role_cache WHERE account_id = $account AND player_id = $playerId";
        cmd.Parameters.AddWithValue("$account", accountId ?? "");
        cmd.Parameters.AddWithValue("$playerId", playerId);
        var json = cmd.ExecuteScalar() as string;
        return string.IsNullOrEmpty(json)
            ? null
            : JsonSerializer.Deserialize(json, RoleJsonContext.Default.ListRoleDetail);
    }

    /// <summary>读缓存并校验详情齐全;缺失/不完整返回 null。</summary>
    private List<RoleDetail>? ReadCompleteCacheRoles(string accountId, string playerId)
    {
        var roles = ReadCacheRoles(accountId, playerId);
        return roles is { Count: > 0 } && roles.All(static r => r.IsDetailComplete) ? roles : null;
    }

    /// <summary>读取任意可用的完整缓存(当前账号行 → 旧版空账号键行);无则 null。</summary>
    private List<RoleDetail>? LoadCompleteCacheOrNull(string accountId, string playerId)
    {
        return ReadCompleteCacheRoles(accountId, playerId)
            ?? ReadCompleteCacheRoles("", playerId);
    }

    private void SaveCache(string accountId, string playerId, IReadOnlyList<RoleDetail> roles)
    {
        try
        {
            var json = JsonSerializer.Serialize(roles, RoleJsonContext.Default.ListRoleDetail);
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText =
                """
                INSERT INTO role_cache(account_id, player_id, json, update_time)
                VALUES ($account, $playerId, $json, $time)
                ON CONFLICT(account_id, player_id) DO UPDATE SET json = $json, update_time = $time
                """;
            cmd.Parameters.AddWithValue("$account", accountId ?? "");
            cmd.Parameters.AddWithValue("$playerId", playerId);
            cmd.Parameters.AddWithValue("$json", json);
            cmd.Parameters.AddWithValue("$time", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "写入角色缓存失败,不影响本次返回: playerId={PlayerId}", playerId);
        }
    }
}

[JsonSerializable(typeof(RoleDetail))]
[JsonSerializable(typeof(List<RoleDetail>))]
public sealed partial class RoleCacheJsonContext : JsonSerializerContext;
