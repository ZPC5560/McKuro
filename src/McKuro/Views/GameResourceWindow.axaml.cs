using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using McKuro.Services;
using McKuro.ViewModels;

namespace McKuro.Views;

/// <summary>
/// 「游戏资源」弹窗(用户需求 2026-11):同心圆资源占用 + 设备信息。
/// <para>
/// <b>代码后置只做三件 UI 事</b>:页签切换、复制到剪贴板、打开目录。
/// 数据采集与分类口径全在 <see cref="GameResourceViewModel"/> 与 Core 服务里,
/// 这样分类逻辑能被单测钉住,而这里不承载任何"什么算正式资源"的判断。
/// </para>
/// <para>
/// 窗口关闭时必须取消扫描:游戏目录十几万文件,用户点开看一眼觉得慢就关掉是常态,
/// 不取消的话后台线程还在枚举磁盘(而且 <c>DataContext</c> 已经不会有人读)。
/// </para>
/// </summary>
public partial class GameResourceWindow : Window
{
    private CancellationTokenSource? _cts;
    private GameResourceViewModel? _vm;
    private bool _opened;

    /// <summary>XAML 加载器要求公共无参构造;业务入口请使用 <see cref="ShowAsync"/>。</summary>
    public GameResourceWindow()
    {
        InitializeComponent();
        // 跟随主窗口系统材质:窗口透明,只有内容面板是半透明玻璃面
        TransparencyLevelHint = SystemMaterialService.TransparencyLevelHint;

        Opened += async (_, _) =>
        {
            if (_opened)
            {
                return;
            }

            _opened = true;
            await LoadAsync();
        };

        Closed += (_, _) =>
        {
            // 关闭即取消:后台还在枚举磁盘的话,没人需要那份结果了
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        };
    }

    /// <summary>弹出游戏资源窗口(等用户关掉才返回)。</summary>
    /// <param name="owner">父窗口。</param>
    /// <param name="gameRootDir">游戏根目录;为空时窗口展示「未设置游戏目录」。</param>
    public static async Task ShowAsync(Window? owner, string? gameRootDir)
    {
        var window = new GameResourceWindow
        {
            DataContext = new GameResourceViewModel(gameRootDir),
        };

        if (owner is not null)
        {
            await window.ShowDialog(owner);
        }
        else
        {
            window.Show();
        }
    }

    private async Task LoadAsync()
    {
        if (DataContext is not GameResourceViewModel vm)
        {
            return;
        }

        _vm = vm;
        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        await vm.LoadAsync(_cts.Token);

        // 环从 0 长到目标值(Apple 三环的入场节奏)。放在加载之后播,
        // 这样用户看到的每一环长度就是最终读数,不会"长到一半又改数字"。
        if (Rings is not null)
        {
            Rings.PlayReveal();
        }
    }

    private void OnRescanClick(object? sender, RoutedEventArgs e) => _ = ReloadAsync();

    private async Task ReloadAsync()
    {
        if (_vm is null)
        {
            return;
        }

        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        await _vm.LoadAsync(_cts.Token);
    }

    // ---------- 页签 ----------

    private void OnSelectStorageTab(object? sender, RoutedEventArgs e) => SelectTab(device: false);

    private void OnSelectDeviceTab(object? sender, RoutedEventArgs e) => SelectTab(device: true);

    private void SelectTab(bool device)
    {
        if (_vm is null)
        {
            return;
        }

        _vm.IsStorageTabActive = !device;
        _vm.IsDeviceTabActive = device;

        // 设备页的数据在 LoadAsync 里就并行采完了,切过去只是显示 ——
        // 不需要"切页签才去读注册表",那样反而会看到一次空窗。
        _vm.IsDeviceLoading = false;
    }

    // ---------- 操作 ----------

    private async void OnCopyDeviceInfoClick(object? sender, RoutedEventArgs e)
    {
        if (_vm is null)
        {
            return;
        }

        var text = _vm.BuildDeviceInfoPlainText();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        try
        {
            var clipboard = Clipboard;
            if (clipboard is not null)
            {
                await clipboard.SetTextAsync(text);
            }

            // 按钮文案就地变一下,比另弹一个 Toast 更省(而且这个窗口里没有 Toast 容器)
            var original = _vm.CopyDeviceInfoText;
            _vm.CopyDeviceInfoText = LanguageService.Format("Launcher.Resource.DeviceCopied");
            await Task.Delay(1600);

            // 窗口可能在这 1.6 秒里被关掉:DataContext 还在,但控件已经不在树上了
            if (_opened && _vm is not null && !string.IsNullOrEmpty(original))
            {
                _vm.CopyDeviceInfoText = original;
            }
        }
        catch (Exception)
        {
            // 剪贴板被别的程序占用时静默失败:不影响读屏上的信息
        }
    }

    private void OnOpenFolderClick(object? sender, RoutedEventArgs e)
    {
        var root = _vm?.GameRootDisplay;
        if (string.IsNullOrEmpty(root) || !System.IO.Directory.Exists(root))
        {
            return;
        }

        AppServices.OpenInFileManager(root);
    }

    private void OnLegendRowDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (sender is not Control { DataContext: GameResourceLegendItem item } || !item.CanOpen)
        {
            return;
        }

        if (!string.IsNullOrEmpty(item.Path))
        {
            AppServices.OpenInFileManager(item.Path);
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// 冒烟钩子专用:切到设备信息页(<c>McKuro_SMOKE_RESOURCE=device</c>)。
    /// 不对外开放 —— 正常交互一律走页签按钮,这里只是省掉冒烟脚本里的 UI 自动化点击。
    /// </summary>
    internal void SelectDeviceTabForSmoke() => SelectTab(device: true);
}

