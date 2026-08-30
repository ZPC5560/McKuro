using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Animation;

using Avalonia.Media;
using Avalonia.Threading;
using McKuro.Services;

namespace McKuro;

public partial class MainWindow : Window
{
    /// <summary>固定纵横比 = 启动页视频(2048x1216)内容区比例,缩放窗口时保持。</summary>
    private const double AspectRatio = 2048.0 / 1216.0;

    /// <summary>内容区左侧导航栏宽度(DIP);宽高比以内容区(去掉导航栏/非客户区)为准。</summary>
    private const double ContentNavWidth = 76;

    private bool _resizing;

    /// <summary>状态切换(最大化/最小化/还原)后的首次尺寸事件交由平台处理,不强制比例。</summary>
    private bool _deferAspectAdjust;

    /// <summary>尺寸纠正防抖:拖拽中只重置计时器,停止 120ms 后纠正一次,
    /// 避免每个 WM_SIZE 都反向 SetWindowPos/系统争抢窗口矩形导致边框持续闪烁。</summary>
    private readonly DispatcherTimer _aspectTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };

    /// <summary>标记正在执行纠正(其引发的 SizeChanged 不得再次调度纠正)。</summary>
    private bool _applyingAspect;

    private IntPtr _hwnd;

    /// <summary>系统托盘图标(隐藏到托盘时显示;从托盘恢复后隐藏)。</summary>
    private TrayIcon? _trayIcon;

    /// <summary>导航滑动胶囊动画进行中(布局回归时暂停吸附,避免与动画互相争抢)。</summary>

    public MainWindow()
    {
        InitializeComponent();
        // 窗口可缩放但保持固定比例(1180:650)
        SizeChanged += OnWindowSizeChanged;
        _aspectTimer.Tick += (_, _) => ApplyAspectCorrection();
        PropertyChanged += OnWindowPropertyChanged;
        Opened += (_, _) => InstallSizeAspectHook();
        InitTrayIcon();
        InitSystemMaterial();
        HookNavPill();
    }

    /// <summary>
    /// 跟随系统桌面材质:Win11 Mica / Win10 Acrylic / macOS 毛玻璃(26 液态玻璃由系统渲染)/
    /// Linux 透明+染色。启用 OS 材质时隐藏纯色底座、弱化涂抹层(os-material 样式类);
    /// 若平台实际未启用透明级别(如远程桌面/旧系统),回退到现有纯色设计。
    /// </summary>
    private void InitSystemMaterial()
    {
        TransparencyLevelHint = SystemMaterialService.TransparencyLevelHint;
        if (!SystemMaterialService.IsOsBackdropActive)
        {
            return;
        }
        RootShell.Classes.Add("os-material");
        Opened += (_, _) =>
        {
            // 诊断日志(仅一行,供排查材质是否被平台实际启用;GUI 进程无控制台时无输出)
            System.Console.Error.WriteLine(
                $"MCKURO-MATERIAL kind={SystemMaterialService.Kind} actual={ActualTransparencyLevel} " +
                $"osMaterialClass={RootShell.Classes.Contains("os-material")}");
            if (ActualTransparencyLevel == WindowTransparencyLevel.None)
            {
                RootShell.Classes.Remove("os-material");
            }
            // 导航栏布局诊断:等待一次布局提交后打印真实尺寸(配对 MCKURO-NAV 行解析)
            LayoutUpdated += OnNavLayoutDiagnostics;
        };
    }

    private bool _navDiagLogged;
    private string? _navDiagPrev;

    private void OnNavLayoutDiagnostics(object? sender, EventArgs e)
    {
        if (_navDiagLogged || NavList.Bounds.Height <= 0 || ClientSize.Height <= 0)
        {
            return;
        }
        var panel = NavList.ItemsPanelRoot as Panel;
        var c0 = NavList.ContainerFromIndex(0) as Control;
        var c1 = NavList.ContainerFromIndex(1) as Control;
        var c11 = NavList.ContainerFromIndex(11) as Control;
        var sample =
            $"MCKURO-NAV count={NavList.ItemCount} panel={panel?.GetType().Name} panelDesiredH={(panel?.DesiredSize.Height ?? 0):F0} " +
            $"listH={NavList.Bounds.Height:F0} c0={(c0?.Bounds.Height ?? 0):F0} c1={(c1?.Bounds.Height ?? 0):F0} " +
            $"c11Y={(c11?.Bounds.Y ?? 0):F0} c11H={(c11?.Bounds.Height ?? 0):F0} " +
            $"clientH={ClientSize.Height:F0} winH={Height:F0} scale={RenderScaling:F2}";
        if (sample == _navDiagPrev)
        {
            // 连续两次布局采样一致 → 布局已稳定,输出一次即止
            _navDiagLogged = true;
            LayoutUpdated -= OnNavLayoutDiagnostics;
            System.Console.Error.WriteLine(sample);
        }
        _navDiagPrev = sample;
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
        if (e.Property != WindowStateProperty || e.NewValue is not WindowState state)
        {
            return;
        }

        // 最小化位置=系统托盘 且游戏会话进行中(启动中/游戏中):点击最小化 → 隐藏到托盘,
        // 从托盘恢复后再最小化仍回托盘;游戏进程结束(会话空闲)后恢复为常规任务栏最小化。
        if (state == WindowState.Minimized
            && AppServices.Settings.Current.MinimizeLocationOnLaunch == "Tray"
            && AppServices.GameMonitor.State != GameSessionState.Idle)
        {
            HideToTray();
            return;
        }

        // 从最大化/最小化切回普通状态时,系统已恢复之前的窗口矩形;
        // 跳过紧随其后的尺寸纠正,避免把"还原尺寸"错误地按屏幕尺寸改写。
        if (state == WindowState.Normal)
        {
            _deferAspectAdjust = true;
        }
    }

    private void OnWindowSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (_resizing || _applyingAspect)
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
            // 拖动缩放时每个 WM_SIZE 都触发本回调;若立即按比例反向改
            // Width/Height,会与系统争抢窗口矩形(SetWindowPos 与拖动帧交替),
            // 表现为缩放过程中边框持续闪烁。改防抖:仅重置计时器,拖动停止
            // 120ms 后按当前尺寸纠正一次,期间窗口跟随拖动,松开后归位比例。
            _aspectTimer.Stop();
            _aspectTimer.Start();
        }
        finally
        {
            _resizing = false;
        }
    }

    /// <summary>防抖到期:按当前客户端尺寸纠正到固定比例(仅普通状态;Win32 拖动已由
    /// WM_SIZING 子类化全程锁定,此处兜底非 Win32 平台与编程性尺寸变化)。</summary>
    private void ApplyAspectCorrection()
    {
        if (IsSystemMaximized() || WindowState != WindowState.Normal)
        {
            return;
        }
        _applyingAspect = true;
        try
        {
            var size = ClientSize;
            if (size.Width <= 0 || size.Height <= 0)
            {
                return;
            }
            // 内容区(去掉导航栏)保持启动页视频比例
            var cw = size.Width - ContentNavWidth;
            if (cw <= 0)
            {
                return;
            }
            var h = cw / AspectRatio;
            if (h < MinHeight)
            {
                h = MinHeight;
                cw = h * AspectRatio;
            }
            // 仅在偏离比例超过阈值时纠正,避免抖动
            if (Math.Abs(cw / size.Height - AspectRatio) > 0.01)
            {
                Width = Math.Max(MinWidth, cw + ContentNavWidth);
                Height = Math.Max(MinHeight, h);
            }
        }
        finally
        {
            _applyingAspect = false;
        }
    }

    // ---- Win32 专用:WM_SIZING 子类化,拖动边框时窗口矩形按视频比例同步钳制 ----
    // 这是 Windows 上"拖拽全程锁比例"的标准做法:系统在每次拖动帧把当前矩形
    // 交给窗口过程,我们修改 lParam 里的 RECT 后返回,系统直接采纳该矩形,
    // 期间没有任何 SetWindowPos 与拖动帧的争抢,因此不会闪烁。
    // 非 Win32 平台跳过,由上方防抖纠正兜底。

    private const uint WM_SIZING = 0x0214;
    private const int GWL_WNDPROC = -4;

    private const int HTLEFT = 10;
    private const int HTRIGHT = 11;
    private const int HTTOP = 12;
    private const int HTTOPLEFT = 13;
    private const int HTTOPRIGHT = 14;
    private const int HTBOTTOM = 15;
    private const int HTBOTTOMLEFT = 16;
    private const int HTBOTTOMRIGHT = 17;

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    /// <summary>窗口过程代理必须持有强引用,否则被 GC 回收 = 崩溃。</summary>
    private WndProcDelegate? _wndProcDelegate;

    /// <summary>子类化前的原始窗口过程(转发所有消息)。</summary>
    private IntPtr _originalWndProc;

    /// <summary>窗口左+右边框(物理像素,Opened 后按窗口实际矩形测量一次)。</summary>
    private int _nonClientX;

    /// <summary>标题栏+上下边框(物理像素)。</summary>
    private int _nonClientY;

    /// <summary>当前渲染缩放(物理像素/DIP),用于把 DIP 尺寸换算为 WM_SIZING 的像素坐标。</summary>
    private double _renderScale = 1.0;

    /// <summary>仅 Windows:安装窗口过程钩子,拖动缩放时钳制矩形为视频比例。</summary>
    private void InstallSizeAspectHook()
    {
        if (!OperatingSystem.IsWindows() || _wndProcDelegate is not null)
        {
            return;
        }
        var platformHandle = TryGetPlatformHandle();
        if (platformHandle is null || platformHandle.Handle == IntPtr.Zero)
        {
            return;
        }
        _renderScale = RenderScaling;
        // 缓存非客户区尺寸:GetWindowRect 与 GetClientRect 的差值(同一坐标系的物理像素)
        if (GetWindowRect(platformHandle.Handle, out var wr) && GetClientRect(platformHandle.Handle, out var cr))
        {
            _nonClientX = (wr.Right - wr.Left) - (cr.Right - cr.Left);
            _nonClientY = (wr.Bottom - wr.Top) - (cr.Bottom - cr.Top);
        }
        _wndProcDelegate = WindowProcProxy;
        var procPtr = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate);
        // 64 位进程用 SetWindowLongPtrW,32 位用 SetWindowLongW(该 API 无 SetWindowLongPtrW 导出)
        _originalWndProc = Environment.Is64BitProcess
            ? SetWindowLongPtr64(platformHandle.Handle, GWL_WNDPROC, procPtr)
            : SetWindowLong32(platformHandle.Handle, GWL_WNDPROC, procPtr);
        System.Console.Error.WriteLine(
            $"MCKURO-SIZE hook=1 ncx={_nonClientX} ncy={_nonClientY} scale={_renderScale:F2} ratio={AspectRatio:F4}");
    }

    private IntPtr WindowProcProxy(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (msg == WM_SIZING)
            {
                ApplySizingAspect(lParam, wParam.ToInt32());
                return IntPtr.Zero; // WM_SIZING 默认应返回 0;系统采用修改后的矩形
            }
        }
        catch (Exception ex)
        {
            System.Console.Error.WriteLine($"MCKURO-SIZE wm-ex {ex.Message}");
        }
        return _originalWndProc == IntPtr.Zero
            ? DefWindowProc(hWnd, msg, wParam, lParam)
            : CallWindowProc(_originalWndProc, hWnd, msg, wParam, lParam);
    }

    /// <summary>把 WM_SIZING 的窗口矩形调整为:内容区(窗口矩形 - 非客户区 - 导航栏)比例 = 视频比例。
    /// 拖动边由系统控制,其对边(或对边对)保持不动,因此窗口不漂移。</summary>
    private void ApplySizingAspect(IntPtr lParam, int hitTest)
    {
        var r = Marshal.PtrToStructure<RECT>(lParam);
        var navPx = (int)Math.Round(ContentNavWidth * _renderScale);
        var inL = r.Left;
        var inT = r.Top;
        var inR = r.Right;
        var inB = r.Bottom;

        double cw = (r.Right - r.Left) - _nonClientX - navPx;
        double ch = (r.Bottom - r.Top) - _nonClientY;
        if (cw <= 0 || ch <= 0)
        {
            return;
        }

        double minCw = Math.Max(1.0, (MinWidth - ContentNavWidth) * _renderScale);
        double minCh = MinHeight * _renderScale;
        cw = Math.Max(cw, minCw);
        ch = Math.Max(ch, minCh);

        // 拖动边含左右(上/下边拖动或四角):宽度由用户控制,高度跟随;
        // 仅拖上/下边时:高度由用户控制,宽度跟随。
        bool heightFollowsWidth = hitTest != HTTOP && hitTest != HTBOTTOM;
        double targetW, targetH;
        if (heightFollowsWidth)
        {
            targetW = cw;
            targetH = cw / AspectRatio;
            if (targetH < minCh)
            {
                targetH = minCh;
                targetW = targetH * AspectRatio;
            }
        }
        else
        {
            targetH = ch;
            targetW = ch * AspectRatio;
            if (targetW < minCw)
            {
                targetW = minCw;
                targetH = targetW / AspectRatio;
            }
        }

        // 锚定:拖哪条边,那条边就交给系统,其余边保持;
        // 未知边值(如系统缓动产生的 ht=8)按"左上不动"处理,防止窗口漂移。
        bool anchorLeft = hitTest switch
        {
            HTLEFT or HTTOPLEFT or HTBOTTOMLEFT => false,   // 拖左边的边系 → 右边不动
            _ => true,                                       // 拖右边的边系/未知 → 左边不动
        };
        bool anchorTop = hitTest switch
        {
            HTTOP or HTTOPLEFT or HTTOPRIGHT => false,       // 拖上边的边系 → 底边不动
            _ => true,                                       // 拖底边的边系/未知 → 顶边不动
        };
        int innerW = (int)Math.Round(targetW) + _nonClientX + navPx;
        int innerH = (int)Math.Round(targetH) + _nonClientY;
        if (anchorLeft)
        {
            r.Right = r.Left + innerW;
        }
        else
        {
            r.Left = r.Right - innerW;
        }
        if (anchorTop)
        {
            r.Bottom = r.Top + innerH;
        }
        else
        {
            r.Top = r.Bottom - innerH;
        }
        if (Environment.GetEnvironmentVariable("MCKURO_SIZING_TRACE") == "1")
        {
            System.Console.Error.WriteLine(
                $"MCKURO-SIZING ht={hitTest} in=[{inL},{inT},{inR},{inB}] out=[{r.Left},{r.Top},{r.Right},{r.Bottom}]");
        }
        Marshal.StructureToPtr(r, lParam, false);
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

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern IntPtr SetWindowLong32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);


    // ---- 导航栏液态玻璃滑动胶囊(Apple 风格):可打断的滑动 + 形变 ----

    private DispatcherTimer? _pillTimer;
    private double _pillTargetX;
    private double _pillTargetY;
    private double _pillStretch;

    /// <summary>目标已定但容器未就绪:挂起,LayoutUpdated 补滑(绝不直接跳位)。</summary>
    private bool _pillTargetPending;

    /// <summary>挂接选中/尺寸/布局事件,驱动滑动胶囊。</summary>
    private void HookNavPill()
    {
        NavList.SelectionChanged += (_, _) => OnNavSelected();
        NavList.SizeChanged += (_, _) => SnapNavPill();
        NavList.Loaded += (_, _) => SnapNavPill();
        NavList.LayoutUpdated += (_, _) =>
        {
            // 滑动中:逐帧实时追踪选中项,绝不干预动画,避免互相争抢
            if (_pillTimer is not null)
            {
                return;
            }
            if (_pillTargetPending)
            {
                // 容器就绪了:补上之前挂起的滑动
                OnNavSelected();
                return;
            }
            // 空闲贴合(窗口缩放/DPI 导致的行高变化):仅当目标明显偏离当前位置才静默吸附,
            // 避免在页面切换的布局回归里与滑动动画争夺(那正是"跳变/回弹"的来源)。
            var info = ReadNavTarget();
            if (info is null)
            {
                return;
            }
            bool moved = Math.Abs(info.Value.raw.X - PillTranslate.X) > 0.5
                      || Math.Abs(info.Value.raw.Y + 3 - PillTranslate.Y) > 0.5;
            if (moved)
            {
                SnapNavPill();
            }
        };
    }

    /// <summary>读取当前选中项容器及其目标(相对 NavGlassPanel);容器未就绪返回 null。</summary>
    private (ListBoxItem container, Point raw)? ReadNavTarget()
    {
        if (NavList.SelectedIndex < 0 || NavList.ItemCount == 0)
        {
            return null;
        }
        if (NavList.ContainerFromIndex(NavList.SelectedIndex) is not ListBoxItem container
            || !container.IsLoaded)
        {
            return null;
        }
        var pt = container.TranslatePoint(new Point(0, 0), NavGlassPanel);
        if (pt is null)
        {
            return null;
        }
        return (container, pt.Value);
    }

    private void ApplyNavPillSize(ListBoxItem container)
    {
        NavPill.IsVisible = true;
        NavPill.Width = container.Bounds.Width;
        NavPill.Height = Math.Max(36, container.Bounds.Height - 6);
    }

    /// <summary>选中项变化:定位新容器并启动滑动(容器未就绪则挂起,绝不直接跳位)。</summary>
    private void OnNavSelected()
    {
        if (_pillTimer is not null)
        {
            // 滑动中:逐帧实时追踪会自动滑向新选中项(可打断,方向自动修正)
            _pillTargetPending = false;
            return;
        }
        var info = ReadNavTarget();
        if (info is null)
        {
            _pillTargetPending = true;
            return;
        }
        ApplyNavPillSize(info.Value.container);
        _pillTargetX = info.Value.raw.X;
        _pillTargetY = info.Value.raw.Y + 3;
        _pillTargetPending = false;
        StartNavGlide();
    }

    /// <summary>静默贴合:不滑动(窗口缩放/DPI/初始)。若滑动中则交由实时追踪接管。</summary>
    private void SnapNavPill()
    {
        var info = ReadNavTarget();
        if (info is null)
        {
            NavPill.IsVisible = false;
            _pillTargetPending = false;
            return;
        }
        ApplyNavPillSize(info.Value.container);
        _pillTargetX = info.Value.raw.X;
        _pillTargetY = info.Value.raw.Y + 3;
        _pillTargetPending = false;
        if (_pillTimer is not null)
        {
            // 尺寸/DPI 变化在滑动中:实时追踪会纠正,不清零动画(PillTranslate 已持有当前位)
            return;
        }
        PillTranslate.X = _pillTargetX;
        PillTranslate.Y = _pillTargetY;
        PillScale.ScaleX = 1;
        PillScale.ScaleY = 1;
        _pillStretch = 0;
    }

    /// <summary>液态玻璃滑动:指数追踪 + 速度驱动的挤压/拉伸。
    /// 每帧按"当前"选中容器位置重算目标(消除瞬时快照跳变);指数曲线从不超调单一目标,
    /// 改目标时立即转向新目标(可打断、不来回摆)。0.16 收敛系数在保留玻璃缓动拖尾感的同时
    /// 更快到达,使相邻切换通常落在静止态,避免"滑动未稳就再点"造成的回弹。</summary>
    private void StartNavGlide()
    {
        _pillTimer?.Stop();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _pillTimer = timer;
        const double k = 0.16;
        timer.Tick += (_, _) =>
        {
            // 逐帧实时追踪:每帧按"当前"选中容器位置重算目标。
            // 切换瞬间布局未稳(容器暂未就绪/位置暂偏移)时,胶囊不会冲向一个冻结的错误快照,
            // 而是始终滑向真实位置——这就是消除"跳变"与"回弹"的关键。
            var info = ReadNavTarget();
            if (info is null || !NavPill.IsVisible)
            {
                _pillStretch = 0;
                timer.Stop();
                _pillTimer = null;
                return;
            }
            _pillTargetX = info.Value.raw.X;
            _pillTargetY = info.Value.raw.Y + 3;
            var dx = _pillTargetX - PillTranslate.X;
            var dy = _pillTargetY - PillTranslate.Y;
            if (Math.Abs(dx) < 0.5 && Math.Abs(dy) < 0.5)
            {
                PillTranslate.X = _pillTargetX;
                PillTranslate.Y = _pillTargetY;
                PillScale.ScaleX = 1;
                PillScale.ScaleY = 1;
                _pillStretch = 0;
                timer.Stop();
                _pillTimer = null;
                return;
            }
            // 指数收敛:速度正比于剩余距离,接近目标时自然减速,无超调
            var vx = dx * k;
            var vy = dy * k;
            PillTranslate.X += vx;
            PillTranslate.Y += vy;

            // 速度驱动的挤压/拉伸(Apple Liquid Glass squash & stretch),
            // _pillStretch 插值平滑,避免方向/速度突变时尺寸"pop":
            // 沿运动方向拉伸、垂直方向等比压扁,速度衰减时形变自动归零。
            // 胶囊 RenderTransformOrigin=50%,50%,形变以中心为轴对称——否则拉伸始终从
            // 左上角向下膨胀,向上移动时形变与运动方向相反,读起来像"回弹"。
            var speed = Math.Sqrt(vx * vx + vy * vy);
            var targetStretch = Math.Min(0.28, speed * 0.018);
            _pillStretch += (targetStretch - _pillStretch) * 0.35;
            if (Math.Abs(dx) >= Math.Abs(dy))
            {
                PillScale.ScaleX = 1 + _pillStretch;
                PillScale.ScaleY = Math.Max(0.75, 1 - _pillStretch * 0.55);
            }
            else
            {
                PillScale.ScaleY = 1 + _pillStretch;
                PillScale.ScaleX = Math.Max(0.75, 1 - _pillStretch * 0.55);
            }
        };
        timer.Start();
    }

    private TranslateTransform PillTranslate => ((TransformGroup)NavPill.RenderTransform!).Children[0] as TranslateTransform ?? throw new InvalidOperationException();
    private ScaleTransform PillScale => ((TransformGroup)NavPill.RenderTransform!).Children[1] as ScaleTransform ?? throw new InvalidOperationException();}
