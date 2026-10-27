using McKuro.Services;

namespace McKuro.Tests;

/// <summary>
/// 网速「大字数值 + 小号单位」拆解口径的测试。
/// <para>
/// 回归背景(2026-10 用户反馈"网速显示不明显"):旧实现把速度作为一整串 12px 小字
/// (如 "37.78 MB/s")塞在信息行里,数值与单位同字号、与时间/磁盘同级,视觉上没有主次。
/// 现在拆成两部分由 XAML 分别排版,这里钉住三条:单位换算、(无效速度)占位、两位小数去尾零。
/// </para>
/// </summary>
public class SpeedTextFormatTests
{
    private const double Mb = 1024.0 * 1024.0;

    [Theory]
    [InlineData(0, "--", "")]           // 未开始/已暂停
    [InlineData(-5, "--", "")]          // 异常负值
    [InlineData(0.5, "--", "")]         // 不足 1 B/s 视为无速度
    public void NoMeaningfulSpeed_ShowsDashes_WithoutUnit(double bps, string value, string unit)
    {
        var d = SpeedTextFormat.FromBytesPerSecond(bps);
        Assert.Equal(value, d.Value);
        Assert.Equal(unit, d.Unit);
        Assert.True(d.IsEmpty);
    }

    [Fact]
    public void NonFinite_Speed_ShowsDashes()
    {
        var d = SpeedTextFormat.FromBytesPerSecond(double.NaN);
        Assert.Equal("--", d.Value);
        Assert.True(SpeedTextFormat.FromBytesPerSecond(double.PositiveInfinity).IsEmpty);
    }

    [Fact]
    public void Bytes_And_Kilobytes_Use_SmallUnits()
    {
        Assert.Equal("512 B/s", SpeedTextFormat.FromBytesPerSecond(512).Combined);
        Assert.Equal("1.5 KB/s", SpeedTextFormat.FromBytesPerSecond(1536).Combined);
    }

    [Fact]
    public void Megabytes_KeepTwoDecimals()
    {
        // 37.78MB/s 是启动器里最常见的量级
        var d = SpeedTextFormat.FromBytesPerSecond(37.78 * Mb);
        Assert.Equal("37.78", d.Value);
        Assert.Equal("MB/s", d.Unit);
        Assert.Equal("37.78 MB/s", d.Combined);
    }

    [Fact]
    public void TrailingZeros_AreTrimmed()
    {
        Assert.Equal("37.5", SpeedTextFormat.FromBytesPerSecond(37.5 * Mb).Value);
        Assert.Equal("37", SpeedTextFormat.FromBytesPerSecond(37.0 * Mb).Value);
    }

    [Fact]
    public void GigabyteRange_UpgradesUnit()
    {
        var d = SpeedTextFormat.FromBytesPerSecond(1.25 * 1024 * Mb);
        Assert.Equal("1.25", d.Value);
        Assert.Equal("GB/s", d.Unit);
    }

    [Fact]
    public void ExactlyOneMegabyte_IsMegabyte()
    {
        // 边界:恰好 1MB 应显示 1 MB/s,而不是 1024 KB/s
        var d = SpeedTextFormat.FromBytesPerSecond(Mb);
        Assert.Equal("1", d.Value);
        Assert.Equal("MB/s", d.Unit);
    }

    [Fact]
    public void FromMbps_Matches_BytesPerSecond_Conversion()
    {
        var a = SpeedTextFormat.FromMbps(12.5);
        var b = SpeedTextFormat.FromBytesPerSecond(12.5 * Mb);
        Assert.Equal(b, a);
    }

    [Fact]
    public void EmptyDisplay_Has_NoCombinedUnit()
    {
        Assert.Equal("--", SpeedTextFormat.FromBytesPerSecond(0).Combined);
        Assert.True(SpeedTextFormat.FromMbps(0).IsEmpty);
    }
}
