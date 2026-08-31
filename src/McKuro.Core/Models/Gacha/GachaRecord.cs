using System.Text.Json.Serialization;

namespace McKuro.Core.Models.Gacha;

using CardPoolTypeEnum = McKuro.Core.Models.Gacha.CardPoolType;
/// <summary>
/// 单次抽卡记录(与 gmserver-api 返回字段一致)。
/// <para>gmserver-api 的 cardPoolType 返回中文卡池名(如"角色精准调谐"),数值字段可能为字符串,
/// 用 <see cref="PoolType"/> 把卡池名映射到 <see cref="CardPoolType"/> 枚举。</para>
/// </summary>
public sealed class GachaRecord
{
    /// <summary>卡池标识(gmserver-api 通常返回中文名,如"角色精准调谐";部分新卡池如联动/忆旅返回数字 ID 串,如 "10"/"12")。</summary>
    [JsonPropertyName("cardPoolType")]
    public string CardPoolType { get; set; } = "";

    [JsonPropertyName("resourceId")] public int ResourceId { get; set; }

    [JsonPropertyName("qualityLevel")] public int QualityLevel { get; set; }

    [JsonPropertyName("resourceType")] public string ResourceType { get; set; } = "";

    [JsonPropertyName("name")] public string Name { get; set; } = "";

    [JsonPropertyName("count")] public int Count { get; set; }

    [JsonPropertyName("time")] public string Time { get; set; } = "";

    /// <summary>归属玩家 ID(本地存储时写入)。</summary>
    [JsonIgnore] public string PlayerId { get; set; } = "";

    /// <summary>卡池标识 → 枚举(分析/存储用)。优先按数字 ID 解析,再按名称包含关键词映射;
    /// 常驻池为混合池(角色+武器),按资源类型拆分为角色常驻/武器常驻。
    /// <para>结果按实例缓存:分析路径(分组/每日统计)会对同一记录多次访问 PoolType,
    /// 且关键词匹配必须走 Ordinal(文化敏感 Contains 慢 5-10 倍)。</para></summary>
    [JsonIgnore]
    public CardPoolTypeEnum PoolType
    {
        get
        {
            if (_poolTypeCache is { } cached)
            {
                return cached;
            }
            var name = CardPoolType ?? "";
            // gmserver 对部分新卡池(联动/忆旅等)返回数字 ID 字符串(如 "10"/"12"),数值与枚举一致,直接映射。
            CardPoolTypeEnum result;
            if (int.TryParse(name, out var poolId) && Enum.IsDefined((CardPoolTypeEnum)poolId))
            {
                result = (CardPoolTypeEnum)poolId;
            }
            else if (name.Contains("忆旅", StringComparison.Ordinal)) result = name.Contains("武器", StringComparison.Ordinal) ? CardPoolTypeEnum.WeaponMemoryJourney : CardPoolTypeEnum.CharacterMemoryJourney;
            else if (name.Contains("联动", StringComparison.Ordinal)) result = name.Contains("武器", StringComparison.Ordinal) ? CardPoolTypeEnum.WeaponCollaboration : CardPoolTypeEnum.CharacterCollaboration;
            else if (name.Contains("新手", StringComparison.Ordinal) && name.Contains("感恩", StringComparison.Ordinal)) result = CardPoolTypeEnum.GratitudeOrientation;
            else if (name.Contains("新手", StringComparison.Ordinal)) result = CardPoolTypeEnum.Beginner;
            else if (name.Contains("常驻", StringComparison.Ordinal)) result = IsRole ? CardPoolTypeEnum.RoleResident : CardPoolTypeEnum.WeaponsResident;
            else if (name.Contains("新旅", StringComparison.Ordinal)) result = name.Contains("武器", StringComparison.Ordinal) ? CardPoolTypeEnum.WeaponNovice : CardPoolTypeEnum.CharacterNovice;
            else if (name.Contains("角色", StringComparison.Ordinal)) result = CardPoolTypeEnum.RoleActivity;
            else if (name.Contains("武器", StringComparison.Ordinal)) result = CardPoolTypeEnum.WeaponsActivity;
            else result = CardPoolTypeEnum.RoleActivity;
            _poolTypeCache = result;
            return result;
        }
    }

    private CardPoolTypeEnum? _poolTypeCache;

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
