namespace McKuro.Services;

/// <summary>
/// 内嵌 WebView(极验验证页 / B站、YouTube 嵌入播放器)的主框架导航白名单。
/// <para>
/// 嵌入页里的第三方链接或广告不能把内嵌 WebView 导航到任意站点(无地址栏的窗口适合做假登录钓鱼):
/// 主框架只允许停留在「初始页面所属的来源」上,其余导航取消;用户主动点击的 http/https 链接改用系统浏览器打开。
/// 只约束主框架:页面内脚本/图片/iframe 等子资源(极验 static.geetest.com、jsdelivr 等 CDN)不经过这里。
/// </para>
/// </summary>
public static class WebViewNavigationPolicy
{
    /// <summary>B站嵌入播放器主机(<c>WikiView</c> 的 player.html 地址)。</summary>
    private static readonly string[] BiliHosts = ["player.bilibili.com"];

    /// <summary>YouTube 嵌入播放器主机(<c>WikiView</c> 用 youtube-nocookie 的 /embed 地址)。</summary>
    private static readonly string[] YoutubeHosts = ["www.youtube-nocookie.com", "youtube-nocookie.com"];

    /// <summary>
    /// 主框架导航是否允许。
    /// <list type="bullet">
    /// <item>与初始页同源(协议 + 主机 + 端口)始终允许:极验本地服务 http://127.0.0.1:{port}/verify → /cb/{nonce}。</item>
    /// <item>初始页是 B站/YouTube 嵌入播放器:另允许同一播放器的其他主机名(https)。</item>
    /// <item>about:blank 始终允许(WebView 内部占位)。</item>
    /// </list>
    /// 其余一律拒绝(初始地址不可解析时也拒绝)。
    /// </summary>
    /// <param name="initialUrl">控件创建时要加载的页面地址。</param>
    /// <param name="targetUrl">本次主框架导航的目标地址。</param>
    public static bool IsAllowed(string? initialUrl, string? targetUrl)
    {
        if (string.Equals(targetUrl, "about:blank", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (!Uri.TryCreate(initialUrl, UriKind.Absolute, out var initial)
            || !Uri.TryCreate(targetUrl, UriKind.Absolute, out var target))
        {
            return false;
        }

        // 同源:极验本地服务 http://127.0.0.1:{port}/verify → http://127.0.0.1:{port}/cb/{nonce}
        if (string.Equals(target.Scheme, initial.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(target.Host, initial.Host, StringComparison.OrdinalIgnoreCase)
            && target.Port == initial.Port)
        {
            return true;
        }

        if (target.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }
        if (ContainsHost(BiliHosts, initial.Host))
        {
            return ContainsHost(BiliHosts, target.Host);
        }
        if (ContainsHost(YoutubeHosts, initial.Host))
        {
            return ContainsHost(YoutubeHosts, target.Host);
        }
        return false;
    }

    private static bool ContainsHost(string[] hosts, string host)
    {
        foreach (var h in hosts)
        {
            if (string.Equals(h, host, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}
