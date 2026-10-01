using System.Diagnostics;
using McKuro.Core.Models.Game;
using Microsoft.Extensions.Logging;

namespace McKuro.Core.Services.Game;

/// <summary>一次进度上报所属的阶段(供 UI 决定显示什么、是否显示暂停)。</summary>
public enum DownloadPhase
{
    /// <summary>非下载阶段(差分合成/解压/资源安装/迁移),只切文案不动进度条。</summary>
    Stage,

    /// <summary>本地文件校验阶段(BytesTotal==0),按文件数显示「正在校验本地文件 x/y…」。</summary>
    Verify,

    /// <summary>真正的下载阶段,显示百分比/速度/剩余时间。</summary>
    Download,
}

/// <summary>下载进度(整体)。</summary>
public sealed class DownloadProgress
{
    public required string CurrentFile { get; init; }
    public required int FileIndex { get; init; }
    public required int FileTotal { get; init; }
    public required long BytesDownloaded { get; init; }
    public required long BytesTotal { get; init; }
    public required double SpeedBps { get; init; }

    /// <summary>
    /// 非下载阶段文本(差分合成/解压/安装等,对齐上游 1.6 修复:安装阶段不再卡「下载中」文案)。
    /// 非空时 UI 应显示该文本且不改动进度条百分比(此时字节字段无意义)。
    /// </summary>
    public string? StageText { get; init; }

    /// <summary>
    /// 当前阶段是否可暂停(默认 false)。只有真正的下载阶段才为 true:
    /// 校验/差分合成(hpatchz 外部进程)/解压/资源安装等阶段不产生可暂停的字节读取,
    /// 此时 UI 必须隐藏暂停按钮,否则暂停是空操作(且遗留的暂停门会让后续下载阶段无声卡死)。
    /// 对齐上游 IProgressSetup.CanPause:InstallKrdiffGroupResource/InstallKrdiffResource/MoveFileResource
    /// 均为 false,仅 DownloadAndVerifyResource/InstallKrZipResource 为 true。
    /// </summary>
    public bool CanPause { get; init; }

    /// <summary>
    /// 阶段分类(纯函数,UI 与测试共用同一套判定):
    /// 有阶段文案 → <see cref="DownloadPhase.Stage"/>;
    /// 无字节总量但有文件总数 → <see cref="DownloadPhase.Verify"/>(校验);
    /// 其余 → <see cref="DownloadPhase.Download"/>。
    /// </summary>
    public DownloadPhase Phase => StageText is not null
        ? DownloadPhase.Stage
        : BytesTotal <= 0 && FileTotal > 0
            ? DownloadPhase.Verify
            : DownloadPhase.Download;

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
    /// <param name="progress">进度回调。</param>
    /// <param name="ct">取消令牌。</param>
    /// <param name="streamingGameRoot">非空时启用「边下载边更新」流式安装:
    /// 把 <paramref name="destDir"/> 视为游戏根目录,文件下载完成即直接落位到
    /// <c>{gameRoot}/{entry.Path}</c>(替换前把旧文件备份到 .McKuro_backup),
    /// 不再经过临时暂存目录。返回的失败列表语义与暂存版一致。</param>
    /// <returns>(成功数, 失败列表)</returns>
    public async Task<(int Success, List<string> Failures)> DownloadManyAsync(
        IReadOnlyList<GameFileEntry> files,
        string baseUrl,
        string destDir,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default,
        string? streamingGameRoot = null)
    {
        var failures = new List<string>();
        int success = 0;
        var sw = Stopwatch.StartNew();
        var speedMeter = new SlidingSpeedMeter();

        using var semaphore = new SemaphoreSlim(_maxConcurrency);
        var tasks = new List<Task>();
        long totalBytes = files.Sum(f => f.Size);
        long downloadedBytes = 0;
        long transferredBytes = 0;
        var completedFiles = 0;
        var byteLock = new object();
        long lastReport = 0;

        void ReportProgress(string currentFile, bool force = false)
        {
            if (progress is null)
            {
                return;
            }

            DownloadProgress? update = null;
            lock (byteLock)
            {
                var now = sw.ElapsedMilliseconds;
                if (!force && now - lastReport < 200)
                {
                    return;
                }
                lastReport = now;
                update = new DownloadProgress
                {
                    CurrentFile = currentFile,
                    FileIndex = completedFiles,
                    FileTotal = files.Count,
                    BytesDownloaded = Math.Min(downloadedBytes, totalBytes),
                    BytesTotal = totalBytes,
                    SpeedBps = speedMeter.BytesPerSecond,
                    // 下载批次:字节读取受暂停门控制,是唯一可暂停的阶段
                    CanPause = true,
                };
            }
            progress.Report(update);
        }

        var downloader = new FileDownloader(_http);
        // 流式安装:下载落位即进入游戏目录。替换目标前把旧文件备份到 .McKuro_backup,
        // 备份由 UpdateInstaller.BackupFile 完成(与暂存安装的备份实现一致)。
        var onReplace = streamingGameRoot is null
            ? null
            : new Action<string, string>((dest, _) => UpdateInstaller.BackupFile(dest, streamingGameRoot!));

        foreach (var (entry, index) in files.Select((f, i) => (f, i)))
        {
            ct.ThrowIfCancellationRequested();
            tasks.Add(Task.Run(async () =>
            {
                await semaphore.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    var destPath = GameFilePath.CombineUnderRoot(destDir, entry.Path);
                    long fileDownloadedBytes = 0;
                    IProgress<int>? byteProgress = progress is null
                        ? null
                        : new InlineProgress<int>(bytes =>
                        {
                            lock (byteLock)
                            {
                                fileDownloadedBytes += bytes;
                                downloadedBytes += bytes;
                                transferredBytes += bytes;
                                speedMeter.Add(transferredBytes);
                            }
                            ReportProgress(entry.Path);
                        });

                    // 流式安装:下载目的地位于游戏根目录,替换前先把旧文件备份到 .McKuro_backup。
                    var result = await downloader.DownloadAsync(
                        entry,
                        baseUrl,
                        destPath,
                        progress: byteProgress,
                        ct,
                        rateLimiter: _rateLimiter,
                        pauseToken: _pauseToken,
                        onReplace: onReplace).ConfigureAwait(false);

                    lock (byteLock)
                    {
                        if (result.Success)
                        {
                            // 对已存在、已校验或由 .part 续传跳过的字节补齐进度。
                            var remaining = entry.Size > fileDownloadedBytes
                                ? entry.Size - fileDownloadedBytes
                                : 0;
                            downloadedBytes += remaining;
                            completedFiles++;
                            success++;
                        }
                        else
                        {
                            failures.Add($"{entry.Path}: {result.Error}");
                        }
                    }
                    ReportProgress(entry.Path);
                }
                finally
                {
                    semaphore.Release();
                }
            }, ct));
        }

        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        finally
        {
            // 批次结束即清除暂停门:批次内没有待读取的字节,残留的暂停态会污染后续阶段
            // (新批次一进循环就在 WaitAsync 上静默阻塞,而那时按钮可能已不可见)。
            _pauseToken.Resume();
        }
        // 最后补一次最终进度报告
        ReportProgress(files.Count > 0 ? files[^1].Path : "", force: true);
        return (success, failures);
    }

    private sealed class InlineProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value) => callback(value);
    }
}
