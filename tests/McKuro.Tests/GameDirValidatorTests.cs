using McKuro.Core.Services.Game;
using McKuro.Services;

namespace McKuro.Tests;

/// <summary>
/// 「游戏安装目录」选择校验测试(用户需求:安装要支持装到指定目录)。
/// <para>
/// 回归背景:原先选目录<b>强制</b>要求目录里已有 Wuthering Waves.exe,全新安装无法指定目标目录。
/// 现在空目录/新目录也合法,但"非空且不像游戏目录"仍要拒绝 —— 否则用户误点整个磁盘根或桌面,
/// 几十 GB 会被灌进无关位置。
/// </para>
/// </summary>
public class GameDirValidatorTests : IDisposable
{
    private readonly string _root;

    public GameDirValidatorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "McKuro_gdv_" + Guid.NewGuid().ToString("N"));
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

    private string NewDir(string name)
    {
        var p = Path.Combine(_root, name);
        Directory.CreateDirectory(p);
        return p;
    }

    // ---------- 接受的情况 ----------

    [Fact]
    public void EmptyDirectory_IsValidNewInstallTarget()
    {
        // 核心诉求:全新安装要能选一个空文件夹
        Assert.Equal(GameDirVerdict.NewInstallTarget, GameDirValidator.Validate(NewDir("empty")));
    }

    [Fact]
    public void NonExistentDirectory_IsValidNewInstallTarget()
    {
        // 安装流程会 Directory.CreateDirectory,所以"还不存在"也是合法目标
        Assert.Equal(GameDirVerdict.NewInstallTarget,
            GameDirValidator.Validate(Path.Combine(_root, "not-yet-created")));
    }

    [Fact]
    public void DirectoryWithGameExe_IsExistingInstall()
    {
        var dir = NewDir("installed");
        File.WriteAllText(Path.Combine(dir, GamePathResolver.ExeRootName), "x");
        Assert.Equal(GameDirVerdict.ExistingInstall, GameDirValidator.Validate(dir));
    }

    [Fact]
    public void ExistingInstall_Wins_EvenIfNotFullyPopulated()
    {
        // 只要有主程序就认("装到一半"由 GameUpdater 的未完成判定处理,不是目录校验的职责)
        var dir = NewDir("partial");
        File.WriteAllText(Path.Combine(dir, GamePathResolver.ExeRootName), "x");
        Assert.Equal(GameDirVerdict.ExistingInstall, GameDirValidator.Validate(dir));
    }

    // ---------- 拒绝的情况 ----------

    [Fact]
    public void NonEmptyDirectoryWithoutGameExe_IsRejected()
    {
        // 防止用户误选桌面/文档/磁盘根,把几十 GB 灌进无关位置
        var dir = NewDir("random");
        File.WriteAllText(Path.Combine(dir, "some-user-file.txt"), "keep me");
        Assert.Equal(GameDirVerdict.NotGameDirectory, GameDirValidator.Validate(dir));
    }

    [Fact]
    public void NonEmptyDirectoryWithSubfolderOnly_IsRejected()
    {
        var dir = NewDir("withsub");
        Directory.CreateDirectory(Path.Combine(dir, "pictures"));
        Assert.Equal(GameDirVerdict.NotGameDirectory, GameDirValidator.Validate(dir));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankPath_IsRejected(string? path)
    {
        Assert.Equal(GameDirVerdict.NotGameDirectory, GameDirValidator.Validate(path));
    }

    // ---------- 便捷判定 ----------

    [Theory]
    [InlineData(GameDirVerdict.ExistingInstall, true)]
    [InlineData(GameDirVerdict.NewInstallTarget, true)]
    [InlineData(GameDirVerdict.NotGameDirectory, false)]
    public void IsAcceptable_Contract(GameDirVerdict verdict, bool expected)
    {
        Assert.Equal(expected, GameDirValidator.IsAcceptable(verdict));
    }
}
