using McKuro.Core.Models.Roles;
using McKuro.Core.Services.Roles;

namespace McKuro.Tests;

/// <summary>声骸词条评级/角色养成达成度测试(算法:主副词条统一口径评分,见 <see cref="EchoRatingService"/>)。</summary>
public class EchoRatingServiceTests
{
    private static EchoInfo Echo(params (string Name, string Value)[] subProps)
        => new()
        {
            SubProps = subProps.Select(p => new EchoProp { AttributeName = p.Name, AttributeValue = p.Value }).ToList(),
        };

    /// <summary>全部词条都给首位权重(2.0)的 Wiki 权重表(受双暴 1.5/攻击% 1.25 上限约束,用于验证上限压制口径)。</summary>
    private static IReadOnlyDictionary<string, double> AllTopWeight => new Dictionary<string, double>(StringComparer.Ordinal)
    {
        ["暴击"] = 2.0,
        ["暴击伤害"] = 2.0,
        ["攻击百分比"] = 2.0,
        ["攻击"] = 2.0,
        ["生命"] = 2.0,
        ["防御"] = 2.0,
    };

    [Fact]
    public void Perfect_Echo_Rates_Ace_With_Full_Score()
    {
        // 全部词条给首位权重(2.0),但双暴受全局上限压制为 1.5、攻击% 为 1.25。
        // 该用例无 mainProps → 走兼容口径(固定分母 5×旧顶档 2.0 = 10):
        // 暴击 1.5 + 暴伤 1.5 + 攻击 2.0 + 生命 2.0 + 防御 2.0 = 9.0 → 9/10 = 90 分
        var echo = Echo(
            ("暴击", "10.5%"),
            ("暴击伤害", "21%"),
            ("攻击", "60"),
            ("生命", "580"),
            ("防御", "70"));
        var rating = EchoRatingService.RateEcho(echo, AllTopWeight);
        Assert.Equal(EchoRatingLevel.Ace, rating.PhantomStatus);
        Assert.Equal(EchoRatingLevel.Ace, rating.PropStatus);
        Assert.Equal(90.0, rating.Score);
    }

    [Fact]
    public void Bad_Echo_Rates_Low()
    {
        // 单条低价值词条(生命权重 1 → 0.5)且 Roll 不到一半 → 2.6 分,未毕业档(S)
        var echo = Echo(("生命", "300"));
        var rating = EchoRatingService.RateEcho(echo, null);
        Assert.Equal(EchoRatingLevel.S, rating.PhantomStatus);
        Assert.Equal(EchoRatingLevel.S, rating.PropStatus);
        Assert.Equal(2.6, rating.Score);
    }

    [Fact]
    public void RateEcho_Without_Official_Valid_Falls_Back_To_Universal_Weights()
    {
        // 无 Wiki 权重、无官方 valid → 通用权重表(含双暴上限:暴击/暴伤 1.5、攻击% 1.25)
        // 暴击 10.5%(1.5) + 暴击伤害 21%(1.5) + 攻击 60(1.0) = 4.0 → 4/10 = 40 分
        var echo = Echo(("暴击", "10.5%"), ("暴击伤害", "21%"), ("攻击", "60"));
        var rating = EchoRatingService.RateEcho(echo, null);
        Assert.Equal(40.0, rating.Score);
    }

    [Fact]
    public void RateEcho_Official_Invalid_Substat_Scores_Zero_Contribution()
    {
        // 官方 valid=false → 权重 0,该词条完全不参与计分(最高优先级,不受权重表影响)
        var echo = new EchoInfo
        {
            SubProps =
            [
                new EchoProp { AttributeName = "暴击", AttributeValue = "10.5%", Valid = true },
                new EchoProp { AttributeName = "防御", AttributeValue = "70", Valid = false },
            ],
        };
        var rating = EchoRatingService.RateEcho(echo, null);
        // 只有暴击贡献:核心(≥3)→ 上限 1.5,满 Roll → 1.5/10 = 15 分
        Assert.Equal(15.0, rating.Score);
    }

    [Fact]
    public void Role_Rating_Computes_Total_And_Achievement()
    {
        var echoes = Enumerable.Range(0, 5)
            .Select(_ => Echo(
                ("暴击", "10.5%"),
                ("暴击伤害", "21%"),
                ("攻击", "60"),
                ("生命", "580"),
                ("防御", "70")))
            .ToList();
        var rating = EchoRatingService.RateRole(echoes);
        // 通用权重表(含双暴上限):暴击/暴伤各 1.5 + 攻击 1.0 + 生命/防御各 0.5 = 5.0 → 单件 50.0 分
        Assert.Equal(50.0, rating.Echoes[0].Score);
        Assert.Equal(250.0, rating.TotalScore);
        Assert.Equal(500.0, rating.MaxScore);
        Assert.Equal(50, rating.AchievementPercent);
        Assert.Equal(EchoRatingLevel.SSS, rating.Level);
    }

    [Fact]
    public void Role_Rating_Partial_Achievement()
    {
        // 5 个差声骸(各 2.6 分)→ 总分 13.0,单件均分 2.6 → 达成度 2%,未毕业(S)
        var echoes = Enumerable.Range(0, 5).Select(_ => Echo(("生命", "300"))).ToList();
        var rating = EchoRatingService.RateRole(echoes);
        Assert.Equal(13.0, rating.TotalScore);
        Assert.Equal(2, rating.AchievementPercent);
        Assert.Equal(EchoRatingLevel.S, rating.Level);
    }

    [Fact]
    public void Percent_Values_Are_Normalized()
    {
        // 攻击 + % 值 → 攻击百分比(权重 3 → 上限 1.25,max 11.6)
        // 11.6%(1.25) + 10%(1.25×0.862) + 共鸣效率 12.4%(1.0) = 3.327 → 33.3 分 → 小毕业(SS)
        var echo = Echo(("攻击", "11.6%"), ("攻击", "10%"), ("共鸣效率", "12.4%"));
        var rating = EchoRatingService.RateEcho(echo, null);
        Assert.Equal(33.3, rating.Score);
        Assert.Equal(EchoRatingLevel.SS, rating.PhantomStatus);
    }

    [Fact]
    public void RateRole_Level_Uses_Per_Echo_Mean_Not_Sum()
    {
        // 等级按"单件均分"判定:5 件单件 50 分 → 档位按 50 判定(SSS);
        // 若误按总分 250 判定会溢出档位表,永远拿不到正确等级。
        var echoes = Enumerable.Range(0, 5)
            .Select(_ => Echo(("暴击", "10.5%"), ("暴击伤害", "21%"), ("攻击", "60"), ("生命", "580"), ("防御", "70")))
            .ToList();
        var rating = EchoRatingService.RateRole(echoes);
        Assert.Equal(50.0, rating.Echoes[0].Score);
        Assert.Equal(rating.Echoes.Sum(e => e.Score), rating.TotalScore);
        Assert.Equal(EchoRatingLevel.SSS, rating.Level);
    }

    [Fact]
    public void GraduationTextOf_Maps_Four_Tiers()
    {
        Assert.Equal("完美毕业", EchoRatingService.GraduationTextOf(EchoRatingLevel.Ace));
        Assert.Equal("毕业", EchoRatingService.GraduationTextOf(EchoRatingLevel.SSS));
        Assert.Equal("小毕业", EchoRatingService.GraduationTextOf(EchoRatingLevel.SS));
        Assert.Equal("未毕业", EchoRatingService.GraduationTextOf(EchoRatingLevel.S));
        Assert.Equal("未毕业", EchoRatingService.GraduationTextOf(EchoRatingLevel.N));
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

    [Fact]
    public void EchoProp_Official_Valid_Overrides_Level_And_Weight_Table()
    {
        // valid=false → 0(灰),即使属性名是核心词条
        Assert.Equal(0, new EchoProp { AttributeName = "暴击", AttributeValue = "10.5%", Valid = false }.EffectiveLevel);
        // valid=true 且非核心 → 2(青,官方认可的有效词条)
        Assert.Equal(2, new EchoProp { AttributeName = "共鸣技能伤害加成", AttributeValue = "11.6%", Valid = true }.EffectiveLevel);
        // valid=true 且核心 → 3
        Assert.Equal(3, new EchoProp { AttributeName = "暴击伤害", AttributeValue = "21%", Valid = true }.EffectiveLevel);
    }
}
