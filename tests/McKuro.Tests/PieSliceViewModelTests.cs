using McKuro.ViewModels;

namespace McKuro.Tests;

/// <summary>
/// 环形图扇区生成与统计图切换辅助逻辑的测试。
/// 环形图:外弧顺时针 + 内弧逆时针回绕(中空圆环);整圈时用两个半圆弧拼合。
/// </summary>
public class PieSliceViewModelTests
{
    [Fact]
    public void BuildPie_HalfSplit_ProducesRingSectors()
    {
        var slices = PieSliceViewModel.BuildPie(
            [("A", 60), ("B", 40)],
            [Avalonia.Media.Color.Parse("#1677FF"), Avalonia.Media.Color.Parse("#52C41A")]);

        Assert.Equal(2, slices.Count);
        foreach (var slice in slices)
        {
            // 环形:外弧(46) + 内弧(28),不再含圆心线段
            Assert.Contains("A 46,46", slice.Data);
            Assert.Contains("A 28,28", slice.Data);
            Assert.DoesNotContain("50,50", slice.Data);
        }
    }

    [Fact]
    public void BuildPie_SingleFullCircle_UsesFullRing()
    {
        var slices = PieSliceViewModel.BuildPie(
            [("A", 100)],
            [Avalonia.Media.Color.Parse("#1677FF")]);

        var slice = Assert.Single(slices);
        // 整圈特例:内外两个圆(各用两段半圆弧),环形中空
        Assert.Equal(4, slice.Data.Split("A ").Length - 1);
        Assert.Contains("A 46,46", slice.Data);
        Assert.Contains("A 28,28", slice.Data);
        Assert.DoesNotContain("50,50", slice.Data);
    }

    [Theory]
    [InlineData(0, "0", true)]
    [InlineData(1, "0", false)]
    [InlineData(2, "2", true)]
    [InlineData(3, "x", false)]
    public void IndexEqualsConverter_Compares_IndexToParameter(int value, string parameter, bool expected)
    {
        var result = IndexEqualsConverter.Instance.Convert(value, typeof(bool), parameter, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(expected, result);
    }
}
