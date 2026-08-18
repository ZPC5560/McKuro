using System.Text.Json.Serialization;

namespace McKuro.Core.Models.Game;

/// <summary>
/// 库洛官方启动器更新协议模型(与 index.json 结构一致,参考 WutheringWavesTool 的 com.kuro.game)。
/// </summary>

public sealed class KuroIndex
{
    [JsonPropertyName("default")] public KuroUpdateData? Default { get; set; }

    [JsonPropertyName("predownload")] public KuroUpdateData? Predownload { get; set; }

    [JsonPropertyName("keyFileCheckList")] public List<string>? KeyFileCheckList { get; set; }

    [JsonPropertyName("gameResourceList")] public KuroGameResourceList? GameResourceList { get; set; }
}

public sealed class KuroUpdateData
{
    [JsonPropertyName("cdnList")] public List<KuroCdnData>? CdnList { get; set; }

    /// <summary>资源清单(resource.json)的相对路径。</summary>
    [JsonPropertyName("resources")] public string? Resources { get; set; }

    /// <summary>文件下载地址前缀。</summary>
    [JsonPropertyName("resourcesBasePath")] public string? ResourcesBasePath { get; set; }

    [JsonPropertyName("version")] public string? Version { get; set; }

    [JsonPropertyName("config")] public KuroConfig? Config { get; set; }

    /// <summary>resource.json 的完整下载地址(取 ping 最小的 CDN)。</summary>
    [JsonIgnore]
    public string? ResourceJsonUrl =>
        CdnList is { Count: > 0 } && !string.IsNullOrEmpty(Resources)
            ? CdnList[0].Url + Resources
            : null;
}

public sealed class KuroCdnData
{
    [JsonPropertyName("url")] public string Url { get; set; } = "";

    [JsonPropertyName("ping")] public int Ping { get; set; }

    [JsonPropertyName("priority")] public int Priority { get; set; }
}

public sealed class KuroConfig
{
    [JsonPropertyName("downloadLimit")] public int? DownloadLimit { get; set; }
    [JsonPropertyName("disableUserDownload")] public bool DisableUserDownload { get; set; }

    /// <summary>当前版本补丁的下载体积(参考 Haiyu Config.Size)。</summary>
    [JsonPropertyName("size")] public long? Size { get; set; }

    /// <summary>解压后体积(参考 Haiyu Config.UnCompressSize)。</summary>
    [JsonPropertyName("unCompressSize")] public long? UnCompressSize { get; set; }

    /// <summary>历史补丁配置(最新一项为当前版本)。</summary>
    [JsonPropertyName("patchConfig")] public List<KuroPatchConfig>? PatchConfig { get; set; }
}

public sealed class KuroPatchConfig
{
    [JsonPropertyName("size")] public long? Size { get; set; }

    [JsonPropertyName("unCompressSize")] public long? UnCompressSize { get; set; }

    [JsonPropertyName("version")] public string? Version { get; set; }

    [JsonPropertyName("ext")] public KuroPatchExt? Ext { get; set; }
}

public sealed class KuroPatchExt
{
    /// <summary>安装该补丁所需额外磁盘空间(官方启动器"所需磁盘空间"显示值)。</summary>
    [JsonPropertyName("requiredDiskSpace")] public long? RequiredDiskSpace { get; set; }

    [JsonPropertyName("maxFileSize")] public long? MaxFileSize { get; set; }
}

public sealed class KuroGameResourceList
{
    [JsonPropertyName("resource")] public List<KuroFileInfo>? Resource { get; set; }
}

public sealed class KuroFileInfo
{
    /// <summary>文件的下载地址后缀,以及保存的相对路径。</summary>
    [JsonPropertyName("dest")] public string? Dest { get; set; }

    [JsonPropertyName("md5")] public string? Md5 { get; set; }

    [JsonPropertyName("size")] public long? Size { get; set; }

    [JsonPropertyName("chunkInfos")] public List<KuroChunkInfo>? ChunkInfos { get; set; }
}

public sealed class KuroChunkInfo
{
    [JsonPropertyName("md5")] public string? Md5 { get; set; }
    [JsonPropertyName("size")] public long? Size { get; set; }
    [JsonPropertyName("offset")] public long? Offset { get; set; }
}

/// <summary>各地区启动器 index.json 地址。</summary>
public static class KuroEndpoints
{
    // ---- 鸣潮 ----
    public const string CnIndex =
        "https://prod-cn-alicdn-gamestarter.kurogame.com/launcher/game/G152/10003_Y8xXrXk65DqFHEDgApn3cpK5lfczpFx5/index.json";

    public const string BilibiliIndex =
        "https://prod-cn-alicdn-gamestarter.kurogame.com/launcher/game/G152/10004_j5GWFuUFlb8N31Wi2uS3ZAVHcb7ZGN7y/index.json";

    public const string GlobalIndex =
        "https://prod-alicdn-gamestarter.kurogame.com/launcher/game/G153/50004_obOHXFrFanqsaIEOmuKroCcbZkQRBC7c/index.json";

    // ---- 战双 ----
    public const string PunishCnIndex =
        "https://prod-cn-alicdn-gamestarter.kurogame.com/launcher/game/G148/10012_RnIUKs3r59Csliu3N0rl5uRWWBOFDaJL/index.json";

    public const string PunishBilibiliIndex =
        "https://prod-cn-alicdn-gamestarter.kurogame.com/launcher/game/G148/10011_qYQv6TyyyhCKD3ox3gssyolNPwMoCPZt/index.json";

    public const string PunishGlobalIndex =
        "https://prod-alicdn-gamestarter.kurogame.com/launcher/game/G143/50015_LWdk9D2Ep9mpJmqBZZkcPBU2YNraEWBQ/index.json";

    public static string ForServerType(McKuro.Core.Services.Game.GameServerType type) => type switch
    {
        McKuro.Core.Services.Game.GameServerType.Bilibili => BilibiliIndex,
        McKuro.Core.Services.Game.GameServerType.Global => GlobalIndex,
        _ => CnIndex,
    };

    /// <summary>战双各渠道 index.json 地址。</summary>
    public static string ForPunishServerType(McKuro.Core.Services.Game.GameServerType type) => type switch
    {
        McKuro.Core.Services.Game.GameServerType.Bilibili => PunishBilibiliIndex,
        McKuro.Core.Services.Game.GameServerType.Global => PunishGlobalIndex,
        _ => PunishCnIndex,
    };

    /// <summary>按游戏类型获取 index.json 地址。</summary>
    public static string ForGame(McKuro.Core.Services.Game.KuroGame game, McKuro.Core.Services.Game.GameServerType type) =>
        game == McKuro.Core.Services.Game.KuroGame.Punish ? ForPunishServerType(type) : ForServerType(type);
}
