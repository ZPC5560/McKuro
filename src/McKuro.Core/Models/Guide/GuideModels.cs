using System.Text.Json;
using System.Text.Json.Serialization;

namespace McKuro.Core.Models.Guide;

/// <summary>
/// mcguide 攻略站(guide-server.aki-game.com)数据模型。
/// <para>来源:登录抓包 + introduction/list + introduction/info 接口响应。
/// x-token 由 <c>/user/login/sdk</c> 返回(服务端动态 innerToken),无需自行构造。</para>
/// </summary>
public sealed class GuideEnvelope<T>
{
    [JsonPropertyName("code")] public int Code { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("data")] public T? Data { get; set; }
}

/// <summary>guide 登录换 x-token 响应(data)。</summary>
public sealed class GuideLoginToken
{
    [JsonPropertyName("token")] public string? Token { get; set; }
}

/// <summary>guide 登录请求(user/login/sdk)。</summary>
public sealed class GuideLoginSdkRequest
{
    [JsonPropertyName("cUid")] public string? CUid { get; set; }
    [JsonPropertyName("cName")] public string? CName { get; set; }
    [JsonPropertyName("accessToken")] public string? AccessToken { get; set; }
}

/// <summary>选择玩家请求(user/player/choose)。</summary>
public sealed class GuideChoosePlayerRequest
{
    [JsonPropertyName("playerId")] public long PlayerId { get; set; }
    [JsonPropertyName("serverId")] public string? ServerId { get; set; }
}

/// <summary>玩家列表项(user/player/list)。</summary>
public sealed class GuidePlayerItem
{
    [JsonPropertyName("playerId")] public long PlayerId { get; set; }
    [JsonPropertyName("playerName")] public string? PlayerName { get; set; }
    [JsonPropertyName("serverId")] public string? ServerId { get; set; }
    [JsonPropertyName("serverName")] public string? ServerName { get; set; }
    [JsonPropertyName("level")] public int Level { get; set; }
}

/// <summary>选择玩家响应(data 外层:含 profile)。</summary>
public sealed class GuideChooseData
{
    [JsonPropertyName("profile")] public GuideChooseProfile? Profile { get; set; }
}

/// <summary>选择玩家响应(data.profile)。</summary>
public sealed class GuideChooseProfile
{
    [JsonPropertyName("cUid")] public string? CUid { get; set; }
    [JsonPropertyName("channelId")] public int ChannelId { get; set; }
    [JsonPropertyName("chosenPlayer")] public GuidePlayerItem? ChosenPlayer { get; set; }
}

/// <summary>攻略列表项(introduction/list)。</summary>
public sealed class GuideIntroductionItem
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("role")] public GuideRoleRef? Role { get; set; }
    [JsonPropertyName("likeCount")] public long LikeCount { get; set; }
    [JsonPropertyName("collectCount")] public long CollectCount { get; set; }
    [JsonPropertyName("texts")] public List<GuideTextItem>? Texts { get; set; }
}

/// <summary>攻略项内嵌角色引用。</summary>
public sealed class GuideRoleRef
{
    [JsonPropertyName("roleGbId")] public string? RoleGbId { get; set; }
    [JsonPropertyName("cardPictureUrl")] public string? CardPictureUrl { get; set; }
    [JsonPropertyName("star")] public int Star { get; set; }
    [JsonPropertyName("texts")] public List<GuideTextItem>? Texts { get; set; }

    /// <summary>角色名(zh-Hans)。</summary>
    public string? Name => Texts?.FirstOrDefault(t => t.Language == "zh-Hans")?.Name;
}

/// <summary>多语言文本项。</summary>
public sealed class GuideTextItem
{
    [JsonPropertyName("language")] public string? Language { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("introductionName")] public string? IntroductionName { get; set; }
    [JsonPropertyName("recommendDescription")] public string? RecommendDescription { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("skillDisplay")] public string? SkillDisplay { get; set; }
}

/// <summary>攻略详情(introduction/info)顶层。</summary>
public sealed class GuideIntroductionInfo
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("role")] public GuideRoleInfo? Role { get; set; }
    [JsonPropertyName("roleAttribute")] public GuideRoleAttribute? RoleAttribute { get; set; }
    [JsonPropertyName("echo")] public GuideEcho? Echo { get; set; }
    [JsonPropertyName("echoTexts")] public List<GuideTextItem>? EchoTexts { get; set; }
    [JsonPropertyName("roleSkill")] public GuideRoleSkill? RoleSkill { get; set; }
    [JsonPropertyName("roleResonance")] public GuideRoleResonance? RoleResonance { get; set; }
    [JsonPropertyName("roleResonanceTexts")] public List<GuideTextItem>? RoleResonanceTexts { get; set; }
    [JsonPropertyName("weapon")] public GuideWeapon? Weapon { get; set; }
    [JsonPropertyName("weaponTexts")] public List<GuideTextItem>? WeaponTexts { get; set; }
    [JsonPropertyName("grade")] public string? Grade { get; set; }
    [JsonPropertyName("teammate")] public GuideTeammate? Teammate { get; set; }
}

/// <summary>详情内嵌角色信息。</summary>
public sealed class GuideRoleInfo
{
    [JsonPropertyName("roleGbId")] public string? RoleGbId { get; set; }
    [JsonPropertyName("star")] public int Star { get; set; }
    [JsonPropertyName("texts")] public List<GuideTextItem>? Texts { get; set; }
    [JsonPropertyName("element")] public GuideElement? Element { get; set; }

    public string? Name => Texts?.FirstOrDefault(t => t.Language == "zh-Hans")?.Name;
    public string? SkillDisplay => Texts?.FirstOrDefault(t => t.Language == "zh-Hans")?.SkillDisplay;
}

public sealed class GuideElement
{
    [JsonPropertyName("gbId")] public string? GbId { get; set; }
    [JsonPropertyName("pictureUrl")] public string? PictureUrl { get; set; }
}

/// <summary>角色属性达标(roleAttribute)。</summary>
public sealed class GuideRoleAttribute
{
    [JsonPropertyName("items")] public List<GuideAttributeItem>? Items { get; set; }
    [JsonPropertyName("isFinished")] public bool? IsFinished { get; set; }

    /// <summary>达标项数。</summary>
    [JsonIgnore] public int FinishedCount => Items?.Count(i => i.IsFinished == true) ?? 0;
    [JsonIgnore] public int TotalCount => Items?.Count ?? 0;
}

/// <summary>单个属性达标项。</summary>
public sealed class GuideAttributeItem
{
    [JsonPropertyName("gbId")] public string? GbId { get; set; }
    [JsonPropertyName("pictureUrl")] public string? PictureUrl { get; set; }
    [JsonPropertyName("texts")] public List<GuideTextItem>? Texts { get; set; }
    [JsonPropertyName("recommendAmount")] public string? RecommendAmount { get; set; }
    [JsonPropertyName("currentAmount")] public string? CurrentAmount { get; set; }
    [JsonPropertyName("isFinished")] public bool? IsFinished { get; set; }

    public string? Name => Texts?.FirstOrDefault(t => t.Language == "zh-Hans")?.Name;
}

/// <summary>声骸(echo)。</summary>
public sealed class GuideEcho
{
    [JsonPropertyName("current")] public GuideEchoBuild? Current { get; set; }
    [JsonPropertyName("main")] public GuideEchoBuild? Main { get; set; }
    [JsonPropertyName("spare")] public GuideEchoBuild? Spare { get; set; }
    [JsonPropertyName("isFinished")] public bool? IsFinished { get; set; }
}

/// <summary>一套声骸配装(主/备)。</summary>
public sealed class GuideEchoBuild
{
    [JsonPropertyName("echoProps")] public GuideEchoProps? EchoProps { get; set; }
    [JsonPropertyName("echoSetEffects")] public List<GuideEchoSetEffect>? EchoSetEffects { get; set; }
    [JsonPropertyName("echoAttributes")] public List<GuideEchoAttribute>? EchoAttributes { get; set; }
}

public sealed class GuideEchoProps
{
    [JsonPropertyName("gbId")] public string? GbId { get; set; }
    [JsonPropertyName("pictureUrl")] public string? PictureUrl { get; set; }
    [JsonPropertyName("star")] public int Star { get; set; }
    [JsonPropertyName("cost")] public int Cost { get; set; }
    [JsonPropertyName("texts")] public List<GuideTextItem>? Texts { get; set; }
    public string? Name => Texts?.FirstOrDefault(t => t.Language == "zh-Hans")?.Name;
}

public sealed class GuideEchoSetEffect
{
    [JsonPropertyName("echoSet")] public int EchoSet { get; set; }
    [JsonPropertyName("texts")] public List<GuideTextItem>? Texts { get; set; }
    public string? Name => Texts?.FirstOrDefault(t => t.Language == "zh-Hans")?.Name;
}

/// <summary>单件声骸(等级/主副词条达标)。</summary>
public sealed class GuideEchoAttribute
{
    [JsonPropertyName("cost")] public int Cost { get; set; }
    [JsonPropertyName("currentLevel")] public int? CurrentLevel { get; set; }
    [JsonPropertyName("isFinishedMaxLevel")] public bool? IsFinishedMaxLevel { get; set; }
    [JsonPropertyName("isFinished")] public bool? IsFinished { get; set; }
    [JsonPropertyName("attribute")] public GuideEchoPropRef? Attribute { get; set; }
    [JsonPropertyName("attribute2")] public GuideEchoPropRef? Attribute2 { get; set; }
}

public sealed class GuideEchoPropRef
{
    [JsonPropertyName("gbId")] public string? GbId { get; set; }
    [JsonPropertyName("texts")] public List<GuideTextItem>? Texts { get; set; }
    public string? Name => Texts?.FirstOrDefault(t => t.Language == "zh-Hans")?.Name;
}

/// <summary>技能(roleSkill)。</summary>
public sealed class GuideRoleSkill
{
    [JsonPropertyName("addPointTarget")] public List<GuideSkillTarget>? AddPointTarget { get; set; }
    /// <summary>固定技能列表(含图标 pictureUrl,用于角色详情页技能展示)。</summary>
    [JsonPropertyName("fixedSkills")] public List<GuideFixedSkill>? FixedSkills { get; set; }
    [JsonPropertyName("isFinished")] public bool? IsFinished { get; set; }
}

/// <summary>技能加点目标(推荐等级 vs 当前等级)。</summary>
public sealed class GuideSkillTarget
{
    [JsonPropertyName("gbId")] public string? GbId { get; set; }
    [JsonPropertyName("skillType")] public GuideSkillType? SkillType { get; set; }
    [JsonPropertyName("texts")] public List<GuideTextItem>? Texts { get; set; }
    /// <summary>推荐等级(部分角色攻略为字符串,用 JsonElement 容错)。</summary>
    [JsonPropertyName("recommendLevel")] public JsonElement? RecommendLevel { get; set; }
    /// <summary>当前等级(部分角色攻略为字符串,用 JsonElement 容错)。</summary>
    [JsonPropertyName("currentLevel")] public JsonElement? CurrentLevel { get; set; }

    public string? Name => Texts?.FirstOrDefault(t => t.Language == "zh-Hans")?.Name;
    public string? TypeName => SkillType?.Texts?.FirstOrDefault(t => t.Language == "zh-Hans")?.Name;

    /// <summary>推荐等级(解析后的 int;无法解析为 0)。</summary>
    public int RecommendLevelValue => TryParseInt(RecommendLevel);
    /// <summary>当前等级(解析后的 int;无法解析为 0)。</summary>
    public int CurrentLevelValue => TryParseInt(CurrentLevel);

    private static int TryParseInt(JsonElement? e)
    {
        if (e is { ValueKind: JsonValueKind.Number } num && num.TryGetInt32(out var v))
        {
            return v;
        }
        if (e is { ValueKind: JsonValueKind.String } str)
        {
            var s = str.GetString();
            if (int.TryParse(s, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var n))
            {
                return n;
            }
        }
        return 0;
    }
}

public sealed class GuideSkillType
{
    [JsonPropertyName("gbId")] public string? GbId { get; set; }
    [JsonPropertyName("texts")] public List<GuideTextItem>? Texts { get; set; }
}

/// <summary>固定技能(roleSkill.fixedSkills):角色详情页展示用,含图标。</summary>
public sealed class GuideFixedSkill
{
    [JsonPropertyName("gbId")] public string? GbId { get; set; }
    [JsonPropertyName("pictureUrl")] public string? PictureUrl { get; set; }
    [JsonPropertyName("skillType")] public GuideSkillType? SkillType { get; set; }
    [JsonPropertyName("texts")] public List<GuideTextItem>? Texts { get; set; }

    public string? Name => Texts?.FirstOrDefault(t => t.Language == "zh-Hans")?.Name;
    public string? TypeName => SkillType?.Texts?.FirstOrDefault(t => t.Language == "zh-Hans")?.Name;
}

/// <summary>共鸣链(roleResonance)。</summary>
public sealed class GuideRoleResonance
{
    [JsonPropertyName("items")] public List<GuideResonanceItem>? Items { get; set; }
    [JsonPropertyName("isFinished")] public bool? IsFinished { get; set; }
    [JsonPropertyName("texts")] public List<GuideTextItem>? Texts { get; set; }

    [JsonIgnore] public int AcquiredCount => Items?.Count(i => i.IsAcquired == true) ?? 0;
    [JsonIgnore] public int TotalCount => Items?.Count ?? 0;
}

public sealed class GuideResonanceItem
{
    [JsonPropertyName("resonanceSequence")] public int ResonanceSequence { get; set; }
    [JsonPropertyName("texts")] public List<GuideTextItem>? Texts { get; set; }
    [JsonPropertyName("isAcquired")] public bool? IsAcquired { get; set; }

    public string? Name => Texts?.FirstOrDefault(t => t.Language == "zh-Hans")?.Name;
    public string? Description => Texts?.FirstOrDefault(t => t.Language == "zh-Hans")?.Description;
}

/// <summary>武器(weapon)。</summary>
public sealed class GuideWeapon
{
    [JsonPropertyName("current")] public GuideWeaponItem? Current { get; set; }
    [JsonPropertyName("items")] public List<GuideWeaponItem>? Items { get; set; }
    [JsonPropertyName("isFinished")] public bool? IsFinished { get; set; }
}

public sealed class GuideWeaponItem
{
    [JsonPropertyName("gbId")] public string? GbId { get; set; }
    [JsonPropertyName("star")] public int Star { get; set; }
    [JsonPropertyName("pictureUrl")] public string? PictureUrl { get; set; }
    [JsonPropertyName("status")] public int Status { get; set; }
    [JsonPropertyName("isAcquired")] public bool? IsAcquired { get; set; }
    [JsonPropertyName("isFinished")] public bool? IsFinished { get; set; }
    [JsonPropertyName("weaponType")] public GuideSkillType? WeaponType { get; set; }
    [JsonPropertyName("texts")] public List<GuideTextItem>? Texts { get; set; }

    public string? Name => Texts?.FirstOrDefault(t => t.Language == "zh-Hans")?.Name;
    public string? TypeName => WeaponType?.Texts?.FirstOrDefault(t => t.Language == "zh-Hans")?.Name;
}

/// <summary>配队推荐(teammate)。</summary>
public sealed class GuideTeammate
{
    [JsonPropertyName("items")] public List<GuideTeammateItem>? Items { get; set; }
}

public sealed class GuideTeammateItem
{
    [JsonPropertyName("main")] public GuideRoleRef? Main { get; set; }
    [JsonPropertyName("spares")] public List<GuideRoleRef>? Spares { get; set; }
}

[JsonSerializable(typeof(GuideEnvelope<GuideLoginToken>))]
[JsonSerializable(typeof(GuideEnvelope<List<GuidePlayerItem>>))]
[JsonSerializable(typeof(GuideEnvelope<GuideChooseData>))]
[JsonSerializable(typeof(GuideEnvelope<List<GuideIntroductionItem>>))]
[JsonSerializable(typeof(GuideEnvelope<GuideIntroductionInfo>))]
[JsonSerializable(typeof(GuideLoginSdkRequest))]
[JsonSerializable(typeof(GuideChoosePlayerRequest))]
[JsonSerializable(typeof(GuidePlayerItem))]
[JsonSerializable(typeof(List<GuidePlayerItem>))]
[JsonSerializable(typeof(GuideChooseData))]
[JsonSerializable(typeof(GuideChooseProfile))]
[JsonSerializable(typeof(GuideLoginToken))]
[JsonSerializable(typeof(GuideIntroductionItem))]
[JsonSerializable(typeof(List<GuideIntroductionItem>))]
[JsonSerializable(typeof(GuideIntroductionInfo))]
[JsonSerializable(typeof(GuideRoleRef))]
[JsonSerializable(typeof(GuideRoleInfo))]
[JsonSerializable(typeof(GuideTextItem))]
[JsonSerializable(typeof(GuideFixedSkill))]
[JsonSerializable(typeof(List<GuideFixedSkill>))]
public sealed partial class GuideJsonContext : JsonSerializerContext;
