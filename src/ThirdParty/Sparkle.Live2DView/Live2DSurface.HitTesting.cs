using Avalonia.Controls;

namespace Sparkle.Live2DView;

public sealed partial class Live2DSurface
{
    public bool HitTest(string hitAreaName, double x, double y, double width, double height)
    {
        Live2DHitArea? hitArea = _hitAreas.FirstOrDefault(
            item => string.Equals(item.Name, hitAreaName, StringComparison.OrdinalIgnoreCase));

        if (hitArea is null)
            return false;

        lock (_modelGate)
        {
            return TryMapToModel(x, y, width, height, out float modelX, out float modelY) &&
                   _model!.HitTest(hitArea.Name, modelX, modelY);
        }
    }

    public IReadOnlyList<Live2DHitArea> HitTestAll(
        double x,
        double y,
        double width,
        double height)
    {
        lock (_modelGate)
        {
            if (!TryMapToModel(x, y, width, height, out float modelX, out float modelY))
                return [];

            var hits = new List<Live2DHitArea>();
            foreach (Live2DHitArea hitArea in _hitAreas)
            {
                if (_model!.HitTest(hitArea.Name, modelX, modelY))
                    hits.Add(hitArea);
            }

            return hits;
        }
    }

    private bool TryMapToModel(
        double x,
        double y,
        double width,
        double height,
        out float modelX,
        out float modelY)
    {
        modelX = 0;
        modelY = 0;

        if (_model is null || _live2D is null || width <= 0 || height <= 0)
            return false;

        double localX = x / width * Bounds.Width;
        double localY = y / height * Bounds.Height;
        double scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? _renderScale;

        float screenX = _live2D.View.TransformScreenX((float)(localX * scale));
        float screenY = _live2D.View.TransformScreenY((float)(localY * scale));
        var view = _live2D.Live2dManager.ViewMatrix;

        modelX = view.InvertTransformX(screenX);
        modelY = view.InvertTransformY(screenY);
        return true;
    }
}
