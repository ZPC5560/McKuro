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

        try
        {
            long resumeFrom = 0;
            if (File.Exists(partPath))
            {
                resumeFrom = new FileInfo(partPath).Length;
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (resumeFrom > 0)
            {
                request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(resumeFrom, null);
            }

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var totalLength = response.Content.Headers.ContentLength ?? entry.Size;
            if (totalLength > 0 && resumeFrom >= totalLength)
            {
                // 已下载完成,直接校验
                File.Move(partPath, destPath, overwrite: true);
                return Verify(destPath, entry);
            }

            await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var target = new FileStream(
                partPath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 128 * 1024,
                useAsync: true);
            if (resumeFrom == 0)
            {
                target.SetLength(0);
            }

            var buffer = new byte[128 * 1024];
            long downloaded = resumeFrom;
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
                downloaded += read;

                // 全局速率限制(所有并发文件共享同一配额)
                if (rateLimiter is not null)
                {
                    await rateLimiter.ConsumeAsync(read, ct).ConfigureAwait(false);
                }

                progress?.Report(read);
            }

            await target.FlushAsync(ct).ConfigureAwait(false);
            File.Move(partPath, destPath, overwrite: true);
            return Verify(destPath, entry);
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

    private static FileDownloadResult Verify(string path, GameFileEntry entry)
    {
        if (string.IsNullOrEmpty(entry.Md5))
        {
            return new FileDownloadResult { Success = true, RelativePath = entry.Path, HashVerified = false };
        }

        try
        {
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
        if (!File.Exists(path) || string.IsNullOrEmpty(entry.Md5))
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
