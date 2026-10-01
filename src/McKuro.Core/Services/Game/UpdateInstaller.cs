using System.Runtime.ExceptionServices;
using McKuro.Core.Models.Game;

namespace McKuro.Core.Services.Game;

/// <summary>需要下载的文件列表。</summary>
public sealed class UpdateDiff
{
    public List<GameFileEntry> ToDownload { get; } = [];
    public long TotalBytes => ToDownload.Sum(f => f.Size);
    public bool HasChanges => ToDownload.Count > 0;
}

/// <summary>本地文件校验进度(供 UI 显示"校验文件 x/N")。</summary>
public readonly record struct DiffProgress(int Checked, int Total, string CurrentFile);

/// <summary>
/// 更新安装器:对比本地文件与清单,计算差异,并将(预下载的)文件原子化安装到游戏目录。
/// 替换文件前备份到 <c>.McKuro_backup</c>,安装完成后写入版本标记。
/// </summary>
public sealed class UpdateInstaller
{
    public const string BackupDirName = ".McKuro_backup";

    /// <summary>
    /// 计算需要下载/更新的文件(缺失或 MD5 不一致)。
    /// 文件彼此独立,使用 <see cref="Parallel.ForEachAsync"/> 并行读盘校验(纯 IO 密集,
    /// SSD/机械盘数万文件均显著加速),保留 MD5 校验失败列入下载、跳过列表与取消语义。
    /// </summary>
    /// <param name="manifest">服务端清单。</param>
    /// <param name="gameRootDir">游戏根目录。</param>
    /// <param name="skipPaths">跳过校验的相对路径集合(OrdinalIgnoreCase,使用正斜杠);命中则视为无需下载。</param>
    /// <param name="progress">校验进度回调(节流,避免几万文件高频回调阻塞 UI)。</param>
    /// <param name="maxDegreeOfParallelism">并行度;默认 min(Environment.ProcessorCount, 8)。</param>
    public UpdateDiff ComputeDiff(
        GameManifest manifest,
        string gameRootDir,
        IReadOnlySet<string>? skipPaths = null,
        IProgress<DiffProgress>? progress = null,
        CancellationToken ct = default,
        int? maxDegreeOfParallelism = null)
    {
        var diff = new UpdateDiff();
        var total = manifest.Files.Count;
        int checkedCount = 0;
        long lastReportTicks = DateTime.UtcNow.Ticks;
        var exceptionToThrow = (Exception?)null;

        Parallel.ForEach(
            manifest.Files,
            new ParallelOptions
            {
                CancellationToken = ct,
                // 纯 IO 并行;上限 8 防止机械盘上过度争用反而变慢
                MaxDegreeOfParallelism = maxDegreeOfParallelism ?? Math.Min(Environment.ProcessorCount, 8),
            },
            entry =>
            {
                ct.ThrowIfCancellationRequested();
                // 用户配置的跳过校验文件:直接忽略(对齐 Haiyu SkipVerifyFiles)
                if (skipPaths is not null && skipPaths.Contains(entry.Path))
                {
                    Interlocked.Increment(ref checkedCount);
                    return;
                }

                try
                {
                    var localPath = GameFilePath.CombineUnderRoot(gameRootDir, entry.Path);
                    if (!File.Exists(localPath))
                    {
                        lock (diff.ToDownload)
                        {
                            diff.ToDownload.Add(entry);
                        }
                    }
                    else if (!string.IsNullOrEmpty(entry.Md5) && !FileDownloader.VerifyLocalFile(localPath, entry))
                    {
                        lock (diff.ToDownload)
                        {
                            diff.ToDownload.Add(entry);
                        }
                    }
                }
                catch (Exception ex)
                {
                    // 并行下不允许异常逃逸到其他迭代:记录首个异常,统一在结束后抛出
                    // (保留旧语义:任一文件校验失败即整体失败,如路径越界 InvalidDataException)
                    Interlocked.CompareExchange(ref exceptionToThrow, ex, null);
                }

                var now = DateTime.UtcNow.Ticks;
                var count = Interlocked.Increment(ref checkedCount);
                // 节流:最多每 100ms 报一次,避免几万文件高频回调阻塞 UI(并发下 CAS 竞争,线程安全)
                if (progress is not null)
                {
                    var last = Volatile.Read(ref lastReportTicks);
                    if (now - last >= TimeSpan.TicksPerMillisecond * 100
                        && Interlocked.CompareExchange(ref lastReportTicks, now, last) == last)
                    {
                        progress.Report(new DiffProgress(count, total, entry.Path));
                    }
                }
            });

        if (exceptionToThrow is not null)
        {
            ExceptionDispatchInfo.Capture(exceptionToThrow).Throw();
        }

        progress?.Report(new DiffProgress(total, total, ""));
        return diff;
    }

    /// <summary>
    /// 将暂存目录(预下载)中的文件安装到游戏目录。
    /// 文件彼此独立,使用 <see cref="Parallel.ForEach"/> 并行「校验→备份→替换」,
    /// 移动前目录创建幂等;单个文件失败不中断其余文件(失败列表与顺序版语义一致)。
    /// </summary>
    /// <param name="stagingDir">预下载暂存目录(文件按相对路径存放)。</param>
    /// <param name="manifest">安装所用清单。</param>
    /// <returns>(安装文件数, 失败列表)</returns>
    public (int Installed, List<string> Failures) InstallFromStaging(
        string stagingDir,
        string gameRootDir,
        GameManifest manifest)
    {
        var failures = new List<string>();
        int installed = 0;

        Parallel.ForEach(
            manifest.Files,
            new ParallelOptions { MaxDegreeOfParallelism = Math.Min(Environment.ProcessorCount, 8) },
            entry =>
            {
                try
                {
                    var stagedPath = GameFilePath.CombineUnderRoot(stagingDir, entry.Path);
                    if (!File.Exists(stagedPath))
                    {
                        return;
                    }

                    var destPath = GameFilePath.CombineUnderRoot(gameRootDir, entry.Path);
                    var destDir = Path.GetDirectoryName(destPath);
                    if (!string.IsNullOrEmpty(destDir))
                    {
                        Directory.CreateDirectory(destDir);
                    }

                    // 校验暂存文件
                    if (!string.IsNullOrEmpty(entry.Md5))
                    {
                        var ok = FileDownloader.VerifyLocalFile(stagedPath, entry);
                        if (!ok)
                        {
                            lock (failures)
                            {
                                failures.Add($"{entry.Path}: 暂存文件校验失败");
                            }
                            return;
                        }
                    }

                    if (File.Exists(destPath))
                    {
                        BackupFile(destPath, gameRootDir);
                        File.Delete(destPath);
                    }

                    File.Move(stagedPath, destPath);
                    Interlocked.Increment(ref installed);
                }
                catch (Exception ex)
                {
                    lock (failures)
                    {
                        failures.Add($"{entry.Path}: {ex.Message}");
                    }
                }
            });

        return (installed, failures);
    }

    /// <summary>安装指定普通资源,跳过 krdiff/krpdiff/krzip 等补丁包。</summary>
    public (int Installed, List<string> Failures) InstallFilesFromStaging(
        string stagingDir,
        string gameRootDir,
        IReadOnlyList<GameFileEntry> entries)
    {
        var failures = new List<string>();
        int installed = 0;

        Parallel.ForEach(
            entries,
            new ParallelOptions { MaxDegreeOfParallelism = Math.Min(Environment.ProcessorCount, 8) },
            entry =>
            {
                try
                {
                    var stagedPath = GameFilePath.CombineUnderRoot(stagingDir, entry.Path);
                    if (!File.Exists(stagedPath))
                    {
                        return;
                    }

                    var destPath = GameFilePath.CombineUnderRoot(gameRootDir, entry.Path);
                    var destDir = Path.GetDirectoryName(destPath);
                    if (!string.IsNullOrEmpty(destDir))
                    {
                        Directory.CreateDirectory(destDir);
                    }
                    if (!string.IsNullOrEmpty(entry.Md5) && !FileDownloader.VerifyLocalFile(stagedPath, entry))
                    {
                        lock (failures)
                        {
                            failures.Add($"{entry.Path}: 暂存文件校验失败");
                        }
                        return;
                    }
                    if (File.Exists(destPath))
                    {
                        BackupFile(destPath, gameRootDir);
                        File.Delete(destPath);
                    }
                    File.Move(stagedPath, destPath, overwrite: true);
                    Interlocked.Increment(ref installed);
                }
                catch (Exception ex)
                {
                    lock (failures)
                    {
                        failures.Add($"{entry.Path}: {ex.Message}");
                    }
                }
            });

        return (installed, failures);
    }

    /// <summary>
    /// 备份将被替换的文件到 <c>.McKuro_backup</c>(供流式安装/回滚)。
    /// 公开供下载引擎在「边下载边更新」替换目标文件前调用;备份失败不阻断安装。
    /// </summary>
    public static void BackupFile(string filePath, string gameRootDir)
    {
        try
        {
            var backupDir = GameFilePath.CombineUnderRoot(gameRootDir, BackupDirName);
            var relative = Path.GetRelativePath(gameRootDir, filePath);
            var dest = GameFilePath.CombineUnderRoot(backupDir, relative);
            var destDir = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(destDir))
            {
                Directory.CreateDirectory(destDir);
            }
            if (File.Exists(dest))
            {
                File.Delete(dest);
            }
            File.Copy(filePath, dest);
        }
        catch (Exception)
        {
            // 备份失败不阻断安装
        }
    }

    /// <summary>删除游戏目录中不在清单内的文件(可选清理)。</summary>
    public int RemoveObsolete(GameManifest manifest, string gameRootDir)
    {
        var manifestPaths = manifest.Files
            .Select(f => f.Path.Replace('/', Path.DirectorySeparatorChar))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        int removed = 0;
        foreach (var file in Directory.EnumerateFiles(gameRootDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(gameRootDir, file);
            if (relative.StartsWith(BackupDirName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            // 预下载暂存与补丁临时目录都在游戏目录的工作区里(DiffData/ 及其下的 install_tmp/),
            // 它们不在游戏清单内,但删除会丢掉已预载的几十 GB,因此与备份目录一样跳过。
            // .McKuro_patch 是与之并列的分组差分临时目录,同样跳过。
            if (relative.StartsWith(GamePathResolver.DiffDataDirName, StringComparison.OrdinalIgnoreCase)
                || relative.StartsWith(GamePathResolver.PatchTempDirName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (manifestPaths.Contains(relative))
            {
                continue;
            }
            try
            {
                File.Delete(file);
                removed++;
            }
            catch (Exception)
            {
                // 忽略
            }
        }
        return removed;
    }
}
