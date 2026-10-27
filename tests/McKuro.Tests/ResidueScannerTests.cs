using McKuro.Core.Services.Game;

namespace McKuro.Tests;

/// <summary>
/// 「安装后重复/残留文件」扫描与清理策略的测试(用户需求 2026-10:
/// 多次断连下载后出现重复文件,安装完成后比对体积,明显过大时后台校验并弹窗询问删除)。
/// <para>
/// 这套用例守的是<b>误删</b>这条红线:扫描必须只认「启动器自己产生的工作产物」,
/// 任何「清单外的文件」都不许进报告 —— 其它画质资源包、存档、日志本来就不在清单里,
/// 按"清单外即垃圾"清扫会直接毁掉用户的游戏。
/// </para>
/// </summary>
public class ResidueScannerTests : IDisposable
{
    private readonly string _root;

    public ResidueScannerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "McKuro_residue_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception)
        {
            // 忽略
        }
    }

    /// <summary>造一个指定大小的文件(内容无关,只算长度)。</summary>
    private string WriteFile(string relative, long size)
    {
        var path = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        using var fs = File.Create(path);
        fs.SetLength(size);
        return path;
    }

    // ---------- ① 替换前备份 ----------

    [Fact]
    public void BackupDirectory_IsReported_AsReclaimable()
    {
        WriteFile($"{UpdateInstaller.BackupDirName}/Client/Binaries/Win64/a.exe", 40L * 1024 * 1024);
        WriteFile($"{UpdateInstaller.BackupDirName}/Client/Content/b.pak", 100L * 1024 * 1024);

        var report = ResidueScanner.Scan(_root);

        var backup = Assert.Single(report.Items, i => i.Kind == ResidueKind.Backup);
        Assert.True(backup.IsDirectory);
        Assert.Equal(140L * 1024 * 1024, backup.Size);
        Assert.Equal(140L * 1024 * 1024, report.ReclaimBytes);
    }

    [Fact]
    public void SmallBackupDirectory_IsIgnoredAsNoise()
    {
        // 几 MB 的备份不值得弹窗打断用户
        WriteFile($"{UpdateInstaller.BackupDirName}/tiny.txt", 4 * 1024);
        Assert.False(ResidueScanner.Scan(_root).HasResidue);
    }

    [Fact]
    public void EmptyBackupDirectory_ProducesNothing()
    {
        Directory.CreateDirectory(Path.Combine(_root, UpdateInstaller.BackupDirName));
        Assert.False(ResidueScanner.Scan(_root).HasResidue);
    }

    // ---------- ② 补丁 / 差分临时目录 ----------

    [Fact]
    public void PatchTempAndDiffTemp_AreReported()
    {
        WriteFile($"DiffData/{GamePathResolver.InstallTempDirName}/abc/patch.krdiff", 60L * 1024 * 1024);
        WriteFile($"{GamePathResolver.PatchTempDirName}/guid/out.pak", 80L * 1024 * 1024);

        var report = ResidueScanner.Scan(_root);

        Assert.Single(report.Items, i => i.Kind == ResidueKind.PatchTemp);
        Assert.Single(report.Items, i => i.Kind == ResidueKind.DiffTemp);
        Assert.Equal(140L * 1024 * 1024, report.ReclaimBytes);
    }

    // ---------- ③ 孤儿 .part ----------

    [Fact]
    public void PartWithOfficialFilePresent_IsOrphan()
    {
        // 正式文件已落位 → .part 是断连残留,可删
        WriteFile("Client/Content/big.pak", 200L * 1024 * 1024);
        WriteFile("Client/Content/big.pak.part", 10L * 1024 * 1024);

        var report = ResidueScanner.Scan(_root);

        var part = Assert.Single(report.Items, i => i.Kind == ResidueKind.PartialDownload);
        Assert.Equal(10L * 1024 * 1024, part.Size);
        Assert.False(part.IsDirectory);
    }

    [Fact]
    public void PartWithoutOfficialFile_IsKept_ForResume()
    {
        // 正在下载/可续传的现场:正式文件还没有,删了就得重下几十 GB
        WriteFile("Client/Content/big.pak.part", 500L * 1024 * 1024);

        Assert.False(ResidueScanner.Scan(_root).HasResidue);
    }

    [Fact]
    public void OrphanPartOfRemovedFile_IsReported_WhenNotClaimed()
    {
        // 该路径已不在服务端清单里(旧版本文件被移除),正式文件也不存在 → 孤儿
        WriteFile("Client/Content/old.pak.part", 20L * 1024 * 1024);
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Client/Content/new.pak" };

        var report = ResidueScanner.Scan(_root, claimedRelativePaths: claimed);

        Assert.Single(report.Items, i => i.Kind == ResidueKind.PartialDownload);
    }

    [Fact]
    public void OrphanPartStillClaimedByManifest_IsKept()
    {
        // 清单里还有这个路径 → 说明是待续传的,不能删
        WriteFile("Client/Content/big.pak.part", 20L * 1024 * 1024);
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Client/Content/big.pak" };

        Assert.False(ResidueScanner.Scan(_root, claimedRelativePaths: claimed).HasResidue);
    }

    // ---------- ④ 误删红线 ----------

    [Fact]
    public void OtherQualityPackages_AreNeverReported()
    {
        // 其它画质资源包/存档/日志都不在本次清单里,但绝不能进报告
        WriteFile("Client/Content/Paks/Extra/uhd.pak", 900L * 1024 * 1024);
        WriteFile("Client/Saved/SaveGames/user.sav", 30L * 1024 * 1024);
        WriteFile("Client/Saved/Logs/Client.log", 50L * 1024 * 1024);
        WriteFile("Wuthering Waves.exe", 20L * 1024 * 1024);

        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Wuthering Waves.exe" };
        var report = ResidueScanner.Scan(_root, claimedRelativePaths: claimed);

        Assert.False(report.HasResidue);
    }

    [Fact]
    public void InUseStagingDirectory_IsSkipped()
    {
        // 带标记的预下载暂存目录(curl 续传要用):即便被识别成临时目录也不许列出来
        var staging = Path.Combine(_root, "DiffData", "3.0.0", "patch.krdiff");
        WriteFile($"DiffData/3.0.0/patch.krdiff", 300L * 1024 * 1024);

        var inUse = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.Combine(_root, "DiffData", "3.0.0"),
        };
        var report = ResidueScanner.Scan(_root, inUsePaths: inUse);

        Assert.False(report.HasResidue);
    }

    [Fact]
    public void NonexistentRoot_ReturnsEmpty_WithoutThrowing()
    {
        Assert.False(ResidueScanner.Scan(Path.Combine(_root, "nope")).HasResidue);
        Assert.False(ResidueScanner.Scan(null).HasResidue);
        Assert.False(ResidueScanner.Scan("").HasResidue);
    }

    // ---------- ⑤ 删除 ----------

    [Fact]
    public void Delete_RemovesReportedItems_AndReportsFreedBytes()
    {
        WriteFile($"{UpdateInstaller.BackupDirName}/a.pak", 70L * 1024 * 1024);
        WriteFile("Client/Content/x.pak", 100L * 1024 * 1024);
        WriteFile("Client/Content/x.pak.part", 5L * 1024 * 1024);

        var report = ResidueScanner.Scan(_root);
        var (freed, failures) = ResidueScanner.Delete(report);

        Assert.Equal(75L * 1024 * 1024, freed);
        Assert.Equal(0, failures);
        // 备份目录已删、孤儿 .part 已删、正式文件保留
        Assert.False(Directory.Exists(Path.Combine(_root, UpdateInstaller.BackupDirName)));
        Assert.False(File.Exists(Path.Combine(_root, "Client", "Content", "x.pak.part")));
        Assert.True(File.Exists(Path.Combine(_root, "Client", "Content", "x.pak")));
    }

    [Fact]
    public void Delete_OnlyTouchesReportedPaths()
    {
        // 报告一旦固定,删除就不得再"顺手扫一遍":其它文件必须原样保留
        WriteFile($"{UpdateInstaller.BackupDirName}/a.pak", 70L * 1024 * 1024);
        WriteFile("Client/Content/Paks/Extra/uhd.pak", 900L * 1024 * 1024);

        var report = ResidueScanner.Scan(_root);
        ResidueScanner.Delete(report);

        Assert.True(File.Exists(Path.Combine(_root, "Client", "Content", "Paks", "Extra", "uhd.pak")));
    }

    [Fact]
    public void Delete_OfEmptyReport_IsNoOp()
    {
        var (freed, failures) = ResidueScanner.Delete(ResidueReport.Empty);
        Assert.Equal(0, freed);
        Assert.Equal(0, failures);
    }

    [Fact]
    public void Delete_MissingItemIsSkipped_NotCountedAsFailure()
    {
        WriteFile($"{UpdateInstaller.BackupDirName}/a.pak", 70L * 1024 * 1024);
        var report = ResidueScanner.Scan(_root);
        // 扫描后被别处删掉(或上一轮已删) → 不算失败
        Directory.Delete(Path.Combine(_root, UpdateInstaller.BackupDirName), recursive: true);

        var (freed, failures) = ResidueScanner.Delete(report);
        Assert.Equal(0, freed);
        Assert.Equal(0, failures);
    }

    // ---------- ⑥ 体积统计 ----------

    [Fact]
    public void MeasureDirectoryBytes_SumsAllFiles()
    {
        WriteFile("a.pak", 10L * 1024 * 1024);
        WriteFile("Client/b.pak", 20L * 1024 * 1024);
        Assert.Equal(30L * 1024 * 1024, ResidueScanner.MeasureDirectoryBytes(_root));
    }

    [Fact]
    public void MeasureDirectoryBytes_MissingDir_ReturnsZero()
    {
        Assert.Equal(0, ResidueScanner.MeasureDirectoryBytes(Path.Combine(_root, "nope")));
        Assert.Equal(0, ResidueScanner.MeasureDirectoryBytes(null));
    }

    // ---------- ⑦ 报告聚合 ----------

    [Fact]
    public void Report_CountsByKind()
    {
        WriteFile($"{UpdateInstaller.BackupDirName}/a.pak", 70L * 1024 * 1024);
        WriteFile("Client/x.pak", 10L * 1024 * 1024);
        WriteFile("Client/x.pak.part", 5L * 1024 * 1024);

        var report = ResidueScanner.Scan(_root);
        Assert.Equal(1, report.CountOf(ResidueKind.Backup));
        Assert.Equal(1, report.CountOf(ResidueKind.PartialDownload));
        Assert.Equal(0, report.CountOf(ResidueKind.PatchTemp));
    }

    [Fact]
    public void RelativePath_UsesForwardSlashes()
    {
        WriteFile($"{UpdateInstaller.BackupDirName}/Client/a.pak", 70L * 1024 * 1024);
        var item = Assert.Single(ResidueScanner.Scan(_root).Items);
        Assert.DoesNotContain('\\', item.RelativePath);
    }
}
