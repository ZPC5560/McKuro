using McKuro.Core.Services.Game;

namespace McKuro.Tests;

/// <summary>
/// 版本比较语义测试:对齐 Haiyu GetGameContextStatusAsync 的 localV &lt; serverV 数值比较。
/// 修复"本地是新版本仍提示更新"问题(字符串 != 对 "2.2.0" vs "2.2.0.0" 误判)。
/// </summary>
public class UpdateVersionComparisonTests
{
    /// <summary>同版本不同格式(如 2.2.0 vs 2.2.0.0)不应提示更新。</summary>
    [Theory]
    [InlineData("2.2.0", "2.2.0")]
    [InlineData("2.2.0", "2.2.0.0")]
    [InlineData("2.2.0.0", "2.2.0")]
    public void Same_Version_Different_Format_No_Update(string installed, string server)
    {
        Assert.False(GameUpdater.IsVersionOlder(installed, server));
    }

    /// <summary>本地缺段按 0 处理:2.2.0 实际低于 2.2.0.1,应提示更新。</summary>
    [Fact]
    public void Missing_Component_Treated_As_Zero()
    {
        Assert.True(GameUpdater.IsVersionOlder("2.2.0", "2.2.0.1"));
    }

    /// <summary>本地版本高于服务端(如手动更新超前)不应提示更新。</summary>
    [Theory]
    [InlineData("2.3.0", "2.2.0")]
    [InlineData("2.2.1", "2.2.0")]
    [InlineData("10.0.0", "2.2.0")]
    public void Newer_Local_Version_No_Update(string installed, string server)
    {
        Assert.False(GameUpdater.IsVersionOlder(installed, server));
    }

    /// <summary>本地版本低于服务端应提示更新。</summary>
    [Theory]
    [InlineData("2.1.0", "2.2.0")]
    [InlineData("2.2.0", "2.3.0.0")]
    [InlineData("1.0.0", "2.0.0")]
    public void Older_Local_Version_Has_Update(string installed, string server)
    {
        Assert.True(GameUpdater.IsVersionOlder(installed, server));
    }

    /// <summary>无本地版本记录时保守提示更新。</summary>
    [Fact]
    public void No_Local_Version_Records_Has_Update()
    {
        Assert.True(GameUpdater.IsVersionOlder(null, "2.2.0"));
        Assert.True(GameUpdater.IsVersionOlder("", "2.2.0"));
        Assert.True(GameUpdater.IsVersionOlder("   ", "2.2.0"));
    }

    /// <summary>解析失败(带尾缀)时回退字符串比较。</summary>
    [Theory]
    [InlineData("2.2.0-beta", "2.2.0-beta", false)]
    [InlineData("2.2.0-beta", "2.2.0", true)]
    [InlineData("v2.2.0", "v2.2.0", false)]
    [InlineData("v2.2.0", "v2.2.1", true)]
    public void Unparseable_Falls_Back_To_String_Compare(string installed, string server, bool expected)
    {
        Assert.Equal(expected, GameUpdater.IsVersionOlder(installed, server));
    }
}
