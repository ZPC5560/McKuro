using System.Collections.Concurrent;
using System.Diagnostics;
using Avalonia;
using Avalonia.OpenGL;
using Live2DCSharpSDK.App;
using Live2DCSharpSDK.Framework.Model;
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
    private readonly AutoplayMotionDriver _autoplay = new();
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
            DisposeModel(loaded?.Live2D, loaded?.Model);
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

        // 在 try 外声明:catch 里要按 model 的原生指针清 shim 缓存(见 DisposeModel 说明)。
        LAppModel? model = null;
        try
        {
            CopyTransform(_live2D, live2D);

            // 必须在 LoadModel 之前声明尾巴图层:渲染器是在 LAppModel 构造函数里创建的,
            // 那时还没有顶点可用于标定。先声明 -> 换成可注入的渲染器 -> 加载完再标定。
            (IReadOnlyList<string> tailIds, IReadOnlyList<string> bodyIds) =
                Live2DTailSway.ReadLayerIds(ReadExportStatus(modelJson));
            live2D.SetTailDrawableIds(tailIds);

            model = live2D.Live2dManager.LoadModel(directory, modelName);
            RepairDrawableRenderOrder(model);

            // 把"语义角色 → 真实参数 Id"的映射交给渲染代理,并挑出该由我们接管的角色。
            //
            // 判据必须看【模型真实的参数表】而不是只信 cdi3 的显示名:
            // SDK 的呼吸/指针跟随写死官方参数名(ParamAngleX、ParamBreath…),
            // 第三方导出模型没有这些参数,写入会静默落进"不存在参数"旁路表 —— 模型因此静止。
            // 而对官方模型(Hiyori 等)这些参数真实存在,SDK 本来就在驱动,不能再叠一层。
            live2D.SetParameterMap(ReadParameterMap(model));
            live2D.SetSdkHandledRoles(FindSdkHandledRoles(model.Model));

            // 顶点已就绪,标定尾巴的根/权重/半径(找不到尾巴图层则自动不启用)。
            if (tailIds.Count > 0)
            {
                live2D.ConfigureTailSway(model, bodyIds);
            }

            model.RandomMotion = _automaticIdle;
            model.Renderer?.SetModelColor(1, 1, 1, _modelOpacity);

            return new LoadedModel(live2D, model, HitAreaReader.Read(modelJson));
        }
        catch
        {
            DisposeModel(live2D, model);
            throw;
        }
    }

    /// <summary>
    /// 读取第三方导出器写在模型目录里的 <c>export-status.json</c>(没有则返回 null)。
    ///
    /// <para>该文件保存了「源图层名 → moc3 drawable Id」的映射 —— 这是唯一能认出尾巴图层的地方:
    /// moc3 自己的 drawable Id 已被截断成 <c>ArtMeshPL_layer_000_______..._52b1f8aaa8b8</c>,
    /// 不含任何可读信息。</para>
    /// </summary>
    private static string? ReadExportStatus(string modelJson)
    {
        try
        {
            string? directory = Path.GetDirectoryName(modelJson);
            if (string.IsNullOrEmpty(directory))
            {
                return null;
            }

            string path = Path.Combine(directory, "export-status.json");
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[Sparkle.Live2DView] 读取 export-status.json 失败，跳过尾巴摆动：{exception.Message}");
            return null;
        }
    }

    /// <summary>
    /// 修复 drawable 的 renderOrder(用于兼容"导出器未写 renderOrder"的 moc3)。
    ///
    /// 背景:官方 Cubism 渲染器的排序算法是槽位赋值 ——
    ///     sortedDrawableIndexList[renderOrder[i]] = i
    /// 因此 renderOrder 必须是 0..N-1 的排列。部分第三方导出工具(如把分层 PSD 转 Cubism 的工具)
    /// 会把整张数组写成全 0;此时槽位表退化成"只有最后一个索引 + 其余 0"，
    /// 模型只画出 drawable[0] 与 drawable[N-1](实机表现为"只剩一件饰品和一条尾巴")。
    ///
    /// 处理:moc3 的 drawable 数组本身已按源工程图层顺序排列，即数组下标顺序就是正确的绘制顺序，
    /// 故把 renderOrder 归一化为恒等排列即可。仅当数组不是合法排列时才改写，正常模型零影响。
    /// renderOrders 指向 Core 的 model 缓冲，写入后可跨 csmUpdateModel 保持(已实测)。
    /// </summary>
    private static unsafe void RepairDrawableRenderOrder(LAppModel model)
    {
        CubismModel coreModel = model.Model;
        int count = coreModel.GetDrawableCount();
        if (count == 0)
        {
            return;
        }

        Int32* renderOrders = coreModel.GetDrawableRenderOrders();
        if (renderOrders == null)
        {
            return;
        }

        // 合法性判定:必须是 0..count-1 的排列(既查范围也查重复)。
        var current = new int[count];
        for (int i = 0; i < count; i++)
        {
            current[i] = renderOrders[i];
        }

        if (Live2DRenderOrder.IsValidPermutation(current))
        {
            return;
        }

        Debug.WriteLine(
            $"[Sparkle.Live2DView] renderOrder 不是合法排列(drawable={count}, 首项={renderOrders[0]})，"
            + "已归一化为恒等顺序以保证完整绘制。");

        for (int i = 0; i < count; i++)
        {
            renderOrders[i] = i;
        }
    }

    /// <summary>
    /// 找出「已由 SDK 自己驱动」的语义角色。
    ///
    /// <para><b>模型级判定</b>:只要该模型具备<b>任意一个</b> SDK 硬编码的官方参数
    /// (<c>ParamAngleX</c>、<c>ParamBodyAngleX</c>、<c>ParamEyeBallX/Y</c>、<c>ParamBreath</c>、
    /// <c>ParamEyeLOpen/ROpen</c>),就认定它是"官方命名"的模型 —— SDK 的呼吸/指针跟随/眨眼本来
    /// 就在工作,程序化待机应<b>整体不介入</b>,保证官方模型(Hiyori 等)行为与改造前完全一致。</para>
    ///
    /// <para>反之,第三方「分层 PSD → Cubism」导出模型的这些参数<b>一个都不存在</b>,
    /// SDK 写入全部落进"不存在参数"旁路表(模型因此静止),此时返回空集 —— 由程序化待机全面接管。</para>
    /// </summary>
    private static IReadOnlyList<Live2DSemanticParameter> FindSdkHandledRoles(CubismModel model)
    {
        foreach (Live2DSemanticParameter role in Live2DParameterMap.AllRoles)
        {
            if (Live2DParameterMap.OfficialId(role) is { } official && IsRealParameter(model, official))
            {
                return Live2DParameterMap.AllRoles;
            }
        }

        return [];
    }

    /// <summary>
    /// 判断 Id 是否为本模型真实存在的参数。
    ///
    /// <para>不能只看 <c>GetParameterIndex</c> 是否返回有效下标 —— 它对未知 Id 会<b>合成</b>一个
    /// 越界下标并登记到"不存在参数"表里(见 <c>CubismModel.GetParameterIndex</c>),
    /// 因此必须再用 <c>GetParameterCount</c> 排除。</para>
    /// </summary>
    private static bool IsRealParameter(CubismModel model, string id)
    {
        int index = model.GetParameterIndex(id);
        return index >= 0 && index < model.GetParameterCount();
    }

    /// <summary>
    /// 从模型的 <c>FileReferences.DisplayInfo</c>(<c>*.cdi3.json</c>)读取语义参数映射。
    /// 读不到时返回空映射 —— 此时行为退回改造前(不做任何额外写入),不会让加载失败。
    /// </summary>
    private static Live2DParameterMap ReadParameterMap(LAppModel model)
    {
        try
        {
            string? displayInfo = model._modelSetting.FileReferences?.DisplayInfo;
            if (string.IsNullOrWhiteSpace(displayInfo))
            {
                return Live2DParameterMap.Empty;
            }

            string path = Path.GetFullPath(model._modelHomeDir + displayInfo);
            return File.Exists(path)
                ? Live2DParameterMap.FromDisplayInfoJson(File.ReadAllText(path))
                : Live2DParameterMap.Empty;
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[Sparkle.Live2DView] 读取 DisplayInfo 失败，跳过语义参数映射：{exception.Message}");
            return Live2DParameterMap.Empty;
        }
    }

    private void UseModel(LoadedModel loaded, string modelDirectory, string modelName)
    {
        InteractiveLive2DDelegate? previousLive2D = _live2D;
        LAppModel? previousModel = _model;

        // 先挂 autoplay 并行层(等价于 SDK 里"先跑动作、再叠加指针注视"的顺序),
        // 再挂参数控制器(指针注视 + 宿主参数覆写),后者可覆盖动作写下的值。
        _autoplay.Attach(loaded.Model);

        try
        {
            _parameters.Attach(loaded.Model, loaded.Live2D.ApplyPointerParameters);
        }
        catch
        {
            if (previousModel is not null && previousLive2D is not null)
            {
                _parameters.Attach(previousModel, previousLive2D.ApplyPointerParameters);
                _autoplay.Attach(previousModel);
            }
            else
            {
                _autoplay.Detach();
            }

            throw;
        }

        // 已由 autoplay 并行层接管动作的模型(第三方导出),关掉 SDK 的随机 Idle:
        // 否则 LAppModel.Update() 在动作队列为空时会随机起 Idle 组里的一条,
        // 和我们并行驱动的层抢同一批参数。
        loaded.Model.RandomMotion = _automaticIdle && _autoplay.LayerCount == 0;

        _live2D = loaded.Live2D;
        _model = loaded.Model;
        _hitAreas = loaded.HitAreas;
        _currentExpression = null;
        _nextMotion = 0;
        InvalidateModelLayout();

        ModelDirectory = modelDirectory;
        ModelName = modelName;

        DisposeModel(previousLive2D, previousModel);
    }

    private void ReleaseCurrentModel()
    {
        if (_model is null && _live2D is null)
            return;

        var eventArgs = new Live2DModelEventArgs(ModelDirectory, ModelName);
        InteractiveLive2DDelegate? live2D = _live2D;
        // 先留住要释放的模型:下面会把字段清空,清空后就取不到原生指针了。
        LAppModel? model = _model;

        _autoplay.Detach();
        _parameters.Detach();
        _live2D = null;
        _model = null;
        _hitAreas = [];
        _currentExpression = null;
        _nextMotion = 0;

        DisposeModel(live2D, model);
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
        => DisposeModel(live2D, IntPtr.Zero);

    /// <summary>
    /// 释放模型,并通知 Core 兼容层丢弃该模型的 renderOrder 缓存。
    /// <para>
    /// 必须传原生模型指针:shim 的缓存以 <c>csmModel*</c> 为键,模型销毁后分配器可能把
    /// 同一地址复用给下一个模型 —— 不清缓存会让新模型继承上一个模型的绘制顺序
    /// (见 <see cref="Live2DCoreShim"/> 与 <c>Assets/live2d/shim/shim.c</c>)。
    /// </para>
    /// <para>Windows 定制 Core 没有该导出,<see cref="Live2DCoreShim"/> 内部静默降级。</para>
    /// </summary>
    private static void DisposeModel(InteractiveLive2DDelegate? live2D, LAppModel? model)
        => DisposeModel(live2D, NativeModelPointer(model));

    /// <summary>取 <see cref="LAppModel"/> 的原生 <c>csmModel*</c>(缺失时为零)。</summary>
    private static IntPtr NativeModelPointer(LAppModel? model)
    {
        if (model is null)
        {
            return IntPtr.Zero;
        }
        try
        {
            CubismModel? core = model.Model;
            return core is null ? IntPtr.Zero : core.Model;
        }
        catch (Exception)
        {
            return IntPtr.Zero;
        }
    }

    private static void DisposeModel(InteractiveLive2DDelegate? live2D, IntPtr nativeModel)
    {
        if (live2D is null && nativeModel == IntPtr.Zero)
            return;

        // 先丢缓存再释放:释放后 Core 的 model 内存已失效,不能再拿去查表
        // (shim 只比较指针值、不解引用,但先清更保守)。
        Live2DCoreShim.ForgetRenderOrderCache(nativeModel);

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
