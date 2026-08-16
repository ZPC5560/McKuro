using Avalonia.Platform;
using Xunit;

namespace McKuro.Tests;

/// <summary>
/// 验证窗口图标已作为 avares 资源嵌入(McKuro.dll),防止 Window.Icon 运行时找不到资源崩溃。
/// </summary>
public class AwaresIconTests
{
    [Fact]
    public void AppIcon_Is_Embedded_As_Avares_Resource()
    {
        // StandardAssetLoader 为独立实现,无需显示平台/全局注册
        var loader = new StandardAssetLoader();
        using var stream = loader.Open(new Uri("avares://McKuro/Assets/app.ico"), baseUri: null);
        Assert.NotNull(stream);
        Assert.True(stream.Length > 0, "图标资源不应为空");

        // ICO 文件头: ICONDIR (reserved=0, type=1, count>0)
        var header = new byte[6];
        stream.ReadExactly(header);
        Assert.Equal(0, header[0]);
        Assert.Equal(0, header[1]);
        Assert.Equal(1, header[2]);
        Assert.Equal(0, header[3]);
        var count = BitConverter.ToUInt16(header, 4);
        Assert.True(count >= 1, "ICO 至少应含 1 个尺寸");
    }

    [Fact]
    public void NavLogo_Is_Embedded_As_Avares_Resource()
    {
        // 导航栏顶部守岸人 Logo(参照 Java 项目 MainView 顶部 icon)
        var loader = new StandardAssetLoader();
        using var stream = loader.Open(new Uri("avares://McKuro/Assets/shorekeeper_icon.png"), baseUri: null);
        Assert.NotNull(stream);
        Assert.True(stream.Length > 0, "Logo 资源不应为空");

        // PNG 头: 89 50 4E 47
        var header = new byte[4];
        stream.ReadExactly(header);
        Assert.Equal(0x89, header[0]);
        Assert.Equal((byte)'P', header[1]);
        Assert.Equal((byte)'N', header[2]);
        Assert.Equal((byte)'G', header[3]);
    }
}
