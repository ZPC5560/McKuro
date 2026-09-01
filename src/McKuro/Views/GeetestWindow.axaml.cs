using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Animation;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using McKuro.Controls;

namespace McKuro.Views;

/// <summary>
/// 应用内极验滑块验证窗口:内嵌平台 WebView(macOS=WKWebView,Windows=WebView2)加载
/// GeetVerifyService 的本地验证页,用户在窗口内完成滑块,结果经本地 HTTP 回调返回
/// (不再跳转系统浏览器)。对齐 Java 版鸣潮助手 GeetestCaptchaDialog 的应用内模态弹窗方案。
/// 平台不支持内置 WebView 时由调用方回退系统浏览器,不会创建本窗口;
/// WebView 创建失败(如未装 WebView2 Runtime)时自动关窗触发取消。
/// Windows 端 WebView2 冷启动(环境 + 浏览器子进程)期间显示加载遮罩动画,渲染开始后淡出。
/// </summary>
public partial class GeetestWindow : Window
{
    /// <summary>遮罩最短显示时长:避免渲染过快时动画一闪而过。</summary>
    private static readonly TimeSpan MinOverlayDuration = TimeSpan.FromMilliseconds(900);

    private readonly DateTime _openedAt = DateTime.Now;

    private DispatcherTimer? _spinnerTimer;
    private double _spinnerAngle;

    // 公共无参构造器为 XAML 运行时加载器要求(AVLN3001);外部调用方仍应使用 ShowAsync 工厂。
    public GeetestWindow()
    {
        InitializeComponent();
        ApplyTheme();
    }

    /// <summary>按应用主题配色(与极验页注入的主题一致,避免加载态与页面态闪色)。</summary>
    private void ApplyTheme()
    {
        var dark = McKuro.Services.GeetVerifyService.ResolveTheme() == "dark";
        var bg = Brush.Parse(dark ? "#FF10141A" : "#FFF8FAFC");
        Background = bg;
        LoadingOverlay.Background = bg;
        SpinnerGlyph.Foreground = Brush.Parse("#FF14B8C6");
        LoadingText.Foreground = Brush.Parse(dark ? "#FFC9D1D9" : "#FF334155");
        LoadingSub.Foreground = Brush.Parse(dark ? "#FF8B949E" : "#FF667085");
    }

    /// <summary>当前平台是否支持应用内验证窗口。</summary>
    public static bool IsPlatformSupported => WkWebViewControl.IsSupported || WebView2Control.IsSupported;

    /// <summary>内置 WebView 不可用(Windows:浏览器子进程被安全软件拦截/运行时创建失败)。
    /// 调用方应关闭本窗口并自动回退系统浏览器。</summary>
    public event Action? CreationFailed;

    /// <param name="verifyPageUrl">GeetVerifyService 本地服务的验证页地址(/verify?cb=...)。</param>
    public GeetestWindow(string verifyPageUrl) : this()
    {
        if (WkWebViewControl.IsSupported)
        {
            // macOS WKWebView 无渲染开始信号:不显示加载遮罩
            LoadingOverlay.IsVisible = false;
            WebViewHost.Content = new WkWebViewControl { Url = verifyPageUrl };
        }
        else if (WebView2Control.IsSupported)
        {
            var wv2 = new WebView2Control { Url = verifyPageUrl };
            // WebView2 不可用(运行时缺失/渲染子窗口未出现):通知调用方回退,由其关窗
            wv2.CreationFailed += () => Dispatcher.UIThread.Post(() => CreationFailed?.Invoke());
            // 渲染子窗口出现(页面开始绘制):淡出加载遮罩
            wv2.PageLoadCompleted += FadeOutLoadingOverlay;
            // 关窗前(OnClosing,HWND 尚未销毁)在原生线程上回收 WebView2:
            // 等 HWND 销毁后再释放,WebView2 已随窗口自行释放对象,指针悬空会 AccessViolation
            Closing += (_, _) => (WebViewHost.Content as WebView2Control)?.CloseWebView();
            WebViewHost.Content = wv2;
            StartSpinner();
        }
        else
        {
            // 不应发生(调用方已用 IsPlatformSupported 过滤);兜底立即关窗避免空白窗口
            Dispatcher.UIThread.Post(Close);
        }
    }

    /// <summary>启动加载环旋转动画(30ms 步进,遮罩隐藏时停止)。</summary>
    private void StartSpinner()
    {
        _spinnerTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        _spinnerTimer.Tick += (_, _) =>
        {
            _spinnerAngle = (_spinnerAngle + 30) % 360;
            (SpinnerGlyph.RenderTransform as RotateTransform)!.Angle = _spinnerAngle;
        };
        _spinnerTimer.Start();
    }

    private void FadeOutLoadingOverlay()
    {
        Dispatcher.UIThread.Post(() =>
        {
            var remaining = MinOverlayDuration - (DateTime.Now - _openedAt);
            _ = Task.Delay(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero)
                .ContinueWith(_ => Dispatcher.UIThread.Post(FadeAndHide));
        });
    }

    private void FadeAndHide()
    {
        _spinnerTimer?.Stop();
        _spinnerTimer = null;
        LoadingOverlay.Transitions = new Transitions
        {
            new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(280) },
        };
        LoadingOverlay.Opacity = 0;
        _ = Task.Delay(320).ContinueWith(_ => Dispatcher.UIThread.Post(() => LoadingOverlay.IsVisible = false));
    }
}
