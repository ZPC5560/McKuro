using Avalonia;
using Avalonia.Controls;

namespace McKuro;

public partial class MainWindow : Window
{
    /// <summary>固定宽高比(1150:650),缩放时保持比例。</summary>
    private const double AspectRatio = 1150.0 / 650.0;

    private bool _resizing;

    public MainWindow()
    {
        InitializeComponent();
        // 窗口可缩放但保持固定比例(1150:650)
        SizeChanged += OnWindowSizeChanged;
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
}
