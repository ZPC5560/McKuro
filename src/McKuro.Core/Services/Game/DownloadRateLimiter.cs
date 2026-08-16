using System.Diagnostics;

namespace McKuro.Core.Services.Game;

/// <summary>
/// 全局下载速率限制器(对齐 Haiyu DownloadState.SetSpeedLimitAsync)。
/// 所有并发文件下载共享一个实例,按总字节数限速;0 = 不限速。
/// </summary>
public sealed class DownloadRateLimiter
{
    private readonly object _lock = new();
    private long _bytesPerSecond;
    private long _windowStart;
    private long _windowBytes;

    public DownloadRateLimiter(long bytesPerSecond = 0)
    {
        _bytesPerSecond = Math.Max(0, bytesPerSecond);
        _windowStart = Stopwatch.GetTimestamp();
    }

    /// <summary>设置限速(字节/秒,0 = 不限)。</summary>
    public void SetSpeed(long bytesPerSecond)
    {
        lock (_lock)
        {
            _bytesPerSecond = Math.Max(0, bytesPerSecond);
        }
    }

    /// <summary>当前限速(字节/秒,0 = 不限)。</summary>
    public long BytesPerSecond
    {
        get
        {
            lock (_lock)
            {
                return _bytesPerSecond;
            }
        }
    }

    /// <summary>
    /// 消费 bytes 字节:若超出当前 1 秒窗口配额则等待,直到窗口滚动或配额充足。
    /// 等待期间可被取消。
    /// </summary>
    public async ValueTask ConsumeAsync(int bytes, CancellationToken ct = default)
    {
        if (bytes <= 0)
        {
            return;
        }

        while (true)
        {
            long limit;
            long waitMs;
            lock (_lock)
            {
                limit = _bytesPerSecond;
                if (limit <= 0)
                {
                    return; // 不限速
                }

                var now = Stopwatch.GetTimestamp();
                var elapsedMs = (long)((now - _windowStart) * 1000.0 / Stopwatch.Frequency);
                if (elapsedMs >= 1000)
                {
                    _windowStart = now;
                    _windowBytes = 0;
                }

                if (_windowBytes + bytes <= limit)
                {
                    _windowBytes += bytes;
                    return;
                }

                // 需要等到窗口滚动:剩余时间(保守取整加 5ms 抖动,避免边界忙等)
                waitMs = Math.Max(1, 1000 - elapsedMs + 5);
            }

            ct.ThrowIfCancellationRequested();
            await Task.Delay((int)Math.Min(waitMs, 200), ct).ConfigureAwait(false);
        }
    }
}

/// <summary>
/// 暂停门(对齐 Haiyu DownloadState.IsPaused + PauseDownloadAsync/ResumeDownloadAsync)。
/// Pause() 后所有等待方阻塞,Resume() 放行。
/// </summary>
public sealed class PauseTokenSource
{
    private volatile TaskCompletionSource<bool> _gate =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _paused;

    /// <summary>是否处于暂停状态。</summary>
    public bool IsPaused => Volatile.Read(ref _paused) == 1;

    /// <summary>暂停:关闭门(幂等)。</summary>
    public void Pause()
    {
        if (Interlocked.Exchange(ref _paused, 1) == 0)
        {
            _gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    /// <summary>恢复:打开门(幂等)。</summary>
    public void Resume()
    {
        if (Interlocked.Exchange(ref _paused, 0) == 1)
        {
            _gate.TrySetResult(true);
        }
    }

    /// <summary>暂停时等待放行;未暂停立即返回。可被取消。</summary>
    public async Task WaitAsync(CancellationToken ct = default)
    {
        if (IsPaused)
        {
            await _gate.Task.WaitAsync(ct).ConfigureAwait(false);
        }
    }
}
