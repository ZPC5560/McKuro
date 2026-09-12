using Avalonia.Controls;
using Avalonia.Threading;
using McKuro.Controls;
using McKuro.ViewModels;

namespace McKuro.Views;

/// <summary>
/// 资讯页:B站视频的内嵌播放器实例由本代码后置按平台注入(WkWebView/WebView2),
/// 仅当前轮播项为B站视频时创建;直接视频文件由 XAML 中的 VideoBackgroundControl 播放。
/// 轮播区高度不按视频比例走,而是反向跟随左侧游戏公告卡的自然高度(左右对齐,
/// 下方内容随公告卡高度自动上移/下移)。
/// </summary>
public partial class WikiView : UserControl
{
    /// <summary>当前已注入的B站内嵌播放器对应的 BV 号(切换视频项时重建 WebView)。</summary>
    private string? _embedSessionKey;

    /// <summary>当前注入的 WebView2 实例(换视频项/离开时需 CloseWebView 回收)。</summary>
    private WebView2Control? _biliWebView2;

    /// <summary>B站播放器静音注入定时器(页面加载有先后,分多次注入)。</summary>
    private DispatcherTimer? _biliMuteTimer;

    /// <summary>当前直接视频的显示比例(w/h,分辨率解析后记录)。</summary>
    private double? _videoAspect;

    /// <summary>静音脚本:把当前页面内 video/audio 静音(幂等,单次执行,不持续干预);
    /// 由注入定时器分多次调用以覆盖播放器延迟创建 video 元素的时序,
    /// 注入结束后用户可通过播放器音量控制手动恢复声音。</summary>
    private const string BiliMuteScript =
        "(function(){var els=document.querySelectorAll('video,audio');" +
        "for(var i=0;i<els.length;i++){try{els[i].muted=true;els[i].volume=0;}catch(e){}}})()";

    public WikiView()
    {
        InitializeComponent();

        // 视频区按比例自适应:分辨率解析后高度 = 宽度/显示比例(剩余空间由官方资讯区填满)
        BannerVideo.VideoAspectRatioResolved += (w, h) =>
        {
            _videoAspect = h > 0 ? (double)w / h : null;
            UpdateBannerHeight();
        };
        BannerHost.SizeChanged += (_, _) => UpdateBannerHeight();

        DataContextChanged += (_, _) =>
        {
            if (DataContext is WikiViewModel vm)
            {
                vm.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName is nameof(WikiViewModel.CurrentBannerIsDirectVideo)
                        or nameof(WikiViewModel.CurrentBannerIsBili)
                        or nameof(WikiViewModel.CurrentBanner))
                    {
                        UpdateVideoOverlay();
                    }
                };
            }
            UpdateVideoOverlay();
        };
    }

    /// <summary>视频区高度:直接视频按显示比例、B站嵌入按 16:9、图片保持默认 220;
    /// 右列总高跟随左列(整体高度绑定),官方资讯区填满视频以下剩余空间。</summary>
    private void UpdateBannerHeight()
    {
        var vm = DataContext as WikiViewModel;
        double? aspect = vm?.CurrentBannerIsDirectVideo == true ? _videoAspect
            : vm?.CurrentBannerIsBili == true ? 16.0 / 9.0
            : null;
        if (aspect is null or <= 0 || BannerHost.Bounds.Width <= 0)
        {
            return;
        }
        BannerHost.Height = Math.Clamp(BannerHost.Bounds.Width / aspect.Value, 160, 640);
    }

    /// <summary>按当前轮播项维护视频覆盖层:直接视频交给 XAML 控件;B站/YouTube 视频注入平台 WebView。
    /// B站为中文轮播预告;YouTube 为国际服英文轮播预告(界面语言 en-US 的数据源)。</summary>
    private void UpdateVideoOverlay()
    {
        var vm = DataContext as WikiViewModel;
        var banner = vm?.CurrentBannerIsWebVideo == true ? vm.CurrentBanner : null;
        // 会话键:B站用 BV 号,YouTube 用视频 ID(前缀区分来源)
        string? sessionKey = banner switch
        {
            { IsYoutubeVideo: true } => "yt:" + banner.YoutubeId,
            { IsBiliVideo: true } => "bili:" + banner.Bvid,
            _ => null,
        };

        // 离开网页视频项(或会话键变化):回收旧 WebView,停止静音注入
        if (sessionKey is null || sessionKey != _embedSessionKey)
        {
            _biliMuteTimer?.Stop();
            _biliMuteTimer = null;
            (_embedSessionKey is null ? null : BiliEmbedHost.Content as WebView2Control)?.CloseWebView();
            BiliEmbedHost.Content = null;
            _biliWebView2 = null;
        }
        if (sessionKey is not null && sessionKey != _embedSessionKey)
        {
            var isYoutube = sessionKey.StartsWith("yt:", StringComparison.Ordinal);
            // B站多语言 PV(【中】【日】【英】【韩】分P):英文界面带 p= 参数播英文分P
            var pageParam = banner is { BvPage: > 0 } ? $"&p={banner.BvPage}" : "";
            var playerUrl = isYoutube
                ? $"https://www.youtube-nocookie.com/embed/{Uri.EscapeDataString(sessionKey[3..])}?autoplay=1&mute=1&loop=1&playlist={Uri.EscapeDataString(sessionKey[3..])}&rel=0"
                : $"https://player.bilibili.com/player.html?bvid={Uri.EscapeDataString(sessionKey[5..])}&autoplay=1&danmaku=0&mute=1{pageParam}";
            if (WkWebViewControl.IsSupported)
            {
                if (isYoutube)
                {
                    // YouTube embed 页被 WKWebView 顶层直接加载会报"配置错误 153"(缺来源上下文),
                    // 包一层 iframe 且 baseURL 指向 https 来源即可正常播放
                    BiliEmbedHost.Content = new WkWebViewControl
                    {
                        Html = BuildYoutubeIframeHtml(playerUrl),
                        HtmlBaseUrl = "https://example.com",
                    };
                }
                else
                {
                    BiliEmbedHost.Content = new WkWebViewControl { Url = playerUrl };
                }
            }
            else if (WebView2Control.IsSupported)
            {
                _biliWebView2 = new WebView2Control { Url = playerUrl };
                BiliEmbedHost.Content = _biliWebView2;
            }
            // YouTube 播放器原生支持 mute=1 URL 参数,无需 JS 注入;
            // B站播放器不支持可靠的 URL 静音参数:页面加载后分多次注入 JS 静音
            // (脚本自身 MutationObserver + 定时器持续保持静音,一次成功注入即长期有效)
            if (banner?.IsBiliVideo == true)
            {
                StartBiliMuteInjection();
            }
        }
        _embedSessionKey = sessionKey;
    }

    /// <summary>YouTube embed 的 iframe 包装页(满铺、无边距;autoplay+mute 参数由 embed URL 携带)。</summary>
    private static string BuildYoutubeIframeHtml(string embedUrl)
        => $$"""<!DOCTYPE html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><style>html,body{margin:0;padding:0;height:100%;background:#000;overflow:hidden}iframe{position:absolute;inset:0;width:100%;height:100%;border:0}</style></head><body><iframe src="{{embedUrl}}" allow="autoplay; encrypted-media; picture-in-picture" allowfullscreen></iframe></body></html>""";

    /// <summary>启动B站播放器静音注入:2/4/6/8/10 秒各一次,覆盖页面加载与播放器延迟创建 video 元素的时序;
    /// 注入窗口结束后不再干预 —— 用户可通过播放器自带音量控制手动恢复声音。</summary>
    private void StartBiliMuteInjection()
    {
        _biliMuteTimer?.Stop();
        var attempts = 0;
        _biliMuteTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _biliMuteTimer.Tick += (_, _) =>
        {
            attempts++;
            if (attempts >= 5)
            {
                _biliMuteTimer?.Stop();
            }
            switch (BiliEmbedHost.Content)
            {
                case WkWebViewControl wk:
                    wk.EvaluateJavaScript(BiliMuteScript);
                    break;
                case WebView2Control wv2:
                    wv2.EvaluateJavaScript(BiliMuteScript);
                    break;
            }
        };
        _biliMuteTimer.Start();
    }
}
