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
/// <para>同步链拆分(2026-08 优化):<see cref="LoadRoleListAsync"/> 只拉角色列表(roleData),
/// <see cref="LoadRoleDetailAsync"/> 在用户点击具体角色时单发 getRoleDetail——
/// 页面加载时不再批量串行拉全量详情(高频接口易触发极验风控,且列表页无需全部详情)。</para>
/// </summary>
public sealed class RoleDataService : IRoleDataService
{
    private readonly KujiequApiClient _api;
    private readonly LocalRoleDataReader _localReader;
    private readonly AppDatabase _db;
    private readonly KuroClient _kuro;
    private readonly KuroAccountService _accounts;
    private readonly ILogger<RoleDataService> _logger;

    /// <summary>最近一次列表同步获得的访问令牌(详情按需加载时复用,避免每次点击重复 getGamer/requestToken)。</summary>
    private string? _accessToken;

    /// <summary>最近一次列表同步获得的库街区 userId(与 <see cref="_accessToken"/> 配套)。</summary>
    private string _userId = "";

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
    public Task<RoleDataLoadResult> LoadRoleListAsync(
        string token,
        string roleId,
        CancellationToken ct = default)
        => LoadRoleListCoreAsync(token, roleId, ct);

    /// <summary>列表同步主流程:getGamer → requestToken → roleData(仅列表,不请求任何 getRoleDetail)。</summary>
    private async Task<RoleDataLoadResult> LoadRoleListCoreAsync(
        string token,
        string roleId,
        CancellationToken ct)
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
            await EnsurePublicIpAsync(ct).ConfigureAwait(false);
            var deviceId = _accounts.Current?.DeviceId ?? Guid.NewGuid().ToString("N");

            // 1. 通过角色列表接口确认角色条目存在,并取库街区 userId(requestToken 需要)
            //    (对齐 WutheringWavesTool: serverId/gameId 用固定官方值,只需条目 roleId + userId)
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
            _userId = item.UserId ?? _accounts.Current?.UserId ?? "";

            // 2. requestToken 换 B-At 令牌(对齐 WutheringWavesTool BaseTask.requestToken)
            var accessToken = await _api.GetAccessTokenAsync(
                token, deviceId, roleId, _userId, "android", ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(accessToken))
            {
                _logger.LogWarning("获取角色数据访问令牌失败: roleId={RoleId}", roleId);
                return new RoleDataLoadResult
                {
                    Source = RoleDataSource.None,
                    Message = "获取角色数据访问令牌失败(Token 可能已失效,请重新登录)",
                };
            }
            _accessToken = accessToken;

            // 3. 角色列表(roleData):仅基础列表,不做 getRoleDetail 批量请求
            //    (refreshData 已被服务端停用,不再调用;详情按用户点击角色时单独拉取)
            var list = await _api.GetRoleDataAsync(
                accessToken, deviceId, roleId, "android", ct).ConfigureAwait(false);

            // 4. 列表项合并本地缓存中已同步过的完整详情(按 cardRoleId 匹配):
            //    上次同步/已点击查看过的角色详情区在页面加载后即有数据,未命中的由点击时按需拉取
            MergeCachedDetails(list, _userId, roleId);

            return new RoleDataLoadResult
            {
                Source = RoleDataSource.Kujiequ,
                Roles = list,
                Message = list.Count == 0 ? "角色列表为空(接口返回空数据)" : null,
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "库街区角色列表请求失败: roleId={RoleId}", roleId);
            return new RoleDataLoadResult
            {
                Source = RoleDataSource.None,
                Message = $"库街区请求失败: {ex.Message}",
            };
        }
    }

    /// <inheritdoc/>
    public async Task<KujiequApiClient.RoleDetailResult> LoadRoleDetailAsync(
        string token,
        string roleId,
        int targetRoleId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(roleId) || targetRoleId <= 0)
        {
            return new KujiequApiClient.RoleDetailResult(null, false);
        }

        try
        {
            await EnsurePublicIpAsync(ct).ConfigureAwait(false);
            var deviceId = _accounts.Current?.DeviceId ?? Guid.NewGuid().ToString("N");

            // 1. 复用在用的访问令牌(列表同步后点击);否则完整走 getGamer → requestToken
            if (string.IsNullOrEmpty(_accessToken))
            {
                if (!await EnsureAccessTokenAsync(token, roleId, deviceId, ct).ConfigureAwait(false))
                {
                    return new KujiequApiClient.RoleDetailResult(null, false);
                }
            }

            // 2. 单角色详情(单次请求;与用户点击节流,不并发批量)
            var result = await _api.GetRoleDetailResultAsync(
                _accessToken!, deviceId, roleId, targetRoleId, "android", ct).ConfigureAwait(false);
            if (result.Detail is not null)
            {
                UpdateCacheRole(_userId, roleId, result.Detail);
                return result;
            }
            if (result.GeeTest)
            {
                // 极验风控:不重试验证(角色场景无法解除),由界面提示稍后重试
                return result;
            }

            // 3. 非风控失败(如令牌过期):重新鉴权后重试一次(250ms 间隔,保持串行节流)
            _accessToken = null;
            if (await EnsureAccessTokenAsync(token, roleId, deviceId, ct).ConfigureAwait(false))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), ct).ConfigureAwait(false);
                result = await _api.GetRoleDetailResultAsync(
                    _accessToken!, deviceId, roleId, targetRoleId, "android", ct).ConfigureAwait(false);
                if (result.Detail is not null)
                {
                    UpdateCacheRole(_userId, roleId, result.Detail);
                }
            }
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "角色详情请求失败: roleId={RoleId} id={TargetRoleId}", roleId, targetRoleId);
            return new KujiequApiClient.RoleDetailResult(null, false);
        }
    }

    /// <summary>Devcode 头需要公网 IP(IP 未就绪时主动拉取一次,防止 devCode 缺 IP 特征触发风控)。</summary>
    private async Task EnsurePublicIpAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_kuro.Ip))
        {
            try
            {
                await _kuro.InitAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "获取公网 IP 失败,devCode 将仅含 UA");
            }
        }
        _api.PublicIp = _kuro.Ip;
    }

    /// <summary>getGamer → 校验角色条目 → requestToken;成功写入 <see cref="_accessToken"/>/<see cref="_userId"/> 并返回 true。</summary>
    private async Task<bool> EnsureAccessTokenAsync(string token, string roleId, string deviceId, CancellationToken ct)
    {
        var gamer = await _kuro.GetGamerAsync(
            new KuroAccount { Token = token, DeviceId = deviceId },
            (int)KuroGameType.Waves,
            ct).ConfigureAwait(false);
        if (gamer is not null && gamer.Code != 200)
        {
            _logger.LogWarning("库街区角色列表接口返回非 200(可能 token 失效): code={Code} msg={Msg}",
                gamer.Code, gamer.Msg);
            return false;
        }
        var item = gamer?.Data?.FirstOrDefault(r => r.RoleId == roleId);
        if (item is null)
        {
            _logger.LogWarning("未找到角色条目(角色 ID 与账号不匹配): roleId={RoleId}", roleId);
            return false;
        }
        _userId = item.UserId ?? _accounts.Current?.UserId ?? "";
        var accessToken = await _api.GetAccessTokenAsync(
            token, deviceId, roleId, _userId, "android", ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(accessToken))
        {
            _logger.LogWarning("获取角色数据访问令牌失败: roleId={RoleId}", roleId);
            return false;
        }
        _accessToken = accessToken;
        return true;
    }

    /// <summary>把缓存中已同步过的角色详情合并进新拉取的列表项(按 cardRoleId 匹配;保留列表已有的最新基础信息)。</summary>
    private void MergeCachedDetails(IReadOnlyList<RoleDetail> freshList, string userId, string roleId)
    {
        var cached = ReadCacheRoles(userId, roleId) ?? ReadCacheRoles("", roleId);
        if (cached is not { Count: > 0 })
        {
            return;
        }
        var byCardId = new Dictionary<int, RoleDetail>();
        foreach (var r in cached)
        {
            if (r.Role?.RoleId is int id and > 0)
            {
                byCardId[id] = r;
            }
        }
        foreach (var item in freshList)
        {
            if (item.Role?.RoleId is int id and > 0 && byCardId.TryGetValue(id, out var cachedRole))
            {
                MergeMissingSections(item, cachedRole);
            }
        }
    }

    /// <summary>
    /// 把 source 的详情区块补进 target 缺失的部位(武器/技能/属性/声骸/共鸣链;基础信息以 target 为准)。
    /// </summary>
    internal static void MergeMissingSections(RoleDetail target, RoleDetail source)
    {
        if (target.Role is null && source.Role is not null)
        {
            target.Role = source.Role;
        }
        else if (target.Role is { } targetRole && source.Role is { } sourceRole)
        {
            if (targetRole.StarLevel <= 0)
            {
                targetRole.StarLevel = sourceRole.StarLevel;
            }
            if (string.IsNullOrWhiteSpace(targetRole.RoleIconUrl))
            {
                targetRole.RoleIconUrl = sourceRole.RoleIconUrl;
            }
            if (string.IsNullOrWhiteSpace(targetRole.RolePicUrl))
            {
                targetRole.RolePicUrl = sourceRole.RolePicUrl;
            }
            if (targetRole.ChainUnlockNum <= 0)
            {
                targetRole.ChainUnlockNum = sourceRole.ChainUnlockNum;
            }
        }
        target.WeaponData ??= source.WeaponData;
        if (target.Skills is not { Count: > 0 })
        {
            target.Skills = source.Skills;
        }
        if (target.Attributes is not { Count: > 0 })
        {
            target.Attributes = source.Attributes;
        }
        target.PhantomData ??= source.PhantomData;
        if (target.Chains is not { Count: > 0 })
        {
            target.Chains = source.Chains;
        }
    }

    /// <summary>
    /// 单角色详情拉取成功后回写缓存(按 cardRoleId 合并进现有行,不覆盖其他角色数据;
    /// 页面加载/列表同步本身不写缓存——基础列表不含详情,写缓存会丢上次的完整数据)。
    /// </summary>
    private void UpdateCacheRole(string userId, string roleId, RoleDetail fresh)
    {
        if (fresh.Role?.RoleId is not int cardId || cardId <= 0)
        {
            return; // 无 cardRoleId 的详情不参与缓存,避免污染现有行
        }
        var roles = ReadCacheRoles(userId, roleId) ?? ReadCacheRoles("", roleId) ?? new List<RoleDetail>();
        var idx = roles.FindIndex(r => (r.Role?.RoleId ?? 0) == cardId);
        if (idx >= 0)
        {
            roles[idx] = fresh;
        }
        else
        {
            roles.Add(fresh);
        }
        SaveCache(userId, roleId, roles);
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
