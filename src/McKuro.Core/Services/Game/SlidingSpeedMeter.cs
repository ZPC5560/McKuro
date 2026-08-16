namespace McKuro.Core.Services.Game;

/// <summary>
/// 滑动窗口速率计:按固定采样间隔记录 (时间, 累计字节) 采样点,
/// 速率 = (窗口内最新字节 - 窗口起点字节) / 窗口内耗时。
/// <para>
/// 比"瞬时速率"平滑,比"全程平均"灵敏;线程安全(内部 lock)。
/// 时间源用 <see cref="TimeProvider"/>,便于单元测试注入假时钟。
/// </para>
/// </summary>
public sealed class SlidingSpeedMeter
{
    private readonly object _lock = new();
    private readonly TimeProvider _time;
    private readonly TimeSpan _window;
    private readonly TimeSpan _interval;
    private readonly Queue<(long Timestamp, long Bytes)> _samples = new();
    private bool _hasSample;
    private long _lastSampleTimestamp;
    private long _lastBytes;

    /// <param name="window">速率统计窗口(默认 5 秒)。</param>
    /// <param name="interval">采样间隔(默认 500ms)。</param>
    /// <param name="time">时间源(默认系统时钟)。</param>
    public SlidingSpeedMeter(TimeSpan? window = null, TimeSpan? interval = null, TimeProvider? time = null)
    {
        _window = window ?? TimeSpan.FromSeconds(5);
        _interval = interval ?? TimeSpan.FromMilliseconds(500);
        _time = time ?? TimeProvider.System;
    }

    /// <summary>累计字节数更新(通常每完成一个文件块调用一次)。</summary>
    public void Add(long cumulativeBytes)
    {
        lock (_lock)
        {
            _lastBytes = cumulativeBytes;
            var now = _time.GetTimestamp();
            if (_hasSample && now - _lastSampleTimestamp < _interval.Ticks)
            {
                return; // 未到采样间隔
            }

            _hasSample = true;
            _lastSampleTimestamp = now;
            _samples.Enqueue((now, cumulativeBytes));

            // 淘汰超出窗口的旧采样(至少保留 1 个)
            var cutoff = now - _window.Ticks;
            while (_samples.Count > 1 && _samples.Peek().Timestamp < cutoff)
            {
                _samples.Dequeue();
            }
        }
    }

    /// <summary>当前窗口内平均速率(Bytes/s);采样不足 2 个时返回 0。</summary>
    public double BytesPerSecond
    {
        get
        {
            lock (_lock)
            {
                if (_samples.Count < 2)
                {
                    return 0;
                }

                var first = _samples.Peek();
                var last = _samples.Last();

                var dtTicks = last.Timestamp - first.Timestamp;
                if (dtTicks <= 0)
                {
                    return 0;
                }

                var dtSeconds = dtTicks / (double)_time.TimestampFrequency;
                var bytes = last.Bytes - first.Bytes;
                return dtSeconds > 0 ? bytes / dtSeconds : 0;
            }
        }
    }

    /// <summary>最近一次报告的累计字节(用于进度显示兜底)。</summary>
    public long LastBytes
    {
        get { lock (_lock) { return _lastBytes; } }
    }
}
