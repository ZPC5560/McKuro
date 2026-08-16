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

    /// <summary>实际卡池列表(含起止时间与 UP 五星 ID,是判定当期 UP 的权威来源)。</summary>
    [JsonPropertyName("pool_list")] public List<PoolItem>? PoolList { get; set; }
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

    /// <summary>卡池类型:null/空 = 限定角色;0 = 常驻角色(如卡卡罗/凌阳)。用于区分 UP/歪。</summary>
    [JsonPropertyName("pool_type")] public int? PoolType { get; set; }
}

/// <summary>单个卡池条目(pool_list 项):用于按生效时间段确定当期 UP 五星。</summary>
public sealed class PoolItem
{
    [JsonPropertyName("start_at")] public string? StartAt { get; set; }
    [JsonPropertyName("end_at")] public string? EndAt { get; set; }
    [JsonPropertyName("pool_id")] public string? PoolId { get; set; }

    /// <summary>当期 UP 五星名(多个以逗号分隔)。</summary>
    [JsonPropertyName("up_five_names")] public string? UpFiveNames { get; set; }

    /// <summary>当期 UP 五星 ID(多个以逗号分隔)。</summary>
    [JsonPropertyName("up_five_ids")] public string? UpFiveIds { get; set; }

    [JsonPropertyName("up_four_ids")] public string? UpFourIds { get; set; }

    /// <summary>卡池类型:role=角色池,weapon=武器池。</summary>
    [JsonPropertyName("type")] public string? Type { get; set; }
}

[JsonSerializable(typeof(FiveGroupModel))]
[JsonSerializable(typeof(FiveGroupData))]
[JsonSerializable(typeof(FiveGroupConfig))]
[JsonSerializable(typeof(FiveMap))]
[JsonSerializable(typeof(PoolItem))]
public sealed partial class UpPoolJsonContext : JsonSerializerContext;
