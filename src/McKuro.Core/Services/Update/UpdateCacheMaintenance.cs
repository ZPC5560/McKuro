using System.Globalization;
// 同一文件里有两个命名空间:缓存侧(本文件下半部分)要复用 Infrastructure.StorageFootprint
using McKuro.Core.Infrastructure;

namespace McKuro.Core.Infrastructure
{
    /// <summary>
    /// 目录占用统计与体积格式化的**唯一**口径(纯函数:无反射、无进程、无 UI 依赖,可在 NativeAOT 下安全调用)。
    ///
    /// <para>
    /// <b>为什么单独抽出来</b>:项目里已经有两处需要「这个目录占了多大」(更新包缓存
    /// <c>%AppData%\McKuro\updates</c> 与软件日志目录),两处若各自写一份枚举与格式化,迟早会漂移 ——
    /// 一处把隐藏文件算进去、另一处不算;一处 1024 进制、另一处 1000 进制。用户在同一块界面上看到两个
    /// 互相矛盾的「2.6 GB」时,不会觉得是两段代码的问题,只会觉得这个数字不可信。
    /// </para>
    /// <para>
    /// <b>安全边界</b>:枚举统一 <c>AttributesToSkip = ReparsePoint</c>。目录树里若存在联接点/符号链接
    /// (用户手工迁移过目录、或某些"清理/搬家"工具留下的链接),跟进一次就会把链接指向的整棵树当作
    /// 自己的占用算进来 —— 统计出来可能是「整个磁盘」,而清理侧还可能顺着链接删到真实文件。
    /// </para>
    /// </summary>
    public static class StorageFootprint
    {
        /// <summary>
        /// 递归统计目录占用。
        ///
        /// <para>
        /// <see cref="DirectoryCount"/> 只数 <b>子目录</b>(不含 <paramref name="dir"/> 自身),
        /// 也不把联接点/符号链接算进去 —— 否则界面上会出现「占 0 字节却有 300 个目录」的怪数字。
        /// </para>
        /// <para>
        /// 目录不存在、或整棵目录不可枚举(权限/路径失效)返回 <c>(0, 0, 0)</c>:对调用方而言
        /// 「读不到」与「确实为空」是同一件事 —— 都没有可显示、可清理的东西。
        /// </para>
        /// </summary>
        /// <param name="dir">要统计的目录。</param>
        /// <returns>总字节数、文件数、子目录数。</returns>
        public static (long TotalBytes, int FileCount, int DirectoryCount) Measure(string dir)
        {
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            {
                return (0, 0, 0);
            }

            long totalBytes = 0;
            var fileCount = 0;
            var directoryCount = 0;
            try
            {
                foreach (var path in Directory.EnumerateFiles(dir, "*", Options))
                {
                    if (!TryReadLength(path, out var length))
                    {
                        // 枚举出来又被删/被独占到读不到:这一条跳过即可,不让整次统计失败
                        continue;
                    }

                    totalBytes += length;
                    fileCount++;
                }

                foreach (var _ in Directory.EnumerateDirectories(dir, "*", Options))
                {
                    directoryCount++;
                }
            }
            catch (Exception ex) when (IsExpectedFileSystemFailure(ex))
            {
                // 目录本身不可枚举(权限/被占用/路径失效):对调用方等价于"没有占用"
                return (0, 0, 0);
            }

            return (totalBytes, fileCount, directoryCount);
        }

        /// <summary>
        /// 字节 → 人类可读体积(<c>"2.6 GB"</c> / <c>"812.3 MB"</c> / <c>"12.4 KB"</c> / <c>"320 B"</c>)。
        ///
        /// <para>
        /// <b>为什么显式用不变文化</b>:界面上出现 <c>"2,6 GB"</c>(de-DE 的小数点)会让中文用户把小数点读成
        /// 千位分隔符、把 <c>2.6</c> 看成 <c>2,600</c>。体积不是本地化文案,小数点必须固定为 <c>.</c>。
        /// </para>
        /// <para>
        /// <b>单位口径</b>:严格按 1024 进制(B/KB/MB/GB/TB)。这与 Windows 资源管理器以及项目里既有的体积文案
        /// 一致;混用 1000 进制会让同一个目录在「我们的界面」和「属性对话框」里差出 7%。
        /// </para>
        /// </summary>
        /// <param name="bytes">字节数;非正数一律显示 <c>"0 B"</c>(字节数只会因为计算错误变负,不该显示负体积)。</param>
        public static string FormatSize(long bytes)
        {
            if (bytes <= 0)
            {
                return "0 B";
            }

            // 字节级保持整数:界面上 "320.0 B" 既啰嗦又像是算错了
            if (bytes < 1024)
            {
                return bytes.ToString(CultureInfo.InvariantCulture) + " B";
            }

            string[] units = ["KB", "MB", "GB", "TB"];
            double size = bytes;
            var unit = -1;
            while (size >= 1024 && unit < units.Length - 1)
            {
                size /= 1024;
                unit++;
            }

            // 非字节单位固定保留一位小数:再多是假精度(文件系统本身有簇对齐误差),
            // 而界面上「2.6 GB」与「2.6 GB」必须逐字一致,不随值是否整好而抖动
            return size.ToString("0.0", CultureInfo.InvariantCulture) + " " + units[unit];
        }

        /// <summary>
        /// 递归枚举的统一设置。<b>internal 而非 private</b>:缓存清理(<c>UpdateCacheMaintenance</c>)也要用同一套,
        /// 两边共用一个常量才能保证「测量看到的文件」与「清理看到的文件」是同一批。
        /// <para>
        /// <see cref="EnumerationOptions.AttributesToSkip"/> 被显式设成只有 <see cref="FileAttributes.ReparsePoint"/>:
        /// 默认值还带 Hidden/System,会漏掉被标记成隐藏/系统的文件,导致「统计显示 0、磁盘却不空」。
        /// </para>
        /// </summary>
        internal static readonly EnumerationOptions Options = new()
        {
            RecurseSubdirectories = true,
            // 单个文件无权限/被占用只跳过该条目,不让整次统计或清理抛出
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            ReturnSpecialDirectories = false,
        };

        /// <summary>读一个文件的长度;读不到(枚举后又被删/被独占)返回 false。</summary>
        internal static bool TryReadLength(string path, out long length)
        {
            try
            {
                length = new FileInfo(path).Length;
                return true;
            }
            catch (Exception ex) when (IsExpectedFileSystemFailure(ex))
            {
                length = 0;
                return false;
            }
        }

        /// <summary>
        /// 文件系统层"可预期"的失败。只吞这些:统计与清理都是尽力而为的动作,但 <see cref="OutOfMemoryException"/>、
        /// <see cref="OperationCanceledException"/> 之类仍应向上冒泡,不能被伪装成"目录是空的"。
        /// </summary>
        internal static bool IsExpectedFileSystemFailure(Exception ex) =>
            ex is IOException or UnauthorizedAccessException or System.Security.SecurityException
                or ArgumentException or NotSupportedException;
    }
}

namespace McKuro.Core.Services.Update
{
    /// <summary>
    /// 「更新包缓存」的体积统计与按龄清理(纯逻辑:不碰 UI、不起进程、不用反射,可在 NativeAOT 下安全调用)。
    ///
    /// <para>
    /// <b>为什么需要它</b>:McKuro 自更新会把下载好的安装包(<c>*.zip</c>)连同它的解压目录一起留在
    /// <c>%AppData%\McKuro\updates\</c>,替换成功后从不回收。用户每升级一次就多出一份「安装包 + 整个解压后的
    /// 安装目录」,真实反馈里已经堆到 <b>2.6 GB</b>。这些文件在升级结束后既没有保留价值、又没人清理,
    /// 所以要分成两件事:能测量(告诉用户占了多大)、能按龄清理(而不是一上来就全删)。
    /// </para>
    /// <para>
    /// <b>为什么按「龄」清理,而不是「删到某个上限」</b>:升级失败后重试需要原地保留的安装包,用户也可能
    /// 自己去双击那个包手动安装。3 天足以覆盖「一次失败的升级 + 一个周末的手动重试」;超过这个窗口的包
    /// 基本只剩「占地方」一个作用。<c>*.part</c> 是下载到一半的临时文件,没有后续会话续传就再也不会被用上,
    /// 因此给更短的 1 天。
    /// </para>
    /// <para>
    /// <b>体积统计与格式化委托给 <see cref="StorageFootprint"/></b>:软件日志清理要用同一套口径,两份实现迟早漂移。
    /// </para>
    /// </summary>
    public static class UpdateCacheMaintenance
    {
        /// <summary>
        /// 缓存目录的**唯一**拼接口径:<c>{appDataDir}\updates</c>。
        ///
        /// <para>
        /// 单独抽成方法而不是让每个调用点自己 <see cref="Path.Combine(string, string)"/>:缓存目录一旦被
        /// 某个调用点拼成 <c>Updates</c>/<c>update</c>,测量就会报 0、清理就会删不到东西,而这种错误不会有任何
        /// 异常,只是"看起来没东西可清"。
        /// </para>
        /// </summary>
        /// <param name="appDataDir">应用数据根目录(通常 <c>%AppData%\McKuro</c>)。</param>
        public static string DefaultDirectory(string appDataDir) => Path.Combine(appDataDir, "updates");

        /// <summary>
        /// 整包(安装包与其解压目录)的最长保留时间:3 天。见类型注释里对阈值的取舍说明。
        /// </summary>
        public static readonly TimeSpan DefaultMaxAge = TimeSpan.FromDays(3);

        /// <summary>
        /// 半成品下载(<c>*.part</c>)的最长保留时间:1 天。它比整包短,因为半成品缺少续传就没有任何用处。
        /// </summary>
        public static readonly TimeSpan PartMaxAge = TimeSpan.FromDays(1);

        /// <summary>
        /// 字节 → 人类可读体积。**直接委托** <see cref="StorageFootprint.FormatSize"/>:
        /// 保留这个方法是为了让缓存面板不必同时 using 两个命名空间,口径只有一份实现。
        /// </summary>
        /// <param name="bytes">字节数。</param>
        public static string FormatSize(long bytes) => StorageFootprint.FormatSize(bytes);

        /// <summary>
        /// 统计缓存目录占用。目录不存在或不可枚举时返回 <see cref="UpdateCacheUsage.Empty"/>。
        /// <para>规则完全由 <see cref="StorageFootprint.Measure"/> 决定(跳过重解析点 / 不数根目录自身)。</para>
        /// </summary>
        /// <param name="dir">缓存目录。</param>
        public static UpdateCacheUsage Measure(string dir)
        {
            var (totalBytes, fileCount, directoryCount) = StorageFootprint.Measure(dir);
            return new UpdateCacheUsage(totalBytes, fileCount, directoryCount);
        }

        /// <summary>
        /// 按龄清理缓存目录,返回真正释放的字节数与删除计数。
        ///
        /// <para>
        /// <b>规则</b>:<c>*.part</c>(大小写不敏感)超过 <see cref="PartMaxAge"/> 删除,其余文件超过
        /// <paramref name="maxAge"/>(缺省 <see cref="DefaultMaxAge"/>)删除;文件删完后自底向上删除已经变空的
        /// 子目录(<paramref name="dir"/> 本身保留 —— 调用方后续升级还要往里写)。
        /// </para>
        /// <para>
        /// <b>为什么单个文件删失败要静默跳过</b>:缓存目录里的文件常被杀毒/索引服务短暂占用。一次 <c>IOException</c>
        /// 就让整次清理中断,只会让用户看到"清理失败"却什么都没释放。跳过并继续,下次启动还能再清。
        /// </para>
        /// <para>
        /// <b>时间基准用的是最后写入时间</b>:下载完成时写入时间即"落地时间";创建时间会被复制/移动重置,
        /// 解压出来的整包目录更不可靠。所以判断"这个包躺了多久"只看 <see cref="File.GetLastWriteTimeUtc(string)"/>。
        /// </para>
        /// </summary>
        /// <param name="dir">缓存目录。</param>
        /// <param name="utcNow">当前 UTC 时间(显式传入而非内部取时钟:让"按龄"可被测试锁定)。</param>
        /// <param name="maxAge">整包保留时长;传 <c>null</c> 用 <see cref="DefaultMaxAge"/>。</param>
        public static UpdateCacheCleanupResult Clean(string dir, DateTime utcNow, TimeSpan? maxAge = null)
        {
            // 目录不存在(没升级过 / 用户已手工清空)就是"没什么可做",不是错误
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            {
                return default;
            }

            var packageMaxAge = maxAge ?? DefaultMaxAge;
            if (packageMaxAge < TimeSpan.Zero)
            {
                // 负阈值会把"刚下载完的包"也算过期。这里收敛到 0(仍然只认已过去的时间),
                // 不让一个参数写错演变成"删光缓存"
                packageMaxAge = TimeSpan.Zero;
            }

            long bytesFreed = 0;
            var filesRemoved = 0;
            try
            {
                foreach (var path in Directory.EnumerateFiles(dir, "*", StorageFootprint.Options))
                {
                    if (!TryReadEntry(path, out var length, out var lastWriteUtc))
                    {
                        continue;
                    }

                    // "超过阈值才删":用 < 判保留,正好躺在阈值上的条目归入过期一侧,
                    // 否则删不删取决于几毫秒的抖动
                    var limit = IsPartialDownload(path) ? PartMaxAge : packageMaxAge;
                    if (utcNow - lastWriteUtc < limit)
                    {
                        continue;
                    }

                    // 先累计字节再删:删掉之后就读不到长度了。删除失败时回滚,
                    // 保证 BytesFreed 只统计"真的消失了的字节"(否则界面会承诺一个没兑现的释放量)
                    bytesFreed += length;
                    try
                    {
                        File.Delete(path);
                        filesRemoved++;
                    }
                    catch (Exception ex) when (StorageFootprint.IsExpectedFileSystemFailure(ex))
                    {
                        bytesFreed -= length;
                    }
                }
            }
            catch (Exception ex) when (StorageFootprint.IsExpectedFileSystemFailure(ex))
            {
                // 枚举中途失败(权限变更/目录被移走):保留已经完成的清理结果,比整次回滚更有用
            }

            var directoriesRemoved = RemoveEmptyDirectories(dir);
            return new UpdateCacheCleanupResult(bytesFreed, filesRemoved, directoriesRemoved);
        }

        /// <summary>
        /// 读一个文件的长度与最后写入时间;读不到(枚举后又被删/被独占)返回 false。
        /// </summary>
        private static bool TryReadEntry(string path, out long length, out DateTime lastWriteUtc)
        {
            try
            {
                var info = new FileInfo(path);
                length = info.Length;
                lastWriteUtc = info.LastWriteTimeUtc;
                return true;
            }
            catch (Exception ex) when (StorageFootprint.IsExpectedFileSystemFailure(ex))
            {
                length = 0;
                lastWriteUtc = default;
                return false;
            }
        }

        /// <summary>
        /// 是否为半成品下载(<c>*.part</c>,大小写不敏感 —— 下载器可能写成 <c>.PART</c>/<c>.Part</c>)。
        /// </summary>
        private static bool IsPartialDownload(string path) =>
            path.EndsWith(".part", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// 删除已经变空的**子**目录(自底向上),返回删除个数。
        /// <para>
        /// 必须自底向上:父目录只有在子目录被删掉之后才可能变空。层深用分隔符个数衡量 —— 所有条目的前缀
        /// 都是同一个 <paramref name="root"/>,分隔符越多必然越深。
        /// </para>
        /// <para>
        /// 只删"当前真的没有条目"的目录:里面若还有删不掉的文件、或用户自己放的东西,一律保留。
        /// 联接点/符号链接不出现在列表里(枚举已跳过重解析点),因此不可能顺着链接删到外面去。
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

            // 由深到浅(稳定排序不重要:同深度之间没有父子关系)
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
                    // 期间又被写入/权限不允许:保留该目录
                }
            }

            return removed;
        }

        /// <summary>路径深度 = 分隔符个数(同一 root 前缀下与真实层级严格同序)。</summary>
        private static int DepthOf(string path) =>
            path.Count(c => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar);
    }

    /// <summary>
    /// 一次缓存测量结果。
    /// </summary>
    /// <param name="TotalBytes">所有文件字节数之和。</param>
    /// <param name="FileCount">文件个数。</param>
    /// <param name="DirectoryCount">子目录个数(不含被测量的根目录,不含重解析点)。</param>
    public readonly record struct UpdateCacheUsage(long TotalBytes, int FileCount, int DirectoryCount)
    {
        /// <summary>
        /// 空统计:目录不存在、不可枚举、或确实什么都没有时都用它。
        /// <para>
        /// 做成静态属性而不是 <c>new UpdateCacheUsage()</c>:调用点写 <see cref="Empty"/> 一眼就能看出
        /// "这是空结果",而不是像写错参数顺序的构造调用。
        /// </para>
        /// </summary>
        public static UpdateCacheUsage Empty => default;

        /// <summary>是否连一个字节/一个文件/一个子目录都没有。</summary>
        public bool IsEmpty => TotalBytes == 0 && FileCount == 0 && DirectoryCount == 0;
    }

    /// <summary>
    /// 一次清理的结果(用于界面回显"释放了多少")。
    /// </summary>
    /// <param name="BytesFreed">真正被释放的字节数(删除失败的条目不计入)。</param>
    /// <param name="FilesRemoved">成功删除的文件数。</param>
    /// <param name="DirectoriesRemoved">成功删除的空子目录数。</param>
    public readonly record struct UpdateCacheCleanupResult(long BytesFreed, int FilesRemoved, int DirectoriesRemoved)
    {
        /// <summary>
        /// 是否动过任何东西。<b>不看 <see cref="BytesFreed"/></b>:0 字节的空文件同样是一次成功删除,
        /// 界面不该因此说"没有可清理的"。
        /// </summary>
        public bool RemovedAnything => FilesRemoved > 0 || DirectoriesRemoved > 0;
    }
}
