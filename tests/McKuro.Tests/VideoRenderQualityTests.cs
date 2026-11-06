using System.Reflection;
using McKuro.Controls;

namespace McKuro.Tests;

/// <summary>
/// 背景视频解码档位的 mpv 参数契约(不加载 libmpv,只断言生成的选项值)。
/// <para>
/// 这两处出过实测问题:固定写 <c>scale=w=1920</c> 会把 480p 源放大到 1080p;
/// <c>min(iw,1920)</c> 里的逗号裸写会被 mpv 当成滤镜分隔符,整个滤镜被静默丢弃。
/// </para>
/// </summary>
public sealed class VideoRenderQualityTests
{
    private static readonly Type ControlType = typeof(VideoBackgroundControl);

    private static T Invoke<T>(string method, object arg) =>
        (T)(ControlType.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"VideoBackgroundControl 缺少 {method}"))
        .Invoke(null, [arg])!;

    private static string? ScaleFilterFor(int cap) => Invoke<string?>("ScaleFilterFor", cap);

    [Theory]
    [InlineData(1280)]
    [InlineData(1920)]
    [InlineData(3840)]
    public void ScaleFilter_Only_Downscales_And_Keeps_Expression_Quoted(int cap)
    {
        var vf = ScaleFilterFor(cap);
        Assert.NotNull(vf);
        // 只缩小不放大:宽取 min(iw,cap),而不是固定 cap
        Assert.Contains($"min(iw,{cap})", vf);
        // 逗号必须在 lavfi=[...] 方括号内,否则 mpv 会在逗号处拆滤镜链
        var open = vf!.IndexOf("lavfi=[", StringComparison.Ordinal);
        var comma = vf.IndexOf(',');
        var close = vf.IndexOf(']', open);
        Assert.True(open >= 0 && open < comma && comma < close, $"逗号不在方括号引用内: {vf}");
        // 滤镜图线程数受限(默认按核数开 swscale 线程)
        Assert.Contains("o=[threads=", vf);
    }

    [Fact]
    public void ScaleFilter_Is_Not_Set_For_Native()
    {
        Assert.Null(ScaleFilterFor(0));
        Assert.Equal(0, Invoke<int>("WidthCapFor", VideoRenderQuality.Native));
    }

    [Theory]
    [InlineData(VideoRenderQuality.Hd720, true)]
    [InlineData(VideoRenderQuality.Hd1080, true)]
    [InlineData(VideoRenderQuality.Uhd2160, false)]
    [InlineData(VideoRenderQuality.Native, false)]
    public void Fast_Render_Profile_Only_For_Low_Tiers(VideoRenderQuality quality, bool expected)
        => Assert.Equal(expected, Invoke<bool>("UsesFastRenderProfile", quality));
}
