using System.Text.Json.Serialization;

namespace McKuro.Core.Models.Game;

/// <summary>
/// 库洛新版启动器协议(<c>.../launcher/game/{appKey}/G152/official/index.json</c>)。
/// <para>
/// 相对旧协议(McKuro 原先使用的 <c>default.resources</c> + resource.json)的关键差异:
/// 新协议按**资源等级**把游戏资源拆成多个包(<c>resourcePacks</c>),并用 <c>bundles</c>
/// 描述每个等级由哪些包组成。旧协议的 resource.json 恰好只等于 HD 包体,因此无法用于
/// 切换 SD/UHD。
/// </para>
/// </summary>
public sealed class KuroResourcePackIndex
{
    [JsonPropertyName("cdnList")] public List<KuroCdnData>? CdnList { get; set; }

    /// <summary>可用资源包(键为包名:common/hd/sd/uhd)。</summary>
    [JsonPropertyName("resourcePacks")] public Dictionary<string, KuroResourcePack>? ResourcePacks { get; set; }

    /// <summary>资源等级组合(键为组合名:HD/SD/UHD)。</summary>
    [JsonPropertyName("bundles")] public Dictionary<string, KuroResourceBundle>? Bundles { get; set; }
}

/// <summary>单个资源包(下载其 indexFile 可得到包内文件清单)。</summary>
public sealed class KuroResourcePack
{
    [JsonPropertyName("version")] public string? Version { get; set; }

    /// <summary>包清单(indexFile.json)的相对路径。</summary>
    [JsonPropertyName("indexFile")] public string? IndexFile { get; set; }

    [JsonPropertyName("indexFileMd5")] public string? IndexFileMd5 { get; set; }

    /// <summary>包内文件的下载前缀(以 zip/ 结尾)。</summary>
    [JsonPropertyName("baseUrl")] public string? BaseUrl { get; set; }

    /// <summary>包体下载体积(字节)。</summary>
    [JsonPropertyName("size")] public long? Size { get; set; }

    [JsonPropertyName("unCompressSize")] public long? UnCompressSize { get; set; }

    [JsonPropertyName("patchType")] public string? PatchType { get; set; }

    /// <summary>包内文件数(来自 indexFile,由加载器填充)。</summary>
    [JsonIgnore]
    public int FileCount { get; set; }

    /// <summary>包内文件总量(来自 indexFile 求和,由加载器填充)。</summary>
    [JsonIgnore]
    public long TotalBytes { get; set; }
}

/// <summary>资源等级组合(如 HD = [common, hd])。</summary>
public sealed class KuroResourceBundle
{
    /// <summary>组成该等级的包名列表(首个通常是共享的 common)。</summary>
    [JsonPropertyName("resourcePacks")] public List<string>? ResourcePacks { get; set; }
}
