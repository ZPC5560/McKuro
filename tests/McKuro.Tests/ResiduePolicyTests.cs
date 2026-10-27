using McKuro.Core.Services.Game;

namespace McKuro.Tests;

/// <summary>
/// 「是否弹窗询问删除重复文件」的策略测试。
/// <para>
/// 阈值不能设成"只要比清单大就提示":游戏目录必然包含运行时存档、日志、以及其它画质资源包
/// (它们本来就不在当前清单里),那样会天天弹窗。用例把两个触发条件与"都不满足则不打扰"钉住。
/// </para>
/// </summary>
public class ResiduePolicyTests
{
    private const long Mb = 1024L * 1024;

    private static ResidueScanResult Make(
        long reclaimBytes,
        long actualBytes,
        long manifestBytes,
        int itemCount = 1)
    {
        var report = new ResidueReport();
        if (itemCount > 0)
        {
            report.Items.Add(new ResidueFile
            {
                FullPath = @"C:\game\.McKuro_backup",
                RelativePath = ".McKuro_backup",
                Size = reclaimBytes,
                Kind = ResidueKind.Backup,
                IsDirectory = true,
            });
        }
        return new ResidueScanResult
        {
            Report = report,
            ActualBytes = actualBytes,
            ManifestBytes = manifestBytes,
        };
    }

    // ---------- 条件一:残留本身够大 ----------

    [Fact]
    public void LargeResidue_Prompts_EvenIfFolderSizeIsWithinManifest()
    {
        // 目录 100GB / 清单 100GB(看起来"没有超"),但里面有 500MB 备份 → 仍值得提示
        var result = Make(reclaimBytes: 500 * Mb, actualBytes: 100_000 * Mb, manifestBytes: 100_000 * Mb);
        Assert.True(ResiduePolicy.ShouldPrompt(result));
    }

    [Fact]
    public void SmallResidue_WithoutExcess_DoesNotPrompt()
    {
        var result = Make(reclaimBytes: 10 * Mb, actualBytes: 100_000 * Mb, manifestBytes: 100_000 * Mb);
        Assert.False(ResiduePolicy.ShouldPrompt(result));
    }

    [Fact]
    public void ThresholdBoundary_IsInclusive()
    {
        Assert.True(ResiduePolicy.ShouldPrompt(Make(
            ResiduePolicy.MinPromptBytes, 100_000 * Mb, 100_000 * Mb)));
        Assert.False(ResiduePolicy.ShouldPrompt(Make(
            ResiduePolicy.MinPromptBytes - 1, 100_000 * Mb, 100_000 * Mb)));
    }

    // ---------- 条件二:目录明显超出清单 ----------

    [Fact]
    public void ClearExcessOverManifest_Prompts_EvenWithSmallResidue()
    {
        // 目录比清单大 40%(远超 25% 容忍):说明还有不在已知残留里的重复文件
        var result = Make(reclaimBytes: 5 * Mb, actualBytes: 140_000 * Mb, manifestBytes: 100_000 * Mb);
        Assert.True(ResiduePolicy.ShouldPrompt(result));
    }

    [Fact]
    public void SlightExcessWithinTolerance_DoesNotPrompt()
    {
        // 大 10%:存档/日志的正常量级,不该打扰
        var result = Make(reclaimBytes: 5 * Mb, actualBytes: 110_000 * Mb, manifestBytes: 100_000 * Mb);
        Assert.False(ResiduePolicy.ShouldPrompt(result));
    }

    // ---------- 边界与空值 ----------

    [Fact]
    public void NoResidue_NeverPrompts()
    {
        Assert.False(ResiduePolicy.ShouldPrompt(Make(0, 200_000 * Mb, 100_000 * Mb, itemCount: 0)));
    }

    [Fact]
    public void NullResult_DoesNotPrompt()
    {
        Assert.False(ResiduePolicy.ShouldPrompt(null));
    }

    [Fact]
    public void UnknownManifest_DoesNotTriggerExcessBranch()
    {
        // 清单拿不到(ManifestBytes = 0)时只有"残留够大"一条路径能触发
        Assert.False(ResiduePolicy.ShouldPrompt(Make(10 * Mb, 500_000 * Mb, 0)));
        Assert.True(ResiduePolicy.ShouldPrompt(Make(500 * Mb, 500_000 * Mb, 0)));
    }

    [Fact]
    public void FolderSmallerThanManifest_DoesNotProduceNegativeExcess()
    {
        var result = Make(reclaimBytes: 5 * Mb, actualBytes: 50_000 * Mb, manifestBytes: 100_000 * Mb);
        Assert.Equal(0, result.ExcessRatio);
        Assert.False(ResiduePolicy.ShouldPrompt(result));
    }

    // ---------- 结果换算 ----------

    [Fact]
    public void EffectiveBytes_SubtractsReclaimable()
    {
        var result = Make(reclaimBytes: 300 * Mb, actualBytes: 100_000 * Mb, manifestBytes: 99_800 * Mb);
        Assert.Equal(99_700 * Mb, result.EffectiveBytes);
    }

    [Fact]
    public void ExcessRatio_IgnoresReclaimableWhenComparing()
    {
        // 扣掉残留后正好等于清单 → 比例 0
        var result = Make(reclaimBytes: 200 * Mb, actualBytes: 100_200 * Mb, manifestBytes: 100_000 * Mb);
        Assert.Equal(0, result.ExcessRatio, 6);
    }

    // ---------- 摘要 ----------

    [Fact]
    public void Summarize_GroupsByKind()
    {
        var report = new ResidueReport();
        report.Items.Add(new ResidueFile { FullPath = "a", RelativePath = "a", Size = 10, Kind = ResidueKind.Backup });
        report.Items.Add(new ResidueFile { FullPath = "b", RelativePath = "b", Size = 20, Kind = ResidueKind.Backup });
        report.Items.Add(new ResidueFile { FullPath = "c", RelativePath = "c", Size = 30, Kind = ResidueKind.PatchTemp });

        var summary = ResiduePolicy.Summarize(report);

        Assert.Equal(2, summary.Count);
        Assert.Contains(summary, s => s.Kind == ResidueKind.Backup && s.Count == 2 && s.Bytes == 30);
        Assert.Contains(summary, s => s.Kind == ResidueKind.PatchTemp && s.Count == 1 && s.Bytes == 30);
    }

    [Fact]
    public void Summarize_EmptyReport_ReturnsEmpty()
    {
        Assert.Empty(ResiduePolicy.Summarize(ResidueReport.Empty));
        Assert.Empty(ResiduePolicy.Summarize(null));
    }

    // ---------- 空单例不可变约定 ----------

    [Fact]
    public void EmptyReport_IsReusableAndHasNoItems()
    {
        Assert.False(ResidueReport.Empty.HasResidue);
        Assert.Equal(0, ResidueReport.Empty.ReclaimBytes);
        Assert.Same(ResidueScanResult.None, ResidueScanResult.None);
        Assert.False(ResidueScanResult.None.Report.HasResidue);
    }
}
