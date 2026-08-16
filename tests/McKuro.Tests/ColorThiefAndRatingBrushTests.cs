using System.Runtime.InteropServices;
using Avalonia.Media;
using McKuro.Core.Services.Roles;
using McKuro.Services;
using McKuro.ViewModels;

namespace McKuro.Tests;

/// <summary>ColorThiefHelper 主色提取测试(用纯像素缓冲,不依赖 Avalonia 渲染平台)。</summary>
public class ColorThiefHelperTests
{
    /// <summary>分配 w×h 的 BGRA8888 缓冲并填充指定颜色(alpha=255)。</summary>
    private static IntPtr AllocSolid(int w, int h, Color color)
    {
        var ptr = Marshal.AllocHGlobal(w * h * 4);
        for (int i = 0; i < w * h; i++)
        {
            Marshal.WriteByte(ptr, i * 4 + 0, color.B);
            Marshal.WriteByte(ptr, i * 4 + 1, color.G);
            Marshal.WriteByte(ptr, i * 4 + 2, color.R);
            Marshal.WriteByte(ptr, i * 4 + 3, 255);
        }
        return ptr;
    }

    [Fact]
    public void GetDominantColors_Returns_Red_For_SolidRed()
    {
        var ptr = AllocSolid(64, 64, Color.FromRgb(200, 30, 30));
        try
        {
            var colors = ColorThiefHelper.FromBgraBytes(ptr, 64, 64, 64 * 4, 2);
            Assert.NotEmpty(colors);
            // 主色应接近红色(R 显著高于 G/B)
            Assert.True(colors[0].R > 120, $"主色 R 应较大,实际={colors[0]}");
            Assert.True(colors[0].G < 100, $"主色 G 应较小,实际={colors[0]}");
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    [Fact]
    public void GetDominantColors_Returns_Empty_For_ZeroPointer()
    {
        Assert.Empty(ColorThiefHelper.FromBgraBytes(IntPtr.Zero, 64, 64, 64 * 4, 2));
    }

    [Fact]
    public void GetDominantColors_Handles_White_And_Black()
    {
        // 近白/近黑应被忽略;纯白图提取不到主色(返回空,不崩溃)
        var white = AllocSolid(64, 64, Colors.White);
        try
        {
            var colors = ColorThiefHelper.FromBgraBytes(white, 64, 64, 64 * 4, 2);
            Assert.Empty(colors); // 纯白被过滤
        }
        finally
        {
            Marshal.FreeHGlobal(white);
        }
    }
}

/// <summary>主题自适应转换器测试(默认无 Avalonia Application → 按浅色主题返回深色版)。</summary>
public class RatingBrushConverterTests
{
    [Fact]
    public void EchoRatingLevel_SSS_Returns_DarkYellow_In_Light()
    {
        var c = new EchoRatingLevelBrushConverter();
        var brush = Assert.IsType<SolidColorBrush>(c.Convert(EchoRatingLevel.SSS, typeof(IBrush), null, null));
        Assert.Equal(Color.Parse("#a88400"), brush.Color);
    }

    [Fact]
    public void EchoRatingLevel_Ace_Returns_Red()
    {
        var c = new EchoRatingLevelBrushConverter();
        var brush = Assert.IsType<SolidColorBrush>(c.Convert(EchoRatingLevel.Ace, typeof(IBrush), null, null));
        Assert.Equal(Color.Parse("#e33737"), brush.Color);
    }

    [Fact]
    public void PropLevel_3_Returns_DarkYellow_In_Light()
    {
        var c = new PropLevelBrushConverter();
        var brush = Assert.IsType<SolidColorBrush>(c.Convert(3, typeof(IBrush), null, null));
        Assert.Equal(Color.Parse("#a88400"), brush.Color);
    }

    [Fact]
    public void PropLevel_0_Returns_Gray()
    {
        var c = new PropLevelBrushConverter();
        var brush = Assert.IsType<SolidColorBrush>(c.Convert(0, typeof(IBrush), null, null));
        Assert.Equal(Color.Parse("#9e9e9e"), brush.Color);
    }
}
