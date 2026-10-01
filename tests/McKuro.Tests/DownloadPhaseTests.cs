using McKuro.Core.Services.Game;

namespace McKuro.Tests;

/// <summary>
/// 进度阶段判定与「暂停按钮可见性」契约的回归测试。
///
/// 背景(真实缺陷):安装/修复把 IsDownloading 也置真,而暂停按钮只看 IsDownloading,
/// 于是在「合成分组差分 (31/38)…」(hpatchz 外部进程)与「正在校验本地文件」(纯读盘)
/// 这两个**非下载**阶段照样渲染出可点的「暂停下载」。此时暂停门拦不到任何字节读取,
/// 点击是空操作;更糟的是遗留的暂停态会在后续进入下载批次时静默卡死。
///
/// 约定:只有 DownloadPhase.Download 阶段 CanPause 为真(对齐上游 IProgressSetup.CanPause:
/// InstallKrdiffGroupResource/InstallKrdiffResource/MoveFileResource 均为 false)。
/// </summary>
public class DownloadPhaseTests
{
    private static DownloadProgress Progress(
        string? stageText = null,
        int fileIndex = 0,
        int fileTotal = 0,
        long bytesDownloaded = 0,
        long bytesTotal = 0,
        bool canPause = false) => new()
        {
            CurrentFile = "some/file.pak",
            FileIndex = fileIndex,
            FileTotal = fileTotal,
            BytesDownloaded = bytesDownloaded,
            BytesTotal = bytesTotal,
            SpeedBps = 0,
            StageText = stageText,
            CanPause = canPause,
        };

    [Fact]
    public void Stage_Text_Classified_As_Stage_And_Not_Pausable()
    {
        // 截图 1:「正在合成分组差分 (31/38)…」——hpatchz 外部进程,没有可暂停的字节读取
        var p = Progress(stageText: "正在合成分组差分 (31/38)…");

        Assert.Equal(DownloadPhase.Stage, p.Phase);
        Assert.False(p.CanPause, "差分合成阶段不允许暂停(对齐上游 CanPause=false)");
    }

    [Fact]
    public void Verify_Phase_Classified_By_File_Count_Without_Bytes()
    {
        // 截图 2:校验阶段上报 FileIndex/FileTotal,BytesTotal 为 0
        var p = Progress(fileIndex: 912, fileTotal: 915);

        Assert.Equal(DownloadPhase.Verify, p.Phase);
        Assert.False(p.CanPause, "校验阶段不允许暂停(纯读盘,无字节读取可挂起)");
    }

    [Fact]
    public void Verify_Phase_Does_Not_Report_Zero_Byte_Download()
    {
        // 校验阶段若被当成下载阶段,会显示误导性的 "912/915 文件 · 0 B/0 B"(截图 2 的文案)
        var p = Progress(fileIndex: 912, fileTotal: 915);

        Assert.NotEqual(DownloadPhase.Download, p.Phase);
        // 同时确认真实下载阶段的文案字段不是 0 值,证明两者确实可区分
        var real = Progress(fileIndex: 1, fileTotal: 915, bytesDownloaded: 1024, bytesTotal: 4096);
        Assert.Equal(1024, real.BytesDownloaded);
        Assert.Equal(0, p.BytesDownloaded);
    }

    [Fact]
    public void Real_Download_Phase_Is_Pausable()
    {
        var p = Progress(
            fileIndex: 3,
            fileTotal: 10,
            bytesDownloaded: 50,
            bytesTotal: 100,
            canPause: true);

        Assert.Equal(DownloadPhase.Download, p.Phase);
        Assert.True(p.CanPause, "只有真正的下载阶段才允许暂停");
    }

    [Fact]
    public void Stage_Text_Takes_Precedence_Over_File_Count()
    {
        // 阶段文案存在时优先判定为 Stage(即使同时带了文件计数,也不该当成下载/校验)
        var p = Progress(stageText: "正在安装资源文件 (12 个)…", fileIndex: 1, fileTotal: 12);

        Assert.Equal(DownloadPhase.Stage, p.Phase);
    }

    [Fact]
    public void Progress_Without_Any_Phase_Hint_Is_Treated_As_Download()
    {
        // 迁移等阶段会同时带 StageText;没有 StageText 也没有文件数时按下载处理(保守,由 CanPause 兜底)
        var p = Progress();

        Assert.Equal(DownloadPhase.Download, p.Phase);
        Assert.False(p.CanPause, "CanPause 默认 false,调用方必须显式声明可暂停");
    }

    [Fact]
    public async Task Download_Engine_Clears_Pause_When_Batch_Finishes()
    {
        // 批次结束后必须清掉暂停门:残留的暂停态会污染后续阶段
        // (下一批次的文件一进读取循环就在 WaitAsync 上静默阻塞,而那时按钮可能已不可见)。
        var engine = new DownloadEngine(new HttpClient());
        engine.Pause();
        Assert.True(engine.IsPaused);

        // 空批次:不下载任何文件,但收尾必须复位暂停门
        var (success, failures) = await engine.DownloadManyAsync([], "", Path.GetTempPath());

        Assert.Equal(0, success);
        Assert.Empty(failures);
        Assert.False(engine.IsPaused, "下载批次结束后应清除暂停门,避免污染后续下载阶段");
    }
}
