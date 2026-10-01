using McKuro.Core.Models.Gacha;
using McKuro.Core.Services;

namespace McKuro.Tests;

/// <summary>
/// 保底状态标题的歪率格式化回归测试。
/// <para>
/// 根因:标题曾把 <c>OffBannerRate * 100</c> 原样交给格式化,double 全精度被拼进文案 ——
/// 界面显示"歪率 33.33333333333333%"(用户实际截图)。修复为在模型层
/// <see cref="PoolStats.OffBannerPercent"/> 取整到两位小数。
/// </para>
/// 这里直接驱动真实的 <see cref="PoolStats.GuaranteeHeaderText"/>,而非复刻取整表达式,
/// 因此在产品代码回退成裸 double 时会真实失败。
/// </summary>
public class GachaPercentFormatTests : IDisposable
{
    private readonly Func<string, string> _originalResolver;

    public GachaPercentFormatTests()
    {
        // 测试进程内 LanguageService 未加载时 CoreStrings.Resolver 未注册,
        // GuaranteeHeaderText 会走 fallback 中文模板(其中直接内插数值),同样可验证取整行为。
        _originalResolver = CoreStrings.Resolver;
        CoreStrings.Resolver = key => key switch
        {
            "Gacha.PityHeader" => "保底状态: {0} · 歪率 {1}%",
            _ => "",
        };
    }

    public void Dispose()
    {
        // CoreStrings.Resolver 是进程级静态:恢复原值,避免影响其他测试的文案断言
        CoreStrings.Resolver = _originalResolver;
    }

    private static PoolStats PoolWithRate(double? rate) => new()
    {
        PoolType = CardPoolType.RoleActivity,
        OffBannerRate = rate,
    };

    /// <summary>1/3 歪率:修复前渲染为 33.33333333333333%,修复后为 33.33%。</summary>
    [Fact]
    public void GuaranteeHeader_RoundsOffBannerRate_ToTwoDecimals()
    {
        var text = PoolWithRate(1.0 / 3.0).GuaranteeHeaderText;

        Assert.DoesNotContain("3333333333", text); // 修复前的特征:裸 double 全精度
        Assert.Contains("33.33%", text);
    }

    [Fact]
    public void GuaranteeHeader_TruncatesLongBinaryFractions()
    {
        // 2/7 = 0.2857142857142857:修复前会把整串小数带进文案
        var text = PoolWithRate(2.0 / 7.0).GuaranteeHeaderText;

        Assert.DoesNotContain("2857142857142857", text);
        Assert.Contains("28.57%", text);
    }

    [Fact]
    public void GuaranteeHeader_KeepsExactFractionsCompact()
    {
        // 50% 不应渲染成 50.00%(Math.Round 不补零,与统计条"出货率 1.77%"同口径)
        var text = PoolWithRate(0.5).GuaranteeHeaderText;

        Assert.Contains("50%", text);
        Assert.DoesNotContain("50.00", text);
    }

    [Fact]
    public void GuaranteeHeader_HandlesZero_AndNullRate()
    {
        // 无法判定(null)与 0% 都应稳定渲染,不出现 NaN/异常
        Assert.Contains("0%", PoolWithRate(0).GuaranteeHeaderText);
        Assert.Contains("0%", PoolWithRate(null).GuaranteeHeaderText);
    }

    [Fact]
    public void OffBannerPercent_MatchesPercentBasis()
    {
        // 内部保持 0~100 的百分比基数(模板自带 % 号,不能重复乘)
        Assert.Equal(33.33, PoolWithRate(1.0 / 3.0).OffBannerPercent);
        Assert.Equal(100, PoolWithRate(1).OffBannerPercent);
        Assert.Equal(0, PoolWithRate(null).OffBannerPercent);
    }
}
