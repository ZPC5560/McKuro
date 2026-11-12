using System.Text.Json.Serialization;

namespace McKuro.Core.Models.Game;

/// <summary>
/// 预下载规格(对齐官方启动器 3.8 起的「资源分类下载」)。
/// <para>
/// 官方在 PC 端把预下载内容分成两档:<b>全量</b>一次下载大部分资源、进游戏后只需少量补充;
/// <b>精简</b>只下启动游戏所必需的部分,其余按账号进度进游戏后再补。
/// </para>
/// <para>
/// 服务端没有单独的「规格」字段 —— 判据在补丁清单顶层的
/// <c>tagGroups.min.exclude_prefixes</c>:命中该前缀的分组属于「进游戏后再补」的内容,
/// 精简规格整组不下载。实测国服 3.8.0(HD 组合,common+hd):
/// 全量 12.08 GiB / 精简 7.30 GiB,与官方启动器显示值逐字节一致。
/// </para>
/// </summary>
public enum PredownloadSpec
{
    /// <summary>全量:下载清单里的全部内容。</summary>
    Full,

    /// <summary>精简:只下启动所需的,跳过多媒体等可后补的分组。</summary>
    Minimal,
}

/// <summary>
/// 新版协议(<c>.../launcher/game/{appKey}/G152/official/index.json</c>)的 <c>predownload</c> 节点。
/// <para>
/// <b>与旧协议的关键差异</b>:旧协议的 <c>predownload</c> 是一个完整的
/// <see cref="KuroUpdateData"/>(带 <c>version</c> / <c>config</c> / <c>cdnList</c>);
/// 新版协议的 <c>predownload</c> <b>只有</b> <c>bundles</c> + <c>resourcePacks</c>,
/// 版本号在<b>每个包自己的</b> <c>version</c> 里。
/// </para>
/// <para>
/// 因此它不能复用 <see cref="KuroUpdateData"/> —— 硬套会得到一个 <c>Version</c> 为 null 的对象,
/// 上游据此判断"当前没有可用的预载版本"(按钮出现却点了报错)。
/// </para>
/// </summary>
public sealed class KuroPredownloadNode
{
    /// <summary>资源等级组合(键为 HD/SD/UHD)。</summary>
    [JsonPropertyName("bundles")] public Dictionary<string, KuroResourceBundle>? Bundles { get; set; }

    /// <summary>预下载各资源包(键为 common/hd/sd/uhd)。</summary>
    [JsonPropertyName("resourcePacks")] public Dictionary<string, KuroResourcePack>? ResourcePacks { get; set; }
}

/// <summary>
/// 补丁清单顶层的 <c>tagGroups</c>:官方用它描述「哪些分组不属于精简规格」。
/// <para>
/// 实测形如 <c>{"min":{"exclude_prefixes":["Client/Saved/Resources/"]}}</c>,
/// 即「<c>Client/Saved/Resources/</c> 下的可选内容不进精简包」——
/// 那正是剧情过场/场景包(<c>Option_Pack_*</c>)所在目录,官方文档说的
/// 「精简资源仅包含当前账号所处游戏进度所需的必要资源」就是它。
/// </para>
/// </summary>
public sealed class KuroPatchTagGroups
{
    /// <summary>「精简」档要排除的规则。</summary>
    [JsonPropertyName("min")] public KuroPatchTagGroup? Min { get; set; }
}

/// <summary>单个标签组的规则。</summary>
public sealed class KuroPatchTagGroup
{
    /// <summary>路径前缀列表;命中任一项的文件/分组不属于该标签组。</summary>
    [JsonPropertyName("exclude_prefixes")] public List<string>? ExcludePrefixes { get; set; }
}

/// <summary>
/// 一次「读取预下载信息」的结果:目标版本、可选的规格与各自体积。
/// <para>
/// 供 UI 展示「全量 / 精简」两张卡片用,不需要再各自发一次网络请求。
/// </para>
/// </summary>
public sealed class PredownloadInfo
{
    /// <summary>预载目标版本(取各包 <c>version</c>;不一致时取最高版本)。</summary>
    public string TargetVersion { get; init; } = "";

    /// <summary>资源等级组合名(HD/SD/UHD)。</summary>
    public string BundleName { get; init; } = "";

    /// <summary>该等级由哪些包组成(如 [common, hd])。</summary>
    public IReadOnlyList<string> PackNames { get; init; } = [];

    /// <summary>
    /// 各包对应的补丁清单定位。预估与实际下载共用这份计划,避免"两处各算一次"而漂移。
    /// </summary>
    public IReadOnlyList<PredownloadPackPlan> Packs { get; init; } = [];

    /// <summary>全量规格下载体积。</summary>
    public long FullBytes { get; init; }

    /// <summary>精简规格下载体积(服务端没给 <c>tagGroups.min</c> 时等于全量)。</summary>
    public long MinimalBytes { get; init; }

    /// <summary>全量规格所需磁盘空间(下载体积 + 最大单文件)。</summary>
    public long FullDiskBytes { get; init; }

    /// <summary>精简规格所需磁盘空间。</summary>
    public long MinimalDiskBytes { get; init; }

    /// <summary>服务端是否真的提供了「精简」区分(否则两张卡片体积相同,UI 应只给一档)。</summary>
    public bool SupportsMinimal => MinimalBytes < FullBytes;

    /// <summary>取某规格的下载体积。</summary>
    public long BytesFor(PredownloadSpec spec) => spec == PredownloadSpec.Minimal ? MinimalBytes : FullBytes;

    /// <summary>取某规格的所需磁盘空间。</summary>
    public long DiskBytesFor(PredownloadSpec spec) => spec == PredownloadSpec.Minimal ? MinimalDiskBytes : FullDiskBytes;
}

/// <summary>
/// 单个预载资源包「从本地版本升到目标版本」的补丁清单定位信息。
/// </summary>
public sealed class PredownloadPackPlan
{
    /// <summary>包名(common/hd/sd/uhd)。</summary>
    public required string PackName { get; init; }

    /// <summary>补丁清单(indexFile.json)的完整地址。</summary>
    public required string PatchIndexUrl { get; init; }

    /// <summary>补丁资源下载前缀(清单里条目相对它拼 URL)。</summary>
    public string? BaseUrl { get; init; }

    /// <summary>该补丁清单自身的 MD5(用于落地前校验)。</summary>
    public string? IndexFileMd5 { get; init; }
}

/// <summary>
/// 「补齐精简版缺失的可选资源」的计划。
/// <para>
/// 精简预下载/精简更新会跳过 <c>tagGroups.min.exclude_prefixes</c> 命中的分组
/// (官方命名为 <c>Option_Pack_*</c> 的场景/剧情资源)。这些内容官方设计是进游戏后按
/// 账号进度自动补,但如果用户想一次性备齐(避免游玩中卡加载),可以定向下载。
/// </para>
/// <para>
/// 实测 3.8.0 国服 HD:26 个可选包,common 2.91 GiB + hd 1.88 GiB。
/// </para>
/// </summary>
public sealed class ResourceTopUpPlan
{
    /// <summary>需要补的包(通常 common + 等级包)。</summary>
    public required IReadOnlyList<PredownloadPackPlan> Packs { get; init; }

    /// <summary>要下载的差分包数量。</summary>
    public int FileCount { get; init; }

    /// <summary>下载总字节。</summary>
    public long DownloadBytes { get; init; }

    /// <summary>
    /// 补齐后应落在游戏目录的目标文件(相对路径)。
    /// <para>
    /// <b>检测"缺不缺"必须用这份清单,而不是差分包路径</b>:差分包(<c>.krpdiff</c>)本来就不在
    /// 游戏目录里,拿它判断会永远得出"缺失";真正的判据是应用差分后应存在的文件。
    /// </para>
    /// </summary>
    public IReadOnlyList<string> TargetPaths { get; init; } = [];
}

/// <summary>
/// 正式版本的「差分安装计划」:各包从本地版本升到目标版本的补丁清单定位。
/// <para>
/// <b>与预下载共用同一套定位结构</b>(<see cref="PredownloadPackPlan"/>),因为两者的数据源相同:
/// 新版协议 <c>resourcePacks[].patchConfig[]</c> 里每一档就是「从某个版本升到本包版本」的差分清单。
/// 差别只在触发时机 —— 预下载是把差分包提前下到暂存区,正式更新是当场下载并应用。
/// </para>
/// </summary>
public sealed class UpdatePatchPlan
{
    /// <summary>目标版本。</summary>
    public required string TargetVersion { get; init; }

    /// <summary>来源版本(本地已装版本)。</summary>
    public required string SourceVersion { get; init; }

    /// <summary>各包的差分清单。</summary>
    public required IReadOnlyList<PredownloadPackPlan> Packs { get; init; }

    /// <summary>差分包下载总字节(估算值,来自各包 patchConfig.size)。</summary>
    public long DownloadBytes { get; init; }

    /// <summary>
    /// 下载规格。<b>下载与安装必须共用它</b>:体积预估、实际下载、分组过滤三处各算一遍
    /// 就会漂移成「按精简算、按全量下」。
    /// </summary>
    public PredownloadSpec Spec { get; init; } = PredownloadSpec.Full;

    /// <summary>全量规格的下载字节(供界面展示两档对比)。</summary>
    public long FullBytes { get; init; }

    /// <summary>精简规格的下载字节;服务端没给排除规则时等于全量。</summary>
    public long MinimalBytes { get; init; }

    /// <summary>服务端是否真的提供了「精简」区分。</summary>
    public bool SupportsMinimal => MinimalBytes < FullBytes;
}

/// <summary>
/// 一次分级预下载的完整计划:要下哪些包、各自用哪份补丁清单、多少字节。
/// <para>
/// <b>预估与实际必须共用同一份计划</b>:UI 显示的「全量 12.08 GB / 精简 7.30 GB」与实际
/// 下载的字节数由同一套补丁清单算出。分成两处各算一次,迟早会漂移成
/// 「说好 7.30 GB,实际下了 12 GB」这种无法向用户解释的偏差。
/// </para>
/// </summary>
public sealed class PredownloadPlan
{
    /// <summary>预载目标版本。</summary>
    public required string TargetVersion { get; init; }

    /// <summary>资源等级组合名(HD/SD/UHD)。</summary>
    public required string BundleName { get; init; }

    /// <summary>各包对应的补丁清单(通常 common + 等级包共 2 项)。</summary>
    public required IReadOnlyList<PredownloadPackPlan> Packs { get; init; }
}
