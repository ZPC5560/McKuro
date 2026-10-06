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
/// <para>刷新链(2026-10 修复):列表同步先调 refreshData 让库街区服务端回游戏服务器重拉最新数据——
/// 数据中心接口返回的是服务端缓存快照,不刷新则同步/详情都是旧数据。</para>
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
            return new RoleDataLoadResult { Source = RoleDataSource.None, Message = CoreStrings.T("Core.Roles.NoToken", "未配置库街区 Token") };
        }
        if (string.IsNullOrWhiteSpace(roleId))
        {
            return new RoleDataLoadResult { Source = RoleDataSource.None, Message = CoreStrings.T("Core.Roles.NoRoleId", "未配置角色 ID") };
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
                    Message = CoreStrings.F("Core.Roles.LoginExpired", $"登录已失效(账号可能已在其他设备登录),请重新登录 ({gamer.Msg ?? $"code={gamer.Code}"})", gamer.Msg ?? $"code={gamer.Code}"),
                };
            }
            var item = gamer?.Data?.FirstOrDefault(r => r.RoleId == roleId);
            if (item is null)
            {
                _logger.LogWarning("未找到角色条目(角色 ID 与账号不匹配): roleId={RoleId}", roleId);
                return new RoleDataLoadResult
                {
                    Source = RoleDataSource.None,
                    Message = CoreStrings.T("Core.Roles.RoleNotFound", "未找到该角色条目(请确认角色 ID 与当前账号一致)"),
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
                    Message = CoreStrings.T("Core.Roles.TokenFailed", "获取角色数据访问令牌失败(Token 可能已失效,请重新登录)"),
                };
            }
            _accessToken = accessToken;

            // 3. 触发服务端刷新(refreshData):数据中心接口(roleData/getRoleDetail)返回的是
            //    库街区服务端自己的缓存快照,不先刷新拿到的都是上次快照
            //    (用户实测:同步之后角色详情数据还是旧的)。
            //    2026-10 实测接口可用(3 字段 body + B-At 头 → 200/data:true);
            //    失败仅告警不阻断,继续用现有快照(旧数据好过没数据)。
            //    返回值记录日志(评审反馈):便于区分"刷新成功/极验被拦/参数缺失跳过"与真实失败
            var refreshed = await _api.RefreshDataAsync(
                accessToken, deviceId, roleId, item.ServerId ?? "", ct: ct).ConfigureAwait(false);
            if (!refreshed)
            {
                _logger.LogInformation("refreshData 未生效,本次列表沿用服务端现有快照: roleId={RoleId}", roleId);
            }

            // 4. 角色列表(roleData):仅基础列表,不做 getRoleDetail 批量请求
            //    (详情按用户点击角色时单独拉取)
            var list = await _api.GetRoleDataAsync(
                accessToken, deviceId, roleId, "android", ct).ConfigureAwait(false);

            // 5. 列表项合并本地缓存中已同步过的完整详情(按 cardRoleId 匹配):
            //    上次同步/已点击查看过的角色详情区在页面加载后即有数据,未命中的由点击时按需拉取
            MergeCachedDetails(list, _userId, roleId);

            // 6. 把合并后的整份列表写回缓存:旧实现只在单角色详情拉取成功时回写,列表同步本身不落盘,
            //    导致「同步拿到的新角色关掉页面就没了、重开又退回上次缓存里那几个」。
            //    合并写回会按 cardRoleId 保留缓存中已有的详情区块,不会丢上次的完整数据。
            MergeListIntoCachedRoles(_userId, roleId, list);

            return new RoleDataLoadResult
            {
                Source = RoleDataSource.Kujiequ,
                Roles = list,
                Message = list.Count == 0 ? CoreStrings.T("Core.Roles.EmptyList", "角色列表为空(接口返回空数据)") : null,
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
                Message = CoreStrings.F("Core.Roles.RequestFailed", $"库街区请求失败: {ex.Message}", ex.Message),
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

    /// <summary>
    /// 把整份角色列表合并进缓存并落盘(列表同步收尾)。
    /// <para>
    /// 合并规则:<b>新列表对"角色集合"有最终权威</b> —— 结果就是新列表本身(顺序也以接口为准),
    /// 缓存只用来给命中的角色<b>补全详情</b>(<see cref="MergeMissingSections"/>)。
    /// </para>
    /// <para>
    /// 早期实现是"缓存里多出来的角色保留、不丢弃",出发点是不丢数据,但副作用是
    /// <b>角色条目一旦进了缓存就永远出不去</b>:玩家换掉/替换掉的角色(实测:漂泊者 1406 被
    /// 1309 取代后,两者同名)会长期留在列表里,表现为"点了同步还是之前那几个/出现重复角色"。
    /// 因此改为以新列表为准;空列表仍然不覆盖(接口异常时不致清空用户数据)。
    /// </para>
    /// </summary>
    private void MergeListIntoCachedRoles(string userId, string roleId, IReadOnlyList<RoleDetail> freshList)
    {
        if (freshList.Count == 0)
        {
            return; // 空列表多为接口异常,不覆盖已有缓存(与"风控不覆盖完整缓存"同一原则)
        }
        var cached = ReadCacheRoles(userId, roleId) ?? ReadCacheRoles("", roleId) ?? [];
        var cachedByCardId = new Dictionary<int, RoleDetail>();
        foreach (var r in cached)
        {
            if (r.Role?.RoleId is int id and > 0)
            {
                cachedByCardId[id] = r;
            }
        }

        var merged = new List<RoleDetail>(freshList.Count);
        foreach (var fresh in freshList)
        {
            if (fresh.Role?.RoleId is int cardId and > 0
                && cachedByCardId.TryGetValue(cardId, out var old))
            {
                // 详情以缓存为准补进新列表项(列表项本身只有基础信息时不会清空已有详情)
                MergeMissingSections(fresh, old);
            }
            merged.Add(fresh);
        }
        SaveCache(userId, roleId, merged);
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
        // 声骸区块:不能只看"target 是不是 null"。
        // <para>
        // 陈旧快照问题(2026-10 实测):EchoProp.Valid 是 2026-10-05 才加入的字段,更早写盘的缓存
        // 整行没有 valid(全 null)。若这里无条件沿用旧区块,列表同步每轮都会把它原样写回缓存
        // (见 <see cref="MergeListIntoCachedRoles"/>),表现为"同步了却还是看不到有效词条",
        // 且永远不自愈。而库街区实时响应里 valid 一直是完整的(实测相里要/折枝/漂泊者/安可等
        // 均 null=0),所以缺的不是数据源,是覆盖策略。
        // </para>
        // <para>
        // 规则:target 无数据 → 用 source;target 有数据但<b>缺 valid 而 source 有</b> → 用 source
        // (只有"更完整的判定信息"才能覆盖,避免用旧数据覆盖新数据)。
        // </para>
        target.PhantomData = PickPhantomData(target.PhantomData, source.PhantomData);
        if (target.Chains is not { Count: > 0 })
        {
            target.Chains = source.Chains;
        }
    }

    /// <summary>
    /// 声骸区块合并择取:优先"带词条有效性判定"的一侧,避免陈旧快照(无 valid)粘滞。
    /// <para>target 为空 → source;source 无数据 → target;两者都有 real 数据时,
    /// 只要 target 缺 valid 且 source 有,就用 source 覆盖。</para>
    /// </summary>
    internal static PhantomData? PickPhantomData(PhantomData? target, PhantomData? source)
    {
        if (target is null)
        {
            return source;
        }
        if (source?.Phantoms is not { Count: > 0 })
        {
            return target; // 来源没数据,不覆盖
        }
        if (target.Phantoms is not { Count: > 0 })
        {
            return source; // 目标没数据,直接用来源
        }
        // 双方都有声骸:仅当来源带了有效性判定而目标没有时,才让来源覆盖(修陈旧快照)
        return !target.HasSubstatValidity && source.HasSubstatValidity ? source : target;
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
        return new RoleDataLoadResult { Source = RoleDataSource.None, Message = CoreStrings.T("Core.Roles.NoLocalData", "本地未找到角色数据") };
    }

    /// <inheritdoc/>
    public RoleDataLoadResult LoadFromCache(string accountId, string playerId)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return new RoleDataLoadResult { Source = RoleDataSource.None, Message = CoreStrings.T("Core.Roles.NoRoleId", "未配置角色 ID") };
        }
        try
        {
            var roles = ReadCacheRoles(accountId ?? "", playerId);
            if (roles is not null)
            {
                // 当前账号行以列表同步写入,天然含有只有基础信息的角色(详情按点击补),
                // 因此不能再用"整行是否全部完整"来决定是否换成旧的空账号键缓存 ——
                // 那会把刚同步到的整份角色列表换回旧版那几条(表现为"重开又退回几个角色")。
                // 正确做法:以当前账号行为准(它是最新的角色集合),仅用旧版缓存**补全详情**。
                var merged = MergeLegacyDetailInto(roles, ReadCacheRoles("", playerId));
                var usedLegacy = merged is not null;
                return new RoleDataLoadResult
                {
                    Source = RoleDataSource.Local,
                    Roles = usedLegacy ? merged! : roles,
                    Message = usedLegacy
                        ? CoreStrings.T("Core.Roles.FromLegacyCache", "来自本地完整缓存(旧版账号键)")
                        : CoreStrings.T("Core.Roles.FromCache", "来自本地缓存"),
                };
            }

            // 兼容旧版:早期账号登录态未持久化时缓存以空账号键保存,同一玩家数据仍有效
            var legacyRow = ReadCompleteCacheRoles("", playerId);
            if (legacyRow is not null)
            {
                return new RoleDataLoadResult
                {
                    Source = RoleDataSource.Local, Roles = legacyRow, Message = CoreStrings.T("Core.Roles.FromLegacyCache", "来自本地完整缓存(旧版账号键)"),
                };
            }

            // 账号键漂移兜底:账号被登出/自动移除(如 token 过期时签到页会移除失效账号)后
            // CurrentKuroUserId 会变空,而缓存仍写在真实账号键下。此时按 playerId 取最近写入的一行,
            // 否则「同步失败 → 回读缓存」这条路会报"无缓存(或账号不一致)",页面永远停在旧列表上
            // (用户看到的现象就是"点了同步还是之前的缓存")。playerId 本身即账号下的角色 ID,
            // 足以区分账号,按它兜底不会串号。
            var driftedRow = ReadNewestCacheRolesForPlayer(playerId);
            if (driftedRow is not null)
            {
                var merged = MergeLegacyDetailInto(driftedRow, ReadCacheRoles("", playerId));
                return new RoleDataLoadResult
                {
                    Source = RoleDataSource.Local,
                    Roles = merged ?? driftedRow,
                    Message = CoreStrings.T("Core.Roles.FromCache", "来自本地缓存"),
                };
            }
            return new RoleDataLoadResult { Source = RoleDataSource.None, Message = CoreStrings.T("Core.Roles.NoCache", "无缓存(或账号不一致)") };
        }
        catch (Exception)
        {
            return new RoleDataLoadResult { Source = RoleDataSource.None, Message = CoreStrings.T("Core.Roles.CacheReadFailed", "缓存读取失败") };
        }
    }

    /// <summary>
    /// 用旧版(空账号键)缓存补全当前账号行的详情:角色集合仍以 <paramref name="accountRoles"/> 为准
    /// (不因旧缓存多/少而增删),只把按 cardRoleId 命中的详情区块补进缺失项。
    /// 需至少补到一条才返回结果,否则返回 null 表示无需回退(调用方沿用原行并给常规提示)。
    /// </summary>
    private static List<RoleDetail>? MergeLegacyDetailInto(
        List<RoleDetail> accountRoles,
        List<RoleDetail>? legacyRoles)
    {
        if (legacyRoles is not { Count: > 0 })
        {
            return null;
        }
        var byCardId = new Dictionary<int, RoleDetail>();
        foreach (var r in legacyRoles)
        {
            if (r.Role?.RoleId is int id and > 0 && r.IsDetailComplete)
            {
                byCardId[id] = r;
            }
        }
        if (byCardId.Count == 0)
        {
            return null;
        }
        var filled = false;
        foreach (var role in accountRoles)
        {
            if (role.IsDetailComplete || role.Role?.RoleId is not int id || id <= 0)
            {
                continue;
            }
            if (byCardId.TryGetValue(id, out var legacy))
            {
                MergeMissingSections(role, legacy);
                filled = true;
            }
        }
        return filled ? accountRoles : null;
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

    /// <summary>
    /// 按 playerId 取最近写入的一行缓存(忽略 account_id)。
    /// <para>用于账号键漂移的兜底:token 失效后账号被自动移除,CurrentKuroUserId 变空,
    /// 但缓存仍写在真实账号键下;此时不应报"无缓存"。</para>
    /// </summary>
    private List<RoleDetail>? ReadNewestCacheRolesForPlayer(string playerId)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return null;
        }
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText =
            """
            SELECT json FROM role_cache
            WHERE player_id = $playerId
            ORDER BY update_time DESC
            LIMIT 1
            """;
        cmd.Parameters.AddWithValue("$playerId", playerId);
        var json = cmd.ExecuteScalar() as string;
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }
        var roles = JsonSerializer.Deserialize(json, RoleJsonContext.Default.ListRoleDetail);
        return roles is { Count: > 0 } ? roles : null;
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
