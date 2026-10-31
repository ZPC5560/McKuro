namespace McKuro.Services;

/// <summary>
/// 背景视频磁盘缓存(<c>%TEMP%\McKuroVideo</c>):原子落盘 + 定期清理。
/// <para>
/// 旧实现把整段视频 <c>GetByteArrayAsync</c> 进内存后直接写最终文件名,且"非空即命中":
/// 离开页面取消、写入中崩溃都会留下截断的 .mp4,此后 mpv 每次都失败只显示兜底图;
/// 走 60 秒总超时的元数据客户端,慢网下大视频永远下不完;目录也没有大小上限。
/// 现在:流式写一次性临时文件 → <see cref="File.Move(string, string, bool)"/> 覆盖替换;
/// 只有完整下载的文件才会出现在最终文件名下。
/// </para>
/// </summary>
public static class VideoCache
{
    /// <summary>
    /// 缓存文件后缀。带版本号:旧版本按 <c>{hash}.mp4</c> 直接写最终文件名,可能是截断文件,
    /// 无法区分好坏,清理时一律按"旧格式"删除,让它们重新下载一次。
    /// </summary>
    public const string FileSuffix = ".v2.mp4";

    /// <summary>单个视频大小上限(防异常响应撑满磁盘)。</summary>
    public const long MaxFileBytes = 1L << 30;

    /// <summary>缓存目录总大小上限(超出按最近使用时间淘汰)。</summary>
    public const long MaxTotalBytes = 2L << 30;

    /// <summary>超过这么久未使用的缓存文件删除。</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(30);

    /// <summary>下载空闲超时:连续这么久收不到数据即放弃(下载本身不设总超时)。</summary>
    public static readonly TimeSpan IdleReadTimeout = TimeSpan.FromSeconds(60);

    /// <summary>残留临时文件(崩溃/强退留下)超过这么久才清理:避免误删其他控件正在下载的临时文件。</summary>
    private static readonly TimeSpan StaleTempAge = TimeSpan.FromDays(1);

    private const string TempMarker = ".tmp-";

    /// <summary>默认缓存目录。</summary>
    public static string DefaultDirectory => Path.Combine(Path.GetTempPath(), "McKuroVideo");

    /// <summary>按 URL 哈希得到缓存文件路径。</summary>
    public static string PathFor(string cacheDir, string url)
    {
        var hash = Convert.ToHexString(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(url)));
        return Path.Combine(cacheDir, hash + FileSuffix);
    }

    /// <summary>缓存命中时刷新最近使用时间(清理按它淘汰;NTFS 默认不维护 LastAccessTime)。失败忽略。</summary>
    public static void Touch(string path)
    {
        try
        {
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
        }
        catch (Exception)
        {
            // 文件被占用/只读:不影响播放
        }
    }

    /// <summary>
    /// 流式下载到 <paramref name="destPath"/>:先写一次性临时文件,完整后原子替换。
    /// 返回 false 表示响应为空或超过 <paramref name="maxBytes"/>(不落盘);
    /// 网络错误抛异常,空闲超时抛 <see cref="TimeoutException"/>,<paramref name="ct"/> 取消抛 <see cref="OperationCanceledException"/>。
    /// 任何失败都不会在最终文件名下留下文件,临时文件一并删除。
    /// </summary>
    public static async Task<bool> DownloadAsync(
        HttpClient http,
        string url,
        string destPath,
        CancellationToken ct,
        long maxBytes = MaxFileBytes,
        TimeSpan? idleTimeout = null)
    {
        var idleSpan = idleTimeout ?? IdleReadTimeout;
        var tmp = destPath + TempMarker + Guid.NewGuid().ToString("N");
        try
        {
            using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct);
            idle.CancelAfter(idleSpan);
            using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, idle.Token).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            if (resp.Content.Headers.ContentLength is long declared && declared > maxBytes)
            {
                return false;
            }

            long total = 0;
            await using (var src = await resp.Content.ReadAsStreamAsync(idle.Token).ConfigureAwait(false))
            await using (var dst = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                while (true)
                {
                    idle.CancelAfter(idleSpan); // 每次读取前重置空闲计时
                    var n = await src.ReadAsync(buffer, idle.Token).ConfigureAwait(false);
                    if (n == 0)
                    {
                        break;
                    }
                    total += n;
                    if (total > maxBytes)
                    {
                        return false;
                    }
                    await dst.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
                }
            }
            if (total == 0)
            {
                return false;
            }

            try
            {
                File.Move(tmp, destPath, overwrite: true);
            }
            catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && IsUsable(destPath))
            {
                // 另一个控件已下好同一视频且正被 mpv 打开(Windows 上无法覆盖):直接用现成的
            }
            return true;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"视频下载 {idleSpan.TotalSeconds:F0}s 无数据");
        }
        finally
        {
            TryDelete(tmp);
        }
    }

    /// <summary>缓存文件存在且非空。</summary>
    public static bool IsUsable(string path)
    {
        try
        {
            return File.Exists(path) && new FileInfo(path).Length > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// 清理缓存目录:删除旧格式文件、过期残留临时文件、超过 <paramref name="maxAge"/> 未用的文件;
    /// 剩余文件总量超过 <paramref name="maxTotalBytes"/> 时按最近使用时间从旧到新淘汰。
    /// <paramref name="keepPath"/>(当前要播放的文件)永不删除。被占用的文件跳过。不抛异常。
    /// </summary>
    public static void Prune(
        string cacheDir,
        DateTime utcNow,
        string? keepPath = null,
        long maxTotalBytes = MaxTotalBytes,
        TimeSpan? maxAge = null)
    {
        try
        {
            if (!Directory.Exists(cacheDir))
            {
                return;
            }
            var age = maxAge ?? MaxAge;
            var keep = keepPath is null ? null : Path.GetFullPath(keepPath);
            var candidates = new List<FileInfo>();
            // 当前要播放的文件永不删除:先把它的大小计入总量,其余文件按最近使用时间分配剩余额度
            long total = 0;
            foreach (var file in new DirectoryInfo(cacheDir).GetFiles())
            {
                if (keep is not null && string.Equals(file.FullName, keep, StringComparison.OrdinalIgnoreCase))
                {
                    total += file.Length;
                    continue;
                }
                if (file.Name.Contains(TempMarker, StringComparison.Ordinal))
                {
                    if (utcNow - file.LastWriteTimeUtc > StaleTempAge)
                    {
                        TryDelete(file.FullName);
                    }
                    continue;
                }
                if (!file.Name.EndsWith(FileSuffix, StringComparison.OrdinalIgnoreCase)
                    || utcNow - file.LastWriteTimeUtc > age)
                {
                    TryDelete(file.FullName);
                    continue;
                }
                candidates.Add(file);
            }

            foreach (var file in candidates.OrderByDescending(f => f.LastWriteTimeUtc))
            {
                total += file.Length;
                if (total > maxTotalBytes)
                {
                    TryDelete(file.FullName);
                    total -= file.Length;
                }
            }
        }
        catch (Exception)
        {
            // 清理是尽力而为
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception)
        {
            // 被占用(mpv 正在播放)/无权限:下次再清
        }
    }
}
