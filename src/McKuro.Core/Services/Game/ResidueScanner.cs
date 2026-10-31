using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace McKuro.Core.Services.Game;

/// <summary>
/// 一个「疑似重复/残留」文件的分类。
/// <para>
/// <b>只收录启动器自己产生的工作产物</b>,绝不按「不在清单里」来判定 —— 不同画质资源包
/// (sd/hd/uhd)本来就互相不在对方清单里,按清单外判定会把几十 GB 合法资源当垃圾删掉。
/// </para>
/// </summary>
public enum ResidueKind
{
    /// <summary>
    /// 替换前备份(<c>.McKuro_backup/</c>)。流式安装/修复替换目标文件前把旧文件移进来;
    /// 最终校验通过后由 GameUpdater 删除,只有中断/校验失败/删除失败时才会残留。
    /// </summary>
    Backup,

    /// <summary>
    /// 未完成的下载半成品(<c>xxx.part</c>)。断连/取消后残留;续传会复用它,
    /// 但安装已完成时它就是纯占位(对应正式文件已存在)。
    /// </summary>
    PartialDownload,

    /// <summary>
    /// 补丁包临时下载目录(<c>DiffData/install_tmp/*</c>)。补丁装完即删;按补丁固定命名(<c>p-&lt;key&gt;</c>),
    /// 中断后保留以便续传 —— 当前本地版本对应的那个由调用方放进 inUsePaths,其它补丁/旧版 GUID 目录才是残留。
    /// </summary>
    PatchTemp,

    /// <summary>
    /// 分组差分合成临时目录(<c>.McKuro_patch/*</c>)。合成结束即删,崩溃则残留。
    /// </summary>
    DiffTemp,
}

/// <summary>扫描到的一条残留。</summary>
public sealed class ResidueFile
{
    public required string FullPath { get; init; }

    /// <summary>相对游戏根目录的路径(用 <c>/</c> 分隔,便于展示)。</summary>
    public required string RelativePath { get; init; }

    public required long Size { get; init; }

    public required ResidueKind Kind { get; init; }

    /// <summary>整目录残留(备份/临时目录)的统计条目标记为 true,删除时整目录递归删。</summary>
    public bool IsDirectory { get; init; }

    /// <summary>该条目的展示名(相对路径就是最好的标识)。</summary>
    public string DisplayPath => RelativePath;
}

/// <summary>残留扫描的进度(供 UI 显示"正在检查…",避免长扫描期间界面像卡死)。</summary>
/// <param name="ScannedFiles">已枚举的候选文件数。</param>
/// <param name="CurrentPath">当前正在处理的路径(展示用,可能为空)。</param>
public readonly record struct ResidueScanProgress(int ScannedFiles, string CurrentPath);

/// <summary>一次残留扫描的结果。</summary>
public sealed class ResidueReport
{
    public static ResidueReport Empty { get; } = new();

    public List<ResidueFile> Items { get; } = [];

    /// <summary>可回收字节数。</summary>
    public long ReclaimBytes => Items.Sum(i => i.Size);

    public bool HasResidue => Items.Count > 0;

    /// <summary>各类残留的条数(便于文案按类汇总)。</summary>
    public int CountOf(ResidueKind kind) => Items.Count(i => i.Kind == kind);
}

/// <summary>
/// 游戏目录「重复/残留文件」扫描(纯函数式:输入根目录 + 清单,输出可删清单)。
/// <para>
/// <b>背景</b>(用户反馈 2026-10):多次断连下载后游戏目录里出现重复文件、体积明显大于服务端清单
/// 应有的总量。根因是启动器自己的三处工作产物在中断后不清理:
/// </para>
/// <list type="number">
/// <item><c>.McKuro_backup/</c> —— 流式安装替换前的备份,<b>安装成功后从未删除</b>(最确定的泄漏点);</item>
/// <item><c>xxx.part</c> —— 未完成的下载半成品,正式文件已落位后仍留着;</item>
/// <item><c>DiffData/install_tmp/*</c> 与 <c>.McKuro_patch/*</c> —— 补丁/差分临时目录,中断残留。</item>
/// </list>
/// <para>
/// <b>安全边界</b>(宁可漏报也不误删):
/// </para>
/// <list type="bullet">
/// <item>只扫上述三类已知前缀,不遍历全盘做"清单外即垃圾"的判定;</item>
/// <item>路径必须严格落在游戏根目录内(含符号链接逃逸防护由调用方
/// <see cref="GameFilePath.CombineUnderRoot"/> 负责);</item>
/// <item>当前版本仍在用的暂存目录(<paramref name="inUsePaths"/>)一律跳过;</item>
/// <item>小于 <see cref="MinSizeBytes"/> 的碎片不进报告,避免弹窗里全是几字节噪声;</item>
/// <item>扫描过程任何异常(权限/占用)都只跳过该条目,不中断整次扫描。</item>
/// </list>
/// </summary>
public static class ResidueScanner
{
    /// <summary>进入报告的最小体积(1 MB):再小的残留不值得让用户做决定。</summary>
    public const long MinSizeBytes = 1024 * 1024;

    /// <summary>
    /// 扫描游戏目录下的可回收残留。
    /// </summary>
    /// <param name="gameRootDir">游戏根目录。</param>
    /// <param name="inUsePaths">
    /// 当前仍在使用的目录绝对路径(如正在续传的预下载暂存目录、进行中的临时目录)。
    /// 命中(含子路径)的条目一律跳过。
    /// </param>
    /// <param name="claimedRelativePaths">
    /// 本次清单里的相对路径集合。用于判定 <c>.part</c> 是否为"正式文件已存在"的孤儿:
    /// 只收录「同时存在对应正式文件」或「正式文件已在清单中」的半成品。
    /// 传 null 表示不做该过滤(调用方明确知道当前没有进行中的下载)。
    /// </param>
    /// <param name="ct">取消令牌。</param>
    /// <param name="progress">进度回调(可选);可为 null。</param>
    public static ResidueReport Scan(
        string? gameRootDir,
        IReadOnlySet<string>? inUsePaths = null,
        IReadOnlySet<string>? claimedRelativePaths = null,
        CancellationToken ct = default,
        IProgress<ResidueScanProgress>? progress = null)
    {
        var report = new ResidueReport();
        if (string.IsNullOrWhiteSpace(gameRootDir) || !Directory.Exists(gameRootDir))
        {
            return report;
        }

        var root = Path.GetFullPath(gameRootDir);
        // 进度节流:扫描可能枚举上万文件,不必每个文件都回调(高频回调会把 UI 线程刷满)。
        var tracker = new ProgressTracker(progress);

        // ---------- ① 替换前备份:整目录统计 ----------
        AddDirectory(
            report,
            root,
            Path.Combine(root, UpdateInstaller.BackupDirName),
            ResidueKind.Backup,
            inUsePaths,
            ct,
            tracker);

        // ---------- ② 补丁包临时目录 ----------
        // 官方补丁临时目录按补丁固定命名(install_tmp/p-<key>),下载中断后保留以便续传;
        // 当前补丁那个子目录会出现在 inUsePaths 里。此时不能把整个 install_tmp 当一条残留(会连它一起删),
        // 改为逐个子目录统计:当前补丁的跳过,其它补丁/旧版 GUID 目录照常报告。
        var installTemp = Path.Combine(root, GamePathResolver.DiffDataDirName, GamePathResolver.InstallTempDirName);
        if (ContainsInUsePath(installTemp, inUsePaths))
        {
            AddChildDirectories(report, root, installTemp, ResidueKind.PatchTemp, inUsePaths, ct, tracker);
        }
        else
        {
            AddDirectory(report, root, installTemp, ResidueKind.PatchTemp, inUsePaths, ct, tracker);
        }

        // ---------- ③ 分组差分临时目录 ----------
        AddDirectory(
            report,
            root,
            Path.Combine(root, GamePathResolver.PatchTempDirName),
            ResidueKind.DiffTemp,
            inUsePaths,
            ct,
            tracker);

        // ---------- ④ 孤儿 .part(正式文件已落位/已在清单里) ----------
        ScanOrphanParts(report, root, claimedRelativePaths, inUsePaths, ct, tracker);

        return report;
    }

    /// <summary>进度节流器:最多每 80ms 报一次,避免上万次回调刷爆 UI 线程。</summary>
    private sealed class ProgressTracker(IProgress<ResidueScanProgress>? sink)
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
                && count % 64 != 0)
            {
                return;
            }
            Interlocked.Exchange(ref _lastTicks, now);
            sink.Report(new ResidueScanProgress(count, path));
        }
    }

    /// <summary>统计一个残留目录(整体删除:目录内的分片都是同一批临时产物)。</summary>
    private static void AddDirectory(
        ResidueReport report,
        string root,
        string dir,
        ResidueKind kind,
        IReadOnlySet<string>? inUsePaths,
        CancellationToken ct,
        ProgressTracker tracker)
    {
        if (!Directory.Exists(dir) || IsInUse(dir, inUsePaths))
        {
            return;
        }

        long total = 0;
        var count = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                tracker.Report(file);
                try
                {
                    total += new FileInfo(file).Length;
                    count++;
                }
                catch (Exception)
                {
                    // 单个文件读不到(占用/权限)只跳过它
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // 目录不可枚举:整条跳过
            return;
        }

        if (count == 0 || total < MinSizeBytes)
        {
            return;
        }

        report.Items.Add(new ResidueFile
        {
            FullPath = dir,
            RelativePath = ToRelative(root, dir),
            Size = total,
            Kind = kind,
            IsDirectory = true,
        });
    }

    /// <summary>
    /// 孤儿半成品:同名正式文件已存在(说明这批字节已经落位完毕),或该路径已在本次清单内
    /// 但正式文件同样存在。仍在下拉的文件(正式文件不存在)一律跳过 —— 那是续传要用的。
    /// </summary>
    private static void ScanOrphanParts(
        ResidueReport report,
        string root,
        IReadOnlySet<string>? claimedRelativePaths,
        IReadOnlySet<string>? inUsePaths,
        CancellationToken ct,
        ProgressTracker tracker)
    {
        IEnumerable<string> parts;
        try
        {
            parts = Directory.EnumerateFiles(root, "*.part", SearchOption.AllDirectories);
        }
        catch (Exception)
        {
            return;
        }

        try
        {
            foreach (var part in parts)
            {
                ct.ThrowIfCancellationRequested();
                tracker.Report(part);
                if (IsInUse(part, inUsePaths))
                {
                    continue;
                }

                var full = Path.GetFullPath(part);
                // 只处理严格位于根目录内的文件(防符号链接/相对路径逃逸)
                if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var official = full[..^".part".Length];
                var hasOfficial = File.Exists(official);
                if (!hasOfficial)
                {
                    // 没有正式文件:可能是"下到一半"的续传现场。只有清单根本没提这个路径时
                    // 才敢当作孤儿(例如该文件在新版本里已被移除)。
                    var rel = ToRelative(root, official);
                    if (claimedRelativePaths is null || claimedRelativePaths.Contains(rel))
                    {
                        continue;
                    }
                }

                long size;
                try
                {
                    size = new FileInfo(full).Length;
                }
                catch (Exception)
                {
                    continue;
                }
                if (size < MinSizeBytes)
                {
                    continue;
                }

                report.Items.Add(new ResidueFile
                {
                    FullPath = full,
                    RelativePath = ToRelative(root, full),
                    Size = size,
                    Kind = ResidueKind.PartialDownload,
                    IsDirectory = false,
                });
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // 枚举中途失败:保留已收集的部分结果
        }
    }

    /// <summary>逐个子目录统计(每个子目录各自做"在用"判定与体积门槛)。</summary>
    private static void AddChildDirectories(
        ResidueReport report,
        string root,
        string dir,
        ResidueKind kind,
        IReadOnlySet<string>? inUsePaths,
        CancellationToken ct,
        ProgressTracker tracker)
    {
        List<string> children;
        try
        {
            children = Directory.Exists(dir) ? Directory.EnumerateDirectories(dir).ToList() : [];
        }
        catch (Exception)
        {
            return;
        }
        foreach (var child in children)
        {
            ct.ThrowIfCancellationRequested();
            AddDirectory(report, root, child, kind, inUsePaths, ct, tracker);
        }
    }

    /// <summary>某个"仍在使用"的路径是否位于 <paramref name="dir"/> 之内(严格的后代,不含它本身)。</summary>
    private static bool ContainsInUsePath(string dir, IReadOnlySet<string>? inUsePaths)
    {
        if (inUsePaths is null || inUsePaths.Count == 0)
        {
            return false;
        }
        var prefix = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        foreach (var used in inUsePaths)
        {
            if (string.IsNullOrWhiteSpace(used))
            {
                continue;
            }
            if (Path.GetFullPath(used).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>路径是否落在"仍在使用"的目录内。</summary>
    private static bool IsInUse(string path, IReadOnlySet<string>? inUsePaths)
    {
        if (inUsePaths is null || inUsePaths.Count == 0)
        {
            return false;
        }
        var full = Path.GetFullPath(path);
        foreach (var used in inUsePaths)
        {
            if (string.IsNullOrWhiteSpace(used))
            {
                continue;
            }
            var usedFull = Path.GetFullPath(used);
            if (full.Equals(usedFull, StringComparison.OrdinalIgnoreCase)
                || full.StartsWith(usedFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static string ToRelative(string root, string path)
        => Path.GetRelativePath(root, path).Replace('\\', '/');

    /// <summary>
    /// 把报告里的条目删掉,返回 (实际回收字节, 失败条数)。
    /// <para>只删报告里的路径(不做任何"扫一遍再删"的推断),失败条目保留原文件。</para>
    /// </summary>
    public static (long FreedBytes, int Failures) Delete(ResidueReport report, CancellationToken ct = default)
    {
        long freed = 0;
        var failures = 0;
        foreach (var item in report.Items)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (item.IsDirectory)
                {
                    if (!Directory.Exists(item.FullPath))
                    {
                        continue;
                    }
                    Directory.Delete(item.FullPath, recursive: true);
                }
                else
                {
                    if (!File.Exists(item.FullPath))
                    {
                        continue;
                    }
                    File.Delete(item.FullPath);
                }
                freed += item.Size;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                failures++;
            }
        }
        return (freed, failures);
    }

    /// <summary>
    /// 游戏目录实际占用字节(排除残留后即为"应有体积"),用于和清单总量对比给出"明显过大"的信号。
    /// </summary>
    /// <param name="dir">要统计的目录。</param>
    /// <param name="ct">取消令牌。</param>
    /// <param name="progress">进度回调(可选);这一趟要遍历整个游戏目录,是最慢的一步。</param>
    public static long MeasureDirectoryBytes(
        string? dir,
        CancellationToken ct = default,
        IProgress<ResidueScanProgress>? progress = null)
    {
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
        {
            return 0;
        }
        var tracker = new ProgressTracker(progress);
        long total = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                tracker.Report(file);
                try
                {
                    total += new FileInfo(file).Length;
                }
                catch (Exception)
                {
                    // 跳过读不到的文件
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // 目录不可枚举
        }
        return total;
    }
}
