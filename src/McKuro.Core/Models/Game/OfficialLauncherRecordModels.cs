using System.Text.Json.Serialization;

namespace McKuro.Core.Models.Game;

/// <summary>
/// 官方启动器(KRLauncher)的游戏目录安装配置:<c>&lt;游戏目录&gt;/launcherDownloadConfig.json</c>。
/// <para>
/// <b>为什么需要这个文件:</b>官方启动器判定「某个目录已装过游戏」读的是这份顶层记录,而不是
/// 各分包标记(<c>launcherDownloadConfig/{pack}.json</c>)。只有分包标记时,用户用官方启动器
/// 「定位游戏目录」会被判为未安装,进而触发上百 GB 的整包重下 —— 文件明明都在。
/// </para>
/// <para>
/// 字段名与官方 <c>KRInstalledConfig</c>/<c>BundleInstalledInfo</c> 的 JsonPropertyName 严格对齐,
/// 不要按自己的喜好改名。
/// </para>
/// </summary>
public sealed class OfficialInstalledConfig
{
    /// <summary>官方 appId(= 各渠道启动器的 resId:官服 10003 / B站 10004 / 国际服 50004)。</summary>
    [JsonPropertyName("appId")] public string AppId { get; set; } = "";

    /// <summary>顶层版本:仅旧协议(无 bundles)使用;新协议下只是 default-bundle 的兼容视图。</summary>
    [JsonPropertyName("version")] public string Version { get; set; } = "";

    /// <summary>顶层状态:旧协议的瞬态标记;新协议下写空串(官方写的是 moving/repairing 这类过程态)。</summary>
    [JsonPropertyName("state")] public string State { get; set; } = "";

    /// <summary>键 = 分包组合名(服务器原样拼写:HD/SD/UHD)。同一目录可并存多个组合。</summary>
    [JsonPropertyName("bundles")] public Dictionary<string, OfficialBundleInstalledInfo> Bundles { get; set; } = [];
}

/// <summary>官方 <c>BundleInstalledInfo</c> 对应结构。</summary>
public sealed class OfficialBundleInstalledInfo
{
    /// <summary>该组合已安装的版本(= 主分包版本)。</summary>
    [JsonPropertyName("version")] public string Version { get; set; } = "";

    /// <summary>官方瞬态标记(写入时为空串)。</summary>
    [JsonPropertyName("state")] public string State { get; set; } = "";

    /// <summary>
    /// 已安装的分包名列表。必须列全该组合在服务器端的**全部分包**(服务器原样拼写),
    /// 缺项会让官方启动器认为该组合不完整、照样重下。
    /// </summary>
    [JsonPropertyName("resourcePacks")] public List<string> ResourcePacks { get; set; } = [];
}

/// <summary>
/// 官方分包版本文件:<c>launcherDownloadConfig/{pack}.json</c>(官方 <c>PackVersionConfig</c>)。
/// <para>
/// 这个版本号是官方后续<b>增量更新</b>的基线,必须与实际已安装的文件版本一致 ——
/// 写成服务器最新版本而文件其实是旧的,用户切到官方启动器后会被判定"已是最新",更新永远不触发。
/// </para>
/// </summary>
public sealed class OfficialPackVersionConfig
{
    [JsonPropertyName("packName")] public string PackName { get; set; } = "";

    [JsonPropertyName("version")] public string Version { get; set; } = "";
}

/// <summary>
/// 官方本地资源清单:<c>&lt;游戏目录&gt;/LocalGameResources.json</c>(官方 <c>KRGameResourceRecord</c>)。
/// <para>官方对它所有读取点都是 best-effort 簿记:缺失不触发下载,但存在时能省掉官方的全量校验。</para>
/// </summary>
public sealed class OfficialGameResourceRecord
{
    [JsonPropertyName("resource")] public List<OfficialResourceFileInfo> Resource { get; set; } = [];
}

/// <summary>官方 <c>KRFileInfo</c> 对应结构。<b>路径字段名是 dest,不是 path。</b></summary>
public sealed class OfficialResourceFileInfo
{
    /// <summary>相对游戏目录的路径(无前导 /,分隔符用 /)。</summary>
    [JsonPropertyName("dest")] public string Dest { get; set; } = "";

    [JsonPropertyName("size")] public long Size { get; set; }

    [JsonPropertyName("md5")] public string Md5 { get; set; } = "";

    [JsonPropertyName("fromFolder")] public string? FromFolder { get; set; }

    /// <summary>分块校验信息;无分块时为 null(官方只有大文件才带)。</summary>
    [JsonPropertyName("chunkInfos")] public List<OfficialResourceChunkInfo>? ChunkInfos { get; set; }
}

/// <summary>官方分块校验信息。</summary>
public sealed class OfficialResourceChunkInfo
{
    [JsonPropertyName("start")] public long Start { get; set; }

    [JsonPropertyName("end")] public long End { get; set; }

    [JsonPropertyName("md5")] public string Md5 { get; set; } = "";
}

/// <summary>
/// 待写入的官方安装记录:按画质组合(bundle)分组,每组含 appId、组合名与逐分包版本。
/// </summary>
public sealed record OfficialInstallRecord(
    string AppId,
    string BundleName,
    IReadOnlyList<OfficialPackVersion> Packs)
{
    /// <summary>旧协议渠道(没有 resourcePacks/bundles):官方格式是顶层 version/state,不写 bundles。</summary>
    public bool IsLegacy { get; init; }

    /// <summary>同目录已安装的其他画质组合(不含 <see cref="BundleName"/>),写入时一并登记 bundle 节点。</summary>
    public IReadOnlyList<OfficialBundleRecord> AdditionalBundles { get; init; } = [];
}

/// <summary>单个附加画质组合的安装记录。</summary>
public sealed record OfficialBundleRecord(
    string BundleName,
    IReadOnlyList<OfficialPackVersion> Packs);

/// <summary>单个分包的目标版本。</summary>
public sealed record OfficialPackVersion(string Name, string Version);

/// <summary>「定位游戏目录」的结果状态。</summary>
public enum OfficialRecordLocateState
{
    /// <summary>目录可用,可以写入。</summary>
    Ready,

    /// <summary>未设置游戏目录,或目录不存在。</summary>
    GameFolderMissing,

    /// <summary>目录存在但是空的(很可能指错了地方)。</summary>
    GameFolderEmpty,

    /// <summary>目录里没有游戏主程序 —— 官方判定也依赖它,写入无意义。</summary>
    GameExeMissing,
}

/// <summary>「定位游戏目录」结果。</summary>
public sealed class OfficialRecordLocateResult
{
    public OfficialRecordLocateState State { get; init; }

    /// <summary>命中的游戏目录(仅 <see cref="OfficialRecordLocateState.Ready"/> 时有值)。</summary>
    public string? GameFolder { get; init; }

    /// <summary>该目录里是否已经存在官方顶层记录(用于提示"会覆盖/合并已有记录")。</summary>
    public bool HasExistingRecord { get; init; }

    /// <summary>已有记录里登记的画质组合名。</summary>
    public IReadOnlyList<string> ExistingBundles { get; init; } = [];

    /// <summary>失败时的可读原因(成功为 null)。</summary>
    public string? Message { get; init; }
}

/// <summary>写入/移除结果。写入是"全部成功或全部不生效",见服务的两阶段提交说明。</summary>
public sealed class OfficialRecordWriteResult
{
    public IReadOnlyList<string> WrittenFiles { get; init; } = [];

    public IReadOnlyList<string> FailedFiles { get; init; } = [];

    public IReadOnlyList<string> RemovedEntries { get; init; } = [];

    /// <summary>是否有失败项(任一失败即整体未生效)。</summary>
    public bool HasFailed => FailedFiles.Count > 0;

    public int WrittenCount => WrittenFiles.Count;

    public int RemovedCount => RemovedEntries.Count;
}

/// <summary>官方安装记录的 JSON 源生成上下文(AOT 安全;不要改成反射序列化)。</summary>
[JsonSerializable(typeof(OfficialInstalledConfig))]
[JsonSerializable(typeof(OfficialPackVersionConfig))]
[JsonSerializable(typeof(OfficialGameResourceRecord))]
public sealed partial class OfficialLauncherJsonContext : JsonSerializerContext;
