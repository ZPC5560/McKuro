using System.Text.Json.Serialization;

namespace donet.Core.Models.Game;

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
