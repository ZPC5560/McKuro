using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using McKuro.Controls;

namespace McKuro.Views;

/// <summary>
/// 应用内极验滑块验证窗口:内嵌平台 WebView(macOS=WKWebView,Windows=WebView2)加载
/// GeetVerifyService 的本地验证页,用户在窗口内完成滑块,结果经本地 HTTP 回调返回
/// (不再跳转系统浏览器)。对齐 Java 版鸣潮助手 GeetestCaptchaDialog 的应用内模态弹窗方案。
/// 平台不支持内置 WebView 时由调用方回退系统浏览器,不会创建本窗口;
/// WebView 创建失败(如未装 WebView2 Runtime)时自动关窗触发取消。
/// </summary>
public partial class GeetestWindow : Window
{
    private GeetestWindow()
    {
        InitializeComponent();
    }

    /// <summary>当前平台是否支持应用内验证窗口。</summary>
    public static bool IsPlatformSupported => WkWebViewControl.IsSupported || WebView2Control.IsSupported;

    /// <param name="verifyPageUrl">GeetVerifyService 本地服务的验证页地址(/verify?cb=...)。</param>
    public GeetestWindow(string verifyPageUrl) : this()
    {
        if (WkWebViewControl.IsSupported)
        {
            var wk = new WkWebViewControl { Url = verifyPageUrl };
            Content = wk;
        }
        else if (WebView2Control.IsSupported)
        {
            var wv2 = new WebView2Control { Url = verifyPageUrl };
            // WebView2 环境创建失败(运行时缺失等):关窗走取消路径,调用方提示后回退外部浏览器
            wv2.CreationFailed += () => Avalonia.Threading.Dispatcher.UIThread.Post(Close);
            Content = wv2;
        }
        else
        {
            // 不应发生(调用方已用 IsPlatformSupported 过滤);兜底立即关窗避免空白窗口
            Avalonia.Threading.Dispatcher.UIThread.Post(Close);
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
