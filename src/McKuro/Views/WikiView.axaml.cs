using Avalonia.Controls;
using Avalonia.Threading;
using McKuro.Controls;
using McKuro.ViewModels;

namespace McKuro.Views;

/// <summary>
/// 资讯页:B站视频的内嵌播放器实例由本代码后置按平台注入(WkWebView/WebView2),
/// 仅当前轮播项为B站视频时创建;直接视频文件由 XAML 中的 VideoBackgroundControl 播放。
/// 视频分辨率解析后轮播控件按比例自适应高度(下方内容自动下移)。
/// </summary>
public partial class WikiView : UserControl
{
    /// <summary>当前视频宽高比(w/h,分辨率解析后记录;切走视频项时清空)。</summary>
    private double? _videoAspect;

    /// <summary>当前已注入的B站内嵌播放器对应的 BV 号(切换视频项时重建 WebView)。</summary>
    private string? _biliEmbedBvid;

    /// <summary>当前注入的 WebView2 实例(换视频项/离开时需 CloseWebView 回收)。</summary>
    private WebView2Control? _biliWebView2;

    public WikiView()
    {
        InitializeComponent();

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

    /// <summary>按当前轮播项维护视频覆盖层:直接视频交给 XAML 控件;B站视频注入平台 WebView。</summary>
    private void UpdateVideoOverlay()
    {
        var vm = DataContext as WikiViewModel;
        var bvid = vm?.CurrentBannerIsBili == true ? vm.CurrentBanner?.Bvid : null;

        // 离开B站视频项(或 BV 号变化):回收旧 WebView
        if (bvid is null || bvid != _biliEmbedBvid)
        {
            (_biliEmbedBvid is null ? null : BiliEmbedHost.Content as WebView2Control)?.CloseWebView();
            BiliEmbedHost.Content = null;
            _biliWebView2 = null;
        }
        if (bvid is not null && bvid != _biliEmbedBvid)
        {
            var playerUrl = $"https://player.bilibili.com/player.html?bvid={Uri.EscapeDataString(bvid)}&autoplay=1&danmaku=0";
            if (WkWebViewControl.IsSupported)
            {
                BiliEmbedHost.Content = new WkWebViewControl { Url = playerUrl };
            }
            else if (WebView2Control.IsSupported)
            {
                _biliWebView2 = new WebView2Control { Url = playerUrl };
                BiliEmbedHost.Content = _biliWebView2;
            }
        }
        _biliEmbedBvid = bvid;

        // 切走直接视频项时清空比例,恢复默认高度
        if (vm?.CurrentBannerIsDirectVideo != true)
        {
            _videoAspect = null;
        }
        UpdateBannerHeight();
    }

    /// <summary>
    /// 轮播控件高度自适应:视频模式按视频比例(宽度已知 → 高 = 宽/比例,钳制 180-560 防止极端比例撑爆页面),
    /// 其余恢复默认 220。高度变化时页面布局自动把下方内容向下让位。
    /// </summary>
    private void UpdateBannerHeight()
    {
        var vm = DataContext as WikiViewModel;
        var isDirect = vm?.CurrentBannerIsDirectVideo == true;
        var isBili = vm?.CurrentBannerIsBili == true;

        double? aspect = isDirect ? _videoAspect : isBili ? 16.0 / 9.0 : null;
        if (aspect is null || aspect <= 0)
        {
            BannerHost.Height = 220;
            return;
        }
        var width = BannerHost.Bounds.Width;
        if (width <= 0)
        {
            return;
        }
        BannerHost.Height = Math.Clamp(width / aspect.Value, 180, 560);
    }
}
