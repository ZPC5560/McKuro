using System.Diagnostics;
using System.Runtime.CompilerServices;
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
/// 背景视频解码分辨率档位。
/// <para>
/// 官方宣传片源通常是 2280p:按原分辨率解码时解码帧池/GPU 纹理/驱动映射会让进程
/// 多占 400~500 MB(实测只开视频就从 429 MB 跳到 917 MB)。背景封面在窗口里实际
/// 只显示到约 1100 DIP 宽,1080p 与 4K 肉眼几乎无差,所以默认 1080p。
/// </para>
/// </summary>
public enum VideoRenderQuality
{
    /// <summary>1280 宽上限(低配/小窗口)。</summary>
    Hd720 = 0,

    /// <summary>1920 宽上限(默认)。</summary>
    Hd1080 = 1,

    /// <summary>3840 宽上限(接近源分辨率)。</summary>
    Uhd2160 = 2,

    /// <summary>不限制,按源分辨率解码(内存占用最高)。</summary>
    Native = 3,
}

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

    /// <summary>
    /// 当前渲染线程的运行标志(每个线程一份):停线程只清自己那份。
    /// 共用一个 bool 时,卡住未退出的旧线程会被新会话的 StartRenderThread 重新置 true"复活",与新线程并发渲染。
    /// </summary>
    private StrongBox<bool>? _renderRun;

    /// <summary>
    /// 渲染线程 join 超时时有意泄露的资源(旧线程可能仍在 mpv SoftwareRender 里写 pin 缓冲):
    /// 解除固定/释放 mpv 会让原生代码写进已移动的托管数组或已释放的上下文(堆损坏)。
    /// 静态持有保证 GC 不回收、终结器不运行;只在 mpv 卡死超过 1 秒时发生,泄露有界。
    /// </summary>
    private static readonly List<object> s_abandoned = new();

    // 从 mpv video-params 读取的实际视频尺寸(首帧到达后解析一次);
    // 位图按视频原生分辨率创建,交给 Image 的 UniformToFill 做封面缩放 —— 与原 LibVLC 行为一致。
    private int _videoWidth;
    private int _videoHeight;
    private bool _sizeResolved;
    private bool _firstFrameShown;

    // 播放启动看门狗:规定时间内没有首帧(网络卡死/解码失败无事件)则回退静态图。
    private CancellationTokenSource? _timeoutCts;

    /// <summary>
    /// 当前会话的解码宽度上限(像素;0 = 不限制)。由 <see cref="Quality"/> 在启动会话时快照,
    /// 供 vf 滤镜与软件渲染缓冲共用。背景封面在窗口里实际只显示到约 1100 DIP 宽,
    /// 4K 源按原分辨率解码会让解码帧池/双缓冲常驻数百 MB(实测 +488 MB)。
    /// </summary>
    private int _renderWidthCap;

    /// <summary>
    /// 取 <see cref="Quality"/> 对应的宽度上限并缓存(会话内不变,避免逐帧读 StyledProperty),
    /// 同时按它限制底下静态封面的解码尺寸。
    /// <para>
    /// 封面(官方 FirstFrameImage 是 3840x2280)在视频播放期间一直压在视频层下面,
    /// 原尺寸解码常驻 35 MB 位图 + 同等大小的 GPU 纹理,却从来看不见。开着视频时按与视频
    /// 同一档位解码;不开视频时封面就是正式背景,保持原尺寸。
    /// <paramref name="fullSizeCover"/> 用于 libmpv 不可用等「封面就是最终画面」的场景。
    /// </para>
    /// </summary>
    private void RefreshRenderWidthCap(bool fullSizeCover = false)
    {
        _renderWidthCap = WidthCapFor(Quality);
        // 宽高同取上限 = 长边不超过上限且不放大(AsyncImage 取更严格的一边,源更小时原样解码)
        var coverCap = !fullSizeCover && IsVideoEnabled && !string.IsNullOrWhiteSpace(VideoUrl)
            ? _renderWidthCap
            : 0;
        _fallback?.SetDecodeBounds(coverCap, coverCap);
    }

    // ── 挂起(最小化/隐藏/游戏运行中)──────────────────────────────────────────
    // 软件解码 + 2048x1216 帧拷贝常驻约 1~2 核;不挂起时,点「启动游戏」后(最小化主窗口
    // 或留在启动页)会与 UE 的着色器编译/资源加载抢 CPU 与磁盘,游戏启动明显慢于官方
    // 启动器(官方启动后会自行退出让出资源)。挂起=暂停 mpv 解码并跳过渲染,恢复原位续播。
    private volatile bool _suspended;
    private bool _mpvPausedApplied; // 仅渲染线程读写
    private Window? _watchedWindow;

    // ── 长时间挂起释放 ──────────────────────────────────────────────────────────
    // 挂起只让 mpv 暂停:解码器、帧池、GL FBO 与几十个 mpv 线程仍常驻(实测视频会话约
    // 250 MB 专用内存)。而「挂起」的典型场景恰恰是开着启动器玩几个小时游戏、或最小化到托盘。
    // 挂起超过 SuspendReleaseDelay 就整个销毁会话(露出底下的静态封面),恢复时重建;
    // 短暂最小化再还原不受影响(仍是原位续播)。代价:恢复后视频冷启动约 1 秒、从头播放。
    //
    // 游戏运行中(gameActive)立即释放:用户不可能边玩游戏边看启动器视频,
    // 10 秒宽限毫无意义,反而让视频会话的 ~250 MB 和游戏本体的内存同时常驻。

    /// <summary>最小化/隐藏挂起多久后释放播放会话(短暂最小化再还原不受影响)。</summary>
    internal static readonly TimeSpan SuspendReleaseDelay = TimeSpan.FromSeconds(10);

    /// <summary>游戏运行中立即释放:几乎为零的延迟,让下一个 Dispatcher 节拍立即销毁会话。</summary>
    internal static readonly TimeSpan SuspendReleaseDelayGame = TimeSpan.FromMilliseconds(100);

    /// <summary>挂起释放计时器(仅 UI 线程读写)。</summary>
    private IDisposable? _suspendReleaseTimer;

    /// <summary>当前会话是否因长时间挂起被释放(恢复时据此重建;仅 UI 线程读写)。</summary>
    private bool _releasedForSuspend;

    /// <summary>播放会话代号:每次 TryStartVideo 自增,异步回调只对当前会话生效(防旧会话串台)。</summary>
    private long _imageGeneration;

    /// <summary>已入队的 UI 重绘请求所属会话。</summary>
    private long _postedGeneration;

    /// <summary>当前播放会话代号。</summary>
    private long Generation => Interlocked.Read(ref _imageGeneration);

    /// <summary>
    /// 异步续体守卫:gen 非当前会话即过期。只查 _disposed 不够 —— TryStartVideo 会把它重置为
    /// false,旧会话在途的续体(GL 初始化等待/看门狗/下载回调)醒来后会误判为当前会话,在
    /// 新 mpv 上建第二个渲染上下文(mpv 只允许一个,第二个失败 → GL 层显示未初始化 FBO 垃圾
    /// 帧/整个会话被异常杀死),自定义壁纸启动时官方 URL → 自定义路径连续两次切换必触发。
    /// </summary>
    private bool Stale(long gen) => _disposed || Interlocked.Read(ref _imageGeneration) != gen;

    public static readonly StyledProperty<string> VideoUrlProperty =
        AvaloniaProperty.Register<VideoBackgroundControl, string>(nameof(VideoUrl));

    public static readonly StyledProperty<string> FallbackImageUrlProperty =
        AvaloniaProperty.Register<VideoBackgroundControl, string>(nameof(FallbackImageUrl));

    public static readonly StyledProperty<bool> IsVideoEnabledProperty =
        AvaloniaProperty.Register<VideoBackgroundControl, bool>(nameof(IsVideoEnabled));

    /// <summary>是否静音播放(默认静音;需要声音的场景显式设 false)。</summary>
    public static readonly StyledProperty<bool> IsMutedProperty =
        AvaloniaProperty.Register<VideoBackgroundControl, bool>(nameof(IsMuted), defaultValue: true);

    /// <summary>
    /// 是否循环播放(默认 true,背景封面视频需要)。
    /// 技能演示等"播完即停"场景设 false:播完停在末帧,用户可点击重播。
    /// </summary>
    public static readonly StyledProperty<bool> ShouldLoopProperty =
        AvaloniaProperty.Register<VideoBackgroundControl, bool>(nameof(ShouldLoop), defaultValue: true);

    /// <summary>是否循环播放(默认 true;false 时播完自动暂停)。</summary>
    public bool ShouldLoop
    {
        get => GetValue(ShouldLoopProperty);
        set => SetValue(ShouldLoopProperty, value);
    }

    /// <summary>背景视频解码分辨率档位(默认 1080p)。</summary>
    public static readonly StyledProperty<VideoRenderQuality> QualityProperty =
        AvaloniaProperty.Register<VideoBackgroundControl, VideoRenderQuality>(
            nameof(Quality), defaultValue: VideoRenderQuality.Hd1080);

    /// <summary>
    /// 解码/渲染分辨率档位。
    /// <para>
    /// 官方宣传片源是 2280p(约 4K),按原分辨率解码时 d3d11va 帧池 + ANGLE FBO + 驱动映射
    /// 会让进程常年多占 400~500 MB(实测只开视频就从 429 MB 跳到 917 MB)。背景封面在窗口里
    /// 实际只显示到 ~1100 DIP 宽,因此默认按 1080p 解码即可,肉眼无差别。
    /// </para>
    /// <para>改档会重建播放会话(与换 URL 同路径),因此切档后无需重启应用。</para>
    /// </summary>
    public VideoRenderQuality Quality
    {
        get => GetValue(QualityProperty);
        set => SetValue(QualityProperty, value);
    }

    /// <summary>档位对应的解码宽度上限(像素);<see cref="VideoRenderQuality.Native"/> 返回 0 = 不限制。</summary>
    internal static int WidthCapFor(VideoRenderQuality quality) => quality switch
    {
        VideoRenderQuality.Hd720 => 1280,
        VideoRenderQuality.Hd1080 => 1920,
        VideoRenderQuality.Uhd2160 => 3840,
        _ => 0,
    };

    /// <summary>
    /// 档位是否允许「零拷贝硬件解码直出」。只有 Native 档才直出:直出时 d3d11va 帧池
    /// 必须按源分辨率向 GPU 申请纹理(4K 源约 300~400 MB 常驻),这也是 4K 官方宣传片
    /// 让进程多占四五百 MB 的根因。降档后改为解码帧复制回内存再缩放,内存按显示尺寸走。
    /// </summary>
    internal static bool AllowsZeroCopyHwdec(VideoRenderQuality quality)
        => quality == VideoRenderQuality.Native;

    /// <summary>
    /// 是否正在播放(供 UI 绑定播放/暂停按钮图标)。
    /// <para>播放中 = 已开始且未暂停;播完(ShouldLoop=false)后为 false。
    /// 挂起(最小化/游戏运行中)时也视为非播放中。</para>
    /// </summary>
    public static readonly StyledProperty<bool> IsPlayingProperty =
        AvaloniaProperty.Register<VideoBackgroundControl, bool>(nameof(IsPlaying));

    /// <summary>是否正在播放(UI 可绑定:切换播放/暂停按钮外观)。</summary>
    public bool IsPlaying
    {
        get => GetValue(IsPlayingProperty);
        private set => SetValue(IsPlayingProperty, value);
    }

    /// <summary>用户是否已手动暂停(与播放结束/挂起区分,决定 TogglePause 的下一步动作)。</summary>
    private volatile bool _userPaused;

    /// <summary>是否已播放到结尾(ShouldLoop=false 时;此时 TogglePause 语义为"重播")。</summary>
    private volatile bool _reachedEnd;

    /// <summary>播放结束(ShouldLoop=false 且播到末帧)时触发,供 UI 把按钮切回"播放"态。</summary>
    public event EventHandler? PlaybackEnded;

    /// <summary>
    /// 播放/暂停切换:播放中 → 暂停;暂停中 → 继续;已播完 → 从头重播。
    /// 无视频/未初始化时为空操作。
    /// </summary>
    public void TogglePause()
    {
        lock (_sync)
        {
            if (_mpv is null)
            {
                return;
            }
            try
            {
                if (_reachedEnd)
                {
                    // 已播完:此时"继续"没有意义(停在末帧),语义降级为从头重播
                    _reachedEnd = false;
                    _userPaused = false;
                    _mpv.SetProperty("time-pos", 0d);
                    // 挂起优先(与 Resume/恢复分支同策略):挂起中重播也不真正解除暂停
                    _mpv.SetPropertyFlag("pause", _suspended);
                    IsPlaying = !_suspended;
                    return;
                }
                if (_userPaused)
                {
                    _userPaused = false;
                    // 挂起优先:挂起中不真正恢复解码(避免与省电策略打架)
                    _mpv.SetPropertyFlag("pause", _suspended);
                    IsPlaying = !_suspended;
                }
                else
                {
                    _userPaused = true;
                    _mpv.SetPropertyFlag("pause", true);
                    IsPlaying = false;
                }
            }
            catch (Exception)
            {
                // 播放器状态异常时忽略:不影响其余功能
            }
        }
    }

    /// <summary>显式暂停(无视频/未初始化时为空操作)。</summary>
    public void Pause()
    {
        lock (_sync)
        {
            if (_mpv is null || _userPaused)
            {
                return;
            }
            try
            {
                _userPaused = true;
                _mpv.SetPropertyFlag("pause", true);
                IsPlaying = false;
            }
            catch (Exception)
            {
                // 忽略
            }
        }
    }

    /// <summary>显式继续播放(挂起中不真正恢复;已播完则从头重播)。</summary>
    public void Resume()
    {
        lock (_sync)
        {
            if (_mpv is null)
            {
                return;
            }
            try
            {
                if (_reachedEnd)
                {
                    _reachedEnd = false;
                    _mpv.SetProperty("time-pos", 0d);
                }
                _userPaused = false;
                _mpv.SetPropertyFlag("pause", _suspended);
                IsPlaying = !_suspended;
            }
            catch (Exception)
            {
                // 忽略
            }
        }
    }

    /// <summary>
    /// 从头重播当前视频(技能演示"手动触发再次播放")。
    /// 无视频/未初始化时为空操作。
    /// </summary>
    public void Replay()
    {
        lock (_sync)
        {
            if (_mpv is null)
            {
                return;
            }
            try
            {
                // 回到起点并解除暂停(播完自动暂停后需要显式恢复);挂起中保持挂起策略
                _reachedEnd = false;
                _userPaused = false;
                _mpv.SetProperty("time-pos", 0d);
                _mpv.SetPropertyFlag("pause", _suspended);
                IsPlaying = !_suspended;
            }
            catch (Exception)
            {
                // 播放器状态异常时忽略:不影响其余功能
            }
        }
    }

    /// <summary>是否把视频分辨率同步给宿主窗口比例(启动页全屏背景用 true;
    /// 设置页小尺寸预览用 false,避免预览视频把窗口比例改掉)。默认 true。</summary>
    public static readonly StyledProperty<bool> SyncWindowAspectProperty =
        AvaloniaProperty.Register<VideoBackgroundControl, bool>(nameof(SyncWindowAspect), defaultValue: true);

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

    /// <summary>是否把视频分辨率同步给宿主窗口比例(设置页预览设 false)。</summary>
    public bool SyncWindowAspect
    {
        get => GetValue(SyncWindowAspectProperty);
        set => SetValue(SyncWindowAspectProperty, value);
    }

    /// <summary>是否静音播放(资讯页轮播视频等自动播放场景使用;默认关闭保持原有行为)。</summary>
    public bool IsMuted
    {
        get => GetValue(IsMutedProperty);
        set => SetValue(IsMutedProperty, value);
    }

    /// <summary>
    /// 档位对应的 mpv 缩放滤镜串(<c>vf</c>);<paramref name="widthCap"/> ≤ 0(Native 档)返回 null = 不设滤镜。
    /// <para>
    /// ① 只缩小不放大:宽取 <c>min(iw,cap)</c>。固定写 <c>scale=w=1920</c> 会把 480p 源放大到 1080p
    /// (实测 854x480 → 1920x1080),解码后的每一帧都变大,白多十几 MB 内存、几十 MB 显存。
    /// ② 用 <c>lavfi=[...]</c> 方括号整段引用滤镜图:<c>min(iw,cap)</c> 里的逗号若裸写,
    /// 会被 mpv 当成滤镜分隔符拆开,整个滤镜被静默丢弃;方括号内原样交给 libavfilter,不用转义。
    /// ③ <c>o=[threads=4]</c> 限制滤镜图线程数:默认按 CPU 核数开 swscale 切片线程
    /// (16 核机器实测多 30 个线程、约 17 MB),与解码线程同为 4 足够 4K→1080p 每帧缩放。
    /// </para>
    /// </summary>
    internal static string? ScaleFilterFor(int widthCap)
        => widthCap > 0 ? $"lavfi=[scale=w='min(iw,{widthCap})':h=-2]:o=[threads=4]" : null;

    /// <summary>
    /// 档位是否用 mpv 的 <c>profile=fast</c>(双线性缩放、关抖动等)。
    /// 1080p 及以下档背景封面看不出画质差别,实测省约 85 MB 显存(4K 源;核显机器上显存即内存);
    /// 4K/原生档是用户主动要画质,保持 mpv 默认渲染。
    /// </summary>
    internal static bool UsesFastRenderProfile(VideoRenderQuality quality)
        => quality is VideoRenderQuality.Hd720 or VideoRenderQuality.Hd1080;

    /// <summary>按 <see cref="_renderWidthCap"/> 给 mpv 设置缩放滤镜(见 <see cref="ScaleFilterFor"/>)。</summary>
    private void ApplyScaleFilter(BackgroundMpvContext ctx)
    {
        if (ScaleFilterFor(_renderWidthCap) is not { } vf)
        {
            return;
        }
        try
        {
            ctx.SetOptionString("vf", vf);
        }
        catch (Exception ex)
        {
            System.Console.Error.WriteLine($"MCKURO-VIDEO vf 设置失败(仅影响内存,不影响播放): {ex.Message}");
        }
    }

    /// <summary>
    /// 构造期向 <see cref="BackgroundMpvContext.OnPreInitialize"/> 传递本次会话的档位。
    /// <para>
    /// 为什么用 ThreadStatic 而不是实例字段:基类构造器会在派生类字段赋值<b>之前</b>回调
    /// OnPreInitialize(与 LoopFileOption 必须用字面常量同一约束),那时读实例字段只能拿到默认值。
    /// 而 hwdec 又必须在 mpv_initialize 之前设置、运行期改不了,只能走这条「创建前写线程槽」的路。
    /// 同一线程上 mpv 上下文是串行创建的(TryStartVideo 在 UI 线程),不会串台。
    /// </para>
    /// </summary>
    [ThreadStatic]
    private static VideoRenderQuality t_pendingQuality;

    /// <summary>档位 → 构造期 hwdec 值。降档走 copy 模式,帧池不再按源分辨率向 GPU 申请。</summary>
    private static string HwdecForPendingQuality()
        => AllowsZeroCopyHwdec(t_pendingQuality) ? "auto-safe" : "d3d11va-copy";

    /// <summary>
    /// 仅网络源才启用大缓冲与 CDN 请求头:播放源一律先经 <see cref="EnsureVideoCachedAsync"/>
    /// 落盘为本地文件后再 LoadFile,真正交给 mpv 的总是本地路径 —— 此时 demuxer 缓冲
    /// (50 MB)/缓存秒数纯属常驻内存浪费;本地文件无缓冲耗尽问题,用 mpv 默认小缓冲即可。
    /// 必须在创建播放器后调用(OnPreInitialize 拿不到运行期 VideoUrl,见 LoopFileOption 约定)。
    /// </summary>
    private void ApplyNetworkBufferingOptions(BackgroundMpvContext ctx, string url)
    {
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        try
        {
            // 网络源需要打开 demuxer 缓存(OnPreInitialize 里为省内存设成了 cache=no)。
            ctx.SetOptionString("cache", "yes");
            ctx.SetOptionString("demuxer-max-bytes", "50M");
            ctx.SetOptionString("demuxer-max-back-bytes", "16M");
            ctx.SetOptionString("cache-secs", "30");
            ctx.SetOptionString("demuxer-readahead-secs", "30");
            ctx.SetOptionString("network-timeout", "30");
            // CDN 可能校验请求头(库洛游戏静态资源 CDN)
            ctx.SetOptionString("user-agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
            ctx.SetOptionString("referrer", "https://kurogame.com");
        }
        catch (Exception)
        {
            // 缓冲设置失败不影响播放(默认值兜底)
        }
    }

    /// <summary>按 IsMuted 配置设置 mpv 静音(创建播放器后调用;失败不影响播放)。</summary>
    private void ApplyMuteIfConfigured(BackgroundMpvContext ctx)
    {
        if (!IsMuted)
        {
            return;
        }
        try
        {
            ctx.SetPropertyFlag("mute", true);
        }
        catch (Exception)
        {
            // 静音设置失败不影响播放
        }
    }

    public VideoBackgroundControl()
    {
        ClipToBounds = true;
        _fallback = new AsyncImage
        {
            Stretch = Stretch.UniformToFill,
        };
        Children.Add(_fallback);

        // 注意:三个 Url 类处理器挂在静态构造里(见下方 static ctor),
        // 不要移回实例构造 —— 那是类型级事件,实例构造里挂会随实例数累积不解除的处理器。

        DetachedFromVisualTree += OnDetached;
        LayoutUpdated += OnLayoutDiag;
    }

    /// <summary>
    /// 静态构造:类处理器只挂一次(详见 <c>WebView2Control</c> 上的同名说明)。
    /// 这三个都是「换了 URL 就重新加载」的响应式属性,处理器挂在类型上即可正确工作。
    /// </summary>
    static VideoBackgroundControl()
    {
        VideoUrlProperty.Changed.AddClassHandler<VideoBackgroundControl>((o, e) => o.OnUrlChanged());
        FallbackImageUrlProperty.Changed.AddClassHandler<VideoBackgroundControl>((o, e) => o.OnUrlChanged());
        IsVideoEnabledProperty.Changed.AddClassHandler<VideoBackgroundControl>((o, e) => o.OnUrlChanged());
        // 清晰度档位变化 = 重建播放会话(vf/缓冲尺寸都要重来),与换 URL 同一条路径。
        QualityProperty.Changed.AddClassHandler<VideoBackgroundControl>((o, e) => o.OnUrlChanged());
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
        // v4 GL 路径:_videoImage 恒为 null(无位图),vid/src 无意义 → 报 gl=1 供区分两条渲染路径
        if (_glRenderer is not null)
        {
            var glSample = $"MCKURO-VIDEO gl=1 self=[{Bounds.Width:F0}x{Bounds.Height:F0}] vis={child?.IsEffectivelyVisible}";
            if (glSample != _diagPrev)
            {
                _diagPrev = glSample;
                _diagCnt++;
                System.Console.Error.WriteLine(glSample);
            }
            return;
        }
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
            // 首次加载兜底重试:此刻可能刚挂载、GL 上下文/解码器尚未就绪,
            // TryStartVideo 会静默失败且此后不再触发 —— 表现为"首次不播放,切走再切回才正常"。
            // 延后一拍重试一次:若已成功播放则重试是幂等的(会 Dispose 后重启同一 URL)。
            ScheduleStartRetry();
        }
    }

    /// <summary>首次启动兜底重试(仅当仍未出首帧时执行一次)。</summary>
    private void ScheduleStartRetry()
    {
        var gen = Generation;
        DispatcherTimer.RunOnce(() =>
        {
            // 已出首帧 / 已切走 / 已销毁 / 会话已建起 → 不需要重试。
            // "会话已建起(_mpv 非空)但首帧未到"多半是网络视频还在整段下载(首帧前需先完成下载):
            // 此时重试会 Dispose 掉健康的下载会话重头再来(双倍带宽、首帧更晚 —— 评审反馈),必须跳过;
            // 真正需要重试的是"刚挂载 GL 未就绪、TryStartVideo 静默失败(_mpv 为 null)"的场景。
            if (Stale(gen) || _firstFrameShown || _mpv is not null || !_attached || string.IsNullOrWhiteSpace(VideoUrl))
            {
                return;
            }
            Debug.WriteLine("[libmpv] 首帧未出现,执行一次启动重试");
            TryStartVideo();
        }, TimeSpan.FromMilliseconds(700));
    }

    private void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        AttachedToVisualTree -= OnAttached;
        _attached = true;
        // 先定封面解码尺寸再给 URL:反过来会先按原尺寸解一次 4K 封面并留在 LRU 里
        RefreshRenderWidthCap();
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
        CancelSuspendRelease();
        _releasedForSuspend = false;
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

        if (_suspended)
        {
            // 游戏运行中立即释放(100ms);窗口最小化/隐藏保留 10s 宽限供快速还原
            ScheduleSuspendRelease(gameActive ? SuspendReleaseDelayGame : SuspendReleaseDelay);
            return;
        }
        CancelSuspendRelease();
        if (_releasedForSuspend && _attached)
        {
            // 挂起期间会话已被释放:重建(TryStartVideo 会清掉 _releasedForSuspend)
            TryStartVideo();
            ScheduleStartRetry();
        }
    }

    /// <summary>
    /// 挂起持续 <paramref name="delay"/> 后释放当前播放会话(见字段区说明)。
    /// 已有计时器 / 已释放 / 无会话时为空操作。
    /// </summary>
    private void ScheduleSuspendRelease(TimeSpan delay)
    {
        if (_suspendReleaseTimer is not null || _releasedForSuspend || _mpv is null)
        {
            return;
        }
        var gen = Generation;
        _suspendReleaseTimer = DispatcherTimer.RunOnce(() =>
        {
            _suspendReleaseTimer = null;
            if (Stale(gen) || !_suspended || !_attached || _mpv is null)
            {
                return;
            }
            // 用户手动暂停 / 已播完(技能演示停在末帧)的会话不释放:重建会从头自动播放,
            // 违背用户意图;这两种状态下 mpv 本就处于暂停,只是少省一份内存。
            if (_userPaused || _reachedEnd)
            {
                return;
            }
            System.Console.Error.WriteLine("MCKURO-VIDEO 挂起超时,释放播放会话");
            DisposePlayer();
            IsPlaying = false;
            _releasedForSuspend = true;
        }, delay);
    }

    private void CancelSuspendRelease()
    {
        _suspendReleaseTimer?.Dispose();
        _suspendReleaseTimer = null;
    }

    private void TryStartVideo()
    {
        // 新会话接管:旧会话的挂起释放计时/标记作废(末尾按当前挂起状态重新计时)
        CancelSuspendRelease();
        _releasedForSuspend = false;

        // 先自增 generation 再销毁旧会话:旧 mpv 在 Dispose 中(同步或异步)触发的
        // EndFile/Stop 回调进来时 Stale(旧 gen) 必然成立,不会污染新会话状态。
        // (此前顺序相反:DisposePlayer 与自增之间回调可穿过 _disposed=false + 旧 gen 的窗口)
        _imageGeneration = Interlocked.Increment(ref _imageGeneration);
        DisposePlayer();
        _disposed = false;
        var gen = Generation; // 本会话代号:所有异步回调以此判断是否已过期(见 Stale)
        // 新会话复位播放状态标志:否则"暂停旧视频 → 切换新视频"时,新视频自动播放
        // 但首帧逻辑因 _userPaused 残留 true 而不置 IsPlaying → 播放中显示暂停图标、
        // 控件层常驻(用户反馈"播放中控件仍显示"的另一入口)。
        _userPaused = false;
        _reachedEnd = false;

        // 本会话的解码尺寸档位:在 UI 线程快照一次(vf 与软件缓冲都用它),
        // 避免逐帧/跨线程读 StyledProperty(见 AsyncImage 的同类教训)。
        RefreshRenderWidthCap();

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
            RefreshRenderWidthCap(fullSizeCover: true); // 封面即最终画面,不降档
            ShowFallback(gen);
            return;
        }

        try
        {
            // hwdec 必须在 mpv_initialize 之前设、运行期改不了,而基类构造器又早于实例字段赋值
            // 回调 OnPreInitialize —— 只能先把本次档位写进线程槽,让 OnPreInitialize 读到。
            t_pendingQuality = Quality;
            try
            {
                // 循环与否由**类型**决定,不能传进构造器存字段:基类构造器会在派生字段赋值前
                // 调用 OnPreInitialize,读字段必然拿到默认值 false(详见 BackgroundMpvContext 注释)。
                _mpv = ShouldLoop ? new LoopingMpvContext() : new OneShotMpvContext();
            }
            finally
            {
                t_pendingQuality = VideoRenderQuality.Native; // 复位:避免下次别的会话误读
            }
            ApplyMuteIfConfigured(_mpv);
            ApplyNetworkBufferingOptions(_mpv, VideoUrl);

            // 播放结束 / 错误 → 回退静态图或标记结束态
            _mpv.EndFile += (_, e) =>
            {
                if (e.Reason == MpvEndFileReason.Error)
                {
                    ShowFallback(gen);
                    return;
                }
                // 正常播完(EndOfFile/Stop):ShouldLoop=false 时停在末帧,置结束态并通知 UI。
                // 必须 Stale(gen) 守卫:切换视频时旧会话 Dispose 会触发 Stop 回调,
                // 不判过期就会把"刚开播的新视频"打成未播放(播放中误显暂停图标/控件层,
                // 与角色页演示控件层误判同源);循环视频(ShouldLoop=true)的 Stop/EOF
                // 不是"播完",同样不得上报结束态。状态字段一并挪进 UI 回调内判定后再写。
                if (e.Reason is MpvEndFileReason.EndOfFile or MpvEndFileReason.Stop && !ShouldLoop)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (Stale(gen))
                        {
                            return;
                        }
                        _reachedEnd = true;
                        _userPaused = false;
                        IsPlaying = false;
                        PlaybackEnded?.Invoke(this, EventArgs.Empty);
                    }, DispatcherPriority.Background);
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
            _ = WatchdogAsync(gen, token);

            // v4:优先 OpenGL GPU 渲染(GPU 解码 + GPU 呈现,见 VideoGlRenderer);
            // 平台无 GL 后端/初始化失败时自动回退 v3 软件渲染路径。
            _ = TryGlOrFallbackAsync(gen, token);

            // 在挂起状态下开播(如游戏运行中打开启动页):同样计时释放
            if (_suspended)
            {
                var gameNow = AppServices.GameMonitor.State != GameSessionState.Idle;
                ScheduleSuspendRelease(gameNow ? SuspendReleaseDelayGame : SuspendReleaseDelay);
            }
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
    /// 初始化成功 → GL 路径(GPU 解码+渲染);失败/超时/会话已过期 → 回退软件渲染路径。
    /// </summary>
    private async Task TryGlOrFallbackAsync(long gen, CancellationToken token)
    {
        VideoGlRenderer? gl = null;
        try
        {
            if (_mpv is null || Stale(gen))
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

            // 过期或失败都要先拆除未接管的渲染器:DisposePlayer 只拆 _glRenderer 字段指向的实例,
            // 若本渲染器已完成 GL 初始化但续体尚未运行(字段还没指向它),它会残留视觉树,
            // 每帧继续用已 Dispose 的 mpv 句柄调用 OpenGlRender(use-after-free)。
            // 会话过期则到此为止(绝不能落到 StartSoftwareRenderingPath —— 那会拿当前会话的
            // _mpv 建第二个渲染上下文,mpv 只允许一个,第二个失败 → GL 层垃圾帧/会话被杀)。
            if (!ok || Stale(gen))
            {
                if (gl is not null && !ReferenceEquals(_glRenderer, gl) && Children.Contains(gl))
                {
                    gl.BeginShutdown();
                    Children.Remove(gl);
                }
                if (Stale(gen))
                {
                    return;
                }
                StartSoftwareRenderingPath(gen);
                return;
            }

            // GL 路径成功:渲染器接管显示;首个文件加载完成后取消看门狗并显示视频
            _glRenderer = gl;
            _mpv.FileLoaded += (_, _) => OnGlFileLoaded(gen);
            try
            {
                // 铺满渲染目标(=UniformToFill 的裁切行为;仅 GL 路径生效)
                _mpv.SetOptionString("panscan", "1.0");
                // 按 Quality 档位钳制解码/渲染尺寸:官方宣传片源是 2280p,原分辨率解码时
                // d3d11va 帧池 + ANGLE FBO + 驱动映射会常驻 400~500 MB;背景在窗口里只显示到
                // ~1100 DIP 宽,1080p 完全够用。Native 档不设置 vf(保留原分辨率)。
                ApplyScaleFilter(_mpv);
            }
            catch (Exception)
            {
            }

            _ = LoadLocalVideoAndPlayAsync(gen, VideoUrl, token);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[libmpv] GL 渲染路径失败,回退软件渲染: {ex.Message}");
            if (!Stale(gen))
            {
                if (gl is not null && !ReferenceEquals(_glRenderer, gl) && Children.Contains(gl))
                {
                    gl.BeginShutdown();
                    Children.Remove(gl);
                }
                StartSoftwareRenderingPath(gen);
            }
            else if (gl is not null && !ReferenceEquals(_glRenderer, gl) && Children.Contains(gl))
            {
                // 过期会话的渲染器同样不能残留(见上方注释)
                gl.BeginShutdown();
                Children.Remove(gl);
            }
        }
    }

    /// <summary>GL 路径的首帧信号:文件加载完成 → 取消看门狗、显示 GL 视频层、更新窗口比例。
    /// 带会话代号:过期会话的事件(Dispose 竞态窗口内已入队)不得作用于新会话的渲染器。</summary>
    private void OnGlFileLoaded(long gen)
    {
        if (Stale(gen))
        {
            return;
        }
        _timeoutCts?.Cancel();
        Dispatcher.UIThread.Post(() =>
        {
            if (Stale(gen))
            {
                return;
            }
            if (_glRenderer is not null)
            {
                _glRenderer.Opacity = 1;
            }
            // GL 路径的"真的开始播放"信号 —— 与软件路径的首帧处理保持一致。
            // 不设置的话,自动播放时 IsPlaying 一直为 false,UI 侧(技能演示的控件层显隐、
            // 播放/暂停按钮图标)会误判为"未播放",表现为播放中控件仍然显示。
            _reachedEnd = false;
            _firstFrameShown = true;
            if (!_userPaused && !_suspended)
            {
                IsPlaying = true;
            }
            if (_mpv is not null)
            {
                _ = PollVideoAspectAndPropagateAsync(gen);
                MaybeScheduleTranscode(gen);
            }
        });
    }

    /// <summary>
    /// 轮询读取视频显示比例并传播:优先 video-params/aspect(显示比例 DAR,含变形宽银幕),
    /// 回退 w/h(编码像素尺寸);参数在首帧解码后才可用,FileLoaded 时刻可能尚未就绪,
    /// 最多重试 8 次×500ms。宿主按该比例自适应高度后 mpv 画面恰好填满、无黑边。
    /// </summary>
    private async Task PollVideoAspectAndPropagateAsync(long gen)
    {
        for (var attempt = 0; attempt < 8 && !Stale(gen); attempt++)
        {
            if (_mpv is not null)
            {
                try
                {
                    double dar = _mpv.GetProperty<double>("video-params/aspect");
                    if (dar is > 0.01 and < 10)
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            if (!Stale(gen))
                            {
                                PropagateVideoAspectRatio((int)Math.Round(dar * 1000), 1000);
                            }
                        });
                        return;
                    }
                    int w = _mpv.GetProperty<int>("video-params/w");
                    int h = _mpv.GetProperty<int>("video-params/h");
                    if (w > 0 && h > 0)
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            if (!Stale(gen))
                            {
                                PropagateVideoAspectRatio(w, h);
                            }
                        });
                        return;
                    }
                }
                catch
                {
                    // 参数尚未就绪:稍后重试
                }
            }
            await Task.Delay(500).ConfigureAwait(false);
        }
    }

    /// <summary>GL 渲染线程获取当前 mpv 上下文(渲染器初始化/渲染时调用;可能为 null:已释放)。</summary>
    private BackgroundMpvContext? GetMpvForGl()
    {
        lock (_sync)
        {
            return _disposed ? null : _mpv;
        }
    }

    /// <summary>软件渲染路径(v3 回退):位图呈现 + 专用渲染线程。调用方已验证会话未过期。</summary>
    private void StartSoftwareRenderingPath(long gen)
    {
        try
        {
            if (_mpv is null || Stale(gen))
            {
                return;
            }

            if (_videoImage is null)
            {
                _videoImage = new Image
                {
                    Stretch = Stretch.UniformToFill,
                    IsHitTestVisible = false,
                    Opacity = 0,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                };
                Children.Add(_videoImage);
            }

            // 软件渲染同样按档位缩放解码输出:pinned 缓冲与 WriteableBitmap
            // 都按 TryResolveVideoSize 钳制后的尺寸分配(双缓冲),与 GL 路径同一上限。
            ApplyScaleFilter(_mpv);

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
            _ = LoadLocalVideoAsync(gen, url, _timeoutCts?.Token ?? CancellationToken.None);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"软件渲染启动失败(回退首帧图): {ex.Message}");
            DisposePlayer();
            _fallback!.ImageUrl = FallbackImageUrl;
        }
    }

    /// <summary>GL 路径:下载视频到本地缓存目录,完成后直接 LoadFile(mpv 命令 API 线程安全,任意线程可调)。</summary>
    private async Task LoadLocalVideoAndPlayAsync(long gen, string url, CancellationToken token)
    {
        try
        {
            var localPath = await EnsureVideoCachedAsync(gen, url, token).ConfigureAwait(false);
            if (Stale(gen) || localPath is null || token.IsCancellationRequested)
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
            ShowFallback(gen);
        }
    }

    /// <summary>
    /// 解析视频源为本地文件路径(GPU 渲染路径专用):
    /// 本地文件(自定义动态壁纸:本地视频 / Wallpaper Engine 包内视频)直接返回;
    /// http(s) 下载到本地缓存目录(CDN 流式缓冲不可靠,本地文件最稳);缓存命中直接复用。
    /// </summary>
    private async Task<string?> EnsureVideoCachedAsync(long gen, string url, CancellationToken token)
    {
        // 本地文件分支必须在 HttpClient 之前:本地路径不是合法 http URI,GetByteArrayAsync 会抛异常。
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            if (File.Exists(url))
            {
                return url;
            }
            ShowFallback(gen);
            return null;
        }

        var cacheDir = VideoCache.DefaultDirectory;
        Directory.CreateDirectory(cacheDir);
        var localPath = VideoCache.PathFor(cacheDir, url);

        // 已有本档位的降分辨率副本(见 VideoTranscoder):直接播副本。原片不再刷新使用时间,
        // 按 LRU 自然过期;切到更高档位时若原片已被清理,重新下载一次即可。
        var cap = _renderWidthCap;
        var transcoded = cap > 0 ? VideoCache.TranscodedPathFor(localPath, cap) : null;
        if (transcoded is not null && VideoTranscoder.IsUsable(transcoded))
        {
            SchedulePruneOnce(cacheDir, transcoded);
            VideoCache.Touch(transcoded);
            Volatile.Write(ref _cacheState, new CacheState(gen, localPath, transcoded, cap, IsTranscoded: true));
            System.Console.Error.WriteLine($"MCKURO-VIDEO 使用转码副本 {Path.GetFileName(transcoded)}");
            return transcoded;
        }
        SchedulePruneOnce(cacheDir, localPath);
        // 播原片:首帧后若源宽超过档位上限,后台转一份副本(MaybeScheduleTranscode)
        Volatile.Write(ref _cacheState,
            transcoded is null ? null : new CacheState(gen, localPath, transcoded, cap, IsTranscoded: false));

        // 只有完整下载的文件才会出现在最终文件名下(临时文件 + 原子替换),非空即可信
        if (VideoCache.IsUsable(localPath))
        {
            VideoCache.Touch(localPath); // 刷新最近使用时间,供清理按 LRU 淘汰
            return localPath;
        }

        // 下载专用客户端:无 60 秒总超时(慢网大视频也能下完),挂死由 VideoCache 的空闲超时兜底;
        // 流式写盘,不再整段缓冲在内存。取消/失败不会留下半截文件。
        var ok = await VideoCache.DownloadAsync(AppServices.DownloadHttp, url, localPath, token).ConfigureAwait(false);
        if (Stale(gen))
        {
            return null; // 下载期间会话已过期:不播放(完整文件已入缓存,下次直接命中)
        }
        if (!ok)
        {
            ShowFallback(gen);
            return null;
        }
        return localPath;
    }

    /// <summary>
    /// 本会话播放的缓存文件与对应的转码副本(<see cref="EnsureVideoCachedAsync"/> 在后台线程写入,
    /// 首帧后 <see cref="MaybeScheduleTranscode"/> 与失败回退 <see cref="ShowFallback"/> 读取;按会话代号作废)。
    /// </summary>
    private sealed record CacheState(long Gen, string Source, string Target, int Cap, bool IsTranscoded);

    private CacheState? _cacheState; // 跨线程:一律经 Volatile/Interlocked 读写

    /// <summary>
    /// 首帧后:本会话播的是原片且源宽超过档位上限 → 后台转一份降分辨率副本,下次会话直接用。
    /// 每个会话只生效一次(循环播放会再次触发 FileLoaded;CompareExchange 取走候选)。
    /// </summary>
    private void MaybeScheduleTranscode(long gen)
    {
        if (Volatile.Read(ref _cacheState) is not { IsTranscoded: false } state || state.Gen != gen
            || Interlocked.CompareExchange(ref _cacheState, null, state) != state)
        {
            return;
        }
        _ = Task.Run(async () =>
        {
            // video-params 在首帧解码后才可用(与 PollVideoAspectAndPropagateAsync 同样重试)
            for (var attempt = 0; attempt < 8 && !Stale(gen); attempt++)
            {
                int width;
                try
                {
                    width = _mpv?.GetProperty<int>("video-params/w") ?? 0;
                }
                catch (Exception)
                {
                    width = 0;
                }
                if (width > 0)
                {
                    long bytes;
                    try
                    {
                        bytes = new FileInfo(state.Source).Length;
                    }
                    catch (Exception)
                    {
                        return;
                    }
                    if (VideoCache.ShouldTranscode(width, state.Cap, bytes))
                    {
                        VideoTranscoder.Enqueue(state.Source, state.Target, state.Cap);
                    }
                    return;
                }
                await Task.Delay(500).ConfigureAwait(false);
            }
        });
    }

    /// <summary>每个进程只清理一次视频缓存(后台执行,不阻塞播放)。</summary>
    private static int s_pruneScheduled;

    private static void SchedulePruneOnce(string cacheDir, string keepPath)
    {
        if (Interlocked.Exchange(ref s_pruneScheduled, 1) == 1)
        {
            return;
        }
        _ = Task.Run(() => VideoCache.Prune(cacheDir, DateTime.UtcNow, keepPath));
    }

    /// <summary>下载视频到本地缓存目录,完成后用本地文件播放(软件渲染路径专用:排队给渲染线程)。</summary>
    private async Task LoadLocalVideoAsync(long gen, string url, CancellationToken token)
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
                    ShowFallback(gen);
                }
                return;
            }

            var localPath = await EnsureVideoCachedAsync(gen, url, token).ConfigureAwait(false);
            if (Stale(gen) || localPath is null || token.IsCancellationRequested)
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
            ShowFallback(gen);
        }
    }

    /// <summary>播放启动看门狗:15s 内无首帧则回退静态图;会话过期时静默退出(不作用于新会话)。</summary>
    private async Task WatchdogAsync(long gen, CancellationToken token)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(15), token).ConfigureAwait(false);
            if (!Stale(gen))
            {
                ShowFallback(gen);
            }
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
        var renderExited = StopRenderThread();
        if (!renderExited)
        {
            // 渲染线程 1 秒内未退出(多半卡在 mpv 内部):它可能仍持有 _sync 并在 SoftwareRender 中
            // 使用 mpv 与 pin 缓冲 —— 此时释放 mpv/位图是 use-after-free。有意泄露(见 s_abandoned),
            // 且不取 _sync(否则 UI 线程会陪着卡住)。
            Debug.WriteLine("[libmpv] 渲染线程未按时退出:泄露 mpv 会话与渲染缓冲以避免堆损坏");
            if (_mpv is not null)
            {
                lock (s_abandoned)
                {
                    s_abandoned.Add(_mpv);
                }
                _mpv = null;
            }
            _bitmap = null;
        }

        if (_mpv is { } mpv)
        {
            // 先清字段再 Dispose:GL 渲染器的 OnOpenGlDeinit 以「仍是宿主当前 mpv」为条件才在 GL 线程释放渲染上下文,
            // 若 Deinit 被延后到这之后才执行,它看到 null 就不会再碰正在/已经 Dispose 的上下文。
            Volatile.Write(ref _mpv, null);
            // libmpv render API 是嵌入式一等 API:MpvContext.Dispose() 内部 StopRendering(停帧+释放 render context,
            // GL 路径下通常已在 OnOpenGlDeinit 里释放过)+ TerminateDestroy(mpv_terminate_destroy)。
            try
            {
                mpv.Dispose();
            }
            catch (Exception)
            {
                // 忽略释放异常
            }
        }

        if (renderExited)
        {
            lock (_sync)
            {
                _bitmap?.Dispose();
                _bitmap = null;
            }
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

    /// <summary>回退静态封面。带会话代号:过期会话的失败回调(Dispose 竞态窗口内已入队)
    /// 不得隐藏新会话正在播放的视频(旧实现无差别作用于当前 _glRenderer/_videoImage)。</summary>
    private void ShowFallback(long gen)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (Stale(gen))
            {
                return;
            }

            // 本会话播的是转码副本却失败了:停用这份副本(下次回到原片并重转),
            // 不能让一份坏副本把视频永久变成静态图
            if (Volatile.Read(ref _cacheState) is { IsTranscoded: true } state && state.Gen == gen)
            {
                VideoTranscoder.MarkBad(state.Target);
            }

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
        if (!StopRenderThread())
        {
            // 旧线程卡死未退出:其缓冲已被放弃(AbandonRenderBuffer),新线程用新缓冲,互不干扰
            Debug.WriteLine("[libmpv] 旧渲染线程未退出,已放弃其缓冲");
        }
        var run = new StrongBox<bool>(true);
        _renderRun = run;
        _renderThread = new Thread(() => RenderLoop(run))
        {
            IsBackground = true,
            Name = "McKuroVideoRender",
        };
        _renderThread.Start();
    }

    /// <summary>停止渲染线程并释放渲染缓冲。返回 false = 线程 1 秒内未退出(缓冲已被有意泄露,调用方不得释放 mpv)。</summary>
    private bool StopRenderThread()
    {
        if (_renderRun is { } run)
        {
            Volatile.Write(ref run.Value, false);
        }
        _renderRun = null;
        try
        {
            _frameSignal.Set();
        }
        catch (ObjectDisposedException)
        {
        }
        var exited = true;
        if (_renderThread is { IsAlive: true })
        {
            // 等待渲染线程退出(最多 1s),避免 Dispose 时正占用 mpv render context
            exited = _renderThread.Join(1000);
        }
        _renderThread = null;
        if (exited)
        {
            ReleaseRenderBuffer();
            lock (_sync)
            {
                _pendingVideoPath = null;
            }
        }
        else
        {
            // 超时:线程可能仍在 SoftwareRender 中写 _bufferPtr —— 不能解除固定(堆损坏),也不能取 _sync(会陪着卡住)
            AbandonRenderBuffer();
            _pendingVideoPath = null;
        }
        _postedGeneration = 0;
        return exited;
    }

    /// <summary>
    /// 专用视频线程:定时处理 LoadFile 并持续调用 SoftwareRender。
    /// mpv 回调只负责唤醒线程，回调丢失也不会让播放停住；UI 线程只接收最新帧。
    /// </summary>
    private void RenderLoop(StrongBox<bool> run)
    {
        while (Volatile.Read(ref run.Value))
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

            if (!Volatile.Read(ref run.Value) || _disposed)
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

    /// <summary>解除固定并释放渲染缓冲。取 <c>_sync</c>:与渲染线程的 SoftwareRender/帧拷贝互斥
    /// (可重入:EnsureRenderBuffer 已在锁内调用)。</summary>
    private void ReleaseRenderBuffer()
    {
        lock (_sync)
        {
            if (_bufferHandle.IsAllocated)
            {
                _bufferHandle.Free();
                _bufferHandle = default;
            }
            _renderBuffer = null;
            _bufferPtr = IntPtr.Zero;
        }
    }

    /// <summary>放弃(不释放)当前渲染缓冲:pin 句柄与数组进 <see cref="s_abandoned"/> 常驻,原生代码可继续安全写入。</summary>
    private void AbandonRenderBuffer()
    {
        if (_bufferHandle.IsAllocated || _renderBuffer is not null)
        {
            lock (s_abandoned)
            {
                // GCHandle 是值类型:装箱保存即可保持固定(从不 Free)
                s_abandoned.Add(_bufferHandle);
                if (_renderBuffer is not null)
                {
                    s_abandoned.Add(_renderBuffer);
                }
            }
        }
        _bufferHandle = default;
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
                // 首帧到达 = 真的开始播放了。必须在这里把 IsPlaying 置真:
                // automatic 播放(未经过 Replay/TogglePause)此前从不设置该状态,
                // 于是 UI 侧(技能演示的控件层显隐、播放/暂停图标)会一直误判为"未播放",
                // 表现为"自动播放时播放控件仍然显示"。
                _reachedEnd = false;
                if (!_userPaused && !_suspended)
                {
                    IsPlaying = true;
                }
                MaybeScheduleTranscode(gen);
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

            // 软件渲染的 pinned 缓冲 + WriteableBitmap 都按此尺寸分配(每帧两份,双缓冲)。
            // 按 Quality 档位钳制:4K 源单帧 33 MB,两份缓冲加解码侧缩放帧即 ~100 MB;
            // 显示上限只有主窗口 ~1100 DIP,1080p 档完全够用、肉眼无差。
            if (_renderWidthCap > 0 && w > _renderWidthCap)
            {
                h = (int)((long)h * _renderWidthCap / w);
                w = _renderWidthCap;
            }

            _videoWidth = w;
            _videoHeight = h;
            _sizeResolved = true;
            // 软件渲染线程内解析:派发到 UI 线程同步窗口比例(带会话守卫,过期会话不改窗口)
            var gen = Generation;
            Dispatcher.UIThread.Post(() =>
            {
                if (!Stale(gen))
                {
                    PropagateVideoAspectRatio(w, h);
                }
            });
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>视频分辨率已解析(宽,高,UI 线程触发);宿主可据此做比例自适应布局(无论 SyncWindowAspect 开关)。</summary>
    public event Action<double, double>? VideoAspectRatioResolved;

    private void PropagateVideoAspectRatio(int videoWidth, int videoHeight)
    {
        if (videoWidth <= 0 || videoHeight <= 0 || _disposed)
        {
            return;
        }
        // 先通知宿主做比例自适应(如资讯页轮播控件按视频比例改变高度),再按需同步窗口比例
        try
        {
            VideoAspectRatioResolved?.Invoke(videoWidth, videoHeight);
        }
        catch (Exception)
        {
            // 宿主布局失败不影响播放
        }
        if (!SyncWindowAspect)
        {
            return;
        }
        // 宿主窗口可能是 MainWindow(启动页);非窗口宿主(测试/预览)忽略
        if (TopLevel.GetTopLevel(this) is MainWindow win)
        {
            try
            {
                win.SetContentAspectRatio(videoWidth, videoHeight);
            }
            catch (Exception)
            {
                // 窗口尺寸调整失败不影响视频播放
            }
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
    /// 带封面播放选项的 libmpv 上下文基类。基类构造顺序:mpv_create → <see cref="OnPreInitialize"/>
    /// (设置必须在 mpv_initialize 之前的核心选项)→ mpv_initialize → 事件循环。
    /// <c>vo=libmpv</c> 必须在此设置,render API 才能接管输出;其余为后台封面播放的合理默认。
    /// <para>
    /// <b>为什么循环与否用两个子类表达,而不是构造器传 bool 存进字段</b>:
    /// <c>MpvContextBase</c> 的构造器会在<b>派生类构造器体执行之前</b>调用本方法(虚方法),
    /// 所以此刻"在构造器里赋值的实例字段"仍然是默认值 <c>false</c>。把 loop 存进字段再在
    /// <see cref="OnPreInitialize"/> 里读它,<c>loop-file</c> 就会恒被写成 <c>"no"</c>
    /// —— 背景视频永远不循环,播完一遍即停在末帧(用户报告的"视频卡住/不循环";
    /// 实测:传入 loop=true 时 <c>options/loop-file</c> 实际为 <c>"no"</c>)。
    /// 因此这里要求子类用<b>字面常量</b>提供选项值,与构造顺序无关。
    /// </para>
    /// </summary>
    private abstract class BackgroundMpvContext : MpvContext
    {
        /// <summary>
        /// <c>loop-file</c> 选项值。<b>必须是字面常量,绝不能读构造器里赋值的实例字段</b>
        /// (字段此刻尚未赋值,原因见类注释)。
        /// </summary>
        protected abstract string LoopFileOption { get; }

        protected override void OnPreInitialize()
        {
            // 渲染 API 要求 vo=libmpv 在 initialize 前设置
            SetOptionString("vo", "libmpv");
            // 低档位用 mpv 内置 fast 预设(见 UsesFastRenderProfile)。必须先于下面的显式选项:
            // 预设只是一组默认值,后设的 hwdec 等仍以本方法为准。旧版 mpv 无此预设时忽略即可。
            if (UsesFastRenderProfile(t_pendingQuality))
            {
                try
                {
                    SetOptionString("profile", "fast");
                }
                catch (Exception)
                {
                    // 预设不存在:保持 mpv 默认渲染,不影响播放
                }
            }
            SetOptionString("audio", "no");
            // v4:硬件解码(GPU 解码)。auto-safe = 白名单 hwdec:macOS videotoolbox /
            // Windows d3d11va / Linux vaapi-nvdec;失败自动回退软件解码。
            // GL 渲染路径下解码帧留在 GPU 侧直接互操作;软件渲染路径下 mpv 自动降级软解。
            // 注意:降档(默认 1080p)时改走 copy 模式,避免零拷贝帧池按 4K 源分辨率撑爆内存,
            // 档位由 TryStartVideo 在创建上下文前写入线程槽(见 HwdecForPendingQuality)。
            SetOptionString("hwdec", HwdecForPendingQuality());
            // 循环:背景封面视频需要 inf;技能演示设 no —— 播完停在末帧等用户重播
            SetOptionString("loop-file", LoopFileOption);
            SetOptionString("volume", "0");
            // 解码侧限制:背景封面不需要「最高画质 + 大量预解码帧」。
            // vd-lavc-threads 控制解码线程数,线程越多每线程各自的帧缓冲/上下文越多;
            // 对背景封面这种低帧率静态感画面,4 线程足够,能显著压低解码器常驻内存。
            SetOptionString("vd-lavc-threads", "4");
            // 硬件解码的帧池按【源分辨率】向 GPU 申请,是 4K 源常驻数百 MB 的根因。
            // 关掉 hwdec 的直接输出(d3d11va 的零拷贝路径必须按源尺寸建纹理池),
            // 改为 GPU 解码后拉回系统内存再按 vf 缩放 —— 内存换 CPU,但背景封面
            // 帧率/画质无感,而内存从"按源分辨率"降到"按显示分辨率"。
            SetOptionString("hwdec-extra-frames", "2");
            // 只保留最小前向缓存:官方宣传片是本地文件,不需要预读大缓冲。
            // (网络源的大缓冲另由 ApplyNetworkBufferingOptions 按需打开。)
            SetOptionString("cache", "no");
            SetOptionString("demuxer-max-bytes", "16M");
            SetOptionString("demuxer-max-back-bytes", "4M");
            // 网络缓冲/请求头不在这里设置:OnPreInitialize 在基类构造器里回调,
            // 此刻拿不到运行期的 VideoUrl(与 LoopFileOption 必须用字面常量同一原因,
            // 见类注释)。网络源才需要的大缓冲挪到 TryStartVideo 创建上下文后按需设置。
        }
    }

    /// <summary>循环播放上下文:启动页封面 / 资讯页轮播 / 设置页壁纸预览等"一直循环"场景。</summary>
    private sealed class LoopingMpvContext : BackgroundMpvContext
    {
        protected override string LoopFileOption => "inf";
    }

    /// <summary>单次播放上下文:技能演示等"播完停在末帧、由用户点重播"场景。</summary>
    private sealed class OneShotMpvContext : BackgroundMpvContext
    {
        protected override string LoopFileOption => "no";
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
    /// <para>
    /// 释放顺序:libmpv OpenGL 后端要求释放渲染上下文时其 GL 上下文是当前上下文。
    /// 所以 <see cref="BeginShutdown"/> 只停帧,渲染上下文在 <see cref="OnOpenGlDeinit"/>
    /// (Avalonia 已把 GL 上下文设为当前)里经 <see cref="MpvContextBase.StopRendering"/> 释放;
    /// 宿主随后再 Dispose mpv 核心(此时 render context 已为空,Dispose 不会再在非 GL 线程释放它)。
    /// </para>
    /// </summary>
    private sealed class VideoGlRenderer : OpenGlControlBase
    {
        private readonly VideoBackgroundControl _owner;
        private readonly TaskCompletionSource<bool> _glReadyTcs;
        private readonly object _sync = new();
        private bool _glReady;
        private BackgroundMpvContext? _mpv;

        /// <summary>BeginShutdown 已调用:不再渲染,但保留 _mpv 供 OnOpenGlDeinit 在 GL 上下文内释放渲染上下文。</summary>
        private bool _shuttingDown;

        /// <summary>GL 上下文已丢失:此时不能再调用 GL 相关释放,交给 mpv Dispose 兜底。</summary>
        private bool _contextLost;

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
                _owner.ApplyMuteIfConfigured(ctx);
                try
                {
                    // GL 函数解析:直接用 Avalonia 的 GlInterface.GetProcAddress(IntPtr (string) 签名一致)
                    ctx.StartOpenGlRendering(
                        getProcAddress: gl.GetProcAddress,
                        updateCallback: () => Dispatcher.UIThread.Post(RequestNextFrameRendering),
                        x11Display: IntPtr.Zero,
                        waylandDisplay: IntPtr.Zero);
                    _glReady = true;
                }
                catch (Exception ex)
                {
                    // 渲染上下文创建失败(mpv 只允许一个 render context;重复创建返回错误):
                    // 不能把异常抛进 Avalonia 渲染线程 —— 保持未就绪,让上层 5s 超时后回退软件渲染。
                    Debug.WriteLine($"[libmpv] GL 渲染上下文创建失败: {ex.Message}");
                    _mpv = null;
                    _glReady = false;
                    _glReadyTcs.TrySetResult(false);
                    return;
                }
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
                var ctx = _mpv;
                var hadContext = _glReady || _shuttingDown;
                _glReady = false;
                _mpv = null;
                // GL 上下文此刻为当前上下文:在这里释放 mpv 渲染上下文(StopRendering 停回调并 free render context)。
                // 只释放本渲染器创建过的上下文;上下文已丢失时跳过,交给 mpv Dispose 兜底。
                // 还要求 ctx 仍是宿主当前的 mpv(正常 DisposePlayer 顺序:先移除本控件 → 本回调 → 再 Dispose mpv);
                // 过期会话的 mpv 可能已被 Dispose,不能再碰。
                if (ctx is not null && hadContext && !_contextLost && ReferenceEquals(ctx, Volatile.Read(ref _owner._mpv)))
                {
                    try
                    {
                        ctx.StopRendering();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[libmpv] GL 渲染上下文释放失败: {ex.Message}");
                    }
                }
            }
            _glReadyTcs.TrySetResult(false);
        }

        protected override void OnOpenGlLost()
        {
            lock (_sync)
            {
                _glReady = false;
                _contextLost = true;
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

        /// <summary>
        /// 释放前调用:停止渲染并等待在途帧完成(与 OnOpenGlRender 互斥)。
        /// 不清 _mpv:渲染上下文须由随后移出视觉树触发的 <see cref="OnOpenGlDeinit"/> 在 GL 上下文内释放。
        /// </summary>
        public void BeginShutdown()
        {
            lock (_sync)
            {
                _shuttingDown = _glReady;
                _glReady = false;
                if (!_shuttingDown)
                {
                    _mpv = null; // 从未建立渲染上下文:没有需要在 GL 线程释放的东西
                }
            }
            _glReadyTcs.TrySetResult(false);
        }
    }
}
