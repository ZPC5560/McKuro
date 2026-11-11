using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace McKuro.Core.Services.Game;

/// <summary>
/// 游戏目录占用的分类。
/// <para>
/// <b>为什么要分类</b>:用户看到的"游戏占了多少 GB"往往是几个完全不同的东西加在一起 ——
/// 已安装的正式资源、已经预下载但还没安装的暂存包、游戏日志、游戏截图,以及启动器自己
/// 的工作产物(备份/临时目录)。它们的可清理性和含义差别很大:预下载装完就没了,
/// 日志可以随便删,而正式资源删了就开不了游戏。合成一个总数只会让用户不敢动任何东西。
/// </para>
/// </summary>
public enum GameResourceCategory
{
    /// <summary>正式资源:游戏本体(Content/Paks/Binaries/Engine 等),删了游戏就跑不起来。</summary>
    Official,

    /// <summary>预下载暂存(<c>DiffData/&lt;版本&gt;</c>):已下好待安装的新版本资源。</summary>
    Predownload,

    /// <summary>游戏日志(<c>Client/Saved/Logs</c>):可安全删除,只影响历史排查。</summary>
    Logs,

    /// <summary>游戏截图(<c>Client/Saved/ScreenShot</c>):用户自己的产物。</summary>
    Screenshots,

    /// <summary>启动器工作产物(<c>.McKuro_backup</c> / <c>.McKuro_patch</c> / <c>DiffData/install_tmp</c> / <c>*.part</c>)。</summary>
    LauncherTemp,
}

/// <summary>一个分类的占用(<see cref="GameResourceCategory"/> + 字节数)。</summary>
public sealed class GameResourceCategorySize
{
    public required GameResourceCategory Category { get; init; }

    public required long Bytes { get; init; }
}

/// <summary>一个具名目录的占用(如某个预下载版本目录)。</summary>
public sealed class GameResourceDirSize
{
    /// <summary>目录名(预下载为版本号,日志/截图为目录名)。</summary>
    public required string Name { get; init; }

    public required long Bytes { get; init; }

    public required int FileCount { get; init; }
}

/// <summary>
/// 一次「游戏资源占用」扫描的结果。
/// <para>
/// 所有 <c>XxxBytes</c> 互不重叠,加总严格等于 <see cref="TotalBytes"/> ——
/// 这是环形图各环比例能自洽的前提(否则会出现"各段加起来超过 100%"的诡异画面)。
/// </para>
/// </summary>
public sealed class GameResourceUsage
{
    public static GameResourceUsage Empty { get; } = new();

    /// <summary>游戏目录总占用(所有分类之和)。</summary>
    public long TotalBytes { get; init; }

    /// <summary>正式资源占用。</summary>
    public long OfficialBytes { get; init; }

    /// <summary>预下载暂存占用。</summary>
    public long PredownloadBytes { get; init; }

    /// <summary>日志占用。</summary>
    public long LogBytes { get; init; }

    /// <summary>截图占用。</summary>
    public long ScreenshotBytes { get; init; }

    /// <summary>启动器工作产物占用。</summary>
    public long LauncherTempBytes { get; init; }

    /// <summary>统计到的文件数(给"共 N 个文件"这类副标题用)。</summary>
    public int FileCount { get; init; }

    /// <summary>游戏所在磁盘的总容量;读不到为 0。</summary>
    public long DiskTotalBytes { get; init; }

    /// <summary>游戏所在磁盘的当前可用空间;读不到为 0。</summary>
    public long DiskFreeBytes { get; init; }

    /// <summary>各预下载版本目录的明细(新→旧按名称倒序)。</summary>
    public IReadOnlyList<GameResourceDirSize> PredownloadDirs { get; init; } = [];

    /// <summary>游戏是否真的被扫描过(目录不存在时为 false,UI 据此显示"未设置游戏目录")。</summary>
    public bool Scanned { get; init; }

    /// <summary>是否读到了磁盘容量(读不到时"占硬盘比例"退化为"占总量的比例")。</summary>
    public bool HasDiskInfo => DiskTotalBytes > 0;

    /// <summary>游戏占整块磁盘的比例(0-1)。磁盘容量未知时为 0。</summary>
    public double DiskRatio => Ratio(TotalBytes, DiskTotalBytes);

    /// <summary>正式资源占游戏总量的比例(0-1)。</summary>
    public double OfficialRatio => Ratio(OfficialBytes, TotalBytes);

    /// <summary>预下载占游戏总量的比例(0-1)。</summary>
    public double PredownloadRatio => Ratio(PredownloadBytes, TotalBytes);

    /// <summary>日志 + 截图占游戏总量的比例(0-1)。</summary>
    public double LogAndShotRatio => Ratio(LogBytes + ScreenshotBytes, TotalBytes);

    /// <summary>启动器工作产物占游戏总量的比例(0-1)。</summary>
    public double LauncherTempRatio => Ratio(LauncherTempBytes, TotalBytes);

    /// <summary>按分类取字节数(便于 UI 用同一套模板渲染图例)。</summary>
    public long BytesOf(GameResourceCategory category) => category switch
    {
        GameResourceCategory.Official => OfficialBytes,
        GameResourceCategory.Predownload => PredownloadBytes,
        GameResourceCategory.Logs => LogBytes,
        GameResourceCategory.Screenshots => ScreenshotBytes,
        GameResourceCategory.LauncherTemp => LauncherTempBytes,
        _ => 0,
    };

    /// <summary>
    /// 占比计算(0-1)。分母 ≤ 0 或分子 ≤ 0 时返回 0 —— 环形图不接受 NaN,
    /// 而"总量为 0 时算比例"在实际使用中(空目录、刚选好目录还没扫完)必然发生。
    /// </summary>
    public static double Ratio(long part, long whole)
        => part <= 0 || whole <= 0 ? 0 : Math.Clamp((double)part / whole, 0, 1);
}

/// <summary>扫描进度(供 UI 显示"正在统计…",几十万文件时避免界面像卡死)。</summary>
/// <param name="ScannedFiles">已统计的文件数。</param>
/// <param name="CurrentPath">当前处理的路径(展示用,可能为空)。</param>
public readonly record struct GameResourceScanProgress(int ScannedFiles, string CurrentPath);

/// <summary>
/// 游戏目录占用分类扫描(纯函数式:输入根目录,输出互不重叠的分类体积)。
/// <para>
/// <b>一次遍历</b>:游戏目录通常有一两千到几十万文件,按分类各扫一遍会把 IO 放大数倍。
/// 这里只走一遍 <c>EnumerateFiles(AllDirectories)</c>,按相对路径前缀把每个文件归到一个桶里。
/// </para>
/// <para>
/// <b>安全边界</b>:遇到无权限/被占用的文件只跳过该文件(<c>IgnoreInaccessible</c>),
/// 遇到符号链接/联接点直接不进入 —— 后者会把统计结果放大成"整个盘"。
/// </para>
/// </summary>
public static class GameResourceScanner
{
    /// <summary>游戏截图目录(相对游戏根目录)。取自官方启动器 KRApp.conf 的 <c>gameScreenshotPath</c>。</summary>
    public const string ScreenshotRelativePath = "Client/Saved/ScreenShot";

    /// <summary>游戏日志目录(相对游戏根目录)。</summary>
    public const string LogRelativePath = "Client/Saved/Logs";

    /// <summary>
    /// 把相对路径分类。纯函数(只有字符串判断),单测钉住分类边界 ——
    /// 分类错一格,环形图就会把"可删的日志"画进"删了会坏档的正式资源"里。
    /// </summary>
    /// <param name="relativePath">相对游戏根目录的路径,<c>/</c> 或 <c>\</c> 分隔均可。</param>
    public static GameResourceCategory Classify(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return GameResourceCategory.Official;
        }

        // 统一成 '/' 再比前缀:Windows 路径可能是 '\',而常量里写的是 '/'
        var path = relativePath.Replace('\\', '/').TrimStart('/');

        // 未完成的下载半成品:不属于任何既有分类,单独归到可清理的工作产物
        if (path.EndsWith(".part", StringComparison.OrdinalIgnoreCase))
        {
            return GameResourceCategory.LauncherTemp;
        }

        // 启动器工作产物(注意必须排在 DiffData 之前:install_tmp 是 DiffData 的子目录,
        // 但它是可清理的临时下载,不该被算进"预下载资源")
        if (HasPrefix(path, GamePathResolver.DiffDataDirName + "/" + GamePathResolver.InstallTempDirName))
        {
            return GameResourceCategory.LauncherTemp;
        }
        if (HasPrefix(path, GamePathResolver.PatchTempDirName))
        {
            return GameResourceCategory.LauncherTemp;
        }
        if (HasPrefix(path, UpdateInstaller.BackupDirName))
        {
            return GameResourceCategory.LauncherTemp;
        }

        if (HasPrefix(path, LogRelativePath))
        {
            return GameResourceCategory.Logs;
        }

        // 截图目录官方历史上出现过单复数两种写法,两种都认
        if (HasPrefix(path, ScreenshotRelativePath)
            || HasPrefix(path, "Client/Saved/Screenshots"))
        {
            return GameResourceCategory.Screenshots;
        }

        if (HasPrefix(path, GamePathResolver.DiffDataDirName))
        {
            return GameResourceCategory.Predownload;
        }

        return GameResourceCategory.Official;
    }

    private static bool HasPrefix(string path, string prefix)
        => path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase)
            || path.Equals(prefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 扫描游戏目录,按分类统计占用。
    /// </summary>
    /// <param name="gameRootDir">游戏根目录;为空或不存在时返回 <see cref="GameResourceUsage.Empty"/>(Scanned=false)。</param>
    /// <param name="ct">取消令牌。</param>
    /// <param name="progress">进度回调(可选),内部按 80ms 节流。</param>
    public static GameResourceUsage Scan(
        string? gameRootDir,
        CancellationToken ct = default,
        IProgress<GameResourceScanProgress>? progress = null)
    {
        if (string.IsNullOrWhiteSpace(gameRootDir) || !Directory.Exists(gameRootDir))
        {
            return GameResourceUsage.Empty;
        }

        string root;
        try
        {
            root = Path.GetFullPath(gameRootDir);
        }
        catch (Exception)
        {
            return GameResourceUsage.Empty;
        }

        long official = 0, predownload = 0, logs = 0, shots = 0, launcherTemp = 0;
        var count = 0;
        var predownloadDirs = new Dictionary<string, (long Bytes, int Count)>(StringComparer.OrdinalIgnoreCase);
        var tracker = new ProgressTracker(progress);

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            // 无权限/被占用的条目只跳过,不让整次统计失败
            IgnoreInaccessible = true,
            // 符号链接/联接点一律不进入:跟进一次就可能把整块盘算成"游戏占用"
            AttributesToSkip = FileAttributes.ReparsePoint,
        };

        try
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", options))
            {
                ct.ThrowIfCancellationRequested();
                tracker.Report(file);

                long size;
                try
                {
                    size = new FileInfo(file).Length;
                }
                catch (Exception)
                {
                    // 单个文件读不到长度(占用/权限/已删除)只跳过它
                    continue;
                }

                count++;
                var relative = ToRelative(root, file);
                switch (Classify(relative))
                {
                    case GameResourceCategory.Logs:
                        logs += size;
                        break;
                    case GameResourceCategory.Screenshots:
                        shots += size;
                        break;
                    case GameResourceCategory.LauncherTemp:
                        launcherTemp += size;
                        break;
                    case GameResourceCategory.Predownload:
                        predownload += size;
                        AccumulatePredownloadVersion(predownloadDirs, relative, size);
                        break;
                    default:
                        official += size;
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // 根目录不可枚举:已统计到的部分照常返回(总比"什么都不知道"好)
        }

        var (diskTotal, diskFree) = ReadDiskInfo(root);

        return new GameResourceUsage
        {
            Scanned = true,
            OfficialBytes = official,
            PredownloadBytes = predownload,
            LogBytes = logs,
            ScreenshotBytes = shots,
            LauncherTempBytes = launcherTemp,
            TotalBytes = official + predownload + logs + shots + launcherTemp,
            FileCount = count,
            DiskTotalBytes = diskTotal,
            DiskFreeBytes = diskFree,
            PredownloadDirs =
            [
                .. predownloadDirs
                    // 版本号倒序(3.8.0 排在 3.7.0 前面):新版本才是用户此刻最关心的那个
                    .OrderByDescending(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(kv => new GameResourceDirSize
                    {
                        Name = kv.Key,
                        Bytes = kv.Value.Bytes,
                        FileCount = kv.Value.Count,
                    }),
            ],
        };
    }

    /// <summary>把 <c>DiffData/&lt;版本&gt;/...</c> 里的版本号取出来累加。</summary>
    private static void AccumulatePredownloadVersion(
        Dictionary<string, (long Bytes, int Count)> buckets,
        string relativePath,
        long size)
    {
        var rest = relativePath[(GamePathResolver.DiffDataDirName.Length + 1)..];
        var slash = rest.IndexOf('/');
        if (slash <= 0)
        {
            return;
        }

        var version = rest[..slash];
        buckets.TryGetValue(version, out var current);
        buckets[version] = (current.Bytes + size, current.Count + 1);
    }

    /// <summary>读游戏所在磁盘的容量与可用空间(失败返回 (0,0),不抛)。</summary>
    private static (long Total, long Free) ReadDiskInfo(string root)
    {
        try
        {
            var driveRoot = Path.GetPathRoot(root);
            if (string.IsNullOrEmpty(driveRoot))
            {
                return (0, 0);
            }

            var drive = new DriveInfo(driveRoot);
            return drive.IsReady ? (drive.TotalSize, drive.AvailableFreeSpace) : (0, 0);
        }
        catch (Exception)
        {
            return (0, 0);
        }
    }

    /// <summary>相对路径(统一 <c>/</c> 分隔),用于展示与分类。</summary>
    private static string ToRelative(string root, string fullPath)
    {
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            return fullPath.Replace('\\', '/');
        }

        var trimmed = fullPath[root.Length..].TrimStart('\\', '/');
        return trimmed.Replace('\\', '/');
    }

    /// <summary>进度节流:最多每 80ms 报一次,避免上万次回调刷爆 UI 线程。</summary>
    private sealed class ProgressTracker(IProgress<GameResourceScanProgress>? sink)
    {
        private long _lastTicks;
        private int _scanned;

        public void Report(string path)
        {
            var count = ++_scanned;
            if (sink is null)
            {
                return;
            }

            var now = DateTime.UtcNow.Ticks;
            if (now - Interlocked.Read(ref _lastTicks) < TimeSpan.TicksPerMillisecond * 80
                && count % 512 != 0)
            {
                return;
            }

            Interlocked.Exchange(ref _lastTicks, now);
            sink.Report(new GameResourceScanProgress(count, path));
        }
    }
}
