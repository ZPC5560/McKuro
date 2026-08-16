using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using McKuro.Core.Models.Roles;

namespace McKuro.Services;

/// <summary>
/// 角色图标磁盘持久化缓存:库街区正常时把角色详情图标缓存到 <c>icon_cache/</c>,
/// mcguide 攻略站兜底时按名称复用缓存图标,避免切换数据源(A/B 域名不同)导致的图标缺失/错位。
/// <para>
/// 目录结构:<c>icon_cache/index.json</c> 记录 category → name → 本地文件路径,
/// 图标存 <c>icon_cache/{category}/{safe(name)}.png</c>(category ∈ role/weapon/skill/echo/chain/attr)。
/// 文件名按名称清洗非法字符;单图标下载失败静默,不影响主流程。</para>
/// </summary>
public sealed class IconDiskCacheService
{
    public const string CategoryRole = "role";
    public const string CategoryWeapon = "weapon";
    public const string CategorySkill = "skill";
    public const string CategoryEcho = "echo";
    public const string CategoryChain = "chain";
    public const string CategoryAttr = "attr";

    private readonly string _cacheDir;
    private readonly string _indexPath;
    private readonly Func<string, CancellationToken, Task<byte[]?>> _download;
    private readonly object _lock = new();
    private readonly Dictionary<string, Dictionary<string, string>> _index = new(StringComparer.Ordinal);

    /// <param name="cacheDir">缓存目录;缺省为 <c>%AppData%\McKuro\icon_cache</c>。</param>
    /// <param name="download">下载委托(可注入便于测试);缺省用 <see cref="AppServices.Http"/> 下载字节。</param>
    public IconDiskCacheService(string? cacheDir = null, Func<string, CancellationToken, Task<byte[]?>>? download = null)
    {
        _cacheDir = cacheDir ?? Path.Combine(AppServices.AppDataDir, "icon_cache");
        _indexPath = Path.Combine(_cacheDir, "index.json");
        _download = download ?? DefaultDownload;
        LoadIndex();
    }

    /// <summary>默认下载:用应用共享 HttpClient 拉取字节(失败抛异常,由调用方静默处理)。</summary>
    private static async Task<byte[]?> DefaultDownload(string url, CancellationToken ct)
        => await AppServices.Http.GetByteArrayAsync(url, ct).ConfigureAwait(false);

    /// <summary>
    /// 缓存角色详情的所有图标:遍历角色立绘/武器/技能/声骸/共鸣链/属性,
    /// 对每个 (category, name, url):url 为 http 且本地无该 name 缓存时下载落盘并更新索引。
    /// 下载失败静默(不抛)。
    /// </summary>
    public async Task CacheRoleIconsAsync(RoleDetail role, CancellationToken ct = default)
    {
        if (role is null)
        {
            return;
        }
        foreach (var (category, name, url) in EnumerateIcons(role))
        {
            if (ct.IsCancellationRequested)
            {
                return;
            }
            if (!IsHttpUrl(url) || GetCachedIconPath(category, name) is not null)
            {
                continue; // 非 http(本地路径)或已缓存,跳过
            }
            try
            {
                var bytes = await _download(url, ct).ConfigureAwait(false);
                if (bytes is null || bytes.Length == 0)
                {
                    continue;
                }
                Store(category, name, bytes);
            }
            catch (Exception)
            {
                // 单图标下载失败静默,不影响其他图标
            }
        }
        SaveIndex();
    }

    /// <summary>查询本地缓存图标路径(做存在性校验);无缓存返回 null。</summary>
    public string? GetCachedIconPath(string category, string name)
    {
        if (string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(name))
        {
            return null;
        }
        lock (_lock)
        {
            if (_index.TryGetValue(category, out var map)
                && map.TryGetValue(name, out var path)
                && !string.IsNullOrEmpty(path)
                && File.Exists(path))
            {
                return path;
            }
        }
        return null;
    }

    /// <summary>解析图标:有缓存→本地路径;否则→fallbackUrl(如保留 mcguide 的 B 域名 URL)。</summary>
    public string ResolveIcon(string category, string name, string fallbackUrl)
        => GetCachedIconPath(category, name) ?? fallbackUrl;

    /// <summary>把名称清洗为合法文件名(非法文件名字符替换为 '_')。</summary>
    public static string Safe(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name ?? "";
        }
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            sb.Append(invalid.Contains(c) ? '_' : c);
        }
        return sb.ToString();
    }

    /// <summary>枚举角色详情所有 (category, name, url) 图标条目(跳过空名称/空 URL)。</summary>
    private static IEnumerable<(string Category, string Name, string Url)> EnumerateIcons(RoleDetail role)
    {
        if (role.Role is { } r && !string.IsNullOrWhiteSpace(r.RolePicUrl))
        {
            yield return (CategoryRole, role.RoleName, r.RolePicUrl);
        }
        if (role.WeaponData?.Weapon is { } w
            && !string.IsNullOrWhiteSpace(w.WeaponIcon)
            && !string.IsNullOrWhiteSpace(role.WeaponData.DisplayName))
        {
            yield return (CategoryWeapon, role.WeaponData.DisplayName, w.WeaponIcon);
        }
        if (role.Skills is not null)
        {
            foreach (var s in role.Skills)
            {
                if (s.Skill is { } sk && !string.IsNullOrWhiteSpace(sk.SkillName) && !string.IsNullOrWhiteSpace(sk.IconUrl))
                {
                    yield return (CategorySkill, sk.SkillName, sk.IconUrl);
                }
            }
        }
        if (role.Chains is not null)
        {
            foreach (var c in role.Chains)
            {
                if (!string.IsNullOrWhiteSpace(c.ChainName) && !string.IsNullOrWhiteSpace(c.IconUrl))
                {
                    yield return (CategoryChain, c.ChainName, c.IconUrl);
                }
            }
        }
        if (role.Attributes is not null)
        {
            foreach (var a in role.Attributes)
            {
                if (!string.IsNullOrWhiteSpace(a.AttributeName) && !string.IsNullOrWhiteSpace(a.IconUrl))
                {
                    yield return (CategoryAttr, a.AttributeName, a.IconUrl);
                }
            }
        }
        if (role.PhantomData?.Phantoms is not null)
        {
            foreach (var e in role.PhantomData.Phantoms)
            {
                if (!string.IsNullOrWhiteSpace(e.PhantomName) && !string.IsNullOrWhiteSpace(e.IconUrl))
                {
                    yield return (CategoryEcho, e.PhantomName, e.IconUrl);
                }
            }
        }
    }

    private static bool IsHttpUrl(string url)
        => url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
           || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    private void Store(string category, string name, byte[] bytes)
    {
        var dir = Path.Combine(_cacheDir, category);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, Safe(name) + ".png");
        File.WriteAllBytes(path, bytes);
        lock (_lock)
        {
            if (!_index.TryGetValue(category, out var map))
            {
                map = new Dictionary<string, string>(StringComparer.Ordinal);
                _index[category] = map;
            }
            map[name] = path;
        }
    }

    private void LoadIndex()
    {
        try
        {
            if (!File.Exists(_indexPath))
            {
                return;
            }
            var json = File.ReadAllText(_indexPath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return;
            }
            var loaded = JsonSerializer.Deserialize(json, IconCacheJsonContext.Default.IconCacheIndex);
            if (loaded?.Categories is not { } categories)
            {
                return;
            }
            lock (_lock)
            {
                _index.Clear();
                foreach (var kv in categories)
                {
                    _index[kv.Key] = new Dictionary<string, string>(kv.Value, StringComparer.Ordinal);
                }
            }
        }
        catch (Exception)
        {
            // 索引损坏/不可读:从空开始,下次缓存重建
        }
    }

    /// <summary>把当前索引持久化到 index.json(锁内串行写,失败静默)。</summary>
    public void SaveIndex()
    {
        lock (_lock)
        {
            try
            {
                Directory.CreateDirectory(_cacheDir);
                var doc = new IconCacheIndex { Categories = _index };
                var json = JsonSerializer.Serialize(doc, IconCacheJsonContext.Default.IconCacheIndex);
                File.WriteAllText(_indexPath, json);
            }
            catch (Exception)
            {
                // 写索引失败静默(磁盘只读等),不影响已落盘的图标文件
            }
        }
    }
}

/// <summary>索引文件 JSON 结构:categories = category → name → 本地文件路径。</summary>
public sealed class IconCacheIndex
{
    public Dictionary<string, Dictionary<string, string>> Categories { get; set; } = new(StringComparer.Ordinal);
}

[JsonSerializable(typeof(IconCacheIndex))]
internal sealed partial class IconCacheJsonContext : JsonSerializerContext;
