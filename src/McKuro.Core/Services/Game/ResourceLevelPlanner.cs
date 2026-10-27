using McKuro.Core.Models.Game;

namespace McKuro.Core.Services.Game;

/// <summary>单个资源等级的可选信息(供 UI 展示「游戏大小 / 需下载 / 已安装」,对齐官方启动器)。</summary>
public sealed class ResourceLevelOption
{
    public required GameResourceLevel Level { get; init; }

    /// <summary>参数取值(uhd/hd/sd)。</summary>
    public required string Value { get; init; }

    /// <summary>组合名(HD/SD/UHD)。</summary>
    public required string BundleName { get; init; }

    /// <summary>该等级的总下载体积(common + 等级包);包清单未加载时为 0。</summary>
    public long TotalBytes { get; init; }

    /// <summary>
    /// <b>切到这个等级实际还需要下载的字节数</b>(只算本地缺失的包)。
    /// <para>
    /// 与 <see cref="TotalBytes"/> 的区别很关键:已装 HD 的用户切 UHD,common 已经在了,
    /// 只需下 uhd(实测 61.50 GB),但 TotalBytes 会显示 common+uhd = 99.45 GB —— 多报 37.95 GB。
    /// 拿 TotalBytes 当"要下多少"就是在吓用户。
    /// </para>
    /// </summary>
    public long DownloadBytes { get; init; }

    /// <summary>该等级的全部文件已就位(即「已安装」)。</summary>
    public bool Installed { get; init; }
}

/// <summary>
/// 一次「读取资源等级列表」的完整结果:选项列表 + 官方画质说明页链接。
/// <para>
/// 链接单独拎出来,是因为它属于 <b>index.json 根节点</b>而不是某一档;而 VM 刷新面板时
/// 只需要多拿一个字段 —— 若为它再加一次网络请求就浪费了(同一个 index.json 已经拿过)。
/// </para>
/// </summary>
public sealed class ResourceLevelOptions
{
    public IReadOnlyList<ResourceLevelOption> Items { get; init; } = [];

    /// <summary>官方「画质怎么选」说明页(按当前界面语言挑);没有该节点时为 null。</summary>
    public string? HintUrl { get; init; }
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

    /// <summary>
    /// 切到某等级<b>实际还需要下载</b>的体积(只累加本地缺失的包)。
    /// <para>
    /// <b>为什么不能直接用 <see cref="TotalBytesFor"/>:</b>它算的是「这个等级有多大」,
    /// 而不是「你还差多少」。实测国服 3.7.1:common 40.17 GB + uhd 61.50 GB = 99.45 GB,
    /// 而已经装了 HD 的用户 common 本来就有,切 UHD 只需下 61.50 GB ——
    /// 展示 99.45 会多报 37.95 GB,足够让人放弃切换。
    /// </para>
    /// <para>
    /// "本地缺失"由调用方传入的判定函数决定(通常是「官方标记版本与服务端一致」),
    /// 这样这个纯函数不需要知道任何磁盘/标记细节,可直接单测。
    /// </para>
    /// </summary>
    /// <param name="index">新版协议索引。</param>
    /// <param name="bundleName">组合名(HD/SD/UHD)。</param>
    /// <param name="isPackInstalled">该包是否已完整安装(版本一致)。</param>
    public static long MissingBytesFor(
        KuroResourcePackIndex index,
        string bundleName,
        Func<string, bool> isPackInstalled)
    {
        long missing = 0;
        foreach (var packName in ResolvePackNames(index, bundleName))
        {
            if (isPackInstalled(packName))
            {
                continue;
            }
            if (index.ResourcePacks is not null && index.ResourcePacks.TryGetValue(packName, out var pack))
            {
                missing += PackBytes(pack);
            }
        }
        return missing;
    }

    /// <summary>
    /// 官方「画质怎么选」说明页链接(按界面语言挑,挑不到退英文,再退第一条)。
    /// <para>
    /// 数据来自 index.json 根节点的 <c>config.resourceLevelHint</c>:实测国服/B站只给
    /// <c>zh-Hans</c> 一条,国际服给全 10 种语言。没有该节点(或为空)时返回 null,
    /// 由 UI 决定不显示这个入口 —— 不要自己编一个链接。
    /// </para>
    /// </summary>
    /// <param name="index">新版协议索引。</param>
    /// <param name="language">当前界面语言(如 zh-Hans / en-US)。</param>
    public static string? LevelHintUrl(KuroResourcePackIndex index, string? language)
    {
        var hints = index.Config?.ResourceLevelHint;
        if (hints is null || hints.Count == 0)
        {
            return null;
        }

        // 界面语言是带地区的(en-US / zh-Hans),官方键是短码(en / zh-Hans / pt-BR),两轮匹配
        if (!string.IsNullOrWhiteSpace(language))
        {
            if (hints.TryGetValue(language, out var exact) && !string.IsNullOrWhiteSpace(exact))
            {
                return exact;
            }

            var shortCode = language.Split('-')[0];
            if (hints.TryGetValue(shortCode, out var byShort) && !string.IsNullOrWhiteSpace(byShort))
            {
                return byShort;
            }
        }

        if (hints.TryGetValue("en", out var en) && !string.IsNullOrWhiteSpace(en))
        {
            return en;
        }

        return hints.Values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
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

    // ---------- 删除资源包的安全规则(纯逻辑,便于单测) ----------

    /// <summary>共享包名:被所有等级组合引用,不得删除。</summary>
    public const string SharedPackName = "common";

    /// <summary>某个包是否被「不是它自己」的其它等级组合引用(即共享包)。</summary>
    public static bool IsSharedPack(KuroResourcePackIndex index, string packName)
    {
        if (string.Equals(packName, SharedPackName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (index.Bundles is null)
        {
            return false;
        }

        // 被两个及以上组合引用即视为共享(如某天 common 改名或多出别的共享包)
        var refs = index.Bundles.Values.Count(b =>
            b.ResourcePacks is { Count: > 0 } packs
            && packs.Any(p => string.Equals(p, packName, StringComparison.OrdinalIgnoreCase)));
        return refs > 1;
    }

    /// <summary>删除某个包的拒绝原因;可删除时返回 <c>null</c>。</summary>
    public enum PackDeleteBlockReason
    {
        /// <summary>可删除。</summary>
        None,

        /// <summary>共享包(common):被所有等级引用,删了每个等级都会缺文件。</summary>
        SharedPack,

        /// <summary>该包是当前生效等级的唯一完整等级:删掉会让游戏落到一个不完整的等级。</summary>
        ActiveLevelOnly,
    }

    /// <summary>
    /// 判断包能否安全删除(纯函数)。
    /// <para>
    /// 规则(这是对上游的**加固**:Haiyu 只做提示,不阻止):
    /// ① 共享包 <c>common</c> 一律不可删 —— 它被 HD/SD/UHD 共同引用;
    /// ② 若该包属于<b>当前生效等级</b>、且删掉后<b>没有任何其它完整等级</b>可用,则拒绝
    /// (否则用户会进入「设置指向一个已残缺的等级」的状态,游戏可能启动异常)。
    /// 存在其它完整等级时允许删除,由调用方把设置回退到那个等级。
    /// </para>
    /// </summary>
    /// <param name="index">新版协议索引。</param>
    /// <param name="packName">待删包名。</param>
    /// <param name="currentLevel">当前生效等级取值(uhd/hd/sd)。</param>
    /// <param name="installedPacks">已安装的包名集合(依据官方 launcherDownloadConfig 标记)。</param>
    public static PackDeleteBlockReason CanDeletePack(
        KuroResourcePackIndex index,
        string packName,
        string? currentLevel,
        IReadOnlySet<string> installedPacks)
    {
        if (IsSharedPack(index, packName))
        {
            return PackDeleteBlockReason.SharedPack;
        }

        // 删的是当前等级自己的包:看有没有"另一个完整等级"可回退
        var currentBundle = LevelToBundleName(currentLevel);
        var packBelongsToCurrent = ResolvePackNames(index, currentBundle)
            .Any(p => string.Equals(p, packName, StringComparison.OrdinalIgnoreCase));
        if (!packBelongsToCurrent)
        {
            return PackDeleteBlockReason.None;
        }

        var hasAlternative = EnumerateLevels(index)
            .Select(l => l.BundleName)
            .Where(b => !string.Equals(b, currentBundle, StringComparison.OrdinalIgnoreCase))
            .Any(b => ResolvePackNames(index, b).All(p => installedPacks.Contains(p)));

        return hasAlternative ? PackDeleteBlockReason.None : PackDeleteBlockReason.ActiveLevelOnly;
    }

    /// <summary>
    /// 删除某包后<b>是否</b>需要回退等级:仅当该包属于<b>当前生效等级</b>时才需要。
    /// <para>
    /// 为什么要单独判:删除「与当前等级无关」的包(当前 SD、删 UHD)只是在回收磁盘,
    /// 绝不能顺手改用户的画质选择 —— 否则「回收磁盘」会变成「静默改画质」。
    /// </para>
    /// </summary>
    public static bool NeedsFallbackAfterDelete(
        KuroResourcePackIndex index,
        string packName,
        string? currentLevel,
        IReadOnlySet<string> installedPacks)
    {
        if (IsSharedPack(index, packName))
        {
            return false; // 共享包本就不可删;真走到这里也不该改等级
        }

        var currentBundle = LevelToBundleName(currentLevel);
        return ResolvePackNames(index, currentBundle)
            .Any(p => string.Equals(p, packName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 删除某包后应回退到哪个等级(取值)。仅当该包属于当前等级时才有意义:
    /// 返回<b>已完整安装</b>的等级中排序最高者;没有则返回 <c>null</c>(调用方应拒绝删除)。
    /// </summary>
    public static string? FallbackLevelAfterDelete(
        KuroResourcePackIndex index,
        string packName,
        IReadOnlySet<string> installedPacks)
    {
        foreach (var (_, value, bundleName) in EnumerateLevels(index))
        {
            if (ResolvePackNames(index, bundleName)
                .Any(p => string.Equals(p, packName, StringComparison.OrdinalIgnoreCase)))
            {
                continue; // 排掉"本来就缺这个包"的等级
            }

            if (ResolvePackNames(index, bundleName).All(p => installedPacks.Contains(p)))
            {
                return value;
            }
        }
        return null;
    }

    /// <summary>该等级的包是否全部已安装(即该等级完整可用)。</summary>
    public static bool IsBundleInstalled(
        KuroResourcePackIndex index,
        string bundleName,
        IReadOnlySet<string> installedPacks) =>
        ResolvePackNames(index, bundleName).All(p => installedPacks.Contains(p));
}
