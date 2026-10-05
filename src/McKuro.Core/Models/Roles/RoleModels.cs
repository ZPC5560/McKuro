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

/// <summary>
/// 共鸣链(命座,对齐 Haiyu getRoleDetail.chainList)。
/// <para>
/// 实现 <see cref="INotifyPropertyChanged"/>:库街区 chainList 无图标,
/// 攻略数据到达后按链序号回填 <see cref="IconUrl"/>(见 RolesViewModel.MergeGuideChains);
/// 不通知则 UI 已经渲染过该链,后补的图标不会显示(用户反馈"共鸣链还是没有显示图标")。
/// </para>
/// </summary>
public sealed class ChainInfo : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private string _iconUrl = "";

    [JsonPropertyName("order")] public int ChainNum { get; set; }
    [JsonPropertyName("name")] public string ChainName { get; set; } = "";

    private bool _isUnlock;

    /// <summary>
    /// 是否已解锁(变更时通知 UI,并连带通知 <see cref="ChainFrameColor"/>;
    /// 评审反馈:此前为无通知 auto-property,回填解锁态后星框色会停在旧值)。
    /// </summary>
    [JsonPropertyName("unlocked")]
    public bool IsUnlock
    {
        get => _isUnlock;
        set
        {
            if (_isUnlock == value)
            {
                return;
            }
            _isUnlock = value;
            var args = new PropertyChangedEventArgs(nameof(IsUnlock));
            PropertyChanged?.Invoke(this, args);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ChainFrameColor)));
        }
    }

    /// <summary>链图标(库街区无,由攻略 pictureUrl 回填;变更时通知 UI)。</summary>
    [JsonPropertyName("iconUrl")]
    public string IconUrl
    {
        get => _iconUrl;
        set
        {
            if (string.Equals(_iconUrl, value, StringComparison.Ordinal))
            {
                return;
            }
            _iconUrl = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IconUrl)));
        }
    }

    [JsonPropertyName("description")] public string Description { get; set; } = "";

    private bool _isRecommended;
    /// <summary>是否为攻略推荐的共鸣链(由攻略 roleResonanceTexts 解析链号后回填;变更时通知 UI)。</summary>
    [JsonIgnore]
    public bool IsRecommended
    {
        get => _isRecommended;
        set
        {
            if (_isRecommended == value)
            {
                return;
            }
            _isRecommended = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRecommended)));
        }
    }

    /// <summary>
    /// 四角星形装饰边框的描边色(十六进制)。
    /// <para>已解锁 = 金色(呼应攻略站装饰框);未解锁 = 中性灰,与图标置灰一致。
    /// Core 层不引用 Avalonia,故返回色值字符串,由 UI 转换器转画刷。</para>
    /// </summary>
    [JsonIgnore]
    public string ChainFrameColor => IsUnlock ? "#B08D3F" : "#9E9E9E";
}

/// <summary>声骸(Phantom,鸣潮的"圣遗物",对齐 WutheringWavesTool Phantom)。</summary>
public sealed class EchoInfo : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

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

    /// <summary>词条评级文本(<b>英文档位</b> ACE/SSS/SS/S/N;与总评的中文毕业术语不同,见 GraduationTextOf)。</summary>
    [JsonIgnore]
    public string PhantomRatingText => Rate.PhantomText;

    /// <summary>数值评级文本(与词条评级同源;保留以兼容绑定)。</summary>
    [JsonIgnore]
    public string PropRatingText => Rate.PropText;

    /// <summary>独立评分文本(如「62.0分」,一位小数对齐参考工具;权重回填时随评级一起刷新)。</summary>
    [JsonIgnore]
    public string ScoreText => Services.CoreStrings.F("Roles.EchoScoreFmt", "{0:0.0}分", Rate.Score);

    /// <summary>词条结构评级等级(供评级徽章按等级配色)。</summary>
    [JsonIgnore]
    public McKuro.Core.Services.Roles.EchoRatingLevel PhantomStatus => Rate.PhantomStatus;

    /// <summary>词条数值评级等级(供评级徽章按等级配色)。</summary>
    [JsonIgnore]
    public McKuro.Core.Services.Roles.EchoRatingLevel PropStatus => Rate.PropStatus;

    /// <summary>惰性缓存的评级结果(避免多次触发 RateEcho;权重表回填时清缓存)。</summary>
    [JsonIgnore]
    private McKuro.Core.Services.Roles.EchoRating? _rateCache;
    [JsonIgnore]
    private McKuro.Core.Services.Roles.EchoRating Rate
        => _rateCache ??= McKuro.Core.Services.Roles.EchoRatingService.RateEcho(this, PriorityWeights);

    /// <summary>
    /// Wiki「声骸词条」优先级权重(每角色,来自攻略站 Wiki 角色攻略;见 WikiGuideService)。
    /// <para>VM 在角色数据装载后异步拉取回填;变更时清评级缓存并通知 UI(联名角色/无攻略为 null,回退官方 valid/通用权重)。</para>
    /// </summary>
    [JsonIgnore]
    public IReadOnlyDictionary<string, double>? PriorityWeights
    {
        get => _priorityWeights;
        set
        {
            if (ReferenceEquals(_priorityWeights, value))
            {
                return;
            }
            _priorityWeights = value;
            _rateCache = null;
            var args = new PropertyChangedEventArgs(nameof(PriorityWeights));
            PropertyChanged?.Invoke(this, args);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PhantomRatingText)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PhantomStatus)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PropRatingText)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PropStatus)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ScoreText)));
        }
    }
    [JsonIgnore] private IReadOnlyDictionary<string, double>? _priorityWeights;

    // ---- 攻略站推荐标识(VM 在攻略加载后回填;不入 JSON/缓存,每次反序列化后由 VM 重新计算) ----

    private bool _isRecommendedPhantom;
    private bool _isRecommendedSet;

    /// <summary>是否为攻略站推荐声骸(仅 4C 且与推荐配装首件一致;变更时通知 UI)。</summary>
    [JsonIgnore]
    public bool IsRecommendedPhantom
    {
        get => _isRecommendedPhantom;
        set
        {
            if (_isRecommendedPhantom == value)
            {
                return;
            }
            _isRecommendedPhantom = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRecommendedPhantom)));
        }
    }

    /// <summary>其套装是否为攻略站推荐套装(变更时通知 UI)。</summary>
    [JsonIgnore]
    public bool IsRecommendedSet
    {
        get => _isRecommendedSet;
        set
        {
            if (_isRecommendedSet == value)
            {
                return;
            }
            _isRecommendedSet = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRecommendedSet)));
        }
    }
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
    /// <summary>
    /// 词条重要程度(0/1/2/3):<b>库街区 getRoleDetail 不返回此字段</b>(恒为 0),
    /// 保留以兼容同结构的其他数据源;有效判定用 <see cref="Valid"/>。
    /// </summary>
    [JsonPropertyName("level")] public int Level { get; set; }

    /// <summary>
    /// 官方词条有效性(getRoleDetail mainProps/subProps[].valid,按角色区分)。
    /// <para>实测(2026-10,丽贝卡):暴击/暴击伤害/攻击%/共鸣效率/普攻伤害加成 = true,
    /// 生命%/防御/重击伤害加成 = false —— 以接口为准,不再依赖本地权重表猜。
    /// null = 数据源未给出(旧缓存/其他源),退回权重表。</para>
    /// </summary>
    [JsonPropertyName("valid")] public bool? Valid { get; set; }

    /// <summary>
    /// 有效词条重要度(0-3,副词条色条/文字着色):
    /// 官方 valid=false → 0(灰,无效);valid=true → 核心(暴击/暴伤/攻击%)= 3,
    /// 其余官方认可的有效词条 = 2(青,含权重表未收录的伤害加成类);
    /// 无官方判定 → 旧规则(核心 3,其余 1 —— 2026-10 用户反馈:无官方数据时只亮核心)。
    /// </summary>
    [JsonIgnore]
    public int EffectiveLevel
    {
        get
        {
            var core = McKuro.Core.Services.Roles.EchoRatingService.GetPropLevel(AttributeName, AttributeValue) >= 3;
            return Valid switch
            {
                false => 0,
                true => core ? 3 : 2,
                null => Level > 0 ? Level : (core ? 3 : 1),
            };
        }
    }
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
        // 总评失效 + 通知放在批次最前:先丢弃旧缓存,后续 PhantomData 等通知触发重新取值时
        // 拿到的就是本次合并后的新结果(取值本身由 EchoTotal 惰性完成,见其说明)。
        RefreshEchoRating();
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
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasEchoRating)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasAttributes)));
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

    /// <summary>是否有声骸评级(用于 UI 可见性)。</summary>
    [JsonIgnore]
    public bool HasEchoRating => PhantomData?.Phantoms is { Count: > 0 };

    /// <summary>是否有属性面板数据。</summary>
    public bool HasAttributes => Attributes is { Count: > 0 };

    /// <summary>声骸总评分(5 件得分之和 0-500;惰性计算,见 <see cref="EchoTotal"/>)。</summary>
    [JsonIgnore]
    public double? EchoTotalScore => EchoTotal?.TotalScore;

    /// <summary>声骸总评级文本(未毕业/小毕业/毕业/完美毕业)。</summary>
    [JsonIgnore]
    public string EchoTotalGrade => EchoTotal?.LevelText ?? "";

    /// <summary>声骸总评分文本(如「186.8 分」;模板随语言,见 Roles.EchoTotalScoreFmt)。</summary>
    [JsonIgnore]
    public string EchoTotalScoreText => EchoTotal is { } r
        ? Services.CoreStrings.F("Roles.EchoTotalScoreFmt", "{0:0.0} 分", r.TotalScore)
        : "";

    /// <summary>声骸总评级等级(供徽章/文字配色)。</summary>
    [JsonIgnore]
    public McKuro.Core.Services.Roles.EchoRatingLevel EchoTotalLevel
        => EchoTotal?.Level ?? McKuro.Core.Services.Roles.EchoRatingLevel.None;

    /// <summary>是否已算出声骸总评分(有声骸即成立)。</summary>
    [JsonIgnore]
    public bool HasEchoTotal => EchoTotal is not null;

    /// <summary>
    /// 惰性缓存的声骸总评(无需任何"先调用一次刷新"的前置条件 —— 见下方 <see cref="EchoTotal"/> 说明)。
    /// </summary>
    [JsonIgnore]
    private McKuro.Core.Services.Roles.RoleEchoRating? _echoTotal;

    /// <summary>
    /// 声骸总评(惰性计算 + 缓存)。
    /// <para>
    /// <b>为什么是计算属性而不是"由 RefreshEchoRating 写入的状态"</b>:总评此前只在
    /// <see cref="RefreshEchoRating"/> 里赋值,而该方法只在「在线详情合并 / 攻略填充 / Wiki 权重回填」
    /// 三条路径上被调用 —— 纯缓存加载(构造函数 LoadFromLocal → 选中角色)与"详情已完整即跳过在线刷新"
    /// 都不经过它们,于是 <c>HasEchoTotal</c> 恒为 false,声骸区整段在但<b>总评行不显示</b>
    /// (用户反馈:角色详情的声骸列表缺失声骸总评)。改成惰性计算后,任何入口只要有声骸数据就一定有值;
    /// 与单件声骸的 <see cref="EchoInfo.Rate"/>、<see cref="HasEchoRating"/> 保持同一模式。
    /// </para>
    /// </summary>
    [JsonIgnore]
    private McKuro.Core.Services.Roles.RoleEchoRating? EchoTotal
    {
        get
        {
            if (_echoTotal is null && PhantomData?.Phantoms is { Count: > 0 } phantoms)
            {
                _echoTotal = McKuro.Core.Services.Roles.EchoRatingService.RateRole(phantoms);
            }
            return _echoTotal;
        }
    }

    /// <summary>
    /// 丢弃缓存的总评并通知 UI 重算(5 件均值;声骸数据合并、Wiki 权重回填后调用)。
    /// <para>只负责"失效 + 通知";取值仍由 <see cref="EchoTotal"/> 惰性完成。</para>
    /// </summary>
    public void RefreshEchoRating()
    {
        _echoTotal = null;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EchoTotalScore)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EchoTotalScoreText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EchoTotalGrade)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EchoTotalLevel)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasEchoTotal)));
    }

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
