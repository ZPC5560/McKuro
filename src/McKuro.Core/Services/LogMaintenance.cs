using McKuro.Core.Infrastructure;

namespace McKuro.Core.Services;

/// <summary>
/// 「清理软件日志」的范围。
/// </summary>
public enum LogCleanScope
{
    /// <summary>
    /// 只清历史（文件名里的日期不是今天）。<b>界面默认应该用这个</b>：今天的日志正被
    /// <see cref="FileLoggerProvider"/> 的常驻句柄追加，是排查"此刻发生了什么"的唯一现场，
    /// 清掉它等于把用户刚遇到的故障证据销毁。
    /// </summary>
    OldOnly,

    /// <summary>
    /// 清掉全部 <c>*.log</c>（含今天的）。用于"重置日志"这类明确要求全清的场景。
    /// Windows 上被打开的今天的文件删不掉，此时静默跳过 —— 不因为一个文件失败就让整批中断。
    /// </summary>
    All,
}

/// <summary>
/// 软件日志目录的占用统计与清理（纯逻辑：不碰 UI、不起进程、不用反射，NativeAOT 下可安全调用）。
///
/// <para>
/// <b>为什么需要它</b>：<see cref="FileLoggerProvider"/> 把日志写成
/// <c>&lt;LogDir&gt;/&lt;Category 最后一段&gt;/McKuro-yyyyMMdd.log</c> —— 每个 category 一个子目录、
/// 每天一个新文件，并且<b>只增不减</b>（跨天只切新文件，旧文件永不回收）。跑上几个月 <c>logs\</c>
/// 能占几百 MB，用户没有任何入口收回它。所以拆成两件事：能测（占了多少、其中多少是历史）
/// 与能清（默认只清历史）。
/// </para>
/// <para>
/// <b>为什么"今天"必须单独保护</b>：今天那个文件很可能正被 <see cref="FileLoggerProvider"/> 的常驻
/// <see cref="StreamWriter"/> 追加写入（它以 <see cref="FileShare.ReadWrite"/> 打开，但那份共享里
/// 没有 <c>Delete</c>）。Windows 上删除一个被打开的句柄会抛 <see cref="IOException"/>。若清理逻辑
/// 让一个异常冒出去，界面就会报"清理失败"，而实际上历史早就删干净了 —— 用户看到的是失败，磁盘却变了。
/// 因此两条路径分开设计：<see cref="LogCleanScope.OldOnly"/> 按文件名日期<b>直接跳过</b>今天
/// （连打开/删除都不尝试），<see cref="LogCleanScope.All"/> 则尽力删、删不掉静默跳过并继续。
/// </para>
/// <para>
/// <b>为什么"历史"按文件名里的日期判，而不是按最后写入时间</b>：文件名里的 <c>yyyyMMdd</c> 就是这份
/// 日志覆盖的那一天，语义稳定。而 <see cref="FileSystemInfo.LastWriteTimeUtc"/> 会被复制、备份、
/// 杀毒软件"扫描即触碰"重写 —— 用户把 logs 目录从旧机器搬过来时，去年的日志会全部显示成"今天"，
/// 于是永远清不掉。
/// </para>
/// <para>
/// <b>为什么统计不直接调 <see cref="StorageFootprint.Measure"/></b>：那个方法统计目录里的
/// <b>全部</b>文件，而日志占用必须只算 <c>*.log</c>。目录里可能躺着崩溃转储、<c>.log.tmp</c>
/// 半成品或用户自己扔进来的东西，把它们算成"日志占用"就会出现「显示占 300 MB、清理只释放 2 MB」的
/// 对不上的数字。所以这里只复用它的<b>枚举口径</b>（<see cref="StorageFootprint.Options"/>，
/// 含 <c>AttributesToSkip = ReparsePoint</c>）与<b>失败口径</b>
/// （<see cref="StorageFootprint.IsExpectedFileSystemFailure"/>、<see cref="StorageFootprint.TryReadLength"/>），
/// 保证「测量看到的文件」与「清理看到的文件」是同一批，同时不把非日志文件算进账。
/// </para>
/// </summary>
public static class LogMaintenance
{
    /// <summary>日志文件名前缀，与 <see cref="FileLoggerProvider"/> 的写入命名保持一致。</summary>
    private const string FileNamePrefix = "McKuro-";

    /// <summary>
    /// 唯一处理的扩展名。只认 <c>.log</c> 结尾：<c>.log.tmp</c>（写盘中间态）、<c>.dmp</c>
    /// 崩溃转储、以及用户自己放进 logs 目录的任何文件都不该被这个功能删掉。
    /// </summary>
    private const string FileExtension = ".log";

    /// <summary>
    /// 统计日志目录占用。
    ///
    /// <para>
    /// 只统计 <c>*.log</c>；目录不存在、或根本不可枚举时返回 <c>default</c>（对调用方而言
    /// "读不到"与"确实没有日志"是同一件事：都没有可显示、可清理的东西）。
    /// </para>
    /// <para>
    /// 单个文件读不到长度就跳过该条（枚举之后又被删/被独占），不让整次统计失败。
    /// </para>
    /// </summary>
    /// <param name="logDir">日志根目录（Windows 上是 exe 同级 <c>logs\</c>，其他平台是 <c>%AppData%/McKuro/logs</c>）。</param>
    /// <param name="now">当前时间（显式传入而非内部取时钟：让"今天"可被测试锁定）。</param>
    /// <returns>总字节、文件数、历史文件数。</returns>
    public static LogFootprint Measure(string logDir, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(logDir) || !Directory.Exists(logDir))
        {
            return default;
        }

        long totalBytes = 0;
        var fileCount = 0;
        var oldFileCount = 0;
        try
        {
            foreach (var path in Directory.EnumerateFiles(logDir, "*", StorageFootprint.Options))
            {
                if (!IsLogFile(path))
                {
                    continue;
                }

                if (!StorageFootprint.TryReadLength(path, out var length))
                {
                    // 读不到长度（枚举出来又被删/被独占到连元数据都取不到）：不计入。
                    // 统计宁可少算，也不该报一个下次清理兑现不了的数字
                    continue;
                }

                totalBytes += length;
                fileCount++;
                if (!IsCurrentDayFileName(Path.GetFileName(path), now))
                {
                    oldFileCount++;
                }
            }
        }
        catch (Exception ex) when (StorageFootprint.IsExpectedFileSystemFailure(ex))
        {
            // 目录枚举中途失败（权限变更/目录被移走）：与"空目录"同义处理
            return default;
        }

        return new LogFootprint(totalBytes, fileCount, oldFileCount);
    }

    /// <summary>
    /// 按范围清理日志，返回真正释放的字节数与删除的文件数。
    ///
    /// <para>
    /// <b>不会抛异常</b>：被占用、无权限、删到一半文件被别人删掉，全部静默跳过并继续。清理是
    /// "尽力而为"的动作，做不完的部分下次再做；把异常抛给界面只会让用户以为整个功能坏了。
    /// </para>
    /// <para>
    /// 删完自底向上回收已空的 category 子目录（<paramref name="logDir"/> 本身保留 —— 它是
    /// <see cref="FileLoggerProvider"/> 的写入根，删掉会让下一次写日志多一次建目录，甚至触发失败）。
    /// 仍有非日志文件残留的子目录不会被删。
    /// </para>
    /// </summary>
    /// <param name="logDir">日志根目录。</param>
    /// <param name="now">当前时间（用于判定"今天"，显式传入以便测试锁定）。</param>
    /// <param name="scope">清理范围。</param>
    public static LogCleanResult Clean(string logDir, DateTime now, LogCleanScope scope)
    {
        // 目录不存在（从没写过日志 / 用户已手工删除）就是"没什么可做"，不是错误
        if (string.IsNullOrWhiteSpace(logDir) || !Directory.Exists(logDir))
        {
            return default;
        }

        long bytesFreed = 0;
        var filesRemoved = 0;
        try
        {
            foreach (var path in Directory.EnumerateFiles(logDir, "*", StorageFootprint.Options))
            {
                if (!IsLogFile(path))
                {
                    continue;
                }

                // OldOnly 对今天的文件连删除都不尝试：删它既有失败风险（见类注释），
                // 又会毁掉当前故障现场
                if (scope == LogCleanScope.OldOnly && IsCurrentDayFileName(Path.GetFileName(path), now))
                {
                    continue;
                }

                // 长度必须在删除前取（删完就读不到了）。取不到就记 0：文件照样尽力删，
                // 只是这份不计入释放量 —— 界面数字宁可偏小，也不能承诺没兑现的字节
                StorageFootprint.TryReadLength(path, out var length);
                try
                {
                    File.Delete(path);
                    filesRemoved++;
                    bytesFreed += length;
                }
                catch (Exception ex) when (StorageFootprint.IsExpectedFileSystemFailure(ex))
                {
                    // 被常驻句柄占用（All 路径下今天的文件）/ 无权限 / 杀毒软件临时锁：
                    // 跳过这一条继续后面的文件
                }
            }
        }
        catch (Exception ex) when (StorageFootprint.IsExpectedFileSystemFailure(ex))
        {
            // 枚举中途失败：保留已经完成的部分结果，比整次回滚更有用
        }

        RemoveEmptyDirectories(logDir);
        return new LogCleanResult(bytesFreed, filesRemoved);
    }

    /// <summary>
    /// 文件名是否指向"今天"：解析 <c>McKuro-yyyyMMdd.log</c> 里的日期段，与 <paramref name="now"/>
    /// 同日为 <c>true</c>。
    ///
    /// <para>
    /// <b>解析不出日期一律返回 <c>false</c>（即视为历史）</b>：这个判断的唯一用途是"保护今天的文件"，
    /// 而认不出来的名字根本不可能是今天那份（<see cref="FileLoggerProvider"/> 写的名字是固定的）。
    /// 反向（认不出就保留）会让 <c>McKuro-backup.log</c> 之类永久占位、且清理指标长期对不上。
    /// 放宽的方向是"多清掉一个无日期的日志"，代价远小于"永远清不掉一堆历史文件"。
    /// </para>
    /// </summary>
    /// <param name="fileName">纯文件名（可含路径分隔符，会被忽略的部分不影响解析结果）。</param>
    /// <param name="now">当前时间。</param>
    public static bool IsCurrentDayFileName(string fileName, DateTime now) =>
        TryParseFileNameDate(Path.GetFileName(fileName), out var date) && date == DateOnly.FromDateTime(now);

    /// <summary>
    /// 从 <c>McKuro-yyyyMMdd.log</c> 解析出日期。
    ///
    /// <para>
    /// <b>手写逐位解析而不用 <c>DateTime.TryParseExact</c></b>：日志名的日期段是机器写的固定 8 位，
    /// 手写解析不分配中间字符串、不受当前区域设置影响，并且能自然拒绝 <c>20241320</c>（13 月）、
    /// <c>20240230</c>（2 月 30 日）这类"看起来是 8 位数字"的非法日期。
    /// </para>
    /// </summary>
    /// <param name="fileName">纯文件名。</param>
    /// <param name="date">解析成功时的日期。</param>
    /// <returns>前缀、8 位日期、扩展名全部匹配且日期合法时返回 <c>true</c>。</returns>
    internal static bool TryParseFileNameDate(string? fileName, out DateOnly date)
    {
        date = default;
        if (string.IsNullOrEmpty(fileName))
        {
            return false;
        }

        // 扩展名不区分大小写：Windows 上用户/工具可能把 .log 改成 .LOG 或 .Log
        if (!fileName.EndsWith(FileExtension, StringComparison.OrdinalIgnoreCase)
            || !fileName.StartsWith(FileNamePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (fileName.Length - FileNamePrefix.Length - FileExtension.Length != 8)
        {
            return false;
        }

        var digits = fileName.AsSpan(FileNamePrefix.Length, 8);
        var year = 0;
        var month = 0;
        var day = 0;
        for (var i = 0; i < digits.Length; i++)
        {
            var c = digits[i];
            if (c is < '0' or > '9')
            {
                return false;
            }

            var value = c - '0';
            if (i < 4)
            {
                year = (year * 10) + value;
            }
            else if (i < 6)
            {
                month = (month * 10) + value;
            }
            else
            {
                day = (day * 10) + value;
            }
        }

        try
        {
            date = new DateOnly(year, month, day);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            // 20241320 / 20240230：8 位数字但不是合法日期，按"解析不出"处理
            return false;
        }
    }

    /// <summary>是否为需要处理的日志文件（只看扩展名，大小写不敏感）。</summary>
    private static bool IsLogFile(string path) =>
        path.EndsWith(FileExtension, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 删除已经变空的<b>子</b>目录（自底向上），返回删除个数。
    /// <para>
    /// 必须自底向上：父目录只有在子目录被删掉之后才可能变空。层深用分隔符个数衡量 —— 所有条目的
    /// 前缀都是同一个 <paramref name="root"/>，分隔符越多必然越深。
    /// </para>
    /// <para>
    /// 只删"此刻真的没有条目"的目录：里面还有删不掉的文件或用户自己放的东西，一律保留。
    /// 联接点/符号链接不会出现在列表里（枚举已跳过重解析点），因此不可能顺着链接删到 logs 之外。
    /// </para>
    /// </summary>
    private static int RemoveEmptyDirectories(string root)
    {
        List<string> children;
        try
        {
            children = Directory.EnumerateDirectories(root, "*", StorageFootprint.Options).ToList();
        }
        catch (Exception ex) when (StorageFootprint.IsExpectedFileSystemFailure(ex))
        {
            return 0;
        }

        // 由深到浅（同深度之间没有父子关系，稳定排序不重要）
        children.Sort((left, right) => DepthOf(right).CompareTo(DepthOf(left)));

        var removed = 0;
        foreach (var child in children)
        {
            try
            {
                if (Directory.EnumerateFileSystemEntries(child).Any())
                {
                    continue;
                }

                Directory.Delete(child);
                removed++;
            }
            catch (Exception ex) when (StorageFootprint.IsExpectedFileSystemFailure(ex))
            {
                // 期间又被写入 / 权限不允许 / 已被别人删掉：保留该目录，继续下一个
            }
        }

        return removed;
    }

    /// <summary>路径深度 = 分隔符个数（同一 <c>root</c> 前缀下与真实层级严格同序）。</summary>
    private static int DepthOf(string path) =>
        path.Count(c => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar);
}

/// <summary>
/// 一次日志占用统计的结果。
/// </summary>
/// <param name="TotalBytes">所有 <c>*.log</c> 的字节数之和。</param>
/// <param name="FileCount">日志文件个数。</param>
/// <param name="OldFileCount">其中"历史"（文件名日期不是今天）的个数。</param>
public readonly record struct LogFootprint(long TotalBytes, int FileCount, int OldFileCount)
{
    /// <summary>是否连一个日志文件都没有（目录不存在、空目录、或只有非日志文件时都为它）。</summary>
    public bool IsEmpty => TotalBytes == 0 && FileCount == 0 && OldFileCount == 0;

    /// <summary>
    /// 是否存在可清理的历史日志。界面用它决定"清理历史日志"按钮是否可用 ——
    /// 注意 <see cref="TotalBytes"/> 不为 0 <b>但</b>全是今天的日志时，这个按钮就该是灰的。
    /// </summary>
    public bool HasOld => OldFileCount > 0;
}

/// <summary>
/// 一次日志清理的结果（用于界面回显"释放了多少"）。
/// </summary>
/// <param name="BytesFreed">真正被释放的字节数（删除失败的条目不计入）。</param>
/// <param name="FilesRemoved">成功删除的文件数。</param>
public readonly record struct LogCleanResult(long BytesFreed, int FilesRemoved)
{
    /// <summary>
    /// 是否动过任何东西。<b>只看 <see cref="FilesRemoved"/>，不看 <see cref="BytesFreed"/></b>：
    /// 0 字节的空日志同样是一次成功删除，界面不该因为字节数为 0 就说"没有可清理的"。
    /// </summary>
    public bool RemovedAnything => FilesRemoved > 0;
}
