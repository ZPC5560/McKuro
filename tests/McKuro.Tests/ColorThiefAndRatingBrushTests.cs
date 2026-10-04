using System.Globalization;
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
        var brush = Assert.IsType<SolidColorBrush>(c.Convert(EchoRatingLevel.SSS, typeof(IBrush), null, CultureInfo.InvariantCulture));
        Assert.Equal(Color.Parse("#a88400"), brush.Color);
    }

    [Fact]
    public void EchoRatingLevel_Ace_Returns_Red()
    {
        var c = new EchoRatingLevelBrushConverter();
        var brush = Assert.IsType<SolidColorBrush>(c.Convert(EchoRatingLevel.Ace, typeof(IBrush), null, CultureInfo.InvariantCulture));
        Assert.Equal(Color.Parse("#e33737"), brush.Color);
    }

    [Fact]
    public void PropLevel_3_Returns_Vivid_Amber_In_Light()
    {
        // 2026-10 调整:有效词条(level3)改为更鲜艳的高饱和琥珀(#E08A00)
        var c = new PropLevelBrushConverter();
        var brush = Assert.IsType<SolidColorBrush>(c.Convert(3, typeof(IBrush), null, CultureInfo.InvariantCulture));
        Assert.Equal(Color.Parse("#E08A00"), brush.Color);
    }

    [Fact]
    public void PropLevel_0_Returns_Gray()
    {
        // 2026-10 调整:无效词条灰提亮为 #B0B0B0(浅色主题)/#666666(深色主题)
        var c = new PropLevelBrushConverter();
        var brush = Assert.IsType<SolidColorBrush>(c.Convert(0, typeof(IBrush), null, CultureInfo.InvariantCulture));
        Assert.Equal(Color.Parse("#B0B0B0"), brush.Color);
    }

    [Fact]
    public void PropLevel_1_Is_Gray_Like_Invalid()
    {
        // 回归(用户反馈「无效词条应该是灰色」):level1 = 防御/生命 这类低价值词条,
        // 必须与 level0 同为灰,不能像此前那样与 level2 同色(#007a85)而显得「有效」。
        var c = new PropLevelBrushConverter();
        var lv1 = Assert.IsType<SolidColorBrush>(c.Convert(1, typeof(IBrush), null, CultureInfo.InvariantCulture)).Color;
        var lv0 = Assert.IsType<SolidColorBrush>(c.Convert(0, typeof(IBrush), null, CultureInfo.InvariantCulture)).Color;
        Assert.Equal(lv0, lv1);
    }

    [Fact]
    public void PropLevel_Only_Level3_Is_Valid_Others_Gray()
    {
        // 2026-10 规则调整(用户要求):只有 level3(暴击/暴击伤害/攻击百分比)算有效词条,
        // level2/1/0 一律灰 —— 此前 level2 用青色,使「共鸣效率/伤害加成」看起来同样有效。
        var c = new PropLevelBrushConverter();
        var gray = Assert.IsType<SolidColorBrush>(c.Convert(0, typeof(IBrush), null, CultureInfo.InvariantCulture)).Color;
        var lv1 = Assert.IsType<SolidColorBrush>(c.Convert(1, typeof(IBrush), null, CultureInfo.InvariantCulture)).Color;
        var lv2 = Assert.IsType<SolidColorBrush>(c.Convert(2, typeof(IBrush), null, CultureInfo.InvariantCulture)).Color;
        var lv3 = Assert.IsType<SolidColorBrush>(c.Convert(3, typeof(IBrush), null, CultureInfo.InvariantCulture)).Color;
        // level2/1 与 level0 同为灰(无效)
        Assert.Equal(gray, lv1);
        Assert.Equal(gray, lv2);
        // level3 = 有效,必须明显区别于灰
        Assert.NotEqual(gray, lv3);
    }

    [Fact]
    public void PropText_1_Is_Gray_Like_Invalid()
    {
        var c = new PropTextBrushConverter();
        var lv1 = Assert.IsType<SolidColorBrush>(c.Convert(1, typeof(IBrush), null, CultureInfo.InvariantCulture)).Color;
        var lv0 = Assert.IsType<SolidColorBrush>(c.Convert(0, typeof(IBrush), null, CultureInfo.InvariantCulture)).Color;
        Assert.Equal(lv0, lv1);
    }

    [Fact]
    public void PropText_Matches_PropLevel_Bar_For_Valid_Levels()
    {
        // 有效词条(仅 level3)装饰条与文字必须完全同色,避免"条一个色、字另一个色"
        var bars = new PropLevelBrushConverter();
        var texts = new PropTextBrushConverter();
        foreach (var level in new[] { 3 })
        {
            var bar = Assert.IsType<SolidColorBrush>(bars.Convert(level, typeof(IBrush), null, CultureInfo.InvariantCulture));
            var text = Assert.IsType<SolidColorBrush>(texts.Convert(level, typeof(IBrush), null, CultureInfo.InvariantCulture));
            Assert.Equal(bar.Color, text.Color);
        }
    }

    [Fact]
    public void PropText_And_Bar_Are_Both_Gray_For_Invalid_Levels()
    {
        // 无效词条(level0/1/2):装饰条与文字各自取灰(明度略有差异以便文字可读),
        // 但都必须是「灰」——即三通道相近、且明显区别于有效词条的亮色。
        var bars = new PropLevelBrushConverter();
        var texts = new PropTextBrushConverter();
        foreach (var level in new[] { 0, 1, 2 })
        {
            var bar = Assert.IsType<SolidColorBrush>(bars.Convert(level, typeof(IBrush), null, CultureInfo.InvariantCulture)).Color;
            var text = Assert.IsType<SolidColorBrush>(texts.Convert(level, typeof(IBrush), null, CultureInfo.InvariantCulture)).Color;
            foreach (var c in new[] { bar, text })
            {
                Assert.True(Math.Max(Math.Max(c.R, c.G), c.B) - Math.Min(Math.Min(c.R, c.G), c.B) <= 8,
                    $"invalid level {level} should be neutral gray, got {c}");
            }
        }
        // 同档位下条与字一致(level0/1/2 走同一个兜底分支)
        var b1 = Assert.IsType<SolidColorBrush>(bars.Convert(1, typeof(IBrush), null, CultureInfo.InvariantCulture)).Color;
        var b2 = Assert.IsType<SolidColorBrush>(bars.Convert(2, typeof(IBrush), null, CultureInfo.InvariantCulture)).Color;
        var b0 = Assert.IsType<SolidColorBrush>(bars.Convert(0, typeof(IBrush), null, CultureInfo.InvariantCulture)).Color;
        var t1 = Assert.IsType<SolidColorBrush>(texts.Convert(1, typeof(IBrush), null, CultureInfo.InvariantCulture)).Color;
        var t2 = Assert.IsType<SolidColorBrush>(texts.Convert(2, typeof(IBrush), null, CultureInfo.InvariantCulture)).Color;
        var t0 = Assert.IsType<SolidColorBrush>(texts.Convert(0, typeof(IBrush), null, CultureInfo.InvariantCulture)).Color;
        Assert.Equal(b0, b1);
        Assert.Equal(b0, b2);
        Assert.Equal(t0, t1);
        Assert.Equal(t0, t2);
    }
}
