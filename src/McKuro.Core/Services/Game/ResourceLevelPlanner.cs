using McKuro.Core.Models.Game;

namespace McKuro.Core.Services.Game;

/// <summary>单个资源等级的可选信息(供 UI 展示「游戏大小 / 已安装」,对齐官方启动器)。</summary>
public sealed class ResourceLevelOption
{
    public required GameResourceLevel Level { get; init; }

    /// <summary>参数取值(uhd/hd/sd)。</summary>
    public required string Value { get; init; }

    /// <summary>组合名(HD/SD/UHD)。</summary>
    public required string BundleName { get; init; }

    /// <summary>该等级的总下载体积(common + 等级包);包清单未加载时为 0。</summary>
    public long TotalBytes { get; init; }

    /// <summary>该等级的全部文件已就位(即「已安装」)。</summary>
    public bool Installed { get; init; }
}

/// <summary>
/// 资源等级(画质包)切换计划。
/// </summary>
public sealed class ResourceLevelPlan
{
    /// <summary>需要下载的包:包名 + 包清单(文件列表)。</summary>
    public List<(string PackName, GameManifest Manifest)> Packages { get; init; } = [];

    /// <summary>目标等级取值(uhd/hd/sd)。</summary>
    public required string TargetLevel { get; init; }

    /// <summary>目标组合名(HD/SD/UHD)。</summary>
    public required string BundleName { get; init; }

    /// <summary>目标等级是否已经完整安装(无需任何下载)。</summary>
    public bool AlreadyInstalled { get; init; }

    /// <summary>需下载总字节数。</summary>
    public long DownloadBytes => Packages.Sum(p => p.Manifest.Files.Sum(f => f.Size));

    /// <summary>需下载总文件数。</summary>
    public int FileCount => Packages.Sum(p => p.Manifest.Files.Count);

    /// <summary>是否存在需要下载的内容。</summary>
    public bool HasWork => Packages.Count > 0;
}

/// <summary>
/// 资源等级规划的纯逻辑部分(不触网、不读盘,便于单测)。
/// <para>
/// 依据实测的官方新版协议:index.json 的 <c>resourcePacks</c> 给出各包(common/hd/sd/uhd),
/// <c>bundles</c> 给出等级组合(HD=[common,hd]、SD=[common,sd]、UHD=[common,uhd])。
/// 各等级包落盘目录互不重叠(<c>Client/Content/{HD,SD,UHD}/</c>),<c>common</c> 为共享包,
/// 因此「切换画质」= 下载目标等级包(以及缺失的 common),不会覆盖或删除已有等级的文件。
/// </para>
/// </summary>
public static class ResourceLevelPlanner
{
    /// <summary>等级取值 → 组合名(HD/SD/UHD)。</summary>
    public static string LevelToBundleName(string? level) =>
        LaunchArguments.Normalize(level).ToUpperInvariant();

    /// <summary>组合名(HD/SD/UHD)→ 等级取值;无法识别返回 null。</summary>
    public static string? BundleNameToLevel(string? bundleName) => bundleName?.Trim().ToUpperInvariant() switch
    {
        "UHD" => "uhd",
        "HD" => "hd",
        "SD" => "sd",
        _ => null,
    };

    /// <summary>
    /// 取某等级组合包含的包名(如 HD → [common, hd])。
    /// bundles 缺失时按官方约定回退为 [common, 等级包]。
    /// </summary>
    public static IReadOnlyList<string> ResolvePackNames(KuroResourcePackIndex index, string bundleName)
    {
        if (index.Bundles is not null
            && index.Bundles.TryGetValue(bundleName, out var bundle)
            && bundle.ResourcePacks is { Count: > 0 })
        {
            return bundle.ResourcePacks;
        }

        return ["common", bundleName.ToLowerInvariant()];
    }

    /// <summary>包体积(优先清单真实总量,回退 index.json 的 size)。</summary>
    public static long PackBytes(KuroResourcePack? pack) =>
        pack is null ? 0 : pack.TotalBytes > 0 ? pack.TotalBytes : pack.Size ?? 0;

    /// <summary>
    /// 某等级的总下载体积(common + 等级包)。
    /// </summary>
    public static long TotalBytesFor(KuroResourcePackIndex index, string bundleName)
    {
        long total = 0;
        foreach (var packName in ResolvePackNames(index, bundleName))
        {
            if (index.ResourcePacks is not null && index.ResourcePacks.TryGetValue(packName, out var pack))
            {
                total += PackBytes(pack);
            }
        }
        return total;
    }

    /// <summary>把 index.json 的 bundles 转成有序的等级选项(极致 → 高清 → 流畅)。</summary>
    public static IReadOnlyList<(GameResourceLevel Level, string Value, string BundleName)> EnumerateLevels(
        KuroResourcePackIndex index)
    {
        var available = index.Bundles?.Keys
            .Select(BundleNameToLevel)
            .Where(v => v is not null)
            .Select(v => v!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var result = new List<(GameResourceLevel, string, string)>();
        foreach (var level in new[] { GameResourceLevel.Ultra, GameResourceLevel.High, GameResourceLevel.Smooth })
        {
            var value = LaunchArguments.ToValue(level);
            // 只在服务端确实提供该等级时才列出(避免给出点了必然失败的选项)
            if (available is { Count: > 0 } && !available.Contains(value))
            {
                continue;
            }
            result.Add((level, value, LevelToBundleName(value)));
        }
        return result;
    }
}
