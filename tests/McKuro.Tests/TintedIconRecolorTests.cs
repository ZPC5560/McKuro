using Avalonia.Media;
using McKuro.Controls;

namespace McKuro.Tests;

/// <summary>
/// 图标 CPU 重着色单测(纯函数,无需渲染平台)。
/// <para>
/// 回归背景:攻略站共鸣链/技能图标是<b>白色线稿</b>(RGB 全 255,图形完全由 alpha 构成),
/// 在浅色主题上不可见。旧的 OpacityMask 方案实测不生效,改为 CPU 像素重着色。
/// 这里锁死两条最关键的不变量:<b>RGB 被整体替换</b>、<b>alpha 原样保留</b>
/// (若把 alpha 抹成 255,图标会退化成"一整块实心方块")。
/// </para>
/// </summary>
public class TintedIconRecolorTests
{
    /// <summary>目标色 <c>#1F2430</c> 的 BGRA8888 字节序(R=1F / G=24 / B=30)。</summary>
    private static readonly Color DarkTint = Color.Parse("#1F2430");

    /// <summary>白色线稿:RGB 全 255,图形由 alpha 构成。</summary>
    private static readonly Color WhiteLineArtRgb = Color.FromRgb(0xFF, 0xFF, 0xFF);

    [Fact]
    public void RecolorBgra_Replaces_Rgb_And_Preserves_Alpha()
    {
        // 2x2:覆盖全透明 / 半透明 / 不透明 / 低 alpha 异常色
        var pixels = new byte[]
        {
            0xFF, 0xFF, 0xFF, 0x00,
            0xFF, 0xFF, 0xFF, 0x7F,
            0xFF, 0xFF, 0xFF, 0xFF,
            0x00, 0x11, 0x22, 0x33,
        };

        TintedAsyncImage.RecolorBgra(pixels, 2, 2, 8, DarkTint);

        var expected = new byte[]
        {
            0x30, 0x24, 0x1F, 0x00,
            0x30, 0x24, 0x1F, 0x7F,
            0x30, 0x24, 0x1F, 0xFF,
            0x30, 0x24, 0x1F, 0x33,
        };
        Assert.Equal(expected, pixels);
    }

    [Fact]
    public void RecolorBgra_Fully_Transparent_Pixel_Keeps_Zero_Alpha()
    {
        var pixels = new byte[] { 0xFF, 0xFF, 0xFF, 0x00 };

        TintedAsyncImage.RecolorBgra(pixels, 1, 1, 4, DarkTint);

        // RGB 被替换,但 alpha 仍为 0 → 视觉上依然完全不可见
        Assert.Equal(0x30, pixels[0]);
        Assert.Equal(0x24, pixels[1]);
        Assert.Equal(0x1F, pixels[2]);
        Assert.Equal(0x00, pixels[3]);
    }

    [Fact]
    public void RecolorBgra_Tint_Alpha_Is_Ignored_Source_Alpha_Wins()
    {
        // tint 自带半透明 alpha(0x80):输出 alpha 必须仍等于输入像素的 alpha
        var tint = Color.FromArgb(0x80, 0x1F, 0x24, 0x30);
        var pixels = new byte[] { 0x00, 0x00, 0x00, 0x11 };

        TintedAsyncImage.RecolorBgra(pixels, 1, 1, 4, tint);

        Assert.Equal(0x11, pixels[3]);
        Assert.Equal(0x30, pixels[0]);
        Assert.Equal(0x24, pixels[1]);
        Assert.Equal(0x1F, pixels[2]);
    }

    [Fact]
    public void RecolorBgra_Honors_Stride_And_Leaves_Padding_Untouched()
    {
        // width=2 → 紧凑行 8 字节;stride=12 → 每行有 4 字节 padding
        const int width = 2;
        const int height = 2;
        const int stride = 12;
        const byte pad = 0xAB;

        var pixels = new byte[stride * height];
        Array.Fill(pixels, pad);
        // 第 0 行
        WritePixel(pixels, 0, 0x01, 0x02, 0x03, 0x40);
        WritePixel(pixels, 4, 0x04, 0x05, 0x06, 0x80);
        // 第 1 行(偏移必须落在 stride 上,而不是 width*4=8)
        WritePixel(pixels, stride, 0x07, 0x08, 0x09, 0xC0);
        WritePixel(pixels, stride + 4, 0x0A, 0x0B, 0x0C, 0xFF);

        TintedAsyncImage.RecolorBgra(pixels, width, height, stride, DarkTint);

        // 第 0 行像素
        AssertPixel(pixels, 0, 0x30, 0x24, 0x1F, 0x40);
        AssertPixel(pixels, 4, 0x30, 0x24, 0x1F, 0x80);
        // 第 1 行像素(写在 stride 偏移处,证明不是按 width*4 推进)
        AssertPixel(pixels, stride, 0x30, 0x24, 0x1F, 0xC0);
        AssertPixel(pixels, stride + 4, 0x30, 0x24, 0x1F, 0xFF);

        // padding 一个字节都不能被碰
        for (var x = 0; x < 4; x++)
        {
            Assert.Equal(pad, pixels[8 + x]);
            Assert.Equal(pad, pixels[stride + 8 + x]);
        }
    }

    [Fact]
    public void RecolorBgra_Stride_Not_Multiple_Of_Row_Is_Still_Correct()
    {
        // stride 有对齐填充但行首仍按 stride 推进(含奇数 stride:9)
        const int width = 2;
        const int height = 2;
        const int stride = 9;
        var pixels = new byte[stride * height];

        WritePixel(pixels, 0, 0x01, 0x01, 0x01, 0x10);
        WritePixel(pixels, 4, 0x02, 0x02, 0x02, 0x20);
        WritePixel(pixels, stride, 0x03, 0x03, 0x03, 0x30);
        WritePixel(pixels, stride + 4, 0x04, 0x04, 0x04, 0x40);

        TintedAsyncImage.RecolorBgra(pixels, width, height, stride, DarkTint);

        AssertPixel(pixels, 0, 0x30, 0x24, 0x1F, 0x10);
        AssertPixel(pixels, 4, 0x30, 0x24, 0x1F, 0x20);
        AssertPixel(pixels, stride, 0x30, 0x24, 0x1F, 0x30);
        AssertPixel(pixels, stride + 4, 0x30, 0x24, 0x1F, 0x40);
    }

    [Theory]
    [InlineData(0, 2, 8)]
    [InlineData(-1, 2, 8)]
    [InlineData(2, 0, 8)]
    [InlineData(2, -5, 8)]
    public void RecolorBgra_NonPositive_Dimensions_Are_NoOp(int width, int height, int stride)
    {
        var pixels = new byte[64];
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = (byte)(i + 1);
        }
        var before = (byte[])pixels.Clone();

        TintedAsyncImage.RecolorBgra(pixels, width, height, stride, DarkTint);

        Assert.Equal(before, pixels);
    }

    [Fact]
    public void RecolorBgra_Does_Not_Throw_When_Buffer_Too_Small()
    {
        // 需要 2*2*4=16 字节,只给 12 → 必须静默返回,绝不越界写
        var pixels = new byte[12];
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = (byte)(0xF0 + i);
        }
        var before = (byte[])pixels.Clone();

        TintedAsyncImage.RecolorBgra(pixels, 2, 2, 8, DarkTint);

        Assert.Equal(before, pixels);
    }

    [Fact]
    public void RecolorBgra_Stride_Smaller_Than_Tight_Row_Is_NoOp()
    {
        // stride=4 < width*4=8:布局非法,不得按错误的行距乱写
        var pixels = new byte[32];
        Array.Fill(pixels, (byte)0x5A);
        var before = (byte[])pixels.Clone();

        TintedAsyncImage.RecolorBgra(pixels, 2, 2, 4, DarkTint);

        Assert.Equal(before, pixels);
    }

    [Fact]
    public void RecolorBgra_Preserves_LineArt_Shape_Encoded_In_Alpha()
    {
        // 白线稿 3x3:RGB 全 255,alpha 构成图形(0=空,255=笔画,128=抗锯齿边)
        byte[] alphas = [0x00, 0xFF, 0x00, 0xFF, 0xFF, 0xFF, 0x00, 0x80, 0x00];
        const int width = 3;
        const int height = 3;
        const int stride = width * 4;

        var pixels = new byte[stride * height];
        for (var i = 0; i < width * height; i++)
        {
            WritePixel(pixels, i * 4, WhiteLineArtRgb.R, WhiteLineArtRgb.G, WhiteLineArtRgb.B, alphas[i]);
        }

        TintedAsyncImage.RecolorBgra(pixels, width, height, stride, DarkTint);

        for (var i = 0; i < width * height; i++)
        {
            // 形状 = alpha 逐像素完全一致
            Assert.Equal(alphas[i], pixels[i * 4 + 3]);
            // 颜色 = 目标色(不再是白)
            Assert.Equal(0x30, pixels[i * 4 + 0]);
            Assert.Equal(0x24, pixels[i * 4 + 1]);
            Assert.Equal(0x1F, pixels[i * 4 + 2]);
        }

        // 明确表达"不能把 alpha 抹平":全透明像素仍是 0,不是 255
        Assert.Equal(0x00, pixels[3]);
        Assert.Equal(0x00, pixels[(2 * width + 0) * 4 + 3]);
    }

    [Fact]
    public void RecolorBgra_Empty_Span_Is_NoOp()
    {
        // 空缓冲 + 正尺寸:静默返回(不抛)
        var pixels = Array.Empty<byte>();
        TintedAsyncImage.RecolorBgra(pixels, 1, 1, 4, DarkTint);
        Assert.Empty(pixels);
    }

    // ---- ResolveTint:颜色与项目既有约定一致(ThemeIconBrushConverter) ----

    [Theory]
    [InlineData(false, 0x1F, 0x24, 0x30)] // 浅色主题 → 近黑
    [InlineData(true, 0xFF, 0xFF, 0xFF)]  // 暗色主题 → 纯白
    public void ResolveTint_ThemeForeground_Matches_Light_Dark_Convention(bool dark, byte r, byte g, byte b)
    {
        var color = TintedAsyncImage.ResolveTint(IconTintMode.ThemeForeground, dark);
        Assert.NotNull(color);
        Assert.Equal(r, color!.Value.R);
        Assert.Equal(g, color.Value.G);
        Assert.Equal(b, color.Value.B);
        Assert.Equal(0xFF, color.Value.A);
    }

    [Theory]
    [InlineData(false, 0x8A, 0x8F, 0x99)] // 浅色主题 → 次级灰
    [InlineData(true, 0x9A, 0xA3, 0xB2)]  // 暗色主题 → 次级灰
    public void ResolveTint_ThemeMuted_Matches_Light_Dark_Convention(bool dark, byte r, byte g, byte b)
    {
        var color = TintedAsyncImage.ResolveTint(IconTintMode.ThemeMuted, dark);
        Assert.NotNull(color);
        Assert.Equal(r, color!.Value.R);
        Assert.Equal(g, color.Value.G);
        Assert.Equal(b, color.Value.B);
        Assert.Equal(0xFF, color.Value.A);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResolveTint_None_Returns_Null(bool dark)
        => Assert.Null(TintedAsyncImage.ResolveTint(IconTintMode.None, dark));

    // ---- helpers ----

    private static void WritePixel(byte[] buffer, int offset, byte r, byte g, byte b, byte a)
    {
        buffer[offset + 0] = b;
        buffer[offset + 1] = g;
        buffer[offset + 2] = r;
        buffer[offset + 3] = a;
    }

    private static void AssertPixel(byte[] buffer, int offset, byte b, byte g, byte r, byte a)
    {
        Assert.Equal(b, buffer[offset + 0]);
        Assert.Equal(g, buffer[offset + 1]);
        Assert.Equal(r, buffer[offset + 2]);
        Assert.Equal(a, buffer[offset + 3]);
    }
}
