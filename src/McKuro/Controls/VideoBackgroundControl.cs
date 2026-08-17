using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using LibVLCSharp.Shared;

namespace McKuro.Controls;

/// <summary>
/// 背景封面控件:优先用 LibVLC 播放宣传视频,无可用 native 库或播放失败时回退到首帧静态图。
/// 依赖:系统装有 VLC(macOS/Linux)或应用目录带 VideoLAN.LibVLC native 库(Windows)。
/// 视频不可用不影响其余功能 —— 全程 try-catch,绝不抛出。
/// <para>
/// 渲染方式:通过 LibVLCSharp 的软件回调(vmem)把每帧 RGBA 像素拷贝进托管
/// <see cref="WriteableBitmap"/>,再用普通 <see cref="Image"/> 显示。
/// 不使用 LibVLCSharp.Avalonia 的 <c>VideoView</c>(它内部是 <c>NativeControlHost</c>):
/// native 子窗口被操作系统合成在 Avalonia 渲染表面之上(airspace 问题),
/// 会盖住同窗口内的所有普通控件(ZIndex/不透明/层级均无效) —— 这正是"视频播放时
/// 版本信息/启动按键/渠道/轮播全部消失"的根因。托管 Image 参与正常 ZIndex 层叠,
/// 视频在背景层、UI 在上层,两者可同时可见。
/// </para>
/// </summary>
public sealed class VideoBackgroundControl : Grid
{
    private static readonly Lazy<LibVLC?> LibVlc = new(TryCreateLibVlc);

    private readonly object _sync = new();

    private MediaPlayer? _player;
    private Media? _media;
    private Image? _videoImage;
    private WriteableBitmap? _bitmap;
    private AsyncImage? _fallback;
    private bool _initialized;
    private bool _attached;
    private bool _disposed;

    // 软件渲染帧状态(回调线程写,UI 线程读)
    private byte[]? _staging;
    private GCHandle _stagingHandle;
    private int _frameWidth;
    private int _frameHeight;
    private int _framePitch;
    private int _framePending;

    // 持有委托强引用,防止被 GC 回收(libvlc 内部持有原生回调指针)
    private MediaPlayer.LibVLCVideoFormatCb? _formatCb;
    private MediaPlayer.LibVLCVideoCleanupCb? _cleanupCb;
    private MediaPlayer.LibVLCVideoLockCb? _lockCb;
    private MediaPlayer.LibVLCVideoUnlockCb? _unlockCb;
    private MediaPlayer.LibVLCVideoDisplayCb? _displayCb;

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

    private static LibVLC? TryCreateLibVlc()
    {
        try
        {
            // 显式候选目录探测(含 mac 系统 VLC 路径;找不到再用 LibVLCSharp 标准探测)
            foreach (var directory in CandidateLibVlcDirectories())
            {
                if (ContainsNativeLibVlc(directory))
                {
                    LibVLCSharp.Shared.Core.Initialize(directory);
                    return new LibVLC();
                }
            }

            // Linux 优先走 LibVLCSharp 的标准探测(系统 libvlc 由包管理器提供)。
            if (OperatingSystem.IsLinux())
            {
                LibVLCSharp.Shared.Core.Initialize();
                return new LibVLC();
            }

            // macOS:无应用内 libvlc 时,再试系统 VLC 的默认安装位置。
            if (OperatingSystem.IsMacOS())
            {
                foreach (var macVlc in MacSystemVlcDirs())
                {
                    if (ContainsNativeLibVlc(macVlc))
                    {
                        LibVLCSharp.Shared.Core.Initialize(macVlc);
                        return new LibVLC();
                    }
                }
                // 仍失败:尝试 LibVLCSharp 标准探测(可能用户自定义安装/brew)
                LibVLCSharp.Shared.Core.Initialize();
                return new LibVLC();
            }

            // Windows:上面候选目录已覆盖发布包 libvlc;最后尝试标准探测
            LibVLCSharp.Shared.Core.Initialize();
            return new LibVLC();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"LibVLC 初始化失败(将回退首帧图): {ex.Message}");
            return null;
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

        var libvlc = LibVlc.Value;
        if (libvlc is null)
        {
            // 无 native 库 → 回退首帧图
            _fallback!.ImageUrl = FallbackImageUrl;
            return;
        }

        try
        {
            var player = new MediaPlayer(libvlc)
            {
                // 软件回调(vmem)路径:关闭硬件解码,确保像素回调可用
                EnableHardwareDecoding = false,
                Volume = 0,
            };
            _player = player;

            // 托管 Image 显示软件渲染帧(参与正常 ZIndex,不遮挡 UI)
            _videoImage = new Image
            {
                Stretch = Stretch.UniformToFill,
                IsHitTestVisible = false,
                Opacity = 0,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
            };
            Children.Add(_videoImage);

            // 持有委托强引用 + 注册软件渲染回调(libvlc 解码后把帧写入 vmem 缓冲)
            _formatCb = VideoFormatCb;
            _cleanupCb = VideoCleanupCb;
            _lockCb = VideoLockCb;
            _unlockCb = VideoUnlockCb;
            _displayCb = VideoDisplayCb;
            player.SetVideoFormatCallbacks(_formatCb, _cleanupCb);
            player.SetVideoCallbacks(_lockCb, _unlockCb, _displayCb);

            _media = new Media(libvlc, new Uri(VideoUrl));
            player.EncounteredError += (_, _) => ShowFallback();
            player.EndReached += (_, _) =>
            {
                // LibVLC 事件线程不能直接再次调用播放 API，切换线程后循环播放。
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        player.Time = 0;
                        player.Play();
                    }
                    catch (Exception)
                    {
                        ShowFallback();
                    }
                });
            };
            player.Play(_media);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"视频背景播放失败(回退首帧图): {ex.Message}");
            DisposePlayer();
            _fallback!.ImageUrl = FallbackImageUrl;
        }
    }

    private void DisposePlayer()
    {
        _disposed = true;

        if (_player is not null)
        {
            try
            {
                _player.Stop();
            }
            catch (Exception)
            {
                // 忽略
            }

            try
            {
                _player.Dispose();
            }
            catch (Exception)
            {
                // 忽略
            }

            _player = null;
        }

        _media?.Dispose();
        _media = null;

        lock (_sync)
        {
            _frameWidth = 0;
            _frameHeight = 0;
            _framePitch = 0;
            _staging = null;
            FreeStagingHandle();
            _bitmap?.Dispose();
            _bitmap = null;
        }

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

    // ---- LibVLC vmem 软件渲染回调(libvlc 线程) ----

    private uint VideoFormatCb(ref IntPtr opaque, IntPtr chroma, ref uint width, ref uint height, ref uint pitches, ref uint lines)
    {
        try
        {
            // 请求 RGBA(4 字节/像素,与 Avalonia Rgba8888 直接对应)
            byte[] fourcc = { (byte)'R', (byte)'G', (byte)'B', (byte)'A' };
            Marshal.Copy(fourcc, 0, chroma, 4);

            lock (_sync)
            {
                _frameWidth = (int)width;
                _frameHeight = (int)height;
                _framePitch = (int)(width * 4);
                pitches = (uint)_framePitch;
                lines = height;

                // 分配自管托管缓冲并 pin:libvlc 把帧写入我们的缓冲(Lock 返回其指针),
                // Unlock 时从托管数组读 —— 绝不访问 libvlc 内部缓冲,避免其释放后指针失效崩溃。
                FreeStagingHandle();
                _staging = new byte[_framePitch * _frameHeight];
                _stagingHandle = GCHandle.Alloc(_staging, GCHandleType.Pinned);
            }

            // 在 UI 线程创建/更新位图
            Dispatcher.UIThread.Post(CreateBitmap);
            return 1; // 成功
        }
        catch (Exception)
        {
            return 0; // 失败 → libvlc 终止该输出
        }
    }

    private void VideoCleanupCb(ref IntPtr opaque)
    {
        // 无需额外清理
    }

    /// <summary>释放自管缓冲的 pin 句柄(须在 _sync 锁内调用)。</summary>
    private void FreeStagingHandle()
    {
        if (_stagingHandle.IsAllocated)
        {
            _stagingHandle.Free();
        }
        _stagingHandle = default;
    }

    private IntPtr VideoLockCb(IntPtr opaque, IntPtr planes)
    {
        // 返回我们 pin 的托管缓冲指针(libvlc 直接写入);缓冲始终有效,不依赖 libvlc 内部生命周期
        lock (_sync)
        {
            if (_stagingHandle.IsAllocated)
            {
                return _stagingHandle.AddrOfPinnedObject();
            }
        }
        return IntPtr.Zero;
    }

    private void VideoUnlockCb(IntPtr opaque, IntPtr picture, IntPtr planes)
    {
        try
        {
            // 帧已被 libvlc 写入我们的 _staging(托管数组),从托管读,不触碰任何 native plane
            byte[]? staging;
            int pitch, height;
            lock (_sync)
            {
                if (_staging is null || _framePitch <= 0 || _frameHeight <= 0)
                {
                    return;
                }
                staging = _staging;
                pitch = _framePitch;
                height = _frameHeight;
            }

            int size = pitch * height;
            if (size <= 0 || size > staging.Length)
            {
                return;
            }

            // 合并多帧:同一 UI 迭代只渲染最新一帧,避免刷爆消息队列
            if (Interlocked.Exchange(ref _framePending, 1) == 0)
            {
                Dispatcher.UIThread.Post(RenderFrame);
            }
        }
        catch (Exception)
        {
            // 忽略单帧异常(含回调期间的竞态)
        }
    }

    private void VideoDisplayCb(IntPtr opaque, IntPtr picture)
    {
        // 帧已通过 Unlock 拷出,无需在此再处理
    }

    // ---- UI 线程渲染 ----

    private void CreateBitmap()
    {
        if (_disposed)
        {
            return;
        }

        lock (_sync)
        {
            if (_frameWidth <= 0 || _frameHeight <= 0)
            {
                return;
            }

            if (_bitmap is null
                || _bitmap.PixelSize.Width != _frameWidth
                || _bitmap.PixelSize.Height != _frameHeight)
            {
                _bitmap?.Dispose();
                _bitmap = new WriteableBitmap(
                    new PixelSize(_frameWidth, _frameHeight),
                    new Vector(96, 96),
                    PixelFormats.Rgba8888,
                    AlphaFormat.Opaque);
                if (_videoImage is not null)
                {
                    _videoImage.Source = _bitmap;
                }
            }
        }
    }

    private void RenderFrame()
    {
        Interlocked.Exchange(ref _framePending, 0);
        if (_disposed)
        {
            return;
        }

        lock (_sync)
        {
            if (_bitmap is null || _staging is null)
            {
                return;
            }

            try
            {
                using (var fb = _bitmap.Lock())
                {
                    int srcRowBytes = _framePitch;
                    int dstRowBytes = fb.RowBytes;
                    int height = Math.Min(_frameHeight, fb.Size.Height);
                    int copyRow = Math.Min(srcRowBytes, dstRowBytes);
                    for (int y = 0; y < height; y++)
                    {
                        Marshal.Copy(_staging, y * srcRowBytes, IntPtr.Add(fb.Address, y * dstRowBytes), copyRow);
                    }
                }
            }
            catch (Exception)
            {
                // 忽略渲染异常
            }
        }

        // 首个真实帧到达后才显示视频(此前保持透明,让静态图可见)
        if (_videoImage is not null && _videoImage.Opacity < 1)
        {
            _videoImage.Opacity = 1;
        }
    }

    private static IEnumerable<string> CandidateLibVlcDirectories()
    {
        var baseDirectory = AppContext.BaseDirectory;
        yield return baseDirectory;
        yield return Path.Combine(baseDirectory, "libvlc");
        yield return Path.Combine(baseDirectory, "libvlc", "win-x64");
        yield return Path.Combine(baseDirectory, "Contents", "Frameworks");
        yield return Path.Combine(baseDirectory, "Contents", "Frameworks", "libvlc");
        if (OperatingSystem.IsMacOS())
        {
            foreach (var d in MacSystemVlcDirs())
            {
                yield return d;
            }
        }
    }

    /// <summary>macOS 系统 VLC 的 libvlc 位置(官方安装 + 用户目录安装)。</summary>
    private static IEnumerable<string> MacSystemVlcDirs()
    {
        yield return "/Applications/VLC.app/Contents/MacOS/lib";
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home))
        {
            yield return Path.Combine(home, "Applications", "VLC.app", "Contents", "MacOS", "lib");
        }
        // Homebrew 安装的 vlc
        yield return "/opt/homebrew/lib";
        yield return "/usr/local/lib";
    }

    private static bool ContainsNativeLibVlc(string directory)
        => Directory.Exists(directory)
           && (File.Exists(Path.Combine(directory, "libvlc.dll"))
               || File.Exists(Path.Combine(directory, "libvlc.dylib"))
               || File.Exists(Path.Combine(directory, "libvlc.so")));
}
