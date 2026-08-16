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

        // 权威来源:pool_list 中所有卡池的 up_five_ids(历史 + 当期)。
        // 注意:不做"当期生效"过滤——限定角色无论何时抽到都应算 UP,
        // 只有从未 UP 过的常驻角色才算歪(用当前期过滤会把历史限定全误判为歪)。
        // five_maps 只是全量五星目录(含常驻),仅作无 pool_list 时的兜底。
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

            // pool_list 存在但没有任何 UP 时,用全量目录兜底以避免误判
            if (roleIds.Count == 0 && weaponIds.Count == 0)
            {
                CollectFromFiveMaps(model.Data.FiveGroupConfig?.FiveMaps, roleIds, weaponIds);
            }
        }
        else
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
        // 常驻池:用当期全部 UP(当期 UP 中出现的常驻角色判定;不在集合=歪)
        map[CardPoolType.RoleResident] = all;
        map[CardPoolType.WeaponsResident] = weaponIds;
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
