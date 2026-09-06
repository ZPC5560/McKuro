using McKuro.Core.Services.Launcher;

namespace McKuro.Tests;

/// <summary>B站视频链接识别与 BV 号提取(资讯页轮播链接跳应用内播放窗口用)。</summary>
public class BiliVideoHelperTests
{
    [Theory]
    [InlineData("https://www.bilibili.com/video/BV1xx411c7mD?p=1", true)]
    [InlineData("https://b23.tv/abcd123", true)]
    [InlineData("https://www.kurobbs.com/forum/12345", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsBiliVideoUrl_Detects_Bili_Links(string? url, bool expected)
    {
        Assert.Equal(expected, BiliVideoHelper.IsBiliVideoUrl(url));
    }

    [Theory]
    [InlineData("https://www.bilibili.com/video/BV1xx411c7mD/", "BV1xx411c7mD")]
    [InlineData("https://www.bilibili.com/video/BV1GJ411x7h7?p=1&share_source=copy", "BV1GJ411x7h7")]
    [InlineData("https://www.bilibili.com/video/av12345", null)]      // av 号不支持
    [InlineData("https://b23.tv/abcd123", null)]                      // 短链不含 BV 号,需解析
    [InlineData("", null)]
    public void TryExtractBvId_Extracts_Page_Links_Only(string url, string? expected)
    {
        Assert.Equal(expected, BiliVideoHelper.TryExtractBvId(url));
    }
}
