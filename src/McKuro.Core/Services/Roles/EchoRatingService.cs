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
    /// <summary>词条结构评级(有效词条数量/权重结构)。</summary>
    public EchoRatingLevel PhantomStatus { get; init; }
    /// <summary>词条数值评级(词条数值相对满值的达成)。</summary>
    public EchoRatingLevel PropStatus { get; init; }
    /// <summary>本声骸得分(2-10 = 词条结构 1-5 + 数值 1-5)。</summary>
    public int Score { get; init; }

    /// <summary>评级文本(如 ACE/SSS/SS/S/N)。</summary>
    public string PhantomText => EchoRatingService.LevelTextOf(PhantomStatus);
    public string PropText => EchoRatingService.LevelTextOf(PropStatus);
}

/// <summary>角色声骸总评级(5 件声骸得分汇总 + 养成达成度)。</summary>
public sealed class RoleEchoRating
{
    public int TotalScore { get; init; }
    public int MaxScore { get; init; }
    public EchoRatingLevel Level { get; init; }
    /// <summary>养成毕业达成度(0-100%,总分/满分)。</summary>
    public int AchievementPercent { get; init; }
    public IReadOnlyList<EchoRating> Echoes { get; init; } = [];
    public string LevelText => EchoRatingService.LevelTextOf(Level);
}

/// <summary>
/// 声骸词条评级服务。
/// <para>算法对齐 WutheringWavesTool <c>OwnRoleDetailViewModel</c> 的
/// <c>scorePhantomStatus</c> / <c>scorePropStatus</c> / <c>scoreToStatus</c>,
/// 词条权重用通用权重表(未引入每角色社区权重 JSON)。</para>
/// </summary>
public static class EchoRatingService
{
    /// <summary>通用词条权重(0-3):越核心权重越高(暴击/暴伤/攻击% = 3)。</summary>
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
        ["攻击"] = 2,
        ["防御百分比"] = 1,
        ["生命"] = 1,
        ["防御"] = 1,
    };

    /// <summary>各属性满值(百分比词条用;对齐 WutheringWavesTool propMaxValueMap)。</summary>
    private static readonly IReadOnlyDictionary<string, double> PropMaxValue = new Dictionary<string, double>(StringComparer.Ordinal)
    {
        ["暴击伤害"] = 21.0,
        ["暴击"] = 10.5,
        ["攻击"] = 60.0,
        ["攻击百分比"] = 11.6,
        ["生命"] = 580.0,
        ["生命百分比"] = 11.6,
        ["防御"] = 60.0,
        ["防御百分比"] = 14.7,
        ["共鸣效率"] = 12.4,
        ["普攻伤害加成"] = 11.6,
        ["重击伤害加成"] = 11.6,
        ["共鸣技能伤害加成"] = 11.6,
        ["共鸣解放伤害加成"] = 11.6,
    };

    /// <summary>词条重要度(0-3):按通用权重表计算,供装饰条/高亮使用。</summary>
    public static int GetPropLevel(string attributeName, string? attributeValue)
    {
        if (string.IsNullOrWhiteSpace(attributeName))
        {
            return 0;
        }
        var name = NormalizePropName(attributeName, attributeValue);
        return PropWeights.TryGetValue(name, out var w) ? w : 0;
    }

    /// <summary>评级一个声骸。</summary>
    public static EchoRating RateEcho(EchoInfo echo)
    {
        var subs = echo.SubProps ?? [];
        int level3 = 0, level2 = 0, level1 = 0;
        double subCount = 0.0;
        foreach (var sub in subs)
        {
            if (string.IsNullOrEmpty(sub.AttributeName))
            {
                continue;
            }
            // 属性名规范化:攻击/生命/防御 且值含 % → 视为百分比词条
            var name = NormalizePropName(sub.AttributeName, sub.AttributeValue);
            int level = PropWeights.TryGetValue(name, out var w) ? w : 0;
            double value = ParseValue(sub.AttributeValue);
            double max = PropMaxValue.TryGetValue(name, out var m) ? m : 0;
            if (max <= 0)
            {
                continue;
            }
            double percent = value / max;
            if (level == 3)
            {
                level3++;
                subCount += percent;
            }
            else if (level == 2)
            {
                level2++;
                subCount += percent;
            }
            else if (level == 1)
            {
                level1++;
                subCount += percent;
            }
        }

        var phantom = ScorePhantomStatus(level3, level2, level1);
        var prop = ScorePropStatus(level3, level1, subCount);
        return new EchoRating
        {
            PhantomStatus = phantom,
            PropStatus = prop,
            Score = ScoreValue(phantom) + ScoreValue(prop),
        };
    }

    /// <summary>评级角色的全部声骸,给出总分与养成达成度。</summary>
    public static RoleEchoRating RateRole(IEnumerable<EchoInfo> echoes)
    {
        var list = echoes.Where(e => e is not null).ToList();
        var ratings = list.Select(RateEcho).ToList();
        int total = ratings.Sum(r => r.Score);
        const int max = 50; // 5 声骸 × (结构 5 + 数值 5)
        var level = ScoreToStatus(total);
        int percent = total * 100 / max;
        if (percent > 100) percent = 100;
        return new RoleEchoRating
        {
            TotalScore = total,
            MaxScore = max,
            Level = level,
            AchievementPercent = percent,
            Echoes = ratings,
        };
    }

    // ---- 评分算法(对齐 WutheringWavesTool) ----

    private static EchoRatingLevel ScorePhantomStatus(int level3, int level2, int level1)
    {
        int sum = level2 + level1;
        if (level3 == 2 && sum == 3) return EchoRatingLevel.Ace;      // 2 三权 + 3 低权 = 完美
        if (level3 == 2 && sum == 2) return EchoRatingLevel.SSS;
        if (level3 == 2 && sum == 1 || level3 == 1 && sum == 3) return EchoRatingLevel.SS;
        if (level3 == 2 || level3 == 1 && sum >= 2) return EchoRatingLevel.S;
        return EchoRatingLevel.N;
    }

    private static EchoRatingLevel ScorePropStatus(int level3, int level1, double subCount)
    {
        if (level3 == 2 && subCount > 3.5) return EchoRatingLevel.Ace;
        if (level3 == 2 && subCount > 2.8) return EchoRatingLevel.SSS;
        if (level3 == 2 && subCount > 2.1 || level3 == 1 && subCount > 2.4) return EchoRatingLevel.SS;
        if (level3 == 1 && subCount > 1.6 || level3 == 2 && subCount > 1.2) return EchoRatingLevel.S;
        return EchoRatingLevel.N;
    }

    private static EchoRatingLevel ScoreToStatus(int score)
    {
        if (score == 50) return EchoRatingLevel.Ace;
        if (score >= 35) return EchoRatingLevel.SSS;
        if (score >= 25) return EchoRatingLevel.SS;
        if (score >= 18) return EchoRatingLevel.S;
        return EchoRatingLevel.N;
    }

    private static int ScoreValue(EchoRatingLevel level) => level switch
    {
        EchoRatingLevel.Ace => 5,
        EchoRatingLevel.SSS => 4,
        EchoRatingLevel.SS => 3,
        EchoRatingLevel.S => 2,
        _ => 1,
    };

    private static string LevelText(EchoRatingLevel level) => LevelTextOf(level);

    /// <summary>评级 → 文本。</summary>
    public static string LevelTextOf(EchoRatingLevel level) => level switch
    {
        EchoRatingLevel.Ace => "ACE",
        EchoRatingLevel.SSS => "SSS",
        EchoRatingLevel.SS => "SS",
        EchoRatingLevel.S => "S",
        EchoRatingLevel.N => "N",
        _ => "-",
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
