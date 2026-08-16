using System.Text.Json.Serialization;

namespace McKuro.Core.Services.Gacha;

/// <summary>聚合接口返回的卡池配置(api3.sanyueqi.cn/draw_config_infos)。</summary>
public sealed class FiveGroupModel
{
    [JsonPropertyName("code")] public int Code { get; set; }
    [JsonPropertyName("data")] public FiveGroupData? Data { get; set; }
}

public sealed class FiveGroupData
{
    [JsonPropertyName("five_group_config")] public FiveGroupConfig? FiveGroupConfig { get; set; }
}

public sealed class FiveGroupConfig
{
    [JsonPropertyName("five_maps")] public List<FiveMap>? FiveMaps { get; set; }
}

/// <summary>UP 卡池映射项:item_id=角色 resourceId,weapon_id=武器 resourceId。</summary>
public sealed class FiveMap
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("item_id")] public int ItemId { get; set; }
    [JsonPropertyName("weapon_id")] public int WeaponId { get; set; }
}

[JsonSerializable(typeof(FiveGroupModel))]
[JsonSerializable(typeof(FiveGroupData))]
[JsonSerializable(typeof(FiveGroupConfig))]
[JsonSerializable(typeof(FiveMap))]
public sealed partial class UpPoolJsonContext : JsonSerializerContext;
