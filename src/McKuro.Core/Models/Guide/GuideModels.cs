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

/// <summary>攻略项内嵌角色引用(teammate.items[*].main / .spares[*];实测两者字段完全同构)。</summary>
public sealed class GuideRoleRef
{
    [JsonPropertyName("roleGbId")] public string? RoleGbId { get; set; }
    [JsonPropertyName("cardPictureUrl")] public string? CardPictureUrl { get; set; }
    /// <summary>角色立绘 URL(实测 teammate main/spares 有;list/role 无)。</summary>
    [JsonPropertyName("illustrationPictureUrl")] public string? IllustrationPictureUrl { get; set; }
    [JsonPropertyName("star")] public int Star { get; set; }
    [JsonPropertyName("texts")] public List<GuideTextItem>? Texts { get; set; }
    /// <summary>是否已拥有(★「未拥有」判定:false=未拥有;实测 teammate main/spares 有该字段)。</summary>
    [JsonPropertyName("isAcquired")] public bool? IsAcquired { get; set; }
    /// <summary>属性(实测 teammate main 有 element;用于配队区属性图标)。</summary>
    [JsonPropertyName("element")] public GuideElement? Element { get; set; }
    /// <summary>玩法/操作演示列表(实测 teammate main 有 rolePlays)。</summary>
    [JsonPropertyName("rolePlays")] public List<GuideRolePlay>? RolePlays { get; set; }

    /// <summary>角色名(zh-Hans)。</summary>
    public string? Name => Texts?.FirstOrDefault(t => t.Language == "zh-Hans")?.Name;

    /// <summary>基础连招/技能展示文本(zh-Hans;实测 teammate main 有,如「基础连招:普通攻击*5 → 延奏离场」)。</summary>
    public string? SkillDisplay => Texts?.FirstOrDefault(t => t.Language == "zh-Hans")?.SkillDisplay;

    /// <summary>未拥有(isAcquired == false);字段缺失(null)时不判为未拥有。</summary>
    [JsonIgnore] public bool IsNotOwned => IsAcquired == false;
}

/// <summary>多语言文本项。</summary>
public sealed class GuideTextItem
{
    [JsonPropertyName("language")] public string? Language { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("introductionName")] public string? IntroductionName { get; set; }
    /// <summary>攻略作者(如「轩儿Xuaner」;baseTexts/list texts 有)。</summary>
    [JsonPropertyName("introductionSource")] public string? IntroductionSource { get; set; }
    [JsonPropertyName("recommendDescription")] public string? RecommendDescription { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("skillDisplay")] public string? SkillDisplay { get; set; }
    /// <summary>武器效果名(如「承天之祐」;仅武器 texts 有)。</summary>
    [JsonPropertyName("effectName")] public string? EffectName { get; set; }
    /// <summary>武器效果描述(仅武器 texts 有)。</summary>
    [JsonPropertyName("effectDescription")] public string? EffectDescription { get; set; }
}

/// <summary>攻略详情(introduction/info)顶层。</summary>
public sealed class GuideIntroductionInfo
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("role")] public GuideRoleInfo? Role { get; set; }
    /// <summary>攻略标题/作者等顶层文本(baseTexts)。</summary>
    [JsonPropertyName("baseTexts")] public List<GuideBaseTextItem>? BaseTexts { get; set; }
    [JsonPropertyName("roleAttribute")] public GuideRoleAttribute? RoleAttribute { get; set; }
    [JsonPropertyName("echo")] public GuideEcho? Echo { get; set; }
    [JsonPropertyName("echoTexts")] public List<GuideTextItem>? EchoTexts { get; set; }
    [JsonPropertyName("roleSkill")] public GuideRoleSkill? RoleSkill { get; set; }
    [JsonPropertyName("roleResonance")] public GuideRoleResonance? RoleResonance { get; set; }
    [JsonPropertyName("roleResonanceTexts")] public List<GuideResonanceRecommendation>? RoleResonanceTexts { get; set; }
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
    /// <summary>玩法/操作演示列表(含图标;与技能演示同源展示)。</summary>
    [JsonPropertyName("rolePlays")] public List<GuideRolePlay>? RolePlays { get; set; }

    public string? Name => Texts?.FirstOrDefault(t => t.Language == "zh-Hans")?.Name;
    public string? SkillDisplay => Texts?.FirstOrDefault(t => t.Language == "zh-Hans")?.SkillDisplay;
}

/// <summary>玩法演示项(rolePlays):图标 + 可选的第二图标。</summary>
public sealed class GuideRolePlay
{
    [JsonPropertyName("gbId")] public string? GbId { get; set; }
    [JsonPropertyName("pictureUrl")] public string? PictureUrl { get; set; }
    [JsonPropertyName("secondPictureUrl")] public string? SecondPictureUrl { get; set; }
}

/// <summary>
/// 角色基础资料(role/info 的 data):技能演示视频 + 角色特点图标。
/// <para>
/// 技能演示视频只在本接口(<c>skills[].videoUrl</c>,实测 5 个技能全有 mp4);
/// introduction/info 只带 keynoteSkills 的 1 个视频,不足以撑起演示列表。
/// </para>
/// </summary>
public sealed class GuideRoleInfoData
{
    [JsonPropertyName("roleGbId")] public string? RoleGbId { get; set; }
    [JsonPropertyName("cardPictureUrl")] public string? CardPictureUrl { get; set; }
    [JsonPropertyName("star")] public int Star { get; set; }
    [JsonPropertyName("texts")] public List<GuideTextItem>? Texts { get; set; }
    /// <summary>角色特点图标(概览卡横排展示)。</summary>
    [JsonPropertyName("rolePlays")] public List<GuideRolePlay>? RolePlays { get; set; }
    /// <summary>技能列表(含演示视频 videoUrl)。</summary>
    [JsonPropertyName("skills")] public List<GuideRoleSkillVideo>? Skills { get; set; }

    /// <summary>角色名。</summary>
    public string? Name => Texts?.FirstOrDefault(t => t.Language == "zh-Hans")?.Name;
}

/// <summary>role/info 的技能项(含演示视频)。</summary>
public sealed class GuideRoleSkillVideo
{
    [JsonPropertyName("gbId")] public string? GbId { get; set; }
    [JsonPropertyName("pictureUrl")] public string? PictureUrl { get; set; }
    /// <summary>技能演示视频(mp4)。</summary>
    [JsonPropertyName("videoUrl")] public string? VideoUrl { get; set; }
    [JsonPropertyName("skillType")] public GuideSkillType? SkillType { get; set; }
    [JsonPropertyName("texts")] public List<GuideTextItem>? Texts { get; set; }

    public string? Name => Texts?.FirstOrDefault(t => t.Language == "zh-Hans")?.Name;
    public string? TypeName => SkillType?.Texts?.FirstOrDefault(t => t.Language == "zh-Hans")?.Name;
    public string? Description => Texts?.FirstOrDefault(t => t.Language == "zh-Hans")?.Description;
    public bool HasVideo => !string.IsNullOrWhiteSpace(VideoUrl);
}

public sealed class GuideElement
{
    [JsonPropertyName("gbId")] public string? GbId { get; set; }
    [JsonPropertyName("pictureUrl")] public string? PictureUrl { get; set; }
    /// <summary>第二图标(实测 teammate main.element 有 secondPictureUrl)。</summary>
    [JsonPropertyName("secondPictureUrl")] public string? SecondPictureUrl { get; set; }
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
    /// <summary>核心技能(含演示视频 videoUrl;实测 1504 延奏技能有 mp4)。</summary>
    [JsonPropertyName("keynoteSkill")] public GuideFixedSkill? KeynoteSkill { get; set; }
    /// <summary>核心技能列表(含演示视频 videoUrl)。</summary>
    [JsonPropertyName("keynoteSkills")] public List<GuideFixedSkill>? KeynoteSkills { get; set; }
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

/// <summary>固定技能(roleSkill.fixedSkills / keynoteSkills):角色详情页展示用,含图标与演示视频。</summary>
public sealed class GuideFixedSkill
{
    [JsonPropertyName("gbId")] public string? GbId { get; set; }
    [JsonPropertyName("pictureUrl")] public string? PictureUrl { get; set; }
    /// <summary>技能演示视频(mp4;攻略站 guide-res 域名,可内嵌播放)。</summary>
    [JsonPropertyName("videoUrl")] public string? VideoUrl { get; set; }
    [JsonPropertyName("skillType")] public GuideSkillType? SkillType { get; set; }
    [JsonPropertyName("texts")] public List<GuideTextItem>? Texts { get; set; }

    public string? Name => Texts?.FirstOrDefault(t => t.Language == "zh-Hans")?.Name;
    public string? TypeName => SkillType?.Texts?.FirstOrDefault(t => t.Language == "zh-Hans")?.Name;
    public string? Description => Texts?.FirstOrDefault(t => t.Language == "zh-Hans")?.Description;
    public bool HasVideo => !string.IsNullOrWhiteSpace(VideoUrl);
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
    [JsonPropertyName("gbId")] public string? GbId { get; set; }
    /// <summary>共鸣链图标(guide-res 域名;实测 1504 六链均有图)。</summary>
    [JsonPropertyName("pictureUrl")] public string? PictureUrl { get; set; }
    [JsonPropertyName("resonanceSequence")] public int ResonanceSequence { get; set; }
    [JsonPropertyName("texts")] public List<GuideTextItem>? Texts { get; set; }
    [JsonPropertyName("isAcquired")] public bool? IsAcquired { get; set; }

    public string? Name => Texts?.FirstOrDefault(t => t.Language == "zh-Hans")?.Name;
    public string? Description => Texts?.FirstOrDefault(t => t.Language == "zh-Hans")?.Description;
}

/// <summary>共鸣链推荐项:攻略推荐的链数与说明(roleResonanceTexts)。</summary>
public sealed class GuideResonanceRecommendation
{
    [JsonPropertyName("language")] public string? Language { get; set; }
    /// <summary>攻略推荐描述(如「共鸣链2…共鸣链4…共鸣链6是副C最重要的」)。</summary>
    [JsonPropertyName("recommendDescription")] public string? RecommendDescription { get; set; }
}

/// <summary>baseTexts 顶层:攻略标题/作者(实测 introduction/info 顶层 baseTexts 数组,字段与 GuideTextItem 同构)。</summary>
public sealed class GuideBaseTextItem
{
    [JsonPropertyName("language")] public string? Language { get; set; }
    [JsonPropertyName("introductionName")] public string? IntroductionName { get; set; }
    [JsonPropertyName("introductionSource")] public string? IntroductionSource { get; set; }
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
    [JsonPropertyName("pictureUrl")] public string? PictureUrl { get; set; }
    [JsonPropertyName("star")] public int Star { get; set; }
    /// <summary>推荐位:实测 1=首选(推荐武器),2=备选(items 数组内;无该字段时为 0)。</summary>
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

/// <summary>
/// 配队推荐项(teammate.items[*])。
/// <para>实测 1311 的 items[0] 有 7 个字段:main / spares / weapon / echoProps /
/// echoSetEffect2 / echoSetEffect5 / echoAttributes(此前只映射了 main/spares)。</para>
/// </summary>
public sealed class GuideTeammateItem
{
    [JsonPropertyName("main")] public GuideRoleRef? Main { get; set; }
    [JsonPropertyName("spares")] public List<GuideRoleRef>? Spares { get; set; }
    /// <summary>推荐武器(与 weapon.current 同构)。</summary>
    [JsonPropertyName("weapon")] public GuideWeaponItem? Weapon { get; set; }
    /// <summary>推荐声骸主词条(4C)。</summary>
    [JsonPropertyName("echoProps")] public GuideEchoProps? EchoProps { get; set; }
    /// <summary>2 件套声骸效果。</summary>
    [JsonPropertyName("echoSetEffect2")] public GuideEchoSetEffect? EchoSetEffect2 { get; set; }
    /// <summary>5 件套声骸效果(实测可能为 null)。</summary>
    [JsonPropertyName("echoSetEffect5")] public GuideEchoSetEffect? EchoSetEffect5 { get; set; }
    /// <summary>声骸词条(每项含 cost + attribute)。</summary>
    [JsonPropertyName("echoAttributes")] public List<GuideEchoAttribute>? EchoAttributes { get; set; }
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
[JsonSerializable(typeof(GuideBaseTextItem))]
[JsonSerializable(typeof(GuideResonanceRecommendation))]
[JsonSerializable(typeof(List<GuideResonanceRecommendation>))]
[JsonSerializable(typeof(GuideRoleRef))]
[JsonSerializable(typeof(GuideRoleInfo))]
[JsonSerializable(typeof(GuideRoleInfoData))]
[JsonSerializable(typeof(GuideEnvelope<GuideRoleInfoData>))]
[JsonSerializable(typeof(GuideRoleSkillVideo))]
[JsonSerializable(typeof(List<GuideRoleSkillVideo>))]
[JsonSerializable(typeof(GuideRolePlay))]
[JsonSerializable(typeof(List<GuideRolePlay>))]
[JsonSerializable(typeof(GuideTextItem))]
[JsonSerializable(typeof(GuideFixedSkill))]
[JsonSerializable(typeof(List<GuideFixedSkill>))]
public sealed partial class GuideJsonContext : JsonSerializerContext;
