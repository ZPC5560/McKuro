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
    private string? _biliEmbedBvid;

    /// <summary>当前注入的 WebView2 实例(换视频项/离开时需 CloseWebView 回收)。</summary>
    private WebView2Control? _biliWebView2;

    /// <summary>B站播放器静音注入定时器(页面加载有先后,分多次注入)。</summary>
    private DispatcherTimer? _biliMuteTimer;

    /// <summary>静音脚本:幂等注入,持续把页面内 video/audio 静音(B站播放器音量控件不反操作时保持无声)。</summary>
    private const string BiliMuteScript =
        "(function(){if(window.__mckuroMuted)return;window.__mckuroMuted=1;" +
        "function mute(){var els=document.querySelectorAll('video,audio');for(var i=0;i<els.length;i++){" +
        "try{els[i].muted=true;els[i].volume=0;els[i].defaultMuted=true;}catch(e){}}}" +
        "mute();new MutationObserver(mute).observe(document.documentElement,{childList:true,subtree:true});" +
        "setInterval(mute,1000);})()";

    public WikiView()
    {
        InitializeComponent();

        // 轮播区高度跟随左列公告卡(公告卡图片加载/页签切换都会改变其自然高度)
        NoticeHost.SizeChanged += (_, _) => SyncBannerHeight();

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

    /// <summary>轮播区高度 = 左列公告卡自然高度(下限 220 防加载期塌陷);公告卡加载完图片后升高,此处跟随。</summary>
    private void SyncBannerHeight()
    {
        var h = NoticeHost.Bounds.Height;
        BannerHost.Height = h > 220 ? h : 220;
    }

    /// <summary>按当前轮播项维护视频覆盖层:直接视频交给 XAML 控件;B站视频注入平台 WebView。</summary>
    private void UpdateVideoOverlay()
    {
        var vm = DataContext as WikiViewModel;
        var bvid = vm?.CurrentBannerIsBili == true ? vm.CurrentBanner?.Bvid : null;

        // 离开B站视频项(或 BV 号变化):回收旧 WebView,停止静音注入
        if (bvid is null || bvid != _biliEmbedBvid)
        {
            _biliMuteTimer?.Stop();
            _biliMuteTimer = null;
            (_biliEmbedBvid is null ? null : BiliEmbedHost.Content as WebView2Control)?.CloseWebView();
            BiliEmbedHost.Content = null;
            _biliWebView2 = null;
        }
        if (bvid is not null && bvid != _biliEmbedBvid)
        {
            var playerUrl = $"https://player.bilibili.com/player.html?bvid={Uri.EscapeDataString(bvid)}&autoplay=1&danmaku=0&mute=1";
            if (WkWebViewControl.IsSupported)
            {
                BiliEmbedHost.Content = new WkWebViewControl { Url = playerUrl };
            }
            else if (WebView2Control.IsSupported)
            {
                _biliWebView2 = new WebView2Control { Url = playerUrl };
                BiliEmbedHost.Content = _biliWebView2;
            }
            // B站播放器不支持可靠的 URL 静音参数:页面加载后分多次注入 JS 静音
            // (脚本自身 MutationObserver + 定时器持续保持静音,一次成功注入即长期有效)
            StartBiliMuteInjection();
        }
        _biliEmbedBvid = bvid;
    }

    /// <summary>启动B站播放器静音注入:2/5/9 秒三次(覆盖页面加载与播放器延迟创建 video 元素的时序)。</summary>
    private void StartBiliMuteInjection()
    {
        _biliMuteTimer?.Stop();
        var attempts = 0;
        _biliMuteTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _biliMuteTimer.Tick += (_, _) =>
        {
            attempts++;
            if (attempts >= 3)
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
