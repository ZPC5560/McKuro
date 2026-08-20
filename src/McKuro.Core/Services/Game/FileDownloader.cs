using System.Net;
using System.Security.Cryptography;
using McKuro.Core.Models.Game;

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
/// </summary>
public sealed class FileDownloader
{
    private readonly HttpClient _http;

    public FileDownloader(HttpClient http)
    {
        _http = http;
    }

    public async Task<FileDownloadResult> DownloadAsync(
        GameFileEntry entry,
        string baseUrl,
        string destPath,
        IProgress<int>? progress = null,
        CancellationToken ct = default,
        DownloadRateLimiter? rateLimiter = null,
        PauseTokenSource? pauseToken = null)
    {
        var dir = Path.GetDirectoryName(destPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var partPath = destPath + ".part";
        var url = entry.Url ?? baseUrl + entry.Path;

        if (File.Exists(destPath)
            && (entry.Size <= 0 || new FileInfo(destPath).Length == entry.Size)
            && (string.IsNullOrWhiteSpace(entry.Md5) || VerifyLocalFile(destPath, entry)))
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
            return await DownloadByChunksAsync(entry, url, destPath, partPath, progress, ct, rateLimiter, pauseToken)
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
                        File.Move(partPath, destPath, overwrite: true);
                        return Verify(destPath, entry);
                    }

                    // 非分片续传无法定位损坏区间;完整 .part 校验失败时从头下载。
                    File.Delete(partPath);
                    resumeFrom = 0;
                }
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (resumeFrom > 0)
            {
                request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(resumeFrom, null);
            }

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var isPartial = response.StatusCode == System.Net.HttpStatusCode.PartialContent;
            if (resumeFrom > 0 && !isPartial)
            {
                // CDN 忽略 Range 时从头覆盖,不能把完整响应追加到 .part。
                resumeFrom = 0;
            }
            var totalLength = response.Content.Headers.ContentLength ?? (isPartial ? entry.Size - resumeFrom : entry.Size);
            if (totalLength > 0 && resumeFrom >= entry.Size && entry.Size > 0)
            {
                File.Move(partPath, destPath, overwrite: true);
                return Verify(destPath, entry);
            }

            await using (var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var target = new FileStream(
                partPath,
                resumeFrom == 0 ? FileMode.Create : FileMode.Append,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 128 * 1024,
                useAsync: true))
            {
                var buffer = new byte[128 * 1024];
                while (true)
                {
                    // 暂停门:暂停时在此等待(对齐 Haiyu PauseDownloadAsync/ResumeDownloadAsync)
                    if (pauseToken is not null)
                    {
                        await pauseToken.WaitAsync(ct).ConfigureAwait(false);
                    }

                    int read = await source.ReadAsync(buffer, ct).ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }
                    await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);

                    // 全局速率限制(所有并发文件共享同一配额)
                    if (rateLimiter is not null)
                    {
                        await rateLimiter.ConsumeAsync(read, ct).ConfigureAwait(false);
                    }

                    progress?.Report(read);
                }

                await target.FlushAsync(ct).ConfigureAwait(false);
            }
            var verify = Verify(partPath, entry);
            if (!verify.Success)
            {
                TryDeletePart(partPath);
                return verify;
            }
            File.Move(partPath, destPath, overwrite: true);
            return verify;
        }
        catch (OperationCanceledException)
        {
            return new FileDownloadResult { Success = false, RelativePath = entry.Path, Error = "已取消" };
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
        PauseTokenSource? pauseToken)
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
                128 * 1024,
                useAsync: true);
            if (entry.Size > 0)
            {
                target.SetLength(entry.Size);
            }

            var buffer = new byte[128 * 1024];
            foreach (var chunk in entry.ChunkInfos.OrderBy(x => x.Start))
            {
                ct.ThrowIfCancellationRequested();
                var expectedLength = chunk.Length;
                if (expectedLength <= 0)
                {
                    continue;
                }

                if (await IsChunkValidAsync(target, chunk, ct).ConfigureAwait(false))
                {
                    continue;
                }

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(chunk.Start, chunk.End);
                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                    .ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                if (response.StatusCode != HttpStatusCode.PartialContent && chunk.Start > 0)
                {
                    throw new IOException($"服务器未返回分片响应: {response.StatusCode}");
                }

                await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                target.Position = chunk.Start;
                long written = 0;
                while (written < expectedLength)
                {
                    if (pauseToken is not null)
                    {
                        await pauseToken.WaitAsync(ct).ConfigureAwait(false);
                    }
                    var read = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, expectedLength - written)), ct)
                        .ConfigureAwait(false);
                    if (read == 0)
                    {
                        throw new IOException($"分片下载不完整: {written}/{expectedLength}");
                    }
                    await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    if (rateLimiter is not null)
                    {
                        await rateLimiter.ConsumeAsync(read, ct).ConfigureAwait(false);
                    }
                    written += read;
                    progress?.Report(read);
                }

                await target.FlushAsync(ct).ConfigureAwait(false);
                if (!await IsChunkValidAsync(target, chunk, ct).ConfigureAwait(false))
                {
                    throw new IOException($"分片 MD5 校验失败: {chunk.Start}-{chunk.End}");
                }
            }

            target.SetLength(entry.Size > 0 ? entry.Size : target.Length);
            target.Close();
            var verify = Verify(partPath, entry);
            if (!verify.Success)
            {
                TryDeletePart(partPath);
                return verify;
            }
            File.Move(partPath, destPath, overwrite: true);
            return verify;
        }
        catch (OperationCanceledException)
        {
            return new FileDownloadResult { Success = false, RelativePath = entry.Path, Error = "已取消" };
        }
        catch (Exception ex)
        {
            return new FileDownloadResult { Success = false, RelativePath = entry.Path, Error = ex.Message };
        }
    }

    private static async Task<bool> IsChunkValidAsync(FileStream file, GameChunkInfo chunk, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(chunk.Md5) || file.Length < chunk.End + 1)
        {
            return false;
        }

        file.Position = chunk.Start;
        using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        var buffer = new byte[128 * 1024];
        long remaining = chunk.Length;
        while (remaining > 0)
        {
            var read = await file.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), ct)
                .ConfigureAwait(false);
            if (read == 0)
            {
                return false;
            }
            md5.AppendData(buffer, 0, read);
            remaining -= read;
        }
        return string.Equals(Convert.ToHexStringLower(md5.GetHashAndReset()), chunk.Md5, StringComparison.OrdinalIgnoreCase);
    }

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
                return new FileDownloadResult { Success = false, RelativePath = entry.Path, Error = "下载文件不存在" };
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

            using var stream = File.OpenRead(path);
            var hash = Convert.ToHexStringLower(MD5.HashData(stream));
            var ok = string.Equals(hash, entry.Md5, StringComparison.OrdinalIgnoreCase);
            return new FileDownloadResult
            {
                Success = ok,
                RelativePath = entry.Path,
                HashVerified = true,
                Error = ok ? null : $"MD5 校验失败: 期望 {entry.Md5},实际 {hash}",
            };
        }
        catch (Exception ex)
        {
            return new FileDownloadResult { Success = false, RelativePath = entry.Path, Error = ex.Message };
        }
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
            using var stream = File.OpenRead(path);
            var hash = Convert.ToHexStringLower(MD5.HashData(stream));
            return string.Equals(hash, entry.Md5, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
