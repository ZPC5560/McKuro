using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;

namespace McKuro;

public partial class MainWindow : Window
{
    /// <summary>固定宽高比(1150:650),缩放时保持比例。</summary>
    private const double AspectRatio = 1150.0 / 650.0;

    private bool _resizing;

    /// <summary>状态切换(最大化/最小化/还原)后的首次尺寸事件交由平台处理,不强制比例。</summary>
    private bool _deferAspectAdjust;

    private IntPtr _hwnd;

    public MainWindow()
    {
        InitializeComponent();
        // 窗口可缩放但保持固定比例(1150:650)
        SizeChanged += OnWindowSizeChanged;
        PropertyChanged += OnWindowPropertyChanged;
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
