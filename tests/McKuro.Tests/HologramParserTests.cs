using System.Text.Json;
using McKuro.Core.Models.Tower;
using Xunit;

namespace McKuro.Tests;

/// <summary>
/// 全息战略解析纯函数测试(HologramParser / TowerViewModel 的映射逻辑)。
/// <para>
/// 数据取自 2026-10 实测响应(23 boss / 138 条难度记录)。本组的重点是钉住四个
/// <b>反直觉点</b> —— 它们都是照着官方 H5 压缩代码建模会写错的地方:
/// ① <c>challengeInfo</c> 是字典(key=字符串 bossId)不是数组;
/// ② 索引与记录里同名 <c>difficulty</c> 语义相反(已通关最高档 vs 难度档位);
/// ③ <c>roles</c> 未通关时整个键缺失(不是空数组);
/// ④ <c>passTime</c> 单位是秒,0 表示未通关。
/// </para>
/// </summary>
public class HologramParserTests
{
    private static HologramIndexData ParseIndex(string json)
        => JsonSerializer.Deserialize(json, HologramJsonContext.Default.HologramIndexData)!;

    private static HologramDetailData ParseDetails(string json)
        => JsonSerializer.Deserialize(json, HologramJsonContext.Default.HologramDetailData)!;

    /// <summary>实测 challengeIndex 响应片段(两个 boss:一个未挑战、一个已通关难度 5)。</summary>
    /// <remarks>
    /// 地区与 boss <b>故意逆序</b>下发(强袭 sort 90 在前、演武 130 在后;boss 也按 sort 升序):
    /// 夹具若本就按期望顺序排列,删掉 <see cref="HologramParser.SortCountries"/>/
    /// <see cref="HologramParser.SortBosses"/> 里的排序断言照样通过。
    /// </remarks>
    private const string IndexJson = """
        {
          "isUnlock": true,
          "open": true,
          "wikiUrl": "https://wiki.kurobbs.com/mc/home",
          "challengeList": [
            {
              "sort": 90,
              "country": { "countryId": 100003, "countryName": "强袭",
                "homePageImage": "https://img/region-strong.png" },
              "indexList": [
                { "bossId": 15010, "bossName": "燎照之骑", "bossLevel": 60, "difficulty": 6,
                  "bossHeadIcon": "https://img/head-15010.png", "bossIconUrl": "https://img/pic-15010.png",
                  "contryId": 100003, "sort": 93 }
              ]
            },
            {
              "sort": 130,
              "country": {
                "countryId": 100004,
                "countryName": "演武",
                "homePageImage": "https://web-static.kurobbs.com/adminConfig/74/home_page_icon_two/1783652611738.png",
                "homePageIcon": "https://web-static.kurobbs.com/adminConfig/74/home_page_icon/",
                "bgColor": ""
              },
              "indexList": [
                { "bossId": 31010, "bossName": "达妮娅", "bossLevel": 60, "difficulty": 5,
                  "bossHeadIcon": "https://img/head-31010.png", "bossIconUrl": "https://img/pic-31010.png",
                  "contryId": 100004, "sort": 109 },
                { "bossId": 32010, "bossName": "万囮牢·朽躯", "bossLevel": 60, "difficulty": 0,
                  "bossHeadIcon": "https://img/head-32010.png", "bossIconUrl": "https://img/pic-32010.png",
                  "contryId": 100004, "sort": 110 }
              ]
            }
          ]
        }
        """;

    /// <summary>实测 challengeDetails 响应片段(32010 六档;31010 只给前两档与第六档)。</summary>
    /// <remarks>
    /// 记录<b>故意乱序</b>(6 → 1 → 5)且在难度 5 与 6 之间夹一条难度 1:
    /// 夹具本身有序的话,<see cref="HologramParser.TiersOf"/> 里的 OrderBy 删掉测试照样过,
    /// 排序断言就成了假绿。
    /// </remarks>
    private const string DetailsJson = """
        {
          "isUnlock": true,
          "open": true,
          "challengeInfo": {
            "32010": [
              { "bossHeadIcon": "h1", "bossIconUrl": "p1", "bossLevel": 60, "bossName": "万囮牢·朽躯",
                "challengeId": 3201, "difficulty": 1, "passTime": 0 },
              { "bossHeadIcon": "h6", "bossIconUrl": "p6", "bossLevel": 100, "bossName": "万囮牢·朽躯",
                "challengeId": 3206, "difficulty": 6, "passTime": 0 }
            ],
            "31010": [
              { "bossHeadIcon": "h6", "bossIconUrl": "p6", "bossLevel": 100, "bossName": "达妮娅",
                "challengeId": 3106, "difficulty": 6, "passTime": 0 },
              { "bossHeadIcon": "h1", "bossIconUrl": "p1", "bossLevel": 60, "bossName": "达妮娅",
                "challengeId": 3101, "difficulty": 1, "passTime": 10,
                "roles": [
                  { "natureId": 1, "roleName": "绯雪", "roleLevel": 90, "roleHeadIcon": "r1" },
                  { "natureId": 6, "roleName": "千咲", "roleLevel": 90, "roleHeadIcon": "r2" },
                  { "natureId": 2, "roleName": "嘉贝莉娜", "roleLevel": 90, "roleHeadIcon": "r3" }
                ] },
              { "bossHeadIcon": "h5", "bossIconUrl": "p5", "bossLevel": 90, "bossName": "达妮娅",
                "challengeId": 3105, "difficulty": 5, "passTime": 37,
                "roles": [ { "natureId": 1, "roleName": "绯雪", "roleLevel": 90, "roleHeadIcon": "r1" } ] }
            ],
            "15010": [
              { "bossHeadIcon": "h6", "bossIconUrl": "p6", "bossLevel": 100, "bossName": "燎照之骑",
                "challengeId": 1506, "difficulty": 6, "passTime": 205,
                "roles": [ { "natureId": 4, "roleName": "卡提希娅", "roleLevel": 90, "roleHeadIcon": "r9" } ] }
            ]
          }
        }
        """;

    // ---------- ① challengeInfo 是字典,key 是字符串 bossId ----------

    [Fact]
    public void TiersOf_Reads_By_String_BossId_Key()
    {
        var details = ParseDetails(DetailsJson);
        var tiers = HologramParser.TiersOf(details, 31010);

        // 字典键是字符串形式;用 int 拼 key 才能取到(写成数组下标就取不到了)
        Assert.Equal(3, tiers.Count);
        Assert.Contains(tiers, r => r.Difficulty == 5 && r.PassTime == 37);
    }

    [Fact]
    public void TiersOf_Returns_Sorted_By_Difficulty()
    {
        // 服务端给的是数组且当前下标=difficulty-1,但排序后对顺序调整免疫
        var details = ParseDetails(DetailsJson);
        var tiers = HologramParser.TiersOf(details, 31010);
        Assert.Equal([1, 5, 6], tiers.Select(t => t.Difficulty));
    }

    [Fact]
    public void TiersOf_Unknown_Boss_Returns_Empty()
    {
        var details = ParseDetails(DetailsJson);
        Assert.Empty(HologramParser.TiersOf(details, 99999));
        Assert.Empty(HologramParser.TiersOf(null, 31010));
    }

    [Fact]
    public void TiersOf_Handles_Null_Values_Null_Elements_And_Missing_ChallengeInfo()
    {
        // 三条"整页空白"防线(2026-10 评审补充),各自都必须返回空/跳过而不是抛 NRE:
        // ① 字典条目的<b>值</b>为 JSON null("31010": null);
        // ② 记录<b>数组元素</b>为 JSON null(服务端空洞)—— List<T> 反序列化出的就是 null 引用,
        //    直接 OrderBy 会在键选择器上 NRE,且异常会被 TowerViewModel.LoadAsync 的
        //    页面级 catch 吞掉,把"一个坏元素"放大成"整页签加载失败";
        // ③ details 非 null 但 challengeInfo 键整体缺失(如 data="{}")。
        const string json = """
            {
              "challengeInfo": {
                "31010": null,
                "32010": [ { "difficulty": 4, "passTime": 9 }, null, { "difficulty": 2, "passTime": 5 } ]
              }
            }
            """;
        var details = ParseDetails(json);

        Assert.Empty(HologramParser.TiersOf(details, 31010));

        // 元素 null 被跳过,其余记录仍按难度升序返回(夹具故意乱序:4 在 2 前)
        var tiers = HologramParser.TiersOf(details, 32010);
        Assert.Equal([2, 4], tiers.Select(t => t.Difficulty));

        var noChallengeInfo = ParseDetails("{}");
        Assert.Null(noChallengeInfo.ChallengeInfo);
        Assert.Empty(HologramParser.TiersOf(noChallengeInfo, 31010));
    }

    // ---------- ② 同名 difficulty 语义相反 ----------

    [Fact]
    public void Index_Difficulty_Means_Cleared_Max_Tier()
    {
        // 索引里的 difficulty 是"已通关最高难度",0 = 未挑战(不是"难度 0")
        var index = ParseIndex(IndexJson);
        var yanwu = index.ChallengeList!.Single(c => c.Country!.CountryId == 100004);
        var bosses = yanwu.IndexList!;

        Assert.Equal(0, bosses.Single(b => b.BossId == 32010).ClearedDifficulty);
        Assert.Equal(5, bosses.Single(b => b.BossId == 31010).ClearedDifficulty);
    }

    [Fact]
    public void Record_Difficulty_Means_Tier_Number()
    {
        // 记录里的 difficulty 是难度档位,取值 1..6(不会是 0)
        var details = ParseDetails(DetailsJson);
        var all = details.ChallengeInfo!.Values.SelectMany(v => v).ToList();
        Assert.NotEmpty(all);
        Assert.All(all, r => Assert.InRange(r.Difficulty, 1, 6));
    }

    // ---------- ③ roles 未通关时键缺失 ----------

    [Fact]
    public void Cleared_Record_Has_Roles_Uncleared_Has_Missing_Key()
    {
        var details = ParseDetails(DetailsJson);
        var tiers = HologramParser.TiersOf(details, 31010);

        var cleared = tiers.Single(t => t.Difficulty == 5);
        Assert.True(HologramParser.IsCleared(cleared));
        Assert.NotNull(cleared.Roles);
        Assert.NotEmpty(cleared.Roles!);

        var uncleared = tiers.Single(t => t.Difficulty == 6);
        Assert.False(HologramParser.IsCleared(uncleared));
        Assert.Null(uncleared.Roles); // 键整体缺失 → null,而不是空数组
    }

    // ---------- ④ passTime 单位是秒,0 = 未通关 ----------

    [Fact]
    public void BossLevel_Varies_Per_Difficulty()
    {
        // bossLevel 随难度递增,且不是全服固定阶梯(强袭老 boss 是 45..90,常规是 60..100);
        // 建模上必须按"每档一条记录"取值,不能只取索引里难度 1 的等级
        var details = ParseDetails(DetailsJson);
        var tiers = HologramParser.TiersOf(details, 31010);
        Assert.Equal(60, tiers.Single(t => t.Difficulty == 1).BossLevel);
        Assert.Equal(90, tiers.Single(t => t.Difficulty == 5).BossLevel);
        Assert.Equal(100, tiers.Single(t => t.Difficulty == 6).BossLevel);
    }

    [Theory]
    [InlineData(37, "00:37")]
    [InlineData(10, "00:10")]
    [InlineData(205, "03:25")]
    [InlineData(5, "00:05")]
    [InlineData(59, "00:59")]
    [InlineData(60, "01:00")]
    [InlineData(3600, "1:00:00")]
    [InlineData(3661, "1:01:01")]
    public void FormatPassTime_Pads_To_Two_Digits(int seconds, string expected)
        => Assert.Equal(expected, HologramParser.FormatPassTime(seconds));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void FormatPassTime_Not_Cleared_Is_Empty(int seconds)
        => Assert.Equal("", HologramParser.FormatPassTime(seconds));

    [Fact]
    public void IsCleared_And_Roles_Presence_Are_Equivalent()
    {
        // 实测严格等价:passTime > 0 ⟺ roles 键存在(95/95 与 43/43)
        var details = ParseDetails(DetailsJson);
        var records = details.ChallengeInfo!.Values.SelectMany(v => v).ToList();
        // 防空转:夹具若解析成空集合,下面的 foreach 会一条都不查却"全绿"
        Assert.NotEmpty(records);
        Assert.Contains(records, HologramParser.IsCleared);
        Assert.Contains(records, r => !HologramParser.IsCleared(r));
        foreach (var record in records)
        {
            Assert.Equal(HologramParser.IsCleared(record), record.Roles is { Count: > 0 });
        }
    }

    // ---------- 排序与统计 ----------

    [Fact]
    public void SortCountries_Is_Descending_By_Sort()
    {
        // 实测降序即 演武 → 同步 → 幻痛 → 强袭,与界面四个 logo 顺序一致
        var sorted = HologramParser.SortCountries(ParseIndex(IndexJson).ChallengeList);
        Assert.Equal(["演武", "强袭"], sorted.Select(c => c.Country!.CountryName));
    }

    [Fact]
    public void SortCountries_Drops_Entries_Without_Country()
    {
        var groups = new List<HologramCountryGroup>
        {
            new() { Sort = 10, Country = null, IndexList = [] },
            new() { Sort = 5, Country = new HologramCountryInfo { CountryName = "强袭" } },
        };
        var sorted = HologramParser.SortCountries(groups);
        Assert.Single(sorted);
        Assert.Equal("强袭", sorted[0].Country!.CountryName);
    }

    [Fact]
    public void SortBosses_Is_Descending_By_Sort()
    {
        var index = ParseIndex(IndexJson);
        // 夹具里演武下发的 boss 是升序(109 在 110 前),排序后必须倒过来
        var yanwu = index.ChallengeList!.Single(c => c.Country!.CountryId == 100004);
        Assert.Equal([31010, 32010], yanwu.IndexList!.Select(b => b.BossId)); // 下发顺序(升序)

        var sorted = HologramParser.SortBosses(yanwu.IndexList);
        // sort 110(32010) 在 109(31010) 之前
        Assert.Equal([32010, 31010], sorted.Select(b => b.BossId));
    }

    [Fact]
    public void CountFullyCleared_Only_Counts_Tier6()
    {
        var details = ParseDetails(DetailsJson);
        var index = ParseIndex(IndexJson);
        var strong = index.ChallengeList!.Single(c => c.Country!.CountryId == 100003);
        var yanwu = index.ChallengeList!.Single(c => c.Country!.CountryId == 100004);

        // 强袭那个 boss 已通关 6 档 → 满档;演武的 5 档与 0 档都不算满档
        Assert.Equal(1, HologramParser.CountFullyCleared(strong.IndexList));
        Assert.Equal(0, HologramParser.CountFullyCleared(yanwu.IndexList));
    }

    [Fact]
    public void IsFullyCleared_Boundary()
    {
        Assert.False(HologramParser.IsFullyCleared(5));
        Assert.True(HologramParser.IsFullyCleared(6));
        Assert.True(HologramParser.IsFullyCleared(7)); // 越界也不当"未满档"
    }

    // ---------- 响应形态边界 ----------

    [Fact]
    public void Empty_Index_Payload_Lacks_Unlock_Keys()
    {
        // 实测:roleId 不存在时返回 200 + challengeList:[] 且 isUnlock/open 键缺失 → 必须可空
        var index = ParseIndex("""{"challengeList":[],"wikiUrl":"https://wiki.kurobbs.com/mc/home"}""");
        Assert.Null(index.IsUnlock);
        Assert.Null(index.Open);
        Assert.Empty(index.ChallengeList!);
    }

    [Fact]
    public void Null_Data_String_Deserializes_To_Null()
    {
        // 实测:roleId/serverId 无效时 challengeDetails 的 data 是字符串 "null"
        var details = JsonSerializer.Deserialize("null", HologramJsonContext.Default.HologramDetailData);
        Assert.Null(details);
        Assert.Empty(HologramParser.TiersOf(details, 31010));
    }

    [Fact]
    public void Country_Directory_Prefix_Fields_Are_Not_Needed()
    {
        // homePageIcon 等实测是目录前缀(无文件名),只有 homePageImage 是完整 URL。
        // 模型只保留可用字段,避免出现永远为空的死字段。
        var index = ParseIndex(IndexJson);
        var country = index.ChallengeList!.Single(c => c.Country!.CountryId == 100004).Country!;
        Assert.EndsWith(".png", country.HomePageImage);
        Assert.Equal(100004, country.CountryId);
    }

    [Fact]
    public void Index_Keeps_Misspelled_ContryId_Field()
    {
        // 服务端拼作 contryId;JSON 映射必须按拼写来,否则该字段恒为 0
        var index = ParseIndex(IndexJson);
        var boss = index.ChallengeList!
            .Single(c => c.Country!.CountryId == 100004).IndexList!
            .Single(b => b.BossId == 32010);
        Assert.Equal(100004, boss.CountryId);
    }

    [Fact]
    public void Element_Icons_Map_To_Local_Attr_Assets()
    {
        // natureId 与 roleData 的 attributeId 同口径(1..6 = 冷凝/热熔/导电/气动/衍射/湮灭),
        // 可复用本地 Assets/attr/{id}.png
        for (var id = 1; id <= 6; id++)
        {
            var name = McKuro.ViewModels.TowerViewModel.ElementNameOf(id);
            Assert.False(string.IsNullOrEmpty(name));
            var path = McKuro.ViewModels.TowerViewModel.ElementIconPathOf(id);
            Assert.EndsWith(Path.Combine("attr", $"{id}.png"), path);
        }
        // 越界 ID 退化为空(不指向不存在的文件)
        Assert.Equal("", McKuro.ViewModels.TowerViewModel.ElementNameOf(0));
        Assert.Equal("", McKuro.ViewModels.TowerViewModel.ElementNameOf(7));
        Assert.Equal("", McKuro.ViewModels.TowerViewModel.ElementIconPathOf(0));
    }
}
