using System.Text.Json.Serialization;

namespace McKuro.Core.Models.Game;

/// <summary>启动器信息(与官方 launcher information 接口一致,含轮播图与公告)。</summary>
public sealed class LauncherInfo
{
    [JsonPropertyName("guidance")] public Guidance? Guidance { get; set; }

    [JsonPropertyName("slideshow")] public List<SlideshowItem>? Slideshow { get; set; }
}

/// <summary>公告/新闻/活动分组。</summary>
public sealed class Guidance
{
    [JsonPropertyName("desc")] public string Desc { get; set; } = "";

    [JsonPropertyName("activity")] public AnnouncementGroup? Activity { get; set; }

    [JsonPropertyName("notice")] public AnnouncementGroup? Notice { get; set; }

    [JsonPropertyName("news")] public AnnouncementGroup? News { get; set; }
}

/// <summary>公告分组(标题 + 条目列表)。</summary>
public sealed class AnnouncementGroup
{
    [JsonPropertyName("title")] public string Title { get; set; } = "";

    [JsonPropertyName("sort")] public int Sort { get; set; }

    [JsonPropertyName("functionSwitch")] public int FunctionSwitch { get; set; }

    [JsonPropertyName("contents")] public List<AnnouncementItem>? Contents { get; set; }
}

/// <summary>单条公告/新闻。</summary>
public sealed class AnnouncementItem
{
    [JsonPropertyName("content")] public string Content { get; set; } = "";

    [JsonPropertyName("jumpUrl")] public string JumpUrl { get; set; } = "";

    [JsonPropertyName("time")] public string Time { get; set; } = "";
}

/// <summary>封面轮播图。</summary>
public sealed class SlideshowItem
{
    [JsonPropertyName("url")] public string Url { get; set; } = "";

    [JsonPropertyName("jumpUrl")] public string JumpUrl { get; set; } = "";

    [JsonPropertyName("md5")] public string Md5 { get; set; } = "";

    [JsonPropertyName("carouselNotes")] public string CarouselNotes { get; set; } = "";
}

/// <summary>启动器背景数据(官方 background 接口,含宣传视频/首帧图/版本Logo)。</summary>
public sealed class LauncherBackgroundData
{
    [JsonPropertyName("functionSwitch")] public int FunctionSwitch { get; set; }

    /// <summary>背景文件 URL(视频 mp4 或图片)。</summary>
    [JsonPropertyName("backgroundFile")] public string BackgroundFile { get; set; } = "";

    /// <summary>背景文件类型(1=图片,2=视频)。</summary>
    [JsonPropertyName("backgroundFileType")] public int BackgroundFileType { get; set; }

    /// <summary>首帧占位图 URL(视频加载前的静态封面)。</summary>
    [JsonPropertyName("firstFrameImage")] public string FirstFrameImage { get; set; } = "";

    /// <summary>版本 Logo / 标语图 URL。</summary>
    [JsonPropertyName("slogan")] public string Slogan { get; set; } = "";
}

/// <summary>启动器 index.json 的 functionCode 节。</summary>
public sealed class LauncherFunctionCode
{
    [JsonPropertyName("background")] public string Background { get; set; } = "";
}

/// <summary>
/// 启动器 index.json 的 config 节(启动器 3.0 改版后新增,functionCode 被收进这一层)。
/// 旧版 index.json 的 functionCode 在顶层,新版在 <c>config.functionCode</c> —— 两种都要认。
/// </summary>
public sealed class LauncherIndexConfig
{
    [JsonPropertyName("functionCode")] public LauncherFunctionCode? FunctionCode { get; set; }
}

/// <summary>启动器 index.json(取 functionCode.background 编码)。</summary>
public sealed class LauncherIndex
{
    [JsonPropertyName("functionCode")] public LauncherFunctionCode? FunctionCode { get; set; }

    /// <summary>新版 index.json 的 config 节(旧版没有该字段)。</summary>
    [JsonPropertyName("config")] public LauncherIndexConfig? Config { get; set; }

    /// <summary>
    /// 背景 functionCode:新版优先取 <c>config.functionCode.background</c>,旧版取顶层 <c>functionCode.background</c>。
    /// 空串表示这份 index 没有可用的背景编码。
    /// </summary>
    public string ResolveBackgroundCode()
    {
        var code = Config?.FunctionCode?.Background;
        if (string.IsNullOrWhiteSpace(code))
        {
            code = FunctionCode?.Background;
        }
        return code ?? "";
    }
}
