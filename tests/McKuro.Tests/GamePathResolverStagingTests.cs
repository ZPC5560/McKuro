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

    /// <summary>
    /// H3:版本号来自服务端,暂存目录之后可能被递归删除。".." 会让暂存目录变成游戏根目录,
    /// 绝对路径会删到任意位置 —— 只接受纯数字点分版本号。
    /// </summary>
    [Theory]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("../3.7.0")]
    [InlineData("3.7.0/..")]
    [InlineData(@"C:\Windows")]
    [InlineData("/etc")]
    [InlineData("3.7.0 ")]
    [InlineData("3..7")]
    [InlineData("v3.7.0")]
    [InlineData("1.2.3.4.5")]
    [InlineData("-1.0")]
    public void Unsafe_Version_Yields_Null(string version)
    {
        var paths = new GamePathResolver(() => Root);

        Assert.False(GamePathResolver.IsSafeVersionSegment(version));
        Assert.Null(paths.PredownloadStagingDir(version));
    }

    [Theory]
    [InlineData("3")]
    [InlineData("3.7")]
    [InlineData("3.7.0")]
    [InlineData("3.7.0.12345")]
    public void Numeric_Version_Is_Accepted(string version)
    {
        Assert.True(GamePathResolver.IsSafeVersionSegment(version));
        Assert.Equal(
            Path.Combine(Root, "DiffData", version),
            new GamePathResolver(() => Root).PredownloadStagingDir(version));
    }

    /// <summary>递归删除前的断言:只有 DiffData 的<b>直接</b>子目录才算数。</summary>
    [Fact]
    public void IsDirectChildOf_Only_Accepts_Direct_Children()
    {
        var diffData = Path.Combine(Root, "DiffData");

        Assert.True(GamePathResolver.IsDirectChildOf(diffData, Path.Combine(diffData, "3.7.0")));
        Assert.True(GamePathResolver.IsDirectChildOf(diffData + Path.DirectorySeparatorChar, Path.Combine(diffData, "3.7.0")));
        // 自身、上级、更深的后代、同前缀兄弟目录、无关路径都不行
        Assert.False(GamePathResolver.IsDirectChildOf(diffData, diffData));
        Assert.False(GamePathResolver.IsDirectChildOf(diffData, Root));
        Assert.False(GamePathResolver.IsDirectChildOf(diffData, Path.Combine(diffData, "..")));
        Assert.False(GamePathResolver.IsDirectChildOf(diffData, Path.Combine(diffData, "3.7.0", "sub")));
        Assert.False(GamePathResolver.IsDirectChildOf(diffData, Path.Combine(Root, "DiffDataX", "3.7.0")));
        Assert.False(GamePathResolver.IsDirectChildOf(diffData, null));
        Assert.False(GamePathResolver.IsDirectChildOf(null, Path.Combine(diffData, "3.7.0")));
    }

    /// <summary>
    /// M1:官方补丁临时目录按补丁固定命名 —— 同一补丁每次得到同一目录(才能续传),
    /// 不同补丁/不同源版本得到不同目录;目录名只含前缀 + 十六进制。
    /// </summary>
    [Fact]
    public void PatchInstallTempDir_Is_Deterministic_Per_Patch()
    {
        var paths = new GamePathResolver(() => Root);
        var key = GamePathResolver.ComputePatchKey("launcher/patch/indexFile.json", "ABCDEF", "3.6.0");

        Assert.Equal(key, GamePathResolver.ComputePatchKey("launcher/patch/indexFile.json", "abcdef", "3.6.0"));
        Assert.NotEqual(key, GamePathResolver.ComputePatchKey("launcher/patch/indexFile.json", "abcdef", "3.5.0"));
        Assert.NotEqual(key, GamePathResolver.ComputePatchKey("launcher/patch/other.json", "abcdef", "3.6.0"));
        Assert.Matches("^[0-9a-f]{16}$", key);

        var dir = paths.PatchInstallTempDir(key);
        Assert.Equal(Path.Combine(Root, "DiffData", "install_tmp", GamePathResolver.PatchInstallTempPrefix + key), dir);
        Assert.Equal(dir, paths.PatchInstallTempDir(key));
        Assert.True(GamePathResolver.IsDirectChildOf(paths.InstallTempDir, dir));

        // 非法 key(含分隔符/..)一律不拼路径
        Assert.Null(paths.PatchInstallTempDir("../x"));
        Assert.Null(paths.PatchInstallTempDir(""));
        Assert.Null(new GamePathResolver(() => null).PatchInstallTempDir(key));
    }
}
