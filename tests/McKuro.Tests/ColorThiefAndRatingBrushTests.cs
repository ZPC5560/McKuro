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

    [Fact]
    public void PickVivid_Single_Candidate_Returns_It()
    {
        var c = Color.FromRgb(30, 120, 200);
        Assert.Equal(c, ColorThiefHelper.PickVivid([c]));
    }

    [Fact]
    public void PickVivid_Prefers_Vivid_Yellow_Over_Dominant_Gray()
    {
        // 模拟海报配色:大面积暗灰蓝背景(出现最多) + 小面积高饱和主题黄
        var vivid = ColorThiefHelper.PickVivid(
        [
            Color.FromRgb(90, 105, 132),  // 灰蓝(第 1 名)
            Color.FromRgb(244, 205, 80),  // 主题黄(鲜明,第 2 名)
        ]);
        Assert.True(vivid.R > 200, $"应选鲜明黄,实际={vivid}");
        Assert.True(vivid.G > 150, $"应选鲜明黄,实际={vivid}");
    }

    [Fact]
    public void PickVivid_Keeps_Dominant_When_It_Is_Already_Vivid()
    {
        // 大面积高饱和红仍应压过小面积亮黄
        var vivid = ColorThiefHelper.PickVivid(
        [
            Color.FromRgb(200, 30, 30),   // 红(第 1 名,高饱和)
            Color.FromRgb(244, 205, 80),  // 黄(第 2 名)
        ]);
        Assert.True(vivid.R > 180 && vivid.G < 100, $"应保留大面积红,实际={vivid}");
    }

    [Fact]
    public void FromBgra_Then_PickVivid_Prefers_Yellow_Over_Gray_Background()
    {
        // 64×64 缓冲:3/4 灰蓝背景 + 1/4 黄色带 → vivid 应取黄
        const int w = 64, h = 64;
        var ptr = Marshal.AllocHGlobal(w * h * 4);
        try
        {
            for (int i = 0; i < w * h; i++)
            {
                var color = i % w < 48 ? Color.FromRgb(90, 105, 132) : Color.FromRgb(244, 205, 80);
                Marshal.WriteByte(ptr, i * 4 + 0, color.B);
                Marshal.WriteByte(ptr, i * 4 + 1, color.G);
                Marshal.WriteByte(ptr, i * 4 + 2, color.R);
                Marshal.WriteByte(ptr, i * 4 + 3, 255);
            }
            var vivid = ColorThiefHelper.PickVivid(ColorThiefHelper.FromBgraBytes(ptr, w, h, w * 4, 5));
            Assert.True(vivid.R > 200, $"应选鲜明黄,实际={vivid}");
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
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
