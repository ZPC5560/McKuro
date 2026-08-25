using System.Diagnostics;
using System.IO.Compression;
using McKuro.Core.Models.Game;
using Microsoft.Extensions.Logging;

namespace McKuro.Core.Services.Game;

/// <summary>
/// 执行库洛官方 patch index 的分阶段安装。
/// krdiff/krpdiff 由随程序提供的 hpatchz 执行,普通资源与 krzip 使用托管实现。
/// </summary>
public sealed class PatchInstaller
{
    private readonly ILogger<PatchInstaller> _logger;
    private readonly UpdateInstaller _fileInstaller;

    public PatchInstaller(UpdateInstaller fileInstaller, ILogger<PatchInstaller>? logger = null)
    {
        _fileInstaller = fileInstaller;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<PatchInstaller>.Instance;
    }

    public async Task<PatchInstallResult> InstallAsync(
        GamePatchPlan plan,
        string stagingDir,
        string gameRootDir,
        IReadOnlyList<GameFileEntry> patchFiles,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (plan.DiffPackages.Count > 0 || plan.DiffGroups.Count > 0)
        {
            var hpatchz = FindHpatchz();
            if (hpatchz is null)
            {
                return PatchInstallResult.Failed(
                    "当前更新包含 krdiff/krpdiff 差分包,但未找到 hpatchz.exe。请将 hpatchz.exe 放入 Assets\\HpatchzResource,或设置 MCKURO_HPATCHZ 环境变量。");
            }

            for (var i = 0; i < plan.DiffPackages.Count; i++)
            {
                var package = plan.DiffPackages[i];
                ct.ThrowIfCancellationRequested();
                ReportStage(progress, $"正在合成差分包 ({i + 1}/{plan.DiffPackages.Count})…", package.Package.Path);
                var diffPath = GetStagedPath(stagingDir, package.Package.Path);
                if (!File.Exists(diffPath))
                {
                    return PatchInstallResult.Failed($"差分包不存在: {package.Package.Path}");
                }
                if (!HasFreeSpace(gameRootDir, package.Package.Size))
                {
                    return PatchInstallResult.Failed($"差分安装空间不足: 需要 {FormatBytes(package.Package.Size)}");
                }
                var result = await ApplyDiffAsync(hpatchz, gameRootDir, diffPath, gameRootDir, ct).ConfigureAwait(false);
                if (!result.Success)
                {
                    return result;
                }
            }

            if (plan.DiffGroups.Count > 0)
            {
                var groupResult = await InstallGroupsAsync(hpatchz, plan.DiffGroups, stagingDir, gameRootDir, progress, ct)
                    .ConfigureAwait(false);
                if (!groupResult.Success)
                {
                    return groupResult;
                }
            }
        }

        for (var i = 0; i < plan.ZipPackages.Count; i++)
        {
            var zip = plan.ZipPackages[i];
            ct.ThrowIfCancellationRequested();
            ReportStage(progress, $"正在解压压缩包 ({i + 1}/{plan.ZipPackages.Count})…", zip.Package.Path);
            var zipPath = GetStagedPath(stagingDir, zip.Package.Path);
            if (!File.Exists(zipPath))
            {
                return PatchInstallResult.Failed($"压缩包不存在: {zip.Package.Path}");
            }
            var result = await ExtractZipAsync(zipPath, gameRootDir, ct).ConfigureAwait(false);
            if (!result.Success)
            {
                return result;
            }
        }

        var ordinary = patchFiles
            .Where(f => !IsPatchPackage(f.Path))
            .ToArray();
        var installed = 0;
        if (ordinary.Length > 0)
        {
            ReportStage(progress, $"正在安装资源文件 ({ordinary.Length} 个)…");
            var fileResult = await Task.Run(
                () => _fileInstaller.InstallFilesFromStaging(stagingDir, gameRootDir, ordinary),
                ct).ConfigureAwait(false);
            if (fileResult.Failures.Count > 0)
            {
                return PatchInstallResult.Failed($"普通资源安装失败: {fileResult.Failures[0]}");
            }
            installed = fileResult.Installed;
        }

        // deleteFiles 延后到目标版本完整清单校验成功后由 GameUpdater 执行。
        // 这样补丁应用失败时不会先破坏旧版本文件。
        return PatchInstallResult.Succeeded(installed);
    }

    public static bool IsPatchPackage(string path) =>
        path.EndsWith(".krdiff", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".krpdiff", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".krzip", StringComparison.OrdinalIgnoreCase);

    private async Task<PatchInstallResult> InstallGroupsAsync(
        string hpatchz,
        IReadOnlyList<GamePatchGroup> groups,
        string stagingDir,
        string gameRootDir,
        IProgress<DownloadProgress>? progress,
        CancellationToken ct)
    {
        // 与 Haiyu 一致:分组差分输出位于游戏盘,避免预载目录在另一盘时耗尽错误磁盘。
        var tempRoot = Path.Combine(gameRootDir, ".McKuro_patch", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            // Haiyu 先完成所有 group diff,再统一替换源文件;避免后续差分失去旧版本输入。
            var sourcesToDelete = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var generatedFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var gi = 0; gi < groups.Count; gi++)
            {
                var group = groups[gi];
                ct.ThrowIfCancellationRequested();
                ReportStage(progress, $"正在合成分组差分 ({gi + 1}/{groups.Count})…", group.Package.Path);
                var diffPath = GetStagedPath(stagingDir, group.Package.Path);
                if (!File.Exists(diffPath))
                {
                    return PatchInstallResult.Failed($"分组差分包不存在: {group.Package.Path}");
                }
                var outputBytes = group.DestinationFiles.Sum(file => file.Size);
                if (!HasFreeSpace(gameRootDir, outputBytes))
                {
                    return PatchInstallResult.Failed($"分组差分空间不足: 需要 {FormatBytes(outputBytes)}");
                }

                var result = await ApplyDiffAsync(hpatchz, gameRootDir, diffPath, tempRoot, ct).ConfigureAwait(false);
                if (!result.Success)
                {
                    return result;
                }

                foreach (var source in group.SourceFiles)
                {
                    sourcesToDelete.Add(SafeCombine(gameRootDir, source.Path));
                }
                foreach (var destination in group.DestinationFiles)
                {
                    var generated = SafeCombine(tempRoot, destination.Path);
                    if (!File.Exists(generated))
                    {
                        return PatchInstallResult.Failed($"分组差分未生成目标文件: {destination.Path}");
                    }
                    if (!string.IsNullOrWhiteSpace(destination.Md5)
                        && !FileDownloader.VerifyLocalFile(generated, new GameFileEntry
                        {
                            Path = destination.Path,
                            Size = destination.Size,
                            Md5 = destination.Md5,
                        }))
                    {
                        return PatchInstallResult.Failed($"分组差分目标校验失败: {destination.Path}");
                    }
                    generatedFiles[generated] = SafeCombine(gameRootDir, destination.Path);
                }
            }

            foreach (var source in sourcesToDelete)
            {
                if (File.Exists(source))
                {
                    File.Delete(source);
                }
            }
            foreach (var (generated, target) in generatedFiles)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Move(generated, target, overwrite: true);
            }
            return PatchInstallResult.Succeeded(0);
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "清理差分临时目录失败: {Path}", tempRoot);
            }
        }
    }

    private static async Task<PatchInstallResult> ExtractZipAsync(
        string zipPath,
        string gameRootDir,
        CancellationToken ct)
    {
        try
        {
            await Task.Run(() =>
            {
                using var stream = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, useAsync: true);
                using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
                foreach (var entry in archive.Entries)
                {
                    ct.ThrowIfCancellationRequested();
                    var destination = GameFilePath.CombineUnderRoot(gameRootDir, entry.FullName);
                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        Directory.CreateDirectory(destination);
                        continue;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    using var input = entry.Open();
                    using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.Read, 128 * 1024);
                    input.CopyTo(output);
                }
            }, ct).ConfigureAwait(false);
            return PatchInstallResult.Succeeded(0);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return PatchInstallResult.Failed($"压缩包安装失败: {ex.Message}");
        }
    }

    private static async Task<PatchInstallResult> ApplyDiffAsync(
        string hpatchz,
        string oldRoot,
        string diffPath,
        string newRoot,
        CancellationToken ct)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = hpatchz,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = oldRoot,
            },
        };
        process.StartInfo.ArgumentList.Add(oldRoot);
        process.StartInfo.ArgumentList.Add(diffPath);
        process.StartInfo.ArgumentList.Add(newRoot);
        process.StartInfo.ArgumentList.Add("-f");
        process.StartInfo.ArgumentList.Add("-d");

        try
        {
            if (!process.Start())
            {
                return PatchInstallResult.Failed($"无法启动差分引擎: {hpatchz}");
            }
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
            return process.ExitCode == 0
                ? PatchInstallResult.Succeeded(0)
                : PatchInstallResult.Failed($"差分引擎退出码: {process.ExitCode}");
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // 忽略退出竞态
            }
            throw;
        }
        catch (Exception ex)
        {
            return PatchInstallResult.Failed($"执行差分引擎失败: {ex.Message}");
        }
    }

    /// <summary>上报非下载安装阶段(差分合成/解压/资源安装),UI 据此切换文案而不动进度条。</summary>
    private static void ReportStage(IProgress<DownloadProgress>? progress, string stageText, string? currentFile = null)
    {
        progress?.Report(new DownloadProgress
        {
            CurrentFile = currentFile ?? stageText,
            FileIndex = 0,
            FileTotal = 0,
            BytesDownloaded = 0,
            BytesTotal = 0,
            SpeedBps = 0,
            StageText = stageText,
        });
    }

    private static string? FindHpatchz()
    {
        var configured = Environment.GetEnvironmentVariable("MCKURO_HPATCHZ");
        var candidates = new[]
        {
            configured,
            Path.Combine(AppContext.BaseDirectory, "Assets", "HpatchzResource", "hpatchz.exe"),
            Path.Combine(AppContext.BaseDirectory, "hpatchz.exe"),
        };
        return candidates.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path));
    }

    private static bool HasFreeSpace(string path, long requiredBytes)
    {
        if (requiredBytes <= 0)
        {
            return true;
        }
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            return !string.IsNullOrWhiteSpace(root) && new DriveInfo(root).AvailableFreeSpace >= requiredBytes;
        }
        catch
        {
            return true;
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }
        var units = new[] { "KB", "MB", "GB", "TB" };
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.##} {units[unit]}";
    }

    private static string GetStagedPath(string stagingDir, string relative) =>
        GameFilePath.CombineUnderRoot(stagingDir, relative);

    private static string SafeCombine(string root, string relative) =>
        GameFilePath.CombineUnderRoot(root, relative);
}

public sealed class PatchInstallResult
{
    public bool Success { get; private init; }
    public int Installed { get; private init; }
    public string? Message { get; private init; }

    public static PatchInstallResult Succeeded(int installed) => new() { Success = true, Installed = installed };
    public static PatchInstallResult Failed(string message) => new() { Success = false, Message = message };
}
