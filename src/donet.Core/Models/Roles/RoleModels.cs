using System.Text.Json.Serialization;

namespace donet.Core.Models.Roles;

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

/// <summary>武器数据(weaponData 含等级/精炼)。</summary>
public sealed class WeaponData
{
    [JsonPropertyName("weapon")] public WeaponInfo? Weapon { get; set; }
    [JsonPropertyName("level")] public int Level { get; set; }
    [JsonPropertyName("breach")] public int Breach { get; set; }
    [JsonPropertyName("rank")] public int Rank { get; set; }

    public string DisplayName => Weapon?.WeaponName ?? "未装备";
    public int StarLevel => Weapon?.WeaponStarLevel ?? 0;
}

/// <summary>技能(含等级)。</summary>
public sealed class SkillInfo
{
    [JsonPropertyName("skillId")] public int SkillId { get; set; }
    [JsonPropertyName("skillName")] public string SkillName { get; set; } = "";
    [JsonPropertyName("skillLevel")] public int SkillLevel { get; set; }
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("iconUrl")] public string IconUrl { get; set; } = "";
}

/// <summary>共鸣链(命座)。</summary>
public sealed class ChainInfo
{
    [JsonPropertyName("chainId")] public int ChainId { get; set; }
    [JsonPropertyName("chainName")] public string ChainName { get; set; } = "";
    [JsonPropertyName("chainNum")] public int ChainNum { get; set; }
    [JsonPropertyName("isUnlock")] public bool IsUnlock { get; set; }
    [JsonPropertyName("iconUrl")] public string IconUrl { get; set; } = "";
}

/// <summary>声骸(Phantom,鸣潮的"圣遗物")。</summary>
public sealed class EchoInfo
{
    [JsonPropertyName("phantomId")] public int PhantomId { get; set; }
    [JsonPropertyName("phantomName")] public string PhantomName { get; set; } = "";
    [JsonPropertyName("level")] public int Level { get; set; }
    [JsonPropertyName("quality")] public int Quality { get; set; }
    [JsonPropertyName("cost")] public int Cost { get; set; }
    [JsonPropertyName("iconUrl")] public string IconUrl { get; set; } = "";
}

/// <summary>角色属性面板。</summary>
public sealed class RoleAttribute
{
    [JsonPropertyName("attributeName")] public string AttributeName { get; set; } = "";
    [JsonPropertyName("attributeValue")] public string AttributeValue { get; set; } = "";
    [JsonPropertyName("attributeType")] public string AttributeType { get; set; } = "";
}

/// <summary>角色养成详情(库街区 roleData 列表中的一项)。</summary>
public sealed class RoleDetail
{
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
    public int ChainCount => Role?.ChainUnlockNum ?? 0;

    /// <summary>已解锁共鸣链数量(从链列表统计)。</summary>
    public int UnlockedChainCount =>
        Chains?.Count(c => c.IsUnlock) ?? 0;
}

/// <summary>声骸数据。</summary>
public sealed class PhantomData
{
    [JsonPropertyName("phantoms")] public List<EchoInfo>? Phantoms { get; set; }
}

/// <summary>角色数据接口响应(data 部分)。</summary>
public sealed class RoleDataResponse
{
    [JsonPropertyName("roleData")] public List<RoleDetail>? RoleData { get; set; }
}
