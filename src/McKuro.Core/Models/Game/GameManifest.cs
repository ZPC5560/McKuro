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
