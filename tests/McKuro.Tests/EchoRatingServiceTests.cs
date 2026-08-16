using McKuro.Core.Models.Roles;
using McKuro.Core.Services.Roles;

namespace McKuro.Tests;

/// <summary>声骸词条评级/角色养成达成度测试(算法对齐 WutheringWavesTool)。</summary>
public class EchoRatingServiceTests
{
    private static EchoInfo Echo(params (string Name, string Value)[] subProps)
        => new()
        {
            SubProps = subProps.Select(p => new EchoProp { AttributeName = p.Name, AttributeValue = p.Value }).ToList(),
        };

    [Fact]
    public void Perfect_Echo_Rates_Ace_With_Full_Score()
    {
        // 2 个三权(暴击/暴伤) + 3 个低权词条,数值拉满 → 结构 ACE + 数值 ACE
        var echo = Echo(
            ("暴击", "10.5%"),
            ("暴击伤害", "21%"),
            ("攻击", "60"),
            ("生命", "580"),
            ("防御", "60"));
        var rating = EchoRatingService.RateEcho(echo);
        Assert.Equal(EchoRatingLevel.Ace, rating.PhantomStatus);
        Assert.Equal(EchoRatingLevel.Ace, rating.PropStatus);
        Assert.Equal(10, rating.Score);
    }

    [Fact]
    public void Bad_Echo_Rates_N()
    {
        // 无有效词条(权重 0) → N
        var echo = Echo(("生命", "300"));
        var rating = EchoRatingService.RateEcho(echo);
        Assert.Equal(EchoRatingLevel.N, rating.PhantomStatus);
        Assert.Equal(EchoRatingLevel.N, rating.PropStatus);
        Assert.Equal(2, rating.Score); // N(1) + N(1)
    }

    [Fact]
    public void Role_Rating_Computes_Total_And_Achievement()
    {
        var echoes = new[]
        {
            Echo(("暴击", "10.5%"), ("暴击伤害", "21%"), ("攻击", "60"), ("生命", "580"), ("防御", "60")),
            Echo(("暴击", "10.5%"), ("暴击伤害", "21%"), ("攻击", "60"), ("生命", "580"), ("防御", "60")),
            Echo(("暴击", "10.5%"), ("暴击伤害", "21%"), ("攻击", "60"), ("生命", "580"), ("防御", "60")),
            Echo(("暴击", "10.5%"), ("暴击伤害", "21%"), ("攻击", "60"), ("生命", "580"), ("防御", "60")),
            Echo(("暴击", "10.5%"), ("暴击伤害", "21%"), ("攻击", "60"), ("生命", "580"), ("防御", "60")),
        };
        var rating = EchoRatingService.RateRole(echoes);
        Assert.Equal(50, rating.TotalScore);
        Assert.Equal(100, rating.AchievementPercent);
        Assert.Equal(EchoRatingLevel.Ace, rating.Level);
    }

    [Fact]
    public void Role_Rating_Partial_Achievement()
    {
        // 5 个差声骸(各 N/N=2分) → 总分 10,达成度 20%
        var echoes = Enumerable.Range(0, 5).Select(_ => Echo(("生命", "300"))).ToList();
        var rating = EchoRatingService.RateRole(echoes);
        Assert.Equal(10, rating.TotalScore);
        Assert.Equal(20, rating.AchievementPercent);
        Assert.Equal(EchoRatingLevel.N, rating.Level);
    }

    [Fact]
    public void Percent_Values_Are_Normalized()
    {
        // 攻击 + % 值 → 攻击百分比(权重 3,max 11.6);2 个三权 + 1 个二权 → SS
        var echo = Echo(("攻击", "11.6%"), ("攻击", "10%"), ("共鸣效率", "12.4%"));
        var rating = EchoRatingService.RateEcho(echo);
        Assert.Equal(EchoRatingLevel.SS, rating.PhantomStatus);
    }

    [Theory]
    [InlineData("暴击", "10.5%", 3)]
    [InlineData("暴击伤害", "21%", 3)]
    [InlineData("攻击", "11.6%", 3)]   // 攻击+% → 攻击百分比 → 3
    [InlineData("共鸣效率", "12.4%", 2)]
    [InlineData("防御", "60", 1)]
    [InlineData("未知属性", "5%", 0)]
    public void GetPropLevel_Returns_Weight(string name, string value, int expected)
    {
        Assert.Equal(expected, EchoRatingService.GetPropLevel(name, value));
    }

    [Fact]
    public void EchoProp_EffectiveLevel_Uses_Weight_When_Level_Is_Zero()
    {
        // 库街区不返回 level(0) → EffectiveLevel 按权重算
        var prop = new EchoProp { AttributeName = "暴击伤害", AttributeValue = "21%", Level = 0 };
        Assert.Equal(3, prop.EffectiveLevel);
    }

    [Fact]
    public void EchoProp_EffectiveLevel_Prefers_Interface_Level()
    {
        var prop = new EchoProp { AttributeName = "暴击伤害", AttributeValue = "21%", Level = 2 };
        Assert.Equal(2, prop.EffectiveLevel);
    }
}
