using System.Net;
using System.Security.Cryptography;
using McKuro.Core.Models.Game;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace McKuro.Core.Services.Game;

/// <summary>单个文件下载完成的结果。</summary>
public sealed class FileDownloadResult
{
    public required bool Success { get; init; }
    public required string RelativePath { get; init; }
    public string? Error { get; init; }
    public bool HashVerified { get; init; }
}

/// <summary>
/// 单个文件下载器:支持断点续传(Range)、进度回调与 MD5 校验。
/// 下载中间文件为 <paramref name="destPath"/>.part,完成后原子改名。
/// <para>
/// 网络中断自动重试(对齐 Haiyu 1.4.3 的「下载重试机制,不再等待单一 TCP 连接失效」):
/// 每次读取都有空闲超时,超时/连接中断/5xx 等瞬时错误按指数退避重试,
/// 并从<b>已写入的偏移</b>续传,不再退回起点或干等 OS 级连接超时。
/// </para>
/// </summary>
public sealed class FileDownloader
{
    /// <summary>缓冲区大小(128 KiB)。</summary>
    private const int BufferSize = 128 * 1024;

    /// <summary>
    /// 校验读盘时的文件流缓冲区大小(256 KiB)。
    /// <para>
    /// <b>与 <see cref="BufferSize"/> 分工不同,勿合并:</b>那个是网络响应体的读取缓冲区,
    /// 这个是本地文件流内部的缓冲。<see cref="File.OpenRead(string)"/> 默认只给 <b>4096 字节</b>,
    /// 大文件整份算 MD5 时读盘会退化成大量小 IO。
    /// </para>
    /// <para>
    /// <b>实测(同一 2.19 GiB pak,同机同盘,Precision):</b>
    /// 默认 4096 缓冲 <b>270 MiB/s</b> → 128 KiB <b>540 MiB/s</b> → 256 KiB <b>577 MiB/s</b>,
    /// 即 <b>约 2.1 倍</b>,哈希值完全一致。
    /// 同时实测确认:<see cref="FileOptions.SequentialScan"/> 只带来约 2% 收益(540 → 546 MiB/s),
    /// 手工 <see cref="IncrementalHash"/> 循环反而更慢(560 MiB/s < 577 MiB/s)且代码复杂度高得多 ——
    /// 因此这里<b>只调缓冲大小</b>,保留 <see cref="MD5.HashData"/> 的整份实现。
    /// </para>
    /// </summary>
    private const int VerifyBufferSize = 256 * 1024;

    /// <summary>
    /// 单次读取的最长空闲时间(超过即视为连接已失效,触发重试)。对齐 Haiyu 的 90 秒。
    /// <para>
    /// <b>与 <see cref="HttpClient.Timeout"/> 的分工(实测结论,勿混用):</b>
    /// 两者覆盖<b>不同阶段</b>,不是"谁先到谁生效":
    /// ①「等响应头」阶段由 <see cref="HttpClient.Timeout"/> 兜住(实测:总超时 2 秒、服务端永不发头
    /// → 约 2 秒后失败并触发重试);
    /// ②「读响应体」阶段 <b><see cref="HttpClient.Timeout"/> 完全不生效</b>
    /// (实测:总超时 2 秒、服务端发头 + 100 字节后静默 → 12 秒仍未返回、且没有重试;
    /// 把本空闲超时设为 30 秒则单次尝试精确在 ≈30 秒后重试)。
    /// 所以本值是响应体阶段<b>唯一</b>的挂死保护,调大它等于放宽挂死容忍度,
    /// 而不是"被 HttpClient.Timeout 抢先"。
    /// </para>
    /// </summary>
    public static readonly TimeSpan DefaultIdleReadTimeout = TimeSpan.FromSeconds(90);

    /// <summary>瞬时错误的最大重试次数(首次尝试之外额外重试的次数)。对齐 Haiyu MaxRetryCount=4。</summary>
    public const int MaxRetryCount = 4;

    private readonly HttpClient _http;
    private readonly ILogger _logger;
    private readonly TimeSpan _idleReadTimeout;
    private readonly double _retryDelayScale;

    /// <param name="http">下载用 HttpClient。</param>
    /// <param name="logger">可选日志(重试与续传会记 Warning)。</param>
    /// <param name="idleReadTimeout">单次读取空闲超时;默认 90 秒。测试可注入极短值。</param>
    /// <param name="retryDelayScale">退避时长的缩放系数,仅用于让测试免于真等 1+2+4+8 秒;生产保持 1.0。</param>
    public FileDownloader(
        HttpClient http,
        ILogger? logger = null,
        TimeSpan? idleReadTimeout = null,
        double retryDelayScale = 1.0)
    {
        _http = http;
        _logger = logger ?? NullLogger.Instance;
        _idleReadTimeout = idleReadTimeout ?? DefaultIdleReadTimeout;
        _retryDelayScale = retryDelayScale > 0 ? retryDelayScale : 1.0;
    }

    public async Task<FileDownloadResult> DownloadAsync(
        GameFileEntry entry,
        string baseUrl,
        string destPath,
        IProgress<int>? progress = null,
        CancellationToken ct = default,
        DownloadRateLimiter? rateLimiter = null,
        PauseTokenSource? pauseToken = null,
        Action<string, string>? onReplace = null,
        bool skipExistingCheck = false)
    {
        var dir = Path.GetDirectoryName(destPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var partPath = destPath + ".part";
        var url = entry.Url ?? baseUrl + entry.Path;

        // 既无大小又无 MD5 = 没有任何文件级完整性依据:此时"跳过下载"是不可验证的乐观判断
        // (实测:任意内容都会被判为成功)。真实库洛清单里每个条目都带 size+md5,
        // 这属于防御性处理 —— 宁可多下一次也不谎报成功。
        // 判据与判定逻辑由 LocalFileMatches 单点提供,与校验侧(UpdateInstaller)共用,
        // 避免同一条清单行两侧给出相反结论。
        // skipExistingCheck:调用方(ComputeDiff 之后)已判定目标文件缺失或过期,
        // 再整份算一次 MD5 只会得出同样的结论,白读一遍大文件。
        if (!skipExistingCheck && LocalFileMatches(destPath, entry))
        {
            return new FileDownloadResult
            {
                Success = true,
                RelativePath = entry.Path,
                HashVerified = !string.IsNullOrWhiteSpace(entry.Md5),
            };
        }

        if (entry.ChunkInfos.Count > 0)
        {
            return await DownloadByChunksAsync(entry, url, destPath, partPath, progress, ct, rateLimiter, pauseToken, onReplace)
                .ConfigureAwait(false);
        }

        try
        {
            long resumeFrom = 0;
            if (File.Exists(partPath))
            {
                resumeFrom = new FileInfo(partPath).Length;
                if (entry.Size > 0 && resumeFrom > entry.Size)
                {
                    File.Delete(partPath);
                    resumeFrom = 0;
                }
                else if (entry.Size > 0 && resumeFrom == entry.Size)
                {
                    if (string.IsNullOrWhiteSpace(entry.Md5) || VerifyLocalFile(partPath, entry))
                    {
                        if (File.Exists(destPath))
                        {
                            onReplace?.Invoke(destPath, destPath);
                        }
                        File.Move(partPath, destPath, overwrite: true);
                        return Verify(destPath, entry);
                    }

                    // 非分片续传无法定位损坏区间;完整 .part 校验失败时从头下载。
                    File.Delete(partPath);
                    resumeFrom = 0;
                }
            }

            await using (var target = new FileStream(
                partPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: BufferSize,
                useAsync: true))
            {
                if (resumeFrom == 0)
                {
                    target.SetLength(0);
                }

                // 整文件路径:范围开放到文件末尾(end = -1);重试时从已写偏移续传。
                // expectedTotal = entry.Size 让「服务器声明的长度比清单短」或
                // 「TCP 中断被表现为干净 EOF」都能被判为不完整并触发续传重试(整文件路径的兜底)。
                await DownloadRangeWithRetryAsync(
                    entry,
                    url,
                    target,
                    start: resumeFrom,
                    end: -1,
                    knownSize: entry.Size,
                    requirePartialContent: false,
                    progress,
                    ct,
                    rateLimiter,
                    pauseToken).ConfigureAwait(false);

                await target.FlushAsync(ct).ConfigureAwait(false);
            }

            var verify = Verify(partPath, entry);
            if (!verify.Success)
            {
                TryDeletePart(partPath);
                return verify;
            }
            if (File.Exists(destPath))
            {
                onReplace?.Invoke(destPath, destPath);
            }
            File.Move(partPath, destPath, overwrite: true);
            return verify;
        }
        catch (OperationCanceledException)
        {
            return new FileDownloadResult { Success = false, RelativePath = entry.Path, Error = CoreStrings.T("Core.Dl.Cancelled", "已取消") };
        }
        catch (Exception ex)
        {
            return new FileDownloadResult { Success = false, RelativePath = entry.Path, Error = ex.Message };
        }
    }

    private async Task<FileDownloadResult> DownloadByChunksAsync(
        GameFileEntry entry,
        string url,
        string destPath,
        string partPath,
        IProgress<int>? progress,
        CancellationToken ct,
        DownloadRateLimiter? rateLimiter,
        PauseTokenSource? pauseToken,
        Action<string, string>? onReplace = null)
    {
        try
        {
            var dir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            await using var target = new FileStream(
                partPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.Read,
                BufferSize,
                useAsync: true);
            // 预分配前的长度:超出它的区域是 SetLength 补出来的全零,绝不可能通过分片哈希,
            // 对它们做预检只是白算(新下载的 18.6 GB pak 会因此先空读 18.6 GB)。
            var existingLength = target.Length;
            if (entry.Size > 0)
            {
                target.SetLength(entry.Size);
            }

            foreach (var chunk in entry.ChunkInfos.OrderBy(x => x.Start))
            {
                ct.ThrowIfCancellationRequested();
                var expectedLength = chunk.Length;
                if (expectedLength <= 0)
                {
                    continue;
                }

                // 只有完全落在原有字节范围内的分片才可能已下载过(续传),才值得预检。
                if (chunk.End < existingLength
                    && await IsChunkValidAsync(target, chunk, ct).ConfigureAwait(false))
                {
                    continue;
                }

                // 分片路径:重试时从该分片内已写入的偏移续传;CDN 必须回 206 才能定位写入位置。
                await DownloadRangeWithRetryAsync(
                    entry,
                    url,
                    target,
                    start: chunk.Start,
                    end: chunk.End,
                    knownSize: expectedLength,
                    requirePartialContent: chunk.Start > 0,
                    progress,
                    ct,
                    rateLimiter,
                    pauseToken).ConfigureAwait(false);

                await target.FlushAsync(ct).ConfigureAwait(false);
                if (!await IsChunkValidAsync(target, chunk, ct).ConfigureAwait(false))
                {
                    throw new IOException(CoreStrings.F("Core.Dl.ChunkMd5Failed", $"分片 MD5 校验失败: {chunk.Start}-{chunk.End}", chunk.Start, chunk.End));
                }
            }

            target.SetLength(entry.Size > 0 ? entry.Size : target.Length);
            target.Close();

            // 分片完整覆盖时,每个分片都已在本方法内做过哈希复核(:254 命中即跳过、:274 写完再复核),
            // 内容已被逐字节验证;再整份算一遍 MD5 就是纯粹的重复读盘。
            // 实测这份浪费在鸣潮 3.7.1 上是 76 个文件 / 78.54 GB(每次安装)。
            // 覆盖不完整或长度不符时仍走原 Verify()(整文件 MD5),绝不放宽。
            if (FastVerify.HasCompleteChunkCoverage(entry))
            {
                var actualSize = new FileInfo(partPath).Length;
                if (actualSize != entry.Size)
                {
                    TryDeletePart(partPath);
                    return new FileDownloadResult
                    {
                        Success = false,
                        RelativePath = entry.Path,
                        Error = $"文件大小校验失败: 期望 {entry.Size},实际 {actualSize}",
                    };
                }

                var chunkVerified = new FileDownloadResult
                {
                    Success = true,
                    RelativePath = entry.Path,
                    // 分片哈希已验证过内容,这里如实标记为已校验(不走整文件 MD5 但确实校验了)。
                    HashVerified = true,
                };
                if (File.Exists(destPath))
                {
                    onReplace?.Invoke(destPath, destPath);
                }
                File.Move(partPath, destPath, overwrite: true);
                return chunkVerified;
            }

            var verify = Verify(partPath, entry);
            if (!verify.Success)
            {
                TryDeletePart(partPath);
                return verify;
            }
            if (File.Exists(destPath))
            {
                onReplace?.Invoke(destPath, destPath);
            }
            File.Move(partPath, destPath, overwrite: true);
            return verify;
        }
        catch (OperationCanceledException)
        {
            return new FileDownloadResult { Success = false, RelativePath = entry.Path, Error = CoreStrings.T("Core.Dl.Cancelled", "已取消") };
        }
        catch (Exception ex)
        {
            return new FileDownloadResult { Success = false, RelativePath = entry.Path, Error = ex.Message };
        }
    }

    /// <summary>
    /// 下载 <paramref name="start"/>~<paramref name="end"/> 范围(含端点;<paramref name="end"/> 为 -1 表示到文件末尾)
    /// 并写入 <paramref name="target"/> 的对应偏移,失败时按指数退避重试。
    /// <para>
    /// 重试从「已成功写入的偏移」继续(绝不重下发起点造成数据错位),瞬时错误最多重试
    /// <see cref="MaxRetryCount"/> 次后抛 <see cref="IOException"/>。
    /// </para>
    /// </summary>
    /// <param name="entry">文件条目(仅用于日志定位)。</param>
    /// <param name="requirePartialContent">true 时要求服务器返回 206;服务器忽略 Range 会写错位置,故判为失败。</param>
    private async Task DownloadRangeWithRetryAsync(
        GameFileEntry entry,
        string url,
        FileStream target,
        long start,
        long end,
        long knownSize,
        bool requirePartialContent,
        IProgress<int>? progress,
        CancellationToken ct,
        DownloadRateLimiter? rateLimiter,
        PauseTokenSource? pauseToken)
    {
        // 有待下载长度时用它做上界;否则(末端开放)用「清单总长 - 本次起始偏移」,
        // 即本次调用应写入的字节数(续传时已写入的前缀不计入);都没有则只认 EOF。
        long expectedTotal = end >= start
            ? end - start + 1
            : (knownSize > start ? knownSize - start : 0);
        // 本次写入的累计字节;writeBase = written==0 时对应的绝对文件偏移。
        // 两者分开是因为「服务端忽略 Range 而回整份内容」时会把写入基准退回文件开头。
        long written = 0;
        long writeBase = start;
        int retry = 0;
        var buffer = new byte[BufferSize];

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var requestStart = writeBase + written;

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                // 有明确范围(分片路径,含首片 bytes=0-12)时始终发 Range;
                // 末端开放的整文件路径从 0 开始时保持原实现的「普通 GET」语义(不对 0 起点的 bytes=0- 做假设)。
                if (requestStart > 0 || end >= start)
                {
                    request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(
                        requestStart,
                        end >= start ? end : (long?)null);
                }

                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                    .ConfigureAwait(false);

                if (IsTransientStatusCode(response.StatusCode))
                {
                    throw new HttpRequestException(
                        $"CDN 返回 {(int)response.StatusCode} ({response.StatusCode})。",
                        null,
                        response.StatusCode);
                }

                response.EnsureSuccessStatusCode();
                var isPartial = response.StatusCode == HttpStatusCode.PartialContent;

                if (requirePartialContent && !isPartial)
                {
                    // 协议违规(而非瞬时故障):重试同一个确定性失败的请求纯属浪费,
                    // 用 InvalidDataException 使其绕开下面的 IOException 重试分支,保持原实现的快速失败。
                    throw new InvalidDataException(CoreStrings.F("Core.Dl.NoChunkResponse", $"服务器未返回分片响应: {response.StatusCode}", response.StatusCode));
                }

                if (isPartial)
                {
                    // 校验 CDN 是否真的按请求起点返回,避免错位分片被静默写坏文件。
                    // 服务器未回 Content-Range 时无法核对,保持宽松(仅在不一致时失败)。
                    var from = response.Content.Headers.ContentRange?.From;
                    if (from is not null && from != requestStart)
                    {
                        // 同上:错位范围是确定性问题,立即失败而不重试。
                        throw new InvalidDataException(CoreStrings.F(
                            "Core.Dl.WrongRange",
                            $"CDN 返回了错误的分片范围: 请求 {requestStart},返回 {from}",
                            requestStart,
                            from.Value));
                    }
                }
                else if (requestStart > 0)
                {
                    // 未要求分片、但服务端忽略了 Range 而回整份内容:只能从头重写,不能追加。
                    _logger.LogWarning("CDN 忽略了续传范围 {Start},改为从头下载: {Path}", requestStart, entry.Path);
                    // 丢弃的字节要如实回报负向修正,否则进度会被重复累加
                    // (实测:200000 字节的文件报出 250000 字节,百分比提前冲到 100%)。
                    ReportDelta(progress, -written);
                    written = 0;
                    writeBase = 0;
                    target.SetLength(0);
                    requestStart = 0;
                    // 写入基准回到文件开头,目标长度随之变为「整个文件」。
                    if (end < start && knownSize > 0)
                    {
                        expectedTotal = knownSize;
                    }
                }

                // 末端开放时用「响应声明长度」作为完成依据:服务器声明得比清单短,
                // 或 TCP 中断被表现为干净 EOF,都会在下面被判为不完整并触发续传重试。
                if (end < start && expectedTotal <= 0)
                {
                    var declared = response.Content.Headers.ContentLength;
                    expectedTotal = declared is > 0 ? declared.Value : 0;
                }

                await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);

                target.Position = requestStart;

                while (expectedTotal <= 0 || written < expectedTotal)
                {
                    ct.ThrowIfCancellationRequested();
                    if (pauseToken is not null)
                    {
                        await pauseToken.WaitAsync(ct).ConfigureAwait(false);
                    }

                    var want = expectedTotal > 0
                        ? (int)Math.Min(buffer.Length, expectedTotal - written)
                        : buffer.Length;
                    if (want <= 0)
                    {
                        break;
                    }

                    // 每次读取各自一个 linked CTS(对齐 Haiyu ReadWithIdleTimeoutAsync):
                    // 空闲超时只能惩罚"读取卡住",不能惩罚"限速等待/暂停等待"。
                    // 若整个请求共用一个 CTS,限速让两次读取间隔超过空闲超时时,
                    // 计时器会在无人读取时触发并把该 CTS 永久置为已取消 ——
                    // 之后同一请求的每次读取都立刻抛取消(连接其实是好的),下载被误判失败。
                    using var idleCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    idleCts.CancelAfter(_idleReadTimeout);
                    var read = await source.ReadAsync(buffer.AsMemory(0, want), idleCts.Token).ConfigureAwait(false);
                    if (read == 0)
                    {
                        break; // 流提前结束:按长度判定是否已读满
                    }

                    await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    if (rateLimiter is not null)
                    {
                        await rateLimiter.ConsumeAsync(read, ct).ConfigureAwait(false);
                    }

                    written += read;
                    ReportDelta(progress, read);
                }

                // 已知目标长度却提前结束 = 响应不完整:交给重试续传(而非当成成功)。
                if (expectedTotal > 0 && written < expectedTotal)
                {
                    throw new IOException(CoreStrings.F(
                        "Core.Dl.RangeIncomplete",
                        $"下载流提前结束: {written}/{expectedTotal}",
                        written,
                        expectedTotal));
                }

                return;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // 空闲超时(或连接被单方面中断)且用户未取消:退避后从已写偏移续传。
                await DelayBeforeRetryAsync(++retry, entry.Path, writeBase + written, end, ct).ConfigureAwait(false);
            }
            catch (HttpRequestException ex) when (IsTransient(ex))
            {
                await DelayBeforeRetryAsync(++retry, entry.Path, writeBase + written, end, ct).ConfigureAwait(false);
            }
            catch (IOException)
            {
                await DelayBeforeRetryAsync(++retry, entry.Path, writeBase + written, end, ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>退避等待;超过 <see cref="MaxRetryCount"/> 次则抛出(不再重试)。</summary>
    /// <param name="ct">
    /// 用户取消令牌。<b>必须传入</b>:否则用户点「取消」后还要白等完当前退避
    /// (最坏 8 秒 + 抖动,实测取消延迟可达 8.5 秒),UI 才会显示已取消。
    /// </param>
    private async Task DelayBeforeRetryAsync(int retryCount, string path, long requestStart, long end, CancellationToken ct)
    {
        if (retryCount > MaxRetryCount)
        {
            throw new IOException(CoreStrings.F(
                "Core.Dl.RetryExhausted",
                $"下载重试 {MaxRetryCount} 次后仍然失败: {path},范围 {requestStart}-{end}",
                MaxRetryCount,
                path,
                requestStart,
                end));
        }

        // 指数退避 + 抖动:1s/2s/4s/8s(上限 8s),抖动 200~800ms 避免并发文件同时重连。
        var baseDelayMs = Math.Min(1000 * (1 << (retryCount - 1)), 8000);
        var delayMs = (int)Math.Max(1, (baseDelayMs + Random.Shared.Next(200, 800)) * _retryDelayScale);
        _logger.LogWarning(
            "下载中断,{Delay}ms 后进行第 {Retry}/{Max} 次续传: {Path} [{Start}-{End}]",
            delayMs,
            retryCount,
            MaxRetryCount,
            path,
            requestStart,
            end);
        // 传入 ct:退避期间用户取消要立即抛出(抛出的 OperationCanceledException 会被
        // 外层 DownloadAsync 的 catch 转成「已取消」),不白等完这次退避。
        await Task.Delay(delayMs, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 上报一个字节增量(可为负),并<b>按 int 上限自动分片</b>。
    /// <para>
    /// 分片是必要的:进度回调的载荷是 <see cref="int"/>,而单个文件可达数 GB
    /// (实测鸣潮 <c>pakchunk70-HD-WindowsNoEditor.pak</c> 18.6 GB),
    /// 直接 <c>Report((int)largeValue)</c> 会溢出成负数或垃圾值。
    /// 负增量用于「CDN 忽略 Range、已写字节被丢弃重下」时的进度回退。
    /// </para>
    /// </summary>
    private static void ReportDelta(IProgress<int>? progress, long delta)
    {
        if (progress is null || delta == 0)
        {
            return;
        }

        const int MaxChunk = int.MaxValue;
        while (delta > MaxChunk)
        {
            progress.Report(MaxChunk);
            delta -= MaxChunk;
        }
        while (delta < -MaxChunk)
        {
            progress.Report(-MaxChunk);
            delta += MaxChunk;
        }
        progress.Report((int)delta);
    }

    /// <summary>HTTP 状态码是否属于「可重试」的瞬时错误(对齐 Haiyu IsTransientStatusCode)。</summary>
    private static bool IsTransientStatusCode(HttpStatusCode statusCode)    {
        var code = (int)statusCode;
        return statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
            || code is 500 or 502 or 503 or 504;
    }

    /// <summary>未拿到状态码(连接层失败)或状态码本身是瞬时错误时视为可重试。</summary>
    private static bool IsTransient(HttpRequestException exception)
        => exception.StatusCode is null || IsTransientStatusCode(exception.StatusCode.Value);

    /// <summary>
    /// 分片哈希复核:真正的异步读盘(<see cref="FastVerify.ChunkHashMatchesAsync"/>,与同步版放在一起避免漂移)。
    /// 以前是 <c>Task.FromResult</c> 包着同步读取,最多 8 路并发下载会各占一个线程池线程做阻塞 IO。
    /// </summary>
    private static Task<bool> IsChunkValidAsync(FileStream file, GameChunkInfo chunk, CancellationToken ct)
        => FastVerify.ChunkHashMatchesAsync(file, chunk, ct);

    private static void TryDeletePart(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // 保留删除失败的暂存文件;下次会按长度/MD5 重新判断。
        }
    }

    private static FileDownloadResult Verify(string path, GameFileEntry entry)
    {
        try
        {
            if (!File.Exists(path))
            {
                return new FileDownloadResult { Success = false, RelativePath = entry.Path, Error = CoreStrings.T("Core.Dl.FileMissing", "下载文件不存在") };
            }
            if (entry.Size > 0 && new FileInfo(path).Length != entry.Size)
            {
                return new FileDownloadResult
                {
                    Success = false,
                    RelativePath = entry.Path,
                    Error = $"文件大小校验失败: 期望 {entry.Size},实际 {new FileInfo(path).Length}",
                };
            }
            if (string.IsNullOrEmpty(entry.Md5))
            {
                return new FileDownloadResult { Success = true, RelativePath = entry.Path, HashVerified = false };
            }

            // 与 VerifyLocalFile 共用 VerifyBufferSize 与 SequentialScan(实测 2.1 倍差距)。
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                VerifyBufferSize,
                FileOptions.SequentialScan);
            var hash = Convert.ToHexStringLower(MD5.HashData(stream));
            var ok = string.Equals(hash, entry.Md5, StringComparison.OrdinalIgnoreCase);
            return new FileDownloadResult
            {
                Success = ok,
                RelativePath = entry.Path,
                HashVerified = true,
                Error = ok ? null : CoreStrings.F("Core.Dl.Md5Failed", $"MD5 校验失败: 期望 {entry.Md5},实际 {hash}", entry.Md5, hash),
            };
        }
        catch (Exception ex)
        {
            return new FileDownloadResult { Success = false, RelativePath = entry.Path, Error = ex.Message };
        }
    }

    /// <summary>
    /// <b>本地文件与清单条目一致性的唯一判据</b>(下载侧的「已存在即跳过」与校验侧的
    /// <c>UpdateInstaller.IsLocalFileIntact</c> 共用同一份实现,避免同一条清单行两侧给出相反结论)。
    /// </summary>
    /// <remarks>
    /// 语义(逐条对应原有行为,不改变任何既有判定结果):
    /// <list type="number">
    /// <item>文件不存在 → <c>false</c>;</item>
    /// <item>条目<b>没有任何文件级完整性依据</b>(size 与 md5 皆空)→ <c>false</c>:
    ///       此时"判定为完好"是不可验证的乐观假设,任意内容都会被判成功 ⇒ 宁可重下;</item>
    /// <item>声明了 size 且长度不符 → <c>false</c>;</item>
    /// <item><b>分片完整覆盖</b>时用分片哈希校验(与下载侧分片收尾同一份证据,见
    ///       <see cref="FastVerify.HasCompleteChunkCoverage"/>);</item>
    /// <item>否则有 md5 → 全文件 MD5 比对;<b>md5 为空但有 size</b> → 长度匹配即视为完好
    ///       (无从校验内容,保持既有约定以免把大批无 MD5 的文件无谓重下)。</item>
    /// </list>
    /// <para>
    /// 注意第 4 条的宽松处理只适用于 <c>size &gt; 0</c>:一旦走到第 2 条(两者皆空)就已返回,
    /// 所以不存在"无 size 又无 md5 却判为完好"的组合。
    /// </para>
    /// <para>
    /// <b>为什么第 4 条要排在分片分支之前:</b>下载侧在分片完整覆盖时<b>不</b>算整文件 MD5,
    /// 若校验侧仍以整文件 md5 优先,两侧判据就不同了;而第 3 条(分片覆盖)本身要求每片都带哈希,
    /// 因此它比"整文件 md5"更严格,优先用它不会放宽任何判定。
    /// </para>
    /// </remarks>
    public static bool LocalFileMatches(string path, GameFileEntry entry)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        // 无任何文件级依据:不得声称完好(与下载侧拒绝跳过同源)。
        if (!entry.HasIntegrityData)
        {
            return false;
        }

        // 声明了大小就必须精确匹配。
        if (entry.Size > 0 && new FileInfo(path).Length != entry.Size)
        {
            return false;
        }

        // 分片完整覆盖 → 用分片哈希(与下载侧分片收尾使用同一份证据,避免两侧结论相反)。
        if (FastVerify.HasCompleteChunkCoverage(entry))
        {
            return FastVerify.VerifyAllChunks(path, entry);
        }

        // 无 MD5 可校验(但已有 size 兜底):长度匹配即视为完好。
        return string.IsNullOrWhiteSpace(entry.Md5) || VerifyLocalFile(path, entry);
    }

    /// <summary>校验本地文件 MD5 是否与清单一致。</summary>
    public static bool VerifyLocalFile(string path, GameFileEntry entry)
    {
        if (!File.Exists(path)
            || (entry.Size > 0 && new FileInfo(path).Length != entry.Size)
            || string.IsNullOrEmpty(entry.Md5))
        {
            return false;
        }
        try
        {
            // 用 VerifyBufferSize 开流:默认 4096 缓冲会让大文件 MD5 退化成小 IO(实测 2.1 倍差距),
            // SequentialScan 提示内核顺序预读(整份哈希本来就是顺序读)。详见 VerifyBufferSize 注释。
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                VerifyBufferSize,
                FileOptions.SequentialScan);
            var hash = Convert.ToHexStringLower(MD5.HashData(stream));
            return string.Equals(hash, entry.Md5, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
