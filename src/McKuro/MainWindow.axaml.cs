using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Animation;

using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using McKuro.Services;

namespace McKuro;

public partial class MainWindow : Window
{
    /// <summary>启动页视频内容区宽高比(默认 2048x1216,视频加载后自动更新)。</summary>
    private double _targetAspectRatio = 2048.0 / 1216.0;

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

    /// <summary>首次布局提交里程碑是否已记录(诊断用)。</summary>
    private bool _firstLayoutMarked;

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
        // 诊断里程碑:窗口创建/打开/首次布局提交(配合 MCKURO-NOTIF 与 UI 心跳定位延迟所处阶段)
        UiHeartbeat.Mark("mainwindow-ctor-end");
        Opened += (_, _) => UiHeartbeat.Mark("window-opened");
        LayoutUpdated += (_, _) =>
        {
            // 只记第一次布局提交(该事件每次布局都会触发,不去重会刷屏且掩盖关键时间点)
            if (!_firstLayoutMarked)
            {
                _firstLayoutMarked = true;
                UiHeartbeat.Mark("first-layout-updated");
            }
        };
        InitTrayIcon();
        InitSystemMaterial();
        HookNavPill();
        ShellGrid.LayoutUpdated += OnShellLayoutProbe;
        InitImmersiveUi();
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

        // 无边框沉浸下操作栏的最大化/还原字形跟随状态
        UpdateMaximizeGlyph();

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
            var h = cw / _targetAspectRatio;
            if (h < MinHeight)
            {
                h = MinHeight;
                cw = h * _targetAspectRatio;
            }
            // 仅在偏离比例超过阈值时纠正,避免抖动
            if (Math.Abs(cw / size.Height - _targetAspectRatio) > 0.01)
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

    /// <summary>
    /// 启动页视频解析到实际分辨率后调用:把窗口比例切换到视频内容区比例,
    /// 并按当前宽度立刻重新计算高度。非法输入忽略(保持现有比例)。
    /// </summary>
    public void SetContentAspectRatio(double videoWidth, double videoHeight)
    {
        if (videoWidth <= 0 || videoHeight <= 0 || _targetAspectRatio == videoWidth / videoHeight)
        {
            return;
        }
        _targetAspectRatio = videoWidth / videoHeight;
        ApplyAspectCorrection();
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

    /// <summary>
    /// 重新测量非客户区厚度(物理像素)与渲染缩放。
    /// 窗口 chrome 会在「原生 ⇄ 无边框沉浸」间切换,切换后客户区厚度变化,
    /// WM_SIZING 的比例钳制必须用新的差值,否则拖拽时会把窗口越拖越偏。
    /// </summary>
    private void MeasureNonClientSize()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        var platformHandle = TryGetPlatformHandle();
        if (platformHandle is null || platformHandle.Handle == IntPtr.Zero)
        {
            return;
        }
        _renderScale = RenderScaling;
        if (GetWindowRect(platformHandle.Handle, out var wr) && GetClientRect(platformHandle.Handle, out var cr))
        {
            _nonClientX = (wr.Right - wr.Left) - (cr.Right - cr.Left);
            _nonClientY = (wr.Bottom - wr.Top) - (cr.Bottom - cr.Top);
        }
    }

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
        MeasureNonClientSize();
        _wndProcDelegate = WindowProcProxy;
        var procPtr = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate);
        // 64 位进程用 SetWindowLongPtrW,32 位用 SetWindowLongW(该 API 无 SetWindowLongPtrW 导出)
        _originalWndProc = Environment.Is64BitProcess
            ? SetWindowLongPtr64(platformHandle.Handle, GWL_WNDPROC, procPtr)
            : SetWindowLong32(platformHandle.Handle, GWL_WNDPROC, procPtr);
        System.Console.Error.WriteLine(
            $"MCKURO-SIZE hook=1 ncx={_nonClientX} ncy={_nonClientY} scale={_renderScale:F2} ratio={_targetAspectRatio:F4}");
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
            targetH = cw / _targetAspectRatio;
            if (targetH < minCh)
            {
                targetH = minCh;
                targetW = targetH * _targetAspectRatio;
            }
        }
        else
        {
            targetH = ch;
            targetW = ch * _targetAspectRatio;
            if (targetW < minCw)
            {
                targetW = minCw;
                targetH = targetW / _targetAspectRatio;
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
            // Title 为空时 FindWindow(null, null) 会匹配任意无标题窗口(可能拿到别人的句柄),必须显式跳过
            var title = Title;
            if (!string.IsNullOrEmpty(title))
            {
                _hwnd = FindWindow(null, title);
            }
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


    // ==================== 沉浸 / 自动隐藏(导航栏 + 自绘窗口操作栏) ====================
    // 设计要点:
    // ① 「窗口操作栏自动隐藏」开启 ⇒ 系统 chrome 换成无边框(WindowDecorations.BorderOnly +
    //    ExtendClientArea),原生标题栏/边框消失,改由软件内 TitleBarHost 承担拖动与三键;
    //    关闭 ⇒ 完全回到系统原生 chrome(XAML 里 TitleBarHost 不显示)。
    // ② 显隐判定只做「指针到窗口左/上边缘的距离」:进入唤出带(≤ RevealZone)立即展开,
    //    离开「展开区域 + HoldZone」后按轮询拍数延迟收起(避免快速划过时闪动)。
    // ③ 轮询同时用于 Windows 上指针落在原生 resize 边框(非客户区,拿不到 PointerMoved)的情形,
    //    其他平台退回 PointerMoved 提供的最近位置。

    /// <summary>导航栏展开宽度(DIP,与 XAML 中 NavRail.Width / ContentNavWidth 一致)。</summary>
    private const double NavRailWidth = ContentNavWidth;

    /// <summary>自绘窗口操作栏高度(DIP,与 XAML 中 TitleBarHost.Height 一致)。</summary>
    private const double TitleBarHeight = 38;

    /// <summary>
    /// 导航栏唤出带:指针距窗口左边缘 ≤ 该值即展开(DIP)。
    /// 按用户要求取「半个导航栏宽度」(76 / 2 = 38):靠近到导航栏一半宽度内就该出现,
    /// 不必一路贴到物理边缘(客户区左缘外还有约 7.3 DIP 的原生 resize 边框,贴边手感更差)。
    /// </summary>
    private const double NavRevealZone = NavRailWidth / 2;

    /// <summary>
    /// 操作栏唤出带:指针距窗口顶部 ≤ 该值即展开(DIP)。
    /// 按用户要求取「一个操作栏高度」(38):靠近顶部一个操作栏高度内就该出现。
    /// 顶部 0~6 DIP 仍是原生 resize 边框(WM_NCHITTEST=HTTOP,拿不到 PointerMoved),
    /// 靠 GetCursorPos 轮询覆盖;38 DIP 的带不会碰到页面首行控件(实测设置页分类菜单顶部约 60 DIP)。
    /// </summary>
    private const double TitleRevealZone = TitleBarHeight;

    /// <summary>已展开时继续悬停的余量:离开「展开区域 + 该余量」后才准备收起(DIP)。</summary>
    private const double HoldZone = 8;

    /// <summary>
    /// 导航栏收起前的等待拍数(轮询 40ms ⇒ 约 40ms,近乎即时)。
    /// 用户反馈「左边收缩有延迟」:导航栏只做一次 40ms 迟滞防抖,指针离开即收起。
    /// </summary>
    private const int NavHideDelayTicks = 1;

    /// <summary>
    /// 窗口操作栏收起前的等待拍数(轮询 70ms ⇒ 约 140ms)。
    /// 用户反馈「操作栏自动隐藏有延迟」:原先 8 拍(70ms/拍 ≈560ms)+200ms 动画接近 0.8 秒。
    /// 现轮询 40ms、只留 1 拍(≈40ms):防抖由「保持区 20 DIP + 按住鼠标不收起」承担,
    /// 不必再靠拉长拍数;指针离开顶部区域后立即开始收起(动画收 130ms)。
    /// </summary>
    private const int TitleHideDelayTicks = 1;

    /// <summary>
    /// 自动隐藏轮询间隔(ms)。原是 70ms(状态机按拍计数,拍粗了整体反应就慢);
    /// 40ms 让「离开边缘 → 开始收起」的判定误差不超过半帧多一点,轮询本身只是两次 GetCursorPos,开销可忽略。
    /// </summary>
    private const int AutoHidePollMs = 40;

    /// <summary>无边框沉浸模式下,指针最多允许超出客户区多少 DIP 仍算"贴边"(覆盖原生边框厚度)。</summary>
    private const double OutsideTolerance = 12;

    /// <summary>
    /// 操作栏需要在唤出带内连续停留多少拍才展开(40ms/拍 ⇒ 160ms)。
    /// 页面顶部常有横向菜单/页签(设置页分类、抽卡页视图),鼠标从窗口顶边穿过时若立即展开,
    /// 内容会被下推 38 DIP —— 用户随后点到的已经不是原来的按钮,表现为"点了没反应",
    /// 指针离开后又收起,看起来像"操作栏闪了一下"。要求停留即可过滤掉这种穿越。
    /// 导航栏不需要(左边缘没有横向菜单,且用户希望贴边即出)。
    /// </summary>
    private const int TitleRevealDwellTicks = 4;

    /// <summary>导航栏显隐状态机(距窗口左边缘的距离)。</summary>
    private readonly EdgeRevealState _navReveal = new()
    {
        Extent = NavRailWidth,
        RevealZone = NavRevealZone,
        HoldZone = HoldZone,
        HideDelayTicks = NavHideDelayTicks,
    };

    /// <summary>窗口操作栏显隐状态机(距窗口上边缘的距离)。</summary>
    private readonly EdgeRevealState _titleReveal = new()
    {
        Extent = TitleBarHeight,
        RevealZone = TitleRevealZone,
        // 保持区 = 操作栏高度 + 20:指针在栏内或紧贴栏下沿时保持显示,再往下就收起。
        // 必须 ≥ 唤出带(这里唤出带 = 一个操作栏高度 38),否则刚唤出就会因"超出保持区"被收起而抖动。
        // 余量也不宜过大(曾是 44):那样指针只在页面首行活动就会把操作栏一直挂住,体感"移开了却不收起"。
        // 真正的保护由「按住鼠标期间冻结」承担(见 OnAutoHideTick)。
        HoldZone = 20,
        HideDelayTicks = TitleHideDelayTicks,
        RevealDwellTicks = TitleRevealDwellTicks,
    };

    /// <summary>当前是否处于无边框沉浸模式(= 设置项 AutoHideTitleBar)。</summary>
    private bool _immersive;

    /// <summary>自动隐藏轮询的诊断计数(MCKURO_AUTOHIDE_TRACE=1 时每 14 拍打印一行)。</summary>
    private int _autohideTraceTicks;

    /// <summary>导航栏/操作栏当前视觉状态(动画的目标值,避免每拍重复触发动画)。</summary>
    private bool _navVisualShown = true;
    private bool _titleVisualShown;

    /// <summary>
    /// 导航栏是否被手动「临时固定」(点击导航栏顶部头像切换)。
    /// 只作用于本次运行:固定后不受贴边/失焦逻辑影响始终展开,再点一次恢复自动收起。
    /// </summary>
    private bool _navPinned;

    /// <summary>
    /// 窗口操作栏是否被手动「临时固定」(点击操作栏左侧应用 logo 切换)。
    /// 固定后操作栏常驻(内容保持下推)、不受贴边/失焦逻辑影响;再点一次恢复自动收起。只作用于本次运行。
    /// </summary>
    private bool _titlePinned;

    private DispatcherTimer? _autoHideTimer;

    /// <summary>上一次轮询到的指针位置(用于判断"指针是否在移动";静止不算贴边唤出)。</summary>
    private Point _lastPolledPoint;
    private bool _polledInitialized;

    /// <summary>非 Windows 平台/兜底:最近一次 PointerMoved 的窗口内坐标与是否在窗口内。</summary>
    private bool _pointerInside;
    private Point _pointerLast;

    /// <summary>
    /// 鼠标左键是否处于按下状态(窗口内任意位置)。
    /// 按下期间不收起操作栏:否则点页面首行按键时操作栏可能中途收起,把被点的按钮整体上移走。
    /// </summary>
    private bool _pointerButtonDown;

    /// <summary>冒烟钩子文件(空 = 未启用);启用时只用伪指针文件,忽略真实指针与指针事件,保证自动化可复现。</summary>
    private string? _smokePointerFile;

    /// <summary>是否启用冒烟伪指针(等价于 <see cref="_smokePointerFile"/> 非空)。</summary>
    private bool _smokePointerEnabled;

    /// <summary>是否输出自动隐藏诊断日志(启动时读一次 MCKURO_AUTOHIDE_TRACE,避免每拍查环境变量)。</summary>
    private bool _autohideTraceEnabled;


    private const string MaximizeGlyphData = "M0.5,0.5 L9.5,0.5 L9.5,9.5 L0.5,9.5 Z";
    private const string RestoreGlyphData = "M2.5,0.5 L9.5,0.5 L9.5,7.5 M0.5,2.5 L7.5,2.5 L7.5,9.5 L0.5,9.5 Z";

    /// <summary>当前最大化按钮字形是否已是"还原"图标(避免每次状态变化重建 Geometry)。</summary>
    private bool _maximizeGlyphIsRestore;


    /// <summary>初始化沉浸/自动隐藏:按已保存设置切换窗口 chrome,并挂接指针与设置变更。</summary>
    private void InitImmersiveUi()
    {
        var settings = AppServices.Settings.Current;
        _smokePointerFile = Environment.GetEnvironmentVariable("MCKURO_AUTOHIDE_POINT_FILE");
        _smokePointerEnabled = !string.IsNullOrEmpty(_smokePointerFile);
        _autohideTraceEnabled = Environment.GetEnvironmentVariable("MCKURO_AUTOHIDE_TRACE") == "1";

        // 初始状态按设置直接到位:启用了自动隐藏就从第一帧就是收起态(不再"先展开 2.2s 再收起",
        // 否则每次启动都会看到两栏闪一下再收走)
        _navReveal.ForceHide();
        _titleReveal.ForceHide();
        ApplyImmersiveChrome(settings.AutoHideTitleBar);
        SetNavRailShown(!settings.AutoHideNavRail, animate: false);
        SetTitleBarShown(!settings.AutoHideTitleBar, animate: false);

        TitleBarSurface.AddHandler(PointerPressedEvent, OnTitleBarPointerPressed, RoutingStrategies.Bubble);
        AddHandler(PointerMovedEvent, OnShellPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerExitedEvent, OnShellPointerExited, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerPressedEvent, OnShellPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnShellPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        // 顶部拖动带:操作栏收起时仍可拖窗(Tunnel 且在控件之前:命中空白处才接管,见方法注释)
        AddHandler(PointerPressedEvent, OnShellPointerPressedForDrag, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnShellKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        Deactivated += (_, _) => OnShellDeactivated();

        if (settings.AutoHideNavRail || settings.AutoHideTitleBar)
        {
            StartAutoHideTimer();
        }

        UiSettingsNotifier.Changed += OnUiSettingsChanged;
        Closed += (_, _) =>
        {
            UiSettingsNotifier.Changed -= OnUiSettingsChanged;
            StopAutoHideTimer();
        };
    }

    /// <summary>设置页改动「导航栏/窗口操作栏自动隐藏」后即时重读并应用。</summary>
    private void OnUiSettingsChanged()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(OnUiSettingsChanged);
            return;
        }
        var settings = AppServices.Settings.Current;

        if (settings.AutoHideTitleBar != _immersive)
        {
            ApplyImmersiveChrome(settings.AutoHideTitleBar);
        }

        // 开启自动隐藏 → 立即以收起状态生效(和启动时一致);关闭 → 常驻展开
        if (settings.AutoHideNavRail)
        {
            _navReveal.ForceHide();
            SetNavRailShown(false, animate: true);
        }
        else
        {
            _navReveal.ForceShow();
            SetNavRailShown(true, animate: true);
        }

        if (settings.AutoHideTitleBar)
        {
            _titleReveal.ForceHide();
            SetTitleBarShown(false, animate: true);
        }
        else if (_immersive)
        {
            _titleReveal.ForceShow();
            SetTitleBarShown(true, animate: true);
        }

        if (settings.AutoHideNavRail || settings.AutoHideTitleBar)
        {
            StartAutoHideTimer();
        }
        else
        {
            StopAutoHideTimer();
        }
        ApplyAutoHideVisuals();
    }

    /// <summary>切换系统窗口 chrome:原生(Full)⇄ 无边框沉浸(BorderOnly + 扩展客户区)。</summary>
    private void ApplyImmersiveChrome(bool immersive)
    {
        _immersive = immersive;
        if (immersive)
        {
            // 先去掉标题栏样式(WS_CAPTION),再让客户区铺满整窗(Avalonia 的扩展客户区行为)
            WindowDecorations = WindowDecorations.BorderOnly;
            ExtendClientAreaToDecorationsHint = true;
            ExtendClientAreaTitleBarHeightHint = -1;
        }
        else
        {
            ExtendClientAreaToDecorationsHint = false;
            WindowDecorations = WindowDecorations.Full;
        }

        TitleBarHost.IsVisible = immersive;
        TitleBarHost.IsHitTestVisible = immersive && _titleVisualShown;
        if (!immersive)
        {
            _titleVisualShown = false;
            _titlePinned = false;
            TitleBarLogoButton.Classes.Set("pinned", false);
            ApplyTitleBarOffset(-TitleBarHeight, animate: false);
        }

        // chrome 切换会改变客户区高度:等一次布局后重新量非客户区、重贴启动页比例与胶囊位置
        Dispatcher.UIThread.Post(() =>
        {
            MeasureNonClientSize();
            SnapNavPill();
            ApplyAspectCorrection();
            UpdateMaximizeGlyph();
            System.Console.Error.WriteLine(
                $"MCKURO-IMMERSIVE on={_immersive} ncx={_nonClientX} ncy={_nonClientY} " +
                $"client={ClientSize.Width:F0}x{ClientSize.Height:F0}");
        }, DispatcherPriority.Loaded);
    }

    private void OnShellPointerMoved(object? sender, PointerEventArgs e)
    {
        var point = e.GetPosition(this);
        _pointerInside = true;
        _pointerLast = point;

        if (_smokePointerEnabled)
        {
            return;
        }

        // 指针事件是"真实输入"的权威来源:某些场景下 GetCursorPos 会滞后/被独占程序裁掉
        // (远程会话、游戏用 ClipCursor 锁光标等),只要窗口确实收到了移动事件,就立即按贴边规则展开。
        // 这里只负责"展开/保持",不推进收起计时(高频事件会把延迟压没),收起仍由 70ms 轮询决定。
        var settings = AppServices.Settings.Current;
        bool changed = false;
        if (settings.AutoHideNavRail)
        {
            changed |= _navReveal.Observe(point.X);
        }
        if (_immersive && settings.AutoHideTitleBar)
        {
            changed |= _titleReveal.Observe(point.Y);
        }
        if (changed)
        {
            ApplyAutoHideVisuals();
        }
    }

    private void OnShellPointerExited(object? sender, PointerEventArgs e)
    {
        _pointerInside = false;
    }

    private void OnShellPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _pointerButtonDown = true;
        }
    }

    private void OnShellPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _pointerButtonDown = false;
    }

    /// <summary>键盘兜底:按 Alt 立即唤出两栏(指针唤出失效时仍可关窗/拖动)。</summary>
    private void OnShellKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.LeftAlt or Key.RightAlt))
        {
            return;
        }
        if (!_immersive && !AppServices.Settings.Current.AutoHideNavRail)
        {
            return;
        }
        _navReveal.ForceShow();
        _titleReveal.ForceShow();
        ApplyAutoHideVisuals();
    }

    /// <summary>窗口失焦:立即收起两栏(切走时界面更干净)。</summary>
    private void OnShellDeactivated()
    {
        if (!_immersive && !AppServices.Settings.Current.AutoHideNavRail)
        {
            return;
        }
        if (!_navPinned)
        {
            _navReveal.ForceHide();
        }
        if (!_titlePinned)
        {
            _titleReveal.ForceHide();
        }
        ApplyAutoHideVisuals();
    }

    private void StartAutoHideTimer()
    {
        if (_autoHideTimer is null)
        {
            _autoHideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(AutoHidePollMs) };
            _autoHideTimer.Tick += OnAutoHideTick;
        }
        if (!_autoHideTimer.IsEnabled)
        {
            _autoHideTimer.Start();
        }
    }

    private void StopAutoHideTimer()
    {
        _autoHideTimer?.Stop();
    }

    /// <summary>自动隐藏轮询:取指针位置 → 喂给两个状态机 → 按结果驱动动画。</summary>
    private void OnAutoHideTick(object? sender, EventArgs e)
    {
        if (!IsVisible || WindowState == WindowState.Minimized)
        {
            return;
        }

        var settings = AppServices.Settings.Current;
        bool navOn = settings.AutoHideNavRail;
        bool titleOn = _immersive && settings.AutoHideTitleBar;
        if (!navOn && !titleOn)
        {
            return;
        }

        var polled = TryGetPointerInClientDip();

        // 指针是否在移动:静止停在贴边带里不算"贴边唤出"。
        // 背景:启动时鼠标可能恰好停在窗口顶部/左边缘,若不判移动就会一启动就弹出操作栏且一直不收起。
        bool moved;
        if (polled is { } current)
        {
            // 冒烟钩子由脚本写文件驱动,视为"有移动"(否则静态伪指针会永远唤不出来)
            moved = _smokePointerEnabled
                || (_polledInitialized
                    && (Math.Abs(current.X - _lastPolledPoint.X) > 0.5
                        || Math.Abs(current.Y - _lastPolledPoint.Y) > 0.5));
            _lastPolledPoint = current;
            _polledInitialized = true;
        }
        else
        {
            moved = false;
            _polledInitialized = false;
        }

        // 指针事件作为第二来源(只有轮询认为"在窗口内"时才采信):
        // 轮询读不到真实位置(远程会话/光标被独占程序裁剪)时靠它兜底;
        // 但轮询已判定指针离开窗口时必须放弃事件源里残留的旧坐标 —— 否则从顶部原生边框带
        // 移出窗口不会触发 PointerExited,残留的"贴边"坐标会让操作栏永远不收起(用户报的"有概率不隐藏")。
        var eventPoint = !_smokePointerEnabled && _pointerInside && polled is not null
            ? _pointerLast
            : (Point?)null;
        var navEdge = NearestEdge(polled?.X, eventPoint?.X);
        var titleEdge = NearestEdge(polled?.Y, eventPoint?.Y);
        if (!moved)
        {
            if (!_navReveal.Shown)
            {
                navEdge = null;
            }
            if (!_titleReveal.Shown)
            {
                titleEdge = null;
            }
        }
        if (_autohideTraceEnabled && ++_autohideTraceTicks % 14 == 0)
        {
            System.Console.Error.WriteLine(
                $"MCKURO-AUTOHIDE poll={(polled is { } pp ? $"{pp.X:F1},{pp.Y:F1}" : "null")} " +
                $"evt={(eventPoint is { } ep ? $"{ep.X:F1},{ep.Y:F1}" : "null")} moved={moved} " +
                $"nav={_navReveal.Shown}/{_navVisualShown} title={_titleReveal.Shown}/{_titleVisualShown} " +
                $"pinned={_navPinned}/{_titlePinned} client={ClientSize.Width:F0}x{ClientSize.Height:F0}");
        }

        if (navOn)
        {
            if (_navPinned)
            {
                _navReveal.ForceShow();
            }
            else
            {
                _navReveal.Update(navEdge);
            }
        }
        if (_pointerButtonDown && OperatingSystem.IsWindows() && !IsLeftButtonDown())
        {
            // 在窗口外松开(wms 收不到 PointerReleased)会让标记永久为真,这里用真实按键状态纠正
            _pointerButtonDown = false;
        }
        if (titleOn)
        {
            if (_titlePinned)
            {
                _titleReveal.ForceShow();
            }
            else if (_pointerButtonDown)
            {
                // 按住鼠标期间**冻结**当前状态(既不 ForceShow 也不推进收起计时)。
                // 教训:这里曾写成 ForceShow —— 结果是“任何一次点击都会先把操作栏弹出来”,
                // 而操作栏一显示就把内容下推 38 DIP,按下的那一刻指针已被按下、
                // 松手时按钮已经移走 → Click 不触发(设置页点菜单“没反应”),
                // 随后 40ms 又收起 → 表现为“操作栏显示又消失”。冻结则两种情况都不会发生。
            }
            else
            {
                _titleReveal.Update(titleEdge);
            }
        }
        ApplyAutoHideVisuals();
    }

    /// <summary>两个指针来源取"更贴近边缘"的那个(忽略 null);都为空表示指针不在窗口内。</summary>
    private static double? NearestEdge(double? a, double? b)
    {
        if (a is not { } av)
        {
            return b;
        }
        if (b is not { } bv)
        {
            return av;
        }
        return Math.Min(av, bv);
    }

    /// <summary>
    /// 取指针相对客户区左上角的 DIP 坐标;指针明显离开窗口时返回 null。
    /// Windows 用 GetCursorPos(同时覆盖原生 resize 边框区域),其余平台用最近一次 PointerMoved。
    /// </summary>
    private Point? TryGetPointerInClientDip()
    {
        // 冒烟钩子:正常运行不设置该环境变量,走真实指针;设置后改为读文件(见方法注释)
        if (_smokePointerFile is { Length: > 0 } smokePointerFile)
        {
            return TryReadSmokePointer(smokePointerFile);
        }

        double x;
        double y;
        if (OperatingSystem.IsWindows() && GetCursorPos(out var screen))
        {
            var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            var origin = new POINT { X = 0, Y = 0 };
            if (handle == IntPtr.Zero || !ClientToScreen(handle, ref origin))
            {
                return null;
            }
            var scale = RenderScaling > 0 ? RenderScaling : 1.0;
            x = (screen.X - origin.X) / scale;
            y = (screen.Y - origin.Y) / scale;
        }
        else if (_pointerInside)
        {
            x = _pointerLast.X;
            y = _pointerLast.Y;
        }
        else
        {
            return null;
        }

        if (x < -OutsideTolerance || y < -OutsideTolerance
            || x > ClientSize.Width + OutsideTolerance || y > ClientSize.Height + OutsideTolerance)
        {
            return null;
        }
        return new Point(x, y);
    }

    /// <summary>
    /// 冒烟专用:从文件读取"伪指针"位置(单位 DIP,相对客户区左上角),供自动化验证贴边唤出。
    /// <para>
    /// 为什么需要:真实指针可能被前台的独占程序(游戏)用 ClipCursor 锁在屏幕中心,
    /// 此时 SetCursorPos 无法把光标移到窗口边缘,自动化脚本就没法验证唤出/收起。
    /// 文件内容为 <c>"x,y"</c>;<c>null</c>/空/文件不存在 = 指针在窗口外。
    /// 只有设置 <c>MCKURO_AUTOHIDE_POINT_FILE</c> 时才会走到这里,正常运行零开销。
    /// </para>
    /// </summary>
    private static Point? TryReadSmokePointer(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }
            var text = File.ReadAllText(path).Trim();
            if (text.Length == 0 || text.Equals("null", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            var parts = text.Split(',');
            if (parts.Length == 2
                && double.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var px)
                && double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var py))
            {
                return new Point(px, py);
            }
        }
        catch (IOException)
        {
            // 文件正被脚本覆盖写入:本拍当作"窗口外",下一拍再读
        }
        catch (UnauthorizedAccessException)
        {
        }
        return null;
    }

    /// <summary>把状态机结果落到视觉上(只在目标状态变化时才启动动画)。</summary>
    private void ApplyAutoHideVisuals()
    {
        var settings = AppServices.Settings.Current;

        bool wantNav = _navPinned || !settings.AutoHideNavRail || _navReveal.Shown;
        if (wantNav != _navVisualShown)
        {
            SetNavRailShown(wantNav, animate: true);
        }

        if (!_immersive)
        {
            return;
        }
        bool wantTitle = _titlePinned || !settings.AutoHideTitleBar || _titleReveal.Shown;
        if (wantTitle != _titleVisualShown)
        {
            SetTitleBarShown(wantTitle, animate: true);
        }
    }

    /// <summary>
    /// 导航栏展开/收起:宽度 76⇄0(XAML 中 NavRail 的 DoubleTransition)+ 内层固定 76 宽面板整体左移
    /// (NavGlassPanel 的 ThicknessTransition),内容区随之平滑回流。
    /// <para>
    /// 之前用 15ms DispatcherTimer 逐帧写属性,实测帧间隔被渲染帧节流到 ~26~32ms/帧
    /// (每个渲染帧只赶上一次回调),肉眼就是"顿挫";改为 Avalonia Transition 后由动画时钟按渲染帧推进。
    /// </para>
    /// </summary>
    private void SetNavRailShown(bool show, bool animate)
    {
        _navVisualShown = show;
        NavRail.IsHitTestVisible = show;
        // 不再在收起瞬间隐藏胶囊:外层 NavRail 宽 0 + ClipToBounds 已经把它裁掉,
        // 立即设 IsVisible=false 会让选中高亮在导航栏滑走前先"消失"一下。

        if (animate)
        {
            StartLayoutProbe("nav");
        }
        SetTransitionDuration(NavRail, show);
        SetTransitionDuration(NavGlassPanel, show);
        SetWithOptionalTransition(NavRail, animate, () => NavRail.Width = show ? NavRailWidth : 0);
        SetWithOptionalTransition(
            NavGlassPanel,
            animate,
            () => NavGlassPanel.Margin = show ? default : new Thickness(-NavRailWidth, 0, 0, 0));

        if (show)
        {
            // 导航容器(内层固定 76 宽)尺寸不变,胶囊位置不受宽度动画影响,直接贴合即可
            SnapNavPill();
        }
    }

    /// <summary>
    /// 自绘窗口操作栏展开/收起:操作栏自身沿 Y 轴上移隐藏,同时把内容区(导航栏 + 页面)**整体下推等高一段**。
    /// <para>
    /// 必须下推而不是覆盖:页面顶部常有可点按键(抽卡页的 UID/同步、主页的账号卡等),
    /// 覆盖式操作栏会挡住它们 —— 指针为了点页面首行反而停留在操作栏的保持区里,操作栏不会收起,
    /// 于是首行按键永远点不到。下推后操作栏底边始终贴住内容顶边:不遮挡、可正常点击。
    /// </para>
    /// </summary>
    private void SetTitleBarShown(bool show, bool animate)
    {
        _titleVisualShown = show;
        if (!_immersive)
        {
            TitleBarHost.IsVisible = false;
            TitleBarHost.IsHitTestVisible = false;
            ApplyTitleBarOffset(-TitleBarHeight, animate: false);
            return;
        }

        TitleBarHost.IsVisible = true;
        TitleBarHost.IsHitTestVisible = show;
        ApplyTitleBarOffset(show ? 0 : -TitleBarHeight, animate);
        UpdateMaximizeGlyph();
    }

    /// <summary>操作栏/导航栏展开与收起各自的补间时长:收起更快,避免"移开后还慢慢往上爬"的观感。</summary>
    private static readonly TimeSpan RevealDuration = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan HideDuration = TimeSpan.FromMilliseconds(130);

    /// <summary>把过渡时长改成展开/收起各自的档位(XAML 里声明的是默认 200ms)。</summary>
    private static void SetTransitionDuration(Control target, bool show)
    {
        var duration = show ? RevealDuration : HideDuration;
        foreach (var transition in target.Transitions ?? [])
        {
            if (transition is ThicknessTransition thickness)
            {
                thickness.Duration = duration;
            }
        }
    }

    /// <summary>
    /// 把操作栏纵向偏移(Y ∈ [-操作栏高度, 0])同时落到「操作栏自身位置」与「内容区顶部留白」:
    /// 操作栏底边永远等于内容顶边 —— 滑出时内容被平滑下推,收起时同步复位。
    /// </summary>
    private void ApplyTitleBarOffset(double titleBarY, bool animate)
    {
        if (animate)
        {
            StartLayoutProbe("title");
        }
        var inset = Math.Clamp(TitleBarHeight + titleBarY, 0, TitleBarHeight);
        SetTransitionDuration(TitleBarHost, titleBarY >= 0);
        SetTransitionDuration(ShellGrid, titleBarY >= 0);
        SetWithOptionalTransition(
            TitleBarHost,
            animate,
            () => TitleBarHost.Margin = new Thickness(0, titleBarY, 0, 0));
        SetWithOptionalTransition(
            ShellGrid,
            animate,
            () => ShellGrid.Margin = new Thickness(0, inset, 0, 0));
    }

    // ---- 临时诊断:测量过渡期间的"实际布局帧间隔"(确认是否还有顿挫) ----
    private long _probeFirstTick;
    private long _probeLastTick;
    private int _probeFrames;
    private long _probeSumGap;
    private long _probeMaxGap;
    private bool _probeActive;
    private string _probeName = "";

    private void StartLayoutProbe(string name)
    {
        if (!_autohideTraceEnabled)
        {
            return;
        }
        _probeActive = true;
        _probeName = name;
        _probeFirstTick = 0;
        _probeLastTick = 0;
        _probeFrames = 0;
        _probeSumGap = 0;
        _probeMaxGap = 0;
        DispatcherTimer.RunOnce(() =>
        {
            if (!_probeActive)
            {
                return;
            }
            _probeActive = false;
            if (_probeFrames > 1)
            {
                System.Console.Error.WriteLine(
                    $"MCKURO-ANIM {_probeName} layoutFrames={_probeFrames} " +
                    $"avg={_probeSumGap / (double)(_probeFrames - 1):F1}ms max={_probeMaxGap}ms " +
                    $"span={_probeLastTick - _probeFirstTick}ms");
            }
        }, TimeSpan.FromMilliseconds(400));
    }

    private void OnShellLayoutProbe(object? sender, EventArgs e)
    {
        if (!_probeActive)
        {
            return;
        }
        var now = Environment.TickCount64;
        if (_probeFirstTick == 0)
        {
            _probeFirstTick = now;
        }
        if (_probeLastTick != 0)
        {
            var gap = now - _probeLastTick;
            _probeSumGap += gap;
            if (gap > _probeMaxGap)
            {
                _probeMaxGap = gap;
            }
        }
        _probeLastTick = now;
        _probeFrames++;
    }

    /// <summary>
    /// 设置属性的目标值:animate=true 时由 XAML 声明的 Transition 自动补间;
    /// animate=false 时临时摘掉 Transition,保证"立即到位"(初始化、退出沉浸、设置切换等场景)。
    /// </summary>
    private static void SetWithOptionalTransition(Control target, bool animate, Action apply)
    {
        if (animate)
        {
            apply();
            return;
        }
        var saved = target.Transitions;
        target.Transitions = null;
        try
        {
            apply();
        }
        finally
        {
            target.Transitions = saved;
        }
    }

    /// <summary>自绘操作栏拖动/双击最大化:点在按钮上时交给按钮处理。</summary>
    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }
        if (e.Source is Visual source && FindAncestorButton(source) is not null)
        {
            return;
        }
        if (e.ClickCount >= 2)
        {
            ToggleMaximizeRestore();
            return;
        }
        BeginMoveDrag(e);
    }

    private static Button? FindAncestorButton(Visual? visual)
    {
        for (var current = visual; current is not null; current = current.GetVisualParent())
        {
            if (current is Button button)
            {
                return button;
            }
        }
        return null;
    }

    /// <summary>
    /// 顶部拖动带:操作栏<b>收起</b>时,客户区顶部 1/4(不少于 44 DIP)的空白处按住即可拖动窗口,
    /// 双击最大化/还原。判定本身在 <see cref="DragBandRules"/>(纯函数、有单测),这里只做 AVALONIA 接线。
    /// <para>
    /// 只在无边框沉浸模式下接管:此模式没有系统标题栏,操作栏一收起整个窗口就没有可拖的空白,
    /// 用户必须先贴顶唤出操作栏才能挪窗 —— 本方法把那片区域补回来。
    /// 操作栏已展开时不接管(那段本来就是操作栏自己的拖动区,避免两处抢同一个手势)。
    /// </para>
    /// <para>
    /// <b>只接空白</b>:命中可交互控件(按钮/输入框/列表项/页签…)时一律放行,否则会把
    /// 「点顶部菜单」变成「拖窗口」—— 这正是上一轮"设置页点分类没反应"的同类事故,
    /// 当时的原因是操作栏展开把内容下推;这里如果再吞点击,症状会一模一样。
    /// </para>
    /// </summary>
    private void OnShellPointerPressedForDrag(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }
        var point = e.GetPosition(this);
        // 判定统一走 DragBandRules(纯函数、有单测):模式/操作栏状态/命中控件/位置四道闸
        if (!DragBandRules.ShouldStartDrag(
                _immersive,
                _titleReveal.Shown,
                _titlePinned,
                IsInteractiveSource(e.Source as Visual),
                point.Y,
                Bounds.Height))
        {
            return;
        }
        if (e.ClickCount >= 2)
        {
            ToggleMaximizeRestore();
            e.Handled = true;
            return;
        }
        BeginMoveDrag(e);
    }

    /// <summary>
    /// 该视觉及其祖先里是否有「会消费指针事件的交互控件」。
    /// <para>
    /// 刻意<b>不含</b> <see cref="ScrollViewer"/> / <see cref="SelectingItemsControl"/> 这类容器:
    /// 页面根节点通常就是 ScrollViewer,把它们算进来会让整条拖动带永久失效。
    /// 容器空白处的按下本来就没有别的语义,交给拖动正合适。
    /// </para>
    /// <para>
    /// 用 <c>is</c> 逐类型判断而不是 <c>switch case</c>:类型间有继承关系
    /// (CheckBox/RadioButton ⊂ ToggleButton ⊂ ButtonBase),写 case 会因"已由上一 case 处理"
    /// 编译不过,且后者更易漏掉新增控件。
    /// </para>
    /// </summary>
    private static bool IsInteractiveSource(Visual? visual)
    {
        for (var current = visual; current is not null; current = current.GetVisualParent())
        {
            if (current is Button || current is TextBox || current is ComboBox || current is Slider
                || current is RadioButton || current is CheckBox || current is ToggleSwitch
                || current is ScrollBar || current is ListBoxItem || current is TreeViewItem
                || current is TabItem || current is MenuItem)
            {
                return true;
            }
        }
        return false;
    }

    private void ToggleMaximizeRestore()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    /// <summary>点击操作栏左侧应用 logo:临时固定/取消固定窗口操作栏(不落盘,重启恢复自动隐藏)。</summary>
    private void OnTitleBarLogoClick(object? sender, RoutedEventArgs e)
    {
        if (!_immersive)
        {
            return;
        }
        _titlePinned = !_titlePinned;
        TitleBarLogoButton.Classes.Set("pinned", _titlePinned);
        if (_titlePinned)
        {
            _titleReveal.ForceShow();
        }
        ApplyAutoHideVisuals();
    }

    /// <summary>点击导航栏顶部头像:临时固定/取消固定导航栏(不落盘,重启恢复自动隐藏)。</summary>
    private void OnNavAvatarClick(object? sender, RoutedEventArgs e)
    {
        if (!AppServices.Settings.Current.AutoHideNavRail)
        {
            // 没开自动隐藏时导航栏本来就常驻,固定没有意义(也会亮起误导性的描边)
            return;
        }
        _navPinned = !_navPinned;
        NavAvatarButton.Classes.Set("pinned", _navPinned);
        if (_navPinned)
        {
            _navReveal.ForceShow();
        }
        ApplyAutoHideVisuals();
    }

    private void OnTitleBarMinimize(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnTitleBarMaximizeRestore(object? sender, RoutedEventArgs e) => ToggleMaximizeRestore();

    private void OnTitleBarClose(object? sender, RoutedEventArgs e) => Close();

    /// <summary>最大化/还原按钮字形随窗口状态切换。</summary>
    private void UpdateMaximizeGlyph()
    {
        if (TitleBarMaximizeGlyph is null)
        {
            return;
        }
        var restore = WindowState == WindowState.Maximized;
        if (restore == _maximizeGlyphIsRestore && TitleBarMaximizeGlyph.Data is not null)
        {
            return;
        }
        _maximizeGlyphIsRestore = restore;
        TitleBarMaximizeGlyph.Data = Geometry.Parse(restore ? RestoreGlyphData : MaximizeGlyphData);
    }

    /// <summary>Win32 屏幕坐标(物理像素,用于读取真实指针位置,能覆盖原生 resize 边框)。</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    /// <summary>当前左键是否仍按住(真实键盘/鼠标状态,不受事件丢失影响)。</summary>
    private static bool IsLeftButtonDown()
        => (GetAsyncKeyState(0x01) & 0x8000) != 0;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    /// <summary>把客户区坐标(物理像素)换算为屏幕坐标,用于把光标位置反推到窗口内。</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

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
