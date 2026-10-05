using System.Collections.ObjectModel;
using Avalonia.Threading;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using McKuro.Core.Models.Guide;
using McKuro.Core.Models.Roles;
using McKuro.Core.Services.Guide;
using McKuro.Core.Services.Roles;
using McKuro.Services;

namespace McKuro.ViewModels;

/// <summary>角色养成页:原生显示当前账号的角色养成数据。</summary>
public sealed partial class RolesViewModel : ViewModelBase
{
    private readonly IMessenger _messenger;

    /// <summary>mcguide 详情填充进行中(避免并发重复请求)。</summary>
    private bool _guideDetailFilling;

    /// <summary>按需详情请求的取消源(切换角色时取消上一个,保持 getRoleDetail 串行不并发)。</summary>
    private CancellationTokenSource? _detailFetchCts;

    /// <summary>最近一次被 mcguide 填充的角色(其图标是 guide-res B 域名,不写入磁盘缓存)。</summary>
    private RoleDetail? _guideFilledRole;

    /// <summary>
    /// 同步后待强制刷新详情的角色 ID 集合(每次「同步」后重建)。
    /// <para>
    /// 「同步」拉到的列表项会被服务层用本地缓存补全详情(<see cref="RoleDetail.IsDetailComplete"/> 为 true),
    /// 而点选角色的详情请求默认因"详情已完整"跳过——这会让详情永远停在缓存快照(用户实测:同步后数据是旧的)。
    /// 因此同步成功后给本次列表中所有角色登记强制刷新,点选时无视 IsDetailComplete 重取一次,取到后移除标记;
    /// 仅读缓存(登录/切号/页面初始)不登记,保持"零请求"语义。
    /// </para>
    /// </summary>
    private HashSet<int> _pendingDetailRefresh = [];

    /// <summary>属性筛选中的"全部"选项(本地化属性;语言重启生效,进程内取值恒定)。</summary>
    public static string AllAttributeFilter => LanguageService.Format("Roles.AllAttr");

    public static string SortByStar => LanguageService.Format("Roles.SortStar");
    public static string SortByName => LanguageService.Format("Roles.SortName");

    [ObservableProperty]
    private string _statusText = LanguageService.Format("Roles.Ready");

    [ObservableProperty]
    private string _sourceText = LanguageService.Format("Roles.SourceNone");

    [ObservableProperty]
    private string _tokenText = "";

    [ObservableProperty]
    private string _roleIdText = "";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _hasRoles;

    [ObservableProperty]
    private RoleDetail? _selectedRole;

    /// <summary>角色详情头部卡片背景(从角色立绘提取主色生成渐变;参照 WutheringWavesTool ImgColorBgTask)。</summary>
    [ObservableProperty]
    private Avalonia.Media.IBrush? _roleHeaderBackground;

    /// <summary>角色头部文字色(按取色主色亮度自适应:背景亮→深字,背景暗→浅字;参照 WutheringWavesTool GetForegroundColor)。</summary>
    [ObservableProperty]
    private Avalonia.Media.IBrush? _roleNameBrush;

    /// <summary>
    /// 总评级色环(角色名下方;S/SS/SSS 等按官方评级配色,未登录/无数据为空)。
    /// 浅色主题下用深色变体保证白底可见(此前用亮黄/亮青在白底上几乎看不见)。
    /// </summary>
    public Avalonia.Media.IBrush? GradeBadgeBrush
    {
        get
        {
            var dark = ThemeHelper.IsDarkTheme();
            return GuideAchievement?.Grade?.ToUpperInvariant() switch
            {
                "SSS" => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#e33737")),
                "SS" => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(dark ? "#f8f05c" : "#B8860B")),
                "S" => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(dark ? "#bc60f2" : "#8B3FC7")),
                "A" => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(dark ? "#2196f3" : "#1565C0")),
                "B" => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(dark ? "#4caf50" : "#2E7D32")),
                "C" => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(dark ? "#9e9e9e" : "#616161")),
                _ => null,
            };
        }
    }

    /// <summary>是否有总评级可显示。</summary>
    public bool HasGrade => !string.IsNullOrWhiteSpace(GuideAchievement?.Grade);

    /// <summary>mcguide 登录状态文案。</summary>
    [ObservableProperty]
    private string _guideStatusText = "";

    /// <summary>是否已登录 mcguide 攻略站(控制登录表单/达成度区可见性)。</summary>
    [ObservableProperty]
    private bool _guideLoggedIn;

    /// <summary>mcguide 达成度加载中。</summary>
    [ObservableProperty]
    private bool _guideLoading;

    /// <summary>当前选中角色的 mcguide 官方达成度(未加载/未支持时为 null)。</summary>
    [ObservableProperty]
    private GuideIntroductionInfo? _guideAchievement;

    partial void OnGuideAchievementChanged(GuideIntroductionInfo? value)
    {
        // 评级色章派生自 Grade,攻略详情刷新时同步通知
        OnPropertyChanged(nameof(GradeBadgeBrush));
        OnPropertyChanged(nameof(HasGrade));
    }

    /// <summary>是否有 mcguide 达成度可展示。</summary>
    [ObservableProperty]
    private bool _hasGuideAchievement;

    /// <summary>官方推荐建议区(武器/声骸/技能加点/共鸣链推荐;登录攻略站且选中角色有攻略时展示)。</summary>
    public ObservableCollection<GuideRecommendItem> GuideRecommendations { get; } = [];

    /// <summary>是否有官方推荐建议可展示。</summary>
    [ObservableProperty]
    private bool _hasGuideRecommendations;

    // ---- 攻略切换(标题+作者下拉;切换后按攻略 id 重新拉详情并刷新推荐区) ----
    /// <summary>可选攻略列表(点赞降序;含「点赞最高攻略」默认项)。</summary>
    public ObservableCollection<GuideOptionItem> GuideOptions { get; } = [];

    /// <summary>当前选中的攻略(切换触发重新加载)。</summary>
    [ObservableProperty]
    private GuideOptionItem? _selectedGuideOption;

    /// <summary>
    /// 程序化赋值 SelectedGuideOption 时置 true(切角色清空/列表拉取后恢复默认项),
    /// 避免被下面的"用户切换 → 重拉详情"回调二次触发造成重复请求/循环。
    /// </summary>
    private bool _suppressGuideSwitch;

    /// <summary>用户在下拉切换攻略 → 按新攻略 id 重拉详情并刷新推荐区。</summary>
    partial void OnSelectedGuideOptionChanged(GuideOptionItem? value)
    {
        if (_suppressGuideSwitch || value is null)
        {
            return;
        }
        _ = SelectGuideAsync();
    }

    /// <summary>是否有攻略可选(≥1 篇时显示切换下拉)。</summary>
    public bool HasGuideOptions => GuideOptions.Count > 0;

    /// <summary>已解析的推荐共鸣链号(如 [2,4,6];用于共鸣链推荐标识)。</summary>
    [ObservableProperty]
    private IReadOnlyList<int> _recommendedChains = [];

    /// <summary>推荐配装首件声骸名(归一化前原文;声骸卡推荐标识用)。</summary>
    [ObservableProperty]
    private string? _recommendedPhantomName;

    /// <summary>推荐套装名(声骸套装推荐标识用)。</summary>
    [ObservableProperty]
    private string? _recommendedSetName;

    /// <summary>推荐武器名(首选;角色卡武器对比用)。</summary>
    [ObservableProperty]
    private string? _recommendedWeaponName;

    /// <summary>推荐武器图标(角色卡对比展示用)。</summary>
    [ObservableProperty]
    private string? _recommendedWeaponIcon;

    /// <summary>玩家当前武器是否为官方推荐/备选档(null=无攻略数据)。</summary>
    [ObservableProperty]
    private bool? _currentWeaponIsRecommended;

    /// <summary>玩家当前武器档位文本(推荐/备选/有差距)。</summary>
    [ObservableProperty]
    private string _currentWeaponTierText = "";

    /// <summary>攻略标题/作者署名(推荐区头部展示)。</summary>
    [ObservableProperty]
    private string _guideSourceText = "";

    /// <summary>技能加点建议(结构化条目:技能名/目标等级单独着色,见 SkillAdviceEntry)。</summary>
    public ObservableCollection<SkillAdviceEntry> SkillAdviceItems { get; } = [];

    /// <summary>是否有加点建议可展示。</summary>
    public bool HasSkillAdvice => SkillAdviceItems.Count > 0;

    /// <summary>
    /// 是否有加点顺序可展示。
    /// <para>
    /// <b>必须看 SkillPriorityNodes</b>(实际填充的集合):此前误绑到从未填充的
    /// SkillPriorityItems(已删),恒为 false —— 加点顺序行永远不显示(用户反馈)。
    /// </para>
    /// </summary>
    public bool HasSkillPriority => SkillPriorityNodes.Count > 1;

    /// <summary>技能独立卡集合(技能分段):图标/等级来自库街区,达标色来自攻略。</summary>
    public ObservableCollection<SkillCardItem> SkillCardItems { get; } = [];

    /// <summary>主技能节点(常态攻击/共鸣技能/共鸣回路/共鸣解放/变奏技能;弧形错落排布,可点击切演示)。</summary>
    public ObservableCollection<SkillCardItem> PrimarySkillNodes { get; } = [];

    /// <summary>副技能节点(延奏技能/谐度破坏;小图标,不参与演示切换)。</summary>
    public ObservableCollection<SkillCardItem> SecondarySkillNodes { get; } = [];

    /// <summary>是否有副技能节点。</summary>
    public bool HasSecondarySkillNodes => SecondarySkillNodes.Count > 0;

    /// <summary>加点顺序节点(推荐等级降序;带图标)。</summary>
    public ObservableCollection<SkillCardItem> SkillPriorityNodes { get; } = [];

    /// <summary>是否有技能卡可展示。</summary>
    public bool HasSkillCards => SkillCardItems.Count > 0;

    // ---- 技能演示 + 基础连招(同一张卡:左侧演示视频/图标选择,右侧连招文本) ----
    /// <summary>技能演示项(含演示视频 mp4;攻略站 roleSkill.keynoteSkills/fixedSkills)。</summary>
    public ObservableCollection<SkillDemoItem> SkillDemos { get; } = [];

    /// <summary>当前选中的演示项(决定播放哪个视频)。</summary>
    [ObservableProperty]
    private SkillDemoItem? _selectedSkillDemo;

    /// <summary>当前演示视频 URL(绑定 VideoBackgroundControl;空则不播放)。</summary>
    [ObservableProperty]
    private string _currentSkillDemoVideo = "";

    /// <summary>基础连招文本(攻略 role/info texts.skillDisplay)。</summary>
    [ObservableProperty]
    private string _comboText = "";

    /// <summary>角色资料(role/info:技能演示视频 + 角色特点图标;异步加载)。</summary>
    [ObservableProperty]
    private GuideRoleInfoData? _roleInfoData;

    /// <summary>角色特点图标 URL(概览卡横排展示)。</summary>
    public ObservableCollection<string> RoleTraits { get; } = [];

    /// <summary>是否有角色特点图标可展示。</summary>
    public bool HasRoleTraits => RoleTraits.Count > 0;

    /// <summary>是否有技能演示/连招可展示。</summary>
    public bool HasSkillDemo => SkillDemos.Count > 0 || !string.IsNullOrWhiteSpace(ComboText);

    /// <summary>选中演示项变化:切换播放视频 + 刷新技能节点的选中高亮。</summary>
    partial void OnSelectedSkillDemoChanged(SkillDemoItem? value)
    {
        CurrentSkillDemoVideo = value?.VideoUrl ?? "";
        foreach (var node in PrimarySkillNodes)
        {
            node.IsCurrentDemo = value is not null
                && (string.Equals(node.TypeName, value.TypeName, StringComparison.Ordinal)
                    || string.Equals(node.Name, value.Name, StringComparison.Ordinal));
        }
    }

    /// <summary>切换当前播放的技能演示(点击演示图标列表)。</summary>
    [RelayCommand]
    private void SelectSkillDemo(SkillDemoItem? item)
    {
        if (item is not null)
        {
            SelectedSkillDemo = item;
        }
    }

    /// <summary>
    /// 按技能类型切换演示(点击技能卡片节点):找到该类型对应的演示项并播放。
    /// <para>技能节点与演示项按类型名联动(常态攻击/共鸣技能/共鸣回路/共鸣解放/变奏技能);
    /// 找不到对应演示时保持当前演示不动。</para>
    /// </summary>
    [RelayCommand]
    private void SelectSkillDemoByType(string? typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return;
        }
        var match = SkillDemos.FirstOrDefault(d => string.Equals(d.TypeName, typeName, StringComparison.Ordinal))
                    ?? SkillDemos.FirstOrDefault(d => string.Equals(d.Name, typeName, StringComparison.Ordinal));
        if (match is not null)
        {
            SelectedSkillDemo = match;
        }
    }

    // ---- 详情锚点导航(概览/属性/技能/共鸣链/达成度/推荐/声骸) ----
    /// <summary>
    /// 锚点导航项(带攻略达成度标识):标题 + 状态。
    /// 状态语义:完全达标(全绿)/ 部分达标(橙)/ 无数据(无标识)。
    /// 这些状态由攻略数据推导(武器/声骸看攻略 isFinished,属性/技能/共鸣链看逐项达标比例)。
    /// </summary>
    public ObservableCollection<SectionNavItem> DetailSections { get; } = [];

    /// <summary>初始化导航项(构造时一次性;状态随后由 ApplyGuideInfo 刷新)。</summary>
    private void InitializeDetailSections()
    {
        DetailSections.Clear();
        DetailSections.Add(new SectionNavItem { Title = LanguageService.Format("Roles.Section.Overview") });
        DetailSections.Add(new SectionNavItem { Title = LanguageService.Format("Roles.Section.Attributes") });
        DetailSections.Add(new SectionNavItem { Title = LanguageService.Format("Roles.Section.Skills") });
        DetailSections.Add(new SectionNavItem { Title = LanguageService.Format("Roles.Section.Chains") });
        DetailSections.Add(new SectionNavItem { Title = LanguageService.Format("Roles.Section.Echoes") });
        DetailSections.Add(new SectionNavItem { Title = LanguageService.Format("Roles.Section.Recommend") });
        DetailSections.Add(new SectionNavItem { Title = LanguageService.Format("Roles.Section.Teammates") });
    }

    /// <summary>重置全部导航达成度标识(切角色/无攻略)。</summary>
    private void ClearSectionStatuses()
    {
        foreach (var s in DetailSections)
        {
            s.Status = SectionStatus.None;
        }
    }

    /// <summary>
    /// 库街区实时技能等级索引(技能类型名 / 技能名 → 等级)。
    /// <para>
    /// 攻略接口 addPointTarget 自带 currentLevel,但它是<b>服务端快照</b>且被本地缓存 24h,
    /// 玩家在游戏里点完技能后长期滞后 ⇒ "已达标仍提示提升"(用户反馈)。
    /// 库街区 getRoleDetail 的 skillList.level 是权威实时值,故达标判定一律以它为准。
    /// </para>
    /// </summary>
    private static Dictionary<string, int> BuildLiveSkillLevels(RoleDetail? role)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var s in role?.Skills ?? [])
        {
            if (s.Skill is not { } sk)
            {
                continue;
            }
            if (!string.IsNullOrWhiteSpace(sk.Type))
            {
                map[sk.Type] = s.SkillLevel;
            }
            if (!string.IsNullOrWhiteSpace(sk.SkillName))
            {
                map[sk.SkillName] = s.SkillLevel;
            }
        }
        return map;
    }

    /// <summary>取攻略目标对应的库街区实时等级(按技能类型名,回退技能名);未匹配返回 null(回退攻略快照)。</summary>
    private static int? LiveLevelOf(GuideSkillTarget? target, IReadOnlyDictionary<string, int> live)
    {
        if (target is null)
        {
            return null;
        }
        foreach (var key in new[] { target.TypeName, target.Name })
        {
            if (!string.IsNullOrWhiteSpace(key) && live.TryGetValue(key, out var level))
            {
                return level;
            }
        }
        return null;
    }

    /// <summary>
    /// 按攻略数据刷新导航标识:
    /// 属性 / 技能 / 共鸣链 按"逐项达标比例"(全达标=完全,部分=部分);
    /// 武器 / 声骸 按攻略 isFinished(完全达标)/ 有推荐但未达标(部分)。
    /// </summary>
    private void UpdateSectionStatuses(RoleDetail? role, GuideIntroductionInfo? info)
    {
        ClearSectionStatuses();
        if (info is null)
        {
            return;
        }

        // 属性:FinishedCount / TotalCount
        var attr = info.RoleAttribute;
        if (attr is { TotalCount: > 0 })
        {
            SetStatus(1, attr.FinishedCount >= attr.TotalCount ? SectionStatus.Complete
                : attr.FinishedCount > 0 ? SectionStatus.Partial : SectionStatus.None);
        }

        // 技能:逐项达标(addPointTarget 推荐等级;当前等级取库街区实时值)
        var liveLevels = BuildLiveSkillLevels(role);
        var targets = info.RoleSkill?.AddPointTarget ?? [];
        var metCount = targets.Count(t => GuideAchievementService.IsSkillLevelMet(t, LiveLevelOf(t, liveLevels)) == true);
        var judged = targets.Count(t => GuideAchievementService.IsSkillLevelMet(t, LiveLevelOf(t, liveLevels)) is not null);
        if (judged > 0)
        {
            SetStatus(2, metCount >= judged ? SectionStatus.Complete
                : metCount > 0 ? SectionStatus.Partial : SectionStatus.None);
        }

        // 共鸣链:已获取链数 / 总链数
        var res = info.RoleResonance;
        if (res is { TotalCount: > 0 })
        {
            SetStatus(3, res.AcquiredCount >= res.TotalCount ? SectionStatus.Complete
                : res.AcquiredCount > 0 ? SectionStatus.Partial : SectionStatus.None);
        }

        // 声骸:攻略 isFinished 为真 = 完全;有推荐配装但未达成 = 部分(导航项索引 4)
        var echoOk = info.Echo?.IsFinished == true;
        var hasEchoRec = info.Echo?.Main?.EchoProps is not null || info.Echo?.Current?.EchoProps is not null;
        SetStatus(4, echoOk ? SectionStatus.Complete
            : hasEchoRec ? SectionStatus.Partial : SectionStatus.None);

        // 武器:攻略 isFinished 为真 = 完全;有推荐武器但未达成 = 部分(挂在「攻略推荐」导航项,索引 5)
        var weaponOk = info.Weapon?.IsFinished == true;
        var hasWeaponRec = (info.Weapon?.Items?.Count ?? 0) > 0;
        SetStatus(5, weaponOk ? SectionStatus.Complete
            : hasWeaponRec ? SectionStatus.Partial : SectionStatus.None);
    }

    /// <summary>按下标设置导航项状态(越界忽略)。</summary>
    private void SetStatus(int index, SectionStatus status)
    {
        if (index >= 0 && index < DetailSections.Count)
        {
            DetailSections[index].Status = status;
        }
    }

    [ObservableProperty]
    private int _selectedDetailSection;

    // ---- 属性卡折叠(默认 7 项,展开显示全部;参照官方 App 属性面板) ----
    /// <summary>属性卡是否展开(默认折叠只显示前 7 项)。</summary>
    [ObservableProperty]
    private bool _attributesExpanded;

    /// <summary>属性折叠展开/收起按钮文本。</summary>
    public string AttributesToggleText => AttributesExpanded
        ? LanguageService.Format("Roles.AttributesCollapse")
        : LanguageService.Format("Roles.AttributesExpand");

    /// <summary>
    /// 属性项(带官方达标信息):当前值 + 推荐值 + 是否达标 + 还差多少。
    /// UI 直接在数值后就地显示达标状态(用户要求"属性后面直接显示是否达标、还差多少")。
    /// </summary>
    public ObservableCollection<RoleAttributeItem> AttributeItems { get; } = [];

    /// <summary>是否有可折叠的超出部分(属性 >7 项才显示展开按钮)。
    /// 必须按原始 Attributes 数判定:折叠时 AttributeItems 已被裁剪到 7,再按它判就永远不出按钮。</summary>
    public bool HasMoreAttributes => SelectedRole?.Attributes is { Count: > 7 };

    // ---- 队友推荐(攻略站 teammate:主推 + 备选;含武器/声骸/套装/词条,并标注是否未拥有) ----
    /// <summary>队友推荐组(每组 = 主推队友 + 其配装 + 备选队友)。</summary>
    public ObservableCollection<TeammateGroupItem> Teammates { get; } = [];

    /// <summary>是否有队友推荐可展示。</summary>
    public bool HasTeammates => Teammates.Count > 0;

    partial void OnAttributesExpandedChanged(bool value) => OnPropertyChanged(nameof(AttributesToggleText));

    /// <summary>展开/收起属性卡。</summary>
    [RelayCommand]
    private void ToggleAttributes()
    {
        AttributesExpanded = !AttributesExpanded;
        RebuildAttributeItems();
        OnPropertyChanged(nameof(HasMoreAttributes));
    }

    /// <summary>
    /// 重建属性项列表:把攻略 roleAttribute.items 按属性名匹配到库街区属性,
    /// 生成"当前值 + 推荐值 + 达标状态 + 差值"的就地提示。
    /// 折叠时只取前 7 项(展开显示全部)。
    /// </summary>
    private void RebuildAttributeItems()
    {
        AttributeItems.Clear();
        var all = SelectedRole?.Attributes;
        if (all is not { Count: > 0 })
        {
            OnPropertyChanged(nameof(HasMoreAttributes));
            return;
        }

        // 攻略达标项按属性名索引(名称归一化:去空白,兼容「攻击」/「攻击%」写法差异)
        var guideItems = GuideAchievement?.RoleAttribute?.Items ?? [];
        var byName = new Dictionary<string, GuideAttributeItem>(StringComparer.Ordinal);
        foreach (var g in guideItems)
        {
            var key = NormalizeAttrName(g.Name);
            if (key.Length > 0)
            {
                byName[key] = g;
            }
        }

        var shown = AttributesExpanded || all.Count <= 7 ? all : all.Take(7).ToList();
        foreach (var a in shown)
        {
            byName.TryGetValue(NormalizeAttrName(a.AttributeName), out var guide);
            AttributeItems.Add(new RoleAttributeItem
            {
                Name = a.AttributeName,
                CurrentText = a.AttributeValue,
                IconUrl = a.IconUrl,
                RecommendText = guide?.RecommendAmount ?? "",
                IsMet = guide?.IsFinished,
                // 差值:当前 - 推荐(仅数值型可算;算不出就不显示差值徽章)
                DeltaText = ComputeDeltaText(a.AttributeValue, guide?.RecommendAmount, guide?.IsFinished),
            });
        }
        OnPropertyChanged(nameof(HasMoreAttributes));
    }

    /// <summary>属性名归一化(去空白/百分号,便于攻略与库街区名称对齐)。</summary>
    private static string NormalizeAttrName(string? name)
        => (name ?? "").Replace(" ", "").Replace("%", "").Replace("％", "").Trim();

    /// <summary>
    /// 计算差值文本:已达标注"已达标";未达标且两侧都能解析出数值时显示"差 X"。
    /// 无法解析(纯文本值)时返回空,UI 只显示达标状态。
    /// </summary>
    private static string ComputeDeltaText(string? current, string? recommend, bool? isMet)
    {
        if (isMet == true)
        {
            return LanguageService.Format("Roles.Guide.StateMatched");
        }
        if (isMet is null)
        {
            return "";
        }
        var cur = ParseAmount(current);
        var rec = ParseAmount(recommend);
        if (cur is null || rec is null)
        {
            return LanguageService.Format("Roles.Guide.StateDiff");
        }
        var delta = rec.Value - cur.Value;
        var suffix = (current ?? "").Contains('%') || (recommend ?? "").Contains('%') ? "%" : "";
        return LanguageService.Format("Roles.Guide.DeltaShort", $"{delta:0.#}{suffix}");
    }

    /// <summary>从「326.0%」「150」这类文本解析数值(带百分号也返回纯数字)。</summary>
    private static double? ParseAmount(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }
        var cleaned = text.Replace("%", "").Replace("％", "").Trim();
        return double.TryParse(cleaned, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    /// <summary>
    /// 属性筛选(多选):除"全部属性"外的所有选项,每项含名称与本地属性图标路径。
    /// 勾选"全部属性"时清空其他选择;勾选任一具体属性时自动取消"全部属性"。
    /// </summary>
    public ObservableCollection<AttributeFilterOption> AttributeFilterOptions { get; } = [];

    /// <summary>筛选按钮上的摘要文本(如「全部属性」/「导电、衍射」/「导电 等 3 项」)。</summary>
    public string AttributeFilterSummary
    {
        get
        {
            var picked = AttributeFilterOptions.Where(o => o.IsSelected && !o.IsAll).Select(o => o.Name).ToList();
            if (picked.Count == 0 || AttributeFilterOptions.FirstOrDefault(o => o.IsAll)?.IsSelected == true)
            {
                return AllAttributeFilter;
            }
            return picked.Count <= 2
                ? string.Join("、", picked)
                : LanguageService.Format("Roles.AttrFilterMore", picked[0], picked.Count);
        }
    }

    /// <summary>属性多选状态变化:刷新摘要与列表。</summary>
    internal void OnAttributeFilterOptionChanged(AttributeFilterOption changed)
    {
        // "全部属性"是互斥项:勾上它清空其他;勾任一具体项则取消它
        if (changed.IsAll && changed.IsSelected)
        {
            foreach (var o in AttributeFilterOptions.Where(o => !o.IsAll && o.IsSelected))
            {
                o.IsSelected = false;
            }
        }
        else if (!changed.IsAll && changed.IsSelected)
        {
            var all = AttributeFilterOptions.FirstOrDefault(o => o.IsAll);
            if (all is not null && all.IsSelected)
            {
                all.IsSelected = false;
            }
        }
        // 全部取消勾选时回落到"全部属性",避免出现空筛选(列表空)
        if (!AttributeFilterOptions.Any(o => o.IsSelected))
        {
            var all = AttributeFilterOptions.FirstOrDefault(o => o.IsAll);
            if (all is not null)
            {
                all.IsSelected = true;
            }
        }
        OnPropertyChanged(nameof(AttributeFilterSummary));
        RebuildFilteredRoles();
    }

    [ObservableProperty]
    private string _selectedSort = SortByStar;

    /// <summary>全部角色(数据源)。</summary>
    public ObservableCollection<RoleDetail> Roles { get; } = [];

    /// <summary>过滤+排序后的显示集合(View 绑定此集合)。</summary>
    public ObservableCollection<RoleDetail> FilteredRoles { get; } = [];

    public IReadOnlyList<string> SortOptions { get; } = [SortByStar, SortByName];

    public RolesViewModel(IMessenger? messenger = null)
    {
        _messenger = messenger ?? WeakReferenceMessenger.Default;
        TokenText = AppServices.Settings.Current.KujiequToken;
        RoleIdText = AppServices.Settings.Current.RoleId;
        GuideLoggedIn = AppServices.Guide.HasToken;
        GuideStatusText = AppServices.Guide.HasToken
            ? LanguageService.Format("Roles.GuideLoggedIn", AppServices.Settings.Current.GuideCName)
            : LanguageService.Format("Roles.GuideNotLoggedIn");
        // 详情导航项(带达成度标识;状态由攻略数据刷新)
        InitializeDetailSections();
        // 默认加载本地缓存,不自动请求库街区(频繁访问易触发账号风控);
        // 在线获取分两层:「同步」按钮只拉角色列表,角色详情在选中角色时按需单发
        LoadFromLocal();

        // 登录/切号后自动同步 → 仅读缓存:在线列表获取依赖用户点击「同步」,详情按选择角色时拉取
        _messenger.Register<RolesViewModel, RolesRefreshRequestedMessage>(this, static (recipient, message) =>
        {
            recipient.TokenText = AppServices.Settings.Current.KujiequToken;
            recipient.RoleIdText = AppServices.Settings.Current.RoleId;
            recipient.LoadFromLocal();
        });
    }

    /// <summary>当前库街区账号 ID(用于校验缓存归属;未登录为空)。</summary>
    private static string CurrentAccountId => AppServices.KuroAccounts.Current?.UserId ?? "";

    partial void OnTokenTextChanged(string value) => AppServices.Settings.Current.KujiequToken = value;

    partial void OnRoleIdTextChanged(string value) => AppServices.Settings.Current.RoleId = value;

    /// <summary>选中角色变化时:按需拉取库街区详情 + 官方达成度 + 角色头部背景取色 + 缺失详情 mcguide 填充 + 图标缓存。</summary>
    partial void OnSelectedRoleChanged(RoleDetail? value)
    {
        GuideAchievement = null;
        HasGuideAchievement = false;
        ClearGuideRecommendations();
        GuideOptions.Clear();
        OnPropertyChanged(nameof(HasGuideOptions));
        _suppressGuideSwitch = true;
        try
        {
            SelectedGuideOption = null;
        }
        finally
        {
            _suppressGuideSwitch = false;
        }
        RecommendedChains = [];
        RecommendedPhantomName = null;
        RecommendedSetName = null;
        RecommendedWeaponName = null;
        RecommendedWeaponIcon = null;
        CurrentWeaponIsRecommended = null;
        CurrentWeaponTierText = "";
        GuideSourceText = "";
        SkillAdviceItems.Clear();
        OnPropertyChanged(nameof(HasSkillAdvice));
        OnPropertyChanged(nameof(HasSkillPriority));
        // 导航达成度标识随角色清空(攻略数据到达后由 ApplyGuideInfo 刷新)
        ClearSectionStatuses();
        // 技能卡/属性折叠随角色重置:技能卡先用本地已加载的详情立即构建(异步数据到达后再刷新)
        if (value is not null)
        {
            // 声骸 Wiki 词条权重回填也要覆盖"纯缓存加载"的角色:
            // 详情已完整时 LoadRoleDetailFromKujiequAsync 会直接返回(不合并 ⇒ 不触发回填),
            // 那样评级只能退回官方 valid/通用权重,与攻略站口径不符。按角色名缓存,重复调用无额外开销。
            QueueEchoWeightsLoad(value);
            BuildSkillCards(value, null);
            // 加点顺序节点也要立即清空:否则新角色攻略数据到达前,页面残留上一个角色的"加点顺序"
            SkillPriorityNodes.Clear();
        }
        else
        {
            SkillCardItems.Clear();
            PrimarySkillNodes.Clear();
            SecondarySkillNodes.Clear();
            SkillPriorityNodes.Clear();
        }
        // 角色资料(演示视频/特点图标)随角色清空,由 LoadRoleInfoAsync 异步填充
        RoleInfoData = null;
        BuildSkillDemo();
        AttributesExpanded = false;
        RebuildAttributeItems();
        OnPropertyChanged(nameof(HasMoreAttributes));
        if (value is not null)
        {
            _ = LoadRoleDetailFromKujiequAsync(value);
            _ = LoadGuideAchievementAsync();
            _ = LoadRoleInfoAsync(value);
            _ = LoadRoleHeaderBackgroundAsync();
            _ = FillRoleDetailFromGuideIfEmptyAsync();
            _ = CacheSelectedRoleIconsAsync();
        }
    }

    /// <summary>后台把当前选中角色的图标缓存到磁盘(库街区正常时);mcguide 兜底填充的角色不缓存。</summary>
    private async Task CacheSelectedRoleIconsAsync()
    {
        var role = SelectedRole;
        if (role is null)
        {
            return;
        }
        // mcguide 兜底填充的角色图标是 guide-res B 域名,不写入磁盘缓存(避免污染库街区 A 域名缓存)
        if (ReferenceEquals(role, _guideFilledRole))
        {
            return;
        }
        try
        {
            await AppServices.IconCache.CacheRoleIconsAsync(role);
        }
        catch (Exception)
        {
            // 缓存失败静默,不影响主流程
        }
    }

    /// <summary>从角色立绘提取主色,生成头部卡片渐变背景(参照 WutheringWavesTool ImgColorBgTask 的 ColorThief 取色)。</summary>
    private async Task LoadRoleHeaderBackgroundAsync()
    {
        var role = SelectedRole;
        var url = role?.Role?.RolePicUrl;
        if (role is null || string.IsNullOrWhiteSpace(url))
        {
            RoleHeaderBackground = null;
            RoleNameBrush = null;
            return;
        }
        try
        {
            // 复用一个轻量 HttpClient 下载图片并解码(AOT 安全)
            var bitmap = await AppServices.Http.GetByteArrayAsync(url);
            if (bitmap.Length == 0)
            {
                return;
            }
            using var ms = new System.IO.MemoryStream(bitmap, writable: false);
            var bmp = new Avalonia.Media.Imaging.Bitmap(ms);
            var colors = ColorThiefHelper.GetDominantColors(bmp, 2);
            if (role != SelectedRole)
            {
                return; // 已切换角色,丢弃过期结果
            }
            if (colors.Count >= 1)
            {
                // 右下角放射渐变:主色 → 两个过渡色 → 白色(主色只集中在右下,过渡柔和)
                var main = colors[0];
                var white = Avalonia.Media.Colors.White;
                RoleHeaderBackground = new Avalonia.Media.RadialGradientBrush
                {
                    Center = new Avalonia.RelativePoint(1, 1, Avalonia.RelativeUnit.Relative),
                    GradientOrigin = new Avalonia.RelativePoint(1, 1, Avalonia.RelativeUnit.Relative),
                    RadiusX = new Avalonia.RelativeScalar(1.2, Avalonia.RelativeUnit.Relative),
                    RadiusY = new Avalonia.RelativeScalar(1.2, Avalonia.RelativeUnit.Relative),
                    GradientStops =
                    {
                        new Avalonia.Media.GradientStop(main, 0),
                        new Avalonia.Media.GradientStop(Mix(main, white, 1.0 / 3.0), 0.33),
                        new Avalonia.Media.GradientStop(Mix(main, white, 2.0 / 3.0), 0.66),
                        new Avalonia.Media.GradientStop(white, 1),
                    },
                };
                // 前景文字按渐变主色(右下)亮度决定,保证右下主色区文字可读
                RoleNameBrush = ForegroundFor(main);
            }
        }
        catch (Exception)
        {
            // 取色失败:保留默认背景,不影响主流程
        }
    }

    /// <summary>线性插值两个颜色(t=0→a,t=1→b)。</summary>
    private static Avalonia.Media.Color Mix(Avalonia.Media.Color a, Avalonia.Media.Color b, double t)
    {
        byte L(byte x, byte y) => (byte)Math.Clamp((int)Math.Round(x + (y - x) * t), 0, 255);
        return Avalonia.Media.Color.FromRgb(L(a.R, b.R), L(a.G, b.G), L(a.B, b.B));
    }

    /// <summary>按主色亮度决定前景文字色(参照 WutheringWavesTool GetForegroundColor:亮度→深/浅字)。</summary>
    private static Avalonia.Media.IBrush ForegroundFor(Avalonia.Media.Color bg)
    {
        double luminance = (bg.R * 0.299 + bg.G * 0.587 + bg.B * 0.114) / 255.0;
        return luminance > 0.5
            ? new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#1a1a1a"))   // 背景亮:深字
            : new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#f5f5f5"));  // 背景暗:浅字
    }

    /// <summary>
    /// 导航到角色页时调用:当前选中角色尚无攻略站数据时按需补拉。
    /// <para>
    /// 页面 VM 只在启动时构造一次,初始化选中的角色可能在攻略站登录/token 就绪前就被选中,
    /// 那次 <see cref="LoadGuideAchievementAsync"/> 会因未登录直接返回,此后不再触发 ——
    /// 表现为"必须点同步才有攻略数据"。进入页面时补一次即可,已有数据则不重复请求。
    /// </para>
    /// </summary>
    public void OnNavigatedTo()
    {
        if (SelectedRole is null)
        {
            return;
        }
        // 已有攻略数据(达成度/推荐/攻略下拉任一)则不重复拉取。
        // 注意不能用 SkillCardItems:技能卡在库街区详情到达时就已构建(无攻略也为真),会导致永不补拉。
        if (HasGuideAchievement || HasGuideRecommendations || GuideOptions.Count > 0
            || !AppServices.Guide.HasToken)
        {
            return;
        }
        _ = LoadGuideAchievementAsync();
    }

    /// <summary>
    /// 拉取角色资料(role/info):技能演示视频(5 个)+ 角色特点图标。
    /// <para>
    /// 与攻略详情分开拉:该接口提供全部技能视频(introduction/info 只有 1 个),
    /// 但哪怕未登录攻略站也应尽量展示(登录才有数据,失败静默不影响主流程)。
    /// </para>
    /// </summary>
    private async Task LoadRoleInfoAsync(RoleDetail role)
    {
        if (!AppServices.Guide.HasToken)
        {
            return;
        }
        var cardRoleId = role.Role?.RoleId ?? 0;
        if (cardRoleId <= 0)
        {
            return;
        }
        try
        {
            var data = await AppServices.Guide.GetRoleInfoDataAsync(role.RoleName, cardRoleId);
            if (role != SelectedRole)
            {
                return; // 已切角色,丢弃过期结果
            }
            RoleInfoData = data;
            BuildSkillDemo();
        }
        catch (Exception)
        {
            // 拉取失败静默:技能演示/特点图标是增强信息
        }
    }

    /// <summary>
    /// 拉取当前选中角色的 mcguide 官方达成度与攻略列表(默认点赞最高)。
    /// </summary>
    private async Task LoadGuideAchievementAsync()
    {
        var role = SelectedRole;
        if (role is null)
        {
            return;
        }
        if (!AppServices.Guide.HasToken)
        {
            GuideStatusText = LanguageService.Format("Roles.GuideNotLoggedInHint");
            return;
        }

        var cardRoleId = role.Role?.RoleId ?? 0;
        if (cardRoleId <= 0)
        {
            GuideStatusText = LanguageService.Format("Roles.GuideNoCardRoleId", role.RoleName);
            return;
        }

        // 每次拉取分配自增序号:快速切角色时旧请求不阻塞新请求(旧结果靠序号 + 角色引用双重校验丢弃)。
        // 此前用 GuideLoading 做全局互斥,连续切换会把新角色的请求直接丢掉(表现为"攻略数据不出现")。
        var requestId = ++_guideRequestId;
        GuideLoading = true;
        GuideStatusText = LanguageService.Format("Roles.GuideFetching", role.RoleName);
        BeginGuideFetch(cardRoleId);
        try
        {
            // 1. 攻略列表(点赞降序) → 填充切换下拉
            var list = await AppServices.Guide.GetIntroductionListAsync(role.RoleName, cardRoleId);
            if (role != SelectedRole || requestId != _guideRequestId)
            {
                return;
            }
            GuideOptions.Clear();
            foreach (var item in list)
            {
                GuideOptions.Add(new GuideOptionItem
                {
                    Id = item.Id,
                    Title = item.Texts?.FirstOrDefault(t => t.Language == "zh-Hans")?.IntroductionName ?? $"#{item.Id}",
                    Author = item.Texts?.FirstOrDefault(t => t.Language == "zh-Hans")?.IntroductionSource ?? "",
                    LikeCount = item.LikeCount,
                });
            }
            OnPropertyChanged(nameof(HasGuideOptions));

            // 2. 默认加载攻略详情:优先恢复用户上次选中的那篇(缓存里有 selected_id),否则取点赞最高
            var cachedEntry = AppServices.Guide.TryGetCachedGuide(cardRoleId);
            var targetId = cachedEntry is { SelectedId: > 0 }
                && list.Any(i => i.Id == cachedEntry.SelectedId)
                ? cachedEntry.SelectedId
                : list.FirstOrDefault()?.Id ?? 0;
            var info = targetId > 0
                ? await AppServices.Guide.GetAchievementByIdAsync(role.RoleName, cardRoleId, targetId)
                : (cachedEntry?.Detail is { } cachedDetail && list.Count == 0 ? cachedDetail : null);
            if (role != SelectedRole || requestId != _guideRequestId)
            {
                return;
            }
            // 恢复"上次选中的攻略/默认点赞最高"是程序化赋值,抑制回调避免刚拉完又重拉一遍
            _suppressGuideSwitch = true;
            try
            {
                SelectedGuideOption = GuideOptions.FirstOrDefault(o => o.Id == targetId) ?? GuideOptions.FirstOrDefault();
            }
            finally
            {
                _suppressGuideSwitch = false;
            }
            ApplyGuideInfo(role, info);
            // 缓存回写已收敛到 GuideAchievementService:列表/详情各自拉成功后统一落盘,
            // 调用方不再手工回写(避免"列表成功/详情失败"时把好详情覆盖成空)。
        }
        catch (Exception ex)
        {
            if (requestId == _guideRequestId)
            {
                GuideStatusText = LanguageService.Format("Roles.GuideFetchFailed", ex.Message);
            }
        }
        finally
        {
            EndGuideFetch(cardRoleId);
            // 只有最新请求能清加载态(旧请求结束时新请求可能还在跑)
            if (requestId == _guideRequestId)
            {
                GuideLoading = false;
            }
        }
    }

    /// <summary>攻略拉取请求序号(丢弃过期响应;见 LoadGuideAchievementAsync)。</summary>
    private int _guideRequestId;

    /// <summary>
    /// 正在拉取攻略的 cardRoleId 集合(按需拉取登记;后台预取据此跳过同一角色,
    /// 避免同步后自动选中首个新角色时预取与按需并发重复请求同一 cardRoleId)。
    /// </summary>
    private readonly HashSet<int> _guideInFlight = [];

    private void BeginGuideFetch(int cardRoleId)
    {
        lock (_guideInFlight)
        {
            _guideInFlight.Add(cardRoleId);
        }
    }

    private void EndGuideFetch(int cardRoleId)
    {
        lock (_guideInFlight)
        {
            _guideInFlight.Remove(cardRoleId);
        }
    }

    private bool IsGuideFetchInFlight(int cardRoleId)
    {
        lock (_guideInFlight)
        {
            return _guideInFlight.Contains(cardRoleId);
        }
    }

    /// <summary>切换攻略(标题+作者下拉选中变化):按攻略 id 重新拉详情并刷新推荐区。</summary>
    [RelayCommand]
    private async Task SelectGuideAsync()
    {
        var role = SelectedRole;
        var option = SelectedGuideOption;
        if (role is null || option is null)
        {
            return;
        }
        var cardRoleId = role.Role?.RoleId ?? 0;
        if (cardRoleId <= 0 || !AppServices.Guide.HasToken)
        {
            return;
        }

        // 与列表拉取共用序号:连续切换只保留最后一次结果
        var requestId = ++_guideRequestId;
        GuideLoading = true;
        try
        {
            var info = await AppServices.Guide.GetAchievementByIdAsync(role.RoleName, cardRoleId, option.Id);
            if (role != SelectedRole || option != SelectedGuideOption || requestId != _guideRequestId)
            {
                return; // 已切角色/又切了别的攻略,丢弃过期结果
            }
            ApplyGuideInfo(role, info);
            // "记住用户选的这篇攻略"由 GetAchievementByIdAsync 成功回写时携带 selectedId 完成,无需再手工回写
        }
        catch (Exception ex)
        {
            if (requestId == _guideRequestId)
            {
                GuideStatusText = LanguageService.Format("Roles.GuideFetchFailed", ex.Message);
            }
        }
        finally
        {
            if (requestId == _guideRequestId)
            {
                GuideLoading = false;
            }
        }
    }

    /// <summary>应用攻略详情:达成度 + 推荐区 + 推荐链号/推荐声骸/推荐武器/技能达标集合 + 署名。</summary>
    private void ApplyGuideInfo(RoleDetail role, GuideIntroductionInfo? info)
    {
        GuideAchievement = info;
        HasGuideAchievement = info is not null;
        GuideStatusText = info is null ? LanguageService.Format("Roles.GuideNoData") : LanguageService.Format("Roles.GuideGrade", info.Grade ?? "-");
        BuildGuideRecommendations(role, info);
        BuildTeammates(info);
        // 导航项达成度标识(完全达标/部分达标)
        UpdateSectionStatuses(role, info);

        if (info is null)
        {
            RecommendedChains = [];
            RecommendedPhantomName = null;
            RecommendedSetName = null;
            RecommendedWeaponName = null;
            RecommendedWeaponIcon = null;
            CurrentWeaponIsRecommended = null;
            CurrentWeaponTierText = "";
            GuideSourceText = "";
            // 无攻略也构建技能卡(只显示图标/等级,无达标徽章)
            // 演示/连招来自 role/info(独立数据源),不因攻略缺失而清空
            BuildSkillCards(role, null);
            BuildSkillPriority();
            OnPropertyChanged(nameof(HasSkillPriority));
            RebuildAttributeItems();  // 无攻略:属性只显示当前值(无达标信息)
            return;
        }

        // 推荐链号(供共鸣链卡推荐标识)
        var chainRec = info.RoleResonanceTexts?.FirstOrDefault(t => t.Language == "zh-Hans")?.RecommendDescription
            ?? info.RoleResonanceTexts?.FirstOrDefault()?.RecommendDescription;
        RecommendedChains = GuideAchievementService.ParseRecommendedChains(chainRec);

        // 共鸣链图标:库街区 chainList 无图标,用攻略 pictureUrl 按链序号回补
        // (库街区详情可能先到、攻略后到,这里覆盖"攻略后到"的方向)
        MergeGuideChains(role, info);

        // 推荐声骸/套装(echo.main 优先,其次 spare/current;供声骸卡推荐标识)
        var build = info.Echo?.Main ?? info.Echo?.Spare ?? info.Echo?.Current;
        RecommendedPhantomName = build?.EchoProps?.Name;
        RecommendedSetName = build?.EchoSetEffects?.FirstOrDefault()?.Name;

        // 回填声骸推荐标识:
        // ① 推荐声骸:只对 4C(COST 4,主声骸)判定 —— 4C 才是决定套装/技能的核心位,
        //    1C/3C 都是填充位,给它们打"推荐"没有意义(用户要求只判 4C)。
        // ② 推荐套装:套装名命中的所有件都标(套装本身是全件生效的)。
        var phantoms = role.PhantomData?.Phantoms;
        if (phantoms is { Count: > 0 })
        {
            var recSetNorm = RecommendedSetName?.Replace(" ", "") ?? "";
            foreach (var echo in phantoms)
            {
                echo.IsRecommendedPhantom = echo.Cost == 4
                    && GuideAchievementService.IsRecommendedPhantom(echo.PhantomName, info.Echo);
                echo.IsRecommendedSet = recSetNorm.Length > 0
                    && string.Equals(echo.FetterName?.Replace(" ", ""), recSetNorm, StringComparison.Ordinal);
            }
        }

        // 推荐武器档位(角色卡武器对比):攻略与库街区详情到达顺序不定,双方各自到达后都要重算
        RefreshWeaponVerdict(role, info);

        // 加点建议(未达标项逐条「建议提升至 N 级」;结构化条目,技能名/等级单独着色):
        // 本地化模板按 {0}/{1} 占位符拆三段,句子结构仍随语言走。
        // 达标判定用库街区实时技能等级(攻略快照 currentLevel 会滞后 ⇒ 已点满仍报"建议提升")。
        SkillAdviceItems.Clear();
        var liveLevels = BuildLiveSkillLevels(role);
        var adviceFmt = LanguageService.Get("Roles.Guide.SkillAdvice");
        var p0 = adviceFmt.IndexOf("{0}", StringComparison.Ordinal);
        var p1 = adviceFmt.IndexOf("{1}", StringComparison.Ordinal);
        var advicePrefix = p0 >= 0 ? adviceFmt[..p0] : "";
        var adviceMiddle = p0 >= 0 && p1 > p0 ? adviceFmt[(p0 + 3)..p1] : "";
        var adviceSuffix = p1 >= 0 ? adviceFmt[(p1 + 3)..] : "";
        foreach (var t in info.RoleSkill?.AddPointTarget ?? [])
        {
            var name = t.TypeName ?? t.Name ?? "";
            var rec = t.RecommendLevelValue;
            var current = LiveLevelOf(t, liveLevels) ?? t.CurrentLevelValue;
            if (rec > 0 && current < rec && !string.IsNullOrWhiteSpace(name))
            {
                SkillAdviceItems.Add(new SkillAdviceEntry
                {
                    Prefix = advicePrefix,
                    Name = name,
                    Middle = adviceMiddle,
                    Level = rec,
                    Suffix = adviceSuffix,
                    Fallback = LanguageService.Format("Roles.Guide.SkillAdvice", name, rec),
                });
            }
        }
        OnPropertyChanged(nameof(HasSkillAdvice));

        BuildSkillCards(role, info);
        // 演示/连招来自 role/info(独立数据源),此处只重建技能卡、加点顺序与属性达标
        BuildSkillPriority();
        OnPropertyChanged(nameof(HasSkillPriority));
        RebuildAttributeItems();  // 攻略到达后:属性就地显示达标状态与差值

        // 攻略署名(标题 · 作者)
        var author = info.BaseTexts?.FirstOrDefault(t => t.Language == "zh-Hans")?.IntroductionSource ?? "";
        var title = SelectedGuideOption?.Title ?? "";
        GuideSourceText = string.IsNullOrWhiteSpace(author) ? title : $"{title} · {author}";
    }

    /// <summary>
    /// 构建技能演示 + 基础连招(技能分段内的同一张卡)。
    /// <para>
    /// 演示项(含 5 个技能视频)来自 <c>role/info</c>(<see cref="RoleInfoData"/>):
    /// introduction/info 只带 keynoteSkills 的 1 个视频,不足以撑起演示列表(用户反馈"只有首个有视频")。
    /// 基础连招来自 role/info 的 texts.skillDisplay。
    /// </para>
    /// </summary>
    private void BuildSkillDemo()
    {
        SkillDemos.Clear();
        SelectedSkillDemo = null;
        CurrentSkillDemoVideo = "";
        ComboText = RoleInfoData?.Texts?.FirstOrDefault(t => t.Language == "zh-Hans")?.SkillDisplay ?? "";

        foreach (var s in RoleInfoData?.Skills ?? [])
        {
            var name = s.Name ?? s.TypeName ?? "";
            if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(s.PictureUrl))
            {
                continue;
            }
            SkillDemos.Add(new SkillDemoItem
            {
                Name = string.IsNullOrWhiteSpace(name) ? (s.TypeName ?? "") : name,
                TypeName = s.TypeName ?? "",
                IconUrl = s.PictureUrl ?? "",
                VideoUrl = s.VideoUrl ?? "",
                Description = s.Description ?? "",
            });
        }

        // 角色特点图标(概览卡横排;role/info.rolePlays)
        RoleTraits.Clear();
        foreach (var t in RoleInfoData?.RolePlays ?? [])
        {
            if (!string.IsNullOrWhiteSpace(t.PictureUrl))
            {
                RoleTraits.Add(t.PictureUrl!);
            }
        }
        OnPropertyChanged(nameof(HasRoleTraits));

        // 默认选中第一个带视频的演示(进入角色即可看到播放)
        SelectedSkillDemo = SkillDemos.FirstOrDefault(d => d.HasVideo) ?? SkillDemos.FirstOrDefault();
        OnPropertyChanged(nameof(HasSkillDemo));
    }

    /// <summary>
    /// 构建技能卡集合(技能分段用):技能图标/等级来自库街区已加载详情,
    /// 攻略推荐等级按技能类型名匹配 addPointTarget → 达标色框(绿=达标/橙=未达)。
    /// 未登录攻略站时只显示技能图标与等级(无徽章)。
    /// </summary>
    private void BuildSkillCards(RoleDetail role, GuideIntroductionInfo? info)
    {
        // 三个集合必须一起清空:此前只清 SkillCardItems,漏清节点集合,
        // 导致每次重建都往 Primary/SecondarySkillNodes 追加 → 技能图标成倍重复(用户反馈)。
        SkillCardItems.Clear();
        PrimarySkillNodes.Clear();
        SecondarySkillNodes.Clear();
        var targets = info?.RoleSkill?.AddPointTarget;
        // 攻略推荐按技能类型名索引(常态攻击/共鸣技能/共鸣回路/共鸣解放/变奏技能)
        var byType = new Dictionary<string, GuideSkillTarget>(StringComparer.Ordinal);
        foreach (var t in targets ?? [])
        {
            var key = t.TypeName ?? t.Name ?? "";
            if (!string.IsNullOrWhiteSpace(key))
            {
                byType[key] = t;
            }
        }

        foreach (var skill in role.Skills ?? [])
        {
            var typeName = skill.Skill?.Type ?? "";
            var name = skill.Skill?.SkillName ?? typeName;
            GuideSkillTarget? target = null;
            if (!string.IsNullOrWhiteSpace(typeName))
            {
                byType.TryGetValue(typeName, out target);
            }
            // 类型名对不上时按技能名兜底匹配
            target ??= (targets ?? []).FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.Ordinal));

            // 本技能当前等级就是库街区实时值;达标判定与 "当前/推荐" 文本都以它为准
            // (攻略快照会滞后,否则徽章数字与旁边的提升建议自相矛盾)。
            var met = target is null ? null : GuideAchievementService.IsSkillLevelMet(target, skill.SkillLevel);
            var item = new SkillCardItem
            {
                Name = string.IsNullOrWhiteSpace(name) ? typeName : name,
                TypeName = typeName,
                LevelText = $"Lv.{skill.SkillLevel}",
                IconUrl = skill.Skill?.IconUrl ?? "",
                HasRecommendation = met is not null,
                IsMet = met == true,
                RecommendText = target is null ? "" : $"{skill.SkillLevel}/{target.RecommendLevelValue}",
            };
            SkillCardItems.Add(item);

            // 分组:主节点(攻略会点评的 5 个战斗技能)进弧线;其余(延奏/谐度破坏)进副节点小图标
            if (PrimarySkillTypes.Contains(typeName))
            {
                PrimarySkillNodes.Add(item);
            }
            else
            {
                SecondarySkillNodes.Add(item);
            }
        }
        // 主节点按攻略站固定顺序排布(常态攻击→共鸣技能→共鸣回路→共鸣解放→变奏技能)
        SortPrimaryNodes();

        OnPropertyChanged(nameof(HasSkillCards));
        OnPropertyChanged(nameof(HasSecondarySkillNodes));
    }

    /// <summary>主技能类型(攻略站弧形排布的 5 个战斗技能,固定顺序)。</summary>
    private static readonly string[] PrimarySkillTypes =
        ["常态攻击", "共鸣技能", "共鸣回路", "共鸣解放", "变奏技能"];

    /// <summary>按 <see cref="PrimarySkillTypes"/> 的固定顺序重排主节点(保证弧形位置稳定)。</summary>
    private void SortPrimaryNodes()
    {
        var ordered = PrimarySkillNodes
            .OrderBy(n => Array.IndexOf(PrimarySkillTypes, n.TypeName) is var i && i >= 0 ? i : int.MaxValue)
            .ToList();
        PrimarySkillNodes.Clear();
        foreach (var n in ordered)
        {
            PrimarySkillNodes.Add(n);
        }
    }

    /// <summary>
    /// 构建加点顺序节点(推荐等级降序;带图标,并列时未达标排前)。
    /// 数据源为已在 <see cref="BuildSkillCards"/> 建好的技能卡 + 攻略推荐等级。
    /// </summary>
    private void BuildSkillPriority()
    {
        SkillPriorityNodes.Clear();
        var rated = PrimarySkillNodes
            .Where(n => n.HasRecommendation && !string.IsNullOrWhiteSpace(n.RecommendText))
            .Select(n =>
            {
                // RecommendText 形如 "当前/推荐"
                var parts = n.RecommendText.Split('/');
                var rec = parts.Length == 2 && int.TryParse(parts[1], out var r) ? r : 0;
                return (Node: n, Recommend: rec);
            })
            .Where(x => x.Recommend > 0)
            .OrderByDescending(x => x.Recommend)
            .ThenBy(x => x.Node.IsMet)
            .Select(x => x.Node)
            .ToList();
        // 尾项标记:模板据此隐藏末尾的「›」分隔符(用户反馈尾部多出一个箭头)。
        // 同一批节点实例可能残留上一次的 IsLast,先全部复位再标记最后一项。
        foreach (var n in PrimarySkillNodes)
        {
            n.IsLast = false;
        }
        if (rated.Count > 0)
        {
            rated[^1].IsLast = true;
        }
        foreach (var n in rated)
        {
            SkillPriorityNodes.Add(n);
        }
    }

    /// <summary>
    /// 构建队友推荐(攻略站 teammate.items):主推队友 + 推荐武器/声骸/套装/词条 + 备选队友。
    /// 「未拥有」直接取接口 isAcquired(实测 teammate 的 main/spares 都带该字段,无需交叉比对)。
    /// </summary>
    private void BuildTeammates(GuideIntroductionInfo? info)
    {
        Teammates.Clear();
        foreach (var t in info?.Teammate?.Items ?? [])
        {
            var main = t.Main;
            var spares = (t.Spares ?? [])
                .Select(s => new TeammateRefItem
                {
                    Name = s.Name ?? "",
                    IconUrl = s.CardPictureUrl ?? "",
                    NotOwned = s.IsNotOwned,
                })
                .Where(s => !string.IsNullOrWhiteSpace(s.Name) || !string.IsNullOrWhiteSpace(s.IconUrl))
                .ToList();

            var props = (t.EchoAttributes ?? [])
                .Select(a => new TeammatePropItem
                {
                    Cost = a.Cost,
                    Name = a.Attribute?.Name ?? "",
                })
                .Where(p => !string.IsNullOrWhiteSpace(p.Name))
                .ToList();

            var group = new TeammateGroupItem
            {
                MainName = main?.Name ?? "",
                MainIconUrl = main?.CardPictureUrl ?? "",
                MainStar = main?.Star ?? 0,
                MainNotOwned = main?.IsNotOwned == true,
                WeaponName = t.Weapon?.Name ?? "",
                WeaponIconUrl = t.Weapon?.PictureUrl ?? "",
                // 未拥有判定:接口 weapon.isAcquired == false(实测该字段只在带 x-token 时为真)
                WeaponNotOwned = t.Weapon?.IsAcquired == false,
                EchoName = t.EchoProps?.Name ?? "",
                EchoIconUrl = t.EchoProps?.PictureUrl ?? "",
                SetName = t.EchoSetEffect2?.Name ?? t.EchoSetEffect5?.Name ?? "",
                Props = props,
                Spares = spares,
            };
            if (!group.IsEmpty)
            {
                Teammates.Add(group);
            }
        }
        OnPropertyChanged(nameof(HasTeammates));
    }

    /// <summary>清空官方推荐建议区(切换角色/无攻略时)。</summary>
    private void ClearGuideRecommendations()
    {
        GuideRecommendations.Clear();
        HasGuideRecommendations = false;
    }

    /// <summary>
    /// 重算角色卡上的「当前佩戴武器 vs 官方推荐武器」档位徽章(推荐/备选/有差距)。
    /// <para>
    /// <b>必须幂等、可重复调用</b>:攻略数据(本地 SQLite 缓存,毫秒级)与库街区详情(网络,慢得多)
    /// 在 <see cref="OnSelectedRoleChanged"/> 里并发发起,到达顺序不定。
    /// 此前只在攻略到达时算一次 —— 攻略先到时 <c>role.WeaponData</c> 还是 null,
    /// 匹配不到任何推荐武器就落进 else 硬写「有差距」,而库街区详情到达后只重建了
    /// 技能卡/属性/共鸣链,<b>没有重算武器档位</b>,错误徽章便一直留在界面上
    /// (用户反馈:佩戴的武器是对的,却提示有差距;重新点一次角色就恢复正常)。
    /// </para>
    /// <para>纳入同步的四种状态:① 有攻略+已匹配 → 推荐/备选;② 有攻略+详情已到但未命中 → 有差距;
    /// ③ 有攻略+详情未到(武器名未知)→ 保持中性(不误报);④ 无攻略 → 清空。</para>
    /// </summary>
    private void RefreshWeaponVerdict(RoleDetail role, GuideIntroductionInfo? info)
    {
        var guideWeapons = info?.Weapon?.Items ?? [];

        // 推荐武器名/图标(首选 status=1,否则 items 首件;供箭头后的推荐图标展示)
        var primary = guideWeapons.FirstOrDefault(w => w.Status == 1) ?? guideWeapons.FirstOrDefault();
        RecommendedWeaponName = primary?.Name;
        RecommendedWeaponIcon = primary?.PictureUrl;

        var equipped = role.WeaponData?.Weapon?.WeaponName;
        var matched = GuideAchievementService.MatchEquippedWeapon(equipped, guideWeapons);
        if (matched is not null)
        {
            // 命中列表 ≠ 命中推荐档:status=0 表示"在攻略 items 里但非首选/备选",
            // 此时档位文本为空 —— 徽章隐藏、箭头仍指向官方首选(与原行为一致)。
            var tier = GuideAchievementService.WeaponRecommendText(matched);
            CurrentWeaponIsRecommended = !string.IsNullOrEmpty(tier);
            CurrentWeaponTierText = tier;
            return;
        }
        if (guideWeapons.Count == 0)
        {
            // 无攻略推荐武器:中性态(不显示徽章,也不显示推荐图标)
            CurrentWeaponIsRecommended = null;
            CurrentWeaponTierText = "";
            return;
        }
        if (string.IsNullOrWhiteSpace(equipped))
        {
            // 有推荐但库街区详情还没到(武器名未知):无从判定,保持中性。
            // 关键修复点:此处此前会写 false +「有差距」,把"还没到"误报成"不匹配"。
            CurrentWeaponIsRecommended = null;
            CurrentWeaponTierText = "";
            return;
        }
        // 详情已到且确实不在推荐列表里 → 有差距
        CurrentWeaponIsRecommended = false;
        CurrentWeaponTierText = LanguageService.Format("Roles.Guide.StateDiff");
    }

    /// <summary>
    /// 构建官方推荐建议区条目(数据源 = 点赞最高的攻略):
    /// 武器推荐(佩戴的是首选/备选)、声骸推荐(首件是否推荐声骸)、技能加点(各项推荐等级 vs 当前)、
    /// 共鸣链推荐(推荐链数说明)。
    /// </summary>
    private void BuildGuideRecommendations(RoleDetail role, GuideIntroductionInfo? info)
    {
        ClearGuideRecommendations();
        if (info is null)
        {
            return;
        }

        // ① 武器:玩家当前佩戴 vs 攻略推荐(首选 status=1 / 备选 status=2)
        //    匹配口径与角色卡徽章共用 MatchEquippedWeapon(归一化去间隔符/剥「梦魇」前缀)
        var equippedWeapon = role.WeaponData?.Weapon?.WeaponName;
        var guideWeapons = info.Weapon?.Items ?? [];
        var matchedWeapon = GuideAchievementService.MatchEquippedWeapon(equippedWeapon, guideWeapons);
        if (matchedWeapon is not null)
        {
            var grade = GuideAchievementService.WeaponRecommendText(matchedWeapon);
            GuideRecommendations.Add(new GuideRecommendItem
            {
                Icon = "Swords",
                Title = LanguageService.Format("Roles.Guide.WeaponTitle"),
                State = grade,
                StateMet = !string.IsNullOrEmpty(grade),
                Detail = string.IsNullOrEmpty(grade)
                    ? LanguageService.Format("Roles.Guide.WeaponNotRecommended", equippedWeapon ?? "-")
                    : LanguageService.Format("Roles.Guide.WeaponMatched", equippedWeapon ?? "-", grade),
            });
        }
        else if (guideWeapons is { Count: > 0 } && !string.IsNullOrWhiteSpace(equippedWeapon))
        {
            // 仅在「库街区详情已到、武器名已知」时才断言有差距。
            // 详情未到时 equippedWeapon 为空 —— 那是"还没到",不能报成"不匹配"。
            var primary = guideWeapons.FirstOrDefault(w => w.Status == 1) ?? guideWeapons[0];
            GuideRecommendations.Add(new GuideRecommendItem
            {
                Icon = "Swords",
                Title = LanguageService.Format("Roles.Guide.WeaponTitle"),
                State = LanguageService.Format("Roles.Guide.StateDiff"),
                StateMet = false,
                Detail = LanguageService.Format("Roles.Guide.WeaponSuggest", equippedWeapon, primary.Name ?? "-"),
            });
        }

        // ② 声骸:首位声骸是否为攻略推荐声骸(echo.main/spare/current 首件)
        var firstPhantom = role.PhantomData?.Phantoms?.FirstOrDefault();
        var firstPhantomName = firstPhantom?.PhantomName;
        if (!string.IsNullOrWhiteSpace(firstPhantomName))
        {
            var isRecommended = GuideAchievementService.IsRecommendedPhantom(firstPhantomName, info.Echo);
            GuideRecommendations.Add(new GuideRecommendItem
            {
                Icon = "Sparkle",
                Title = LanguageService.Format("Roles.Guide.PhantomTitle"),
                State = isRecommended ? LanguageService.Format("Roles.Guide.StateMatched") : LanguageService.Format("Roles.Guide.StateDiff"),
                StateMet = isRecommended,
                Detail = isRecommended
                    ? LanguageService.Format("Roles.Guide.PhantomMatched", firstPhantomName)
                    : LanguageService.Format("Roles.Guide.PhantomSuggest", firstPhantomName, info.Echo?.Main?.EchoProps?.Name ?? info.Echo?.Current?.EchoProps?.Name ?? "-"),
            });
        }

        // ③ 技能加点 / ④ 共鸣链推荐:**不在此列表展示**(避免与技能、共鸣链分段重复渲染)。
        //    技能加点见「技能」分段(SkillCardItems + SkillAdviceItems + SkillPriorityNodes),
        //    共鸣链推荐见「共鸣链」分段(链上"推荐"标识)。
        //    此前两条在此重复出现,用户看到"同步后两个相同的技能"。

        // ⑤ 基础连招(攻略 role.texts.skillDisplay)
        var baseText = info.Role?.SkillDisplay;
        if (!string.IsNullOrWhiteSpace(baseText))
        {
            GuideRecommendations.Add(new GuideRecommendItem
            {
                Icon = "ChatBubblesQuestion",
                Title = LanguageService.Format("Roles.Guide.ComboTitle"),
                State = "",
                StateMet = null,
                Detail = baseText,
            });
        }

        HasGuideRecommendations = GuideRecommendations.Count > 0;
    }

    /// <summary>
    /// 选中角色详情缺失时,按需单发库街区 getRoleDetail(不在页面加载/同步时批量拉取;
    /// 高频接口批量请求极易触发极验风控,且列表页本不需要全量详情)。
    /// <para>
    /// 「同步」之后点选的角色即使详情已由缓存补全,也强制重取一次(见 <see cref="_pendingDetailRefresh"/>):
    /// 同步的意义就是拿最新数据,不能因为缓存里有旧详情而跳过在线刷新。
    /// </para>
    /// </summary>
    private async Task LoadRoleDetailFromKujiequAsync(RoleDetail role)
    {
        var cardRoleId = role.Role?.RoleId ?? 0;
        var forceRefresh = cardRoleId > 0 && _pendingDetailRefresh.Contains(cardRoleId);

        // 详情已完整(本地缓存合并/mcguide 填充/已获取过)且不在本次同步的强制刷新名单中 → 不再请求
        if (role.IsDetailComplete && !forceRefresh)
        {
            return;
        }
        if (string.IsNullOrWhiteSpace(TokenText) || string.IsNullOrWhiteSpace(RoleIdText))
        {
            return; // 未配置库街区登录,交给 mcguide 兜底
        }
        if (!ReferenceEquals(role, SelectedRole))
        {
            return;
        }

        // 切换角色时取消上一条请求:单发 getRoleDetail 保持串行,并发请求易触发极验风控
        _detailFetchCts?.Cancel();
        var cts = new CancellationTokenSource();
        _detailFetchCts = cts;

        StatusText = LanguageService.Format("Roles.FetchingDetail", role.RoleName);
        try
        {
            // 不挂 ConfigureAwait(false):后续要 Clear/Add 多个绑定 ItemsControl 的
            // ObservableCollection(BuildSkillCards/RebuildAttributeItems 等),必须回 UI 线程。
            var result = await AppServices.Roles.LoadRoleDetailAsync(
                TokenText, RoleIdText, role.Role?.RoleId ?? 0, cts.Token);
            if (!ReferenceEquals(role, SelectedRole))
            {
                return; // 已切到其他角色,丢弃过期结果
            }
            if (result.Detail is not null)
            {
                // 强制刷新名单中的角色已拿到最新详情,移除标记(下次点选恢复"完整即跳过"语义)
                _pendingDetailRefresh.Remove(cardRoleId);
                MergeKujiequDetail(role, result.Detail);
                // 库街区技能/属性数据刚到达(链区被整体替换,链图标为空):
                // 先按当前攻略信息重建技能卡、刷新属性列表,并用已在内存的攻略数据回补链图标
                BuildSkillCards(role, GuideAchievement);
                MergeGuideChains(role, GuideAchievement);
                RebuildAttributeItems();
                // 武器档位也必须跟着重算:攻略(本地缓存)常先于库街区详情(网络)到达,
                // 那次计算时 WeaponData 仍为 null ⇒ 徽章会误判。详见 RefreshWeaponVerdict 说明。
                if (GuideAchievement is { } guideInfo)
                {
                    RefreshWeaponVerdict(role, guideInfo);
                    // 推荐建议区同样基于 WeaponData/PhantomData(武器行 + 声骸行),一并重建
                    BuildGuideRecommendations(role, guideInfo);
                }
                // 加点顺序节点同样基于技能卡:重建后必须跟着刷新,否则停留在被丢弃的旧 SkillCardItem 实例上
                BuildSkillPriority();
                OnPropertyChanged(nameof(HasSkillPriority));
                OnPropertyChanged(nameof(HasMoreAttributes));
                StatusText = LanguageService.Format("Roles.DetailLoaded", role.RoleName);
            }
            else if (result.GeeTest)
            {
                // 极验风控:不弹验证页(角色场景实测无法解除),提示稍后重试;详情留给 mcguide 兜底。
                // 强制刷新标记保留:下次点选该角色会再次尝试在线刷新。
                StatusText = LanguageService.Format("Roles.GeetestBlocked", role.RoleName);
                _ = FillRoleDetailFromGuideIfEmptyAsync();
            }
            else
            {
                StatusText = LanguageService.Format("Roles.DetailFailed", role.RoleName);
            }
        }
        catch (OperationCanceledException)
        {
            // 用户已切到其他角色,静默
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(role, SelectedRole))
            {
                StatusText = LanguageService.Format("Roles.DetailFailedWith", role.RoleName, ex.Message);
            }
        }
    }

    /// <summary>
    /// 角色详情合并后异步回填 Wiki「声骸词条」权重(每角色;联名角色/无攻略回退官方 valid/通用权重)。
    /// <para>fire-and-forget:结果按角色名缓存,回填走 UI 线程(INPC 通知刷新评级徽章)。</para>
    /// </summary>
    private static void QueueEchoWeightsLoad(RoleDetail? role)
    {
        if (role?.PhantomData?.Phantoms is not { Count: > 0 } phantoms)
        {
            return;
        }
        var roleName = role.Role?.RoleName;
        if (string.IsNullOrWhiteSpace(roleName))
        {
            return;
        }
        _ = Task.Run(async () =>
        {
            var weights = await WikiGuideService.GetPriorityWeightsAsync(roleName);
            if (weights is null)
            {
                return;
            }
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                foreach (var echo in phantoms)
                {
                    echo.PriorityWeights = weights;
                }
                role.RefreshEchoRating();
            });
        });
    }

    /// <summary>把库街区 getRoleDetail 结果合并进选中角色(权威数据:整体替换详情区块,保留列表基础信息)。</summary>
    private static void MergeKujiequDetail(RoleDetail target, RoleDetail source)
    {
        if (source.Role is not null)
        {
            target.Role = source.Role;
        }
        target.Level = source.Level;
        target.WeaponData = source.WeaponData;
        target.Skills = source.Skills;
        target.Attributes = source.Attributes;
        target.PhantomData = source.PhantomData;
        QueueEchoWeightsLoad(target);
        target.Chains = source.Chains;
        target.NotifyDetailChanged();
    }

    /// <summary>
    /// 库街区 chainList 无图标:用当前已加载的攻略数据按链序号回补共鸣链图标
    /// (不替换链解锁状态等权威数据,仅补 IconUrl)。
    /// <para>
    /// 纯内存映射,<b>不再重发网络请求</b>:攻略详情在 <see cref="ApplyGuideInfo"/> 时已写入
    /// <see cref="GuideAchievement"/>,直接复用它即可;此前再拉一次既慢又容易赶在
    /// 库街区详情(整体替换 Chains)之后才回来,导致图标被冲掉、界面看不到图标。
    /// 双向调用:攻略先到 → ApplyGuideInfo 里回补;库街区详情先到 → 这里回补。
    /// </para>
    /// </summary>
    private void MergeGuideChains(RoleDetail role, GuideIntroductionInfo? info)
    {
        if (role.Chains is not { Count: > 0 } chains)
        {
            return;
        }

        // ① 推荐标识:从推荐描述解析链号(如「共鸣链2…共鸣链6」→ 2/6),给对应链打标
        var chainRec = info?.RoleResonanceTexts?.FirstOrDefault(t => t.Language == "zh-Hans")?.RecommendDescription
            ?? info?.RoleResonanceTexts?.FirstOrDefault()?.RecommendDescription;
        var recommended = GuideAchievementService.ParseRecommendedChains(chainRec);
        foreach (var chain in chains)
        {
            chain.IsRecommended = recommended.Contains(chain.ChainNum);
        }

        // ② 图标:库街区 chainList 无图标,用攻略 pictureUrl 按链序号回补
        var guideChains = info?.RoleResonance?.Items;
        if (guideChains is not { Count: > 0 })
        {
            return;
        }
        var bySeq = new Dictionary<int, string>();
        foreach (var c in guideChains)
        {
            if (!string.IsNullOrWhiteSpace(c.PictureUrl))
            {
                bySeq[c.ResonanceSequence] = c.PictureUrl!;
            }
        }
        foreach (var chain in chains)
        {
            if (string.IsNullOrWhiteSpace(chain.IconUrl)
                && bySeq.TryGetValue(chain.ChainNum, out var url))
            {
                chain.IconUrl = url;
            }
        }
    }

    /// <summary>
    /// 当选中角色详情缺失(库街区 getRoleDetail 被极验风控 → 武器/技能/属性为空)
    /// 且已登录 mcguide 攻略站时,用 mcguide 数据填充 SelectedRole。
    /// </summary>
    private async Task FillRoleDetailFromGuideIfEmptyAsync()
    {
        var role = SelectedRole;
        if (role is null || _guideDetailFilling)
        {
            return;
        }
        // 详情已完整(库街区已返回/getRoleDetail 未被风控/缓存已合并)则跳过
        if (role.IsDetailComplete)
        {
            return;
        }
        if (!AppServices.Guide.HasToken)
        {
            return;
        }
        var cardRoleId = role.Role?.RoleId ?? 0;
        if (cardRoleId <= 0)
        {
            return;
        }

        _guideDetailFilling = true;
        try
        {
            var detail = await AppServices.Guide.GetRoleDetailFromGuideAsync(role.RoleName, cardRoleId);
            if (detail is null || role != SelectedRole)
            {
                return; // 已切到其他角色,丢弃过期结果
            }
            var wasComplete = role.IsDetailComplete;
            MergeGuideDetail(role, detail);
            // mcguide 兜底填充同样会带来 WeaponData(库街区详情被极验风控时为唯一来源):
            // 武器档位徽章必须跟着重算,否则停留在攻略先到时算出的中性/误判态。
            if (GuideAchievement is { } guideInfo)
            {
                RefreshWeaponVerdict(role, guideInfo);
                BuildGuideRecommendations(role, guideInfo);
            }
            if (wasComplete)
            {
                return; // 库街区按需详情已先返回完整数据:保留库街区状态,不用 guide 覆盖文案
            }
            // mcguide 图标是 B 域名:命中库街区磁盘缓存时按名称替换为本地图标,避免缺失/错位
            ApplyCachedRoleIcons(role);
            _guideFilledRole = role;
            SourceText = LanguageService.Format("Roles.SourceGuide");
            StatusText = LanguageService.Format("Roles.GuideFilled", role.RoleName);
        }
        catch (Exception)
        {
            // 填充失败不影响主流程
        }
        finally
        {
            _guideDetailFilling = false;
        }
    }

    /// <summary>把 mcguide 映射的角色详情合并进现有 SelectedRole(仅补缺失区块,保留库街区已有基础信息)。</summary>
    private static void MergeGuideDetail(RoleDetail target, RoleDetail source)
    {
        if (target.Role is not null && source.Role is not null)
        {
            if (string.IsNullOrWhiteSpace(target.Role.RoleName))
            {
                target.Role.RoleName = source.Role.RoleName;
            }
            if (target.Role.StarLevel <= 0)
            {
                target.Role.StarLevel = source.Role.StarLevel;
            }
            if (string.IsNullOrWhiteSpace(target.Role.RoleIconUrl))
            {
                target.Role.RoleIconUrl = source.Role.RoleIconUrl;
            }
        }
        target.Role ??= source.Role;
        target.WeaponData ??= source.WeaponData;
        if (target.Skills is not { Count: > 0 })
        {
            target.Skills = source.Skills;
        }
        if (target.Attributes is not { Count: > 0 })
        {
            target.Attributes = source.Attributes;
        }
        target.PhantomData ??= source.PhantomData;
        QueueEchoWeightsLoad(target);
        if (target.Chains is not { Count: > 0 })
        {
            target.Chains = source.Chains;
        }
        target.NotifyDetailChanged();
    }

    /// <summary>
    /// mcguide 填充后:用磁盘缓存图标(按名称匹配库街区缓存)替换各图标字段,
    /// 未命中保留原(mcguide B 域名)URL。处理武器/技能/共鸣链/属性/声骸/角色立绘。
    /// </summary>
    private static void ApplyCachedRoleIcons(RoleDetail role)
    {
        var cache = AppServices.IconCache;
        if (role.Role is { } r && !string.IsNullOrWhiteSpace(r.RolePicUrl))
        {
            r.RolePicUrl = cache.ResolveIcon(IconDiskCacheService.CategoryRole, role.RoleName, r.RolePicUrl);
        }
        if (role.WeaponData?.Weapon is { } w)
        {
            w.WeaponIcon = cache.ResolveIcon(IconDiskCacheService.CategoryWeapon, role.WeaponData.DisplayName, w.WeaponIcon);
        }
        if (role.Skills is not null)
        {
            foreach (var s in role.Skills)
            {
                if (s.Skill is { } sk && !string.IsNullOrWhiteSpace(sk.IconUrl))
                {
                    sk.IconUrl = cache.ResolveIcon(IconDiskCacheService.CategorySkill, sk.SkillName, sk.IconUrl);
                }
            }
        }
        if (role.Chains is not null)
        {
            foreach (var c in role.Chains)
            {
                if (!string.IsNullOrWhiteSpace(c.IconUrl))
                {
                    c.IconUrl = cache.ResolveIcon(IconDiskCacheService.CategoryChain, c.ChainName, c.IconUrl);
                }
            }
        }
        if (role.Attributes is not null)
        {
            foreach (var a in role.Attributes)
            {
                if (!string.IsNullOrWhiteSpace(a.IconUrl))
                {
                    a.IconUrl = cache.ResolveIcon(IconDiskCacheService.CategoryAttr, a.AttributeName, a.IconUrl);
                }
            }
        }
        if (role.PhantomData?.Phantoms is not null)
        {
            foreach (var e in role.PhantomData.Phantoms)
            {
                if (e.PhantomProp is { } pp && !string.IsNullOrWhiteSpace(pp.IconUrl))
                {
                    pp.IconUrl = cache.ResolveIcon(IconDiskCacheService.CategoryEcho, pp.PhantomName, pp.IconUrl);
                }
            }
        }
        role.NotifyDetailChanged();
    }

    [RelayCommand]
    private async Task LoadFromKujiequAsync()
    {
        if (IsBusy)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(TokenText))
        {
            LoadCachedOrHint(LanguageService.Format("Roles.NotLoggedIn"));
            return;
        }
        if (string.IsNullOrWhiteSpace(RoleIdText))
        {
            LoadCachedOrHint(LanguageService.Format("Roles.NoRoleId"));
            return;
        }

        IsBusy = true;
        StatusText = LanguageService.Format("Roles.Syncing");
        try
        {
            // 记录同步前的角色集合,用于识别"新增角色"(只为新增角色预取攻略/图标)
            var previousIds = Roles.Select(r => r.Role?.RoleId ?? 0).Where(id => id > 0).ToHashSet();

            // 仅同步角色列表(roleData);角色详情在点击具体角色时按需单发(高频接口批量易触发极验风控)
            var result = await AppServices.Roles.LoadRoleListAsync(TokenText, RoleIdText);
            if (result.IsSuccess)
            {
                // 同步拿到的列表项会被服务层用本地缓存补全详情(IsDetailComplete=true),
                // 若不登记强制刷新,点选角色会因"详情已完整"跳过在线请求 → 详情永远停在缓存快照。
                // 登记本次列表所有角色,点选时逐个强制重取一次(单发串行,不触发风控)。
                // 必须在 ApplyRoles 之前登记:ApplyRoles 自动选中第一个角色即触发按需详情请求。
                _pendingDetailRefresh = result.Roles
                    .Select(r => r.Role?.RoleId ?? 0)
                    .Where(id => id > 0)
                    .ToHashSet();

                ApplyRoles(result);
                StatusText = result.Message ?? LanguageService.Format("Roles.Synced", result.Roles.Count);
                if (result.Roles is { Count: > 0 } && SelectedRole is { } first && first.IsDetailComplete)
                {
                    StatusText += LanguageService.Format("Roles.MergedFromCache");
                }

                // 只为"新增角色"预取攻略(老角色的攻略已在 SQLite 缓存里):
                // 新角色才有新攻略/新 logo 要拉,避免每次同步把全部角色重扫一遍。
                var currentIds = _pendingDetailRefresh;
                var newIds = currentIds.Where(id => !previousIds.Contains(id)).ToList();
                _ = PrefetchGuidesAsync(result.Roles.Where(r => newIds.Contains(r.Role?.RoleId ?? 0)).ToList());
                // 清理已不在列表中的角色攻略缓存(换号/移除角色后的孤儿数据)。
                // SQLite DELETE 是同步 I/O,放后台线程,不卡 UI(失败静默:下次同步再清)。
                var pruneIds = currentIds.ToList();
                _ = Task.Run(() =>
                {
                    try
                    {
                        AppServices.Guide.PruneGuideCache(pruneIds);
                    }
                    catch (Exception)
                    {
                    }
                });
            }
            else
            {
                // 同步失败(网络/token 失效等):兜底读本地缓存
                LoadCachedOrHint(result.Message ?? LanguageService.Format("Roles.SyncFailed"));
            }
        }
        catch (Exception ex)
        {
            LoadCachedOrHint(LanguageService.Format("Roles.FetchFailed", ex.Message));
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 为新增角色预取攻略数据并写入 SQLite 缓存(串行,避免并发请求触发风控)。
    /// <para>
    /// 只在同步发现"角色列表新增"时调用:老角色的攻略已在缓存里(24h 内直接用),
    /// 新角色才需要拉攻略(含共鸣链图标、推荐标签、技能达标)。
    /// 预取在后台进行,不阻塞界面;失败静默(用户点该角色时会再按需拉一次)。
    /// </para>
    /// </summary>
    private async Task PrefetchGuidesAsync(IReadOnlyList<RoleDetail> newRoles)
    {
        if (newRoles.Count == 0 || !AppServices.Guide.HasToken)
        {
            return;
        }
        foreach (var role in newRoles)
        {
            var cardRoleId = role.Role?.RoleId ?? 0;
            if (cardRoleId <= 0)
            {
                continue;
            }
            // 已有新鲜缓存则跳过(同一角色重复预取无意义);
            // 用户已点中该角色、按需拉取在途时也跳过(同步后自动选中首个新角色的竞态去重)
            if (IsGuideFetchInFlight(cardRoleId))
            {
                continue;
            }
            var cached = AppServices.Guide.TryGetCachedGuide(cardRoleId);
            if (AppServices.Guide.IsGuideCacheFresh(cached))
            {
                continue;
            }
            try
            {
                // 拉取即回写缓存在 GuideAchievementService 内统一完成,预取只负责触发
                var list = await AppServices.Guide.GetIntroductionListAsync(role.RoleName, cardRoleId);
                if (list.Count > 0)
                {
                    await AppServices.Guide.GetAchievementByIdAsync(role.RoleName, cardRoleId, list[0].Id);
                }
            }
            catch (Exception)
            {
                // 预取失败静默:用户点该角色时会再按需拉取
            }
            // 轻微间隔:避免连续请求过密被服务端限流/风控
            await Task.Delay(300);
        }
    }

    /// <summary>兜底:校验账号后读取本地缓存;无可用缓存时给出提示。</summary>
    private void LoadCachedOrHint(string reason)
    {
        var cached = AppServices.Roles.LoadFromCache(CurrentAccountId, RoleIdText);
        if (cached.IsSuccess && cached.Roles.Count > 0)
        {
            ApplyRoles(cached);
            StatusText = LanguageService.Format("Roles.CachedLoaded", reason, cached.Roles.Count);
        }
        else
        {
            StatusText = LanguageService.Format("Roles.CacheUnavailable", reason, cached.Message);
        }
    }

    /// <summary>读取本地缓存(校验当前账号归属;库街区未登录或同步异常时的备用入口)。</summary>
    [RelayCommand]
    private void LoadFromLocal()
    {
        var accountId = CurrentAccountId;
        var result = AppServices.Roles.LoadFromCache(accountId, RoleIdText);
        if (result.IsSuccess)
        {
            ApplyRoles(result);
            StatusText = LanguageService.Format("Roles.ReadCacheLoaded", result.Roles.Count);
        }
        else
        {
            StatusText = result.Message ?? LanguageService.Format("Roles.CacheUnavailableShort");
        }
    }

    private void ApplyRoles(RoleDataLoadResult result)
    {
        Roles.Clear();
        foreach (var role in result.Roles)
        {
            Roles.Add(role);
        }

        HasRoles = Roles.Count > 0;
        SourceText = result.Source switch
        {
            RoleDataSource.Kujiequ => LanguageService.Format("Roles.SourceOnline"),
            RoleDataSource.Local => LanguageService.Format("Roles.SourceLocal"),
            _ => LanguageService.Format("Roles.SourceNone"),
        };

        // 重建属性筛选选项(多选;每项带本地属性图标 Assets/attr/{attributeId}.png)
        // Owner 放最后赋值:初始 IsSelected 不触发回调(此刻列表还没建完,建完统一 RebuildFilteredRoles),
        // 之后用户勾选才会走 Owner.OnAttributeFilterOptionChanged 做互斥与列表刷新。
        AttributeFilterOptions.Clear();
        AttributeFilterOptions.Add(new AttributeFilterOption
        {
            Name = AllAttributeFilter,
            IsAll = true,
            IsSelected = true,
            IconPath = "",
            Owner = this,
        });
        foreach (var group in Roles
                     .Where(r => !string.IsNullOrWhiteSpace(r.AttributeName))
                     .GroupBy(r => r.AttributeName, StringComparer.Ordinal)
                     .OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            // 取该属性任一角色的 attributeId 定位本地图标(同属性图标一致)
            var attrId = group.Select(r => r.Role?.AttributeId ?? 0).FirstOrDefault(id => id > 0);
            AttributeFilterOptions.Add(new AttributeFilterOption
            {
                Name = group.Key,
                IsAll = false,
                IsSelected = false,
                IconPath = attrId > 0
                    ? System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "attr", $"{attrId}.png")
                    : "",
                Owner = this,
            });
        }
        OnPropertyChanged(nameof(AttributeFilterSummary));

        RebuildFilteredRoles();
        SelectedRole = FilteredRoles.FirstOrDefault();
    }

    partial void OnSelectedSortChanged(string value) => RebuildFilteredRoles();

    /// <summary>按当前属性筛选(多选:命中任一选中属性即通过) + 排序重建 <see cref="FilteredRoles"/>。</summary>
    private void RebuildFilteredRoles()
    {
        var all = Roles;
        var picked = AttributeFilterOptions
            .Where(o => o.IsSelected && !o.IsAll)
            .Select(o => o.Name)
            .ToHashSet(StringComparer.Ordinal);
        // 未选具体属性(= 只勾了"全部属性")时不过滤
        var source = picked.Count == 0
            ? all
            : all.Where(r => picked.Contains(r.AttributeName));

        // 本地化排序选项非常量,不能用 switch 常量模式
        IEnumerable<RoleDetail> ordered = SelectedSort == SortByName
            ? source.OrderBy(r => r.RoleName, StringComparer.Ordinal)
            : source
                .OrderByDescending(r => r.StarLevel)
                .ThenBy(r => r.RoleName, StringComparer.Ordinal);

        FilteredRoles.Clear();
        foreach (var role in ordered)
        {
            FilteredRoles.Add(role);
        }

        // 若当前选中项被过滤掉,回退到第一个
        if (SelectedRole is not null && !FilteredRoles.Contains(SelectedRole))
        {
            SelectedRole = FilteredRoles.FirstOrDefault();
        }
    }
}

/// <summary>
/// 官方推荐建议区条目(角色详情页):武器/声骸/技能/共鸣链四类建议。
/// <para>State 为结论徽章文本(推荐/达标/有差距),StateMet 控制徽章配色(true 绿/false 橙/null 中性);
/// Detail 为具体信息(如「已佩戴推荐武器 时和岁稔(推荐)」「常态攻击 1/8 · 共鸣回路 1/10」)。</para>
/// </summary>
public sealed class GuideRecommendItem
{
    /// <summary>FluentIcon 图标名(Swords/Sparkle/Options/Link/ChatBubblesQuestion)。</summary>
    public string Icon { get; init; } = "Info";

    /// <summary>条目标题(如「武器推荐」「首位声骸」「技能加点」「共鸣链推荐」)。</summary>
    public required string Title { get; init; }

    /// <summary>结论徽章文本(空则不显示徽章)。</summary>
    public string State { get; init; } = "";

    /// <summary>结论是否达标(true 绿/false 橙/null 中性)。</summary>
    public bool? StateMet { get; init; }

    /// <summary>具体信息文本。</summary>
    public required string Detail { get; init; }

    /// <summary>是否有结论徽章。</summary>
    public bool HasState => !string.IsNullOrEmpty(State);
}

/// <summary>攻略切换下拉选项(标题 + 作者;点赞数用于默认排序展示)。</summary>
public sealed class GuideOptionItem
{
    public long Id { get; init; }
    public string Title { get; init; } = "";
    public string Author { get; init; } = "";
    public long LikeCount { get; init; }

    /// <summary>下拉显示文本:「标题 (作者)」;无作者仅标题。</summary>
    public string DisplayText => string.IsNullOrWhiteSpace(Author) ? Title : $"{Title} ({Author})";
}

/// <summary>
/// 加点建议条目(结构化,供「技能名 + 目标等级」着色显示)。
/// <para>
/// 由本地化模板按占位符拆成三段(前缀/中缀/后缀),句子结构随语言走;
/// 技能名用强文本色、目标等级用警示橙(与「橙=未达标」的图例一致)。
/// </para>
/// </summary>
public sealed class SkillAdviceEntry
{
    /// <summary>模板中 {0} 之前的文案(如「建议将」)。</summary>
    public string Prefix { get; init; } = "";

    /// <summary>技能类型名。</summary>
    public string Name { get; init; } = "";

    /// <summary>模板中 {0} 与 {1} 之间的文案(如「提升至」)。</summary>
    public string Middle { get; init; } = "";

    /// <summary>攻略推荐等级(着色数字)。</summary>
    public int Level { get; init; }

    /// <summary>模板中 {1} 之后的文案(如「级」)。</summary>
    public string Suffix { get; init; } = "";

    /// <summary>降级显示:拆分失败或无占位符时的整句兜底(三段都为空时模板用这个)。</summary>
    public string Fallback { get; init; } = "";

    /// <summary>模板占位符拆分是否成功(决定走分段着色还是整句兜底)。</summary>
    public bool HasSegments => Prefix.Length > 0 || Middle.Length > 0 || Suffix.Length > 0;
}

/// <summary>
/// 技能独立卡(技能分段):图标/等级来自库街区 getRoleDetail,
/// 攻略推荐等级命中时按达标情况着色(绿框达标/橙框未达)。
/// </summary>
public sealed class SkillCardItem : System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    /// <summary>技能名(库街区 skill.name;为空时回退类型名)。</summary>
    public string Name { get; init; } = "";

    /// <summary>技能类型名(常态攻击/共鸣技能/…;用于与攻略 addPointTarget 匹配)。</summary>
    public string TypeName { get; init; } = "";

    /// <summary>等级文本(如 "Lv.10")。</summary>
    public string LevelText { get; init; } = "";

    /// <summary>技能图标 URL。</summary>
    public string IconUrl { get; init; } = "";

    /// <summary>攻略是否给出推荐等级(有才显示达标徽章)。</summary>
    public bool HasRecommendation { get; init; }

    /// <summary>是否达到推荐等级。</summary>
    public bool IsMet { get; init; }

    /// <summary>推荐对比文本(如「1/8」)。</summary>
    public string RecommendText { get; init; } = "";

    /// <summary>卡片描边色:有推荐时绿/橙,无推荐时透明(不喧宾夺主)。</summary>
    public Avalonia.Media.IBrush BorderBrush => HasRecommendation
        ? new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(IsMet ? "#22C55E" : "#F59E0B"))
        : Avalonia.Media.Brushes.Transparent;

    /// <summary>达标徽章底色。</summary>
    public Avalonia.Media.IBrush MetBrush => new Avalonia.Media.SolidColorBrush(
        Avalonia.Media.Color.Parse(IsMet ? "#22C55E" : "#F59E0B"));

    /// <summary>达标徽章文本。</summary>
    public string MetText => IsMet
        ? LanguageService.Format("Roles.Guide.StateMatched")
        : RecommendText;

    /// <summary>
    /// 等级角标文本(参照攻略站技能节点的「6/8」角标):
    /// 有推荐等级时显示 当前/推荐;无推荐时只显示当前等级。
    /// </summary>
    public string LevelBadgeText => HasRecommendation && !string.IsNullOrWhiteSpace(RecommendText)
        ? RecommendText
        : (LevelText.StartsWith("Lv.", StringComparison.Ordinal) ? LevelText[3..] : LevelText);

    /// <summary>
    /// 节点纵向错落偏移(参照攻略站"弧形"排布:共鸣回路最高,两侧依次降低)。
    /// 仅主节点(常态攻击/共鸣技能/共鸣回路/共鸣解放/变奏技能)使用。
    /// </summary>
    public Avalonia.Thickness NodeOffset => TypeName switch
    {
        "共鸣回路" => new Avalonia.Thickness(0, 0, 0, 26),
        "共鸣解放" or "共鸣技能" => new Avalonia.Thickness(0, 0, 0, 12),
        _ => new Avalonia.Thickness(0, 0, 0, 0),
    };

    /// <summary>是否当前正在演示该技能(用于节点外圈高亮;由 VM 在切换演示时刷新)。</summary>
    public bool IsCurrentDemo
    {
        get => _isCurrentDemo;
        set
        {
            if (_isCurrentDemo == value)
            {
                return;
            }
            _isCurrentDemo = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsCurrentDemo)));
        }
    }

    private bool _isCurrentDemo;

    /// <summary>
    /// 加点顺序链中是否为最后一项(隐藏尾部的「›」分隔符;由 BuildSkillPriority 维护)。
    /// 同一个节点实例也会进主节点集合,但只有加点顺序模板绑定该属性,互不影响。
    /// </summary>
    public bool IsLast
    {
        get => _isLast;
        set
        {
            if (_isLast == value)
            {
                return;
            }
            _isLast = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsLast)));
        }
    }

    private bool _isLast;

    /// <summary>节点区显示的文本(类型名优先,回退技能名)。</summary>
    public string DisplayText => string.IsNullOrWhiteSpace(TypeName) ? Name : TypeName;
}

/// <summary>官方推荐结论徽章 → 底色(true 绿=达标,false 橙=有差距,null 中性灰)。</summary>
public sealed class RecommendStateBrushConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly RecommendStateBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => new Avalonia.Media.SolidColorBrush(value switch
        {
            true => Avalonia.Media.Color.Parse("#22C55E"),
            false => Avalonia.Media.Color.Parse("#F59E0B"),
            _ => Avalonia.Media.Color.Parse("#9CA3AF"),
        });

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>非空字符串 → 可见(官方推荐结论徽章有无)。</summary>
public sealed class StringNotEmptyToVisibleConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly StringNotEmptyToVisibleConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is string s && !string.IsNullOrWhiteSpace(s);

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>字符串为空 → true(用于"无视频提示"这类空态显示)。</summary>
public sealed class StringIsEmptyConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly StringIsEmptyConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is not string s || string.IsNullOrWhiteSpace(s);

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>GuideRecommendItem.Title == "共鸣链推荐" → 可见(共鸣链 Tab 内嵌推荐说明用;parameter 传标题文本)。</summary>
public sealed class TitleEqualsToVisibleConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly TitleEqualsToVisibleConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is string s && parameter is string p && string.Equals(s, p, StringComparison.Ordinal);

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// 详情导航项(带攻略达成度标识)。
/// <para>Status 语义:Complete=完全达标(绿勾)/ Partial=部分达标(橙勾)/ None=无标识。</para>
/// </summary>
public sealed class SectionNavItem : System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    /// <summary>导航标题(本地化)。</summary>
    public required string Title { get; init; }

    private SectionStatus _status;
    /// <summary>达成度状态(变更时通知绑定刷新标识颜色)。</summary>
    public SectionStatus Status
    {
        get => _status;
        set
        {
            if (_status == value)
            {
                return;
            }
            _status = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Status)));
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(HasStatus)));
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(StatusText)));
        }
    }

    /// <summary>是否有达成度标识可显示。</summary>
    public bool HasStatus => Status != SectionStatus.None;

    /// <summary>标识文本(✓;完全/部分同符号,颜色区分)。</summary>
    public string StatusText => Status == SectionStatus.None ? "" : "✓";
}

/// <summary>导航项达成度状态。</summary>
public enum SectionStatus
{
    /// <summary>无数据/未判定(不显示标识)。</summary>
    None,
    /// <summary>部分达标(橙)。</summary>
    Partial,
    /// <summary>完全达标(绿)。</summary>
    Complete,
}

/// <summary>
/// 技能演示项(技能演示 + 基础连招卡):攻略站 roleSkill 的技能,可带演示视频。
/// </summary>
public sealed class SkillDemoItem
{
    /// <summary>技能名。</summary>
    public string Name { get; init; } = "";

    /// <summary>技能类型(延奏技能/谐度破坏/固有技能…)。</summary>
    public string TypeName { get; init; } = "";

    /// <summary>技能图标。</summary>
    public string IconUrl { get; init; } = "";

    /// <summary>演示视频(mp4;空表示该技能无演示)。</summary>
    public string VideoUrl { get; init; } = "";

    /// <summary>技能说明。</summary>
    public string Description { get; init; } = "";

    /// <summary>是否有演示视频。</summary>
    public bool HasVideo => !string.IsNullOrWhiteSpace(VideoUrl);

    /// <summary>显示名(技能名 + 类型,用于演示选择列表)。</summary>
    public string DisplayText => string.IsNullOrWhiteSpace(TypeName) ? Name : $"{Name} · {TypeName}";
}

/// <summary>导航项达成度状态 → 勾色(完全达标绿 / 部分达标橙 / 无数据透明)。</summary>
public sealed class SectionStatusBrushConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly SectionStatusBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => new Avalonia.Media.SolidColorBrush((value as SectionStatus?) switch
        {
            SectionStatus.Complete => Avalonia.Media.Color.Parse("#22C55E"),
            SectionStatus.Partial => Avalonia.Media.Color.Parse("#F59E0B"),
            _ => Avalonia.Media.Colors.Transparent,
        });

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>可空布尔是否为明确的 false(用于"仅未达标时显示"这类可见性;null 不算 false)。</summary>
public sealed class IsFalseConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly IsFalseConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is false;

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// 属性项视图模型(带官方达标信息):UI 在数值后就地显示「推荐 X」+「已达标/差 X」。
/// </summary>
public sealed class RoleAttributeItem
{
    /// <summary>属性名(如「暴击伤害」)。</summary>
    public required string Name { get; init; }

    /// <summary>当前值文本(如「326.0%」)。</summary>
    public required string CurrentText { get; init; }

    /// <summary>属性图标。</summary>
    public string IconUrl { get; init; } = "";

    /// <summary>官方推荐值文本(无推荐时为空)。</summary>
    public string TargetText => string.IsNullOrWhiteSpace(RecommendText)
        ? ""
        : LanguageService.Format("Roles.Guide.TargetShort", RecommendText);

    /// <summary>官方推荐值原文(如「280.0%」)。</summary>
    public string RecommendText { get; init; } = "";

    /// <summary>是否达标(攻略 isFinished;null = 无官方指标)。</summary>
    public bool? IsMet { get; init; }

    /// <summary>差值徽章文本(已达标 / 差 X / 有差距;空则不显示)。</summary>
    public string DeltaText { get; init; } = "";

    /// <summary>是否有官方指标(决定是否显示达标区)。</summary>
    public bool HasTarget => IsMet is not null;

    /// <summary>达标徽章底色(绿=达标,橙=未达)。</summary>
    public Avalonia.Media.IBrush MetBrush => new Avalonia.Media.SolidColorBrush(
        Avalonia.Media.Color.Parse(IsMet == true ? "#22C55E" : "#F59E0B"));
}

/// <summary>
/// 队友推荐组(攻略站 teammate.items 的一项):主推队友 + 其推荐配装 + 备选队友。
/// 「未拥有」直接来自接口 isAcquired(实测 teammate 的 main/spares/weapon 都带该字段)。
/// </summary>
public sealed class TeammateGroupItem
{
    /// <summary>主推队友名。</summary>
    public required string MainName { get; init; }

    /// <summary>主推队友头像。</summary>
    public string MainIconUrl { get; init; } = "";

    /// <summary>主推队友星级。</summary>
    public int MainStar { get; init; }

    /// <summary>主推队友是否未拥有(接口 isAcquired == false)。</summary>
    public bool MainNotOwned { get; init; }

    /// <summary>推荐武器名(可能为空)。</summary>
    public string WeaponName { get; init; } = "";

    /// <summary>推荐武器图标。</summary>
    public string WeaponIconUrl { get; init; } = "";

    /// <summary>该推荐武器是否未拥有(接口 weapon.isAcquired == false)。</summary>
    public bool WeaponNotOwned { get; init; }

    /// <summary>是否有推荐武器可显示。</summary>
    public bool HasWeapon => !string.IsNullOrWhiteSpace(WeaponIconUrl) || !string.IsNullOrWhiteSpace(WeaponName);

    /// <summary>推荐声骸名(4C 主声骸)。</summary>
    public string EchoName { get; init; } = "";

    /// <summary>推荐声骸图标。</summary>
    public string EchoIconUrl { get; init; } = "";

    /// <summary>是否有推荐声骸可显示。</summary>
    public bool HasEcho => !string.IsNullOrWhiteSpace(EchoIconUrl) || !string.IsNullOrWhiteSpace(EchoName);

    /// <summary>该组攻略没给武器与声骸(有些攻略只给队友搭配,不给配装)。</summary>
    public bool HasNoBuild => !HasWeapon && !HasEcho && Props.Count == 0;

    /// <summary>套装名(2 件套优先;为空则取 5 件套)。</summary>
    public string SetName { get; init; } = "";

    /// <summary>声骸词条列表(如「4 治疗效果加成」简化为属性名)。</summary>
    public IReadOnlyList<TeammatePropItem> Props { get; init; } = [];

    /// <summary>备选队友(可空)。</summary>
    public IReadOnlyList<TeammateRefItem> Spares { get; init; } = [];

    /// <summary>是否有备选队友。</summary>
    public bool HasSpares => Spares.Count > 0;

    /// <summary>是否有配装信息可显示。</summary>
    public bool HasBuild => !string.IsNullOrWhiteSpace(WeaponName) || !string.IsNullOrWhiteSpace(EchoName);

    /// <summary>是否有词条列表。</summary>
    public bool HasProps => Props.Count > 0;

    /// <summary>是否完全没有可展示内容(用于隐藏整组)。</summary>
    public bool IsEmpty => string.IsNullOrWhiteSpace(MainName) && !HasBuild && !HasProps;
}

/// <summary>队友声骸词条(点数 + 属性名)。</summary>
public sealed class TeammatePropItem
{
    /// <summary>COST 点数(4/3/1)。</summary>
    public int Cost { get; init; }

    /// <summary>属性名(如「治疗效果加成」)。</summary>
    public required string Name { get; init; }
}

/// <summary>备选队友(带未拥有标记)。</summary>
public sealed class TeammateRefItem
{
    public required string Name { get; init; }
    public string IconUrl { get; init; } = "";
    public bool NotOwned { get; init; }
}

/// <summary>
/// 主题自适应图标着色(单色线稿用):浅色主题→黑色,暗色主题→白色。
/// <para>攻略站的技能/共鸣链图标是白色线稿,直接显示在浅色主题上看不见;
/// 统一着成"浅色主题黑、暗色主题白"即处处可见,且不依赖图片自身颜色。</para>
/// </summary>
public sealed class ThemeIconBrushConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly ThemeIconBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        var dark = ThemeHelper.IsDarkTheme();
        // 可选参数传 "muted" 时用次级灰(未解锁等非强调场景)
        var muted = parameter is string s && s.Equals("muted", StringComparison.OrdinalIgnoreCase);
        var color = (dark, muted) switch
        {
            (true, false) => "#FFFFFF",
            (true, true) => "#9AA3B2",
            (false, false) => "#1F2430",
            (false, true) => "#8A8F99",
        };
        return new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(color));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>颜色字符串 → 画刷(带主题明暗微调;用于共鸣链星形装饰框描边)。</summary>
public sealed class ColorTextToBrushConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly ColorTextToBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is not string text || string.IsNullOrWhiteSpace(text))
        {
            return Avalonia.Media.Brushes.Transparent;
        }
        // 金色描边在浅色主题下加深、暗色主题下提亮(保证两种主题都清晰可见)
        var dark = ThemeHelper.IsDarkTheme();
        if (text.Equals("#B08D3F", StringComparison.OrdinalIgnoreCase))
        {
            text = dark ? "#E8D9A0" : "#B08D3F";
        }
        try
        {
            return new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(text));
        }
        catch (Exception)
        {
            return Avalonia.Media.Brushes.Transparent;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// 属性筛选选项(多选下拉):名称 + 本地属性图标 + 勾选状态。
/// <para>勾选变化通过 <see cref="Owner"/> 通知 VM 处理互斥("全部属性")与列表刷新。</para>
/// </summary>
public sealed class AttributeFilterOption : System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    /// <summary>所属 VM(用于勾选变化时回调;未设置时仅更新自身状态)。</summary>
    public RolesViewModel? Owner { get; init; }

    /// <summary>属性名(显示文本)。</summary>
    public required string Name { get; init; }

    /// <summary>是否为"全部属性"(互斥项)。</summary>
    public bool IsAll { get; init; }

    /// <summary>本地属性图标路径(Assets/attr/{id}.png;"全部属性"为空)。</summary>
    public string IconPath { get; init; } = "";

    private bool _isSelected;

    /// <summary>是否勾选。</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }
            _isSelected = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsSelected)));
            Owner?.OnAttributeFilterOptionChanged(this);
        }
    }
}

/// <summary>布尔取反(用于 IsEnabled 反向绑定,如 IsBusy → 禁用)。</summary>
public sealed class InverseBoolConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly InverseBoolConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is not true;

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is not true;
}

/// <summary>
/// 共鸣链解锁状态 → 图标不透明度。
/// <para>未解锁不再用 0.35(过淡,黑图标几乎看不见),改为 0.78:
/// 既能表达"未解锁",又保证图标形状与颜色清晰可辨。</para>
/// </summary>
public sealed class UnlockToOpacityConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly UnlockToOpacityConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is true ? 1.0 : 0.78;

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// 是否已解锁 → 图标着色模式(共鸣链图标用)。
/// <para>已解锁 = <c>ThemeForeground</c>(浅色主题黑/暗色主题白,醒目);
/// 未解锁 = <c>ThemeMuted</c>(次级灰,仍清晰可见)。
/// 此前未解锁只靠 <c>Opacity=0.35</c> 压暗,黑色图标被淡到几乎看不出颜色,
/// 用户误以为"着色没有生效"(实际是淡到不可见)。</para>
/// </summary>
public sealed class UnlockToTintConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly UnlockToTintConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is true
            ? McKuro.Controls.IconTintMode.ThemeForeground
            : McKuro.Controls.IconTintMode.ThemeMuted;

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>布尔 → 画刷转换(用于共鸣链解锁状态显示)。</summary>
public sealed class BoolToBrushConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly BoolToBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        var app = Avalonia.Application.Current;
        if (app is null)
        {
            return null;
        }

        bool ok = value is true;
        if (ok)
        {
            return app.TryFindResource("SemiColorPrimary", out var brush) ? brush : null;
        }
        return app.TryFindResource("SemiColorText3", out var gray) ? gray : null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>布尔 → 透明度(未解锁共鸣链置灰;参照 WutheringWavesTool chainImgVisible)。</summary>
public sealed class BoolToOpacityConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly BoolToOpacityConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is true ? 1.0 : 0.35;

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>数值 &gt; 0 判断(角色卡片仅在确有链信息时显示链数)。</summary>
public sealed class GreaterThanZeroConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly GreaterThanZeroConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is int n && n > 0;

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>星级 → 色条画刷(5★金 / 4★紫 / 其他灰;参照 WutheringWavesTool thumb)。</summary>
public sealed class StarThumbBrushConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly StarThumbBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value switch
        {
            5 => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#f8f05c")),
            4 => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#bc60f2")),
            _ => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#4a4a4a")),
        };

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// 词条装饰条色(档位由 <see cref="McKuro.Core.Models.Roles.EchoProp.EffectiveLevel"/> 决定):
/// <para>
/// level3 = 暴击/暴击伤害/攻击% 等核心词条(亮色);level2 = <b>官方 valid 判定有效</b>的
/// 其他词条(青;如普攻/重击/共鸣技能/共鸣解放/谐度破坏伤害加成、共鸣效率 —— 按角色区分,
/// 来源 getRoleDetail subProps[].valid);level1/0 = 官方无效或无官方数据时的非核心词条 → 灰。
/// </para>
/// <para>
/// 演进:2026-10 曾按用户反馈把 level2 降灰(当时只有通用权重表,无法按角色区分"是否有效");
/// 后实测 getRoleDetail 自带按角色的 valid 字段,恢复青档表达"官方认可的有效词条"。
/// </para>
/// </summary>
public sealed class PropLevelBrushConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly PropLevelBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        bool dark = ThemeHelper.IsDarkTheme();
        return value switch
        {
            // 核心有效词条:更鲜艳的亮色(暗色主题亮黄,浅色主题高饱和琥珀)
            3 => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(dark ? "#FFE81A" : "#E08A00")),
            // 官方 valid 判定有效的其他词条:青
            2 => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(dark ? "#4DD0E1" : "#0097A7")),
            // 官方无效 / 无官方数据时的非核心词条:灰
            _ => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(dark ? "#666666" : "#B0B0B0")),
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// 词条文字色:与 <see cref="PropLevelBrushConverter"/> 同档
/// (level3 亮色核心 / level2 青色官方有效 / 其余灰 —— 无效词条必须一眼可辨)。
/// </summary>
public sealed class PropTextBrushConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly PropTextBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        bool dark = ThemeHelper.IsDarkTheme();
        return value switch
        {
            3 => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(dark ? "#FFE81A" : "#E08A00")),
            2 => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(dark ? "#4DD0E1" : "#0097A7")),
            _ => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(dark ? "#8A8A8A" : "#9A9A9A")),
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>声骸评级等级 → 文字色(参照 WutheringWavesTool status:ACE红 / SSS,SS黄 / S紫 / N灰;主题自适应)。</summary>
public sealed class EchoRatingLevelBrushConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly EchoRatingLevelBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        bool dark = ThemeHelper.IsDarkTheme();
        return (value as EchoRatingLevel?) switch
        {
            // 完美毕业:红;毕业:黄;小毕业:紫;未毕业:灰(与评级的四档术语对应)
            EchoRatingLevel.Ace => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#e33737")),
            EchoRatingLevel.SSS =>
                new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(dark ? "#ffec16" : "#a88400")),
            EchoRatingLevel.SS => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(dark ? "#c77dff" : "#7e22ce")),
            EchoRatingLevel.S or EchoRatingLevel.N =>
                new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(dark ? "#9a9a9a" : "#8a8a8a")),
            _ => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(dark ? "#9a9a9a" : "#8a8a8a")),
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>主题明暗辅助(转换器共用)。</summary>
internal static class ThemeHelper
{
    public static bool IsDarkTheme()
    {
        var app = Avalonia.Application.Current;
        if (app?.RequestedThemeVariant == Avalonia.Styling.ThemeVariant.Dark)
        {
            return true;
        }
        if (app?.RequestedThemeVariant == Avalonia.Styling.ThemeVariant.Light)
        {
            return false;
        }
        try
        {
            return app?.PlatformSettings?.GetColorValues().ThemeVariant == Avalonia.Platform.PlatformThemeVariant.Dark;
        }
        catch (Exception)
        {
            return false;
        }
    }
}

/// <summary>声骸品质底色(参照 WutheringWavesTool icon ssr/sr/r:5=金 / 4=紫 / 其他=灰)。</summary>
public sealed class PhantomQualityBrushConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly PhantomQualityBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value switch
        {
            5 => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#8a6d1f")),
            4 => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#5b4a8a")),
            _ => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#4a4a4a")),
        };

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>布尔 → 达成/未达成文字色(达标绿色,未达标灰/红)。</summary>
public sealed class BoolToOkBrushConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly BoolToOkBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is true
            ? new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#4caf50"))
            : new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#ff7043"));

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>可空布尔 → 达成文本(true=已达标 / false=未达标 / null=未知)。</summary>
public sealed class NullableBoolToTextConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly NullableBoolToTextConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value switch
        {
            true => LanguageService.Format("Roles.Met"),
            false => LanguageService.Format("Roles.NotMet"),
            _ => LanguageService.Format("Roles.Unknown"),
        };

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// 主题自适应强调色转换器(参照 WutheringWavesTool GetForegroundColor 的亮度→前景色逻辑)。
/// <para>深色主题(背景暗)返回亮色;浅色主题(背景亮)返回深色,保证对比度。
/// ConverterParameter 可选:缺省=黄调("emphasis"),"cyan"=青调。</para>
/// </summary>
public sealed class ThemeAdaptiveEmphasisBrushConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly ThemeAdaptiveEmphasisBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        bool isDark = ThemeHelper.IsDarkTheme();
        bool cyan = parameter is string p && p.Equals("cyan", StringComparison.OrdinalIgnoreCase);
        return new Avalonia.Media.SolidColorBrush(cyan
            ? (isDark
                ? Avalonia.Media.Color.Parse("#00dde8")  // 深色主题:亮青
                : Avalonia.Media.Color.Parse("#007a85")) // 浅色主题:深青
            : (isDark
                ? Avalonia.Media.Color.Parse("#f8f05c")  // 深色主题:亮黄
                : Avalonia.Media.Color.Parse("#8a6d1f")) // 浅色主题:深琥珀(保黄调+可读)
        );
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>武器星级 → 背景色(5★金 / 4★紫 / 其他灰;参照 WutheringWavesTool weaponBg)。</summary>
public sealed class WeaponStarBrushConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly WeaponStarBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value switch
        {
            5 => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#8a6d1f")),
            4 => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#5b4a8a")),
            _ => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#4a4a4a")),
        };

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>字符串非空判断(用于图标等有值才显示)。</summary>
public sealed class StringNotEmptyConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly StringNotEmptyConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is string s && !string.IsNullOrWhiteSpace(s);

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}