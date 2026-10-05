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
    /// <summary>词条评级(综合副词条有效性与数值,WuWaTools 评分法)。</summary>
    public EchoRatingLevel PhantomStatus { get; init; }
    /// <summary>数值评级(与词条评级同源;保留字段以兼容既有绑定)。</summary>
    public EchoRatingLevel PropStatus { get; init; }
    /// <summary>本声骸得分(0-100,wuwa.uk 评分法;展示一位小数)。</summary>
    public double Score { get; init; }

    /// <summary>评级文本(英文:ACE/SSS/SS/S/N;总评的毕业等级用 GraduationTextOf)。</summary>
    public string PhantomText => EchoRatingService.LevelTextOf(PhantomStatus);
    public string PropText => EchoRatingService.LevelTextOf(PropStatus);
}

/// <summary>角色声骸总评级(5 件声骸得分之和 + 养成达成度)。</summary>
public sealed class RoleEchoRating
{
    /// <summary>总评分(5 件得分之和,0-500;对齐鸣潮工坊的"声骸评分"口径)。</summary>
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
/// 声骸词条评级服务(wuwa.uk 评分法,<see href="https://wuwa.uk/zh/articles/echo-substat-math"/>)。
/// <para>
/// 每条副词条有效值 = (Roll值 ÷ 该词条满值) × 权重;总分 = 有效值之和 ÷ (5 × 首位权重 2.0) × 100。
/// 权重来源优先级:① 攻略站 Wiki「声骸词条」优先级(每角色,<see cref="WikiGuideService"/>,
/// 如「暴击=暴击伤害＞攻击&gt;共鸣技能&gt;共鸣效率」按组序赋权 2.0/1.0/0.75/0.5…);
/// ② 官方 valid(getRoleDetail mainProps/subProps[].valid,核心 2.0 / 其他 1.0);
/// ③ 通用权重表(3/2/1 → 2.0/1.0/0.5)。
/// </para>
/// <para>
/// 等级档位(<b>按单件均分</b>判定,非 5 件总分;阈值见 <see cref="ScoreToStatus"/>):
/// ≥60 → ACE(完美毕业)、≥40 → SSS(毕业)、≥20 → SS(小毕业)、&lt;20 → S(未毕业);
/// 满分 100 = 5 条词条全部为首位权重且 Roll 满。联名/无攻略角色回退 ②③ 权重来源。
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

    /// <summary>各属性满值(挡位表来源:wuwa.uk echo-substat-math —— 百分比词条 8 挡,固定攻击 4 挡 30/40/50/60 满 60,固定防御 4 挡 40/50/60/70 满 70)。</summary>
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

    /// <summary>评级一个声骸(权重来源见类注释;priorityWeights = Wiki「声骸词条」解析结果,可为 null)。</summary>
    public static EchoRating RateEcho(EchoInfo echo, IReadOnlyDictionary<string, double>? priorityWeights)
    {
        var subs = echo.SubProps ?? [];
        double sum = 0.0;
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
            // 权重来源(优先级从高到低):
            // ① 攻略站 Wiki「声骸词条」优先级(每角色;联名/无攻略为 null)
            // ② 官方 valid(getRoleDetail mainProps/subProps[],按角色区分):false = 0,true = 核心 2.0 / 其他 1.0
            // ③ 通用权重表:3 → 2.0,2 → 1.0,1 → 0.5
            double weight;
            if (priorityWeights is { } pw)
            {
                weight = pw.TryGetValue(name, out var w) ? w : 0.3;
            }
            else if (sub.Valid is { } officialValid)
            {
                weight = TierToWeight(officialValid
                    ? Math.Max(2, PropWeights.TryGetValue(name, out var t) ? t : 0)
                    : 0);
            }
            else
            {
                weight = TierToWeight(PropWeights.TryGetValue(name, out var t) ? t : 0);
            }
            if (weight <= 0)
            {
                continue;
            }
            sum += value / max * weight;
        }
        // 归一:5 条全部为首位词条满 Roll = 理论满分(5 × 2.0;现实中全有效完美声骸约 75-85 分)
        var score = sum / 10.0 * 100.0;
        if (score > 100)
        {
            score = 100;
        }
        var status = ScoreToStatus(score);
        return new EchoRating
        {
            PhantomStatus = status,
            PropStatus = status,
            Score = Math.Round(score, 1),
        };
    }

    /// <summary>评分档 → 权重(wuwa.uk 权重域:核心 2.0 / 有效 1.0 / 低价值 0.5)。</summary>
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
    /// 阈值:≥60 → <see cref="EchoRatingLevel.Ace"/>(完美毕业)、≥40 → <see cref="EchoRatingLevel.SSS"/>(毕业)、
    /// ≥20 → <see cref="EchoRatingLevel.SS"/>(小毕业)、其余 → <see cref="EchoRatingLevel.S"/>(未毕业)。
    /// 注意这里的字母是<b>阈值档</b>,与 <see cref="EchoRatingLevel"/> 的枚举名(SSS/SS/S)不是同一套命名,勿混用。
    /// </para>
    /// </summary>
    private static EchoRatingLevel ScoreToStatus(double score)
    {
        if (score >= 60) return EchoRatingLevel.Ace;
        if (score >= 40) return EchoRatingLevel.SSS;
        if (score >= 20) return EchoRatingLevel.SS;
        return EchoRatingLevel.S;
    }

    /// <summary>评级角色的全部声骸:总评分 = 5 件得分之和(0-500),等级按单件均分判定。</summary>
    public static RoleEchoRating RateRole(IEnumerable<EchoInfo> echoes)
    {
        var list = echoes.Where(e => e is not null).ToList();
        var ratings = list.Select(e => RateEcho(e, e.PriorityWeights)).ToList();
        var sum = ratings.Sum(r => r.Score);
        var mean = ratings.Count > 0 ? sum / ratings.Count : 0.0;
        const double max = 500.0;
        var level = ScoreToStatus(mean);
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
}
