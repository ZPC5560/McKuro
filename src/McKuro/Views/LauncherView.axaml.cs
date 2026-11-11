using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using McKuro.Core.Models.Game;
using McKuro.Services;
using McKuro.ViewModels;

namespace McKuro.Views;

public partial class LauncherView : UserControl
{
    private readonly DispatcherTimer _slideTimer;

    /// <summary>
    /// 本 View 注入到 <see cref="LauncherViewModel"/> 的确认委托(用于精确解绑)。
    /// <para>
    /// <b>为什么必须解绑</b>:<see cref="LauncherViewModel"/> 是主窗口构造时创建的单例,
    /// 而本 View 随每次导航重建。委托体捕获了 <c>this</c>(要拿 TopLevel 当弹窗 owner),
    /// 单例 VM 一旦持有它,本 View 及其整棵视觉树就被钉住,永远等不到 GC ——
    /// 表现为「切页内存不断累加」(每进一次启动页泄漏一个 View)。
    /// </para>
    /// </summary>
    private Func<ResourceLevelItem, Task<bool>>? _injectedLevelSwitch;

    private Func<ResourcePackItem, Task<bool>>? _injectedPackDelete;

    /// <summary>本 View 注入的「分级预下载规格选择」委托(同样需要精确解绑,理由同上)。</summary>
    private Func<PredownloadInfo, Task<PredownloadSpec?>>? _injectedPredownloadSpec;

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
        // 不在这里 Start():计时器的启停跟着「进入/离开视觉树」走(见下方 AttachedToVisualTree),
        // 否则 View 被复用时(切走→切回同一实例)计时器会永远停着。

        DataContextChanged += (_, _) => AttachConfirmation();

        // 生命周期严格对称:进入视觉树 → 启动计时器 + 注入委托;离开 → 停表 + 解绑。
        // <para/>
        // <b>为什么启停要跟视觉树走</b>:切页时 ContentPresenter 会新建一个 View 实例替换旧的
        // (Avalonia 的 DataTemplate 不参与回收复用),旧实例 detach 时停表。计时器的启停
        // 因此必须挂在 attach/detach 上成对出现 —— 只在构造函数 Start 的话,
        // 实例从构造到挂载之间的空档里表是停的,而 detach 后再无人在这个实例上 Start。
        // </para>
        AttachedToVisualTree += (_, _) =>
        {
            _slideTimer.Start();
            AttachConfirmation();
        };

        // 离开视觉树(切走页面)时:停计时器 + 解绑注入到单例 VM 的委托。
        // Unloaded 在控件从视觉树移除时触发;DetachedFromVisualTree 更早且覆盖得更全
        // (ContentControl 换 Content 时旧 View 先 detach)。
        DetachedFromVisualTree += (_, _) =>
        {
            _slideTimer.Stop();
            DetachConfirmation();
        };
    }

    /// <summary>
    /// 取游戏所在盘的可用空间(字节);游戏目录未设置或读不到时返回 null。
    /// <para>
    /// 预下载会落在游戏目录的 <c>DiffData</c> 下,所以口径必须是<b>游戏盘</b>而不是系统盘 ——
    /// 这正是「可用空间」那一行要回答的问题。
    /// </para>
    /// </summary>
    private static long? ResolveGameDriveFreeBytes()
    {
        try
        {
            var root = AppServices.Settings.Current.GameRootDir;
            if (string.IsNullOrWhiteSpace(root))
            {
                return null;
            }
            var full = System.IO.Path.GetFullPath(root);
            var driveRoot = System.IO.Path.GetPathRoot(full);
            if (string.IsNullOrEmpty(driveRoot))
            {
                return null;
            }
            return new System.IO.DriveInfo(driveRoot).AvailableFreeSpace;
        }
        catch (Exception)
        {
            // 读不到可用空间不影响选择(该行显示 "-"),绝不能因此挡住预下载。
            return null;
        }
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
    /// 注入资源等级切换 / 画质包删除的二次确认弹窗:切换未安装的等级会下载几十 GB、
    /// 删除画质包不可逆,都必须由用户在模态窗口里明确确认(防误点)。
    /// 未注入时 ViewModel 会拒绝执行。
    /// </summary>
    private void AttachConfirmation()
    {
        // 重挂前先解旧的:DataContext 可能换 VM(理论上不会,VM 是单例),避免旧委托残留
        DetachConfirmation();

        if (DataContext is LauncherViewModel vm)
        {
            _injectedLevelSwitch = item =>
            {
                var owner = TopLevel.GetTopLevel(this) as Window;
                // 弹窗里说「需要下载 X」,必须用**实际还需下载**的量。
                // 用卡片上的"游戏大小"会多报已装好的共享包(实测已装 HD 切 UHD:99.45 vs 61.50 GB),
                // 用户会以为要重下整个画质包而放弃切换。
                var size = !string.IsNullOrWhiteSpace(item.DownloadSizeOnlyText)
                    ? item.DownloadSizeOnlyText
                    : string.IsNullOrWhiteSpace(item.SizeOnlyText) ? item.SizeText : item.SizeOnlyText;
                return ResourceLevelConfirmWindow.ShowAsync(owner, item.DisplayName, size);
            };
            vm.ConfirmLevelSwitchAsync = _injectedLevelSwitch;

            // 删除画质包复用同一个确认窗口,但标题/正文/确认键都换成"删除"措辞
            // (否则用户会误以为是在切换画质)。
            _injectedPackDelete = item =>
            {
                var owner = TopLevel.GetTopLevel(this) as Window;
                return ResourceLevelConfirmWindow.ShowAsync(
                    owner,
                    LanguageService.Format("Launcher.PackDeleteConfirmTitle"),
                    LanguageService.Format("Launcher.PackDeleteConfirmBody", item.DisplayName, item.SizeText),
                    LanguageService.Format("Launcher.PackDeleteConfirmOk"),
                    LanguageService.Format("Launcher.LevelCancel"));
            };
            vm.ConfirmPackDeleteAsync = _injectedPackDelete;

            // 分级预下载:开始下载前弹「全量 / 精简」选择窗(对齐官方 3.8 的「预下载新版本」)。
            // 硬盘可用空间在这里现取(游戏盘口径),让用户点之前就能看到够不够。
            _injectedPredownloadSpec = info =>
            {
                var owner = TopLevel.GetTopLevel(this) as Window;
                var available = ResolveGameDriveFreeBytes();
                return PredownloadSpecWindow.ShowAsync(owner, info, available, vm.SelectedPredownloadSpec);
            };
            vm.ConfirmPredownloadSpecAsync = _injectedPredownloadSpec;
        }
    }

    /// <summary>
    /// 解绑本 View 注入到单例 VM 的确认委托。
    /// <para>
    /// 只在 <c>vm.ConfirmXxxAsync</c> <b>仍指向本 View 注入的那个委托实例</b> 时才置空 ——
    /// 若已被别的来源(理论上不会)覆盖,贸然置空会破坏别人的注入。
    /// </para>
    /// </summary>
    private void DetachConfirmation()
    {
        if (DataContext is LauncherViewModel vm)
        {
            if (_injectedPredownloadSpec is not null && ReferenceEquals(vm.ConfirmPredownloadSpecAsync, _injectedPredownloadSpec))
            {
                vm.ConfirmPredownloadSpecAsync = null;
            }

            if (_injectedLevelSwitch is not null && ReferenceEquals(vm.ConfirmLevelSwitchAsync, _injectedLevelSwitch))
            {
                vm.ConfirmLevelSwitchAsync = null;
            }
            if (_injectedPackDelete is not null && ReferenceEquals(vm.ConfirmPackDeleteAsync, _injectedPackDelete))
            {
                vm.ConfirmPackDeleteAsync = null;
            }
        }
        _injectedLevelSwitch = null;
        _injectedPackDelete = null;
    }
}
