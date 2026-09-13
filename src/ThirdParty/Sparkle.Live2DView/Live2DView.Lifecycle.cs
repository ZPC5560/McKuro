using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Sparkle.Live2DView;

public sealed partial class Live2DView
{
    public static readonly DirectProperty<Live2DView, bool> IsLoadingProperty =
        AvaloniaProperty.RegisterDirect<Live2DView, bool>(
            nameof(IsLoading),
            view => view.IsLoading);

    private Window? _window;
    private bool _isLoading;
    private bool _isAttachedToVisualTree;

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetAndRaise(IsLoadingProperty, ref _isLoading, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        _isAttachedToVisualTree = true;
        _window = TopLevel.GetTopLevel(this) as Window;
        if (_window is not null)
            _window.PropertyChanged += WindowPropertyChanged;

        UpdateSurfaceActivity();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_window is not null)
            _window.PropertyChanged -= WindowPropertyChanged;

        _window = null;
        _isAttachedToVisualTree = false;
        UpdateSurfaceActivity();

        base.OnDetachedFromVisualTree(e);
    }

    private void HookLifecycle()
    {
        _surface.LoadingStateChanged += SurfaceLoadingStateChanged;
        LayoutUpdated += ViewLayoutUpdated;
        IsLoading = _surface.IsLoading;
    }

    private void ViewLayoutUpdated(object? sender, EventArgs e)
    {
        UpdateSurfaceActivity();
    }

    private void SurfaceLoadingStateChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            IsLoading = _surface.IsLoading;
            return;
        }

        Dispatcher.UIThread.Post(() => IsLoading = _surface.IsLoading);
    }

    private void WindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty)
            UpdateSurfaceActivity();
    }

    private void UpdateSurfaceActivity()
    {
        bool windowIsVisible = _window?.WindowState != WindowState.Minimized;
        bool visible = _isAttachedToVisualTree && IsEffectivelyVisible && windowIsVisible;

        _surface.SetHostState(
            visible,
            AutoPauseWhenHidden,
            ReleaseModelWhenHidden);
    }
}
