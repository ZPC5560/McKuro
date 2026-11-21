using System;
using Avalonia.Threading;

namespace McKuro.Services;

/// <summary>
/// 空闲工作集回收:窗口长时间失焦且无输入时,把可回收的驻留页还给系统。
/// <para>
/// <b>为什么需要它:</b>实测 McKuro 可见时专用工作集约 610 MB,最小化 15 秒后掉到 25 MB
/// —— 也就是说有 <b>380 MB 是可回收页</b>(Skia 字形/图层缓存、ANGLE 临时面、GC 已释放但未交回的堆页、
/// mpv 解码帧池),只是常规路径下没人去要。这些页对正确性无影响,但会一直计入任务管理器数字,
/// 也会在 16 GB 机器上与游戏本体争内存。
/// </para>
/// <para>
/// <b>为什么不是周期性调:</b>见 <see cref="WorkingSetTrimmer"/> 的说明 —— 频繁裁剪会造成反复换页
/// (裁掉的页下次访问要重新从页面文件读回)。这里刻意只在「窗口失焦 + 一段时间没有任何输入」
/// 时触发一次,并且触发后要等冷却期结束才可能再触发,最小间隔 5 分钟。
/// </para>
/// <para>
/// <b>为什么不裁有焦点时:</b>用户正在看页面时裁工作集,下一次滚动/切页会立刻把页读回来,
/// 表现为卡顿。只有「用户已经切到别的程序」这个时刻,裁掉的页才真的不会再被碰。
/// </para>
/// </summary>
public sealed class IdleWorkingSetTrimmer : IDisposable
{
    /// <summary>失焦后要空闲多久才裁一次。太短会在 Alt+Tab 来回时反复裁剪。</summary>
    public static readonly TimeSpan DefaultIdleDelay = TimeSpan.FromMinutes(3);

    /// <summary>两次裁剪之间的最小间隔(冷却期)。</summary>
    public static readonly TimeSpan DefaultCooldown = TimeSpan.FromMinutes(5);

    private readonly TimeSpan _idleDelay;
    private readonly TimeSpan _cooldown;
    private readonly Func<bool> _canTrim;
    private readonly Action _trim;

    private DispatcherTimer? _timer;
    private DateTime _lastTrimUtc = DateTime.MinValue;
    private bool _idle;
    private bool _disposed;

    /// <summary>
    /// 创建回收器。
    /// </summary>
    /// <param name="idleDelay">失焦多久后触发;默认 <see cref="DefaultIdleDelay"/>。</param>
    /// <param name="cooldown">两次触发的最小间隔;默认 <see cref="DefaultCooldown"/>。</param>
    /// <param name="canTrim">额外前置条件(如「游戏会话进行中就不要裁」);返回 false 则本轮跳过。</param>
    /// <param name="trim">实际裁剪动作;默认 <see cref="WorkingSetTrimmer.Trim"/>。</param>
    public IdleWorkingSetTrimmer(
        TimeSpan? idleDelay = null,
        TimeSpan? cooldown = null,
        Func<bool>? canTrim = null,
        Action? trim = null)
    {
        _idleDelay = idleDelay ?? ResolveIdleDelay();
        _cooldown = cooldown ?? DefaultCooldown;
        _canTrim = canTrim ?? (static () => true);
        _trim = trim ?? (() => WorkingSetTrimmer.Trim());
    }

    /// <summary>环境变量:设成 1 时禁用空闲回收(用于对照实测与排查)。</summary>
    public const string DisableEnvVar = "McKuro_DISABLE_IDLE_TRIM";

    /// <summary>环境变量:覆盖失焦后的空闲时长(秒);用于实测与按需调节,非法值忽略。</summary>
    public const string IdleDelayEnvVar = "McKuro_IDLE_TRIM_SECONDS";

    /// <summary>环境变量:覆盖高水位阈值(兆字节);设 0 关闭高水位回收。</summary>
    public const string WatermarkEnvVar = "McKuro_TRIM_WATERMARK_MB";

    /// <summary>
    /// 默认高水位:450 MB。低于它什么都不做 —— 应用自身底噪(无视频无 Live2D)约 310 MB,
    /// 加上图片缓存预算 64 MB 后 450 MB 是「确实偏高」的起点;
    /// 高于它意味着视频会话/大图解码已经把可回收页堆起来了。
    /// </summary>
    public const long DefaultHighWatermarkBytes = 450L * 1024 * 1024;

    /// <summary>从环境变量解析高水位(MB);未设置用默认值,显式 0 表示关闭。</summary>
    public static long ResolveHighWatermark()
    {
        var raw = Environment.GetEnvironmentVariable(WatermarkEnvVar);
        if (long.TryParse(raw, out var mb))
        {
            return mb <= 0 ? 0 : Math.Clamp(mb, 100, 8192) * 1024 * 1024;
        }
        return DefaultHighWatermarkBytes;
    }

    /// <summary>
    /// 从环境变量解析失焦后空闲时长:未设置/非法时用 <see cref="DefaultIdleDelay"/>。
    /// 下限 30 秒(更短会在 Alt+Tab 来回时反复裁剪),上限 1 小时。
    /// </summary>
    public static TimeSpan ResolveIdleDelay()
    {
        if (double.TryParse(Environment.GetEnvironmentVariable(IdleDelayEnvVar), out var seconds)
            && seconds > 0)
        {
            return TimeSpan.FromSeconds(Math.Clamp(seconds, 30, 3600));
        }
        return DefaultIdleDelay;
    }

    /// <summary>是否可用(非禁用状态且未释放)。</summary>
    public bool IsEnabled => !_disposed;

    /// <summary>窗口获得焦点:取消待触发的裁剪,恢复常规行为。</summary>
    public void OnActivated()
    {
        if (_disposed)
        {
            return;
        }
        _idle = false;
        _timer?.Stop();
    }

    /// <summary>
    /// 窗口失去焦点:起一个一次性计时,到点若仍在失焦状态则裁剪一次。
    /// </summary>
    public void OnDeactivated()
    {
        if (_disposed)
        {
            return;
        }
        if (Environment.GetEnvironmentVariable(DisableEnvVar) == "1")
        {
            return;
        }

        _idle = true;
        if (_timer is null)
        {
            _timer = new DispatcherTimer();
            _timer.Tick += OnTimerTick;
        }
        // 一次性:每次 Tick 后自行停止,避免持续轮询
        _timer.Stop();
        _timer.Interval = _idleDelay;
        _timer.Start();
    }

    /// <summary>用户有任何输入(移入窗口/按键):视为已回来,不再裁剪。</summary>
    public void OnUserInput()
    {
        if (_disposed || !_idle)
        {
            return;
        }
        _idle = false;
        _timer?.Stop();
    }

    // ── 高水位回收(与焦点无关)──────────────────────────────────────────────
    //
    // 为什么还需要这一路:失焦回收只解决「用户切走了」;但实测最常见的内存高峰是
    // **用户就盯着启动页看视频**——此时窗口一直有焦点,失焦路径永远不触发,
    // 而进程里有约 380 MB 是可回收页(583 MB 裁完 203 MB,25 秒后回到 272 MB)。
    // 这部分页对画面没有任何作用,只是 Skia/ANGLE/mpv 分配后没还给系统的驻留。
    //
    // 判据用「高水位 + 持续」而不是「定时」:只有真的涨过 BaseBytes 才裁,
    // 且裁完要把水位抬到当前值,避免在同一个水平上反复裁(那才是反复换页)。

    /// <summary>触发高水位回收的水位(字节);<c>0</c> 表示关闭这一路。</summary>
    public long HighWatermarkBytes { get; set; }

    /// <summary>高水位探测间隔(默认 30 秒,足够便宜:一次 WorkingSet64 读)。</summary>
    public TimeSpan HighWatermarkInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 高水位超过后要连续保持多久才裁(默认 2 个探测周期 = 1 分钟)。
    /// 避免一次性峰值(刚切到启动页、视频刚建好会话)被立刻裁掉又被读回来。
    /// </summary>
    public TimeSpan HighWatermarkSustain { get; set; } = TimeSpan.FromMinutes(1);

    private DispatcherTimer? _watermarkTimer;
    private DateTime _overWatermarkSinceUtc = DateTime.MinValue;

    /// <summary>开始监测高水位(<see cref="HighWatermarkBytes"/> 为 0 时不做任何事)。</summary>
    public void StartHighWatermarkWatch()
    {
        if (_disposed || HighWatermarkBytes <= 0 || HighWatermarkInterval <= TimeSpan.Zero)
        {
            return;
        }
        if (Environment.GetEnvironmentVariable(DisableEnvVar) == "1")
        {
            return;
        }
        if (_watermarkTimer is null)
        {
            _watermarkTimer = new DispatcherTimer { Interval = HighWatermarkInterval };
            _watermarkTimer.Tick += OnWatermarkTick;
        }
        if (!_watermarkTimer.IsEnabled)
        {
            _watermarkTimer.Start();
        }
    }

    private void OnWatermarkTick(object? sender, EventArgs e)
    {
        if (_disposed || HighWatermarkBytes <= 0)
        {
            return;
        }

        long current;
        try
        {
            current = Environment.WorkingSet;
        }
        catch (Exception)
        {
            return;
        }

        if (current < HighWatermarkBytes)
        {
            _overWatermarkSinceUtc = DateTime.MinValue;
            return;
        }

        var now = DateTime.UtcNow;
        if (_overWatermarkSinceUtc == DateTime.MinValue)
        {
            _overWatermarkSinceUtc = now;
        }
        if (now - _overWatermarkSinceUtc < HighWatermarkSustain)
        {
            return;
        }
        // 与失焦路径共用冷却,避免两条路径叠着裁
        if (now - _lastTrimUtc < _cooldown)
        {
            return;
        }
        if (!SafeCanTrim())
        {
            return;
        }

        _lastTrimUtc = now;
        _overWatermarkSinceUtc = DateTime.MinValue;
        try
        {
            _trim();
            System.Console.Error.WriteLine(
                $"MCKURO-TRIM watermark-trim done t={DateTime.Now:HH:mm:ss} was={current / 1048576}MB");
        }
        catch (Exception ex)
        {
            System.Console.Error.WriteLine($"MCKURO-TRIM watermark-trim failed: {ex.Message}");
        }
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        _timer?.Stop();
        if (_disposed || !_idle)
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (now - _lastTrimUtc < _cooldown)
        {
            // 冷却期未过:安静跳过,不重排计时(下次失焦会重新计时)
            return;
        }

        if (!SafeCanTrim())
        {
            return;
        }

        _lastTrimUtc = now;
        try
        {
            _trim();
            System.Console.Error.WriteLine(
                $"MCKURO-TRIM idle-trim done t={DateTime.Now:HH:mm:ss}");
        }
        catch (Exception ex)
        {
            // 裁剪失败不影响任何功能,只留一条诊断
            System.Console.Error.WriteLine($"MCKURO-TRIM idle-trim failed: {ex.Message}");
        }
    }

    private bool SafeCanTrim()
    {
        try
        {
            return _canTrim();
        }
        catch (Exception)
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        if (_watermarkTimer is not null)
        {
            _watermarkTimer.Tick -= OnWatermarkTick;
            _watermarkTimer.Stop();
            _watermarkTimer = null;
        }
        if (_timer is not null)
        {
            _timer.Tick -= OnTimerTick;
            _timer.Stop();
            _timer = null;
        }
    }
}
