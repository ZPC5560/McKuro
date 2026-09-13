using Avalonia;

namespace Sparkle.Live2DView;

public sealed partial class Live2DView
{
    public IReadOnlyList<Live2DHitArea> HitAreas => _surface.HitAreas;

    public event EventHandler<Live2DHitEventArgs>? HitAreaPressed;

    public bool HitTest(string hitAreaName, Point point)
    {
        return HitTest(hitAreaName, point.X, point.Y);
    }

    public bool HitTest(string hitAreaName, double x, double y)
    {
        return _surface.HitTest(
            hitAreaName,
            x,
            y,
            _inputLayer.Bounds.Width,
            _inputLayer.Bounds.Height);
    }

    public IReadOnlyList<Live2DHitArea> HitTestAll(Point point)
    {
        return HitTestAll(point.X, point.Y);
    }

    public IReadOnlyList<Live2DHitArea> HitTestAll(double x, double y)
    {
        return _surface.HitTestAll(
            x,
            y,
            _inputLayer.Bounds.Width,
            _inputLayer.Bounds.Height);
    }
}
