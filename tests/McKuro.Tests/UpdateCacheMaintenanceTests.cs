using System.Globalization;
using McKuro.Core.Infrastructure;
using McKuro.Core.Services.Update;

namespace McKuro.Tests;

/// <summary>
/// 更新包缓存(测量 + 按龄清理)与共用占用统计口径的回归测试。
/// <para>
/// 对应用户反馈:自更新把安装包和解压目录都留在 <c>%AppData%\McKuro\updates\</c>,每次升级累加,
/// 已堆到 2.6 GB。这些用例锁定三件容易悄悄坏掉的事:
/// (1) 「刚下载完的包」绝不能被删;(2) <c>*.part</c> 的阈值确实比整包短;
/// (3) 联接点/符号链接既不能被统计进体积,也不能被顺着删到目录外。
/// </para>
/// <para>
/// 另外锁定「缓存」与「软件日志」共用同一套口径:<see cref="StorageFootprint"/> 与
/// <see cref="UpdateCacheMaintenance"/> 在同一目录上的测量结果必须逐项相同,
/// 否则界面上两处会给出互相矛盾的体积。
/// </para>
/// </summary>
public class UpdateCacheMaintenanceTests
{
    // ---------- 契约常量 ----------

    [Fact]
    public void DefaultDirectory_Is_Updates_Under_AppData()
    {
        var appData = Path.Combine(Path.GetTempPath(), "McKuro", "AppData");
        Assert.Equal(Path.Combine(appData, "updates"), UpdateCacheMaintenance.DefaultDirectory(appData));
    }

    [Fact]
    public void Retention_Windows_Are_Three_Days_For_Packages_And_One_Day_For_Parts()
    {
        Assert.Equal(TimeSpan.FromDays(3), UpdateCacheMaintenance.DefaultMaxAge);
        Assert.Equal(TimeSpan.FromDays(1), UpdateCacheMaintenance.PartMaxAge);
        // 半成品必须比整包更早被回收,否则这条独立阈值没有意义
        Assert.True(UpdateCacheMaintenance.PartMaxAge < UpdateCacheMaintenance.DefaultMaxAge);
    }

    // ---------- FormatSize ----------

    /// <summary>
    /// 用户反馈里的原文是「2.6 GB」——单位与小数位就是可读性本身,所以逐条锁死。
    /// </summary>
    [Theory]
    [InlineData(2_600_000_000L, "2.4 GB")]  // 1024 进制:2.6e9B ≈ 2.42GiB → 2.4 GB
    [InlineData(2_791_705_241L, "2.6 GB")]  // 2.6 GiB
    [InlineData(851_443_712L, "812.0 MB")]  // 恰好 812MiB
    [InlineData(13_002_342L, "12.4 MB")]
    [InlineData(12_698L, "12.4 KB")]
    [InlineData(320L, "320 B")]
    [InlineData(1023L, "1023 B")]
    [InlineData(0L, "0 B")]
    [InlineData(-5L, "0 B")]
    public void FormatSize_Uses_Binary_Units_With_One_Decimal(long bytes, string expected)
    {
        Assert.Equal(expected, UpdateCacheMaintenance.FormatSize(bytes));
    }

    /// <summary>
    /// 界面上的体积不是本地化文案:德语环境下 <c>"2,6 GB"</c> 会让中文用户把小数点读成千位分隔符。
    /// </summary>
    [Fact]
    public void FormatSize_Is_Culture_Invariant()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var text = UpdateCacheMaintenance.FormatSize(2_791_705_241L);
            Assert.Equal("2.6 GB", text);
            Assert.DoesNotContain(",", text, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    /// <summary>
    /// 缓存面板与日志面板必须拿到**同一个字符串**:两处各留一份实现时,这里会先红。
    /// </summary>
    [Fact]
    public void UpdateCacheMaintenance_FormatSize_Delegates_To_StorageFootprint()
    {
        foreach (var bytes in new long[] { 0, 320, 12_698, 13_002_342, 851_443_712, 2_791_705_241 })
        {
            Assert.Equal(StorageFootprint.FormatSize(bytes), UpdateCacheMaintenance.FormatSize(bytes));
        }
    }

    // ---------- StorageFootprint: 共用口径 ----------

    [Fact]
    public void StorageFootprint_Measure_Missing_Directory_Is_Zero_Tuple()
    {
        var missing = Path.Combine(TempRoot(), "no-such-" + Guid.NewGuid().ToString("N"));

        var (totalBytes, fileCount, directoryCount) = StorageFootprint.Measure(missing);

        Assert.Equal(0, totalBytes);
        Assert.Equal(0, fileCount);
        Assert.Equal(0, directoryCount);
    }

    [Fact]
    public void StorageFootprint_Measure_Empty_Directory_Is_Zero_Tuple()
    {
        var root = NewTempDir();
        try
        {
            Assert.Equal((0L, 0, 0), StorageFootprint.Measure(root));
        }
        finally
        {
            SafeDeleteTree(root);
        }
    }

    /// <summary>
    /// 日志目录的现实形态:多层子目录 + 多个文件。字节数必须是精确和,文件数必须精确,
    /// 子目录数不含根目录自身。
    /// </summary>
    [Fact]
    public void StorageFootprint_Measure_Counts_Nested_Directories_And_Files()
    {
        var root = NewTempDir();
        try
        {
            NewFile(root, "app-2026-11-11.log", 1024);
            NewFile(root, "2026-11/11/app-1.log", 2048);
            NewFile(root, "2026-11/11/app-2.log", 4096);
            NewFile(root, "2026-11/12/app-1.log", 512);
            NewFile(root, "archive/2026-10/deep/x.log", 256);
            Directory.CreateDirectory(Path.Combine(root, "archive", "empty"));

            var (totalBytes, fileCount, directoryCount) = StorageFootprint.Measure(root);

            Assert.Equal(1024 + 2048 + 4096 + 512 + 256, totalBytes);
            Assert.Equal(5, fileCount);
            // 2026-11 / 2026-11/11 / 2026-11/12 / archive / archive/2026-10 /
            // archive/2026-10/deep / archive/empty
            Assert.Equal(7, directoryCount);
        }
        finally
        {
            SafeDeleteTree(root);
        }
    }

    /// <summary>
    /// <b>钉住不漂移</b>:同一个目录上,缓存侧与共用侧的三个数字必须完全一致。
    /// 这条用例的作用不是测算法(上面各自已测),而是防止将来有人只改其中一边
    /// ——比如给缓存侧加「跳过 *.tmp」,于是两块界面报出不同的体积。
    /// </summary>
    [Fact]
    public void StorageFootprint_And_UpdateCache_Measure_Agree_On_Same_Directory()
    {
        var root = NewTempDir();
        try
        {
            NewFile(root, "McKuro-1.3.9.zip", 1000);
            NewFile(root, "extract-1.3.9/McKuro.exe", 2000);
            NewFile(root, "extract-1.3.9/Assets/a.pak", 3000);
            NewFile(root, "extract-1.3.9/Assets/deep/b.pak", 4000);
            NewFile(root, "empty-sub/keep.txt", 50);

            var (totalBytes, fileCount, directoryCount) = StorageFootprint.Measure(root);
            var usage = UpdateCacheMaintenance.Measure(root);

            Assert.Equal(totalBytes, usage.TotalBytes);
            Assert.Equal(fileCount, usage.FileCount);
            Assert.Equal(directoryCount, usage.DirectoryCount);

            // 顺带确认数字本身没错(不是「两边都错成 0」也算一致)
            Assert.Equal(10_050, totalBytes);
            Assert.Equal(5, fileCount);
            Assert.Equal(4, directoryCount);
        }
        finally
        {
            SafeDeleteTree(root);
        }
    }

    // ---------- Measure ----------

    [Fact]
    public void Measure_Missing_Directory_Is_Empty()
    {
        var missing = Path.Combine(TempRoot(), "no-such-" + Guid.NewGuid().ToString("N"));

        var usage = UpdateCacheMaintenance.Measure(missing);

        Assert.True(usage.IsEmpty);
        Assert.Equal(UpdateCacheUsage.Empty, usage);
        Assert.Equal(0, usage.TotalBytes);
        Assert.Equal(0, usage.FileCount);
        Assert.Equal(0, usage.DirectoryCount);
    }

    [Fact]
    public void Measure_Empty_Directory_Is_Empty_But_Reports_Zero_Directories()
    {
        var root = NewTempDir();
        try
        {
            var usage = UpdateCacheMaintenance.Measure(root);
            Assert.True(usage.IsEmpty);
        }
        finally
        {
            SafeDeleteTree(root);
        }
    }

    /// <summary>
    /// 统计口径:字节/文件/子目录三项都要对,且**不含**根目录自身 —— 否则界面上会出现
    /// 「0 字节 1 个目录」这种看着像 bug 的数字。
    /// </summary>
    [Fact]
    public void Measure_Counts_Bytes_Files_And_Child_Directories_Recursively()
    {
        var root = NewTempDir();
        try
        {
            NewFile(root, "McKuro-1.3.9.zip", 1000);
            NewFile(root, "extract-1.3.9/McKuro.exe", 2000);
            NewFile(root, "extract-1.3.9/Assets/a.pak", 3000);
            NewFile(root, "deep/a/b/c.txt", 7);
            Directory.CreateDirectory(Path.Combine(root, "empty-sub"));

            var usage = UpdateCacheMaintenance.Measure(root);

            Assert.False(usage.IsEmpty);
            Assert.Equal(6007, usage.TotalBytes);
            Assert.Equal(4, usage.FileCount);
            // extract-1.3.9 / extract-1.3.9/Assets / deep / deep/a / deep/a/b / empty-sub
            Assert.Equal(6, usage.DirectoryCount);
        }
        finally
        {
            SafeDeleteTree(root);
        }
    }

    // ---------- Clean: 前置条件 ----------

    [Fact]
    public void Clean_Missing_Directory_Is_Noop()
    {
        var missing = Path.Combine(TempRoot(), "no-such-" + Guid.NewGuid().ToString("N"));

        var result = UpdateCacheMaintenance.Clean(missing, DateTime.UtcNow);

        Assert.Equal(default, result);
        Assert.False(result.RemovedAnything);
        Assert.Equal(0, result.BytesFreed);
    }

    [Fact]
    public void Clean_Empty_Directory_Removes_Nothing_And_Keeps_The_Directory()
    {
        var root = NewTempDir();
        try
        {
            var result = UpdateCacheMaintenance.Clean(root, DateTime.UtcNow);

            Assert.False(result.RemovedAnything);
            Assert.True(Directory.Exists(root));
        }
        finally
        {
            SafeDeleteTree(root);
        }
    }

    // ---------- Clean: 按龄 ----------

    /// <summary>
    /// 默认阈值(3 天):躺了 4 天的整包删掉,躺了 2 天的保留 ——
    /// 「刚下载完/正在重试」的包被误删是这条功能最不可接受的失败。
    /// </summary>
    [Fact]
    public void Clean_Deletes_Whole_Package_Past_Default_MaxAge_And_Keeps_Fresh_One()
    {
        var root = NewTempDir();
        var now = DateTime.UtcNow;
        try
        {
            NewFile(root, "old/McKuro-1.3.8.zip", 1500, now.AddDays(-4));
            NewFile(root, "old/McKuro.exe", 500, now.AddDays(-4));
            NewFile(root, "fresh/McKuro-1.3.9.zip", 700, now.AddDays(-2));

            var result = UpdateCacheMaintenance.Clean(root, now);

            Assert.True(result.RemovedAnything);
            Assert.Equal(2, result.FilesRemoved);
            Assert.Equal(2000, result.BytesFreed);
            Assert.False(File.Exists(Path.Combine(root, "old", "McKuro-1.3.8.zip")));
            Assert.False(File.Exists(Path.Combine(root, "old", "McKuro.exe")));
            Assert.True(File.Exists(Path.Combine(root, "fresh", "McKuro-1.3.9.zip")));

            // 缓存目录本身必须留着:下一次升级还要往里写
            Assert.True(Directory.Exists(root));
        }
        finally
        {
            SafeDeleteTree(root);
        }
    }

    /// <summary>
    /// <c>*.part</c> 用更短的 1 天阈值:同一个 2 天前的文件,作为半成品该删,作为整包该留。
    /// 这条用例同时证明两条阈值是**分别**生效的,而不是共用 maxAge。
    /// </summary>
    [Fact]
    public void Clean_Uses_Shorter_Part_MaxAge_For_Partial_Downloads()
    {
        var root = NewTempDir();
        var now = DateTime.UtcNow;
        try
        {
            NewFile(root, "McKuro-1.3.9.zip.part", 400, now.AddDays(-2));
            NewFile(root, "McKuro-1.3.9.zip", 900, now.AddDays(-2));

            var result = UpdateCacheMaintenance.Clean(root, now);

            Assert.Equal(1, result.FilesRemoved);
            Assert.Equal(400, result.BytesFreed);
            Assert.False(File.Exists(Path.Combine(root, "McKuro-1.3.9.zip.part")));
            // 同一龄期的整包仍在 3 天窗口内,必须保住
            Assert.True(File.Exists(Path.Combine(root, "McKuro-1.3.9.zip")));
        }
        finally
        {
            SafeDeleteTree(root);
        }
    }

    /// <summary>
    /// 扩展名判定必须大小写不敏感:下载器在不同路径上会写出 <c>.PART</c>/<c>.Part</c>,
    /// 只比 <c>".part"</c> 会让这些半成品走 3 天阈值(用户最想清掉的就是它们)。
    /// </summary>
    [Theory]
    [InlineData(".PART")]
    [InlineData(".Part")]
    [InlineData(".part")]
    public void Clean_Treats_Part_Extension_Case_Insensitively(string extension)
    {
        var root = NewTempDir();
        var now = DateTime.UtcNow;
        try
        {
            NewFile(root, "download-1.3.9.zip" + extension, 123, now.AddDays(-1).AddHours(-1));

            var result = UpdateCacheMaintenance.Clean(root, now);

            Assert.Equal(1, result.FilesRemoved);
            Assert.Equal(123, result.BytesFreed);
        }
        finally
        {
            SafeDeleteTree(root);
        }
    }

    /// <summary>显式传入的 maxAge 覆盖默认 3 天(调用方可能给"7 天"这种更保守的策略)。</summary>
    [Fact]
    public void Clean_Honors_Explicit_MaxAge_Override()
    {
        var root = NewTempDir();
        var now = DateTime.UtcNow;
        try
        {
            NewFile(root, "McKuro-1.3.8.zip", 100, now.AddDays(-5));

            var kept = UpdateCacheMaintenance.Clean(root, now, TimeSpan.FromDays(7));
            Assert.False(kept.RemovedAnything);
            Assert.True(File.Exists(Path.Combine(root, "McKuro-1.3.8.zip")));

            var removed = UpdateCacheMaintenance.Clean(root, now, TimeSpan.FromDays(1));
            Assert.Equal(1, removed.FilesRemoved);
            Assert.Equal(100, removed.BytesFreed);
        }
        finally
        {
            SafeDeleteTree(root);
        }
    }

    // ---------- Clean: 目录回收 ----------

    /// <summary>
    /// 文件删完之后,空的解压目录子树必须一起回收 —— 只删文件不删目录的话,
    /// 用户看到的仍是"updates 里一堆目录",磁盘没释放,投诉照旧。
    /// </summary>
    [Fact]
    public void Clean_Removes_Emptied_Nested_Directories_But_Keeps_Root()
    {
        var root = NewTempDir();
        var now = DateTime.UtcNow;
        try
        {
            NewFile(root, "extract-1.3.8/McKuro.exe", 1000, now.AddDays(-10));
            NewFile(root, "extract-1.3.8/sub/deeper/a.dll", 1000, now.AddDays(-10));
            // 这个子树里还有一个新鲜文件,整棵子树都不能删
            NewFile(root, "extract-1.3.9/McKuro.exe", 1000, now.AddDays(-10));
            NewFile(root, "extract-1.3.9/keep.txt", 42, now);

            var result = UpdateCacheMaintenance.Clean(root, now);

            Assert.Equal(3, result.FilesRemoved);
            Assert.False(Directory.Exists(Path.Combine(root, "extract-1.3.8")));
            Assert.True(Directory.Exists(Path.Combine(root, "extract-1.3.9")));
            Assert.True(File.Exists(Path.Combine(root, "extract-1.3.9", "keep.txt")));
            Assert.True(Directory.Exists(root));

            // extract-1.3.8/sub/deeper、extract-1.3.8/sub、extract-1.3.8
            Assert.Equal(3, result.DirectoriesRemoved);
            Assert.True(result.RemovedAnything);
        }
        finally
        {
            SafeDeleteTree(root);
        }
    }

    /// <summary>
    /// 目录里还有用户自己放的东西(非空)时不能删 —— 本功能只清"按龄的缓存文件",
    /// 不做"整树删除"。
    /// </summary>
    [Fact]
    public void Clean_Keeps_Non_Empty_Directories()
    {
        var root = NewTempDir();
        var now = DateTime.UtcNow;
        try
        {
            NewFile(root, "extract-1.3.8/old.exe", 10, now.AddDays(-10));
            NewFile(root, "extract-1.3.8/user-notes.txt", 5, now);

            var result = UpdateCacheMaintenance.Clean(root, now);

            Assert.Equal(1, result.FilesRemoved);
            Assert.True(Directory.Exists(Path.Combine(root, "extract-1.3.8")));
            Assert.True(File.Exists(Path.Combine(root, "extract-1.3.8", "user-notes.txt")));
        }
        finally
        {
            SafeDeleteTree(root);
        }
    }

    // ---------- Clean: 失败容忍 ----------

    /// <summary>
    /// 被杀毒/索引服务独占的包只跳过该文件,不能中断整次清理,也不能把它的字节算进
    /// <see cref="UpdateCacheCleanupResult.BytesFreed"/>(否则界面承诺了一个没兑现的释放量)。
    /// </summary>
    [Fact]
    public void Clean_Skips_Locked_File_And_Continues_With_The_Rest()
    {
        var root = NewTempDir();
        var now = DateTime.UtcNow;
        try
        {
            var locked = NewFile(root, "locked.zip", 1000, now.AddDays(-10));
            NewFile(root, "free.zip", 500, now.AddDays(-10));

            using (File.Open(locked, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var result = UpdateCacheMaintenance.Clean(root, now);

                Assert.True(File.Exists(locked));
                Assert.False(File.Exists(Path.Combine(root, "free.zip")));
                // 只统计真正删掉的那一个
                Assert.Equal(1, result.FilesRemoved);
                Assert.Equal(500, result.BytesFreed);
            }
        }
        finally
        {
            SafeDeleteTree(root);
        }
    }

    // ---------- 重解析点 ----------

    /// <summary>
    /// <b>最危险的一条</b>:缓存目录里若存在联接点/符号链接,跟进一次就会把链接指向的整棵树
    /// 当作缓存统计,清理时还会顺着链接删掉真实文件(可能就在安装目录或游戏目录里)。
    /// 因此 <c>AttributesToSkip = ReparsePoint</c> 必须同时作用于 Measure 和 Clean
    /// (两侧共用 <c>StorageFootprint.Options</c>)。
    /// <para>
    /// 环境限制:创建符号链接需要开发者模式或管理员权限,部分环境也会被组策略拦住。
    /// 创建失败时本用例降级为"跳过链接相关断言"并保留注释说明 —— 实现侧的
    /// <c>AttributesToSkip</c> 不能因此省掉(见 <c>StorageFootprint.Options</c>)。
    /// </para>
    /// </summary>
    [Fact]
    public void Measure_And_Clean_Do_Not_Follow_Reparse_Points()
    {
        var root = NewTempDir();
        var outside = NewTempDir();
        var linkPath = Path.Combine(root, "linked-cache");
        var now = DateTime.UtcNow;
        var linked = false;
        try
        {
            // 链接目标模拟"用户真实的安装目录":里面的文件比缓存老得多,一旦被跟进就必删
            NewFile(outside, "installed/McKuro.exe", 4096, now.AddDays(-100));
            NewFile(outside, "installed/data.pak", 8192, now.AddDays(-100));

            linked = TryCreateDirectoryLink(linkPath, outside);
            if (!linked)
            {
                // 当前环境不允许创建链接:跳过这条断言(见上方注释),其余用例已覆盖实现路径
                return;
            }

            // 链接里的内容既不能计入体积/文件数,也不能计入目录数
            var usage = UpdateCacheMaintenance.Measure(root);
            Assert.True(usage.IsEmpty);
            Assert.Equal(0, usage.TotalBytes);
            Assert.Equal(0, usage.FileCount);
            Assert.Equal(0, usage.DirectoryCount);

            // 共用侧同样不能跟进,否则日志面板会把链接目标算进日志体积
            Assert.Equal((0L, 0, 0), StorageFootprint.Measure(root));

            // 链接本身与它指向的真实文件都必须活下来
            var result = UpdateCacheMaintenance.Clean(root, now);
            Assert.False(result.RemovedAnything);
            Assert.Equal(0, result.BytesFreed);
            Assert.True(Directory.Exists(linkPath));
            Assert.True(File.Exists(Path.Combine(outside, "installed", "McKuro.exe")));
            Assert.True(File.Exists(Path.Combine(outside, "installed", "data.pak")));
        }
        finally
        {
            if (linked)
            {
                TryDeleteLink(linkPath);
            }

            SafeDeleteTree(outside);
            SafeDeleteTree(root);
        }
    }

    // ---------- 组合场景 ----------

    /// <summary>
    /// 端到端还原用户现场:一次升级留下「包 + 解压目录」,再叠一个半成品下载和一个新包,
    /// 清理后应该只剩新东西,并且测量值回到与新东西一致。
    /// </summary>
    [Fact]
    public void Clean_Then_Measure_Leaves_Only_Fresh_Cache()
    {
        var root = NewTempDir();
        var now = DateTime.UtcNow;
        try
        {
            NewFile(root, "McKuro-1.3.7.zip", 2_000, now.AddDays(-30));
            NewFile(root, "extract-1.3.7/McKuro.exe", 3_000, now.AddDays(-30));
            NewFile(root, "extract-1.3.8/McKuro.dll", 5_000, now.AddDays(-9));
            NewFile(root, "McKuro-1.3.9.zip.part", 6_000, now.AddDays(-2));
            NewFile(root, "McKuro-1.3.9.zip", 7_000, now.AddHours(-2));

            var result = UpdateCacheMaintenance.Clean(root, now);

            Assert.Equal(4, result.FilesRemoved);
            Assert.Equal(16_000, result.BytesFreed);
            Assert.True(result.RemovedAnything);

            var usage = UpdateCacheMaintenance.Measure(root);
            Assert.Equal(7_000, usage.TotalBytes);
            Assert.Equal(1, usage.FileCount);
            Assert.Equal(0, usage.DirectoryCount);
        }
        finally
        {
            SafeDeleteTree(root);
        }
    }

    [Fact]
    public void Usage_And_Result_Flags_Track_Their_Contents()
    {
        Assert.True(UpdateCacheUsage.Empty.IsEmpty);
        Assert.False(new UpdateCacheUsage(0, 1, 0).IsEmpty);
        Assert.False(new UpdateCacheUsage(0, 0, 1).IsEmpty);
        Assert.False(new UpdateCacheUsage(1, 0, 0).IsEmpty);

        // 0 字节的空文件删掉也是"删过东西",不能因为 BytesFreed == 0 就报无事发生
        Assert.True(new UpdateCacheCleanupResult(0, 1, 0).RemovedAnything);
        Assert.True(new UpdateCacheCleanupResult(0, 0, 1).RemovedAnything);
        Assert.False(new UpdateCacheCleanupResult(123, 0, 0).RemovedAnything);
    }

    // ---------- 辅助 ----------

    private static string TempRoot() => Path.Combine(Path.GetTempPath(), "McKuro-update-cache");

    private static string NewTempDir()
    {
        var dir = Path.Combine(TempRoot(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// 建一个指定大小的文件(用 <see cref="FileStream.SetLength"/> 造稀疏文件,避免真的写几 MB),
    /// 需要时把最后写入时间拨到过去。
    /// </summary>
    private static string NewFile(string root, string relative, long size, DateTime? lastWriteUtc = null)
    {
        var full = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        var parent = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        using (var fs = File.Create(full))
        {
            fs.SetLength(size);
        }

        if (lastWriteUtc is { } stamp)
        {
            File.SetLastWriteTimeUtc(full, stamp);
        }

        return full;
    }

    /// <summary>
    /// 建目录符号链接。权限不足时返回 false(由调用方降级跳过断言)。
    /// </summary>
    private static bool TryCreateDirectoryLink(string linkPath, string targetPath)
    {
        try
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException
                                       or PlatformNotSupportedException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// 只删链接本身:<c>Directory.Delete</c> 对重解析点不会递归进目标(否则清理测试会误伤 outside)。
    /// </summary>
    private static void TryDeleteLink(string linkPath)
    {
        try
        {
            if (Directory.Exists(linkPath))
            {
                Directory.Delete(linkPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 清理失败不影响断言结论
        }
    }

    /// <summary>
    /// 递归删掉临时目录并吞掉清理异常:临时目录残留不该让测试结果变红。
    /// </summary>
    private static void SafeDeleteTree(string dir)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (!Directory.Exists(dir))
                {
                    return;
                }

                Directory.Delete(dir, recursive: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 杀毒/索引服务可能短暂占用刚删过的文件:退避重试
                Thread.Sleep(50 * (attempt + 1));
            }
        }
    }
}
