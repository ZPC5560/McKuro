using Avalonia;
using Avalonia.Data;

namespace Sparkle.Live2DView;

public sealed partial class Live2DView
{
    public static readonly StyledProperty<bool> CanDragProperty =
        AvaloniaProperty.Register<Live2DView, bool>(nameof(CanDrag), true);

    public static readonly StyledProperty<bool> CanZoomProperty =
        AvaloniaProperty.Register<Live2DView, bool>(nameof(CanZoom), true);

    public static readonly StyledProperty<float> PositionXProperty =
        AvaloniaProperty.Register<Live2DView, float>(
            nameof(PositionX),
            0,
            defaultBindingMode: BindingMode.TwoWay,
            validate: float.IsFinite,
            coerce: (_, value) => Math.Clamp(value, -2, 2));

    public static readonly StyledProperty<float> PositionYProperty =
        AvaloniaProperty.Register<Live2DView, float>(
            nameof(PositionY),
            0,
            defaultBindingMode: BindingMode.TwoWay,
            validate: float.IsFinite,
            coerce: (_, value) => Math.Clamp(value, -2, 2));

    public static readonly StyledProperty<float> ZoomProperty =
        AvaloniaProperty.Register<Live2DView, float>(
            nameof(Zoom),
            1,
            defaultBindingMode: BindingMode.TwoWay,
            validate: float.IsFinite,
            coerce: (_, value) => Math.Clamp(value, 0.5f, 3));

    public static readonly StyledProperty<float> ModelOpacityProperty =
        AvaloniaProperty.Register<Live2DView, float>(
            nameof(ModelOpacity),
            1,
            defaultBindingMode: BindingMode.TwoWay,
            validate: float.IsFinite,
            coerce: (_, value) => Math.Clamp(value, 0, 1));

    public static readonly StyledProperty<int> FramesPerSecondProperty =
        AvaloniaProperty.Register<Live2DView, int>(
            nameof(FramesPerSecond),
            60,
            defaultBindingMode: BindingMode.TwoWay,
            coerce: (_, value) => Math.Clamp(value, 1, 120));

    public static readonly StyledProperty<Live2DModelFit> ModelFitProperty =
        AvaloniaProperty.Register<Live2DView, Live2DModelFit>(
            nameof(ModelFit),
            Live2DModelFit.Uniform);

    public static readonly StyledProperty<Thickness> ModelPaddingProperty =
        AvaloniaProperty.Register<Live2DView, Thickness>(nameof(ModelPadding));

    public static readonly StyledProperty<bool> AutoPauseWhenHiddenProperty =
        AvaloniaProperty.Register<Live2DView, bool>(nameof(AutoPauseWhenHidden), true);

    public static readonly StyledProperty<bool> ReleaseModelWhenHiddenProperty =
        AvaloniaProperty.Register<Live2DView, bool>(nameof(ReleaseModelWhenHidden));

    private bool _updatingPropertiesFromSurface;

    public bool CanDrag
    {
        get => GetValue(CanDragProperty);
        set => SetValue(CanDragProperty, value);
    }

    public bool CanZoom
    {
        get => GetValue(CanZoomProperty);
        set => SetValue(CanZoomProperty, value);
    }

    // 这两个名字在早期版本里公开过，暂时保留兼容。
    public bool IsDragEnabled
    {
        get => CanDrag;
        set => CanDrag = value;
    }

    public bool IsWheelZoomEnabled
    {
        get => CanZoom;
        set => CanZoom = value;
    }

    public float PositionX
    {
        get => GetValue(PositionXProperty);
        set => SetValue(PositionXProperty, value);
    }

    public float PositionY
    {
        get => GetValue(PositionYProperty);
        set => SetValue(PositionYProperty, value);
    }

    public float Zoom
    {
        get => GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    public float ModelOpacity
    {
        get => GetValue(ModelOpacityProperty);
        set => SetValue(ModelOpacityProperty, value);
    }

    public int FramesPerSecond
    {
        get => GetValue(FramesPerSecondProperty);
        set => SetValue(FramesPerSecondProperty, value);
    }

    public Live2DModelFit ModelFit
    {
        get => GetValue(ModelFitProperty);
        set => SetValue(ModelFitProperty, value);
    }

    public Thickness ModelPadding
    {
        get => GetValue(ModelPaddingProperty);
        set => SetValue(ModelPaddingProperty, value);
    }

    public bool AutoPauseWhenHidden
    {
        get => GetValue(AutoPauseWhenHiddenProperty);
        set => SetValue(AutoPauseWhenHiddenProperty, value);
    }

    public bool ReleaseModelWhenHidden
    {
        get => GetValue(ReleaseModelWhenHiddenProperty);
        set => SetValue(ReleaseModelWhenHiddenProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (_updatingPropertiesFromSurface)
            return;

        if (change.Property == ModelFitProperty || change.Property == ModelPaddingProperty)
        {
            _surface.SetModelLayout(ModelFit, ModelPadding);
            return;
        }

        if (change.Property == AutoPauseWhenHiddenProperty ||
            change.Property == ReleaseModelWhenHiddenProperty ||
            change.Property == IsVisibleProperty)
        {
            UpdateSurfaceActivity();
            return;
        }

        if (!_surface.IsModelLoaded)
            return;

        if (change.Property == PositionXProperty || change.Property == PositionYProperty)
            _surface.SetPosition(PositionX, PositionY);
        else if (change.Property == ZoomProperty)
            _surface.SetZoom(Zoom);
        else if (change.Property == ModelOpacityProperty)
            _surface.SetOpacity(ModelOpacity);
        else if (change.Property == FramesPerSecondProperty)
            _surface.SetFramesPerSecond(FramesPerSecond);
    }

    private void ApplyDisplayProperties()
    {
        // 先捕获当前属性值再调用 surface:SetPosition/SetZoom 会触发 ViewChanged,
        // 把视图属性从 surface 同步回去(模型刚加载时全是默认值),后续再读属性拿到的
        // 是被重置的值,宿主设置的缩放/位置/不透明度会在模型加载完成后被吞掉。
        var zoom = Zoom;
        var positionX = PositionX;
        var positionY = PositionY;
        var opacity = ModelOpacity;
        var fps = FramesPerSecond;
        _surface.SetPosition(positionX, positionY);
        _surface.SetZoom(zoom);
        _surface.SetOpacity(opacity);
        _surface.SetFramesPerSecond(fps);
        _surface.SetModelLayout(ModelFit, ModelPadding);
        UpdateSurfaceActivity();
    }

    private void SurfaceViewChanged(object? sender, EventArgs e)
    {
        _updatingPropertiesFromSurface = true;

        try
        {
            SetCurrentValue(PositionXProperty, _surface.PositionX);
            SetCurrentValue(PositionYProperty, _surface.PositionY);
            SetCurrentValue(ZoomProperty, _surface.Zoom);
        }
        finally
        {
            _updatingPropertiesFromSurface = false;
        }
    }

}
