using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using McKuro.Services;

namespace McKuro.Controls;

/// <summary>
/// Live2D 模型宿主:封装 Sparkle.Live2DView 控件的创建/加载/参数应用,
/// 首页展示与设置页预览共用。仅支持平台(Windows x64)且 Show=true 且 Core 可用时
/// 才实例化内部渲染器 —— 其他平台保持空面板(不触碰 Cubism P/Invoke)。
/// Zoom/PositionX/PositionY/ModelOpacity 为 TwoWay:预览里拖动/滚轮会写回设置 VM 即时保存。
/// </summary>
public sealed class Live2DModelHost : Panel
{
    private Sparkle.Live2DView.Live2DView? _view;
    private string _loadedDir = "";
    private string _loadedName = "";
    private TopLevel? _hookedTopLevel;

    public static readonly StyledProperty<bool> ShowProperty =
        AvaloniaProperty.Register<Live2DModelHost, bool>(nameof(Show));

    public static readonly StyledProperty<string> ModelDirectoryProperty =
        AvaloniaProperty.Register<Live2DModelHost, string>(nameof(ModelDirectory), "");

    public static readonly StyledProperty<string> ModelNameProperty =
        AvaloniaProperty.Register<Live2DModelHost, string>(nameof(ModelName), "");

    public static readonly StyledProperty<float> ZoomProperty =
        AvaloniaProperty.Register<Live2DModelHost, float>(nameof(Zoom), 1f);

    public static readonly StyledProperty<float> PositionXProperty =
        AvaloniaProperty.Register<Live2DModelHost, float>(nameof(PositionX));

    public static readonly StyledProperty<float> PositionYProperty =
        AvaloniaProperty.Register<Live2DModelHost, float>(nameof(PositionY));

    public static readonly StyledProperty<float> ModelOpacityProperty =
        AvaloniaProperty.Register<Live2DModelHost, float>(nameof(ModelOpacity), 1f);

    public static readonly StyledProperty<int> FramesPerSecondProperty =
        AvaloniaProperty.Register<Live2DModelHost, int>(nameof(FramesPerSecond), 30);

    /// <summary>是否允许指针交互(视线跟随/拖动/滚轮缩放)。首页纯展示置 false 且配合
    /// IsHitTestVisible=false,避免透明区域挡住下方按钮;设置预览保持 true。</summary>
    public static readonly StyledProperty<bool> AllowInteractionProperty =
        AvaloniaProperty.Register<Live2DModelHost, bool>(nameof(AllowInteraction), true);

    /// <summary>视线跟随鼠标(Sparkle 的 IsPointerFollowEnabled;与拖动/缩放独立)。
    /// 首页按设置开关绑定;注意跟随需要控件能收到指针事件(IsHitTestVisible 须为 true)。</summary>
    public static readonly StyledProperty<bool> PointerFollowProperty =
        AvaloniaProperty.Register<Live2DModelHost, bool>(nameof(PointerFollow), true);

    public bool AllowInteraction
    {
        get => GetValue(AllowInteractionProperty);
        set => SetValue(AllowInteractionProperty, value);
    }

    public bool PointerFollow
    {
        get => GetValue(PointerFollowProperty);
        set => SetValue(PointerFollowProperty, value);
    }

    public bool Show
    {
        get => GetValue(ShowProperty);
        set => SetValue(ShowProperty, value);
    }

    public string ModelDirectory
    {
        get => GetValue(ModelDirectoryProperty);
        set => SetValue(ModelDirectoryProperty, value);
    }

    public string ModelName
    {
        get => GetValue(ModelNameProperty);
        set => SetValue(ModelNameProperty, value);
    }

    public float Zoom
    {
        get => GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
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

    /// <summary>模型加载失败(文件损坏/Core 缺失等):静默回退为不显示,设置页有状态指引。</summary>
    public event EventHandler<Exception>? ModelLoadFailed;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // 纯展示模式(首页)不参与命中测试,指针事件到不了模型;改为挂窗口级
        // PointerMoved,按位置转发视线目标,让"跟随鼠标"不需要抢占下方点击。
        if (!AllowInteraction)
        {
            _hookedTopLevel = TopLevel.GetTopLevel(this);
            _hookedTopLevel?.AddHandler(
                InputElement.PointerMovedEvent,
                OnTopLevelPointerMoved,
                RoutingStrategies.Bubble,
                handledEventsToo: true);
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_hookedTopLevel is not null)
        {
            _hookedTopLevel.RemoveHandler(InputElement.PointerMovedEvent, OnTopLevelPointerMoved);
            _hookedTopLevel = null;
        }
        base.OnDetachedFromVisualTree(e);
    }

    private void OnTopLevelPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_view is null || !PointerFollow || !IsEffectivelyVisible)
        {
            return;
        }
        var p = e.GetPosition(_view);
        if (p.X >= 0 && p.X <= _view.Bounds.Width && p.Y >= 0 && p.Y <= _view.Bounds.Height)
        {
            System.Console.Error.WriteLine($"[L2D-DBG] forward LookAt {p.X:0},{p.Y:0} (view {_view.Bounds.Width:0}x{_view.Bounds.Height:0})");
            _view.LookAt(p.X, p.Y, _view.Bounds.Width, _view.Bounds.Height);
        }
        else
        {
            _view.LookAtCenter();
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == ShowProperty
            || e.Property == ModelDirectoryProperty
            || e.Property == ModelNameProperty
            || e.Property == FramesPerSecondProperty)
        {
            Update();
        }
        else if (_view is not null)
        {
            if (e.Property == ZoomProperty)
            {
                _view.Zoom = Zoom;
            }
            else if (e.Property == PointerFollowProperty)
            {
                _view.IsPointerFollowEnabled = PointerFollow;
            }
            else if (e.Property == PositionXProperty)
            {
                _view.PositionX = PositionX;
            }
            else if (e.Property == PositionYProperty)
            {
                _view.PositionY = PositionY;
            }
            else if (e.Property == ModelOpacityProperty)
            {
                _view.ModelOpacity = ModelOpacity;
            }
        }
    }

    private void Update()
    {
        var shouldShow = Show
                         && Live2DLocator.IsSupported
                         && !string.IsNullOrWhiteSpace(ModelDirectory)
                         && !string.IsNullOrWhiteSpace(ModelName);
        if (!shouldShow)
        {
            Teardown();
            return;
        }

        Live2DLocator.EnsureDllSearchPath();
        if (_view is null)
        {
            _view = new Sparkle.Live2DView.Live2DView
            {
                CanDrag = AllowInteraction,
                CanZoom = AllowInteraction,
                IsPointerFollowEnabled = PointerFollow,
                FramesPerSecond = FramesPerSecond,
                AutoPauseWhenHidden = true,
            };
            _view.ModelLoadFailed += (_, e) =>
            {
                System.Diagnostics.Debug.WriteLine($"[Live2D] {e.ModelName} 加载失败: {e.Exception.Message}");
                ModelLoadFailed?.Invoke(this, e.Exception);
            };
            Children.Add(_view);
        }

        _view.FramesPerSecond = FramesPerSecond;
        if (_loadedDir != ModelDirectory || _loadedName != ModelName)
        {
            _loadedDir = ModelDirectory;
            _loadedName = ModelName;
            _ = _view.LoadModelAsync(ModelDirectory, ModelName);
        }
        _view.Zoom = Zoom;
        _view.PositionX = PositionX;
        _view.PositionY = PositionY;
        _view.ModelOpacity = ModelOpacity;
    }

    private void Teardown()
    {
        if (_view is null)
        {
            return;
        }
        Children.Remove(_view);
        _view = null;
        _loadedDir = "";
        _loadedName = "";
    }
}
