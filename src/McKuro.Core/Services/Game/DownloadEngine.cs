using System.Diagnostics;
using McKuro.Core.Models.Game;
using Microsoft.Extensions.Logging;

namespace McKuro.Core.Services.Game;

/// <summary>下载进度(整体)。</summary>
public sealed class DownloadProgress
{
    public required string CurrentFile { get; init; }
    public required int FileIndex { get; init; }
    public required int FileTotal { get; init; }
    public required long BytesDownloaded { get; init; }
    public required long BytesTotal { get; init; }
    public required double SpeedBps { get; init; }

    public double Percent => BytesTotal > 0 ? Math.Clamp((double)BytesDownloaded / BytesTotal, 0, 1) : 0;
}

/// <summary>
/// 并发下载引擎:将文件队列以 N 路并发下载,汇总进度与速度,支持取消。
/// </summary>
public sealed class DownloadEngine
{
    private readonly HttpClient _http;
    private readonly ILogger<DownloadEngine> _logger;
    private volatile int _maxConcurrency;
    private readonly DownloadRateLimiter _rateLimiter = new();
    private readonly PauseTokenSource _pauseToken = new();

    public DownloadEngine(HttpClient http, int maxConcurrency = 8, ILogger<DownloadEngine>? logger = null)
    {
        _http = http;
        _maxConcurrency = Math.Max(1, maxConcurrency);
        _logger = logger ?? NullLogger<DownloadEngine>.Instance;
    }

    /// <summary>更新并发数(下次下载批次生效)。</summary>
    public void SetConcurrency(int maxConcurrency) => _maxConcurrency = Math.Max(1, maxConcurrency);

    /// <summary>设置下载限速(字节/秒,0 = 不限;对齐 Haiyu DownloadState.SetSpeedLimitAsync)。</summary>
    public void SetSpeedLimit(long bytesPerSecond) => _rateLimiter.SetSpeed(bytesPerSecond);

    /// <summary>当前限速(字节/秒)。</summary>
    public long SpeedLimitBytesPerSecond => _rateLimiter.BytesPerSecond;

    /// <summary>是否处于暂停状态。</summary>
    public bool IsPaused => _pauseToken.IsPaused;

    /// <summary>暂停下载(挂起所有进行中的文件读取,不断开连接)。</summary>
    public void Pause() => _pauseToken.Pause();

    /// <summary>恢复下载。</summary>
    public void Resume() => _pauseToken.Resume();

    /// <summary>
    /// 下载文件列表。
    /// </summary>
    /// <param name="files">待下载文件。</param>
    /// <param name="baseUrl">URL 前缀。</param>
    /// <param name="destDir">保存根目录。</param>
    /// <returns>(成功数, 失败列表)</returns>
    public async Task<(int Success, List<string> Failures)> DownloadManyAsync(
        IReadOnlyList<GameFileEntry> files,
        string baseUrl,
        string destDir,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        var failures = new List<string>();
        int success = 0;
        var sw = Stopwatch.StartNew();
        var speedMeter = new SlidingSpeedMeter();

        using var semaphore = new SemaphoreSlim(_maxConcurrency);
        var tasks = new List<Task>();
        long totalBytes = files.Sum(f => f.Size);
        long completedBytes = 0;
        long downloadedBytes = 0;   // 数据流中真实下载字节(含断点续传期间新读字节)
        var byteLock = new object();
        long lastReport = 0;

        // 每个并发文件把"本次新下载字节"累加到全局,供速率计实时采样(而非文件完成时跳变)
        IProgress<int>? byteProgress = null;
        if (progress is not null)
        {
            byteProgress = new Progress<int>(bytes =>
            {
                lock (byteLock)
                {
                    downloadedBytes += bytes;
                    if (downloadedBytes < 0)
                    {
                        downloadedBytes = 0;
                    }
                    speedMeter.Add(downloadedBytes);

                    // 数据流级节流报告:大文件下载期间无文件完成,也要实时刷新网速/进度
                    var now = sw.ElapsedMilliseconds;
                    if (now - lastReport < 200)
                    {
                        return;
                    }
                    lastReport = now;
                    var done = Math.Min(completedBytes, totalBytes);
                    var filesDone = Math.Min(files.Count,
                        (int)(done / Math.Max(totalBytes, 1) * files.Count) + 1);
                    progress.Report(new DownloadProgress
                    {
                        CurrentFile = "正在下载…",
                        FileIndex = filesDone,
                        FileTotal = files.Count,
                        BytesDownloaded = done,
                        BytesTotal = totalBytes,
                        SpeedBps = speedMeter.BytesPerSecond,
                    });
                }
            });
        }

        var downloader = new FileDownloader(_http);

        foreach (var (entry, index) in files.Select((f, i) => (f, i)))
        {
            ct.ThrowIfCancellationRequested();
            tasks.Add(Task.Run(async () =>
            {
                await semaphore.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    var destPath = Path.Combine(destDir, entry.Path.Replace('/', Path.DirectorySeparatorChar));
                    var result = await downloader.DownloadAsync(
                        entry,
                        baseUrl,
                        destPath,
                        progress: byteProgress,
                        ct,
                        rateLimiter: _rateLimiter,
                        pauseToken: _pauseToken).ConfigureAwait(false);
                    lock (byteLock)
                    {
                        completedBytes += entry.Size;
                        if (result.Success)
                        {
                            success++;
                        }
                        else
                        {
                            failures.Add($"{entry.Path}: {result.Error}");
                        }
                    }

                    // 节流:至少 200ms 报告一次,避免高频刷新 UI
                    var now = sw.ElapsedMilliseconds;
                    if (now - lastReport < 200)
                    {
                        return;
                    }
                    lastReport = now;

                    progress?.Report(new DownloadProgress
                    {
                        CurrentFile = entry.Path,
                        FileIndex = index + 1,
                        FileTotal = files.Count,
                        BytesDownloaded = Math.Min(completedBytes, totalBytes),
                        BytesTotal = totalBytes,
                        SpeedBps = speedMeter.BytesPerSecond,
                    });
                }
                finally
                {
                    semaphore.Release();
                }
            }, ct));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
        // 最后补一次最终进度报告
        progress?.Report(new DownloadProgress
        {
            CurrentFile = files.Count > 0 ? files[^1].Path : "",
            FileIndex = files.Count,
            FileTotal = files.Count,
            BytesDownloaded = completedBytes,
            BytesTotal = totalBytes,
            SpeedBps = speedMeter.BytesPerSecond,
        });
        return (success, failures);
    }
}
