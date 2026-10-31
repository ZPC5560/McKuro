using McKuro.Core.Services.Roles;

namespace McKuro.Tests;

/// <summary>
/// Wiki 攻略抓取的结构化解析测试(不走网络):
/// <see cref="WikiGuideService.ExtractAffixPrioritySection"/>(getEntryDetail 响应 → 「声骸词条」那一格)、
/// <see cref="WikiGuideService.ParseCataloguePage"/>(目录分页)与 <see cref="WikiGuideService.FindRoleEntryId"/>。
/// <para>
/// 载荷按真实 getEntryDetail 的形态建模:<c>data.content.modules[].components[].content</c> 为富文本 HTML,
/// 攻略表格的「声骸词条」一行是 <c>&lt;td&gt;声骸词条&lt;/td&gt;&lt;td&gt;值&lt;/td&gt;</c>,
/// 优先级符号写成 <c>&amp;gt;</c> 实体或全角「＞」,并列用全角「＝」。
/// </para>
/// </summary>
public class WikiGuideExtractionTests
{
    /// <summary>典型攻略表格:「声骸词条」行后紧跟其他行(旧实现取标签后 1200 字符,会把后续行一并吞进来)。</summary>
    private const string TableEntryDetail =
        """
        {"code":200,"msg":"请求成功","data":{"id":1234,"name":"椿","content":{"modules":[{"title":"养成推荐","components":[{"type":"rich-text","content":"<table><tbody><tr><td><p><strong>声骸词条</strong></p></td><td><p>暴击＝暴击伤害&gt;攻击&gt;共鸣技能伤害加成&gt;共鸣效率（推荐效率120%以上）</p></td></tr><tr><td><p>备注</p></td><td><p>生命&gt;防御</p></td></tr></tbody></table>"}]}]}}}
        """;

    [Fact]
    public void Extracts_Only_The_Cell_After_The_Label_From_A_Table()
    {
        var section = WikiGuideService.ExtractAffixPrioritySection(TableEntryDetail);
        Assert.NotNull(section);
        Assert.DoesNotContain("声骸词条", section);
        Assert.DoesNotContain("备注", section);

        var w = WikiGuideService.ParsePriorityWeights(section);
        Assert.NotNull(w);
        // 全角「＝」并列(M14)
        Assert.Equal(2.0, w!["暴击"]);
        Assert.Equal(2.0, w["暴击伤害"]);
        Assert.Equal(1.0, w["攻击百分比"]);
        Assert.Equal(0.75, w["共鸣技能伤害加成"]);
        Assert.Equal(0.5, w["共鸣效率"]);
        // 下一行(备注)的生命/防御不得混入(M15:旧实现的 1200 字符窗口会吞进后续行)
        Assert.False(w.ContainsKey("生命"));
        Assert.False(w.ContainsKey("防御"));
    }

    [Fact]
    public void Heading_And_Value_In_Same_Paragraph_Keeps_Leading_Crit()
    {
        // 标题前缀形态(M15):「声骸词条：暴击＞…」在同一段。旧实现首个 token 变成「声骸词条暴击」,
        // 精确匹配失败,排第一的暴击被丢掉
        const string json =
            """
            {"code":200,"data":{"content":{"modules":[{"components":[{"content":"<p>声骸主词条：4C 暴击</p><p>声骸词条：暴击＞暴击伤害＞攻击</p><p>生命&gt;防御</p>"}]}]}}}
            """;
        var section = WikiGuideService.ExtractAffixPrioritySection(json);
        var w = WikiGuideService.ParsePriorityWeights(section);
        Assert.NotNull(w);
        Assert.Equal(2.0, w!["暴击"]);
        Assert.Equal(1.0, w["暴击伤害"]);
        Assert.Equal(0.75, w["攻击百分比"]);
        Assert.False(w.ContainsKey("生命"));
    }

    [Fact]
    public void Skips_Mentions_Without_Parsable_Value_And_Uses_The_Real_Cell()
    {
        // 正文先提到「声骸词条」(见下表)但没有值,真正的值在后面的表格里
        const string json =
            """
            {"code":200,"data":{"content":{"modules":[{"components":[{"content":"<p>声骸词条见下表</p><table><tr><td>声骸词条</td><td>共鸣效率&gt;共鸣解放&gt;防御%</td></tr></table>"}]}]}}}
            """;
        var w = WikiGuideService.ParsePriorityWeights(WikiGuideService.ExtractAffixPrioritySection(json));
        Assert.NotNull(w);
        Assert.Equal(2.0, w!["共鸣效率"]);
        Assert.Equal(1.0, w["共鸣解放伤害加成"]);
        Assert.Equal(0.75, w["防御百分比"]);
    }

    [Fact]
    public void Content_Nested_As_Json_String_Is_Parsed()
    {
        // 富文本模块整体以 JSON 字符串下发(再转义一层)
        const string json =
            """
            {"code":200,"data":{"content":"{\"modules\":[{\"components\":[{\"content\":\"<td>声骸词条</td><td>暴击=暴击伤害&gt;攻击</td>\"}]}]}"}}
            """;
        var w = WikiGuideService.ParsePriorityWeights(WikiGuideService.ExtractAffixPrioritySection(json));
        Assert.NotNull(w);
        Assert.Equal(2.0, w!["暴击"]);
        Assert.Equal(2.0, w["暴击伤害"]);
        Assert.Equal(1.0, w["攻击百分比"]);
    }

    [Fact]
    public void String_Array_Table_Row_Takes_The_Item_After_The_Label()
    {
        const string json =
            """
            {"code":200,"data":{"rows":[["主词条","4C 暴击"],["声骸词条","暴击＝暴击伤害＞攻击"],["备注","生命"]]}}
            """;
        var section = WikiGuideService.ExtractAffixPrioritySection(json);
        Assert.Equal("暴击＝暴击伤害＞攻击", section);
    }

    [Fact]
    public void Returns_Null_When_Document_Has_No_Affix_Section()
    {
        const string json =
            """
            {"code":200,"data":{"content":{"modules":[{"components":[{"content":"<p>联名角色,暂无攻略</p>"}]}]}}}
            """;
        Assert.Null(WikiGuideService.ExtractAffixPrioritySection(json));
    }

    // ---------- 目录分页(M18) ----------

    [Fact]
    public void ParseCataloguePage_Reads_Records_Pages_And_Linked_Entry_Ids()
    {
        const string json =
            """
            {"code":200,"data":{"results":{"pages":3,"total":130,"records":[
              {"name":"椿","entryId":"1001"},
              {"name":"漂泊者-衍射","entryId":1002},
              {"name":"漂泊者-湮灭","content":"{\"linkConfig\":{\"entryId\":\"1003\"}}"},
              {"name":"","entryId":"1004"},
              {"name":"无条目"}
            ]}}}
            """;
        var (page, pages) = WikiGuideService.ParseCataloguePage(json);
        Assert.NotNull(page);
        Assert.Equal(3, pages);
        Assert.Equal(5, page!.Count); // 原始记录数(用于"本页不满一页即末页"判断)
        Assert.Equal(
            new[] { "椿:1001", "漂泊者-衍射:1002", "漂泊者-湮灭:1003" },
            page.Entries.Select(e => $"{e.Name}:{e.EntryId}").ToArray());
    }

    [Fact]
    public void ParseCataloguePage_Error_Envelope_Is_Structural_Failure()
    {
        // 结构不符(被拦截/接口变更)返回 null:调用方当作临时失败,不能缓存成"该角色无攻略"
        var (page, pages) = WikiGuideService.ParseCataloguePage("""{"code":500,"msg":"系统繁忙"}""");
        Assert.Null(page);
        Assert.Equal(0, pages);
    }

    [Fact]
    public void ParseCataloguePage_Non_Json_Throws()
    {
        // WAF/网关返回 HTML:抛出,由 FetchAndCacheAsync 记为临时失败(冷却后重试)
        Assert.ThrowsAny<System.Text.Json.JsonException>(
            () => WikiGuideService.ParseCataloguePage("<html><body>403</body></html>"));
    }

    [Fact]
    public void FindRoleEntryId_Distinguishes_Rover_Forms_By_Attribute()
    {
        // 漂泊者各属性同名:按属性名挑对应攻略卡,不能共用第一张(M18)
        var catalogue = new List<WikiGuideService.CatalogueEntry>
        {
            new("漂泊者-衍射", "1002"),
            new("漂泊者-湮灭", "1003"),
            new("椿", "1001"),
            new("椿·联动", "1009"),
        };
        Assert.Equal("1003", WikiGuideService.FindRoleEntryId(catalogue, "漂泊者", "湮灭"));
        Assert.Equal("1002", WikiGuideService.FindRoleEntryId(catalogue, "漂泊者", "衍射"));
        // 无属性信息:回退到第一张同前缀卡片(旧行为)
        Assert.Equal("1002", WikiGuideService.FindRoleEntryId(catalogue, "漂泊者", null));
        // 精确同名优先于带后缀的卡片
        Assert.Equal("1001", WikiGuideService.FindRoleEntryId(catalogue, "椿", null));
        Assert.Null(WikiGuideService.FindRoleEntryId(catalogue, "今汐", null));
    }
}
