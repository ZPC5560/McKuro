using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform;

namespace McKuro;

public partial class MainWindow : Window
{
    /// <summary>固定宽高比(1150:650),缩放时保持比例。</summary>
    private const double AspectRatio = 1150.0 / 650.0;

    private bool _resizing;

    /// <summary>状态切换(最大化/最小化/还原)后的首次尺寸事件交由平台处理,不强制比例。</summary>
    private bool _deferAspectAdjust;

    private IntPtr _hwnd;

    /// <summary>系统托盘图标(隐藏到托盘时显示;从托盘恢复后隐藏)。</summary>
    private TrayIcon? _trayIcon;

    public MainWindow()
    {
        InitializeComponent();
        // 窗口可缩放但保持固定比例(1150:650)
        SizeChanged += OnWindowSizeChanged;
        PropertyChanged += OnWindowPropertyChanged;
        InitTrayIcon();
    }

    /// <summary>
    /// 系统托盘:仅在窗口隐藏到托盘时显示图标,右键菜单「显示软件 / 退出」。
    /// Avalonia 12 中 TrayIcon 挂在 Application(TrayIcon.SetIcons)上;macOS 上
    /// TrayIcon 自动渲染为菜单栏状态项,任务栏/Dock 最小化走 WindowState.Minimized。
    /// </summary>
    private void InitTrayIcon()
    {
        var icon = new TrayIcon
        {
            ToolTipText = "McKuro · 鸣潮启动器",
            IsVisible = false,
        };
        try
        {
            // 流由 WindowIcon 持有(平台实现构造时拷贝,HICON/NSImage),应用生命周期内保留
            icon.Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://McKuro/Assets/app.ico")));
        }
        catch (Exception)
        {
            // 图标资源异常时托盘仍可用(无图标)
        }

        var menu = new NativeMenu();
        var showItem = new NativeMenuItem { Header = "显示软件" };
        showItem.Click += OnTrayShowClicked;
        var exitItem = new NativeMenuItem { Header = "退出" };
        exitItem.Click += OnTrayExitClicked;
        menu.Items.Add(showItem);
        menu.Items.Add(exitItem);
        icon.Menu = menu;
        icon.Clicked += (_, _) => RestoreFromHidden();
        _trayIcon = icon;

        if (Application.Current is { } app)
        {
            var icons = TrayIcon.GetIcons(app) ?? [];
            icons.Add(icon);
            TrayIcon.SetIcons(app, icons);
        }
    }

    /// <summary>
    /// 最小化到系统托盘:显示托盘图标并隐藏主窗口
    /// (macOS 对应:窗口隐藏,托盘图标渲染为菜单栏状态项)。
    /// </summary>
    public void HideToTray()
    {
        SetTrayIconVisible(true);
        Hide();
    }

    /// <summary>
    /// 从托盘/最小化恢复主窗口(显示软件)。
    /// </summary>
    public void RestoreFromHidden()
    {
        if (!IsVisible)
        {
            Show();
        }
        if (WindowState != WindowState.Normal)
        {
            WindowState = WindowState.Normal;
        }
        Activate();
        SetTrayIconVisible(false);
    }

    /// <summary>托盘图标单击:显示主窗口。</summary>
    private void OnTrayIconClicked(object? sender, EventArgs e) => RestoreFromHidden();

    /// <summary>托盘菜单「显示软件」。</summary>
    private void OnTrayShowClicked(object? sender, EventArgs e) => RestoreFromHidden();

    /// <summary>托盘菜单「退出」。</summary>
    private void OnTrayExitClicked(object? sender, EventArgs e)
    {
        if (Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    private void SetTrayIconVisible(bool visible)
    {
        if (_trayIcon is { } icon)
        {
            icon.IsVisible = visible;
        }
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        // 从最大化/最小化切回普通状态时,系统已恢复之前的窗口矩形;
        // 跳过紧随其后的尺寸纠正,避免把"还原尺寸"错误地按屏幕尺寸改写。
        if (e.Property == WindowStateProperty && e.NewValue is WindowState state && state == WindowState.Normal)
        {
            _deferAspectAdjust = true;
        }
    }

    private void OnWindowSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (_resizing)
        {
            return;
        }
        _resizing = true;
        try
        {
            // 系统标题栏最大化/还原路径下,Avalonia 的 WindowState 属性更新
            // 滞后于平台的尺寸回调:系统先改变窗口矩形(触发 SizeChanged),之后
            // 才回写 WindowState。若在此按屏幕尺寸纠正 Width/Height,会污染
            // Windows 保存的"还原矩形",导致还原后窗口回不到原来的大小。
            // 因此以系统真实放置状态(GetWindowPlacement)而非属性为判据。
            if (IsSystemMaximized() || WindowState != WindowState.Normal)
            {
                return;
            }
            if (_deferAspectAdjust)
            {
                // 还原瞬间的尺寸就是系统恢复的原值,直接采纳
                _deferAspectAdjust = false;
                return;
            }
            var size = e.NewSize;
            if (size.Width <= 0 || size.Height <= 0)
            {
                return;
            }
            // 保持固定比例:按当前宽高比调整到最近的合法尺寸
            var w = size.Width;
            var h = w / AspectRatio;
            if (h < MinHeight)
            {
                h = MinHeight;
                w = h * AspectRatio;
            }
            // 仅在偏离比例超过阈值时纠正,避免抖动
            if (Math.Abs(size.Width / size.Height - AspectRatio) > 0.01)
            {
                Width = Math.Max(MinWidth, w);
                Height = Math.Max(MinHeight, h);
            }
        }
        finally
        {
            _resizing = false;
        }
    }

    private bool IsSystemMaximized()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }
        if (_hwnd == IntPtr.Zero)
        {
            _hwnd = FindWindow(null, Title);
        }
        if (_hwnd == IntPtr.Zero)
        {
            return false;
        }
        var placement = new WINDOWPLACEMENT
        {
            length = Marshal.SizeOf<WINDOWPLACEMENT>(),
        };
        return GetWindowPlacement(_hwnd, ref placement) && placement.showCmd == SW_SHOW_MAXIMIZED;
    }

    private const int SW_SHOW_MAXIMIZED = 3;

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPLACEMENT
    {
        public int length;
        public int flags;
        public int showCmd;
        public int ptMinPositionX;
        public int ptMinPositionY;
        public int ptMaxPositionX;
        public int ptMaxPositionY;
        public int rcNormalPositionLeft;
        public int rcNormalPositionTop;
        public int rcNormalPositionRight;
        public int rcNormalPositionBottom;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);
}
