using McKuro.Core.Services.Game;
using Xunit;

namespace McKuro.Tests;

/// <summary>
/// 游戏目录下的启动器工作区路径解析(预下载暂存与补丁临时目录都必须落在游戏盘):
/// 之前预载包放在 %AppData%(系统盘),既吃系统盘空间又让安装变成跨卷复制。
/// </summary>
public class GamePathResolverStagingTests
{
    private const string Root = @"D:\games\wuthering";

    [Fact]
    public void DiffData_And_Predownload_Staging_Are_Under_GameRoot()
    {
        var paths = new GamePathResolver(() => Root);

        Assert.Equal(Path.Combine(Root, "DiffData"), paths.DiffDataDir);
        Assert.Equal(Path.Combine(Root, "DiffData", "3.7.0"), paths.PredownloadStagingDir("3.7.0"));
    }

    [Fact]
    public void InstallTempDir_Is_Under_DiffData()
    {
        var paths = new GamePathResolver(() => Root);

        Assert.Equal(Path.Combine(Root, "DiffData", "install_tmp"), paths.InstallTempDir);

        // 每次调用给出独立的一次性子目录,避免并发安装互相踩
        var a = paths.NewInstallTempDir();
        var b = paths.NewInstallTempDir();
        Assert.NotNull(a);
        Assert.NotNull(b);
        Assert.NotEqual(a, b);
        Assert.StartsWith(paths.InstallTempDir!, a!);
    }

    [Fact]
    public void Missing_GameRoot_Yields_Null_Instead_Of_Falling_Back()
    {
        // 未设置游戏目录时必须返回 null,让调用方报"未设置游戏目录";
        // 绝不能悄悄回退到数据目录(那会重新把几十 GB 写到系统盘)。
        var paths = new GamePathResolver(() => null);

        Assert.Null(paths.DiffDataDir);
        Assert.Null(paths.PredownloadStagingDir("3.7.0"));
        Assert.Null(paths.InstallTempDir);
        Assert.Null(paths.NewInstallTempDir());
    }

    [Fact]
    public void Blank_Version_Yields_Null()
    {
        var paths = new GamePathResolver(() => Root);

        Assert.Null(paths.PredownloadStagingDir(""));
        Assert.Null(paths.PredownloadStagingDir("   "));
    }
}
