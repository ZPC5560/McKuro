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
/// <remarks>
/// 状态(是否暂停 + 当前门)由一把锁保护,读写总是成对一致。
/// 旧实现先置 <c>_paused</c> 再换新门,读者若在两步之间读到初始那个<b>永远不会完成</b>的门就会永久挂起;
/// Pause/Resume 交错时也可能留下"未暂停却挂着未完成门"的状态。现在初始门是已完成的,
/// 换门与改标志在同一把锁内完成。
/// </remarks>
public sealed class PauseTokenSource
{
    private readonly object _lock = new();
    private TaskCompletionSource<bool> _gate = CreateCompletedGate();
    private volatile bool _paused;

    private static TaskCompletionSource<bool> CreateCompletedGate()
    {
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        gate.SetResult(true);
        return gate;
    }

    /// <summary>是否处于暂停状态。</summary>
    public bool IsPaused => _paused;

    /// <summary>暂停:关闭门(幂等)。</summary>
    public void Pause()
    {
        lock (_lock)
        {
            if (_paused)
            {
                return;
            }
            _gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _paused = true;
        }
    }

    /// <summary>恢复:打开门(幂等)。</summary>
    public void Resume()
    {
        TaskCompletionSource<bool> gate;
        lock (_lock)
        {
            if (!_paused)
            {
                return;
            }
            _paused = false;
            gate = _gate;
        }
        // 门是 RunContinuationsAsynchronously:放行不会在锁内/本线程同步执行等待方的续体。
        gate.TrySetResult(true);
    }

    /// <summary>暂停时等待放行;未暂停立即返回。可被取消。</summary>
    public Task WaitAsync(CancellationToken ct = default)
    {
        // 快速路径:下载热循环每次读取都会调用,未暂停时不取锁。
        // 即使这里读到旧值,也只是"晚一次读取才停下"或"多等一次已完成的门",不会挂起。
        if (!_paused)
        {
            return Task.CompletedTask;
        }
        Task gateTask;
        lock (_lock)
        {
            // 锁内读到的门与标志一致:未暂停时门必定已完成
            gateTask = _gate.Task;
        }
        return gateTask.WaitAsync(ct);
    }
}
