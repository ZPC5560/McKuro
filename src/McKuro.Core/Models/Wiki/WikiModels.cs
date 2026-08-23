using System.Text.Json;
using System.Text.Json.Serialization;

namespace McKuro.Core.Models.Wiki;

/// <summary>图鉴类型。</summary>
public enum WikiType
{
    /// <summary>战双官服。</summary>
    Punish = 2,
    /// <summary>鸣潮官服。</summary>
    Waves = 9,
}

/// <summary>图鉴首页响应。</summary>
public sealed class WikiHomeModel
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("msg")]
    public string? Msg { get; set; }

    [JsonPropertyName("data")]
    public WikiData? Data { get; set; }
}

public sealed class WikiData
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("contentJson")]
    public WikiContentJson? ContentJson { get; set; }
}

public sealed class WikiContentJson
{
    [JsonPropertyName("background")]
    public WikiBackground? Background { get; set; }

    [JsonPropertyName("mainModules")]
    public List<WikiMainModule>? MainModules { get; set; }

    [JsonPropertyName("shortcuts")]
    public WikiShortcuts? Shortcuts { get; set; }

    [JsonPropertyName("banner")]
    public List<WikiBanner>? Banner { get; set; }

    [JsonPropertyName("sideModules")]
    public List<WikiSideModule>? SideModules { get; set; }

    [JsonPropertyName("announcement")]
    public List<WikiAnnouncement>? Announcement { get; set; }
}

public sealed class WikiBackground
{
    [JsonPropertyName("x")]
    public string? X { get; set; }

    [JsonPropertyName("y")]
    public string? Y { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }
}

public sealed class WikiBanner
{
    [JsonPropertyName("linkConfig")]
    public WikiLinkConfig? LinkConfig { get; set; }

    [JsonPropertyName("dateRange")]
    public List<string>? DateRange { get; set; }

    [JsonPropertyName("active")]
    public bool Active { get; set; }

    [JsonPropertyName("describe")]
    public string? Describe { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }
}

public sealed class WikiLinkConfig
{
    [JsonPropertyName("linkUrl")]
    public string? LinkUrl { get; set; }

    [JsonPropertyName("linkType")]
    public int LinkType { get; set; }

    [JsonPropertyName("catalogueId")]
    public object? CatalogueId { get; set; }

    [JsonPropertyName("entryId")]
    public string? EntryId { get; set; }
}

public sealed class WikiMainModule
{
    [JsonPropertyName("iconUrl")]
    public string? IconUrl { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("content")]
    public JsonElement Content { get; set; }
}

public sealed class WikiShortcuts
{
    [JsonPropertyName("iconUrl")]
    public string? IconUrl { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("content")]
    public List<WikiShortcutItem>? Content { get; set; }
}

public sealed class WikiShortcutItem
{
    [JsonPropertyName("contentUrl")]
    public string? ContentUrl { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("mobileImgUrl")]
    public string? MobileImgUrl { get; set; }
}

public sealed class WikiAnnouncement
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("active")]
    public bool Active { get; set; }

    [JsonPropertyName("content")]
    public string? Content { get; set; }
}

/// <summary>侧栏模块(type: hot-content-side / events-side 等)。</summary>
public sealed class WikiSideModule
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("iconUrl")]
    public string? IconUrl { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("content")]
    public JsonElement? Content { get; set; }
}

/// <summary>热点内容。</summary>
public sealed class HotContentSide
{
    [JsonPropertyName("linkConfig")]
    public WikiSideLinkConfig? LinkConfig { get; set; }

    [JsonPropertyName("contentUrl")]
    public string? ContentUrl { get; set; }

    [JsonPropertyName("contentUrlRealName")]
    public string? ContentUrlRealName { get; set; }

    [JsonPropertyName("active")]
    public bool Active { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>活动时间区间(countDown.dateRange)。</summary>
    [JsonPropertyName("countDown")]
    public HotCountDown? CountDown { get; set; }
}

/// <summary>热点倒计时(countDown)。</summary>
public sealed class HotCountDown
{
    [JsonPropertyName("dateRange")]
    public List<string>? DateRange { get; set; }
}

public sealed class WikiSideLinkConfig
{
    [JsonPropertyName("linkUrl")]
    public string? LinkUrl { get; set; }

    [JsonPropertyName("linkType")]
    public int LinkType { get; set; }

    [JsonPropertyName("entryId")]
    public string? EntryId { get; set; }
}

/// <summary>活动内容。</summary>
public sealed class EventContentSide
{
    [JsonPropertyName("visible")]
    public bool Visible { get; set; }

    [JsonPropertyName("tabs")]
    public List<EventSideTab>? Tabs { get; set; }
}

public sealed class EventSideTab
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("active")]
    public bool Active { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("imgs")]
    public List<EventSideImage>? Images { get; set; }

    /// <summary>活动时间区间(countDown.dateRange)。</summary>
    [JsonPropertyName("countDown")]
    public HotCountDown? CountDown { get; set; }
}

public sealed class EventSideImage
{
    [JsonPropertyName("img")]
    public string? Image { get; set; }
}

/// <summary>库街区官方资讯条目(/forum/companyEvent/findEventList,免登录)。</summary>
public sealed class OfficialEventItem
{
    [JsonPropertyName("postId")]
    public string PostId { get; set; } = "";

    [JsonPropertyName("postTitle")]
    public string? PostTitle { get; set; }

    [JsonPropertyName("coverUrl")]
    public string? CoverUrl { get; set; }

    /// <summary>分类:1=活动 2=资讯 3=公告。</summary>
    [JsonPropertyName("eventType")]
    public int EventType { get; set; }

    /// <summary>发布时间(Unix 毫秒时间戳)。</summary>
    [JsonPropertyName("shelveTime")]
    public long ShelveTime { get; set; }
}

/// <summary>findEventList 的 data 节。</summary>
public sealed class OfficialEventData
{
    [JsonPropertyName("list")]
    public List<OfficialEventItem>? List { get; set; }
}

/// <summary>findEventList 的响应信封。</summary>
public sealed class OfficialEventEnvelope
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("msg")]
    public string? Msg { get; set; }

    [JsonPropertyName("data")]
    public OfficialEventData? Data { get; set; }
}

[JsonSerializable(typeof(WikiHomeModel))]
[JsonSerializable(typeof(WikiData))]
[JsonSerializable(typeof(WikiContentJson))]
[JsonSerializable(typeof(WikiBanner))]
[JsonSerializable(typeof(List<WikiBanner>))]
[JsonSerializable(typeof(WikiLinkConfig))]
[JsonSerializable(typeof(WikiMainModule))]
[JsonSerializable(typeof(List<WikiMainModule>))]
[JsonSerializable(typeof(WikiShortcuts))]
[JsonSerializable(typeof(WikiShortcutItem))]
[JsonSerializable(typeof(List<WikiShortcutItem>))]
[JsonSerializable(typeof(WikiAnnouncement))]
[JsonSerializable(typeof(List<WikiAnnouncement>))]
[JsonSerializable(typeof(WikiSideModule))]
[JsonSerializable(typeof(List<WikiSideModule>))]
[JsonSerializable(typeof(HotContentSide))]
[JsonSerializable(typeof(HotCountDown))]
[JsonSerializable(typeof(List<HotContentSide>))]
[JsonSerializable(typeof(EventContentSide))]
[JsonSerializable(typeof(EventSideTab))]
[JsonSerializable(typeof(List<EventSideTab>))]
[JsonSerializable(typeof(EventSideImage))]
[JsonSerializable(typeof(List<EventSideImage>))]
[JsonSerializable(typeof(WikiBackground))]
[JsonSerializable(typeof(WikiSideLinkConfig))]
[JsonSerializable(typeof(OfficialEventItem))]
[JsonSerializable(typeof(List<OfficialEventItem>))]
[JsonSerializable(typeof(OfficialEventData))]
[JsonSerializable(typeof(OfficialEventEnvelope))]
public sealed partial class WikiJsonContext : JsonSerializerContext;
