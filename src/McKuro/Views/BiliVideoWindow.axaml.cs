using Avalonia.Controls;
using Avalonia.Threading;
using McKuro.Controls;

namespace McKuro.Views;

/// <summary>
/// 应用内B站视频播放窗口:内嵌平台 WebView(macOS=WKWebView,Windows=WebView2)加载
/// B站官方播放器嵌入页(player.bilibili.com/player.html?bvid=...),自动播放、免浏览器跳转加载。
/// 平台不支持内置 WebView 时由调用方回退系统浏览器,不会创建本窗口。
/// </summary>
public partial class BiliVideoWindow : Window
{
    public BiliVideoWindow()
    {
        InitializeComponent();
    }

    /// <summary>当前平台是否支持应用内播放窗口。</summary>
    public static bool IsPlatformSupported => WkWebViewControl.IsSupported || WebView2Control.IsSupported;

    /// <param name="bvid">B 站视频 BV 号(已由 BiliVideoHelper 解析)。</param>
    public BiliVideoWindow(string bvid) : this()
    {
        var playerUrl = $"https://player.bilibili.com/player.html?bvid={Uri.EscapeDataString(bvid)}&autoplay=1&danmaku=0&high_quality=1";
        if (WkWebViewControl.IsSupported)
        {
            // macOS WKWebView 无渲染开始信号:不显示加载遮罩
            LoadingOverlay.IsVisible = false;
            WebViewHost.Content = new WkWebViewControl { Url = playerUrl };
        }
        else if (WebView2Control.IsSupported)
        {
            var wv2 = new WebView2Control { Url = playerUrl };
            // 页面开始加载后撤掉遮罩(避免盖住播放器)
            wv2.PageLoadCompleted += () => Dispatcher.UIThread.Post(() => LoadingOverlay.IsVisible = false);
            // 关窗前在原生线程上回收 WebView2(与 GeetestWindow 相同的释放顺序)
            Closing += (_, _) => (WebViewHost.Content as WebView2Control)?.CloseWebView();
            WebViewHost.Content = wv2;
        }
        else
        {
            // 不应发生(调用方已用 IsPlatformSupported 过滤);兜底立即关窗避免空白窗口
            Dispatcher.UIThread.Post(Close);
        }
    }
}
