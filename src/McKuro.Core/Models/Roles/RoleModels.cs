using System.ComponentModel;
using System.IO;
using System.Text.Json.Serialization;

namespace McKuro.Core.Models.Roles;

/// <summary>角色基础信息(库街区 roleData.role 字段,与 WutheringWavesTool 模型一致)。</summary>
public sealed class RoleInfo
{
    [JsonPropertyName("roleId")] public int RoleId { get; set; }
    [JsonPropertyName("roleName")] public string RoleName { get; set; } = "";
    [JsonPropertyName("roleIconUrl")] public string RoleIconUrl { get; set; } = "";
    [JsonPropertyName("rolePicUrl")] public string RolePicUrl { get; set; } = "";
    [JsonPropertyName("level")] public int Level { get; set; }
    [JsonPropertyName("breach")] public int Breach { get; set; }
    [JsonPropertyName("chainUnlockNum")] public int ChainUnlockNum { get; set; }
    [JsonPropertyName("starLevel")] public int StarLevel { get; set; }
    [JsonPropertyName("attributeId")] public int AttributeId { get; set; }
    [JsonPropertyName("attributeName")] public string AttributeName { get; set; } = "";
    [JsonPropertyName("weaponTypeId")] public int WeaponTypeId { get; set; }
    [JsonPropertyName("weaponTypeName")] public string WeaponTypeName { get; set; } = "";
    [JsonPropertyName("acronym")] public string Acronym { get; set; } = "";
}

/// <summary>角色武器(weaponData.weapon)。</summary>
public sealed class WeaponInfo
{
    [JsonPropertyName("weaponId")] public int WeaponId { get; set; }
    [JsonPropertyName("weaponName")] public string WeaponName { get; set; } = "";
    [JsonPropertyName("weaponType")] public int WeaponType { get; set; }
    [JsonPropertyName("weaponStarLevel")] public int WeaponStarLevel { get; set; }
    [JsonPropertyName("weaponIcon")] public string WeaponIcon { get; set; } = "";
    [JsonPropertyName("weaponEffectName")] public string WeaponEffectName { get; set; } = "";
}

/// <summary>武器数据(getRoleDetail.weaponData,含等级/精炼)。</summary>
public sealed class WeaponData
{
    [JsonPropertyName("weapon")] public WeaponInfo? Weapon { get; set; }
    [JsonPropertyName("level")] public int Level { get; set; }
    [JsonPropertyName("breach")] public int Breach { get; set; }
    [JsonPropertyName("resonLevel")] public int Rank { get; set; }

    public string DisplayName => Weapon?.WeaponName ?? "未装备";
    public int StarLevel => Weapon?.WeaponStarLevel ?? 0;
}

/// <summary>技能条目(嵌套 skill,对齐 Haiyu getRoleDetail.skillList)。</summary>
public sealed class SkillInfo
{
    [JsonPropertyName("level")] public int SkillLevel { get; set; }
    [JsonPropertyName("skill")] public SkillBase? Skill { get; set; }

    public string SkillName => Skill?.SkillName ?? "";
}

/// <summary>技能基础信息(嵌套)。</summary>
public sealed class SkillBase
{
    [JsonPropertyName("id")] public int SkillId { get; set; }
    [JsonPropertyName("name")] public string SkillName { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("iconUrl")] public string IconUrl { get; set; } = "";
}

/// <summary>共鸣链(命座,对齐 Haiyu getRoleDetail.chainList)。</summary>
public sealed class ChainInfo
{
    [JsonPropertyName("order")] public int ChainNum { get; set; }
    [JsonPropertyName("name")] public string ChainName { get; set; } = "";
    [JsonPropertyName("unlocked")] public bool IsUnlock { get; set; }
    [JsonPropertyName("iconUrl")] public string IconUrl { get; set; } = "";
    [JsonPropertyName("description")] public string Description { get; set; } = "";
}

/// <summary>声骸(Phantom,鸣潮的"圣遗物",对齐 WutheringWavesTool Phantom)。</summary>
public sealed class EchoInfo
{
    [JsonPropertyName("level")] public int Level { get; set; }
    [JsonPropertyName("cost")] public int Cost { get; set; }
    [JsonPropertyName("quality")] public int Quality { get; set; }
    [JsonPropertyName("phantomProp")] public PhantomPropInfo? PhantomProp { get; set; }

    /// <summary>套装效果(参照 WutheringWavesTool fetterDetail)。</summary>
    [JsonPropertyName("fetterDetail")] public EchoFetterDetail? FetterDetail { get; set; }

    /// <summary>主词条(参照 WutheringWavesTool mainProps)。</summary>
    [JsonPropertyName("mainProps")] public List<EchoProp>? MainProps { get; set; }

    /// <summary>副词条(参照 WutheringWavesTool subProps)。</summary>
    [JsonPropertyName("subProps")] public List<EchoProp>? SubProps { get; set; }

    public string PhantomName => PhantomProp?.PhantomName ?? "";
    public string IconUrl => PhantomProp?.IconUrl ?? "";

    /// <summary>套装名(fetterDetail.name)。</summary>
    public string FetterName => FetterDetail?.Name ?? "";

    /// <summary>词条结构评级文本(词条:ACE/SSS/SS/S/N)。</summary>
    [JsonIgnore]
    public string PhantomRatingText => Rate.PhantomText;

    /// <summary>词条数值评级文本(数值:ACE/SSS/SS/S/N)。</summary>
    [JsonIgnore]
    public string PropRatingText => Rate.PropText;

    /// <summary>词条结构评级等级(供评级徽章按等级配色)。</summary>
    [JsonIgnore]
    public McKuro.Core.Services.Roles.EchoRatingLevel PhantomStatus => Rate.PhantomStatus;

    /// <summary>词条数值评级等级(供评级徽章按等级配色)。</summary>
    [JsonIgnore]
    public McKuro.Core.Services.Roles.EchoRatingLevel PropStatus => Rate.PropStatus;

    /// <summary>惰性缓存的评级结果(避免多次触发 RateEcho)。</summary>
    [JsonIgnore]
    private McKuro.Core.Services.Roles.EchoRating? _rateCache;
    [JsonIgnore]
    private McKuro.Core.Services.Roles.EchoRating Rate
        => _rateCache ??= McKuro.Core.Services.Roles.EchoRatingService.RateEcho(this);
}

/// <summary>声骸套装效果(fetterDetail)。</summary>
public sealed class EchoFetterDetail
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("iconUrl")] public string IconUrl { get; set; } = "";
    [JsonPropertyName("num")] public int Num { get; set; }
    [JsonPropertyName("groupId")] public int GroupId { get; set; }
}

/// <summary>声骸词条(主/副词条,参照 WutheringWavesTool PhoantomMainProps)。</summary>
public sealed class EchoProp
{
    [JsonPropertyName("attributeName")] public string AttributeName { get; set; } = "";
    [JsonPropertyName("attributeValue")] public string AttributeValue { get; set; } = "";
    [JsonPropertyName("iconUrl")] public string IconUrl { get; set; } = "";
    /// <summary>词条重要程度(0/1/2/3,副词条色条;库街区接口不返回此字段,用权重表计算)。</summary>
    [JsonPropertyName("level")] public int Level { get; set; }

    /// <summary>按通用权重表计算的有效词条重要度(0-3;优先用接口 Level,否则按属性名权重)。</summary>
    [JsonIgnore]
    public int EffectiveLevel => Level > 0
        ? Level
        : McKuro.Core.Services.Roles.EchoRatingService.GetPropLevel(AttributeName, AttributeValue);
}

/// <summary>声骸属性(equipPhantomList[].phantomProp)。</summary>
public sealed class PhantomPropInfo
{
    [JsonPropertyName("name")] public string PhantomName { get; set; } = "";
    [JsonPropertyName("phantomId")] public int PhantomId { get; set; }
    [JsonPropertyName("iconUrl")] public string IconUrl { get; set; } = "";
    [JsonPropertyName("quality")] public int Quality { get; set; }
    [JsonPropertyName("cost")] public int Cost { get; set; }
}

/// <summary>角色属性面板。</summary>
public sealed class RoleAttribute
{
    [JsonPropertyName("attributeId")] public int AttributeId { get; set; }
    [JsonPropertyName("attributeName")] public string AttributeName { get; set; } = "";
    [JsonPropertyName("attributeValue")] public string AttributeValue { get; set; } = "";
    [JsonPropertyName("attributeType")] public string AttributeType { get; set; } = "";
    [JsonPropertyName("iconUrl")] public string IconUrl { get; set; } = "";
    [JsonPropertyName("sort")] public int Sort { get; set; }
}

/// <summary>角色养成详情(库街区 roleData 列表中的一项)。</summary>
public sealed class RoleDetail : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// 数据填充(库街区 getRoleDetail 按需合并 / mcguide 攻略站)后通知绑定区刷新
    /// 详情区块及其计算属性(武器/技能/属性/声骸/共鸣链、列表卡片与头部卡片字段)。
    /// </summary>
    public void NotifyDetailChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Role)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Level)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(WeaponData)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Skills)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Attributes)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PhantomData)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Chains)));
        // 计算属性(依赖上方区块;不通知时绑定到精确属性名的表达式不会刷新)
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RoleName)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StarLevel)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AttributeName)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LevelText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ChainCount)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UnlockedChainCount)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsFullChain)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FullChainTitle)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasPhantoms)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasAttributes)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasEchoRating)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EchoRatingText)));
    }

    [JsonPropertyName("role")] public RoleInfo? Role { get; set; }
    [JsonPropertyName("level")] public int Level { get; set; }
    [JsonPropertyName("chainList")] public List<ChainInfo>? Chains { get; set; }
    [JsonPropertyName("weaponData")] public WeaponData? WeaponData { get; set; }
    [JsonPropertyName("phantomData")] public PhantomData? PhantomData { get; set; }
    [JsonPropertyName("skillList")] public List<SkillInfo>? Skills { get; set; }
    [JsonPropertyName("roleAttributeList")] public List<RoleAttribute>? Attributes { get; set; }

    public string RoleName => Role?.RoleName ?? "未知角色";
    public int StarLevel => Role?.StarLevel ?? 0;
    public string AttributeName => Role?.AttributeName ?? "";

    /// <summary>属性图标本地路径(Assets/attr/{attributeId}.png;仅角色列表卡片用)。</summary>
    public string AttributeIconPath =>
        Role is { AttributeId: > 0 } ? Path.Combine(AppContext.BaseDirectory, "Assets", "attr", $"{Role.AttributeId}.png") : "";
    public int ChainCount => Chains?.Count ?? 0;

    /// <summary>已解锁共鸣链数:优先用角色列表接口的 chainUnlockNum;否则按链列表统计。</summary>
    public int UnlockedChainCount =>
        Role is { ChainUnlockNum: > 0 } ? Role.ChainUnlockNum
        : Chains?.Count(c => c.IsUnlock) ?? 0;

    /// <summary>是否 6 链全部解锁(用于显示全链称号)。</summary>
    [JsonIgnore]
    public bool IsFullChain => ChainCount > 0 && UnlockedChainCount >= ChainCount;

    /// <summary>全链称号(6 链全部解锁时 = 第 6 链名称)。</summary>
    [JsonIgnore]
    public string FullChainTitle => Chains is { Count: > 0 } && IsFullChain
        ? Chains[^1].ChainName
        : "";

    /// <summary>列表卡片等级文本。</summary>
    public string LevelText => $"Lv.{Role?.Level ?? 0}";

    /// <summary>声骸评级(词条结构/数值/总分/达成度);无声骸时返回空。仅供详情页展示用。</summary>
    [JsonIgnore]
    public string EchoRatingText
    {
        get
        {
            var phantoms = PhantomData?.Phantoms;
            if (phantoms is null || phantoms.Count == 0)
            {
                return "";
            }
            var rating = McKuro.Core.Services.Roles.EchoRatingService.RateRole(phantoms);
            return $"声骸评级 {rating.LevelText} · 达成度 {rating.AchievementPercent}% ({rating.TotalScore}/{rating.MaxScore})";
        }
    }

    /// <summary>是否有声骸评级(用于 UI 可见性)。</summary>
    [JsonIgnore]
    public bool HasEchoRating => PhantomData?.Phantoms is { Count: > 0 };

    /// <summary>是否有声骸数据。</summary>
    public bool HasPhantoms => PhantomData?.Phantoms is { Count: > 0 };

    /// <summary>是否有属性面板数据。</summary>
    public bool HasAttributes => Attributes is { Count: > 0 };

    /// <summary>
    /// 详情区块是否完整(武器/技能/属性面板齐全)。
    /// getRoleDetail 被极验风控时接口只返回基础信息(详情为 null),此处为 false。
    /// </summary>
    public bool IsDetailComplete => WeaponData is not null && Skills is { Count: > 0 } && Attributes is { Count: > 0 };
}

/// <summary>声骸数据(对齐 Haiyu getRoleDetail.phantomData → equipPhantomList)。</summary>
public sealed class PhantomData
{
    [JsonPropertyName("equipPhantomList")] public List<EchoInfo>? Phantoms { get; set; }
}

/// <summary>角色数据接口响应(data 部分)。</summary>
public sealed class RoleDataResponse
{
    [JsonPropertyName("roleData")] public List<RoleDetail>? RoleData { get; set; }
}

[JsonSerializable(typeof(RoleDetail))]
[JsonSerializable(typeof(RoleInfo))]
[JsonSerializable(typeof(WeaponData))]
[JsonSerializable(typeof(WeaponInfo))]
[JsonSerializable(typeof(SkillInfo))]
[JsonSerializable(typeof(SkillBase))]
[JsonSerializable(typeof(ChainInfo))]
[JsonSerializable(typeof(EchoInfo))]
[JsonSerializable(typeof(EchoFetterDetail))]
[JsonSerializable(typeof(EchoProp))]
[JsonSerializable(typeof(PhantomPropInfo))]
[JsonSerializable(typeof(PhantomData))]
[JsonSerializable(typeof(RoleAttribute))]
[JsonSerializable(typeof(RoleDataResponse))]
[JsonSerializable(typeof(List<RoleDetail>))]
public sealed partial class RoleJsonContext : JsonSerializerContext;
