using System.Text.Json.Serialization;

namespace McKuro.Core.Models.Game;

/// <summary>游戏清单(通用格式,供更新引擎使用)。</summary>
public sealed class GameManifest
{
    /// <summary>清单版本号(游戏资源版本,如 "2.4.0")。</summary>
    [JsonPropertyName("version")] public string Version { get; set; } = "";

    /// <summary>文件列表。</summary>
    [JsonPropertyName("files")] public List<GameFileEntry> Files { get; set; } = [];

    /// <summary>需要校验的关键文件(相对路径),缺失即视为未安装。</summary>
    [JsonPropertyName("keyFiles")] public List<string> KeyFiles { get; set; } = [];

    /// <summary>发布日期(可选)。</summary>
    [JsonPropertyName("releaseDate")] public string? ReleaseDate { get; set; }

    /// <summary>官方 patch index 的安装计划;普通清单为空。</summary>
    [JsonIgnore]
    public GamePatchPlan? PatchPlan { get; set; }
}

/// <summary>清单中的单个文件。</summary>
public sealed class GameFileEntry
{
    /// <summary>相对游戏根目录的保存路径(如 "Client/Binaries/Win64/xxx.exe")。</summary>
    [JsonPropertyName("path")] public string Path { get; set; } = "";

    /// <summary>文件大小(字节)。</summary>
    [JsonPropertyName("size")] public long Size { get; set; }

    /// <summary>MD5(小写十六进制)。</summary>
    [JsonPropertyName("md5")] public string Md5 { get; set; } = "";

    /// <summary>下载 URL;为空时由引擎根据 baseUrl 拼接 path。</summary>
    [JsonPropertyName("url")] public string? Url { get; set; }

    /// <summary>Haiyu/Kuro 分片校验信息;存在时按 HTTP Range 逐片续传。</summary>
    [JsonIgnore]
    public List<GameChunkInfo> ChunkInfos { get; set; } = [];

    /// <summary>
    /// 该条目是否带有<b>文件级</b>完整性依据(文件大小或 MD5)。
    /// <para>
    /// 两者皆空时,任何「本地文件已完好、可以跳过下载」的判断都是<b>不可验证的乐观假设</b>
    /// —— 任意内容都会被判为成功。真实库洛清单里每个条目都带 size+md5,
    /// 这是防御性判据:宁可多下一次,也不谎报「已完好」。
    /// </para>
    /// <para>
    /// <b>下载与校验两侧必须共用本判据</b>(而非各写一遍),否则同一条清单行会出现相反结论:
    /// 校验侧说「完好、无需下载」,下载侧却拒绝跳过 —— 用户看到的是「0 个文件待下载却始终不动」。
    /// </para>
    /// <para>
    /// <b>刻意不含 <see cref="ChunkInfos"/></b>:两侧的文件级判定(长度比较 / 整文件 MD5)
    /// 都用不到分片哈希。若把分片算作依据,「只有分片、没有 size/md5」的行就会被允许跳过,
    /// 而跳过分支并不校验分片 —— 等于未经验证就声称完好,比多下一次更糟。
    /// </para>
    /// </summary>
    [JsonIgnore]
    public bool HasIntegrityData => Size > 0 || !string.IsNullOrWhiteSpace(Md5);
}

/// <summary>官方 patch index 的分阶段安装计划。</summary>
public sealed class GamePatchPlan
{
    public List<GamePatchPackage> DiffPackages { get; } = [];
    public List<GamePatchGroup> DiffGroups { get; } = [];
    public List<GamePatchPackage> ZipPackages { get; } = [];
    public List<string> DeleteFiles { get; } = [];
    public string BaseUrl { get; init; } = "";
    public string? IndexFileMd5 { get; init; }
}

public sealed class GamePatchPackage
{
    public required GameFileEntry Package { get; init; }
    public List<GamePatchEntry> Entries { get; } = [];
}

public sealed class GamePatchGroup
{
    public required GameFileEntry Package { get; init; }
    public List<GamePatchEntry> SourceFiles { get; } = [];
    public List<GamePatchEntry> DestinationFiles { get; } = [];
}

public sealed class GamePatchEntry
{
    public required string Path { get; init; }
    public long Size { get; init; }
    public string Md5 { get; init; } = "";
    public List<GameChunkInfo> ChunkInfos { get; } = [];
}

/// <summary>单个资源分片的范围与 MD5。</summary>
public sealed class GameChunkInfo
{
    public long Start { get; init; }
    public long End { get; init; }
    public string Md5 { get; init; } = "";

    public long Length => End >= Start ? End - Start + 1 : 0;
}
