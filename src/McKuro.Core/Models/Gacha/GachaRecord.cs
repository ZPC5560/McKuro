using System.Text.Json.Serialization;

namespace McKuro.Core.Models.Gacha;

/// <summary>
/// 单次抽卡记录(与 gmserver-api 返回字段一致)。
/// </summary>
public sealed class GachaRecord
{
    [JsonPropertyName("cardPoolType")] public int CardPoolType { get; set; }

    [JsonPropertyName("resourceId")] public int ResourceId { get; set; }

    [JsonPropertyName("qualityLevel")] public int QualityLevel { get; set; }

    [JsonPropertyName("resourceType")] public string ResourceType { get; set; } = "";

    [JsonPropertyName("name")] public string Name { get; set; } = "";

    [JsonPropertyName("count")] public int Count { get; set; }

    [JsonPropertyName("time")] public string Time { get; set; } = "";

    /// <summary>归属玩家 ID(本地存储时写入)。</summary>
    [JsonIgnore] public string PlayerId { get; set; } = "";

    public bool IsFiveStar => QualityLevel >= 5;

    public bool IsRole => string.Equals(ResourceType, "角色", StringComparison.OrdinalIgnoreCase)
        || string.Equals(ResourceType, "role", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// 查询抽卡记录所需的请求参数(从抽卡链接 URL 解析而来)。
/// </summary>
public sealed class GachaRecordRequest
{
    public string PlayerId { get; set; } = "";
    public string RecordId { get; set; } = "";
    public string CardPoolId { get; set; } = "";
    public string ServerId { get; set; } = "";
    public string Language { get; set; } = "zh-Hans";

    /// <summary>原始链接(调试用)。</summary>
    public string RawUrl { get; set; } = "";

    /// <summary>
    /// 依据玩家 ID 判断国服/国际服:国服玩家 ID 以 "1" 开头。
    /// </summary>
    public bool IsChinaServer => PlayerId.StartsWith('1');

    public bool IsValid =>
        !string.IsNullOrWhiteSpace(PlayerId)
        && !string.IsNullOrWhiteSpace(RecordId)
        && !string.IsNullOrWhiteSpace(CardPoolId)
        && !string.IsNullOrWhiteSpace(ServerId);
}
