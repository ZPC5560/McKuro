using McKuro.Core.Models.Roles;

namespace McKuro.Core.Services.Roles;

/// <summary>声骸/角色评级等级(参照 WutheringWavesTool Phantom.Status 与 mcguide 养成达成度)。</summary>
public enum EchoRatingLevel
{
    None,
    N,
    S,
    SS,
    SSS,
    Ace,
}

/// <summary>单个声骸的评级结果。</summary>
public sealed class EchoRating
{
    /// <summary>词条评级(综合主副词条有效性与数值)。</summary>
    public EchoRatingLevel PhantomStatus { get; init; }
    /// <summary>数值评级(与词条评级同源;保留字段以兼容既有绑定)。</summary>
    public EchoRatingLevel PropStatus { get; init; }
    /// <summary>本声骸综合得分(0-100,展示一位小数)。</summary>
    public double Score { get; init; }

    /// <summary>
    /// 主词条分项(0-100;数据源未提供主词条时为 null,此时综合分退化为纯副词条口径)。
    /// </summary>
    public double? MainScore { get; init; }

    /// <summary>副词条分项(0-100)。</summary>
    public double? SubScore { get; init; }

    /// <summary>
    /// 本单件是否按统一口径(纳入主词条)计分。
    /// <para>
    /// 总评判定档位时的口径门必须与单件一致;直接用"MainProps 是否存在"推断会与
    /// 单件的实际门(mainProps 存在<b>且</b>理想权重可算)脱钩 —— 主词条名全部未知/为空的
    /// 单件会走兼容口径,却仍被按统一阈值判档(或反之)。故由 RateEcho 把实际口径带出来,
    /// RateRole 从单件结果聚合,门不可能漂移。
    /// </para>
    /// </summary>
    public bool IsUnifiedScale { get; init; }

    /// <summary>评级文本(英文:ACE/SSS/SS/S/N;总评的毕业等级用 GraduationTextOf)。</summary>
    public string PhantomText => EchoRatingService.LevelTextOf(PhantomStatus);
    public string PropText => EchoRatingService.LevelTextOf(PropStatus);
}

/// <summary>角色声骸总评级(5 件声骸得分之和 + 养成达成度)。</summary>
public sealed class RoleEchoRating
{
    /// <summary>总评分(5 件得分之和,0-500)。</summary>
    public double TotalScore { get; init; }
    /// <summary>满分(500 = 单件满分 100 × 5)。</summary>
    public double MaxScore { get; init; }
    public EchoRatingLevel Level { get; init; }
    /// <summary>养成毕业达成度(0-100;= 单件均分,已是百分比数值,不再除以 100)。</summary>
    public int AchievementPercent { get; init; }
    public IReadOnlyList<EchoRating> Echoes { get; init; } = [];
    /// <summary>总评的毕业等级文本(未毕业/小毕业/毕业/完美毕业)。</summary>
    public string LevelText => EchoRatingService.GraduationTextOf(Level);
}

/// <summary>
/// 声骸词条评级服务(口径:主副词条统一 0-100 分,100 = 完美声骸)。
///
/// /// <para><b>评分公式</b>(主词条与副词条按“槽位预算等份”合成为一个分数):</para>
/// <code>
/// 副词条分项 subRatio  = Σ(roll值 ÷ 该词条满值 × 权重) ÷ Σ(当前权重下最优 5 条的权重)
/// 主词条分项 mainRatio = Σ(词条值 ÷ 该词条满值 × 权重) ÷ Σ(该槽位的理想权重)
/// 综合分   = (5 × subRatio + 2 × mainRatio) ÷ 7 × 100
/// </code>
/// <para>
/// <b>为什么是 5:2</b>:一个声骸最多 2 个主词条槽位 + 5 个副词条槽位,共 7 个槽位。
/// 每个槽位在总分中占等份预算(1/7),所以主词条合计 2/7、副词条合计 5/7。
/// 该权重经反推与旧口径兼容(见 <see cref="ScoreToStatus"/> 的档位换算说明)。
/// </para>
/// <para>
/// <b>两个分母都取“当前权重来源可达的最优”</b>,不写死常数:这样无论角色用的是 Wiki 优先级
/// 还是兜底通用表,只要声骸相对该角色是完美的就得 100 分,分数才<b>可跨角色比较</b>。
/// 写死分母会让权重表顶档词条多的角色天然高分、少的角色天然低分。
/// </para>
/// <para>
/// <b>权重来源</b>(优先级从高到低,均为本项目自有数据源):
/// ① 攻略站 Wiki「声骸词条」优先级(每角色,<see cref="WikiGuideService"/>,
/// 如「暴击=暴击伤害＞攻击&gt;共鸣技能&gt;共鸣效率」按组序赋权 2.0/1.0/0.75/0.5…);
/// ② 官方 valid(getRoleDetail mainProps/subProps[].valid,核心 2.0 / 其他 1.0);
/// ③ 通用权重表(3/2/1 → 2.0/1.0/0.5)。
/// </para>
/// <para>
/// <b>主词条专有词条的回退</b>:Wiki「声骸词条」表针对的是副词条,不含元素伤害加成/治疗加成
/// 这类只出现在主词条上的词条。若直接套用“Wiki 表命中不到即降权”的规则,3C 元素伤害主词条会被
/// 严重低估。故主词条专有词条回退<b>主词条档位表</b>(<see cref="MainPropTiers"/>):元素伤害加成 = 顶档(2.0)、
/// 治疗加成/共鸣效率 = 有效档(1.0)。
/// </para>
/// <para>
/// <b>等级档位</b>(按单件综合分判定,阈值见 <see cref="ScoreToStatus"/>):
/// ≥71.4 → ACE(完美毕业)、≥57.1 → SSS(毕业)、≥42.9 → SS(小毕业)、&lt;42.9 → S(未毕业)。
/// 满分 100 = 2 个主词条取理想词条且满值 + 5 条副词条全部为顶档权重且满 Roll。
/// 联名/无攻略角色回退 ②③ 权重来源。
/// </para>
/// <para>
/// <b>权重上限与满分的关系</b>:暴击/暴伤 ≤1.5、攻击% ≤1.25 的上限(<see cref="CapStatWeight"/>)
/// 同时作用于分子与分母 —— 统一口径下分母 <see cref="MaxAchievableSubstatWeight"/> 取"最优 5 条词条"
/// 的权重和,通用权重表下为 1.5+1.5+1.25+1.0+1.0=6.25,完美副词条组合恰好把它拿满,
/// 故统一口径任何权重来源都能得 100 分。
/// 但<b>兼容口径</b>(无 mainProps)的分母仍是历史固定值 5×2.0=10,通用权重表下最优 5 条
/// 只能凑出 6.25,故兼容口径 + 通用表的单件封顶 62.5 —— 这是"缺角色专属数据时不敢给高分"的
/// 保守表现:一旦取到 Wiki 优先级(来源 ①)或官方 valid(来源 ②),顶档词条数足够即可到 100。
/// </para>
/// <para>
/// <b>数据缺失的兼容</b>:库街区旧缓存等场景可能没有 mainProps。此时主词条分项不可计算,
/// 综合分退化为<b>纯副词条口径</b>(0-100,阈值回到 43/57/71 换算前的 20/40/60),与历史行为一致。
/// </para>
/// <para>
/// <b>⚠️ 主词条“理想值”必须按角色来,不能取词条池的全局最大权重</b>:4C 词条池里暴击权重最高,
/// 若拿它当分母,则生命/防御/治疗型角色(如莫宁、卡提希娅)即使主词条完全正确也只得 0-33 的
/// 主词条分项,被系统性判低分。故优先使用<b>官方攻略站给出的该角色推荐主词条</b>
/// (<see cref="EchoInfo.RecommendedMainStats"/>);无推荐时退化为“实际所选主词条的权重”作分母
/// (即只评数值是否满,不评词条选择),而不是替玩家假设一个最优词条。
/// </para>
/// </summary>
public static class EchoRatingService
{
    /// <summary>
    /// 通用词条权重(0-3):<b>仅作兜底</b>(Wiki 优先级与官方 valid 均缺失时),
    /// 换算权重:3 → 2.0,2 → 1.0,1 → 0.5。
    /// </summary>
    private static readonly IReadOnlyDictionary<string, int> PropWeights = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["暴击伤害"] = 3,
        ["暴击"] = 3,
        ["攻击百分比"] = 3,
        ["生命百分比"] = 2,
        ["共鸣效率"] = 2,
        ["普攻伤害加成"] = 2,
        ["重击伤害加成"] = 2,
        ["共鸣技能伤害加成"] = 2,
        ["共鸣解放伤害加成"] = 2,
        ["谐度破坏伤害加成"] = 2,
        ["攻击"] = 2,
        ["防御百分比"] = 1,
        ["生命"] = 1,
        ["防御"] = 1,
    };

    /// <summary>
    /// 主词条专有词条的档位(只出现在主词条上、不在 <see cref="PropWeights"/> 副词条表里的词条)。
    /// <para>元素伤害加成是输出角色的首选 3C 主词条,给顶档(3 → 2.0);治疗加成给有效档(2 → 1.0)。
    /// 这是在“Wiki 表不收这些词条”前提下,让主词条分项不被系统性低估的兜底。</para>
    /// </summary>
    private static readonly IReadOnlyDictionary<string, int> MainPropTiers = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["冷凝伤害加成"] = 3,
        ["热熔伤害加成"] = 3,
        ["导电伤害加成"] = 3,
        ["气动伤害加成"] = 3,
        ["衍射伤害加成"] = 3,
        ["湮灭伤害加成"] = 3,
        ["治疗效果加成"] = 2,
    };

    /// <summary>各属性满值(副词条挡位表;百分比词条 8 挡,固定攻击 4 挡 30/40/50/60 满 60,固定防御 4 挡 40/50/60/70 满 70)。</summary>
    private static readonly IReadOnlyDictionary<string, double> PropMaxValue = new Dictionary<string, double>(StringComparer.Ordinal)
    {
        ["暴击伤害"] = 21.0,
        ["暴击"] = 10.5,
        ["攻击"] = 60.0,
        ["攻击百分比"] = 11.6,
        ["生命"] = 580.0,
        ["生命百分比"] = 11.6,
        ["防御"] = 70.0,
        ["防御百分比"] = 14.7,
        ["共鸣效率"] = 12.4,
        ["普攻伤害加成"] = 11.6,
        ["重击伤害加成"] = 11.6,
        ["共鸣技能伤害加成"] = 11.6,
        ["共鸣解放伤害加成"] = 11.6,
        ["谐度破坏伤害加成"] = 11.6,
    };

    /// <summary>
    /// 主词条满值(5★ +25;仅作主词条“是否满值”的换算基准)。
    /// <para>
    /// 数值取自游戏内可查的<b>公开资料</b>(主词条固定值:1C 生命 2280 / 3C 攻击 100 / 4C 攻击 150;
    /// 百分比与暴击档位:1C 攻击 18.0%·生命 22.8%·防御 18.0%、3C 各 30%(防御 38%、共鸣效率 32%)、
    /// 4C 攻击 33%·生命 33%·防御 41.5%·暴击 22%·暴击伤害 44%·治疗 26%)。
    /// </para>
    /// <para>⚠️ 1C 防御% 满值(18.0%)不同资料间存在分歧(另有 22.8% 的说法),此处采用资料站表格值。</para>
    /// </summary>
    private static readonly IReadOnlyDictionary<int, IReadOnlyDictionary<string, double>> MainPropMaxValue =
        new Dictionary<int, IReadOnlyDictionary<string, double>>
        {
            [1] = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["攻击百分比"] = 18.0,
                ["生命百分比"] = 22.8,
                ["防御百分比"] = 18.0,
                ["生命"] = 2280.0,
            },
            [3] = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["攻击百分比"] = 30.0,
                ["生命百分比"] = 30.0,
                ["防御百分比"] = 38.0,
                ["共鸣效率"] = 32.0,
                ["冷凝伤害加成"] = 30.0,
                ["热熔伤害加成"] = 30.0,
                ["导电伤害加成"] = 30.0,
                ["气动伤害加成"] = 30.0,
                ["衍射伤害加成"] = 30.0,
                ["湮灭伤害加成"] = 30.0,
                ["攻击"] = 100.0,
            },
            [4] = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["攻击百分比"] = 33.0,
                ["生命百分比"] = 33.0,
                ["防御百分比"] = 41.5,
                ["暴击"] = 22.0,
                ["暴击伤害"] = 44.0,
                ["治疗效果加成"] = 26.0,
                ["攻击"] = 150.0,
            },
        };

    /// <summary>顶档权重(Wiki 首组权重;同时是副词条分项的满分基准)。</summary>
    private const double TopWeight = 2.0;

    /// <summary>副词条槽位数。</summary>
    private const int SubstatSlots = 5;

    /// <summary>主词条槽位数(上限;数据只有 1 条主词条时按实际条数计)。</summary>
    private const int MainstatSlots = 2;

    /// <summary>
    /// 每槽位在总分中占的等份预算(槽位总数 = 2 主 + 5 副 = 7)。
    /// <para>合成公式按 <c>MainstatSlots * mainRatio * <see cref="SlotBudget"/> +
    /// SubstatSlots * subRatio * <see cref="SlotBudget"/></c> 展开,常量直接参与计算。</para>
    /// </summary>
    private const double SlotBudget = 100.0 / (MainstatSlots + SubstatSlots);

    /// <summary>
    /// 统一口径档位阈值:由旧副词条口径 20/40/60 按 5:2 槽位预算<b>精确</b>换算
    /// (score = legacy × 5/7 + 200/7),再取一位小数以与展示分数同刻度。
    /// <para>20 → 42.857 → 42.9;40 → 57.143 → 57.1;60 → 71.429 → 71.4。</para>
    /// </summary>
    private const double UnifiedAceThreshold = 71.4;
    private const double UnifiedSssThreshold = 57.1;
    private const double UnifiedSsThreshold = 42.9;

    /// <summary>纯副词条口径(数据源无 mainProps)的档位阈值,保持历史行为。</summary>
    private const double LegacyAceThreshold = 60.0;
    private const double LegacySssThreshold = 40.0;
    private const double LegacySsThreshold = 20.0;

    /// <summary>四类技能伤害词条(主词条不出现,只作副词条;用于技能级系数)。</summary>
    public static readonly IReadOnlyList<string> SkillDamageStats =
    [
        "普攻伤害加成",
        "重击伤害加成",
        "共鸣技能伤害加成",
        "共鸣解放伤害加成",
    ];

    /// <summary>词条重要度(0-3,通用权重表兜底;供装饰条/高亮使用,有效判定见 EchoProp.EffectiveLevel)。</summary>
    public static int GetPropLevel(string attributeName, string? attributeValue)
    {
        if (string.IsNullOrWhiteSpace(attributeName))
        {
            return 0;
        }
        var name = NormalizePropName(attributeName, attributeValue);
        return PropWeights.TryGetValue(name, out var w) ? w : 0;
    }

    /// <summary>
    /// 评级一个声骸(权重来源见类注释;priorityWeights = Wiki「声骸词条」解析结果,可为 null)。
    /// </summary>
    /// <param name="echo">待评级声骸。</param>
    /// <param name="priorityWeights">Wiki 优先级权重(每角色);为 null 时依次回退官方 valid / 通用权重表。</param>
    /// <param name="skillCoefficients">
    /// 技能伤害角色级系数(可空):把四类技能伤害词条的权重按角色再细分,例如某角色只吃共鸣解放时
    /// 传 { ["共鸣解放伤害加成"] = 1.0, ["普攻伤害加成"] = 0.1 }。缺省(或未列出的技能)系数为 1.0,
    /// 即不改变既有权重。
    /// <para>
    /// 注意:本项目<b>不内置</b>按角色的技能系数表(那需要逐角色的机制数据)。Wiki 优先级表本身已能
    /// 按角色区分具体技能权重的场合无需使用该参数;该参数用于 Wiki 未收录、而调用方自有数据的角色。
    /// </para>
    /// </param>
    public static EchoRating RateEcho(
        EchoInfo echo,
        IReadOnlyDictionary<string, double>? priorityWeights,
        IReadOnlyDictionary<string, double>? skillCoefficients = null)
    {
        var subs = echo.SubProps ?? [];
        var subsSum = 0.0;
        foreach (var sub in subs)
        {
            if (string.IsNullOrEmpty(sub.AttributeName))
            {
                continue;
            }
            // 属性名规范化:攻击/生命/防御 且值含 % → 视为百分比词条
            var name = NormalizePropName(sub.AttributeName, sub.AttributeValue);
            var max = PropMaxValue.TryGetValue(name, out var m) ? m : 0;
            if (max <= 0)
            {
                continue;
            }
            var value = ParseValue(sub.AttributeValue);
            var weight = ResolveSubstatWeight(name, sub, priorityWeights, skillCoefficients);
            if (weight <= 0)
            {
                continue;
            }
            subsSum += value / max * weight;
        }

        // 副词条分项达标度分母 = 当前权重来源下"5 条最优词条满 Roll"的权重上限。
        // 必须随权重来源变化:固定分母会让权重表顶档词条多的角色拿高分、少的角色拿低分,
        // 即便两件声骸的相对质量完全相同 —— 那样分数就不可跨角色比较了。
        // 传入本声骸的真实副词条:分母的"完美声骸假设"必须与分子同源(见方法注释),
        // 否则官方 valid 与优先级表共存时分子/分母口径错位,分项被虚高或压低。
        var subIdeal = MaxAchievableSubstatWeight(echo.SubProps, priorityWeights, skillCoefficients);
        var subRatio = subIdeal > 0 ? Clamp01(subsSum / subIdeal) : 0.0;

        // 主词条分项:数据源(旧缓存/其他来源)可能没有 mainProps
        var (mainRatio, mainIdealWeight) = ComputeMainRatio(echo, priorityWeights);
        var hasMain = mainIdealWeight > 0;

        double score;
        EchoRatingLevel status;
        if (hasMain)
        {
            // 统一口径:按槽位预算等份合成(主 2 份 + 副 5 份);100 = 完美声骸
            score = MainstatSlots * mainRatio * SlotBudget + SubstatSlots * subRatio * SlotBudget;
            score = Math.Clamp(score, 0.0, 100.0);
            // 先取整到展示精度再判档,保证“看到的分数”与“拿到的等级”永远一致
            var unifiedRounded = Math.Round(score, 1);
            status = ScoreToStatus(unifiedRounded, true);
            return new EchoRating
            {
                PhantomStatus = status,
                PropStatus = status,
                Score = unifiedRounded,
                MainScore = Math.Round(mainRatio * 100.0, 1),
                SubScore = Math.Round(subRatio * 100.0, 1),
                IsUnifiedScale = true,
            };
        }

        // 兼容口径:无主词条数据 → 纯副词条 0-100,沿用历史固定分母与历史阈值(不改变旧缓存表现)
        var legacyRatio = Clamp01(subsSum / (SubstatSlots * TopWeight));
        var legacyScore = Math.Round(Math.Clamp(legacyRatio * 100.0, 0.0, 100.0), 1);
        return new EchoRating
        {
            PhantomStatus = ScoreToStatus(legacyScore, false),
            PropStatus = ScoreToStatus(legacyScore, false),
            Score = legacyScore,
            MainScore = null,
            SubScore = legacyScore,
            IsUnifiedScale = false,
        };
    }

    /// <summary>
    /// 当前权重来源下,5 个副词条槽位可达的权重上限(取权重最高的 5 条<b>不同</b>词条之和;
    /// 声骸的同名词条不会重复出现,故用 distinct 口径)。
    /// <para>这是副词条分项 100 分的基准:它使不同角色(权重表不同)的“完美声骸”都等于 100,
    /// 从而分数可跨角色比较。</para>
    /// <para>
    /// <b>口径必须与分子同源</b>(2026-10 评审修复):"完美声骸"是分子恰好拿满的形态 ——
    /// <list type="number">
    /// <item>本声骸<b>带</b>官方 valid 数据时,探针词条取 <c>Valid=true</c>(valid=true 才能拿满),
    ///       且被该角色判为无效(valid=false)的词条<b>从理想集剔除</b> —— 分子给 0 的词条不该占分母;</para>
    /// <item>本声骸<b>不带</b> valid 数据(null)时,探针保持 null —— 与分子的回退分支同源,
    ///       不改变既有行为(未带 valid 的调用点分数不变)。</item>
    /// </list>
    /// 修复前探针恒为 Valid=null:Wiki 表未收录但 valid=true 的词条分子 1.0 / 分母 0.3,
    /// 半 Roll 也能被 Clamp 到 100(虚高);valid=false 的词条分子 0 却仍占分母(压分)。
    /// </para>
    /// <para>参数 <paramref name="subProps"/> 为本声骸的真实副词条(可 null);只需它的 Valid 判定。</para>
    /// </summary>
    private static double MaxAchievableSubstatWeight(
        List<EchoProp>? subProps,
        IReadOnlyDictionary<string, double>? priorityWeights,
        IReadOnlyDictionary<string, double>? skillCoefficients)
    {
        var hasValidity = subProps is { Count: > 0 } props && props.Any(p => p?.Valid is not null);
        var invalidNames = hasValidity
            ? (subProps ?? []).Where(p => p?.Valid == false)
                .Select(p => NormalizePropName(p.AttributeName, p.AttributeValue))
                .ToHashSet(StringComparer.Ordinal)
            : null;

        var weights = new List<double>(PropMaxValue.Count);
        foreach (var name in PropMaxValue.Keys)
        {
            // 与分子同源的探针词条:带 valid 数据的角色按"该角色认可有效"假设理想权重;
            // 该角色已被判无效的词条不可能出现在完美声骸里,直接从理想集剔除
            var probe = hasValidity
                ? (invalidNames is not null && invalidNames.Contains(name)
                    ? new EchoProp { AttributeName = name, Valid = false }
                    : new EchoProp { AttributeName = name, Valid = true })
                : new EchoProp { AttributeName = name };
            weights.Add(ResolveSubstatWeight(name, probe, priorityWeights, skillCoefficients));
        }
        return weights.OrderByDescending(w => w).Take(SubstatSlots).Sum();
    }

    /// <summary>
    /// 计算主词条分项达成度(0-1)与理想权重合计。
    /// <para>
    /// 主词条数值随等级成长,满级(+25)时即为该词条满值,故分项同时体现<b>数值是否满</b>与
    /// <b>词条选择是否合理</b>。
    /// </para>
    /// <para>
    /// <b>槽位理想权重的取法</b>:第 1 个主词条槽位对玩家是<b>可自由选择</b>的,其理想值取
    /// 「该角色推荐主词条中的最大权重」(<see cref="MaxRecommendedMainWeight"/>)——这样对治疗/
    /// 防御向角色同样公平(若官方 valid 已把暴击/暴伤标为无效,池内最优自然落到治疗加成上)。
    /// 其余槽位(3C/4C 的固定攻击、1C 的固定生命)玩家不可选,用其自身权重。
    /// </para>
    /// <para>返回的理想权重合计为 0 表示无法计算(无 mainProps),调用方需回退兼容口径。</para>
    /// </summary>
    private static (double Ratio, double IdealWeight) ComputeMainRatio(
        EchoInfo echo,
        IReadOnlyDictionary<string, double>? priorityWeights)
    {
        var mains = echo.MainProps;
        if (mains is null || mains.Count == 0)
        {
            return (0.0, 0.0);
        }

        var cost = echo.Cost;
        // 未知 COST(表只覆盖 1/3/4;畸形或未来数据可能给出别的值)不做"按满值处理"——
        // 那会把主分项整段白送(约 28.6 分)。缺表说明主词条数值无法校验,
        // 该单件回退兼容口径(纯副词条),与"无 mainProps"同等对待。
        var maxTable = MainPropMaxValue.TryGetValue(cost, out var t) ? t : null;
        if (maxTable is null)
        {
            return (0.0, 0.0);
        }

        var quality = 0.0;
        var ideal = 0.0;
        // 理想主词条权重:有官方推荐 → 推荐集中的最大权重;无推荐 → 不替玩家假设最优词条
        var recommended = echo.RecommendedMainStats;
        var hasRecommendation = recommended is { Count: > 0 };
        var idealWeight = hasRecommendation
            ? MaxRecommendedMainWeight(recommended!, priorityWeights)
            : 0.0;

        for (var i = 0; i < mains.Count && i < MainstatSlots; i++)
        {
            var prop = mains[i];
            if (string.IsNullOrEmpty(prop.AttributeName))
            {
                continue;
            }
            var name = NormalizePropName(prop.AttributeName, prop.AttributeValue);
            var weight = ResolveMainstatWeight(name, prop, priorityWeights);
            double slotIdeal;
            if (i == 0)
            {
                // 首个主词条玩家可自由选择:
                //   有推荐 → 分母取推荐集最优;不在推荐集内的词条权重记 0(选了不该选的主词条要扣分)
                //   无推荐 → 分母取自身权重,即只评“数值是否满”,不评“词条选择”
                // 后者是关键:若此处改用词条池全局最大值,生命/防御/治疗型角色会被系统性判低分。
                slotIdeal = hasRecommendation ? idealWeight : weight;
                // 推荐集与主词条名统一规范化后比较(攻略站给「攻击%」,内部用「攻击百分比」)
                if (hasRecommendation && !recommended!.Contains(NormalizeMainStatName(name)))
                {
                    weight = 0.0;
                }
            }
            else
            {
                // 其余槽位(3C/4C 固定攻击、1C 固定生命)玩家不可选,分母用其自身权重
                slotIdeal = weight;
            }
            ideal += slotIdeal;
            if (slotIdeal <= 0)
            {
                continue;
            }
            // 满级主词条即为满值;缺值时按满值处理,避免把"未知"算成"不合格"(maxTable 已确认存在)
            var ratio = 1.0;
            if (maxTable.TryGetValue(name, out var max) && max > 0)
            {
                ratio = Clamp01(ParseValue(prop.AttributeValue) / max);
            }
            quality += ratio * weight;
        }

        return ideal <= 0 ? (0.0, 0.0) : (Clamp01(quality / ideal), ideal);
    }

    /// <summary>
    /// 该角色推荐主词条中的最大权重(即“选对主词条”的上限)。
    /// <para>推荐集来自官方攻略站的每角色推荐配装(<see cref="EchoInfo.RecommendedMainStats"/>),
    /// 已由调用方按该声骸的 COST 过滤。这样莫宁/守岸人(推荐治疗加成)、卡提希娅(推荐生命%)等
    /// 非暴击向角色,其正确主词条同样能拿满分,而不是被拿去和暴击比。</para>
    /// <para>计算理想权重时不应用 valid(见 <see cref="ResolveMainstatWeight"/> 的 applyValidity)。</para>
    /// </summary>
    private static double MaxRecommendedMainWeight(
        IReadOnlySet<string> recommended,
        IReadOnlyDictionary<string, double>? priorityWeights)
    {
        var best = 0.0;
        foreach (var name in recommended)
        {
            var weight = ResolveMainstatWeight(
                name, new EchoProp { AttributeName = name }, priorityWeights, applyValidity: false);
            if (weight > best)
            {
                best = weight;
            }
        }
        return best > 0 ? best : TopWeight;
    }

    /// <summary>
    /// 把官方/外部来源的主词条名规范化到评分内部使用的词条名。
    /// <para>
    /// 攻略站 <c>echoAttributes[].attribute</c> 返回的是 UI 文案形式:「攻击%」「生命%」「防御%」,
    /// 而评分内部统一用「攻击百分比」「生命百分比」「防御百分比」(<see cref="NormalizePropName"/> 的口径)。
    /// 不做这层映射,推荐主词条会全部匹配不上,判定直接失效。
    /// </para>
    /// <para>
    /// ⚠️ <b>推荐集匹配是静默白名单</b>:推荐集里存在未覆盖的别名时,对应主词条会被记 0 权重
    /// (选错主词条的惩罚),且无任何诊断 —— 故这里要尽量收全数据源的别名
    /// (暴击率/暴击几率的替身写法、全角 ％、无"效果"的治疗简称)。
    /// 新增别名时同步扩展 <c>Recommended_Main_Stat_Names_Are_Normalized</c> 测试。
    /// </para>
    /// </summary>
    public static string NormalizeMainStatName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "";
        }
        var trimmed = name.Trim().Replace('％', '%');
        return trimmed switch
        {
            "攻击%" => "攻击百分比",
            "生命%" => "生命百分比",
            "防御%" => "防御百分比",
            "暴击率" => "暴击",
            "治疗加成" => "治疗效果加成",
            _ => trimmed,
        };
    }

    /// <summary>
    /// 副词条权重。层次(用户定案 2026-10):
    /// <list type="number">
    /// <item><b>官方 valid 是“是否有效”的唯一权威</b>:<c>valid=false</c> 一律权重 0(任何优先级表都不能翻案);</item>
    /// <item><c>valid=true</c> 的词条之间,由优先级表(Wiki / mcguide)细化相对权重;表未收录则按“有效档”(1.0);</item>
    /// <item><c>valid=null</c>(库街区未返回,如相里要/折枝/漂泊者/安可等)时,同样先用优先级表,
    ///       其次才回退通用权重表 —— 即优先级表是 valid 缺失时的替代来源;</item>
    /// <item>无优先级表且无 valid → 通用权重表兜底。</item>
    /// </list>
    /// <para>最后统一施加 <see cref="CapStatWeight"/> 的权重上限,再乘技能伤害角色级系数。</para>
    /// </summary>
    private static double ResolveSubstatWeight(
        string name,
        EchoProp prop,
        IReadOnlyDictionary<string, double>? priorityWeights,
        IReadOnlyDictionary<string, double>? skillCoefficients)
    {
        // ① 官方判定为无效 → 不参与计分(最高优先级,不受任何权重表影响)
        if (prop.Valid == false)
        {
            return 0.0;
        }

        double weight;
        if (priorityWeights is { } pw)
        {
            // ② 优先级表细化有效词条之间的相对权重
            weight = pw.TryGetValue(name, out var w)
                ? w
                // 官方认可有效、但优先级表未收录:给“有效档”,而非当作次要词条降权
                : prop.Valid == true ? TierToWeight(2) : 0.3;
        }
        else if (prop.Valid == true)
        {
            weight = TierToWeight(Math.Max(2, PropWeights.TryGetValue(name, out var t) ? t : 0));
        }
        else
        {
            weight = TierToWeight(PropWeights.TryGetValue(name, out var t) ? t : 0);
        }

        weight = CapStatWeight(name, weight);

        // 技能伤害角色级系数:仅作用于四类技能伤害词条,未配置时系数 1.0(不改变既有权重)
        if (weight > 0 && skillCoefficients is { Count: > 0 } coeffs && SkillDamageStats.Contains(name))
        {
            var coefficient = coeffs.TryGetValue(name, out var c) ? c : 1.0;
            weight *= Math.Max(0.0, coefficient);
        }

        return weight;
    }

    /// <summary>
    /// 词条权重的统一上限(用户定案 2026-10):
    /// <b>暴击 / 暴击伤害 不超过 1.5</b>(原顶档 2.0,下调 0.5);
    /// <b>攻击% 不超过 1.25</b> —— 必须同时压低攻击%,否则它(原同为 2.0)会反超双暴,
    /// 与“双暴保持相对领先”的目标相反。
    /// <para>用“上限”而不是“固定值”,可保留各来源(Wiki / mcguide / 官方 valid / 通用表)之间
    /// 既有的相对排序:例如优先级表给暴伤 1.0 时不会被抬高到 1.5。</para>
    /// </summary>
    private static double CapStatWeight(string name, double weight) => name switch
    {
        "暴击" or "暴击伤害" => Math.Min(weight, CritWeightCap),
        "攻击百分比" => Math.Min(weight, AttackPercentWeightCap),
        _ => weight,
    };

    /// <summary>暴击 / 暴击伤害的权重上限(原 2.0 → 1.5)。</summary>
    private const double CritWeightCap = 1.5;

    /// <summary>攻击% 的权重上限(原 2.0 → 1.25;须低于双暴以保持其相对领先)。</summary>
    private const double AttackPercentWeightCap = 1.25;

    /// <summary>
    /// 主词条权重。
    /// <para>
    /// Wiki 优先级表只覆盖副词条可出现的词条,故主词条专有词条(元素伤害/治疗)
    /// 回退 <see cref="MainPropTiers"/> 档位表,而非按“Wiki 表未命中”降权。
    /// </para>
    /// </summary>
    /// <param name="applyValidity">
    /// 是否应用官方 valid 判定。计算“理想权重”时须传 false:否则一条被标为无效的主词条会把
    /// 自身预算也一并缩小,反而拿满分(等于不扣分)。
    /// <para>注意<b>权重上限始终生效</b>(<see cref="CapStatWeight"/>):它是全局口径,
    /// 与“是否应用 valid”是两件事。若只在上限一侧(实际权重)生效而另一侧(理想权重)不生效,
    /// 分子分母口径不一致,分项会系统性偏低。</para>
    /// </param>
    private static double ResolveMainstatWeight(
        string name,
        EchoProp prop,
        IReadOnlyDictionary<string, double>? priorityWeights,
        bool applyValidity = true)
    {
        if (applyValidity && prop.Valid == false)
        {
            return 0.0;
        }
        if (priorityWeights is { } pw && pw.TryGetValue(name, out var w))
        {
            // 主词条同样受权重上限约束,保证同一词条在主/副词条上的相对地位一致
            return CapStatWeight(name, w);
        }
        // 主词条专有词条 / Wiki 表未收录:走档位表
        var tier = MainPropTiers.TryGetValue(name, out var extra)
            ? extra
            : PropWeights.TryGetValue(name, out var t) ? t : 0;
        if (applyValidity && prop.Valid == true)
        {
            tier = Math.Max(2, tier);
        }
        return CapStatWeight(name, TierToWeight(tier));
    }

    /// <summary>评分档 → 权重(权重域:顶档 2.0 / 有效 1.0 / 低价值 0.5)。</summary>
    private static double TierToWeight(int tier) => tier switch
    {
        >= 3 => 2.0,
        2 => 1.0,
        1 => 0.5,
        _ => 0.0,
    };

    /// <summary>
    /// 单件评分(0-100)→ 毕业等级。
    /// <para>
    /// <b>统一口径</b>(有主词条数据,阈值 71/57/43):由旧副词条口径 20/40/60 按 5:2 槽位预算换算而来 ——
    /// 主词条满值时综合分 = (5 × subRatio + 2 × 1) ÷ 7 × 100 = 0.714 × 副词条分 + 28.6,
    /// 故 20 → 42.9、40 → 57.1、60 → 71.4,取整为 43/57/71。这样档位的判定语义与旧口径一致
    /// (同一件声骸的副词条质量对应同一档),只是分数刻度因纳入主词条而上移。
    /// </para>
    /// <para>
    /// <b>兼容口径</b>(数据源无 mainProps,阈值 20/40/60):分数本身就是纯副词条 0-100,沿用历史阈值。
    /// </para>
    /// <para>
    /// 注意这里的字母是<b>阈值档</b>,与 <see cref="EchoRatingLevel"/> 的枚举名(SSS/SS/S)不是同一套命名,勿混用。
    /// </para>
    /// </summary>
    private static EchoRatingLevel ScoreToStatus(double score, bool hasMain)
    {
        var (ace, sss, ss) = hasMain
            ? (UnifiedAceThreshold, UnifiedSssThreshold, UnifiedSsThreshold)
            : (LegacyAceThreshold, LegacySssThreshold, LegacySsThreshold);
        if (score >= ace) return EchoRatingLevel.Ace;
        if (score >= sss) return EchoRatingLevel.SSS;
        if (score >= ss) return EchoRatingLevel.SS;
        return EchoRatingLevel.S;
    }

    /// <summary>评级角色的全部声骸:总评分 = 5 件得分之和(0-500),等级按单件均分判定。</summary>
    public static RoleEchoRating RateRole(IEnumerable<EchoInfo> echoes)
    {
        var list = echoes.Where(e => e is not null).ToList();
        // 与单件评级保持同一入口口径(同时带上 PriorityWeights 与 SkillCoefficients),
        // 否则总评会漏掉技能级系数,出现“单件加起来 ≠ 总评”。
        var ratings = list.Select(e => RateEcho(e, e.PriorityWeights, e.SkillCoefficients)).ToList();
        var sum = ratings.Sum(r => r.Score);
        var mean = ratings.Count > 0 ? sum / ratings.Count : 0.0;
        const double max = 500.0;
        // 等级口径与单件一致:由单件实际使用的口径聚合(见 EchoRating.IsUnifiedScale),
        // 不能用"MainProps 是否存在"重新推断 —— 那会与单件的实际门脱钩。
        // 混合口径(部分单件有 mainProps、部分没有,旧缓存与新数据并存时可达):
        // 按多数口径判档,并保证"全部单件走兼容口径"时一定回到历史阈值。
        var unifiedCount = ratings.Count(r => r.IsUnifiedScale);
        var hasMain = unifiedCount * 2 > ratings.Count;
        var level = ScoreToStatus(mean, hasMain);
        int percent = (int)(mean);
        if (percent > 100) percent = 100;
        return new RoleEchoRating
        {
            TotalScore = Math.Round(sum, 1),
            MaxScore = max,
            Level = level,
            AchievementPercent = percent,
            Echoes = ratings,
        };
    }

    /// <summary>评级 → 文本(英文:ACE/SSS/SS/S/N;单条声骸徽章用)。</summary>
    public static string LevelTextOf(EchoRatingLevel level) => level switch
    {
        EchoRatingLevel.Ace => "ACE",
        EchoRatingLevel.SSS => "SSS",
        EchoRatingLevel.SS => "SS",
        EchoRatingLevel.S => "S",
        EchoRatingLevel.N => "N",
        _ => "-",
    };

    /// <summary>
    /// 评级 → 毕业等级文本(未毕业/小毕业/毕业/完美毕业;总评行用,用户定案)。
    /// <para>
    /// 经 <see cref="CoreStrings.T"/> 走本地化(应用层注册 Resolver 后按当前语言取值);
    /// fallback 保持中文原文,使未初始化语言服务的单元测试仍得到稳定结果。
    /// </para>
    /// </summary>
    public static string GraduationTextOf(EchoRatingLevel level) => level switch
    {
        EchoRatingLevel.Ace => CoreStrings.T("Roles.EchoGrade.Ace", "完美毕业"),
        EchoRatingLevel.SSS => CoreStrings.T("Roles.EchoGrade.SSS", "毕业"),
        EchoRatingLevel.SS => CoreStrings.T("Roles.EchoGrade.SS", "小毕业"),
        EchoRatingLevel.S => CoreStrings.T("Roles.EchoGrade.S", "未毕业"),
        EchoRatingLevel.N => CoreStrings.T("Roles.EchoGrade.S", "未毕业"),
        _ => CoreStrings.T("Roles.EchoGrade.S", "未毕业"),
    };

    /// <summary>攻击/生命/防御 且值含 % 时视为百分比词条(对齐 WutheringWavesTool)。</summary>
    private static string NormalizePropName(string name, string? value)
    {
        if ((name is "攻击" or "生命" or "防御") && value is { } v && v.Contains('%', StringComparison.Ordinal))
        {
            return name + "百分比";
        }
        return name;
    }

    /// <summary>解析词条数值("11.6%" → 11.6,"45" → 45)。</summary>
    private static double ParseValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }
        var s = value.Trim().Replace("%", "").Trim();
        return double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0;
    }

    private static double Clamp01(double value) => double.IsNaN(value) ? 0.0 : Math.Clamp(value, 0.0, 1.0);
}
