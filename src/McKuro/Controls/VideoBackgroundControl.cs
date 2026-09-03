using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using HanumanInstitute.LibMpv;
using HanumanInstitute.LibMpv.Core;
using McKuro.Services;

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
/// v4(2026):新增 GPU 渲染路径 —— 优先使用 <c>MPV_RENDER_API_TYPE_OPENGL</c>
/// (<see cref="MpvContextBase.StartOpenGlRendering"/> + <see cref="MpvContextBase.OpenGlRender"/>),
/// 通过 Avalonia 的 <see cref="OpenGlControlBase"/> 把视频直接渲染到 OpenGL framebuffer,解码/渲染全在 GPU
/// (hwdec=auto-safe:macOS videotoolbox / Windows d3d11va / Linux vaapi)。GL 初始化失败时自动回退 v3 的
/// 软件渲染路径。仍不创建 native 子窗口 —— OpenGlControlBase 是普通托管控件,参与正常 ZIndex 层叠。
/// </para>
/// <para>
/// 线程模型(v4 GL 路径):<see cref="OpenGlControlBase.OnOpenGlInit"/> 在 Avalonia 渲染线程持有 GL 上下文时
/// 调用 <see cref="MpvContextBase.StartOpenGlRendering"/>;此后 <see cref="OpenGlControlBase.OnOpenGlRender"/>
/// 每帧调用 <see cref="MpvContextBase.OpenGlRender"/> 由 mpv 直接绘制到当前 framebuffer。mpv 的 update 回调
/// 只负责请求下一帧渲染(UI 线程 RequestNextFrameRendering),无专用视频线程、无托管位图拷贝。
/// 资源释放会先移除 GL 控件(触发 OnOpenGlDeinit),再销毁 <see cref="MpvContext"/>,避免跨线程访问 native
/// render context。
/// </para>
/// <para>
/// Native AOT 安全:libmpv 绑定走 LoadLibrary/dlopen + GetProcAddress/dlsym →
/// <see cref="System.Runtime.InteropServices.Marshal.GetDelegateForFunctionPointer{T}"/>,无反射。
/// 注意<b>不要</b>调用 <see cref="MpvContextBase.ClientName"/>(库中唯一自定义 marshaler 入口,与 AOT 裁剪冲突)。
/// </para>
/// <para>
/// native 库定位:统一把 <see cref="MpvApi.RootPath"/> 指向软件目录内的 libmpv 副本。
/// Windows 由 <c>Endpne.LibMPV.Windows</c> 自动拷到输出 <c>libmpv\win-x64\libmpv-2.dll</c>
/// (或 win-arm64);macOS/Linux 由构建目标调用 <c>bundle-mpv-macos.sh</c> 把 libmpv 及其
/// 完整依赖树打包进输出 <c>libmpv/</c> 子目录(全部改写为 @loader_path 相对加载路径,
/// 自带播放环境,目标机器无需安装 Homebrew/mpv)。打包副本缺失时再回退系统/Homebrew 路径。
/// </para>
/// <para>
/// 前提(Program.cs):GL 渲染需要 Avalonia 运行在 OpenGL 系渲染模式 —— Windows ANGLE、
/// macOS 原生 OpenGL、Linux EGL/GLX,均已配置,且都保留 Software 回退。
/// </para>
/// </summary>
public sealed class VideoBackgroundControl : Grid
{
    private readonly object _sync = new();

    private BackgroundMpvContext? _mpv;
    private Image? _videoImage;
    private WriteableBitmap? _bitmap;
    private AsyncImage? _fallback;
    private VideoGlRenderer? _glRenderer;
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

    // 从 mpv video-params 读取的实际视频尺寸(首帧到达后解析一次);
    // 位图按视频原生分辨率创建,交给 Image 的 UniformToFill 做封面缩放 —— 与原 LibVLC 行为一致。
    private int _videoWidth;
    private int _videoHeight;
    private bool _sizeResolved;
    private bool _firstFrameShown;

    // 播放启动看门狗:规定时间内没有首帧(网络卡死/解码失败无事件)则回退静态图。
    private CancellationTokenSource? _timeoutCts;

    // ── 挂起(最小化/隐藏/游戏运行中)──────────────────────────────────────────
    // 软件解码 + 2048x1216 帧拷贝常驻约 1~2 核;不挂起时,点「启动游戏」后(最小化主窗口
    // 或留在启动页)会与 UE 的着色器编译/资源加载抢 CPU 与磁盘,游戏启动明显慢于官方
    // 启动器(官方启动后会自行退出让出资源)。挂起=暂停 mpv 解码并跳过渲染,恢复原位续播。
    private volatile bool _suspended;
    private bool _mpvPausedApplied; // 仅渲染线程读写
    private Window? _watchedWindow;

    /// <summary>播放会话代号:每次 TryStartVideo 自增,异步回调只对当前会话生效(防旧会话串台)。</summary>
    private long _imageGeneration;

    /// <summary>已入队的 UI 重绘请求所属会话。</summary>
    private long _postedGeneration;

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
        LayoutUpdated += OnLayoutDiag;
    }

    /// <summary>背景覆盖诊断:视频位图的实际 Bounds 是否与父控件一致(不一致=裁剪错误)。
    /// 布局变化去重打印,最多 5 条(init 状态与 resize 后状态各一条可对比)。</summary>
    private int _diagCnt;
    private string? _diagPrev;

    private void OnLayoutDiag(object? sender, EventArgs e)
    {
        if (_diagCnt >= 5)
        {
            LayoutUpdated -= OnLayoutDiag;
            return;
        }
        var child = _videoImage ?? (_fallback as Avalonia.Controls.Control);
        var sample = child is null
            ? "MCKURO-VIDEO noChild"
            : $"MCKURO-VIDEO self=[{Bounds.Width:F0}x{Bounds.Height:F0}] " +
              $"child=[{child.Bounds.Width:F0}x{child.Bounds.Height:F0}] f={(child.Bounds == Bounds)} " +
              $"src={(_videoImage is not null && _videoImage.Source is not null)} vis={child.IsEffectivelyVisible} " +
              $"vid=({_videoWidth}x{_videoHeight})";
        if (sample != _diagPrev)
        {
            _diagPrev = sample;
            _diagCnt++;
            System.Console.Error.WriteLine(sample);
        }
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

        // 挂起源①:宿主窗口最小化/隐藏(覆盖「启动游戏后最小化到任务栏/托盘」场景)
        _watchedWindow = TopLevel.GetTopLevel(this) as Window;
        if (_watchedWindow is not null)
        {
            _watchedWindow.PropertyChanged += OnWatchedWindowPropertyChanged;
        }
        // 挂起源②:游戏会话进行中(启动中/游戏中),让出 CPU 与磁盘给游戏本体
        AppServices.GameMonitor.StateChanged += OnGameStateChanged;
        UpdateSuspendState();

        TryStartVideo();
    }

    private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        if (_watchedWindow is not null)
        {
            _watchedWindow.PropertyChanged -= OnWatchedWindowPropertyChanged;
            _watchedWindow = null;
        }
        AppServices.GameMonitor.StateChanged -= OnGameStateChanged;
        DisposePlayer();
        AttachedToVisualTree -= OnAttached;
        AttachedToVisualTree += OnAttached;
    }

    private void OnWatchedWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty || e.Property == IsVisibleProperty)
        {
            UpdateSuspendState();
        }
    }

    private void OnGameStateChanged(GameSessionState state)
    {
        UpdateSuspendState();
    }

    private void UpdateSuspendState()
    {
        var minimized = _watchedWindow?.WindowState == WindowState.Minimized;
        var hidden = _watchedWindow is { IsVisible: false };
        var gameActive = AppServices.GameMonitor.State != GameSessionState.Idle;
        _suspended = minimized || hidden || gameActive;

        if (_glRenderer is not null)
        {
            // GL 路径:直接同步 mpv 暂停标志(无渲染线程,不能依赖 _frameSignal 唤醒)
            _glRenderer.ApplySuspend(_suspended);
        }
        else
        {
            _frameSignal.Set(); // 唤醒渲染线程立即应用(否则最多延迟一个 33ms 节拍)
        }
    }

    private void TryStartVideo()
    {
        DisposePlayer();
        _disposed = false;
        _imageGeneration = Interlocked.Increment(ref _imageGeneration);

        if (!IsVideoEnabled || string.IsNullOrWhiteSpace(VideoUrl))
        {
            _fallback!.ImageUrl = FallbackImageUrl;
            return;
        }

        try
        {
            // 必须在探测/创建 MpvContext 之前完成:libmpv resolver 只在 MpvApi.RootPath
            // (默认 AppContext.BaseDirectory)下找库。Windows 由 Endpne.LibMPV.Windows 拷到
            // libmpv\win-x64;macOS 由 bundle-mpv-macos.sh 打包到 libmpv/ 子目录。
            EnsureNativeRootPath();
        }
        catch (Exception)
        {
            // 设置 RootPath 失败不阻塞:探测与创建仍有系统路径兜底
        }

        // 平台无 libmpv 时直接回退静态图 —— 不能继续创建 MpvContext:
        // 构造器抛 DllNotFoundException 后,部分构造的对象仍会被 GC 终结,
        // 其 Finalize → StopRendering 再次解析 libmpv 函数指针,终结器内异常
        // 无法被托管 try-catch 捕获,进程直接崩溃(实测 macOS Abort trap: 6)。
        if (!IsLibMpvAvailable())
        {
            Debug.WriteLine("[libmpv] native 库不可用,回退静态图");
            ShowFallback();
            return;
        }

        try
        {
            _mpv = new BackgroundMpvContext();

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

            // 看门狗:15s 内无首帧 → 回退静态图
            _timeoutCts = new CancellationTokenSource();
            var token = _timeoutCts.Token;
            _ = WatchdogAsync(token);

            // v4:优先 OpenGL GPU 渲染(GPU 解码 + GPU 呈现,见 VideoGlRenderer);
            // 平台无 GL 后端/初始化失败时自动回退 v3 软件渲染路径。
            _ = TryGlOrFallbackAsync(token);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"视频背景播放失败(回退首帧图): {ex.Message}");
            DisposePlayer();
            _fallback!.ImageUrl = FallbackImageUrl;
        }
    }

    /// <summary>
    /// 尝试 OpenGL GPU 渲染路径:创建 <see cref="VideoGlRenderer"/> 加入视觉树并等待 GL 就绪。
    /// 初始化成功 → GL 路径(GPU 解码+渲染);失败/超时/已释放 → 回退软件渲染路径。
    /// </summary>
    private async Task TryGlOrFallbackAsync(CancellationToken token)
    {
        VideoGlRenderer? gl = null;
        try
        {
            if (_mpv is null || _disposed)
            {
                return;
            }

            // 利用 TaskCompletionSource 在 OnOpenGlInit 完成时通知 —— Avalonia 12 的
            // OpenGlControlBase 无外部可调用的公开 InitializeAsync,只能通过视觉树自动初始化。
            var glReadyTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            gl = new VideoGlRenderer(this, glReadyTcs)
            {
                Opacity = 0,
                IsHitTestVisible = false,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
            };
            Children.Add(gl);

            // 等 GL 初始化完成(自动初始化,OnOpenGlInit 设 tcs)或超时
            bool ok;
            try
            {
                ok = await glReadyTcs.Task.WaitAsync(TimeSpan.FromSeconds(5), token).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                ok = false; // 超时或已取消 → 回退
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[libmpv] GL 初始化异常: {ex.Message}");
                ok = false;
            }

            if (!ok || _disposed || _mpv is null)
            {
                if (!_disposed && gl is not null && Children.Contains(gl))
                {
                    Children.Remove(gl);
                }
                if (!_disposed)
                {
                    StartSoftwareRenderingPath();
                }
                return;
            }

            // GL 路径成功:渲染器接管显示;首个文件加载完成后取消看门狗并显示视频
            _glRenderer = gl;
            _mpv.FileLoaded += OnGlFileLoaded;
            try
            {
                // 铺满渲染目标(=UniformToFill 的裁切行为;仅 GL 路径生效)
                _mpv.SetOptionString("panscan", "1.0");
            }
            catch (Exception)
            {
            }

            _ = LoadLocalVideoAndPlayAsync(VideoUrl, token);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[libmpv] GL 渲染路径失败,回退软件渲染: {ex.Message}");
            if (!_disposed)
            {
                if (gl is not null && Children.Contains(gl))
                {
                    Children.Remove(gl);
                }
                StartSoftwareRenderingPath();
            }
        }
    }

    /// <summary>GL 路径的首帧信号:文件加载完成 → 取消看门狗、显示 GL 视频层。</summary>
    private void OnGlFileLoaded(object? sender, EventArgs e)
    {
        _timeoutCts?.Cancel();
        Dispatcher.UIThread.Post(() =>
        {
            if (_glRenderer is not null && !_disposed)
            {
                _glRenderer.Opacity = 1;
            }
        });
    }

    /// <summary>GL 渲染线程获取当前 mpv 上下文(渲染器初始化/渲染时调用;可能为 null:已释放)。</summary>
    private BackgroundMpvContext? GetMpvForGl()
    {
        lock (_sync)
        {
            return _disposed ? null : _mpv;
        }
    }

    /// <summary>软件渲染路径(v3 回退):位图呈现 + 专用渲染线程。</summary>
    private void StartSoftwareRenderingPath()
    {
        try
        {
            if (_mpv is null)
            {
                _fallback!.ImageUrl = FallbackImageUrl;
                return;
            }

            _videoImage = new Image
            {
                Stretch = Stretch.UniformToFill,
                IsHitTestVisible = false,
                Opacity = 0,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
            };
            Children.Add(_videoImage);

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

            // 预下载视频到本地缓存目录再播放:CDN 流式缓冲不可靠(只播一秒),本地文件最稳。
            // 缓存按 URL 哈希命名,重复进入启动页直接用缓存,不重复下载。
            var url = VideoUrl;
            _ = LoadLocalVideoAsync(url, _timeoutCts?.Token ?? CancellationToken.None);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"软件渲染启动失败(回退首帧图): {ex.Message}");
            DisposePlayer();
            _fallback!.ImageUrl = FallbackImageUrl;
        }
    }

    /// <summary>GL 路径:下载视频到本地缓存目录,完成后直接 LoadFile(mpv 命令 API 线程安全,任意线程可调)。</summary>
    private async Task LoadLocalVideoAndPlayAsync(string url, CancellationToken token)
    {
        try
        {
            var localPath = await EnsureVideoCachedAsync(url, token).ConfigureAwait(false);
            if (localPath is null || token.IsCancellationRequested || _disposed)
            {
                return;
            }

            lock (_sync)
            {
                if (_mpv is null || _disposed)
                {
                    return;
                }
                _mpv.LoadFile(localPath).Invoke();
            }
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

    /// <summary>
    /// 解析视频源为本地文件路径(GPU 渲染路径专用):
    /// 本地文件(自定义动态壁纸:本地视频 / Wallpaper Engine 包内视频)直接返回;
    /// http(s) 下载到本地缓存目录(CDN 流式缓冲不可靠,本地文件最稳);缓存命中直接复用。
    /// </summary>
    private async Task<string?> EnsureVideoCachedAsync(string url, CancellationToken token)
    {
        // 本地文件分支必须在 HttpClient 之前:本地路径不是合法 http URI,GetByteArrayAsync 会抛异常。
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            if (File.Exists(url))
            {
                return url;
            }
            Dispatcher.UIThread.Post(ShowFallback);
            return null;
        }

        var cacheDir = Path.Combine(Path.GetTempPath(), "McKuroVideo");
        Directory.CreateDirectory(cacheDir);
        var hash = Convert.ToHexString(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(url)));
        var localPath = Path.Combine(cacheDir, hash + ".mp4");

        if (File.Exists(localPath) && new FileInfo(localPath).Length > 0)
        {
            return localPath; // 缓存命中,直接使用本地文件
        }

        // 复用共享 HttpClient(连接池/ decompression 统一)
        var bytes = await McKuro.Services.AppServices.Http.GetByteArrayAsync(url, token).ConfigureAwait(false);
        if (bytes.Length == 0)
        {
            Dispatcher.UIThread.Post(ShowFallback);
            return null;
        }
        await File.WriteAllBytesAsync(localPath, bytes, token).ConfigureAwait(false);
        return localPath;
    }

    /// <summary>下载视频到本地缓存目录,完成后用本地文件播放(软件渲染路径专用:排队给渲染线程)。</summary>
    private async Task LoadLocalVideoAsync(string url, CancellationToken token)
    {
        try
        {
            // 本地文件(自定义动态壁纸:本地视频 / Wallpaper Engine 包内视频)无需预下载,直接交给 mpv。
            // 此路径必须在 HttpClient 之前分支:本地路径不是合法 http URI,GetByteArrayAsync 会抛异常回退静态图。
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(url))
                {
                    lock (_sync)
                    {
                        _pendingVideoPath = url;
                    }
                    _frameSignal.Set();
                }
                else
                {
                    // 文件被移动/删除:回退静态封面(与官方视频网络失败同路径)
                    ShowFallback();
                }
                return;
            }

            var localPath = await EnsureVideoCachedAsync(url, token).ConfigureAwait(false);
            if (localPath is null || token.IsCancellationRequested || _disposed)
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

        // 先停 GL 渲染器(与 OnOpenGlRender 互斥,等待在途帧完成),再移除控件
        if (_glRenderer is not null)
        {
            _glRenderer.BeginShutdown();
            if (Children.Contains(_glRenderer))
            {
                Children.Remove(_glRenderer);
            }
            _glRenderer = null;
        }

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

            if (_glRenderer is not null)
            {
                _glRenderer.Opacity = 0;
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
        _postedGeneration = 0;
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

            // 挂起:同步 mpv 暂停标志(停解码器线程),跳过渲染与帧拷贝;恢复后原位续播
            if (_suspended != _mpvPausedApplied)
            {
                try
                {
                    _mpv?.SetPropertyFlag("pause", _suspended);
                }
                catch
                {
                    // mpv 已销毁/属性不可写:忽略,下个节拍重试
                }
                _mpvPausedApplied = _suspended;
            }
            if (_suspended)
            {
                continue;
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

                // 渲染前处理 mpv render context 的挂起队列(标准调用序列;advanced_control=0 时为空操作)
                try
                {
                    _mpv.RenderContextUpdate();
                }
                catch
                {
                    // 忽略
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

                        // 帧拷贝在渲染线程完成(WriteableBitmap 是无视觉树的数据对象,Lock/拷贝线程安全)。
                        // 画面推进不再依赖 UI 线程调度:启动页首屏布局/网络卡顿不再让视频"只播一秒就冻住"。
                        CopyFrameToBitmap();
                    }
                }

                // UI 线程只剩一个轻量"重绘"动作;重绘内容始终是位图里的最新帧,迟执行也不会卡死画面。
                PostInvalidateImage();
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

    /// <summary>渲染线程:把渲染缓冲拷进 WriteableBitmap(纯内存拷贝,不参与解码;不依赖 UI 线程)。</summary>
    private void CopyFrameToBitmap()
    {
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
        }

        // 纯内存拷贝到锁定位图缓冲(stride = width*4,与 Bgra8888 对齐)
        using (var fb = _bitmap.Lock())
        {
            Marshal.Copy(_renderBuffer!, 0, fb.Address, _renderBuffer!.Length);
        }
    }

    /// <summary>
    /// 渲染线程:请求 UI 重绘当前位图(合成/合并同帧重复请求)。
    /// 只发一个轻量动作;位图内容已由渲染线程写就,UI 稍晚执行也只是晚重绘,
    /// 不会让画面永久冻结;会话代际不匹配的旧动作直接丢弃。
    /// </summary>
    private void PostInvalidateImage()
    {
        var gen = Interlocked.Read(ref _imageGeneration);
        if (Interlocked.Read(ref _postedGeneration) == gen)
        {
            return;
        }
        Interlocked.Exchange(ref _postedGeneration, gen);
        Dispatcher.UIThread.Post(() =>
        {
            Interlocked.Exchange(ref _postedGeneration, 0);
            if (Interlocked.Read(ref _imageGeneration) != gen || _disposed)
            {
                return;
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

            // 位图在渲染线程重建,Source 绑定只能在 UI 线程更新
            if (_videoImage is not null && _videoImage.Source != _bitmap)
            {
                _videoImage.Source = _bitmap;
            }

            _videoImage?.InvalidateVisual();
        });
    }

    /// <summary>从 mpv video-params 读取实际视频分辨率(按视频原始分辨率渲染,保持画质)。</summary>
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

            // 使用视频原始分辨率(软件渲染缓冲按源分辨率分配,背景画质最佳)。
            // 仅在极端尺寸(>4K 宽)时按比例限制,防止异常源撑爆内存。
            const int maxWidth = 4096;
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

    /// <summary>
    /// 探测 libmpv native 库是否可用,决定是否尝试视频播放。
    /// 顺序与 LibMpv resolver(<c>MacFunctionResolver</c>)的实际搜索一致:
    /// ① 系统/Homebrew 路径(命中即返回,不加载软件目录打包版 —— 避免同一进程同时
    ///    加载两套 dylib,ObjC 类重复注册告警/潜在崩溃);② 都没有时再探测
    ///    <see cref="MpvApi.RootPath"/> 指向的软件目录打包副本(bundle-mpv-macos.sh
    ///    生成,自包含依赖树,目标机器无需安装 Homebrew/mpv)。
    /// dlopen 成功才能证明库真的可加载(文件存在但架构不符时会失败,此时回退静态图,
    /// 避免构造 MpvContext 崩溃)。Windows:检查 RootPath 下 libmpv-2.dll
    /// (Endpne.LibMPV.Windows 打包的副本,或手动放置)。
    /// </summary>
    private static bool IsLibMpvAvailable()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var root = MpvApi.RootPath ?? AppContext.BaseDirectory;
                if (File.Exists(Path.Combine(root, "libmpv-2.dll")))
                {
                    return true;
                }

                // 兜底:Windows LoadLibrary 默认搜索路径(app 目录 / system32 / PATH)
                if (NativeLibrary.TryLoad("libmpv-2", typeof(VideoBackgroundControl).Assembly, null, out var winHandle))
                {
                    NativeLibrary.Free(winHandle);
                    return true;
                }
                return false;
            }

            // ① 系统/Homebrew 路径(MacFunctionResolver 的搜索顺序,先命中先使用)
            var fileName = OperatingSystem.IsMacOS() ? "libmpv.2.dylib" : "libmpv.so.2";
            string[] systemCandidates = OperatingSystem.IsMacOS()
                ? new[] { "/usr/local/lib", "/opt/homebrew/lib", "/usr/lib" }
                : new[] { "/usr/lib/x86_64-linux-gnu", "/lib64", "/usr/lib64", "/lib", "/usr/lib" };

            foreach (var dir in systemCandidates)
            {
                if (NativeLibrary.TryLoad(Path.Combine(dir, fileName), out var handle))
                {
                    NativeLibrary.Free(handle);
                    return true;
                }
            }

            // ② 系统/Homebrew 都没有 → 探测软件目录打包副本(RootPath 已由 EnsureNativeRootPath 指向 libmpv/)
            var rootPath = MpvApi.RootPath ?? AppContext.BaseDirectory;
            string[] bundledCandidates =
            {
                rootPath,
                Path.Combine(AppContext.BaseDirectory, "libmpv"),
                AppContext.BaseDirectory,
            };
            foreach (var dir in bundledCandidates)
            {
                if (NativeLibrary.TryLoad(Path.Combine(dir, fileName), out var handle))
                {
                    NativeLibrary.Free(handle);
                    return true;
                }
            }

            // 兜底:标准 dlopen 搜索路径(DYLD_LIBRARY_PATH / LD_LIBRARY_PATH 等自定义路径)
            var libName = OperatingSystem.IsMacOS() ? "libmpv.2" : "libmpv.so.2";
            if (NativeLibrary.TryLoad(libName, typeof(VideoBackgroundControl).Assembly, null, out var sysHandle))
            {
                NativeLibrary.Free(sysHandle);
                return true;
            }
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void EnsureNativeRootPath()
    {
        // 所有平台统一:优先把 MpvApi.RootPath 指向软件目录内打包的 libmpv 副本。
        // Windows:Endpne.LibMPV.Windows 会把 dll 拷到 libmpv\win-x64 / libmpv\win-arm64。
        // macOS/Linux:bundle-mpv-macos.sh 构建后生成 libmpv/ 子目录(自包含依赖树,
        // @loader_path 相对加载),MacFunctionResolver 的最后搜索路径就是 RootPath。
        var baseDir = AppContext.BaseDirectory;

        if (OperatingSystem.IsWindows())
        {
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
        else
        {
            // macOS/Linux 打包副本(bundle-mpv-macos.sh 输出,与 Windows 同级布局)
            var bundled = Path.Combine(baseDir, "libmpv");
            var fileName = OperatingSystem.IsMacOS() ? "libmpv.2.dylib" : "libmpv.so.2";
            if (Directory.Exists(bundled) && File.Exists(Path.Combine(bundled, fileName)))
            {
                MpvApi.RootPath = bundled;
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
            // v4:硬件解码(GPU 解码)。auto-safe = 白名单 hwdec:macOS videotoolbox /
            // Windows d3d11va / Linux vaapi-nvdec;失败自动回退软件解码。
            // GL 渲染路径下解码帧留在 GPU 侧直接互操作;软件渲染路径下 mpv 自动降级软解。
            SetOptionString("hwdec", "auto-safe");
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

    /// <summary>
    /// OpenGL GPU 渲染器(v4):继承 Avalonia 的 <see cref="OpenGlControlBase"/>,由 Avalonia 渲染线程
    /// 持有 GL 上下文时调用 libmpv 的 <c>MPV_RENDER_API_TYPE_OPENGL</c> 渲染 API。
    /// <list type="bullet">
    /// <item><see cref="OnOpenGlInit"/>:渲染线程 → <see cref="MpvContextBase.StartOpenGlRendering"/>
    /// (用 Avalonia 的 <c>GlInterface.GetProcAddress</c> 解析 GL 函数,mpv 直接绘制到当前 framebuffer)。</item>
    /// <item><see cref="OnOpenGlRender"/>:每帧 → <see cref="MpvContextBase.OpenGlRender"/>,
    /// 视频在 GPU 上完成解码(含 hwdec 互操作)与呈现,无 CPU 帧拷贝。</item>
    /// <item>mpv update 回调 → UI 线程 <see cref="RequestNextFrameRendering"/>,驱动下一帧。</item>
    /// </list>
    /// 线程安全:<c>_sync</c> 保证 OnOpenGlRender 与 <see cref="BeginShutdown"/> 互斥
    /// (释放时等待在途帧完成,避免 mpv render context 跨线程释放)。
    /// </summary>
    private sealed class VideoGlRenderer : OpenGlControlBase
    {
        private readonly VideoBackgroundControl _owner;
        private readonly TaskCompletionSource<bool> _glReadyTcs;
        private readonly object _sync = new();
        private bool _glReady;
        private BackgroundMpvContext? _mpv;

        public VideoGlRenderer(VideoBackgroundControl owner, TaskCompletionSource<bool> glReadyTcs)
        {
            _owner = owner;
            _glReadyTcs = glReadyTcs;
        }

        protected override void OnOpenGlInit(GlInterface gl)
        {
            var ctx = _owner.GetMpvForGl();
            if (ctx is null)
            {
                _glReadyTcs.TrySetResult(false);
                return; // 已释放/未就绪:保持不可渲染
            }

            lock (_sync)
            {
                if (_owner._disposed)
                {
                    _glReadyTcs.TrySetResult(false);
                    return;
                }
                _mpv = ctx;
                // GL 函数解析:直接用 Avalonia 的 GlInterface.GetProcAddress(IntPtr (string) 签名一致)
                ctx.StartOpenGlRendering(
                    getProcAddress: gl.GetProcAddress,
                    updateCallback: () => Dispatcher.UIThread.Post(RequestNextFrameRendering),
                    x11Display: IntPtr.Zero,
                    waylandDisplay: IntPtr.Zero);
                _glReady = true;
            }

            _glReadyTcs.TrySetResult(true);
        }

        protected override void OnOpenGlRender(GlInterface gl, int fb)
        {
            lock (_sync)
            {
                if (!_glReady || _mpv is null)
                {
                    return;
                }

                // 渲染目标 = 控件像素尺寸(物理像素)
                var scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
                var w = (int)Math.Ceiling(Bounds.Width * scale);
                var h = (int)Math.Ceiling(Bounds.Height * scale);
                if (w <= 0 || h <= 0)
                {
                    return;
                }

                // 挂起(最小化/隐藏/游戏进行中):跳过 GPU 渲染,mpv 已暂停
                if (_owner._suspended)
                {
                    return;
                }

                try
                {
                    // flipY=1:Avalonia 的 GL surface FBO 原点在左上(Y 翻转),mpv 默认按
                    // 左下原点渲染;不翻转会导致画面上下颠倒(实测 macOS)。
                    _mpv.OpenGlRender(w, h, fb, 1);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[libmpv] GL 渲染帧失败: {ex.Message}");
                }
            }
        }

        protected override void OnOpenGlDeinit(GlInterface gl)
        {
            lock (_sync)
            {
                _glReady = false;
                _mpv = null;
            }
            _glReadyTcs.TrySetResult(false);
        }

        protected override void OnOpenGlLost()
        {
            lock (_sync)
            {
                _glReady = false;
            }
        }

        /// <summary>挂起/恢复:同步 mpv 暂停标志(停解码器线程)。与 SW 路径渲染线程的 pause 逻辑一致。</summary>
        public void ApplySuspend(bool suspend)
        {
            lock (_sync)
            {
                if (_mpv is null || !_glReady)
                {
                    return;
                }
                try
                {
                    _mpv.SetPropertyFlag("pause", suspend);
                }
                catch
                {
                    // mpv 已销毁/属性不可写:忽略
                }
            }
        }

        /// <summary>释放前调用:停止渲染并等待在途帧完成(与 OnOpenGlRender 互斥)。</summary>
        public void BeginShutdown()
        {
            lock (_sync)
            {
                _glReady = false;
                _mpv = null;
            }
            _glReadyTcs.TrySetResult(false);
        }
    }
}
