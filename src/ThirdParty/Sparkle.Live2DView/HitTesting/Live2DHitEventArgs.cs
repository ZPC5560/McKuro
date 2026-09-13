using Avalonia;

namespace Sparkle.Live2DView;

public sealed class Live2DHitEventArgs : EventArgs
{
    public Live2DHitEventArgs(Point position, IReadOnlyList<Live2DHitArea> hitAreas)
    {
        Position = position;
        HitAreas = hitAreas;
    }

    public Point Position { get; }
    public IReadOnlyList<Live2DHitArea> HitAreas { get; }
    public Live2DHitArea PrimaryHitArea => HitAreas[0];
}
