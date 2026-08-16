using McKuro.Core.Services.Game;

namespace McKuro.Tests;

/// <summary>
/// 下载限速器与暂停门测试(对齐 Haiyu DownloadState 的限速与暂停/恢复语义)。
/// </summary>
public class DownloadRateLimiterTests
{
    [Fact]
    public async Task No_Limit_Consumes_Immediately()
    {
        var limiter = new DownloadRateLimiter(0);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 10; i++)
        {
            await limiter.ConsumeAsync(128 * 1024);
        }
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 500, $"不限速应无等待,实际 {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task Limit_Throttles_Transfer()
    {
        // 限速 128 KB/s,下载 512 KB 理论约 4s;断言 >2.5s 防 CI 抖动误判
        var limiter = new DownloadRateLimiter(128 * 1024);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var total = 512 * 1024;
        var chunk = 64 * 1024;
        var sent = 0;
        while (sent < total)
        {
            await limiter.ConsumeAsync(chunk);
            sent += chunk;
        }
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds > 2500, $"限速未生效,实际 {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task Rate_Can_Be_Updated_During_Transfer()
    {
        var limiter = new DownloadRateLimiter(0);
        limiter.SetSpeed(1024 * 1024); // 1 MB/s
        var sw = System.Diagnostics.Stopwatch.StartNew();
        // 消费 2MB(超出窗口 1MB 配额,至少等待一次窗口滚动 ≈1s)
        for (var i = 0; i < 16; i++)
        {
            await limiter.ConsumeAsync(128 * 1024);
        }
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds >= 800, $"限速 1MB/s 消费 2MB 应约 1s,实际 {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task Pause_Blocks_Until_Resume()
    {
        var pause = new PauseTokenSource();
        Assert.False(pause.IsPaused);

        pause.Pause();
        Assert.True(pause.IsPaused);

        var task = pause.WaitAsync();
        var completed = await Task.WhenAny(task, Task.Delay(300));
        Assert.NotSame(task, completed); // 暂停时等待不应完成

        pause.Resume();
        Assert.False(pause.IsPaused);
        await task.WaitAsync(TimeSpan.FromSeconds(2)); // 恢复后放行
    }

    [Fact]
    public async Task Pause_Is_Idempotent_And_Resume_Releases_All()
    {
        var pause = new PauseTokenSource();
        pause.Pause();
        pause.Pause(); // 幂等
        Assert.True(pause.IsPaused);

        var a = pause.WaitAsync();
        var b = pause.WaitAsync();
        await Task.Delay(100);
        pause.Resume();

        await Task.WhenAll(a, b).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(pause.IsPaused);
    }

    [Fact]
    public async Task Not_Paused_WaitAsync_Returns_Immediately()
    {
        var pause = new PauseTokenSource();
        await pause.WaitAsync().WaitAsync(TimeSpan.FromMilliseconds(300));
    }
}
