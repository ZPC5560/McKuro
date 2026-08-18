using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using HanumanInstitute.LibMpv;
using HanumanInstitute.LibMpv.Core;

namespace McKuro.Controls;

/// <summary>
/// 背景封面控件:优先用 libmpv 嵌入式渲染 API 播放宣传视频,无可用 native 库或播放失败时回退到首帧静态图。
/// 视频不可用不影响其余功能 —— 全程 try-catch,绝不抛出。
/// <para>
/// v3(2026):从 LibVLC 换到 libmpv,解决 LibVLC 在 Windows 上播放失败(显示静态图)+ msvcrt c0000005
/// 崩溃问题。渲染方式:libmpv 官方 <c>MPV_RENDER_API_TYPE_SW</c> 软件渲染(<see cref="MpvContextBase.StartSoftwareRendering"/>),
/// 每次把整帧写入托管 <see cref="WriteableBitmap"/> 的锁定位图缓冲(封装 <see cref="MpvContextBase.SoftwareRender"/>),
/// 再用普通 <see cref="Image"/> 显示。不使用 <c>HanumanInstitute.LibMpv.Avalonia</c>(避开 ReactiveUI 依赖),
/// 不创建任何 native 子窗口 —— 托管 Image 参与正常 ZIndex 层叠,视频在背景层、UI 在上层,两者同时可见。
/// </para>
/// <para>
/// 线程模型:mpv 的 update 回调跑在 mpv 内部线程,这里只把它 <see cref="Dispatcher.UIThread.Post"/> 到 UI 线程,
/// 所有 <see cref="MpvContextBase.SoftwareRender"/> 与资源释放都串行在 UI 线程,天然规避 LibVLC vmem 那类
/// 跨线程缓冲生命周期崩溃。每个播放器是独立 <see cref="MpvContext"/>,Dispose 时内部会
/// StopRendering + TerminateDestroy,旧播放器不会触碰新播放器的缓冲。
/// </para>
/// <para>
/// Native AOT 安全:libmpv 绑定走 LoadLibrary/dlopen + GetProcAddress/dlsym →
/// <see cref="System.Runtime.InteropServices.Marshal.GetDelegateForFunctionPointer{T}"/>,无反射。
/// 注意<b>不要</b>调用 <see cref="MpvContextBase.ClientName"/>(库中唯一自定义 marshaler 入口,与 AOT 裁剪冲突)。
/// </para>
/// <para>
/// native 库定位:Windows 由 <c>Endpne.LibMPV.Windows</c> 自动拷到输出 <c>libmpv\win-x64\libmpv-2.dll</c>
/// (或 win-arm64)。libmpv 的 Windows resolver 只搜索 <see cref="MpvApi.RootPath"/>,因此首次创建
/// <see cref="MpvContext"/> 前必须把 <see cref="MpvApi.RootPath"/> 指向含 <c>libmpv-2.dll</c> 的目录;
/// macOS/Linux 走系统/Homebrew libmpv(resolver 内置搜索 + RootPath 兜底)。
/// </para>
/// </summary>
public sealed class VideoBackgroundControl : Grid
{
    private readonly object _sync = new();

    private BackgroundMpvContext? _mpv;
    private Image? _videoImage;
    private WriteableBitmap? _bitmap;
    private AsyncImage? _fallback;
    private bool _initialized;
    private bool _attached;
    private bool _disposed;

    // 软件渲染帧状态。更新回调线程只置标志并 Post,实际渲染全在 UI 线程,
    // 因此 _framePending 用 Interlocked 合并多帧,避免同一 UI 迭代重复渲染。
    private int _framePending;

    // 从 mpv video-params 读取的实际视频尺寸(首帧到达后解析一次);
    // 位图按视频原生分辨率创建,交给 Image 的 UniformToFill 做封面缩放 —— 与原 LibVLC 行为一致。
    private int _videoWidth;
    private int _videoHeight;
    private bool _sizeResolved;
    private bool _firstFrameShown;

    // 播放启动看门狗:规定时间内没有首帧(网络卡死/解码失败无事件)则回退静态图。
    private CancellationTokenSource? _timeoutCts;

    public static readonly StyledProperty<string> VideoUrlProperty =
        AvaloniaProperty.Register<VideoBackgroundControl, string>(nameof(VideoUrl));

    public static readonly StyledProperty<string> FallbackImageUrlProperty =
        AvaloniaProperty.Register<VideoBackgroundControl, string>(nameof(FallbackImageUrl));

    public static readonly StyledProperty<bool> IsVideoEnabledProperty =
        AvaloniaProperty.Register<VideoBackgroundControl, bool>(nameof(IsVideoEnabled));

    /// <summary>宣传视频 URL(空则不尝试播放)。</summary>
    public string VideoUrl
    {
        get => GetValue(VideoUrlProperty);
        set => SetValue(VideoUrlProperty, value);
    }

    /// <summary>首帧/静态图 URL(视频不可用时的回退封面)。</summary>
    public string FallbackImageUrl
    {
        get => GetValue(FallbackImageUrlProperty);
        set => SetValue(FallbackImageUrlProperty, value);
    }

    /// <summary>是否启用视频封面(用户设置;禁用则仅显示首帧图)。</summary>
    public bool IsVideoEnabled
    {
        get => GetValue(IsVideoEnabledProperty);
        set => SetValue(IsVideoEnabledProperty, value);
    }

    public VideoBackgroundControl()
    {
        ClipToBounds = true;
        _fallback = new AsyncImage
        {
            Stretch = Stretch.UniformToFill,
        };
        Children.Add(_fallback);

        VideoUrlProperty.Changed.AddClassHandler<VideoBackgroundControl>((o, e) => o.OnUrlChanged());
        FallbackImageUrlProperty.Changed.AddClassHandler<VideoBackgroundControl>((o, e) => o.OnUrlChanged());
        IsVideoEnabledProperty.Changed.AddClassHandler<VideoBackgroundControl>((o, e) => o.OnUrlChanged());

        DetachedFromVisualTree += OnDetached;
    }

    private void OnUrlChanged()
    {
        if (!_initialized)
        {
            // 等挂载后再初始化(需要平台句柄)
            AttachedToVisualTree += OnAttached;
            _initialized = true;
        }
        else if (_attached)
        {
            TryStartVideo();
        }
    }

    private void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        AttachedToVisualTree -= OnAttached;
        _attached = true;
        _fallback!.ImageUrl = FallbackImageUrl;
        TryStartVideo();
    }

    private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        DisposePlayer();
        AttachedToVisualTree -= OnAttached;
        AttachedToVisualTree += OnAttached;
    }

    private void TryStartVideo()
    {
        DisposePlayer();
        _disposed = false;

        if (!IsVideoEnabled || string.IsNullOrWhiteSpace(VideoUrl))
        {
            _fallback!.ImageUrl = FallbackImageUrl;
            return;
        }

        try
        {
            // 必须在 new MpvContext() 之前完成:libmpv Windows resolver 只在 MpvApi.RootPath 下找 libmpv-2.dll
            EnsureNativeRootPath();

            _mpv = new BackgroundMpvContext();
            _videoImage = new Image
            {
                Stretch = Stretch.UniformToFill,
                IsHitTestVisible = false,
                Opacity = 0,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
            };
            Children.Add(_videoImage);

            // 错误/失败 → 回退静态图(EndFile 的 Error reason,含加载/网络/解码失败)
            _mpv.EndFile += (_, e) =>
            {
                if (e.Reason == MpvEndFileReason.Error)
                {
                    ShowFallback();
                }
            };
            // 可选诊断日志
            _mpv.LogMessage += (_, e) =>
            {
                if (e.Level == "error" || e.Level == "fatal")
                {
                    Debug.WriteLine($"[libmpv] {e.Level}: {e.Text}");
                }
            };
            _mpv.RequestLogMessages("warn");

            // 软件渲染:update 回调在 mpv 线程,只合并标志并 Post 到 UI 线程渲染
            _mpv.StartSoftwareRendering(() =>
            {
                if (_disposed)
                {
                    return;
                }
                if (Interlocked.Exchange(ref _framePending, 1) == 0)
                {
                    Dispatcher.UIThread.Post(RenderFrame);
                }
            });

            // 看门狗:15s 内无首帧 → 回退静态图
            _timeoutCts = new CancellationTokenSource();
            var token = _timeoutCts.Token;
            _ = WatchdogAsync(token);

            // loadfile replace → 自动开始播放(loop-file=inf 由 OnPreInitialize 设置,循环不产生 EOF)
            _mpv.LoadFile(VideoUrl).Invoke();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"视频背景播放失败(回退首帧图): {ex.Message}");
            DisposePlayer();
            _fallback!.ImageUrl = FallbackImageUrl;
        }
    }

    private async Task WatchdogAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(15), token).ConfigureAwait(false);
            ShowFallback();
        }
        catch (OperationCanceledException)
        {
            // 首帧已到或已释放:正常路径
        }
    }

    private void DisposePlayer()
    {
        _disposed = true;
        _timeoutCts?.Cancel();
        _timeoutCts?.Dispose();
        _timeoutCts = null;

        if (_mpv is not null)
        {
            // libmpv render API 是嵌入式一等 API:MpvContext.Dispose() 内部 StopRendering(停帧+释放 render context)
            // + TerminateDestroy(mpv_terminate_destroy),比 libvlc 手动管理 vmem 缓冲生命周期可靠得多。
            try
            {
                _mpv.Dispose();
            }
            catch (Exception)
            {
                // 忽略释放异常
            }
            _mpv = null;
        }

        lock (_sync)
        {
            _bitmap?.Dispose();
            _bitmap = null;
        }
        _sizeResolved = false;
        _firstFrameShown = false;
        _videoWidth = 0;
        _videoHeight = 0;

        if (_videoImage is not null)
        {
            _videoImage.Source = null;
            Children.Remove(_videoImage);
            _videoImage = null;
        }
    }

    private void ShowFallback()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_videoImage is not null)
            {
                _videoImage.Opacity = 0;
            }

            if (_fallback is not null)
            {
                _fallback.ImageUrl = FallbackImageUrl;
            }
        });
    }

    // ---- UI 线程渲染 ----

    private void RenderFrame()
    {
        Interlocked.Exchange(ref _framePending, 0);
        if (_disposed || _mpv is null)
        {
            return;
        }

        try
        {
            lock (_sync)
            {
                if (_mpv is null)
                {
                    return;
                }

                if (!_sizeResolved)
                {
                    if (!TryResolveVideoSize())
                    {
                        return;
                    }
                }

                if (_videoWidth <= 0 || _videoHeight <= 0)
                {
                    return;
                }

                if (_bitmap is null
                    || _bitmap.PixelSize.Width != _videoWidth
                    || _bitmap.PixelSize.Height != _videoHeight)
                {
                    _bitmap?.Dispose();
                    _bitmap = new WriteableBitmap(
                        new PixelSize(_videoWidth, _videoHeight),
                        new Vector(96, 96),
                        PixelFormats.Bgra8888,
                        AlphaFormat.Opaque);
                    if (_videoImage is not null)
                    {
                        _videoImage.Source = _bitmap;
                    }
                }

                // mpv SW render 直接写入锁定位图缓冲(stride = width*4,与 Bgra8888 对齐)
                using (var fb = _bitmap.Lock())
                {
                    _mpv.SoftwareRender(fb.Size.Width, fb.Size.Height, fb.Address, "bgra");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"libmpv 渲染帧失败: {ex.Message}");
        }

        // 首个真实帧到达后才显示视频(此前保持透明,让静态图可见)
        if (!_firstFrameShown)
        {
            _firstFrameShown = true;
            _timeoutCts?.Cancel();
            if (_videoImage is not null)
            {
                _videoImage.Opacity = 1;
            }
        }
    }

    /// <summary>从 mpv video-params 读取实际视频分辨率(超过上限按比例缩小,防止 4K/8K 源撑爆软件缓冲)。</summary>
    private bool TryResolveVideoSize()
    {
        if (_mpv is null)
        {
            return false;
        }

        try
        {
            int w = _mpv.GetProperty<int>("video-params/w");
            int h = _mpv.GetProperty<int>("video-params/h");
            if (w <= 0 || h <= 0)
            {
                return false;
            }

            // 上限保护:软件渲染缓冲过大(>2K 宽)时按比例降采样,背景播放无需原分辨率
            const int maxWidth = 2560;
            if (w > maxWidth)
            {
                h = (int)((long)h * maxWidth / w);
                w = maxWidth;
            }

            _videoWidth = w;
            _videoHeight = h;
            _sizeResolved = true;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void EnsureNativeRootPath()
    {
        // libmpv 的 Windows resolver 只在 MpvApi.RootPath 下搜索 libmpv-2.dll(单一路径,无系统回退)。
        // Endpne.LibMPV.Windows 会把 dll 拷到 libmpv\win-x64(AnyCPU→x64)或 libmpv\win-arm64;
        // 探测到就把 RootPath 指过去;找不到则保持默认(AppContext.BaseDirectory,可能直接放 dll)。
        // macOS/Linux resolver 自带系统路径,不需要改 RootPath。
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var baseDir = AppContext.BaseDirectory;
        string[] candidates =
        {
            Path.Combine(baseDir, "libmpv", "win-x64"),
            Path.Combine(baseDir, "libmpv", "win-arm64"),
            baseDir,
        };

        foreach (var dir in candidates)
        {
            if (Directory.Exists(dir) && File.Exists(Path.Combine(dir, "libmpv-2.dll")))
            {
                MpvApi.RootPath = dir;
                return;
            }
        }
    }

    /// <summary>
    /// 带预初始化选项的 libmpv 上下文。基类构造顺序:mpv_create → <see cref="OnPreInitialize"/>
    /// (设置必须在 mpv_initialize 之前的核心选项)→ mpv_initialize → 事件循环。
    /// <c>vo=libmpv</c> 必须在此设置,render API 才能接管输出;其余为后台封面播放的合理默认。
    /// </summary>
    private sealed class BackgroundMpvContext : MpvContext
    {
        protected override void OnPreInitialize()
        {
            // 渲染 API 要求 vo=libmpv 在 initialize 前设置
            SetOptionString("vo", "libmpv");
            SetOptionString("audio", "no");
            SetOptionString("hwdec", "no");
            SetOptionString("loop-file", "inf");
            SetOptionString("volume", "0");
        }
    }
}
