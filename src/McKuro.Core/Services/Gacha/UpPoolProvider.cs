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
        if (model?.Data?.VersionPools is null)
        {
            return map;
        }

        var roleIds = new HashSet<int>();
        var weaponIds = new HashSet<int>();
        foreach (var pool in model.Data.VersionPools)
        {
            if (pool.UpFiveRoleIds is not null)
            {
                roleIds.UnionWith(pool.UpFiveRoleIds);
            }
            if (pool.UpFiveWeaponIds is not null)
            {
                weaponIds.UnionWith(pool.UpFiveWeaponIds);
            }
        }

        map[CardPoolType.RoleActivity] = roleIds;
        map[CardPoolType.WeaponsActivity] = weaponIds;
        var all = new HashSet<int>(roleIds);
        all.UnionWith(weaponIds);
        map[CardPoolType.Beginner] = all;
        map[CardPoolType.BeginnerChoice] = all;
        return map;
    }
}
