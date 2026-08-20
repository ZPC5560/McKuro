using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
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
/// 线程模型:mpv 的 update 回调只唤醒专用视频线程,视频线程固定频率执行 LoadFile、解码和
/// <see cref="MpvContextBase.SoftwareRender"/>;UI 线程只负责把最新帧拷贝到位图。资源释放会先等待视频线程退出,
/// 再销毁 <see cref="MpvContext"/>,避免跨线程访问 native render context。
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
    private volatile bool _disposed;

    // mpv 的更新回调只负责唤醒视频线程。视频线程自身按固定帧率驱动渲染，
    // 不依赖回调次数，避免回调与取标志并发时丢信号导致播放停在首秒。
    private Thread? _renderThread;
    private readonly AutoResetEvent _frameSignal = new(false);
    private string? _pendingVideoPath;
    private byte[]? _renderBuffer;
    private GCHandle _bufferHandle;
    private IntPtr _bufferPtr;
    private volatile bool _renderThreadRunning;
    private bool _uiFramePending;

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

            // mpv 更新回调来自 native 线程，只唤醒专用视频线程；不在回调里做 UI 或渲染。
            _mpv.StartSoftwareRendering(() =>
            {
                if (!_disposed)
                {
                    try
                    {
                        _frameSignal.Set();
                    }
                    catch (ObjectDisposedException)
                    {
                    }
                }
            });

            // 启动独立渲染线程:每帧 SoftwareRender 写入 pin 缓冲,UI 只做轻量拷贝
            StartRenderThread();

            // 看门狗:15s 内无首帧 → 回退静态图
            _timeoutCts = new CancellationTokenSource();
            var token = _timeoutCts.Token;
            _ = WatchdogAsync(token);

            // 预下载视频到本地缓存目录再播放:CDN 流式缓冲不可靠(只播一秒),本地文件最稳。
            // 缓存按 URL 哈希命名,重复进入启动页直接用缓存,不重复下载。
            var url = VideoUrl;
            _ = LoadLocalVideoAsync(url, token);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"视频背景播放失败(回退首帧图): {ex.Message}");
            DisposePlayer();
            _fallback!.ImageUrl = FallbackImageUrl;
        }
    }

    /// <summary>下载视频到本地缓存目录,完成后用本地文件播放(CDN 流式缓冲不可靠)。</summary>
    private async Task LoadLocalVideoAsync(string url, CancellationToken token)
    {
        try
        {
            var cacheDir = Path.Combine(Path.GetTempPath(), "McKuroVideo");
            Directory.CreateDirectory(cacheDir);
            var hash = Convert.ToHexString(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(url)));
            var localPath = Path.Combine(cacheDir, hash + ".mp4");

            if (!File.Exists(localPath) || new FileInfo(localPath).Length == 0)
            {
                Debug.WriteLine($"[libmpv] 下载视频到本地: {url}");
                using var http = new HttpClient();
                var bytes = await http.GetByteArrayAsync(url, token).ConfigureAwait(false);
                if (bytes.Length == 0)
                {
                    Debug.WriteLine("[libmpv] 视频下载为空,回退静态图");
                    Dispatcher.UIThread.Post(ShowFallback);
                    return;
                }
                await File.WriteAllBytesAsync(localPath, bytes, token).ConfigureAwait(false);
            }

            if (token.IsCancellationRequested || _disposed)
            {
                return;
            }

            // 把 LoadFile 排队到专用视频线程，UI 线程只负责控件和位图更新。
            lock (_sync)
            {
                _pendingVideoPath = localPath;
            }
            _frameSignal.Set();
        }
        catch (OperationCanceledException)
        {
            // 已释放/取消:忽略
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[libmpv] 视频预下载失败(回退静态图): {ex.Message}");
            Dispatcher.UIThread.Post(ShowFallback);
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

        // 先停渲染线程,避免它还在用 mpv render context 时 Dispose
        StopRenderThread();

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

    // ---- 独立渲染线程 ----

    /// <summary>启动后台渲染线程(与 UI 线程解耦,UI 卡顿不影响视频解码/渲染)。</summary>
    private void StartRenderThread()
    {
        StopRenderThread();
        _renderThreadRunning = true;
        _renderThread = new Thread(RenderLoop)
        {
            IsBackground = true,
            Name = "McKuroVideoRender",
        };
        _renderThread.Start();
    }

    private void StopRenderThread()
    {
        _renderThreadRunning = false;
        try
        {
            _frameSignal.Set();
        }
        catch (ObjectDisposedException)
        {
        }
        if (_renderThread is { IsAlive: true })
        {
            // 等待渲染线程退出(最多 1s),避免 Dispose 时正占用 mpv render context
            if (!_renderThread.Join(1000))
            {
                // 超时:线程可能卡在 mpv 内部,由 mpv.Dispose 兜底
            }
        }
        _renderThread = null;
        ReleaseRenderBuffer();
        lock (_sync)
        {
            _pendingVideoPath = null;
        }
        _uiFramePending = false;
    }

    /// <summary>
    /// 专用视频线程:定时处理 LoadFile 并持续调用 SoftwareRender。
    /// mpv 回调只负责唤醒线程，回调丢失也不会让播放停住；UI 线程只接收最新帧。
    /// </summary>
    private void RenderLoop()
    {
        while (_renderThreadRunning)
        {
            try
            {
                // 即使 mpv 没有发 update 回调，也每 33ms 驱动一次，避免首秒后停帧。
                _frameSignal.WaitOne(33);
            }
            catch (ObjectDisposedException)
            {
                break;
            }

            if (!_renderThreadRunning || _disposed)
            {
                break;
            }

            try
            {
                string? path = null;
                lock (_sync)
                {
                    if (_pendingVideoPath is not null)
                    {
                        path = _pendingVideoPath;
                        _pendingVideoPath = null;
                    }
                }

                if (path is not null && _mpv is not null)
                {
                    // 播放控制与 mpv 渲染都在同一个视频线程，避免 UI Dispatcher 介入。
                    _mpv.LoadFile(path).Invoke();
                    _sizeResolved = false;
                    _firstFrameShown = false;
                }

                if (_mpv is null)
                {
                    continue;
                }

                if (!_sizeResolved && !TryResolveVideoSize())
                {
                    continue;
                }
                if (_videoWidth <= 0 || _videoHeight <= 0)
                {
                    continue;
                }

                lock (_sync)
                {
                    if (_mpv is null)
                    {
                        continue;
                    }
                    EnsureRenderBuffer(_videoWidth, _videoHeight);
                    if (_bufferPtr != IntPtr.Zero)
                    {
                        _mpv.SoftwareRender(_videoWidth, _videoHeight, _bufferPtr, "bgra");
                    }
                }

                // 只排一个 UI 拷贝任务；任务执行时会复制当前最新帧。
                if (!_uiFramePending)
                {
                    _uiFramePending = true;
                    Dispatcher.UIThread.Post(FlushFrameToBitmap);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"libmpv 播放/渲染帧失败: {ex.Message}");
            }
        }
    }

    /// <summary>确保渲染缓冲存在(按视频尺寸分配并 pin)。</summary>
    private void EnsureRenderBuffer(int width, int height)
    {
        var size = width * height * 4;
        if (_renderBuffer is not null && _renderBuffer.Length >= size)
        {
            return;
        }
        ReleaseRenderBuffer();
        _renderBuffer = new byte[size];
        _bufferHandle = GCHandle.Alloc(_renderBuffer, GCHandleType.Pinned);
        _bufferPtr = _bufferHandle.AddrOfPinnedObject();
    }

    private void ReleaseRenderBuffer()
    {
        if (_bufferHandle.IsAllocated)
        {
            _bufferHandle.Free();
            _bufferHandle = default;
        }
        _renderBuffer = null;
        _bufferPtr = IntPtr.Zero;
    }

    /// <summary>UI 线程:把渲染缓冲拷贝到 WriteableBitmap(纯拷贝,不参与解码)。</summary>
    private void FlushFrameToBitmap()
    {
        _uiFramePending = false;
        if (_disposed || _mpv is null || _renderBuffer is null)
        {
            return;
        }

        try
        {
            lock (_sync)
            {
                if (_mpv is null || _renderBuffer is null)
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

                // 纯内存拷贝到锁定位图缓冲(stride = width*4,与 Bgra8888 对齐)
                using (var fb = _bitmap.Lock())
                {
                    Marshal.Copy(_renderBuffer, 0, fb.Address, _renderBuffer.Length);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"libmpv 拷贝帧失败: {ex.Message}");
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

            // 上限保护:软件渲染缓冲过大(>HD 宽)时按比例降采样,背景装饰无需原分辨率。
            // 1280 宽即可满足满屏背景观感,显著降低软件解码+UI 拷贝负载(参考 Haiyu 用系统硬解)。
            const int maxWidth = 1280;
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
            // 网络缓冲:CDN 视频首次缓冲耗尽后卡住"只动了一下",增大 demuxer 缓冲
            SetOptionString("demuxer-max-bytes", "50M");
            SetOptionString("cache-secs", "30");
            SetOptionString("demuxer-readahead-secs", "30");
            SetOptionString("network-timeout", "30");
            // CDN 可能校验请求头(库洛游戏静态资源 CDN)
            SetOptionString("user-agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
            SetOptionString("referrer", "https://kurogame.com");
        }
    }
}
