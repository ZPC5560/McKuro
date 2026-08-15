using System.Diagnostics;
using donet.Core.Models.Game;

namespace donet.Core.Services.Game;

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
    private readonly int _maxConcurrency;

    public DownloadEngine(HttpClient http, int maxConcurrency = 8)
    {
        _http = http;
        _maxConcurrency = Math.Max(1, maxConcurrency);
    }

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
        long lastBytes = 0;

        using var semaphore = new SemaphoreSlim(_maxConcurrency);
        var tasks = new List<Task>();
        long totalBytes = files.Sum(f => f.Size);
        long completedBytes = 0;
        var byteLock = new object();

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
                        progress: null,
                        ct).ConfigureAwait(false);
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

                    var now = sw.Elapsed.TotalSeconds;
                    var speed = now > 0 ? (completedBytes - lastBytes) / now : 0;
                    progress?.Report(new DownloadProgress
                    {
                        CurrentFile = entry.Path,
                        FileIndex = index + 1,
                        FileTotal = files.Count,
                        BytesDownloaded = completedBytes,
                        BytesTotal = totalBytes,
                        SpeedBps = speed,
                    });
                }
                finally
                {
                    semaphore.Release();
                }
            }, ct));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
        return (success, failures);
    }
}
