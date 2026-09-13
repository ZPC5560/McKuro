using Avalonia.OpenGL.Controls;
using Avalonia.Threading;

namespace Sparkle.Live2DView.Internal;

// DispatcherTimer 足够用了，绘制仍然发生在 Avalonia 的渲染线程。
internal sealed class RenderLoop : IDisposable
{
    private readonly OpenGlControlBase _control;
    private readonly DispatcherTimer _timer;

    public RenderLoop(OpenGlControlBase control, int framesPerSecond = 60)
    {
        _control = control;
        _timer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(1000d / framesPerSecond),
            DispatcherPriority.Render,
            (_, _) => _control.RequestNextFrameRendering());
    }

    public bool IsPaused { get; private set; }

    public void SetFramesPerSecond(int framesPerSecond)
    {
        framesPerSecond = Math.Clamp(framesPerSecond, 1, 120);
        _timer.Interval = TimeSpan.FromMilliseconds(1000d / framesPerSecond);
    }

    public void SetPaused(bool paused)
    {
        if (IsPaused == paused)
            return;

        IsPaused = paused;
        if (paused) _timer.Stop();
        else _timer.Start();
    }

    public void Start() => _timer.Start();
    public void Dispose() => _timer.Stop();
}
