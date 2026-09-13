using Avalonia.Controls;
using Avalonia.Media;

namespace Sparkle.Live2DView;

public sealed partial class Live2DView : UserControl
{
    private readonly Live2DSurface _surface = new();
    private readonly Border _inputLayer = new() { Background = Brushes.Transparent };

    public Live2DView()
    {
        // OpenGL 控件收不到普通的 Avalonia 指针事件，所以在上面盖一层透明 Border。
        Content = new Grid
        {
            Children = { _surface, _inputLayer }
        };

        HookInputEvents();
        HookLifecycle();
        _surface.ViewChanged += SurfaceViewChanged;
        _surface.ModelLoaded += (_, _) => ApplyDisplayProperties();
    }

    public string ModelDirectory
    {
        get => _surface.ModelDirectory;
        set => _surface.ModelDirectory = value;
    }

    public string ModelName
    {
        get => _surface.ModelName;
        set => _surface.ModelName = value;
    }

    public bool IsPointerFollowEnabled { get; set; } = true;
    public bool IsModelLoaded => _surface.IsModelLoaded;

    public event EventHandler? ModelLoaded
    {
        add => _surface.ModelLoaded += value;
        remove => _surface.ModelLoaded -= value;
    }

    public event EventHandler<Live2DModelEventArgs>? ModelLoading
    {
        add => _surface.ModelLoading += value;
        remove => _surface.ModelLoading -= value;
    }

    public event EventHandler<Live2DModelLoadFailedEventArgs>? ModelLoadFailed
    {
        add => _surface.ModelLoadFailed += value;
        remove => _surface.ModelLoadFailed -= value;
    }

    public event EventHandler<Live2DModelEventArgs>? ModelUnloaded
    {
        add => _surface.ModelUnloaded += value;
        remove => _surface.ModelUnloaded -= value;
    }

    public event EventHandler? ViewChanged
    {
        add => _surface.ViewChanged += value;
        remove => _surface.ViewChanged -= value;
    }

    public Task LoadModelAsync(string modelDirectory, string modelName)
    {
        return _surface.LoadModelAsync(modelDirectory, modelName);
    }

    public Task LoadModelAsync(
        string modelDirectory,
        string modelName,
        CancellationToken cancellationToken)
    {
        return _surface.LoadModelAsync(modelDirectory, modelName, cancellationToken);
    }

    public Task ReloadModelAsync()
    {
        return _surface.ReloadModelAsync();
    }

    public Task ReloadModelAsync(CancellationToken cancellationToken)
    {
        return _surface.ReloadModelAsync(cancellationToken);
    }

    public Task UnloadModelAsync()
    {
        return _surface.UnloadModelAsync();
    }

    public void LookAt(double x, double y, double width, double height)
    {
        _surface.LookAt(x, y, width, height);
    }

    public void LookAtCenter()
    {
        _surface.LookAtCenter();
    }

    public void PanBy(double deltaX, double deltaY, double viewportHeight)
    {
        _surface.PanBy(deltaX, deltaY, viewportHeight);
    }

    public void SetPosition(float x, float y)
    {
        SetCurrentValue(PositionXProperty, x);
        SetCurrentValue(PositionYProperty, y);
    }

    public void ZoomBy(double wheelDelta)
    {
        _surface.ZoomBy(wheelDelta);
    }

    public void SetZoom(float zoom)
    {
        SetCurrentValue(ZoomProperty, zoom);
    }

    public void ResetTransform()
    {
        _surface.ResetTransform();
    }

    public void SetOpacity(float opacity)
    {
        SetCurrentValue(ModelOpacityProperty, opacity);
    }

    public void SetFramesPerSecond(int framesPerSecond)
    {
        SetCurrentValue(FramesPerSecondProperty, framesPerSecond);
    }

    public void SetRenderingPaused(bool paused)
    {
        _surface.SetRenderingPaused(paused);
    }
}
