using Avalonia.Threading;

namespace McKuro.Services;

/// <summary>
/// UI 线程心跳诊断(仅 <c>McKuro_UI_HEARTBEAT=1</c> 时启用,生产零开销):
/// 用一个 50ms 的 DispatcherTimer 测「UI 事件循环的实际间隔」——
/// 间隔远大于 50ms 说明 UI 线程被同步工作/布局风暴/渲染阻塞占住(即"界面卡死"的可量化证据)。
/// 每 5 秒汇总一次最大间隔,超过 <see cref="StallThresholdMs"/> 的单次停顿立刻打印。
/// 输出走 stderr(与 MCKURO-* 其它启动诊断一致)。
/// </summary>
public static class UiHeartbeat
{
    /// <summary>单次停顿告警阈值(ms):正常 50ms 定时器在无阻塞下间隔 ≈50-60ms。</summary>
    private const double StallThresholdMs = 200;

    /// <summary>启动里程碑基准(首次 <see cref="Mark"/> 时确立,用于量化"进程启动 → 首帧"各阶段耗时)。</summary>
    private static long _markBase;

    /// <summary>诊断是否启用(McKuro_UI_HEARTBEAT=1)。</summary>
    public static bool Enabled { get; } = Environment.GetEnvironmentVariable("McKuro_UI_HEARTBEAT") == "1";

    /// <summary>打印带相对耗时的启动/运行里程碑(如 app-init-begin / window-opened / first-render / vm-ctor-end)。</summary>
    public static void Mark(string name)
    {
        if (!Enabled)
        {
            return;
        }
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        if (_markBase == 0)
        {
            _markBase = now;
        }
        var ms = System.Diagnostics.Stopwatch.GetElapsedTime(_markBase, now).TotalMilliseconds;
        System.Console.Error.WriteLine($"MCKURO-MARK {name} +{ms:F0}ms t={DateTime.Now:HH:mm:ss.fff}");
    }

    private static readonly DispatcherTimer Timer = new() { Interval = TimeSpan.FromMilliseconds(50) };

    private static DateTime _lastTick;
    private static DateTime _windowStart;
    private static double _maxGapMs;
    private static int _ticks;
    private static bool _started;

    /// <summary>启用心跳(幂等)。</summary>
    public static void Start()
    {
        if (_started)
        {
            return;
        }
        _started = true;
        _lastTick = DateTime.UtcNow;
        _windowStart = _lastTick;
        System.Console.Error.WriteLine("MCKURO-UI-HEARTBEAT started (interval=50ms, stallThreshold=200ms)");

        Timer.Tick += (_, _) =>
        {
            var now = DateTime.UtcNow;
            var gap = (now - _lastTick).TotalMilliseconds;
            _lastTick = now;
            _ticks++;
            if (gap > _maxGapMs)
            {
                _maxGapMs = gap;
            }
            if (gap > StallThresholdMs)
            {
                System.Console.Error.WriteLine($"MCKURO-UI-STALL gap={gap:F0}ms at {now:HH:mm:ss.fff}");
            }
            var windowSeconds = (now - _windowStart).TotalSeconds;
            if (windowSeconds >= 5)
            {
                System.Console.Error.WriteLine(
                    $"MCKURO-UI-HEARTBEAT {windowSeconds:F1}s ticks={_ticks} maxGap={_maxGapMs:F0}ms avgGap={windowSeconds * 1000 / Math.Max(1, _ticks):F0}ms");
                _windowStart = now;
                _maxGapMs = 0;
                _ticks = 0;
            }
        };
        Timer.Start();
    }

    /// <summary>当前窗口内观测到的最大间隔(供自检脚本读取)。</summary>
    public static double MaxGapMs => _maxGapMs;
}
