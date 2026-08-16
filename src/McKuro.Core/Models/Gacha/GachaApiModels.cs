using System.Text.Json.Serialization;

namespace McKuro.Core.Models.Gacha;

/// <summary>gmserver-api 查询请求体。</summary>
public sealed class GachaQueryRequest
{
    [JsonPropertyName("playerId")] public string PlayerId { get; set; } = "";
    [JsonPropertyName("recordId")] public string RecordId { get; set; } = "";
    [JsonPropertyName("cardPoolId")] public string CardPoolId { get; set; } = "";
    [JsonPropertyName("cardPoolType")] public int CardPoolType { get; set; }
    [JsonPropertyName("serverId")] public string ServerId { get; set; } = "";
    [JsonPropertyName("languageCode")] public string LanguageCode { get; set; } = "zh-Hans";
}

/// <summary>gmserver-api 响应体。</summary>
public sealed class GachaQueryResponse
{
    [JsonPropertyName("code")] public int Code { get; set; }
    [JsonPropertyName("msg")] public string Msg { get; set; } = "";
    [JsonPropertyName("data")] public List<GachaRecord>? Data { get; set; }
}
