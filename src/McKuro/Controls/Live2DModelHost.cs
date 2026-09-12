using Avalonia;
using Avalonia.Controls;
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

    public bool AllowInteraction
    {
        get => GetValue(AllowInteractionProperty);
        set => SetValue(AllowInteractionProperty, value);
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
