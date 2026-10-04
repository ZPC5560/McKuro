using System.Text.Json;
using McKuro.Core.Models.Guide;

namespace McKuro.Tests;

/// <summary>
/// 配队推荐(teammate)JSON 映射测试。
/// <para>用实测抓包片段(1311 的 <c>teammate.items[0]</c>)经 <see cref="GuideJsonContext"/> 源生成上下文反序列化,
/// 覆盖 <see cref="GuideTeammateItem"/> 实测存在的 7 个字段以及 <see cref="GuideRoleRef"/> 新增字段
/// (illustrationPictureUrl / isAcquired / element / rolePlays)。</para>
/// </summary>
public class GuideTeammateMappingTests
{
    /// <summary>实测证据:1311 的 teammate.items[0](main 已拥有 / spares 未拥有 / echoAttributes 5 项)。</summary>
    private const string TeammateJson =
        """
        {"id":1311,"teammate":{"items":[
          {"main":{"roleGbId":"1505","cardPictureUrl":"http://img/sa.png","illustrationPictureUrl":"http://img/sa2.png","star":5,"texts":[{"language":"zh-Hans","name":"守岸人","skillDisplay":"基础连招：普通攻击*5 → 延奏离场"}],"isAcquired":true},
           "spares":[{"roleGbId":"1309","cardPictureUrl":"http://img/sp.png","illustrationPictureUrl":"http://img/sp2.png","star":5,"texts":[{"language":"zh-Hans","name":"散华","skillDisplay":"基础连招：普攻*3 → 共鸣技能"}],"isAcquired":false}],
           "weapon":{"gbId":"21050036","pictureUrl":"http://img/w.png","star":5,"texts":[{"language":"zh-Hans","name":"星序协响"}],"status":1,"isAcquired":false,"isFinished":null},
           "echoProps":{"gbId":"60000605","pictureUrl":"http://img/e.png","star":5,"cost":4,"texts":[{"language":"zh-Hans","name":"无归的谬误"}]},
           "echoSetEffect2":{"gbId":"14","pictureUrl":"http://img/set.png","echoSet":5,"texts":[{"language":"zh-Hans","name":"隐世回光","description":"全队共鸣者攻击提升15%"}]},
           "echoSetEffect5":null,
           "echoAttributes":[{"cost":4,"attribute":{"texts":[{"language":"zh-Hans","name":"治疗效果加成"}]}},{"cost":3,"attribute":{"texts":[{"language":"zh-Hans","name":"攻击%"}]}},{"cost":3,"attribute":{"texts":[{"language":"zh-Hans","name":"衍射伤害加成"}]}},{"cost":1,"attribute":{"texts":[{"language":"zh-Hans","name":"攻击%"}]}},{"cost":1,"attribute":{"texts":[{"language":"zh-Hans","name":"攻击%"}]}}]}
         ]}}
        """;

    /// <summary>实测证据变体:队友「未拥有」(isAcquired 缺省/为 false)用于未拥有标识判定。</summary>
    private const string NotOwnedJson =
        """
        {"id":1311,"teammate":{"items":[
          {"main":{"roleGbId":"1206","cardPictureUrl":"http://img/m.png","star":4,"texts":[{"language":"zh-Hans","name":"秧秧"}],"isAcquired":false}}
        ]}}
        """;

    /// <summary>侦察报告:main 还带 element(含 secondPictureUrl)与 rolePlays 两个字段。</summary>
    private const string ElementAndRolePlaysJson =
        """
        {"id":1311,"teammate":{"items":[
          {"main":{"roleGbId":"1505","star":5,"texts":[{"language":"zh-Hans","name":"守岸人"}],"isAcquired":true,
            "element":{"gbId":"3","pictureUrl":"http://img/elem.png","secondPictureUrl":"http://img/elem2.png"},
            "rolePlays":[{"gbId":"9006","pictureUrl":"http://img/rp.png","secondPictureUrl":"http://img/rp2.png"}]}}
        ]}}
        """;

    private static GuideIntroductionInfo Parse(string json)
        => JsonSerializer.Deserialize(json, GuideJsonContext.Default.GuideIntroductionInfo)!;

    [Fact]
    public void TeammateItem_Maps_Weapon_EchoProps_SetEffects_And_Attributes()
    {
        var item = Assert.Single(Parse(TeammateJson).Teammate!.Items!);

        // weapon(与 weapon.current 同构)
        Assert.NotNull(item.Weapon);
        Assert.Equal("星序协响", item.Weapon!.Name);
        Assert.Equal(5, item.Weapon.Star);
        Assert.Equal(1, item.Weapon.Status); // 1=首选
        Assert.False(item.Weapon.IsAcquired);
        Assert.Equal("http://img/w.png", item.Weapon.PictureUrl);

        // echoProps(4C 主词条)
        Assert.NotNull(item.EchoProps);
        Assert.Equal("无归的谬误", item.EchoProps!.Name);
        Assert.Equal(4, item.EchoProps.Cost);
        Assert.Equal(5, item.EchoProps.Star);

        // echoSetEffect2 / echoSetEffect5
        Assert.NotNull(item.EchoSetEffect2);
        Assert.Equal("隐世回光", item.EchoSetEffect2!.Name);
        Assert.Equal(5, item.EchoSetEffect2.EchoSet);
        Assert.Equal("全队共鸣者攻击提升15%",
            item.EchoSetEffect2.Texts?.FirstOrDefault(t => t.Language == "zh-Hans")?.Description);
        Assert.Null(item.EchoSetEffect5); // 实测为 null,不应报错

        // echoAttributes:5 项,首项为 4C 治疗效果加成
        Assert.NotNull(item.EchoAttributes);
        Assert.Equal(5, item.EchoAttributes!.Count);
        Assert.Equal("治疗效果加成", item.EchoAttributes[0].Attribute?.Name);
        Assert.Equal(4, item.EchoAttributes[0].Cost);
        Assert.Equal("攻击%", item.EchoAttributes[3].Attribute?.Name);
        Assert.Equal(1, item.EchoAttributes[3].Cost);
    }

    [Fact]
    public void TeammateMain_IsNotOwned_Reflects_IsAcquired()
    {
        var acquired = Assert.Single(Parse(TeammateJson).Teammate!.Items!).Main!;
        Assert.True(acquired.IsAcquired);
        Assert.False(acquired.IsNotOwned); // isAcquired:true → 已拥有
        Assert.Equal("守岸人", acquired.Name);
        Assert.Equal("http://img/sa2.png", acquired.IllustrationPictureUrl);
        Assert.Equal("基础连招：普通攻击*5 → 延奏离场", acquired.SkillDisplay);

        var notOwned = Assert.Single(Parse(NotOwnedJson).Teammate!.Items!).Main!;
        Assert.False(notOwned.IsAcquired);
        Assert.True(notOwned.IsNotOwned); // isAcquired:false → 未拥有
        Assert.Equal("秧秧", notOwned.Name);
    }

    [Fact]
    public void TeammateSpares_Parsed_With_Same_Shape_As_Main()
    {
        var item = Assert.Single(Parse(TeammateJson).Teammate!.Items!);

        var spare = Assert.Single(item.Spares!);
        // 与 main 字段同构:roleGbId / 立绘 / 星级 / 文本 / 拥有状态
        Assert.Equal("1309", spare.RoleGbId);
        Assert.Equal("散华", spare.Name);
        Assert.Equal(5, spare.Star);
        Assert.Equal("http://img/sp.png", spare.CardPictureUrl);
        Assert.Equal("http://img/sp2.png", spare.IllustrationPictureUrl);
        Assert.Equal("基础连招：普攻*3 → 共鸣技能", spare.SkillDisplay);
        Assert.False(spare.IsAcquired);
        Assert.True(spare.IsNotOwned);
    }

    [Fact]
    public void TeammateMain_Maps_Element_With_Second_Picture_And_RolePlays()
    {
        var main = Assert.Single(Parse(ElementAndRolePlaysJson).Teammate!.Items!).Main!;

        Assert.NotNull(main.Element);
        Assert.Equal("3", main.Element!.GbId);
        Assert.Equal("http://img/elem.png", main.Element.PictureUrl);
        Assert.Equal("http://img/elem2.png", main.Element.SecondPictureUrl);

        var play = Assert.Single(main.RolePlays!);
        Assert.Equal("9006", play.GbId);
        Assert.Equal("http://img/rp.png", play.PictureUrl);
        Assert.Equal("http://img/rp2.png", play.SecondPictureUrl);
    }

    [Fact]
    public void Teammate_Fields_Survive_Cache_Json_RoundTrip()
    {
        // guide_cache 表整体序列化 GuideIntroductionInfo:新增字段必须能经源生成上下文往返
        var info = Parse(TeammateJson);
        var json = JsonSerializer.Serialize(info, GuideJsonContext.Default.GuideIntroductionInfo);
        var restored = JsonSerializer.Deserialize(json, GuideJsonContext.Default.GuideIntroductionInfo)!;

        var item = Assert.Single(restored.Teammate!.Items!);
        Assert.Equal("星序协响", item.Weapon?.Name);
        Assert.Equal("无归的谬误", item.EchoProps?.Name);
        Assert.Equal(4, item.EchoProps?.Cost);
        Assert.Equal("隐世回光", item.EchoSetEffect2?.Name);
        Assert.Equal(5, item.EchoAttributes?.Count);
        Assert.Equal("治疗效果加成", item.EchoAttributes?[0].Attribute?.Name);
        Assert.False(item.Main!.IsNotOwned);
        Assert.Equal("http://img/sa2.png", item.Main.IllustrationPictureUrl);
        Assert.Equal("散华", Assert.Single(item.Spares!).Name);
        Assert.True(Assert.Single(item.Spares!).IsNotOwned);
    }

    [Fact]
    public void Teammate_Cache_JsonContext_Covers_New_Teammate_Types()
    {
        // 源生成上下文必须覆盖 teammate 用到的类型,否则 AOT 下会抛 NotSupportedException
        Assert.NotNull(GuideJsonContext.Default.GuideIntroductionInfo);
        Assert.NotNull(GuideJsonContext.Default.GuideRoleRef);
        Assert.NotNull(GuideJsonContext.Default.GuideRolePlay);
        Assert.NotNull(GuideJsonContext.Default.ListGuideRolePlay);

        var refJson = JsonSerializer.Serialize(
            new GuideRoleRef { RoleGbId = "1505", IsAcquired = false, IllustrationPictureUrl = "u" },
            GuideJsonContext.Default.GuideRoleRef);
        Assert.Contains("\"isAcquired\":false", refJson);
        Assert.Contains("\"illustrationPictureUrl\":\"u\"", refJson);
    }
}
