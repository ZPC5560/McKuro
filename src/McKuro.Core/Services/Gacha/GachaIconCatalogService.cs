using System.Text.Json;
using McKuro.Core.Models.Gacha;
using Microsoft.Extensions.Logging;

namespace McKuro.Core.Services.Gacha;

/// <summary>
/// 图鉴图标目录服务:从 mc.appfeng.com 拉取全量角色/武器目录(avatar.json / weapon.json),
/// 注入 <see cref="IconCatalog"/> 供抽卡分析页解析头像与武器图标。
/// <para>
/// <b>为什么需要它</b>:<see cref="IconCatalog"/> 原本是一份手写的 ResourceId 字典,
/// 每次游戏版本新增五星角色/武器都必须手工补条目,漏补时 <c>GetRoleIconUrl</c> 返回空串,
/// 页面静默留白——表现为"抽到新角色没有头像"(心的 1311 即此漏网)。改为运行时拉取后,
/// 限定与常驻的新增角色/武器都自动覆盖,不再需要改代码。
/// </para>
/// <para>
/// <b>降级策略</b>:磁盘缓存(默认 24h TTL 内直接用)→ 网络拉取并落盘 → 失败时沿用过期缓存 →
/// 仍无则保留 <see cref="IconCatalog"/> 的静态字典兜底。任何一步失败都不会让页面变空白。
/// </para>
/// </summary>
public sealed class GachaIconCatalogService
{
    private const string RoleCatalogUrl = "https://mc.appfeng.com/json/avatar.json";
    private const string WeaponCatalogUrl = "https://mc.appfeng.com/json/weapon.json";

    /// <summary>缓存格式版本,与 <see cref="IconCatalogCache.Version"/> 对应。</summary>
    private const int CacheVersion = 1;

    /// <summary>缓存有效期:24 小时内不重复拉取。</summary>
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);

    private readonly HttpClient _http;
    private readonly string _cachePath;
    private readonly TimeProvider _time;
    private readonly ILogger<GachaIconCatalogService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>清空目录(仅测试用;让远程快照与静态兜底的行为可分别断言)。</summary>
    internal static void ResetForTesting() => IconCatalog.ApplyRemoteCatalog(null, null);

    public GachaIconCatalogService(
        HttpClient http,
        string appDataDir,
        TimeProvider? time = null,
        ILogger<GachaIconCatalogService>? logger = null)
    {
        _http = http;
        _cachePath = Path.Combine(appDataDir, "icon_catalog.json");
        _time = time ?? TimeProvider.System;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<GachaIconCatalogService>.Instance;
    }

    /// <summary>缓存文件路径(诊断/测试用)。</summary>
    public string CachePath => _cachePath;

    /// <summary>
    /// 同步应用磁盘缓存(无网络、无异常抛出)。必须在首屏渲染前调用,
    /// 否则抽卡分析页首次绑定时 <see cref="IconCatalog"/> 只有静态字典,新角色仍会短暂无头像。
    /// </summary>
    public void ApplyCachedCatalog()
    {
        if (LoadCache(allowExpired: true) is { } cached)
        {
            IconCatalog.ApplyRemoteCatalog(cached.Roles, cached.Weapons);
        }
    }

    /// <summary>
    /// 确保目录可用:缓存新鲜则直接返回;否则拉取并落盘。失败时沿用过期缓存/静态字典,不抛异常。
    /// </summary>
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (LoadCache(allowExpired: false) is { } fresh)
            {
                IconCatalog.ApplyRemoteCatalog(fresh.Roles, fresh.Weapons);
                return;
            }

            var roles = await TryFetchAsync(RoleCatalogUrl, ct).ConfigureAwait(false);
            var weapons = await TryFetchAsync(WeaponCatalogUrl, ct).ConfigureAwait(false);

            // 两个目录都拉失败:保持现状(过期缓存已应用 / 静态字典),不落盘空目录把好数据覆盖掉
            if (roles.Count == 0 && weapons.Count == 0)
            {
                _logger.LogDebug("图鉴目录拉取失败,继续使用缓存或静态兜底");
                return;
            }

            // 只合并"这次成功的部分",避免某一边偶发失败时把已有目录清空
            var previous = LoadCache(allowExpired: true);
            if (roles.Count == 0 && previous is not null)
            {
                roles = previous.Roles;
            }
            if (weapons.Count == 0 && previous is not null)
            {
                weapons = previous.Weapons;
            }

            var cache = new IconCatalogCache
            {
                Version = CacheVersion,
                FetchedAt = _time.GetUtcNow(),
                Roles = roles,
                Weapons = weapons,
            };
            IconCatalog.ApplyRemoteCatalog(cache.Roles, cache.Weapons);
            SaveCache(cache);
            _logger.LogInformation("图鉴目录已更新:角色 {Roles} 条,武器 {Weapons} 条", roles.Count, weapons.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "刷新图鉴目录失败,继续使用缓存或静态兜底");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>拉取并解析单个目录;失败(网络/超长/JSON 异常)返回空字典。</summary>
    private async Task<Dictionary<int, string>> TryFetchAsync(string url, CancellationToken ct)
    {
        try
        {
            using var response = await _http.GetAsync(url, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var items = await JsonSerializer.DeserializeAsync(
                stream, IconCatalogJsonContext.Default.ListCommunityIconItem, ct).ConfigureAwait(false);

            var result = new Dictionary<int, string>();
            if (items is null)
            {
                return result;
            }
            foreach (var item in items)
            {
                if (item.Id > 0 && !string.IsNullOrWhiteSpace(item.Icon))
                {
                    result[item.Id] = item.Icon!;
                }
            }
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "拉取图鉴目录失败: {Url}", url);
            return [];
        }
    }

    /// <summary>读取磁盘缓存;不存在/损坏/版本不符返回 null。</summary>
    private IconCatalogCache? LoadCache(bool allowExpired)
    {
        try
        {
            if (!File.Exists(_cachePath))
            {
                return null;
            }
            var cache = JsonSerializer.Deserialize(
                File.ReadAllText(_cachePath), IconCatalogJsonContext.Default.IconCatalogCache);
            if (cache is null || cache.Version != CacheVersion
                || (cache.Roles.Count == 0 && cache.Weapons.Count == 0))
            {
                return null;
            }
            if (!allowExpired && _time.GetUtcNow() - cache.FetchedAt > CacheTtl)
            {
                return null;
            }
            return cache;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "读取图鉴目录缓存失败: {Path}", _cachePath);
            return null;
        }
    }

    /// <summary>原子写入缓存(失败仅记日志,不影响已应用的目录)。</summary>
    private void SaveCache(IconCatalogCache cache)
    {
        try
        {
            var dir = Path.GetDirectoryName(_cachePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            var tempPath = _cachePath + ".tmp-" + Guid.NewGuid().ToString("N");
            File.WriteAllText(tempPath,
                JsonSerializer.Serialize(cache, IconCatalogJsonContext.Default.IconCatalogCache));
            File.Move(tempPath, _cachePath, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "写入图鉴目录缓存失败: {Path}", _cachePath);
        }
    }
}
