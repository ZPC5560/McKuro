using McKuro.Core.Models.Roles;
using McKuro.Core.Services.Roles;

namespace McKuro.Tests;

/// <summary>
/// 声骸评分统一口径测试(主词条 + 副词条合成 0-100 分;见 <see cref="EchoRatingService"/>)。
/// <para>
/// 期望值由<b>独立复算脚本</b>按公式(而非照抄实现)得出,用于锁定:
/// ① 满分口径(完美声骸 = 100);② 5:2 槽位预算;③ 跨权重源可比性;
/// ④ 技能伤害角色级系数;⑤ 主词条/副词条分项;⑥ 档位阈值边界。
/// </para>
/// </summary>
public class EchoRatingUnifiedScoreTests
{
    /// <summary>4C 声骸:主词条暴击伤害 44%(满) + 攻击 150(满)。</summary>
    private static EchoInfo Echo4C(
        IEnumerable<(string Name, string Value)>? mains = null,
        params (string Name, string Value)[] subProps)
        => new()
        {
            Cost = 4,
            MainProps = (mains ?? [("暴击伤害", "44%"), ("攻击", "150")])
                .Select(p => new EchoProp { AttributeName = p.Name, AttributeValue = p.Value }).ToList(),
            SubProps = subProps.Select(p => new EchoProp { AttributeName = p.Name, AttributeValue = p.Value }).ToList(),
        };

    /// <summary>通用权重表(无 Wiki 权重、无官方 valid)下,5 条满值副词条的最优组合。</summary>
    private static (string Name, string Value)[] PerfectSubs =>
    [
        ("暴击", "10.5%"),        // tier 3 → 2.0
        ("暴击伤害", "21%"),      // tier 3 → 2.0
        ("攻击", "11.6%"),        // 攻击+% → 攻击百分比, tier 3 → 2.0
        ("生命", "11.6%"),        // 生命百分比, tier 2 → 1.0
        ("共鸣效率", "12.4%"),    // tier 2 → 1.0
    ];

    [Theory]
    [InlineData(4, "暴击伤害", "44%", "攻击", "150")]
    [InlineData(3, "冷凝伤害加成", "30%", "攻击", "100")]
    [InlineData(1, "攻击", "18%", "生命", "2280")]
    public void Perfect_Echo_Across_Costs_Scores_Full(
        int cost, string main1, string main1Value, string main2, string main2Value)
    {
        // 满分口径:主词条取该 COST 理想词条且满值 + 5 条最优副词条满值 → 恰好 100
        var echo = new EchoInfo
        {
            Cost = cost,
            MainProps =
            [
                new EchoProp { AttributeName = main1, AttributeValue = main1Value },
                new EchoProp { AttributeName = main2, AttributeValue = main2Value },
            ],
            SubProps = PerfectSubs.Select(p => new EchoProp { AttributeName = p.Name, AttributeValue = p.Value }).ToList(),
        };
        var rating = EchoRatingService.RateEcho(echo, null);
        Assert.Equal(100.0, rating.Score);
        Assert.Equal(100.0, rating.MainScore);
        Assert.Equal(100.0, rating.SubScore);
        Assert.Equal(EchoRatingLevel.Ace, rating.PhantomStatus);
    }

    [Fact]
    public void Perfect_Echo_Scores_Full_Under_A_Wiki_Weight_Table_Too()
    {
        // 跨权重源可比性:换成另一套权重(顶档词条更少)时,该角色视角下的完美声骸同样 = 100。
        // 若对分母写死常数,此处会得到明显低于 100 的分数 —— 那正是本测试要防的回归。
        var wiki = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["暴击"] = 2.0,
            ["暴击伤害"] = 2.0,
            ["攻击百分比"] = 1.0,
            ["攻击"] = 1.0,
            ["共鸣效率"] = 0.75,
        };
        var echo = new EchoInfo
        {
            Cost = 4,
            MainProps =
            [
                new EchoProp { AttributeName = "暴击伤害", AttributeValue = "44%" },
                new EchoProp { AttributeName = "攻击", AttributeValue = "150" },
            ],
            SubProps =
            [
                new EchoProp { AttributeName = "暴击", AttributeValue = "10.5%" },
                new EchoProp { AttributeName = "暴击伤害", AttributeValue = "21%" },
                new EchoProp { AttributeName = "攻击", AttributeValue = "11.6%" },
                new EchoProp { AttributeName = "攻击", AttributeValue = "60" },
                new EchoProp { AttributeName = "共鸣效率", AttributeValue = "12.4%" },
            ],
        };
        var rating = EchoRatingService.RateEcho(echo, wiki);
        Assert.Equal(100.0, rating.Score);
    }

    [Fact]
    public void Wiki_Table_And_Official_Valid_Coexist_Denominator_Matches_Numerator()
    {
        // 高危回归(2026-10 评审):分母(理想权重)必须与分子同源 ——
        // 修复前分母探针恒为 Valid=null:Wiki 表未收录但 valid=true 的词条分子 1.0 / 分母 0.3,
        // 5 条满 Roll 的半成品声骸被 Clamp 到 SubScore=100(虚高);valid=false 的词条
        // 分子 0 却仍占分母(压分)。修复后探针取 Valid=true 且无效词条从理想集剔除。
        var wiki = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["暴击"] = 2.0,   // Wiki 表只认可暴击
        };
        var echo = new EchoInfo
        {
            Cost = 4,
            MainProps =
            [
                new EchoProp { AttributeName = "暴击伤害", AttributeValue = "44%", Valid = true },
                new EchoProp { AttributeName = "攻击", AttributeValue = "150" },
            ],
            SubProps =
            [
                new EchoProp { AttributeName = "暴击", AttributeValue = "10.5%", Valid = true },   // 表内:2.0
                new EchoProp { AttributeName = "共鸣效率", AttributeValue = "12.4%", Valid = true }, // 表外有效:1.0
                new EchoProp { AttributeName = "防御百分比", AttributeValue = "14.7%", Valid = true }, // 表外有效:1.0
                new EchoProp { AttributeName = "生命", AttributeValue = "580", Valid = true },        // 表外有效:1.0
                new EchoProp { AttributeName = "普攻伤害加成", AttributeValue = "11.6%", Valid = true }, // 表外有效:1.0
            ],
        };
        var rating = EchoRatingService.RateEcho(echo, wiki);

        // 分子 = 2.0 + 4×1.0 = 6.0;分母(Valid=true 探针)= 2.0 + 4×1.0 = 6.0 → SubScore = 100
        Assert.Equal(100.0, rating.SubScore);

        // 对照组:同样 5 条满 Roll、但去掉全部 valid 判定(Valid=null)——
        // 修复前/后的分母同源分支:探针也是 null → 表外词条按 0.3 兜底档计。
        // 分子 = 2.0 + 4×0.3 = 3.2;分母 = 2.0 + 4×0.3 = 3.2 → SubScore 仍 = 100
        // (分母/分子同步收缩,分数不变 —— 这正是"分母随来源可达最优"的设计目标;
        //  绝对分值比带 valid 时低是权重来源本身的差异,不是缺陷。)
        var withoutValidity = new EchoInfo
        {
            Cost = 4,
            MainProps = echo.MainProps,
            SubProps =
            [
                new EchoProp { AttributeName = "暴击", AttributeValue = "10.5%" },
                new EchoProp { AttributeName = "共鸣效率", AttributeValue = "12.4%" },
                new EchoProp { AttributeName = "防御百分比", AttributeValue = "14.7%" },
                new EchoProp { AttributeName = "生命", AttributeValue = "580" },
                new EchoProp { AttributeName = "普攻伤害加成", AttributeValue = "11.6%" },
            ],
        };
        var rating2 = EchoRatingService.RateEcho(withoutValidity, wiki);
        Assert.Equal(100.0, rating2.SubScore);

        // 对照组 2:同一组合里一条词条被官方判无效 —— 分子记 0,理想集剔除它后
        // 由下一档补位(带 valid 数据的补位探针同样取有效档 1.0):
        // 分子 = 暴击(上限压制 1.5) + 3×1.0 = 4.5;
        // 分母 = 暴击 1.5 + 1.0×4(共鸣效率/生命/普攻/补位)= 5.5 → 81.8
        // (旧实现:无效词条仍占理想集 → 分母 5.5 不变但分子同 4.5,差异不在此例;
        //  本例锁的是"剔除 + 补位"路径本身。)
        var withInvalid = new EchoInfo
        {
            Cost = 4,
            MainProps = echo.MainProps,
            SubProps =
            [
                new EchoProp { AttributeName = "暴击", AttributeValue = "10.5%", Valid = true },
                new EchoProp { AttributeName = "共鸣效率", AttributeValue = "12.4%", Valid = true },
                new EchoProp { AttributeName = "防御百分比", AttributeValue = "14.7%", Valid = false },
                new EchoProp { AttributeName = "生命", AttributeValue = "580", Valid = true },
                new EchoProp { AttributeName = "普攻伤害加成", AttributeValue = "11.6%", Valid = true },
            ],
        };
        var rating3 = EchoRatingService.RateEcho(withInvalid, wiki);
        Assert.Equal((1.5 + 3.0) / (1.5 + 4.0) * 100.0, rating3.SubScore!.Value, 1);
    }

    [Fact]
    public void Unknown_Cost_Falls_Back_To_Legacy_Scale_Instead_Of_Free_Main_Score()
    {
        // 畸形/未来数据可能给出表外 COST(表只覆盖 1/3/4):修复前 maxTable 为 null 时
        // 所有主词条"按满值处理",主分项整段白送(约 28.6 分);修复后回退兼容口径(纯副词条)。
        var echo = new EchoInfo
        {
            Cost = 2,
            MainProps =
            [
                new EchoProp { AttributeName = "暴击伤害", AttributeValue = "44%" },
                new EchoProp { AttributeName = "攻击", AttributeValue = "150" },
            ],
            SubProps =
            [
                new EchoProp { AttributeName = "暴击", AttributeValue = "10.5%" },
                new EchoProp { AttributeName = "暴击伤害", AttributeValue = "21%" },
                new EchoProp { AttributeName = "攻击", AttributeValue = "60" },
            ],
        };
        var rating = EchoRatingService.RateEcho(echo, null);

        // 与无 mainProps 的同一副词条组合完全同分(兼容口径):1.5+1.5+1.0 = 4.0 → 40
        Assert.Null(rating.MainScore);
        Assert.Equal(40.0, rating.Score);
        Assert.Equal(40.0, rating.SubScore);
        Assert.False(rating.IsUnifiedScale);
    }

    [Fact]
    public void Slot_Budget_Is_Five_To_Two()
    {
        // 完美主词条 + 零副词条 → 只拿到主词条的 2/7 预算(28.6),副词条分项为 0
        var echo = Echo4C(subProps: []);
        var rating = EchoRatingService.RateEcho(echo, null);
        Assert.Equal(28.6, rating.Score);
        Assert.Equal(100.0, rating.MainScore);
        Assert.Equal(0.0, rating.SubScore);
    }

    [Fact]
    public void Non_Perfect_Mainstat_Lowers_Main_Score_Only()
    {
        // 主词条数值只有满值一半(暴击伤害 22% 而非 44%)→ 主词条分项降为 70,副词条分项不受影响
        var echo = Echo4C(
            [("暴击伤害", "22%"), ("攻击", "150")],
            PerfectSubs);
        var rating = EchoRatingService.RateEcho(echo, null);
        Assert.Equal(70.0, rating.MainScore);
        Assert.Equal(100.0, rating.SubScore);
        // 暴伤权重受上限压制为 1.5:理想 = 1.5 + 1.0 = 2.5,质量 = 0.5×1.5 + 1.0 = 1.75 → 70%
        // (2×0.70 + 5×1.0)/7×100 = 91.4
        Assert.Equal(91.4, rating.Score);
    }

    [Fact]
    public void Wrong_Mainstat_Selection_Is_Penalised()
    {
        // 4C 选生命% 但该角色推荐的是暴击伤害 → 生命% 不在推荐集内,该槽位权重记 0。
        // 主词条理想 = 推荐集最优(暴击伤害,受上限 1.5) + 固定攻击 1.0 = 2.5,实际只有固定攻击贡献 1.0 → 40.0
        var echo = Echo4C(
            [("生命", "33%"), ("攻击", "150")],
            PerfectSubs);
        echo.RecommendedMainStats = new HashSet<string>(StringComparer.Ordinal) { "暴击伤害" };
        var rating = EchoRatingService.RateEcho(echo, null);
        Assert.Equal(40.0, rating.MainScore);
        // (2×0.40 + 5×1.0)/7×100 = 82.9
        Assert.Equal(82.9, rating.Score);
    }

    [Fact]
    public void Missing_MainProps_Falls_Back_To_Legacy_Substat_Only_Scale()
    {
        // 兼容口径:旧缓存没有 mainProps → 纯副词条 0-100,且沿用历史固定分母(5×旧顶档 2.0 = 10)
        var echo = new EchoInfo
        {
            SubProps =
            [
                new EchoProp { AttributeName = "暴击", AttributeValue = "10.5%" },
                new EchoProp { AttributeName = "暴击伤害", AttributeValue = "21%" },
                new EchoProp { AttributeName = "攻击", AttributeValue = "60" },
            ],
        };
        var rating = EchoRatingService.RateEcho(echo, null);
        // 暴击 1.5 + 暴伤 1.5 + 攻击 1.0 = 4.0 → 4/10 = 40 分
        Assert.Equal(40.0, rating.Score);
        Assert.Null(rating.MainScore);
        Assert.Equal(40.0, rating.SubScore);
    }

    [Fact]
    public void Skill_Coefficient_Lowers_Only_That_Skill_Substat()
    {
        var echo = Echo4C(
            null,
            ("共鸣解放伤害加成", "11.6%"),
            ("共鸣技能伤害加成", "11.6%"));
        var withoutCoef = EchoRatingService.RateEcho(echo, null);
        var withCoef = EchoRatingService.RateEcho(echo, null,
            new Dictionary<string, double>(StringComparer.Ordinal) { ["共鸣解放伤害加成"] = 0.25 });

        // 系数只压低被指定的技能词条:分项下降,且主词条分项完全不受影响
        Assert.True(withCoef.Score < withoutCoef.Score);
        Assert.Equal(withoutCoef.MainScore, withCoef.MainScore);
        Assert.Equal(51.4, withoutCoef.Score);
        Assert.Equal(42.9, withCoef.Score);
    }

    [Fact]
    public void Skill_Coefficient_Does_Not_Affect_Non_Skill_Substats()
    {
        // 系数只作用于四类技能伤害词条:给暴击配系数不应改变任何结果
        var echo = Echo4C(null, ("暴击", "10.5%"), ("暴击伤害", "21%"));
        var baseline = EchoRatingService.RateEcho(echo, null);
        var withCoef = EchoRatingService.RateEcho(echo, null,
            new Dictionary<string, double>(StringComparer.Ordinal) { ["暴击"] = 0.01 });
        Assert.Equal(baseline.Score, withCoef.Score);
    }

    [Fact]
    public void Official_Invalid_Mainstat_Loses_Its_Own_Slot_Budget()
    {
        // 官方 valid=false 的主词条贡献 0,但它<b>仍占据该槽位的理想预算</b>(该槽位本可选出顶档词条)。
        // 因此等价于「对本角色选了无效主词条 → 扣掉该槽位」,而不是把分母也一并缩小来掩盖问题。
        // 注:理想预算由推荐集给出(推荐集不受 valid 影响,否则无效词条会把分母也缩小而"免费"拿满分)。
        var echo = new EchoInfo
        {
            Cost = 4,
            MainProps =
            [
                new EchoProp { AttributeName = "暴击伤害", AttributeValue = "44%", Valid = false },
                new EchoProp { AttributeName = "攻击", AttributeValue = "150" },
            ],
            SubProps = PerfectSubs.Select(p => new EchoProp { AttributeName = p.Name, AttributeValue = p.Value }).ToList(),
            RecommendedMainStats = new HashSet<string>(StringComparer.Ordinal) { "暴击伤害" },
        };
        var rating = EchoRatingService.RateEcho(echo, null);
        // 主词条:质量 1.0(仅固定攻击)÷ 理想 2.5(暴伤受上限 1.5 + 攻击 1.0) = 40.0
        Assert.Equal(40.0, rating.MainScore);
        Assert.Equal(100.0, rating.SubScore);
        // (2×0.40 + 5×1.0)/7×100 = 82.9
        Assert.Equal(82.9, rating.Score);
    }

    /// <summary>
    /// 档位阈值边界(统一口径 42.9/57.1/71.4)。数值由反解公式得出:
    /// score = (2×1 + 5×(v/10.5×1.5)/6.25)/7×100,令其恰好等于阈值
    /// (1.5 = 暴击上限,6.25 = 通用权重下 5 条最优副词条权重和)。
    /// </summary>
    [Theory]
    [InlineData("8.7763", 42.9, EchoRatingLevel.SS)]   // 恰好 42.9 → 小毕业
    [InlineData("8.7324", 42.8, EchoRatingLevel.S)]    // 略低 → 未毕业
    [InlineData("17.4737", 57.1, EchoRatingLevel.SSS)] // 恰好 57.1 → 毕业
    [InlineData("17.3864", 57.0, EchoRatingLevel.SS)]
    [InlineData("26.2325", 71.4, EchoRatingLevel.Ace)] // 恰好 71.4 → 完美毕业
    [InlineData("26.1013", 71.2, EchoRatingLevel.SSS)]
    public void Unified_Thresholds_Use_Rounded_Score(string critValue, double expectedScore, EchoRatingLevel expected)
    {
        var echo = Echo4C(null, ("暴击", critValue + "%"));
        var rating = EchoRatingService.RateEcho(echo, null);
        Assert.Equal(expectedScore, rating.Score);
        Assert.Equal(expected, rating.PhantomStatus);
    }

    [Fact]
    public void Role_Total_Sums_Unified_Per_Echo_Scores()
    {
        // 总评 = 5 件综合分之和(0-500);单件口径统一后,总评也必须同步统一
        var echoes = Enumerable.Range(0, 5)
            .Select(_ => Echo4C(null, PerfectSubs))
            .ToList();
        var rating = EchoRatingService.RateRole(echoes);
        Assert.Equal(100.0, rating.Echoes[0].Score);
        Assert.Equal(500.0, rating.TotalScore);
        Assert.Equal(500.0, rating.MaxScore);
        Assert.Equal(100, rating.AchievementPercent);
        Assert.Equal(EchoRatingLevel.Ace, rating.Level);
    }

    [Fact]
    public void RateRole_Passes_Skill_Coefficients_From_The_Model()
    {
        // 回归:RateRole 曾直接调用 RateEcho(e, e.PriorityWeights) 漏掉技能系数,
        // 会出现「单件分数之和 ≠ 总评」的不一致。
        var echoes = Enumerable.Range(0, 5).Select(_ =>
        {
            var e = Echo4C(null, ("共鸣解放伤害加成", "11.6%"));
            e.SkillCoefficients = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["共鸣解放伤害加成"] = 0.25,
            };
            return e;
        }).ToList();

        var rating = EchoRatingService.RateRole(echoes);
        var expectedPerEcho = EchoRatingService.RateEcho(echoes[0], null, echoes[0].SkillCoefficients).Score;
        Assert.Equal(expectedPerEcho, rating.Echoes[0].Score);
        Assert.Equal(expectedPerEcho * 5, rating.TotalScore);
    }

    /// <summary>
    /// 回归(用户反馈「生命/防御主词条严重低分,卡提希娅与莫宁为主」):
    /// 主词条“理想值”曾取该 COST 词条池的<b>全局最大权重</b>(4C 恒为暴击 2.0),
    /// 于是生命/防御/治疗型角色即便主词条完全正确,主词条分项也只有 0-33,
    /// 而暴击型角色可得满分 —— 同一份质量的声骸因流派不同被判不同分。
    /// <para>修复后:主词条理想值取<b>该角色推荐主词条</b>,各流派选对自己主词条时同为满分。</para>
    /// <para>注:推荐集用评分内部口径(「生命百分比」而非「生命%」),见 NormalizeMainStatName。</para>
    /// </summary>
    [Theory]
    [InlineData("暴击伤害", "44%", "暴击伤害")]
    [InlineData("生命", "33%", "生命百分比")]        // 卡提希娅等生命向(4C 生命%)
    [InlineData("治疗效果加成", "26%", "治疗效果加成")] // 莫宁/守岸人等治疗向
    [InlineData("防御", "41.5%", "防御百分比")]        // 防御向
    public void Mainstat_Choice_Is_Judged_Against_The_Character_Recommendation(
        string rawStat, string value, string normalizedStat)
    {
        var echo = Echo4C(
            [(rawStat, value), ("攻击", "150")],
            PerfectSubs);
        // 该声骸的推荐主词条就是它实际佩戴的那条 → 视为“选对”
        echo.RecommendedMainStats = new HashSet<string>(StringComparer.Ordinal) { normalizedStat };

        var rating = EchoRatingService.RateEcho(echo, null);
        // 各流派只要主词条选对且满值,主词条分项都应是满分,综合分一致(不再因流派而异)
        Assert.Equal(100.0, rating.MainScore);
        Assert.Equal(100.0, rating.Score);
    }

    [Fact]
    public void Wrong_Mainstat_Against_Recommendation_Is_Heavily_Penalised()
    {
        // 治疗角色的 4C 主词条选了暴击伤害(不在推荐集内)→ 该槽位权重记 0,
        // 主词条分项只剩固定攻击槽位贡献,综合分明显低于选对时(100.0)
        var echo = Echo4C(
            [("暴击伤害", "44%"), ("攻击", "150")],
            PerfectSubs);
        echo.RecommendedMainStats = new HashSet<string>(StringComparer.Ordinal) { "治疗效果加成" };

        var rating = EchoRatingService.RateEcho(echo, null);
        Assert.Equal(50.0, rating.MainScore);   // 1.0 / (2.0 + 1.0);推荐集最优点=治疗加成权重 1.0 → 理想 2.0
        Assert.Equal(85.7, rating.Score);
    }

    [Fact]
    public void Without_Recommendation_Mainstat_Is_Scored_On_Value_Only()
    {
        // 无推荐数据时不得替玩家假设“最优主词条”:分母取自身权重,
        // 即只评数值是否满,不评词条选择 —— 否则回到“所有角色都该选暴击”的老 bug。
        foreach (var (stat, value) in new[] { ("暴击伤害", "44%"), ("生命", "33%"), ("治疗效果加成", "26%") })
        {
            var echo = Echo4C([(stat, value), ("攻击", "150")], PerfectSubs);
            var rating = EchoRatingService.RateEcho(echo, null);
            Assert.Equal(100.0, rating.MainScore);
            Assert.Equal(100.0, rating.Score);
        }
    }

    [Fact]
    public void Half_Value_Mainstat_On_Recommended_Stat_Loses_Half_Its_Budget()
    {
        // 推荐 = 治疗效果加成(权重 1.0,理想 2.0);数值只到一半 → 主词条分项 75.0
        var echo = Echo4C(
            [("治疗效果加成", "13%"), ("攻击", "150")],
            PerfectSubs);
        echo.RecommendedMainStats = new HashSet<string>(StringComparer.Ordinal) { "治疗效果加成" };

        var rating = EchoRatingService.RateEcho(echo, null);
        Assert.Equal(75.0, rating.MainScore);
        Assert.Equal(92.9, rating.Score);
    }

    [Theory]
    [InlineData("攻击%", "攻击百分比")]
    [InlineData("攻击％", "攻击百分比")]     // 全角 ％
    [InlineData("生命%", "生命百分比")]
    [InlineData("防御%", "防御百分比")]
    [InlineData("暴击率", "暴击")]           // 数据源常见别名(未覆盖时该主词条会被静默记 0)
    [InlineData("治疗加成", "治疗效果加成")]
    [InlineData("暴击伤害", "暴击伤害")]
    [InlineData("治疗效果加成", "治疗效果加成")]
    public void Recommended_Main_Stat_Names_Are_Normalized(string raw, string expected)
    {
        // 攻略站 echoAttributes 给的是 UI 文案名(「攻击%」),评分内部用「攻击百分比」;
        // 不做这层映射,推荐集将全部匹配不上,主词条判定静默失效。
        Assert.Equal(expected, EchoRatingService.NormalizeMainStatName(raw));
    }

    /// <summary>
    /// 端到端:官方攻略站 echoAttributes → 推荐主词条 → 评分。
    /// <para>
    /// 用实测抓包结构(守岸人/莫宁这类治疗向:4C 治疗效果加成、3C 攻击%、1C 攻击%)验证
    /// 「生命/防御/治疗型角色的正确主词条不再被判低分」这条用户反馈被真正修好。
    /// </para>
    /// </summary>
    [Fact]
    public void Guide_EchoAttributes_Feed_Recommended_Main_Stats_Into_Scoring()
    {
        const string json =
            """
            {"id":1,"echo":{"main":{"echoAttributes":[
              {"cost":4,"attribute":{"texts":[{"language":"zh-Hans","name":"治疗效果加成"}]}},
              {"cost":3,"attribute":{"texts":[{"language":"zh-Hans","name":"攻击%"}]}},
              {"cost":1,"attribute":{"texts":[{"language":"zh-Hans","name":"攻击%"}]}}]}}}
            """;
        var info = System.Text.Json.JsonSerializer.Deserialize(
            json, McKuro.Core.Models.Guide.GuideJsonContext.Default.GuideIntroductionInfo)!;

        var recMains = McKuro.Core.Services.Guide.GuideAchievementService.ParseRecommendedMainStats(info);

        // 名字已规范化(「攻击%」→「攻击百分比」),否则评分侧匹配不上
        Assert.Contains("治疗效果加成", recMains[4]);
        Assert.Contains("攻击百分比", recMains[3]);
        Assert.Contains("攻击百分比", recMains[1]);

        // 治疗角色戴对 4C 主词条(治疗效果加成满值)→ 不该被系统性判低分
        var echo = Echo4C(
            [("治疗效果加成", "26%"), ("攻击", "150")],
            PerfectSubs);
        echo.RecommendedMainStats = recMains.TryGetValue(4, out var m4) ? m4 : null;
        var rating = EchoRatingService.RateEcho(echo, null);
        Assert.Equal(100.0, rating.MainScore);
        Assert.Equal(100.0, rating.Score);
    }
}
