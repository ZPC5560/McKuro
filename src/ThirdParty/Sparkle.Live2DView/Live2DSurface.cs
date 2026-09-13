using Avalonia.Controls;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Live2DCSharpSDK.App;
using Sparkle.Live2DView.Internal;

namespace Sparkle.Live2DView;

public sealed partial class Live2DSurface : OpenGlControlBase
{
    private readonly object _modelGate = new();
    private InteractiveLive2DDelegate? _live2D;
    private LAppModel? _model;
    private RenderLoop? _renderLoop;
    private DateTime _lastFrame;
    private double _renderScale = 1;
    private int _framesPerSecond = 60;
    private float _modelOpacity = 1;
    private bool _automaticIdle;
    private bool _manuallyPaused;
    private bool _hostVisible = true;
    private bool _autoPauseWhenHidden = true;
    private bool _releaseModelWhenHidden;

    public bool IsModelLoaded => _model is not null;
    public float PositionX => _live2D?.X ?? 0;
    public float PositionY => _live2D?.Y ?? 0;
    public float Zoom => _live2D?.Zoom ?? 1;

    public event EventHandler? ViewChanged;

    protected override void OnOpenGlInit(GlInterface gl)
    {
        try
        {
            CubismRuntime.EnsureStarted();
            _gl = gl;
            _lastFrame = DateTime.UtcNow;
            _renderLoop = new RenderLoop(this, _framesPerSecond);
            _renderLoop.Start();
            UpdateRenderLoopState();
        }
        catch (Exception exception)
        {
            ModelLoadFailed?.Invoke(
                this,
                new Live2DModelLoadFailedEventArgs(ModelDirectory, ModelName, exception));
            FailPendingOperations(exception);
            return;
        }

        lock (_modelGate)
        {
            if (_pendingModelChanges.IsEmpty)
                LoadInitialModel();
            else
                ProcessPendingModelChanges();
        }
    }

    protected override void OnOpenGlRender(GlInterface gl, int framebuffer)
    {
        _renderScale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;

        int width = Math.Max(1, (int)(Bounds.Width * _renderScale));
        int height = Math.Max(1, (int)(Bounds.Height * _renderScale));
        gl.Viewport(0, 0, width, height);

        DateTime now = DateTime.UtcNow;
        float elapsed = Math.Min(0.1f, (float)(now - _lastFrame).TotalSeconds);
        _lastFrame = now;

        lock (_modelGate)
        {
            ProcessPendingModelChanges();
            UpdateAutomaticLayout(Bounds.Width, Bounds.Height);
            _live2D?.AdvancePointer(elapsed);
            _live2D?.Run(elapsed);
            _parameters.CaptureValues();
        }
    }

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        _renderLoop?.Dispose();

        lock (_modelGate)
        {
            FailPendingOperations(new ObjectDisposedException(nameof(Live2DSurface)));
            ReleaseCurrentModel();

            _renderLoop = null;
            _gl = null;
        }
    }

    public void LookAt(double x, double y, double width, double height)
    {
        if (_live2D is null || width <= 0 || height <= 0)
            return;

        float lookX = (float)(x / width * 2 - 1);
        float lookY = (float)(1 - y / height * 2);
        _live2D.SetLookAt(lookX, lookY);
    }

    public void LookAtCenter()
    {
        _live2D?.SetLookAt(0, 0);
    }

    public void PanBy(double deltaX, double deltaY, double viewportHeight)
    {
        _live2D?.MoveByPixels(deltaX, deltaY, viewportHeight);
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetPosition(float x, float y)
    {
        _live2D?.SetPosition(x, y);
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ZoomBy(double wheelDelta)
    {
        _live2D?.ZoomBy(wheelDelta);
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetZoom(float zoom)
    {
        _live2D?.SetZoom(zoom);
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ResetTransform()
    {
        _live2D?.ResetView();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetFramesPerSecond(int framesPerSecond)
    {
        _framesPerSecond = Math.Clamp(framesPerSecond, 1, 120);
        _renderLoop?.SetFramesPerSecond(_framesPerSecond);
    }

    public void SetRenderingPaused(bool paused)
    {
        _manuallyPaused = paused;
        UpdateRenderLoopState();
    }

    public void SetAutomaticIdle(bool enabled)
    {
        _automaticIdle = enabled;

        if (_model is not null)
            _model.RandomMotion = enabled;
    }

    public void SetOpacity(float opacity)
    {
        _modelOpacity = Math.Clamp(opacity, 0, 1);
        _model?.Renderer?.SetModelColor(1, 1, 1, _modelOpacity);
    }

    internal void SetHostState(
        bool visible,
        bool autoPauseWhenHidden,
        bool releaseModelWhenHidden)
    {
        if (_hostVisible == visible &&
            _autoPauseWhenHidden == autoPauseWhenHidden &&
            _releaseModelWhenHidden == releaseModelWhenHidden)
        {
            return;
        }

        _hostVisible = visible;
        _autoPauseWhenHidden = autoPauseWhenHidden;
        _releaseModelWhenHidden = releaseModelWhenHidden;
        UpdateRenderLoopState();

        if (!_hostVisible && _releaseModelWhenHidden)
            ReleaseWhileHidden();
        else if (_hostVisible)
            ReloadAfterHiddenRelease();
    }

    private void UpdateRenderLoopState()
    {
        bool shouldPause = _manuallyPaused || (_autoPauseWhenHidden && !_hostVisible);
        _renderLoop?.SetPaused(shouldPause);
    }
}
