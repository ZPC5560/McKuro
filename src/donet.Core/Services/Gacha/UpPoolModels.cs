using System.Text.Json.Serialization;

namespace donet.Core.Services.Gacha;

/// <summary>聚合接口返回的卡池配置(仅取需要的字段)。</summary>
public sealed class FiveGroupModel
{
    [JsonPropertyName("code")] public int Code { get; set; }
    [JsonPropertyName("data")] public FiveGroupData? Data { get; set; }
}

public sealed class FiveGroupData
{
    [JsonPropertyName("versionPools")] public List<VersionPool>? VersionPools { get; set; }
}

public sealed class VersionPool
{
    [JsonPropertyName("upFiveRoleIds")] public List<int>? UpFiveRoleIds { get; set; }
    [JsonPropertyName("upFiveWeaponIds")] public List<int>? UpFiveWeaponIds { get; set; }
}

[JsonSerializable(typeof(FiveGroupModel))]
[JsonSerializable(typeof(FiveGroupData))]
[JsonSerializable(typeof(VersionPool))]
public sealed partial class UpPoolJsonContext : JsonSerializerContext;
