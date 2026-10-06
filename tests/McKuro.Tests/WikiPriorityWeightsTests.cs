using McKuro.Core.Services.Roles;

namespace McKuro.Tests;

/// <summary>
/// 「声骸词条」优先级文本解析测试(<see cref="WikiGuideService.ParsePriorityWeights"/>)。
/// <para>
/// 用例取自<b>真实数据</b>:Wiki(wiki.kurobbs.com)与 mcguide 攻略正文的
/// <c>echoTexts[].recommendDescription</c>。这两处文本写法不同(Wiki 用 <c>=</c> 并列,
/// mcguide 常用 <c>/</c>),早期解析器只认 <c>=</c> 且遇括号即截断,会静默丢词条。
/// </para>
/// </summary>
public class WikiPriorityWeightsTests
{
    [Fact]
    public void Parses_Wiki_Equals_Separated_Text()
    {
        // Wiki 典型写法(该项目最初支持的格式)
        var w = WikiGuideService.ParsePriorityWeights("暴击=暴击伤害＞攻击&gt;共鸣技能&gt; 共鸣效率（推荐效率120%以上）");
        Assert.NotNull(w);
        Assert.Equal(2.0, w!["暴击"]);
        Assert.Equal(2.0, w["暴击伤害"]);
        Assert.Equal(1.0, w["攻击百分比"]);
        Assert.Equal(0.75, w["共鸣技能伤害加成"]);
        Assert.Equal(0.5, w["共鸣效率"]);
    }

    [Fact]
    public void Parses_Slash_Separated_Text_From_Mcguide()
    {
        // 回归:mcguide 用 `/` 作同级分隔(「暴击/暴击伤害」)。早期只按 `=` 切分,
        // 整串被当成一个 token → 只映射出「暴击伤害」,`暴击` 静默丢失。
        var w = WikiGuideService.ParsePriorityWeights("暴击/暴击伤害&gt;攻击%&gt;普攻伤害加成&gt;共鸣效率");
        Assert.NotNull(w);
        Assert.Equal(2.0, w!["暴击"]);
        Assert.Equal(2.0, w["暴击伤害"]);
        Assert.Equal(1.0, w["攻击百分比"]);
        Assert.Equal(0.75, w["普攻伤害加成"]);
        Assert.Equal(0.5, w["共鸣效率"]);
    }

    [Fact]
    public void All_Slash_Separated_Group_Is_Fully_Parsed()
    {
        // 实测千咲:「暴击/暴击伤害/攻击%/共鸣解放/共鸣效率」——同级五条。
        // 这是该 bug 最严重的形态:修复前只解析出 1 条,丢 4 条。
        // 注:`攻击%` 会同时映射出「攻击百分比」与「攻击」两个词条(既有设计:一个别名覆盖两形态),
        // 故总条目数比词条写法数多 1。
        var w = WikiGuideService.ParsePriorityWeights("暴击/暴击伤害/攻击%/共鸣解放/共鸣效率");
        Assert.NotNull(w);
        Assert.Equal(6, w!.Count);
        foreach (var stat in new[] { "暴击", "暴击伤害", "攻击百分比", "攻击", "共鸣解放伤害加成", "共鸣效率" })
        {
            Assert.True(w.ContainsKey(stat), $"应包含 {stat}");
            Assert.Equal(2.0, w[stat]);
        }
    }

    [Fact]
    public void Mid_String_Parenthetical_Does_Not_Truncate_Following_Entries()
    {
        // 回归:mcguide 把补充说明写在句中(「共鸣效率（230%）&gt;暴击伤害&gt;…」)。
        // 早期实现遇第一个 `（` 就截断其后全部内容 → 只剩「共鸣效率」,暴击伤害/解放/生命全丢。
        var w = WikiGuideService.ParsePriorityWeights(
            "共鸣效率（230%）&gt;暴击伤害＞共鸣解放伤害加成&gt;生命%&gt;生命");
        Assert.NotNull(w);
        Assert.Equal(2.0, w!["共鸣效率"]);
        Assert.Equal(1.0, w["暴击伤害"]);
        Assert.Equal(0.75, w["共鸣解放伤害加成"]);
        // 「生命%」同时映射 生命百分比 与 生命(同为 0.5);末组「生命」已存在故不覆盖
        Assert.Equal(0.5, w["生命百分比"]);
        Assert.Equal(0.5, w["生命"]);
    }

    [Fact]
    public void Numeric_Preamble_Does_Not_Stop_Parsing()
    {
        // 回归:mcguide 常见「共鸣效率120%；暴击/暴击伤害&gt;…」——首段含数值说明。
        // 早期在「映射不出词条的组」直接 break,会丢掉其后全部词条。
        var w = WikiGuideService.ParsePriorityWeights(
            "共鸣效率120%；暴击/暴击伤害&gt;生命百分比＞ 固定生命＞普攻伤害=共鸣解放伤害");
        Assert.NotNull(w);
        Assert.Equal(2.0, w!["共鸣效率"]);
        Assert.Equal(1.0, w["暴击"]);
        Assert.Equal(1.0, w["暴击伤害"]);
        Assert.Equal(0.75, w["生命百分比"]);
        Assert.Equal(0.75, w["生命"]);
        Assert.Equal(0.4, w["普攻伤害加成"]);
        Assert.Equal(0.4, w["共鸣解放伤害加成"]);
    }

    [Fact]
    public void Maps_Chinese_Comma_As_Tier_Separator()
    {
        var w = WikiGuideService.ParsePriorityWeights("暴击、暴击伤害&gt;攻击%");
        Assert.NotNull(w);
        Assert.Equal(2.0, w!["暴击"]);
        Assert.Equal(2.0, w["暴击伤害"]);
        Assert.Equal(1.0, w["攻击百分比"]);
    }

    [Fact]
    public void Defensive_Stats_Are_Mapped_For_Non_Dps_Characters()
    {
        // 莫宁/卡提希娅等非输出角色:优先级串以共鸣效率/防御/生命开头,必须能解析出这些词条
        var w = WikiGuideService.ParsePriorityWeights("共鸣效率&gt;共鸣解放&gt;防御%&gt;暴击/暴击伤害");
        Assert.NotNull(w);
        Assert.Equal(2.0, w!["共鸣效率"]);
        Assert.Equal(1.0, w["共鸣解放伤害加成"]);
        Assert.Equal(0.75, w["防御百分比"]);
        Assert.Equal(0.5, w["暴击"]);
        Assert.Equal(0.5, w["暴击伤害"]);
    }

    [Fact]
    public void Returns_Null_For_Empty_Or_Unmappable_Input()
    {
        Assert.Null(WikiGuideService.ParsePriorityWeights(null));
        Assert.Null(WikiGuideService.ParsePriorityWeights("   "));
        Assert.Null(WikiGuideService.ParsePriorityWeights("以下为无关文本,没有任何词条名"));
    }
}
