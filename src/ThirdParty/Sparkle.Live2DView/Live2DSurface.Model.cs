using System.Collections.Concurrent;
using System.Diagnostics;
using Avalonia;
using Avalonia.OpenGL;
using Live2DCSharpSDK.App;
using Sparkle.Live2DView.Internal;

namespace Sparkle.Live2DView;

public sealed partial class Live2DSurface
{
    public static readonly StyledProperty<string> ModelDirectoryProperty =
        AvaloniaProperty.Register<Live2DSurface, string>(
            nameof(ModelDirectory),
            "Assets/Models/Hiyori");

    public static readonly StyledProperty<string> ModelNameProperty =
        AvaloniaProperty.Register<Live2DSurface, string>(nameof(ModelName), "Hiyori");

    private readonly ConcurrentQueue<PendingModelChange> _pendingModelChanges = new();
    private GlInterface? _gl;
    private IReadOnlyList<Live2DHitArea> _hitAreas = [];
    private int _activeLoads;
    private bool _automaticUnloadQueued;
    private bool _reloadWhenVisible;

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

    public bool IsLoading => Volatile.Read(ref _activeLoads) > 0;
    public IReadOnlyList<Live2DHitArea> HitAreas => _hitAreas;

    public event EventHandler<Live2DModelEventArgs>? ModelLoading;
    public event EventHandler? ModelLoaded;
    public event EventHandler<Live2DModelLoadFailedEventArgs>? ModelLoadFailed;
    public event EventHandler<Live2DModelEventArgs>? ModelUnloaded;
    internal event EventHandler? LoadingStateChanged;

    public Task LoadModelAsync(string modelDirectory, string modelName)
    {
        return LoadModelAsync(modelDirectory, modelName, CancellationToken.None);
    }

    public Task LoadModelAsync(
        string modelDirectory,
        string modelName,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelName);

        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled(cancellationToken);

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        QueueLoad(modelDirectory, modelName, cancellationToken, completion);
        return completion.Task;
    }

    public Task ReloadModelAsync()
    {
        return ReloadModelAsync(CancellationToken.None);
    }

    public Task ReloadModelAsync(CancellationToken cancellationToken)
    {
        return LoadModelAsync(ModelDirectory, ModelName, cancellationToken);
    }

    public Task UnloadModelAsync()
    {
        _reloadWhenVisible = false;

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingModelChanges.Enqueue(PendingModelChange.Unload(completion));
        RequestNextFrameRendering();
        return completion.Task;
    }

    private void QueueLoad(
        string modelDirectory,
        string modelName,
        CancellationToken cancellationToken,
        TaskCompletionSource? completion)
    {
        BeginLoading();
        _pendingModelChanges.Enqueue(
            PendingModelChange.Load(
                modelDirectory,
                modelName,
                cancellationToken,
                completion));

        if (_gl is not null)
            RequestNextFrameRendering();
    }

    private void LoadInitialModel()
    {
        BeginLoading();
        LoadPendingModel(
            PendingModelChange.Load(
                ModelDirectory,
                ModelName,
                CancellationToken.None,
                null));
    }

    private void ProcessPendingModelChanges()
    {
        while (_pendingModelChanges.TryDequeue(out PendingModelChange? change))
        {
            if (change.Kind == ModelChangeKind.Load)
                LoadPendingModel(change);
            else
                UnloadModel(change);
        }
    }

    private void LoadPendingModel(PendingModelChange change)
    {
        LoadedModel? loaded = null;
        bool success = false;

        try
        {
            change.CancellationToken.ThrowIfCancellationRequested();
            ModelLoading?.Invoke(
                this,
                new Live2DModelEventArgs(change.ModelDirectory, change.ModelName));

            loaded = OpenModel(change.ModelDirectory, change.ModelName);
            change.CancellationToken.ThrowIfCancellationRequested();

            UseModel(loaded, change.ModelDirectory, change.ModelName);
            loaded = null;
            success = true;
            change.Completion?.TrySetResult();
        }
        catch (OperationCanceledException)
        {
            change.Completion?.TrySetCanceled(change.CancellationToken);
        }
        catch (Exception exception)
        {
            ModelLoadFailed?.Invoke(
                this,
                new Live2DModelLoadFailedEventArgs(
                    change.ModelDirectory,
                    change.ModelName,
                    exception));
            change.Completion?.TrySetException(exception);
        }
        finally
        {
            DisposeModel(loaded?.Live2D);
            EndLoading();
        }

        if (success)
        {
            ModelLoaded?.Invoke(this, EventArgs.Empty);
            if (!_hostVisible && _releaseModelWhenHidden)
                ReleaseWhileHidden();
        }
    }

    private void UnloadModel(PendingModelChange change)
    {
        _automaticUnloadQueued = false;
        ReleaseCurrentModel();
        change.Completion?.TrySetResult();
    }

    private LoadedModel OpenModel(string modelDirectory, string modelName)
    {
        if (_gl is null)
            throw new InvalidOperationException("OpenGL 尚未初始化。");

        string directory = Path.GetFullPath(modelDirectory, AppContext.BaseDirectory);
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException($"模型目录不存在：{directory}");

        string modelJson = FindModelJson(directory, modelName);
        if (!File.Exists(modelJson))
            throw new FileNotFoundException("找不到模型配置文件。", modelJson);

        var live2D = new InteractiveLive2DDelegate(new AvaloniaOpenGlApi(this, _gl))
        {
            BGColor = new(0, 0, 0, 0)
        };

        try
        {
            CopyTransform(_live2D, live2D);

            LAppModel model = live2D.Live2dManager.LoadModel(directory, modelName);
            model.RandomMotion = _automaticIdle;
            model.Renderer?.SetModelColor(1, 1, 1, _modelOpacity);

            return new LoadedModel(live2D, model, HitAreaReader.Read(modelJson));
        }
        catch
        {
            DisposeModel(live2D);
            throw;
        }
    }

    private void UseModel(LoadedModel loaded, string modelDirectory, string modelName)
    {
        InteractiveLive2DDelegate? previousLive2D = _live2D;
        LAppModel? previousModel = _model;

        try
        {
            _parameters.Attach(loaded.Model, loaded.Live2D.ApplyPointerParameters);
        }
        catch
        {
            if (previousModel is not null && previousLive2D is not null)
                _parameters.Attach(previousModel, previousLive2D.ApplyPointerParameters);

            throw;
        }

        _live2D = loaded.Live2D;
        _model = loaded.Model;
        _hitAreas = loaded.HitAreas;
        _currentExpression = null;
        _nextMotion = 0;
        InvalidateModelLayout();

        ModelDirectory = modelDirectory;
        ModelName = modelName;

        DisposeModel(previousLive2D);
    }

    private void ReleaseCurrentModel()
    {
        if (_model is null && _live2D is null)
            return;

        var eventArgs = new Live2DModelEventArgs(ModelDirectory, ModelName);
        InteractiveLive2DDelegate? live2D = _live2D;

        _parameters.Detach();
        _live2D = null;
        _model = null;
        _hitAreas = [];
        _currentExpression = null;
        _nextMotion = 0;

        DisposeModel(live2D);
        ModelUnloaded?.Invoke(this, eventArgs);
    }

    private void FailPendingOperations(Exception exception)
    {
        while (_pendingModelChanges.TryDequeue(out PendingModelChange? change))
        {
            if (change.Kind == ModelChangeKind.Load)
                EndLoading();
            else
                _automaticUnloadQueued = false;

            change.Completion?.TrySetException(exception);
        }
    }

    private void BeginLoading()
    {
        if (Interlocked.Increment(ref _activeLoads) == 1)
            LoadingStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void EndLoading()
    {
        int remaining = Interlocked.Decrement(ref _activeLoads);
        if (remaining == 0)
            LoadingStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ReleaseWhileHidden()
    {
        if (_model is null || _automaticUnloadQueued)
            return;

        _automaticUnloadQueued = true;
        _reloadWhenVisible = true;
        _pendingModelChanges.Enqueue(PendingModelChange.Unload(null));
        RequestNextFrameRendering();
    }

    private void ReloadAfterHiddenRelease()
    {
        if (!_reloadWhenVisible)
            return;

        _reloadWhenVisible = false;
        QueueLoad(ModelDirectory, ModelName, CancellationToken.None, null);
    }

    private static void CopyTransform(
        InteractiveLive2DDelegate? source,
        InteractiveLive2DDelegate destination)
    {
        if (source is null)
            return;

        destination.SetPosition(source.X, source.Y);
        destination.SetZoom(source.Zoom);
    }

    private static void DisposeModel(InteractiveLive2DDelegate? live2D)
    {
        if (live2D is null)
            return;

        try
        {
            live2D.Dispose();
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[Sparkle.Live2DView] 释放模型时出错：{exception}");
        }
    }

    private static string FindModelJson(string directory, string modelName)
    {
        string path = Path.Combine(directory, modelName);
        return File.Exists(path)
            ? path
            : Path.Combine(directory, $"{modelName}.model3.json");
    }

    private sealed class LoadedModel
    {
        public LoadedModel(
            InteractiveLive2DDelegate live2D,
            LAppModel model,
            IReadOnlyList<Live2DHitArea> hitAreas)
        {
            Live2D = live2D;
            Model = model;
            HitAreas = hitAreas;
        }

        public InteractiveLive2DDelegate Live2D { get; }
        public LAppModel Model { get; }
        public IReadOnlyList<Live2DHitArea> HitAreas { get; }
    }
}
