using System.Text.Json.Serialization;

namespace McKuro.Core.Models.Redeem;

/// <summary>兑换码条目(参照 WutheringWavesTool RedemptionCodeItem)。</summary>
public sealed class RedemptionCodeItem
{
    [JsonPropertyName("key")] public string? Key { get; set; }
    [JsonPropertyName("startTime")] public string? StartTime { get; set; }
    [JsonPropertyName("endTime")] public string? EndTime { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("reward")] public string? Reward { get; set; }
    [JsonPropertyName("contributors")] public string? Contributors { get; set; }
    [JsonPropertyName("valid")] public bool Valid { get; set; }
    [JsonPropertyName("gameName")] public string? GameName { get; set; }
}

/// <summary>兑换码列表接口响应(data 内层)。</summary>
public sealed class RedemptionCodeData
{
    /// <summary>国服(mc1001)。</summary>
    [JsonPropertyName("mc1001")] public List<RedemptionCodeItem>? Mainland { get; set; }

    /// <summary>国际服(mc1002)。</summary>
    [JsonPropertyName("mc1002")] public List<RedemptionCodeItem>? Global { get; set; }
}

/// <summary>兑换码接口外层响应。</summary>
public sealed class RedemptionCodeEnvelope
{
    [JsonPropertyName("code")] public int Code { get; set; }
    [JsonPropertyName("msg")] public string? Msg { get; set; }
    [JsonPropertyName("data")] public RedemptionCodeData? Data { get; set; }
}

[JsonSerializable(typeof(RedemptionCodeEnvelope))]
[JsonSerializable(typeof(RedemptionCodeData))]
[JsonSerializable(typeof(RedemptionCodeItem))]
[JsonSerializable(typeof(List<RedemptionCodeItem>))]
public sealed partial class RedeemJsonContext : JsonSerializerContext;
