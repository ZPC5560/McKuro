using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using McKuro.Core.Models.Game;
using McKuro.ViewModels;

namespace McKuro.Views;

public partial class LauncherView : UserControl
{
    private readonly DispatcherTimer _slideTimer;

    public LauncherView()
    {
        InitializeComponent();

        // 封面轮播自动切换(每 6 秒)
        _slideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        _slideTimer.Tick += (_, _) =>
        {
            if (DataContext is LauncherViewModel vm && vm.Slideshows.Count > 1)
            {
                SlideCarousel.SelectedIndex = (SlideCarousel.SelectedIndex + 1) % vm.Slideshows.Count;
            }
        };
        _slideTimer.Start();

        DataContextChanged += (_, _) => AttachConfirmation();

        Unloaded += (_, _) => _slideTimer.Stop();
    }

    /// <summary>
    /// 双击轮播图 → 打开当前这张图的跳转链接。
    /// 用 Carousel.SelectedItem 而不是事件源上的 DataContext:双击可能落在轮播内部的
    /// 图片控件上,取其 DataContext 更直接可靠。
    /// </summary>
    private void OnSlideshowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not LauncherViewModel vm)
        {
            return;
        }

        var item = SlideCarousel.SelectedItem as SlideshowItem;
        if (item is null && SlideCarousel.SelectedIndex >= 0 && SlideCarousel.SelectedIndex < vm.Slideshows.Count)
        {
            item = vm.Slideshows[SlideCarousel.SelectedIndex];
        }

        vm.OpenSlideshowLinkCommand.Execute(item);
    }

    /// <summary>
    /// 注入资源等级切换的二次确认弹窗:切换未安装的等级会下载几十 GB,
    /// 必须由用户在模态窗口里明确确认(防误点)。未注入时 ViewModel 会拒绝切换。
    /// </summary>
    private void AttachConfirmation()
    {
        if (DataContext is LauncherViewModel vm)
        {
            vm.ConfirmLevelSwitchAsync = item =>
            {
                var owner = TopLevel.GetTopLevel(this) as Window;
                // 用纯体积文案("99.45 GB"),避免把卡片的"游戏大小:"前缀带进句子
                var size = string.IsNullOrWhiteSpace(item.SizeOnlyText) ? item.SizeText : item.SizeOnlyText;
                return ResourceLevelConfirmWindow.ShowAsync(owner, item.DisplayName, size);
            };
        }
    }
}
