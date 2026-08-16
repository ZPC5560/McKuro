using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using McKuro.Services;

namespace McKuro;

public partial class MainWindow : Window
{
    private ScreenCaptureService? _capture;

    /// <summary>固定宽高比(1150:650),缩放时保持比例。</summary>
    private const double AspectRatio = 1150.0 / 650.0;

    private bool _resizing;

    public MainWindow()
    {
        InitializeComponent();
        // 窗口可缩放但保持固定比例(1150:650)
        SizeChanged += OnWindowSizeChanged;
    }

    /// <summary>标题栏拖动窗口(双击最大化/还原)。</summary>
    private void TitleBar_OnPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            if (e.ClickCount >= 2)
            {
                ToggleMaximize();
                return;
            }
            BeginMoveDrag(e);
        }
    }

    /// <summary>最小化窗口。</summary>
    private void MinimizeButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    /// <summary>最大化/还原窗口。</summary>
    private void MaximizeButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => ToggleMaximize();

    private void ToggleMaximize()
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    /// <summary>关闭窗口。</summary>
    private void CloseButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => Close();

    private void OnWindowSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (_resizing)
        {
            return;
        }
        _resizing = true;
        try
        {
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

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        var handle = TryGetPlatformHandle()?.Handle ?? nint.Zero;
        if (handle == nint.Zero)
        {
            return;
        }

        var settings = AppServices.Settings.Current;
        _capture = new ScreenCaptureService();
        _capture.Attach(handle);
        _capture.Register(settings.CaptureModifierKey, settings.CaptureKey);
        _capture.CaptureCompleted += path => Dispatcher.UIThread.Post(() =>
        {
            if (DataContext is ViewModels.SettingsViewModel settingsVm)
            {
                settingsVm.NotifyCaptureSaved(path);
            }
        });
    }

    protected override void OnClosed(EventArgs e)
    {
        _capture?.Dispose();
        _capture = null;
        base.OnClosed(e);
    }
}
