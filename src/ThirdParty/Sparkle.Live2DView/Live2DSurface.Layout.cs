using Avalonia;

namespace Sparkle.Live2DView;

public sealed partial class Live2DSurface
{
    private Live2DModelFit _modelFit = Live2DModelFit.Uniform;
    private Thickness _modelPadding;
    private double _layoutWidth = double.NaN;
    private double _layoutHeight = double.NaN;
    private bool _layoutDirty = true;

    internal void SetModelLayout(Live2DModelFit fit, Thickness padding)
    {
        if (_modelFit == fit && _modelPadding.Equals(padding))
            return;

        _modelFit = fit;
        _modelPadding = padding;
        _layoutDirty = true;
        RequestNextFrameRendering();
    }

    private void UpdateAutomaticLayout(double width, double height)
    {
        if (_live2D is null || _model is null || width <= 0 || height <= 0)
            return;

        if (!_layoutDirty && width.Equals(_layoutWidth) && height.Equals(_layoutHeight))
            return;

        if (_modelFit == Live2DModelFit.None)
        {
            _live2D.SetAutomaticLayout(1, 0, 0);
            _layoutWidth = width;
            _layoutHeight = height;
            _layoutDirty = false;
            return;
        }

        double left = Math.Max(0, _modelPadding.Left);
        double top = Math.Max(0, _modelPadding.Top);
        double right = Math.Max(0, _modelPadding.Right);
        double bottom = Math.Max(0, _modelPadding.Bottom);

        double availableWidth = Math.Max(1, width - left - right);
        double availableHeight = Math.Max(1, height - top - bottom);

        float canvasWidth = _model.Model.GetCanvasWidth();
        float canvasHeight = _model.Model.GetCanvasHeight();
        if (canvasWidth <= 0 || canvasHeight <= 0)
            return;

        double modelAspect = canvasWidth / canvasHeight;
        bool sdkFitsWidth = canvasWidth > 1 && width < height;
        double baseWidth = sdkFitsWidth ? width : height * modelAspect;
        double baseHeight = sdkFitsWidth ? width / modelAspect : height;

        double scaleX = availableWidth / baseWidth;
        double scaleY = availableHeight / baseHeight;
        double fitScale = _modelFit == Live2DModelFit.Uniform
            ? Math.Min(scaleX, scaleY)
            : Math.Max(scaleX, scaleY);

        double contentCenterX = left + availableWidth / 2;
        double contentCenterY = top + availableHeight / 2;
        double offsetX = (contentCenterX - width / 2) * 2 / height;
        double offsetY = (height / 2 - contentCenterY) * 2 / height;

        _live2D.SetAutomaticLayout(
            (float)Math.Clamp(fitScale, 0.01, 100),
            (float)offsetX,
            (float)offsetY);

        _layoutWidth = width;
        _layoutHeight = height;
        _layoutDirty = false;
    }

    private void InvalidateModelLayout()
    {
        _layoutDirty = true;
    }
}
