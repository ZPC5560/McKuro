using McKuro.Core.Services.Game;
using Xunit;

namespace McKuro.Tests;

/// <summary>
/// 游戏资源占用分类扫描。
/// <para>
/// 回归背景:用户在「游戏资源」里看到的数字必须能指导他做决定 ——
/// 日志/截图可以删,正式资源删了游戏就跑不起来,预下载装完就自动消失。
/// 分类错一格,环形图就会把"可删的 169 MB 日志"画进"删了会坏档的 100 GB 资源"里,
/// 用户再也不敢动任何东西。这里把分类边界与"各段之和 = 总量"钉住。
/// </para>
/// </summary>
public class GameResourceScannerTests
{
    // ---------- 分类纯函数 ----------

    [Theory]
    [InlineData("Client/Content/Paks/pakchunk0.pak", GameResourceCategory.Official)]
    [InlineData("Client/Binaries/Win64/Client-Win64-Shipping.exe", GameResourceCategory.Official)]
    [InlineData("Wuthering Waves.exe", GameResourceCategory.Official)]
    [InlineData("Engine/Content/x.uasset", GameResourceCategory.Official)]
    public void Base_Game_Files_Are_Official(string path, GameResourceCategory expected)
        => Assert.Equal(expected, GameResourceScanner.Classify(path));

    [Theory]
    [InlineData(@"Client\Saved\Logs\Client.log")]          // 反斜杠也要认
    [InlineData("Client/Saved/Logs/Client-backup-1.log")]
    public void Logs_Are_Logs(string path)
        => Assert.Equal(GameResourceCategory.Logs, GameResourceScanner.Classify(path));

    [Theory]
    [InlineData("Client/Saved/ScreenShot/a.png")]
    [InlineData("Client/Saved/Screenshots/a.png")]          // 官方历史上两种写法都出现过
    public void Screenshots_Are_Screenshots(string path)
        => Assert.Equal(GameResourceCategory.Screenshots, GameResourceScanner.Classify(path));

    [Theory]
    [InlineData("DiffData/3.8.0/pakchunk0.pak")]
    [InlineData("DiffData/3.8.0_min/pakchunk0.pak")]        // 精简规格暂存目录
    public void DiffData_Is_Predownload(string path)
        => Assert.Equal(GameResourceCategory.Predownload, GameResourceScanner.Classify(path));

    [Theory]
    [InlineData("DiffData/install_tmp/p-abc/x.pak")]        // 必须抢在 DiffData 判据之前
    [InlineData(".McKuro_patch/tmp.bin")]
    [InlineData(".McKuro_backup/Client/Content/x.pak")]
    [InlineData("Client/Content/Paks/pakchunk0.pak.part")]  // 未完成的下载半成品
    public void Launcher_Work_Products_Are_Temp(string path)
        => Assert.Equal(GameResourceCategory.LauncherTemp, GameResourceScanner.Classify(path));

    [Fact]
    public void Empty_Input_Defaults_To_Official()
        => Assert.Equal(GameResourceCategory.Official, GameResourceScanner.Classify(null));

    [Fact]
    public void Similar_But_Not_Nested_Paths_Are_Not_Classified_By_Loose_Prefix()
    {
        // "DiffData2/..." 不是 DiffData 的子目录;按字符 StartsWith 会误判成预下载
        Assert.Equal(GameResourceCategory.Official, GameResourceScanner.Classify("DiffData2/x.pak"));
        // "Client/Saved/LogsArchive/x" 同理不该算日志
        Assert.Equal(GameResourceCategory.Official, GameResourceScanner.Classify("Client/Saved/LogsArchive/x.log"));
    }

    // ---------- 扫描 ----------

    private static string CreateTempRoot()
    {
        var dir = Path.Combine(Path.GetTempPath(), "McKuro-resource-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void NewFile(string root, string relative, long size)
    {
        var full = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        var parent = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        using var fs = File.Create(full);
        fs.SetLength(size);
    }

    [Fact]
    public void Scan_Buckets_Every_File_And_Sums_Exactly_To_Total()
    {
        var root = CreateTempRoot();
        try
        {
            NewFile(root, "Client/Content/Paks/a.pak", 1000);
            NewFile(root, "Client/Binaries/Win64/x.exe", 500);
            NewFile(root, "DiffData/3.8.0/a.pak", 700);
            NewFile(root, "DiffData/3.8.0/b.pak", 300);
            NewFile(root, "DiffData/3.7.0/a.pak", 200);
            NewFile(root, "Client/Saved/Logs/Client.log", 50);
            NewFile(root, "Client/Saved/ScreenShot/s.png", 20);
            NewFile(root, ".McKuro_backup/old.pak", 10);

            var usage = GameResourceScanner.Scan(root);

            Assert.True(usage.Scanned);
            Assert.Equal(1500, usage.OfficialBytes);
            Assert.Equal(1200, usage.PredownloadBytes);
            Assert.Equal(50, usage.LogBytes);
            Assert.Equal(20, usage.ScreenshotBytes);
            Assert.Equal(10, usage.LauncherTempBytes);
            Assert.Equal(8, usage.FileCount);

            // 各段之和必须严格等于总量:环形图比例自洽的前提
            var sum = usage.OfficialBytes + usage.PredownloadBytes + usage.LogBytes
                + usage.ScreenshotBytes + usage.LauncherTempBytes;
            Assert.Equal(usage.TotalBytes, sum);
            Assert.Equal(2780, usage.TotalBytes);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Scan_Lists_Predownload_Versions_Newest_First()
    {
        var root = CreateTempRoot();
        try
        {
            NewFile(root, "DiffData/3.7.0/a.pak", 100);
            NewFile(root, "DiffData/3.8.0/a.pak", 200);
            NewFile(root, "DiffData/3.8.0/b.pak", 300);

            var usage = GameResourceScanner.Scan(root);

            Assert.Equal(["3.8.0", "3.7.0"], usage.PredownloadDirs.Select(d => d.Name));
            Assert.Equal(500, usage.PredownloadDirs[0].Bytes);
            Assert.Equal(2, usage.PredownloadDirs[0].FileCount);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Scan_Missing_Directory_Reports_Not_Scanned_Instead_Of_Zero_Usage()
    {
        // Scanned=false 让 UI 能区分"目录不存在"与"目录是空的",文案完全不同
        Assert.False(GameResourceScanner.Scan(null).Scanned);
        Assert.False(GameResourceScanner.Scan("").Scanned);
        Assert.False(GameResourceScanner.Scan(Path.Combine(Path.GetTempPath(), "no-such-" + Guid.NewGuid().ToString("N"))).Scanned);
    }

    [Fact]
    public void Ratios_Are_Safe_On_Empty_Or_Zero_Denominator()
    {
        // 空目录时 TotalBytes=0:比例必须是 0 而不是 NaN(环形图不接受 NaN)
        var usage = GameResourceScanner.Scan(CreateTempRoot());
        Assert.Equal(0, usage.OfficialRatio);
        Assert.Equal(0, usage.DiskRatio);

        var empty = GameResourceUsage.Empty;
        Assert.Equal(0, empty.TotalBytes);
        Assert.Equal(0, empty.OfficialRatio);
        Assert.False(empty.HasDiskInfo);
    }

    [Fact]
    public void Ratio_Clamps_To_Unit_Interval()
    {
        Assert.Equal(0.5, GameResourceUsage.Ratio(50, 100));
        Assert.Equal(0, GameResourceUsage.Ratio(-1, 100));
        Assert.Equal(0, GameResourceUsage.Ratio(50, 0));
    }

    [Fact]
    public void Progress_Callback_Is_Invoked_And_Cancellation_Throws()
    {
        var root = CreateTempRoot();
        try
        {
            NewFile(root, "Client/Content/Paks/a.pak", 100);
            var reports = new List<GameResourceScanProgress>();
            GameResourceScanner.Scan(root, default, new Progress<GameResourceScanProgress>(reports.Add));

            using var cts = new CancellationTokenSource();
            cts.Cancel();
            Assert.Throws<OperationCanceledException>(() => GameResourceScanner.Scan(root, cts.Token));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
