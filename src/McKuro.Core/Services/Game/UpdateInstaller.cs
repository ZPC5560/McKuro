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

    /// <summary>计算需要下载/更新的文件(缺失或 MD5 不一致)。</summary>
    /// <param name="manifest">服务端清单。</param>
    /// <param name="gameRootDir">游戏根目录。</param>
    /// <param name="skipPaths">跳过校验的相对路径集合(OrdinalIgnoreCase,使用正斜杠);命中则视为无需下载。</param>
    /// <param name="progress">校验进度回调(每秒节流,避免几万文件高频回调阻塞 UI)。</param>
    public UpdateDiff ComputeDiff(
        GameManifest manifest,
        string gameRootDir,
        IReadOnlySet<string>? skipPaths = null,
        IProgress<DiffProgress>? progress = null)
    {
        var diff = new UpdateDiff();
        var total = manifest.Files.Count;
        int checkedCount = 0;
        var lastReport = DateTime.UtcNow;
        foreach (var entry in manifest.Files)
        {
            // 用户配置的跳过校验文件:直接忽略(对齐 Haiyu SkipVerifyFiles)
            if (skipPaths is not null && skipPaths.Contains(entry.Path))
            {
                checkedCount++;
                continue;
            }

            var localPath = Path.Combine(gameRootDir, entry.Path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(localPath))
            {
                diff.ToDownload.Add(entry);
            }
            else if (!string.IsNullOrEmpty(entry.Md5) && !FileDownloader.VerifyLocalFile(localPath, entry))
            {
                diff.ToDownload.Add(entry);
            }

            checkedCount++;
            // 节流:最多每 100ms 报一次,避免几万文件高频回调阻塞 UI
            var now = DateTime.UtcNow;
            if (progress is not null && (now - lastReport).TotalMilliseconds >= 100)
            {
                lastReport = now;
                progress.Report(new DiffProgress(checkedCount, total, entry.Path));
            }
        }
        progress?.Report(new DiffProgress(total, total, ""));
        return diff;
    }

    /// <summary>
    /// 将暂存目录(预下载)中的文件安装到游戏目录。
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

        foreach (var entry in manifest.Files)
        {
            var relative = entry.Path.Replace('/', Path.DirectorySeparatorChar);
            var stagedPath = Path.Combine(stagingDir, relative);
            if (!File.Exists(stagedPath))
            {
                continue;
            }

            try
            {
                var destPath = Path.Combine(gameRootDir, relative);
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
                        failures.Add($"{entry.Path}: 暂存文件校验失败");
                        continue;
                    }
                }

                if (File.Exists(destPath))
                {
                    Backup(destPath, gameRootDir);
                    File.Delete(destPath);
                }

                File.Move(stagedPath, destPath);
                installed++;
            }
            catch (Exception ex)
            {
                failures.Add($"{entry.Path}: {ex.Message}");
            }
        }

        return (installed, failures);
    }

    /// <summary>备份将被替换的文件。</summary>
    private static void Backup(string filePath, string gameRootDir)
    {
        try
        {
            var backupDir = Path.Combine(gameRootDir, BackupDirName);
            var relative = Path.GetRelativePath(gameRootDir, filePath);
            var dest = Path.Combine(backupDir, relative);
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
