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

    /// <summary>resource.json 的完整下载地址(按官方 P 优先级选择 CDN)。</summary>
    [JsonIgnore]
    public string? ResourceJsonUrl
    {
        get
        {
            var cdn = CdnList?
                .Where(c => !string.IsNullOrWhiteSpace(c.Url) && c.P != 0)
                .OrderBy(c => c.P)
                .FirstOrDefault()
                ?? CdnList?.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c.Url));
            return cdn is not null && !string.IsNullOrEmpty(Resources)
                ? cdn.Url.TrimEnd('/') + "/" + Resources.TrimStart('/')
                : null;
        }
    }
}

public sealed class KuroCdnData
{
    [JsonPropertyName("url")] public string Url { get; set; } = "";

    [JsonPropertyName("ping")] public int Ping { get; set; }

    [JsonPropertyName("priority")] public int Priority { get; set; }

    // 库洛启动器当前协议使用大写 P 作为 CDN 优先级。
    [JsonPropertyName("P")] public int P { get; set; }
}

public sealed class KuroConfig
{
    [JsonPropertyName("downloadLimit")] public int? DownloadLimit { get; set; }
    [JsonPropertyName("disableUserDownload")] public bool DisableUserDownload { get; set; }

    /// <summary>当前版本补丁的下载体积(参考 Haiyu Config.Size)。</summary>
    [JsonPropertyName("size")] public long? Size { get; set; }

    /// <summary>解压后体积(参考 Haiyu Config.UnCompressSize)。</summary>
    [JsonPropertyName("unCompressSize")] public long? UnCompressSize { get; set; }

    /// <summary>当前版本号(如 "3.7.0")。</summary>
    [JsonPropertyName("version")] public string? Version { get; set; }

    /// <summary>文件下载地址前缀(与 <see cref="KuroUpdateData.ResourcesBasePath"/> 同值)。</summary>
    [JsonPropertyName("baseUrl")] public string? BaseUrl { get; set; }

    /// <summary>
    /// <b>完整校验索引</b>(indexFile.json)的相对路径(参考 Haiyu 的 GetVerificationResourceAsync)。
    /// <para>
    /// 与 <see cref="KuroUpdateData.Resources"/>(resource.json)相比,该索引是<b>严格超集</b>:
    /// 实测鸣潮 CN 两者同为 915 条且 dest/md5/size 全等,但校验索引额外有 76 条带
    /// <c>chunkInfos</c>(分片哈希)—— 分片续传与快速校验都依赖它,故优先使用本索引。
    /// </para>
    /// </summary>
    [JsonPropertyName("indexFile")] public string? IndexFile { get; set; }

    /// <summary>完整校验索引的 MD5(用于校验下载到的 indexFile.json 完整性)。</summary>
    [JsonPropertyName("indexFileMd5")] public string? IndexFileMd5 { get; set; }

    /// <summary>历史补丁配置(最新一项为当前版本)。</summary>
    [JsonPropertyName("patchConfig")] public List<KuroPatchConfig>? PatchConfig { get; set; }
}

public sealed class KuroPatchConfig
{
    [JsonPropertyName("size")] public long? Size { get; set; }

    [JsonPropertyName("unCompressSize")] public long? UnCompressSize { get; set; }

    [JsonPropertyName("version")] public string? Version { get; set; }

    [JsonPropertyName("ext")] public KuroPatchExt? Ext { get; set; }

    /// <summary>补丁文件清单(indexFile.json)的相对路径(参考 Haiyu PatchConfig.IndexFile)。</summary>
    [JsonPropertyName("indexFile")] public string? IndexFile { get; set; }

    /// <summary>补丁资源下载前缀(参考 Haiyu PatchConfig.BaseUrl)。</summary>
    [JsonPropertyName("baseUrl")] public string? BaseUrl { get; set; }

    [JsonPropertyName("indexFileMd5")] public string? IndexFileMd5 { get; set; }
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

    // Haiyu indexFile 使用 start/end;部分旧清单使用 offset/size,两种格式都兼容。
    [JsonPropertyName("start")] public long? Start { get; set; }
    [JsonPropertyName("end")] public long? End { get; set; }
}

// ---- 官方补丁清单(indexFile.json,参考 Haiyu PatchIndexGameResource) ----

public sealed class KuroPatchIndex
{
    [JsonPropertyName("patchInfos")] public List<KuroPatchFile>? PatchInfos { get; set; }

    [JsonPropertyName("groupInfos")] public List<KuroPatchGroup>? GroupInfos { get; set; }

    [JsonPropertyName("zipInfos")] public List<KuroPatchZip>? ZipInfos { get; set; }

    [JsonPropertyName("deleteFiles")] public List<string>? DeleteFiles { get; set; }

    [JsonPropertyName("applyTypes")] public List<string>? ApplyTypes { get; set; }

    [JsonPropertyName("resource")] public List<KuroPatchEntry>? Resource { get; set; }
}

/// <summary>补丁差异文件(下载 krdiff 差分文件应用到游戏目录)。</summary>
public sealed class KuroPatchFile
{
    [JsonPropertyName("dest")] public string? Dest { get; set; }

    [JsonPropertyName("md5")] public string? Md5 { get; set; }

    [JsonPropertyName("size")] public long? Size { get; set; }

    [JsonPropertyName("chunkInfos")] public List<KuroChunkInfo>? ChunkInfos { get; set; }

    [JsonPropertyName("fromFolder")] public string? FromFolder { get; set; }

    [JsonPropertyName("entries")] public List<KuroPatchEntry>? Entries { get; set; }
}

/// <summary>补丁条目(与全量清单一致:dest + md5 + size)。</summary>
public sealed class KuroPatchEntry
{
    [JsonPropertyName("dest")] public string? Dest { get; set; }

    [JsonPropertyName("md5")] public string? Md5 { get; set; }

    [JsonPropertyName("size")] public long? Size { get; set; }

    [JsonPropertyName("chunkInfos")] public List<KuroChunkInfo>? ChunkInfos { get; set; }

    /// <summary>文件所在资源目录(如 .../zip/;官方补丁 resource 条目带,用于拼下载 URL)。</summary>
    [JsonPropertyName("fromFolder")] public string? FromFolder { get; set; }
}

/// <summary>分组文件(源文件组 → 目标文件;下载差分生成目标)。</summary>
public sealed class KuroPatchGroup
{
    [JsonPropertyName("dest")] public string? Dest { get; set; }

    [JsonPropertyName("srcFiles")] public List<KuroPatchEntry>? SrcFiles { get; set; }

    [JsonPropertyName("dstFiles")] public List<KuroPatchEntry>? DstFiles { get; set; }
}

/// <summary>zip 补丁包(下载 zip 后解压到游戏目录)。</summary>
public sealed class KuroPatchZip
{
    [JsonPropertyName("dest")] public string? Dest { get; set; }

    [JsonPropertyName("md5")] public string? Md5 { get; set; }

    [JsonPropertyName("size")] public long? Size { get; set; }

    [JsonPropertyName("chunkInfos")] public List<KuroChunkInfo>? ChunkInfos { get; set; }

    [JsonPropertyName("fromFolder")] public string? FromFolder { get; set; }

    [JsonPropertyName("entries")] public List<KuroPatchEntry>? Entries { get; set; }
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

    // ---- 新版协议(按资源等级拆分;含 resourcePacks/bundles) ----

    /// <summary>新版协议使用的 CN 官方 appKey(与旧协议的 appKey 不同)。</summary>
    public const string CnOfficialAppKey = "10003_oLNgHF1CESo51DGHN2odtp40e3oI1HfZ";

    /// <summary>
    /// CN 官方新版协议 index.json(含 resourcePacks/bundles,可据以切换资源等级)。
    /// </summary>
    public const string CnResourcePackIndex =
        "https://prod-cn-alicdn-gamestarter.kurogame.com/launcher/game/"
        + CnOfficialAppKey + "/G152/official/index.json";

    /// <summary>
    /// 哔哩哔哩渠道的新版协议 index.json。
    /// <para>与 CN 官方同主机、同 <c>G152</c>,只有 appKey 段与 <c>serverCode</c> 换成 <c>bilibili</c>。
    /// 实测(2026-10-27):返回 200,<c>bundles</c> = HD/SD/UHD、<c>resourcePacks</c> = uhd/sd/hd/common,
    /// 与国服完全同构;各包 indexFile 的 <c>dest</c> 前缀同为 <c>Client/Content/{SD,HD,UHD}/</c>。
    /// </para>
    /// </summary>
    public const string BilibiliResourcePackIndex =
        "https://prod-cn-alicdn-gamestarter.kurogame.com/launcher/game/"
        + "10004_IY6xmlAusbImBjWrpPj1OwNuirXEO44x/G152/bilibili/index.json";

    /// <summary>
    /// 国际服新版协议 index.json。
    /// <para>
    /// <b>注意主机名不同</b>:国际服用 <c>prod-alicdn-gamestarter.kurogame.com</c>(<b>没有</b> <c>-cn-</c>),
    /// 用到国服/B站的 <c>prod-cn-alicdn-gamestarter.kurogame.com</c> 会 404;反之把国服地址换成
    /// <c>prod-alicdn</c> 同样 404 —— 两者是各渠道各自的域,不是同一份文件的镜像。
    /// </para>
    /// <para>实测(2026-10-27):200,<c>bundles</c> = HD/SD/UHD、<c>resourcePacks</c> = uhd/sd/hd/common,
    /// 包体积与国服同量级(3.7.1:common 40.17 GB / sd 21.97 GB),落位目录同构。</para>
    /// </summary>
    public const string GlobalResourcePackIndex =
        "https://prod-alicdn-gamestarter.kurogame.com/launcher/game/"
        + "50004_P7xcUZnEr1AXIGON25E6KjpOgTlVrg6e/G153/official/index.json";

    /// <summary>
    /// 取某渠道的新版协议端点(不支持时返回 null,调用方应回退到「不可切换等级」的逻辑)。
    /// <para>
    /// <b>支持面(2026-10-27 逐主机实测,勿凭印象改):</b>
    /// 国服官方 / 哔哩哔哩 / 国际服三家都有该端点;WeGame <b>四个主机全部 404</b>,故仍不支持。
    /// </para>
    /// <para>
    /// <c>Unknown</c> 归到国服官方:未识别渠道时按最保守的假设走国服地址,失败由上层
    /// (加载清单失败 → 返回 null)兜住,不会把用户带进必失败的路径。
    /// </para>
    /// </summary>
    public static string? ResourcePackIndexForServerType(
        McKuro.Core.Services.Game.GameServerType type) => type switch
    {
        McKuro.Core.Services.Game.GameServerType.Bilibili => BilibiliResourcePackIndex,
        McKuro.Core.Services.Game.GameServerType.Global => GlobalResourcePackIndex,
        McKuro.Core.Services.Game.GameServerType.WeGame => null,
        _ => CnResourcePackIndex,
    };

    /// <summary>按游戏类型获取 index.json 地址。</summary>
    public static string ForGame(McKuro.Core.Services.Game.KuroGame game, McKuro.Core.Services.Game.GameServerType type) =>
        game == McKuro.Core.Services.Game.KuroGame.Punish ? ForPunishServerType(type) : ForServerType(type);
}
