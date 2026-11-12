using McKuro.Core.Infrastructure;
using McKuro.Core.Services;
using Microsoft.Extensions.Logging;

namespace McKuro.Tests;

/// <summary>
/// 软件日志占用统计与清理测试。
///
/// <para>
/// 全部用例都锁定一个固定的 <see cref="Now"/>：判定"历史"用的是文件名里的 <c>yyyyMMdd</c>，
/// 用真实的 <c>DateTime.Now</c> 会让用例在跨零点时随机失败（前一秒写的"今天"文件，下一秒变历史）。
/// </para>
/// </summary>
public class LogMaintenanceTests
{
    /// <summary>固定"当前时间"：所有用例都相对它构造文件名。</summary>
    private static readonly DateTime Now = new(2025, 3, 15, 10, 30, 0, DateTimeKind.Local);

    /// <summary>今天（与 <see cref="Now"/> 同日）的日期段。</summary>
    private const string Today = "20250315";

    /// <summary>昨天的日期段（历史）。</summary>
    private const string Yesterday = "20250314";

    /// <summary>更早的日期段（历史）。</summary>
    private const string LongAgo = "20240601";

    private static string NewTempDir() =>
        Path.Combine(Path.GetTempPath(), "mckuro-logmaint-" + Guid.NewGuid().ToString("N"));

    /// <summary>按 FileLoggerProvider 的布局建一个日志文件：<c>&lt;dir&gt;/&lt;category&gt;/McKuro-&lt;day&gt;.log</c>。</summary>
    private static string WriteLog(string dir, string category, string day, int contentBytes = 0, string prefix = "McKuro-")
    {
        var categoryDir = Path.Combine(dir, category);
        Directory.CreateDirectory(categoryDir);
        var path = Path.Combine(categoryDir, $"{prefix}{day}.log");
        File.WriteAllBytes(path, new byte[contentBytes]);
        return path;
    }

    /// <summary>在目录里放一个不属于日志的文件（崩溃转储 / 用户自建说明），用于验证"不碰非 .log"。</summary>
    private static string WriteNonLog(string dir, string category, string fileName, int contentBytes = 4)
    {
        var categoryDir = Path.Combine(dir, category);
        Directory.CreateDirectory(categoryDir);
        var path = Path.Combine(categoryDir, fileName);
        File.WriteAllBytes(path, new byte[contentBytes]);
        return path;
    }

    // ---------------------------------------------------------------- Measure

    [Fact]
    public void Measure_Returns_Empty_When_Directory_Missing()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mckuro-logmaint-missing-" + Guid.NewGuid().ToString("N"));
        Assert.False(Directory.Exists(dir));

        var footprint = LogMaintenance.Measure(dir, Now);

        Assert.Equal(0, footprint.TotalBytes);
        Assert.Equal(0, footprint.FileCount);
        Assert.Equal(0, footprint.OldFileCount);
        Assert.True(footprint.IsEmpty);
        Assert.False(footprint.HasOld);
    }

    [Fact]
    public void Measure_Returns_Empty_When_Directory_Empty()
    {
        var dir = NewTempDir();
        try
        {
            Directory.CreateDirectory(dir);

            var footprint = LogMaintenance.Measure(dir, Now);

            Assert.True(footprint.IsEmpty);
            Assert.Equal(0, footprint.FileCount);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void Measure_Sums_Across_Category_Directories()
    {
        var dir = NewTempDir();
        try
        {
            // 三个 category 子目录：今天 + 两个历史
            WriteLog(dir, "SmsLogin", Today, 100);
            WriteLog(dir, "SmsLogin", Yesterday, 200);
            WriteLog(dir, "GeetVerifyService", LongAgo, 300);
            WriteLog(dir, "GameUpdater", Yesterday, 400);
            WriteLog(dir, "GameUpdater", Today, 500);

            var footprint = LogMaintenance.Measure(dir, Now);

            Assert.Equal(1500, footprint.TotalBytes);
            Assert.Equal(5, footprint.FileCount);
            Assert.Equal(3, footprint.OldFileCount);
            Assert.False(footprint.IsEmpty);
            Assert.True(footprint.HasOld);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void Measure_Ignores_Non_Log_Files()
    {
        var dir = NewTempDir();
        try
        {
            WriteLog(dir, "SmsLogin", Yesterday, 100);
            // 这些都不是日志：崩溃转储、写盘中间态、用户自建文件
            WriteNonLog(dir, "SmsLogin", "McKuro-20250314.log.tmp", 1000);
            WriteNonLog(dir, "SmsLogin", "crash.dmp", 1000);
            WriteNonLog(dir, "SmsLogin", "崩溃日志说明.txt", 1000);

            var footprint = LogMaintenance.Measure(dir, Now);

            // 只有那一个 .log 被算进来
            Assert.Equal(100, footprint.TotalBytes);
            Assert.Equal(1, footprint.FileCount);
            Assert.Equal(1, footprint.OldFileCount);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void Measure_Counts_Today_Only_Footprint_As_NotEmpty_But_Without_Old()
    {
        var dir = NewTempDir();
        try
        {
            WriteLog(dir, "SmsLogin", Today, 700);

            var footprint = LogMaintenance.Measure(dir, Now);

            // 有内容（IsEmpty=false），但没有可清理的历史（HasOld=false）→ 界面该把"清理历史"按钮置灰
            Assert.False(footprint.IsEmpty);
            Assert.False(footprint.HasOld);
            Assert.Equal(1, footprint.FileCount);
            Assert.Equal(0, footprint.OldFileCount);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    // ------------------------------------------------------------ Clean: OldOnly

    [Fact]
    public void OldOnly_Keeps_Today_And_Removes_History()
    {
        var dir = NewTempDir();
        try
        {
            var todayPath = WriteLog(dir, "SmsLogin", Today, 100);
            var oldPath1 = WriteLog(dir, "SmsLogin", Yesterday, 200);
            var oldPath2 = WriteLog(dir, "GameUpdater", LongAgo, 300);

            var result = LogMaintenance.Clean(dir, Now, LogCleanScope.OldOnly);

            Assert.True(File.Exists(todayPath));
            Assert.False(File.Exists(oldPath1));
            Assert.False(File.Exists(oldPath2));
            Assert.Equal(2, result.FilesRemoved);
            Assert.Equal(500, result.BytesFreed);
            Assert.True(result.RemovedAnything);

            // 目录保留（今天的文件还在里面）
            Assert.True(Directory.Exists(Path.Combine(dir, "SmsLogin")));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void OldOnly_On_Only_Today_File_Removes_Nothing()
    {
        var dir = NewTempDir();
        try
        {
            var todayPath = WriteLog(dir, "SmsLogin", Today, 1234);

            var result = LogMaintenance.Clean(dir, Now, LogCleanScope.OldOnly);

            Assert.True(File.Exists(todayPath));
            Assert.Equal(0, result.FilesRemoved);
            Assert.Equal(0, result.BytesFreed);
            Assert.False(result.RemovedAnything);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    // ---------------------------------------------------------------- Clean: All

    [Fact]
    public void All_Removes_Every_Log_Including_Today()
    {
        var dir = NewTempDir();
        try
        {
            var todayPath = WriteLog(dir, "SmsLogin", Today, 100);
            var oldPath = WriteLog(dir, "GameUpdater", Yesterday, 200);

            var result = LogMaintenance.Clean(dir, Now, LogCleanScope.All);

            Assert.False(File.Exists(todayPath));
            Assert.False(File.Exists(oldPath));
            Assert.Equal(2, result.FilesRemoved);
            Assert.Equal(300, result.BytesFreed);
            Assert.True(result.RemovedAnything);

            // 清完之后测量必须是空的
            Assert.True(LogMaintenance.Measure(dir, Now).IsEmpty);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void All_Leaves_Non_Log_Files_Untouched()
    {
        var dir = NewTempDir();
        try
        {
            WriteLog(dir, "SmsLogin", Yesterday, 100);
            var dump = WriteNonLog(dir, "SmsLogin", "crash.dmp", 10);
            var partial = WriteNonLog(dir, "SmsLogin", "McKuro-20250314.log.tmp", 10);

            var result = LogMaintenance.Clean(dir, Now, LogCleanScope.All);

            Assert.Equal(1, result.FilesRemoved);
            Assert.True(File.Exists(dump));
            Assert.True(File.Exists(partial));

            // 目录里有非日志残留 → 目录不能被回收
            Assert.True(Directory.Exists(Path.Combine(dir, "SmsLogin")));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void All_Removes_Empty_Category_Directories_But_Keeps_Root()
    {
        var dir = NewTempDir();
        try
        {
            WriteLog(dir, "SmsLogin", Yesterday, 100);
            WriteLog(dir, Path.Combine("Nested", "Deep", "GameUpdater"), Yesterday, 100);

            LogMaintenance.Clean(dir, Now, LogCleanScope.All);

            // 空的 category 子目录（含多层嵌套）自底向上全部回收
            Assert.False(Directory.Exists(Path.Combine(dir, "SmsLogin")));
            Assert.False(Directory.Exists(Path.Combine(dir, "Nested")));

            // 根日志目录本身必须保留：它是 FileLoggerProvider 的写入根
            Assert.True(Directory.Exists(dir));
            Assert.Empty(Directory.EnumerateFileSystemEntries(dir));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void Clean_Inflates_Nothing_When_Directory_Missing()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mckuro-logmaint-missing-" + Guid.NewGuid().ToString("N"));

        var result = LogMaintenance.Clean(dir, Now, LogCleanScope.All);

        Assert.Equal(0, result.FilesRemoved);
        Assert.Equal(0, result.BytesFreed);
        Assert.False(result.RemovedAnything);
        // 不存在的目录绝不能被"顺手创建"出来
        Assert.False(Directory.Exists(dir));
    }

    // ------------------------------------------------- 日期判定与"解析不出"的文件

    [Fact]
    public void OldOnly_Treats_Undatable_Names_As_History()
    {
        var dir = NewTempDir();
        try
        {
            // 名字没有日期 → 解析不出 → 保守视为历史，应被清掉
            var backup = WriteLog(dir, "SmsLogin", "backup", 10);
            // 名字像日期但日期非法（13 月 / 2 月 30 日）→ 同样视为历史
            var badMonth = WriteLog(dir, "SmsLogin", "20251320", 10);
            var badDay = WriteLog(dir, "SmsLogin", "20250230", 10);
            var todayPath = WriteLog(dir, "SmsLogin", Today, 10);

            var result = LogMaintenance.Clean(dir, Now, LogCleanScope.OldOnly);

            Assert.False(File.Exists(backup));
            Assert.False(File.Exists(badMonth));
            Assert.False(File.Exists(badDay));
            Assert.True(File.Exists(todayPath));
            Assert.Equal(3, result.FilesRemoved);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void Clean_Handles_Uppercase_Log_Extension()
    {
        var dir = NewTempDir();
        try
        {
            var categoryDir = Path.Combine(dir, "SmsLogin");
            Directory.CreateDirectory(categoryDir);
            var upper = Path.Combine(categoryDir, "McKuro-" + Yesterday + ".LOG");
            File.WriteAllBytes(upper, new byte[8]);

            var footprint = LogMaintenance.Measure(dir, Now);
            var result = LogMaintenance.Clean(dir, Now, LogCleanScope.OldOnly);

            Assert.Equal(1, footprint.FileCount);
            Assert.False(File.Exists(upper));
            Assert.Equal(1, result.FilesRemoved);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Theory]
    [InlineData("McKuro-20250315.log", true)]
    [InlineData("McKuro-20250314.log", false)]
    [InlineData("McKuro-20240601.log", false)]
    // 扩展名大小写不敏感
    [InlineData("McKuro-20250315.LOG", true)]
    [InlineData("McKuro-20250315.Log", true)]
    // 解析不出日期 → 一律 false（保守视为历史）
    [InlineData("McKuro-backup.log", false)]
    [InlineData("McKuro-2025031.log", false)]      // 少一位
    [InlineData("McKuro-202503151.log", false)]    // 多一位
    [InlineData("McKuro-20251320.log", false)]     // 13 月
    [InlineData("McKuro-20250230.log", false)]     // 2 月 30 日
    [InlineData("McKuro-00000101.log", false)]     // 1 年 1 月 1 日：合法日期但不是今天
    [InlineData("Other-20250315.log", false)]      // 前缀不对
    [InlineData("McKuro-20250315.log.tmp", false)] // 不是 .log 结尾
    [InlineData("", false)]
    public void IsCurrentDayFileName_Parses_Only_Wellformed_Names(string fileName, bool expected)
    {
        Assert.Equal(expected, LogMaintenance.IsCurrentDayFileName(fileName, Now));
    }

    [Fact]
    public void IsCurrentDayFileName_Ignores_Path_Segments()
    {
        var nested = Path.Combine("logs", "SmsLogin", "McKuro-20250315.log");

        Assert.True(LogMaintenance.IsCurrentDayFileName(nested, Now));
    }

    // --------------------------------------- 文件被独占打开（Windows 上删不掉）的行为

    /// <summary>
    /// All 路径的核心韧性：今天的日志正被 FileLoggerProvider 常驻句柄（或杀毒软件）占用时，
    /// 删除会抛 IOException。清理必须静默跳过、继续处理其余文件，<b>绝不把异常抛给界面</b>。
    /// </summary>
    [Fact]
    public void All_Does_Not_Throw_When_A_Log_Is_Exclusively_Open()
    {
        var dir = NewTempDir();
        try
        {
            var lockedToday = WriteLog(dir, "SmsLogin", Today, 100);
            var openable = WriteLog(dir, "SmsLogin", Yesterday, 200);

            // FileShare.None：模拟"被常驻写句柄占用"。Windows 上此时 File.Delete 抛 IOException；
            // Unix 上 unlink 仍能成功（.NET 的文件共享在 Unix 上是协作式的），所以断言分平台。
            using (new FileStream(lockedToday, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                // 不包 try/catch —— 这里抛异常就是用例失败
                var result = LogMaintenance.Clean(dir, Now, LogCleanScope.All);

                Assert.Equal(1, result.FilesRemoved);
                Assert.Equal(200, result.BytesFreed);
            }

            // 未占用的那个一定被删掉
            Assert.False(File.Exists(openable));

            if (OperatingSystem.IsWindows())
            {
                // 占用的文件保留，且不计入释放量（界面不能承诺没兑现的字节）
                Assert.True(File.Exists(lockedToday));
                // 目录里还有残留文件 → 目录也不能被回收
                Assert.True(Directory.Exists(Path.Combine(dir, "SmsLogin")));
            }
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    /// <summary>
    /// OldOnly 路径更进一步：今天那个文件<b>连删除都不尝试</b>。即使它被独占打开，
    /// 也只处理历史文件，全程无异常、无失败分支。
    /// </summary>
    [Fact]
    public void OldOnly_Skips_Locked_Today_File_Without_Touching_It()
    {
        var dir = NewTempDir();
        try
        {
            var lockedToday = WriteLog(dir, "SmsLogin", Today, 100);
            var old = WriteLog(dir, "SmsLogin", Yesterday, 200);

            using (new FileStream(lockedToday, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var result = LogMaintenance.Clean(dir, Now, LogCleanScope.OldOnly);

                Assert.Equal(1, result.FilesRemoved);
                Assert.Equal(200, result.BytesFreed);
            }

            Assert.True(File.Exists(lockedToday));
            Assert.False(File.Exists(old));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    /// <summary>空日志文件（0 字节）也应当被算作"删掉了一个文件"，否则界面会说"没有可清理的"。</summary>
    [Fact]
    public void RemovedAnything_Is_True_Even_When_Zero_Bytes_Freed()
    {
        var dir = NewTempDir();
        try
        {
            WriteLog(dir, "SmsLogin", Yesterday, 0);

            var result = LogMaintenance.Clean(dir, Now, LogCleanScope.OldOnly);

            Assert.Equal(1, result.FilesRemoved);
            Assert.Equal(0, result.BytesFreed);
            Assert.True(result.RemovedAnything);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    /// <summary>
    /// 端到端：真实 FileLoggerProvider 写的日志 + 手工补的历史文件，验证
    /// 「Provider 的目录布局 → Measure 统计 → OldOnly 只删历史 → 今天的仍可继续追加」这条链路。
    /// </summary>
    [Fact]
    public void Works_With_Real_FileLoggerProvider_Layout()
    {
        var dir = NewTempDir();
        try
        {
            using var provider = new FileLoggerProvider(dir);
            provider.CreateLogger("McKuro.SmsLogin").LogInformation("today line");
            provider.CreateLogger("McKuro.Services.GeetVerifyService").LogInformation("geet line");

            // Provider 真实产出的两个文件都算"今天"，没有历史
            var todayFootprint = LogMaintenance.Measure(dir, DateTime.Now);
            Assert.Equal(2, todayFootprint.FileCount);
            Assert.Equal(0, todayFootprint.OldFileCount);
            Assert.True(todayFootprint.TotalBytes > 0);

            // 补两份历史日志（模拟跑过一段时间后的旧文件）
            var todayTag = DateTime.Now.ToString("yyyyMMdd");
            WriteLog(dir, "SmsLogin", "20240601", 64);
            WriteLog(dir, "SmsLogin", "20240701", 64);

            var mixed = LogMaintenance.Measure(dir, DateTime.Now);
            Assert.Equal(4, mixed.FileCount);
            Assert.Equal(2, mixed.OldFileCount);

            // OldOnly 只清历史；今天的两个（含 Provider 正持有的那个）保留
            var result = LogMaintenance.Clean(dir, DateTime.Now, LogCleanScope.OldOnly);
            Assert.Equal(2, result.FilesRemoved);
            Assert.Equal(128, result.BytesFreed);

            var after = LogMaintenance.Measure(dir, DateTime.Now);
            Assert.Equal(2, after.FileCount);
            Assert.Equal(0, after.OldFileCount);
            Assert.False(File.Exists(Path.Combine(dir, "SmsLogin", "McKuro-20240601.log")));

            // 清理之后 Provider 仍能继续追加（历史目录被判空回收也不影响写入）
            provider.CreateLogger("McKuro.GameUpdater").LogInformation("after clean line");
            Assert.Equal(3, LogMaintenance.Measure(dir, DateTime.Now).FileCount);
            Assert.True(File.Exists(Path.Combine(dir, "GameUpdater", $"McKuro-{todayTag}.log")));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
