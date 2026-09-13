using Live2DCSharpSDK.App;
using Live2DCSharpSDK.OpenGL;

namespace Sparkle.Live2DView.Internal;

internal sealed class InteractiveLive2DDelegate : LAppDelegateOpenGL
{
    private float _x;
    private float _y;
    private float _zoom = 1f;
    private float _lookX;
    private float _lookY;
    private float _targetLookX;
    private float _targetLookY;
    private float _fitScale = 1f;
    private float _fitX;
    private float _fitY;

    public InteractiveLive2DDelegate(OpenGLApi gl)
        : base(gl)
    {
    }

    public float X => _x;
    public float Y => _y;
    public float Zoom => _zoom;

    public override void OnUpdatePre()
    {
        var matrix = Live2dManager.ViewMatrix;
        var originalX = matrix.GetTranslateX();
        var originalY = matrix.GetTranslateY();

        float scale = _zoom * _fitScale;
        matrix.ScaleRelative(scale, scale);

        matrix.Translate(originalX + _x + _fitX, originalY + _y + _fitY);
    }

    public void AdvancePointer(float elapsed)
    {
        float amount = 1 - MathF.Exp(-12 * elapsed);
        _lookX += (_targetLookX - _lookX) * amount;
        _lookY += (_targetLookY - _lookY) * amount;
    }

    public void SetLookAt(float x, float y)
    {
        _targetLookX = Math.Clamp(x, -1, 1);
        _targetLookY = Math.Clamp(y, -1, 1);
    }

    public void ApplyPointerParameters(LAppModel model)
    {
        model.Model.AddParameterValue(model.IdParamAngleX, _lookX * 30);
        model.Model.AddParameterValue(model.IdParamAngleY, _lookY * 30);
        model.Model.AddParameterValue(model.IdParamAngleZ, _lookX * _lookY * -30);
        model.Model.AddParameterValue(model.IdParamBodyAngleX, _lookX * 10);
        model.Model.AddParameterValue(model.IdParamEyeBallX, _lookX);
        model.Model.AddParameterValue(model.IdParamEyeBallY, _lookY);
    }

    public void MoveByPixels(double dx, double dy, double viewportHeight)
    {
        if (viewportHeight <= 0) return;

        _x += (float)(dx * 2d / viewportHeight);
        _y -= (float)(dy * 2d / viewportHeight);
    }

    public void SetPosition(float x, float y)
    {
        _x = Math.Clamp(x, -2f, 2f);
        _y = Math.Clamp(y, -2f, 2f);
    }

    public void ZoomBy(double wheelDelta)
    {
        var step = wheelDelta > 0 ? 1.12f : 1f / 1.12f;
        SetZoom(_zoom * step);
    }

    public void SetZoom(float zoom) => _zoom = Math.Clamp(zoom, 0.5f, 3f);

    public void SetAutomaticLayout(float scale, float x, float y)
    {
        _fitScale = scale;
        _fitX = x;
        _fitY = y;
    }

    public void ResetView()
    {
        _x = 0f;
        _y = 0f;
        _zoom = 1f;
        _lookX = 0f;
        _lookY = 0f;
        _targetLookX = 0f;
        _targetLookY = 0f;
    }
}
