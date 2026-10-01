using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace McKuro.Core.Models.Gacha;

/// <summary>mc.appfeng.com 图鉴条目(avatar.json / weapon.json 的公共字段)。</summary>
public sealed class CommunityIconItem
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }

    /// <summary>图标文件名(不含扩展名),如 <c>T_IconRoleHead256_75_UI</c>。</summary>
    [JsonPropertyName("icon")] public string? Icon { get; set; }

    [JsonPropertyName("star")] public int Star { get; set; }
}

/// <summary>
/// 图鉴目录磁盘缓存(数据目录 icon_catalog.json)。
/// <para>存"游戏 ID → 图标文件名"的解析结果,而非原始 JSON,便于直接注入 <see cref="IconCatalog"/>。</para>
/// </summary>
public sealed class IconCatalogCache
{
    /// <summary>缓存格式版本;不匹配时丢弃重建(避免旧格式字段缺失导致误解析)。</summary>
    [JsonPropertyName("version")] public int Version { get; set; }

    /// <summary>本次拉取时间(UTC,ISO 8601);用于 TTL 判定。</summary>
    [JsonPropertyName("fetchedAt")] public DateTimeOffset FetchedAt { get; set; }

    [JsonPropertyName("roles")] public Dictionary<int, string> Roles { get; set; } = [];

    [JsonPropertyName("weapons")] public Dictionary<int, string> Weapons { get; set; } = [];
}

[JsonSerializable(typeof(List<CommunityIconItem>))]
[JsonSerializable(typeof(IconCatalogCache))]
public sealed partial class IconCatalogJsonContext : JsonSerializerContext;
