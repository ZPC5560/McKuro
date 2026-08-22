using McKuro.ViewModels;

namespace McKuro.Tests;

/// <summary>
/// 五星 UP/歪 徽章转换器测试。
/// 不可判定池(常驻/新手等)IsOffBanner 为 null:徽章隐藏(只显示占位状态而无意义),不歪率等统计亦不展示。
/// </summary>
public class FiveStarFlagConverterTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public void UpFlagVisible_Shown_Only_WhenJudgeable(bool? flag, bool expected)
    {
        var result = UpFlagVisibleConverter.Instance.Convert(
            flag, typeof(bool), null, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(null, "-")]
    [InlineData(true, "歪")]
    [InlineData(false, "UP")]
    public void FiveStarFlagText_Labels(bool? flag, string expected)
    {
        var result = FiveStarFlagTextConverter.Instance.Convert(
            flag, typeof(string), null, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(expected, result);
    }
}
