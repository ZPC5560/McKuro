using McKuro.Services;

namespace McKuro.Tests;

/// <summary>内嵌 WebView 主框架导航白名单:只允许停留在初始页面所属的来源上。</summary>
public class WebViewNavigationPolicyTests
{
    private const string Geetest = "http://127.0.0.1:52311/verify?cb=http%3A%2F%2F127.0.0.1%3A52311%2Fcb%2Fabc";
    private const string Bili = "https://player.bilibili.com/player.html?bvid=BV1xx&autoplay=1";
    private const string Youtube = "https://www.youtube-nocookie.com/embed/abc?autoplay=1";

    [Theory]
    [InlineData(Geetest, "http://127.0.0.1:52311/verify?cb=x")]
    [InlineData(Geetest, "http://127.0.0.1:52311/cb/abc?data=%7B%7D")]
    [InlineData(Bili, "https://player.bilibili.com/player.html?bvid=BV2yy")]
    [InlineData(Youtube, "https://www.youtube-nocookie.com/embed/def")]
    [InlineData(Youtube, "https://youtube-nocookie.com/embed/def")]
    [InlineData(Bili, "about:blank")]
    public void Allows_Same_Origin_And_Player_Hosts(string initial, string target)
        => Assert.True(WebViewNavigationPolicy.IsAllowed(initial, target));

    [Theory]
    [InlineData(Geetest, "http://127.0.0.1:52312/cb/abc")]      // 其他本地端口
    [InlineData(Geetest, "https://static.geetest.com/v4/gt4.js")] // 子资源 CDN 不是主框架目标
    [InlineData(Geetest, "http://localhost:52311/verify")]       // 换主机名
    [InlineData(Bili, "https://www.bilibili.com/video/BV1xx")]   // 播放器里的跳转链接
    [InlineData(Bili, "http://player.bilibili.com/player.html")]  // 降级到 http
    [InlineData(Youtube, "https://www.youtube.com/watch?v=abc")]
    [InlineData(Youtube, "https://evil.example/login")]
    [InlineData(Bili, "file:///C:/Windows/win.ini")]
    [InlineData(Bili, "javascript:alert(1)")]
    [InlineData("", "https://player.bilibili.com/player.html")]
    [InlineData(Bili, null)]
    public void Blocks_Everything_Else(string initial, string? target)
        => Assert.False(WebViewNavigationPolicy.IsAllowed(initial, target));
}
