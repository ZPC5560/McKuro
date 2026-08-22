using McKuro.Core.Models.Gacha;
using Microsoft.Extensions.Logging;

namespace McKuro.Core.Services.Gacha;

/// <summary>
/// UP 五星 ID 提供者:从第三方聚合接口获取当前版本 UP 角色/武器。
/// 不可用时可返回空集合(此时不判定歪/不歪)。
/// </summary>
public interface IUpPoolProvider
{
    Task<IReadOnlyDictionary<CardPoolType, HashSet<int>>> GetUpIdsAsync(CancellationToken ct = default);
}

/// <summary>聚合接口:https://api3.sanyueqi.cn/api/v1/pool/draw_config_infos</summary>
public sealed class RemoteUpPoolProvider : IUpPoolProvider
{
    private const string ApiUrl = "https://api3.sanyueqi.cn/api/v1/pool/draw_config_infos";

    private readonly HttpClient _http;
    private readonly ILogger<RemoteUpPoolProvider> _logger;
    private readonly TimeProvider _time;
    private IReadOnlyDictionary<CardPoolType, HashSet<int>>? _cache;
    private DateTimeOffset _cacheTime;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(6);

    public RemoteUpPoolProvider(HttpClient http, TimeProvider? time = null, ILogger<RemoteUpPoolProvider>? logger = null)
    {
        _http = http;
        _time = time ?? TimeProvider.System;
        _logger = logger ?? NullLogger<RemoteUpPoolProvider>.Instance;
    }

    public async Task<IReadOnlyDictionary<CardPoolType, HashSet<int>>> GetUpIdsAsync(CancellationToken ct = default)
    {
        if (_cache is not null && _time.GetUtcNow() - _cacheTime < CacheDuration)
        {
            return _cache;
        }

        try
        {
            using var response = await _http.GetAsync(ApiUrl, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var model = await System.Text.Json.JsonSerializer.DeserializeAsync(
                stream,
                UpPoolJsonContext.Default.FiveGroupModel,
                ct).ConfigureAwait(false);

            var result = BuildMap(model);
            _cache = result;
            _cacheTime = _time.GetUtcNow();
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "拉取远程 UP 卡池配置失败,返回空集合");
            return new Dictionary<CardPoolType, HashSet<int>>();
        }
    }

    private static IReadOnlyDictionary<CardPoolType, HashSet<int>> BuildMap(FiveGroupModel? model)
    {
        var map = new Dictionary<CardPoolType, HashSet<int>>();
        if (model?.Data is null)
        {
            return map;
        }

        var roleIds = new HashSet<int>();
        var weaponIds = new HashSet<int>();

        // 权威来源:five_maps 的 pool_type 区分限定/常驻——
        //   pool_type 为空/非0 = 限定角色(无论何时抽到都算 UP)
        //   pool_type == 0    = 常驻角色(如卡卡罗/凌阳,抽到算歪)
        // pool_list 只含近期卡池,覆盖不了历史限定,不能作为唯一来源。
        var fiveMaps = model.Data.FiveGroupConfig?.FiveMaps;
        if (fiveMaps is { Count: > 0 })
        {
            foreach (var m in fiveMaps)
            {
                if (m is null)
                {
                    continue;
                }
                // 限定角色(pool_type 空/非0)才进 UP 集合;常驻(pool_type=0)排除
                if (m.PoolType != 0 && m.ItemId > 0)
                {
                    roleIds.Add(m.ItemId);
                }
                if (m.PoolType != 0 && m.WeaponId > 0)
                {
                    weaponIds.Add(m.WeaponId);
                }
            }
        }

        // 补充:pool_list 的 up_five_ids(兜底近期池,与 five_maps 并集)
        if (model.Data.PoolList is { Count: > 0 } pools)
        {
            foreach (var pool in pools)
            {
                var ids = ParseIds(pool.UpFiveIds);
                if (string.Equals(pool.Type, "role", StringComparison.OrdinalIgnoreCase))
                {
                    roleIds.UnionWith(ids);
                }
                else if (string.Equals(pool.Type, "weapon", StringComparison.OrdinalIgnoreCase))
                {
                    weaponIds.UnionWith(ids);
                }
            }
        }

        // 兜底:既无 five_maps 也无 pool_list 时用全量目录(避免空集导致全判歪)
        if (roleIds.Count == 0 && weaponIds.Count == 0)
        {
            CollectFromFiveMaps(model.Data.FiveGroupConfig?.FiveMaps, roleIds, weaponIds);
        }

        var all = new HashSet<int>(roleIds);
        all.UnionWith(weaponIds);

        // 角色类池:当期 UP 角色+武器都可判定为 UP;武器类池:当期 UP 武器
        map[CardPoolType.RoleActivity] = roleIds;
        map[CardPoolType.WeaponsActivity] = weaponIds;
        map[CardPoolType.Beginner] = all;
        map[CardPoolType.BeginnerChoice] = all;
        map[CardPoolType.GratitudeOrientation] = all;
        map[CardPoolType.CharacterNovice] = roleIds;
        map[CardPoolType.WeaponNovice] = weaponIds;
        map[CardPoolType.CharacterCollaboration] = roleIds;
        map[CardPoolType.WeaponCollaboration] = weaponIds;
        map[CardPoolType.CharacterMemoryJourney] = roleIds;
        map[CardPoolType.WeaponMemoryJourney] = weaponIds;
        // 常驻池(角色/武器常驻)无 UP 概念:不提供 UP 集合,五星一律不做歪/UP 判定,
        // 由 GachaAnalysisService 完成最终守卫(常驻/新手类池不判歪)。
        return map;
    }

    /// <summary>从全量五星目录(five_maps)收集角色/武器 ID(兜底用)。</summary>
    private static void CollectFromFiveMaps(
        IReadOnlyList<FiveMap>? fiveMaps,
        HashSet<int> roleIds,
        HashSet<int> weaponIds)
    {
        if (fiveMaps is null)
        {
            return;
        }
        foreach (var m in fiveMaps)
        {
            if (m is null)
            {
                continue;
            }
            if (m.ItemId > 0)
            {
                roleIds.Add(m.ItemId);
            }
            if (m.WeaponId > 0)
            {
                weaponIds.Add(m.WeaponId);
            }
        }
    }

    /// <summary>解析逗号分隔的 UP 五星 ID 字符串。</summary>
    private static HashSet<int> ParseIds(string? raw)
    {
        var result = new HashSet<int>();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return result;
        }
        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(part, out var id) && id > 0)
            {
                result.Add(id);
            }
        }
        return result;
    }
}
