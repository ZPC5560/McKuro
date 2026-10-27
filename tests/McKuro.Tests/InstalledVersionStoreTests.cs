using System.Text.Json;
using McKuro.Core.Infrastructure;
using McKuro.Core.Services.Game;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace McKuro.Tests;

/// <summary>
/// installed_versions 从 JSON 标记文件迁移到 SQLite 的测试:
/// SQLite 读写 / UPSERT 更新 / 旧 JSON 自动迁移 / 无 JSON 时正常路径。
/// </summary>
public class InstalledVersionStoreTests : IDisposable
{
    private readonly string _tmpDir;
    private readonly AppDatabase _db;

    public InstalledVersionStoreTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "McKuro_iv_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmpDir);
        _db = new AppDatabase(_tmpDir);
    }

    public void Dispose()
    {
        _db.Dispose();
        try
        {
            Directory.Delete(_tmpDir, recursive: true);
        }
        catch (Exception)
        {
            // 忽略
        }
    }

    [Fact]
    public void Set_Then_Get_RoundTrips()
    {
        _db.SetInstalledVersion(@"D:\games\wuthering", "1.2.0");
        Assert.Equal("1.2.0", _db.GetInstalledVersion(@"D:\games\wuthering"));
    }

    [Fact]
    public void Get_Missing_Returns_Null()
    {
        Assert.Null(_db.GetInstalledVersion(@"D:\games\nonexistent"));
    }

    [Fact]
    public void Upsert_Updates_Existing_Value()
    {
        const string root = @"D:\games\wuthering";
        _db.SetInstalledVersion(root, "1.2.0");
        _db.SetInstalledVersion(root, "1.3.0");

        Assert.Equal("1.3.0", _db.GetInstalledVersion(root));
    }

    [Fact]
    public void Separate_Roots_Are_Isolated()
    {
        _db.SetInstalledVersion(@"D:\games\wuthering", "1.2.0");
        _db.SetInstalledVersion(@"D:\games\pgr", "2.5.0");

        Assert.Equal("1.2.0", _db.GetInstalledVersion(@"D:\games\wuthering"));
        Assert.Equal("2.5.0", _db.GetInstalledVersion(@"D:\games\pgr"));
    }

    [Fact]
    public void GameUpdater_Migrates_Legacy_Json_Then_Uses_Sqlite()
    {
        // 预置旧版 JSON 标记文件
        var legacy = Path.Combine(_tmpDir, "installed_versions.json");
        File.WriteAllText(legacy, JsonSerializer.Serialize(
            new Dictionary<string, string> { { @"D:\legacy\root", "0.9.0" } }));

        var updater = CreateUpdater();
        var version = GetInstalledVersionViaUpdater(updater, @"D:\legacy\root");

        Assert.Equal("0.9.0", version);
        // JSON 应已被迁移删除
        Assert.False(File.Exists(legacy));
        // SQLite 中应已有数据
        Assert.Equal("0.9.0", _db.GetInstalledVersion(@"D:\legacy\root"));
    }

    [Fact]
    public void GameUpdater_Without_Legacy_Json_Reads_Sqlite()
    {
        _db.SetInstalledVersion(@"D:\sqlite\root", "3.1.0");

        var updater = CreateUpdater();
        var version = GetInstalledVersionViaUpdater(updater, @"D:\sqlite\root");

        Assert.Equal("3.1.0", version);
    }

    [Fact]
    public void GameUpdater_Write_Goes_To_Sqlite()
    {
        var updater = CreateUpdater();
        WriteInstalledVersionViaUpdater(updater, @"D:\new\root", "4.0.0");

        Assert.Equal("4.0.0", _db.GetInstalledVersion(@"D:\new\root"));
        // 不应产生 JSON 标记文件
        Assert.False(File.Exists(Path.Combine(_tmpDir, "installed_versions.json")));
    }

    private GameUpdater CreateUpdater()
    {
        // 用最简依赖构造 GameUpdater(测试只走 installed version 读写路径)
        var loader = new GameManifestLoader(new HttpClient());
        var downloader = new DownloadEngine(new HttpClient());
        var installer = new UpdateInstaller();
        var paths = new GamePathResolver(() => @"D:\games\wuthering");
        return new GameUpdater(
            loader, downloader, installer, paths, _tmpDir,
            _db, logger: NullLogger<GameUpdater>.Instance);
    }

    private static string? GetInstalledVersionViaUpdater(GameUpdater updater, string root)
        => ReadInstalledVersionReflectively(updater, root);

    private static void WriteInstalledVersionViaUpdater(GameUpdater updater, string root, string version)
    {
        // 私有方法经反射调用,仅测试用
        var method = typeof(GameUpdater).GetMethod("WriteInstalledVersion",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        method.Invoke(updater, [root, version]);
    }

    private static string? ReadInstalledVersionReflectively(GameUpdater updater, string root)
    {
        var method = typeof(GameUpdater).GetMethod("ReadInstalledVersion",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        return method.Invoke(updater, [root]) as string;
    }

    // ---------- 「安装未完成」标记 ----------
    // 背景(用户反馈):下载到一半关掉应用,重新打开后按钮变成「启动游戏」,
    // 既无法继续下载、点启动也失败。根因是"只看根 exe 是否存在"把半成品当成了已安装。

    [Fact]
    public void IncompleteInstall_MissingByDefault()
    {
        Assert.False(_db.HasIncompleteInstall(@"D:\games\wuthering"));
    }

    [Fact]
    public void IncompleteInstall_Set_Then_Read_RoundTrips()
    {
        _db.SetIncompleteInstall(@"D:\games\wuthering");
        Assert.True(_db.HasIncompleteInstall(@"D:\games\wuthering"));
    }

    [Fact]
    public void IncompleteInstall_Set_Is_Idempotent()
    {
        // 每次开始安装都会写一遍;重复调用不能抛(主键冲突)
        _db.SetIncompleteInstall(@"D:\games\wuthering");
        _db.SetIncompleteInstall(@"D:\games\wuthering");
        Assert.True(_db.HasIncompleteInstall(@"D:\games\wuthering"));
    }

    [Fact]
    public void IncompleteInstall_Clear_RemovesFlag()
    {
        _db.SetIncompleteInstall(@"D:\games\wuthering");
        _db.ClearIncompleteInstall(@"D:\games\wuthering");
        Assert.False(_db.HasIncompleteInstall(@"D:\games\wuthering"));
    }

    [Fact]
    public void IncompleteInstall_Clear_OnMissing_DoesNotThrow()
    {
        // 正常路径里安装成功后无条件清一次,没标记时不能算异常
        _db.ClearIncompleteInstall(@"D:\games\nonexistent");
        Assert.False(_db.HasIncompleteInstall(@"D:\games\nonexistent"));
    }

    [Fact]
    public void IncompleteInstall_Roots_Are_Isolated()
    {
        _db.SetIncompleteInstall(@"D:\games\wuthering");
        Assert.False(_db.HasIncompleteInstall(@"D:\games\pgr"));
    }

    [Fact]
    public void WriteInstalledVersion_Clears_Incomplete_Flag()
    {
        // 关键不变量:写"已装版本"= 安装真正成功,必须顺手清掉未完成标记
        // (全量安装/补丁安装/修复三条路径都汇到 WriteInstalledVersion,漏清会永久误判未装完)
        var updater = CreateUpdater();
        const string root = @"D:\games\wuthering";
        _db.SetIncompleteInstall(root);
        Assert.True(_db.HasIncompleteInstall(root));

        WriteInstalledVersionViaUpdater(updater, root, "1.3.0");

        Assert.False(_db.HasIncompleteInstall(root));
        Assert.Equal("1.3.0", _db.GetInstalledVersion(root));
    }

    // ---------- 兜底推断:标记缺失时按目录形态判断 ----------
    // 标记机制上线前就被打断的老安装没有标记;只看标记会把它永远当成"装好了"。

    [Fact]
    public void PartialInstall_WithoutMarker_IsDetectedByShape()
    {
        // 根 exe(启动器)在、客户端 exe 不在 = 半成品。这正是用户报的症状。
        var game = Path.Combine(_tmpDir, "partial_game");
        Directory.CreateDirectory(game);
        File.WriteAllText(Path.Combine(game, GamePathResolver.ExeRootName), "launcher");

        var updater = CreateUpdaterFor(game);
        Assert.True(updater.HasIncompleteInstall());
    }

    [Fact]
    public void CompleteInstall_WithBothExes_IsNotFlagged()
    {
        // 正常装完:根 exe + 客户端 exe 都在,必须判为已装完(否则正常用户会被反复要求重装)
        var game = Path.Combine(_tmpDir, "complete_game");
        Directory.CreateDirectory(game);
        File.WriteAllText(Path.Combine(game, GamePathResolver.ExeRootName), "launcher");
        var clientExe = Path.Combine(game, GamePathResolver.ExeClientRelative);
        Directory.CreateDirectory(Path.GetDirectoryName(clientExe)!);
        File.WriteAllText(clientExe, "client");

        var updater = CreateUpdaterFor(game);
        Assert.False(updater.HasIncompleteInstall());
    }

    [Fact]
    public void EmptyDirectory_IsNotFlaggedIncomplete()
    {
        // 从没装过的空目录不是"装到一半" —— 它由 IsGameInstalled 直接判 false,
        // 若这里也返回 true,「从未安装」与「装到一半」的文案就区分不开了。
        var game = Path.Combine(_tmpDir, "empty_game");
        Directory.CreateDirectory(game);

        var updater = CreateUpdaterFor(game);
        Assert.False(updater.HasIncompleteInstall());
    }

    [Fact]
    public void MarkerAlone_IsEnough_EvenIfFilesLookComplete()
    {
        // 标记是权威:即使文件看起来齐全(上一次安装刚写完 exe 就崩在收尾阶段),
        // 只要标记还在就必须继续按"未装完"处理。
        var game = Path.Combine(_tmpDir, "marked_game");
        Directory.CreateDirectory(game);
        File.WriteAllText(Path.Combine(game, GamePathResolver.ExeRootName), "launcher");
        var clientExe = Path.Combine(game, GamePathResolver.ExeClientRelative);
        Directory.CreateDirectory(Path.GetDirectoryName(clientExe)!);
        File.WriteAllText(clientExe, "client");
        _db.SetIncompleteInstall(game);

        var updater = CreateUpdaterFor(game);
        Assert.True(updater.HasIncompleteInstall());
    }

    /// <summary>构造一个指向指定游戏目录的 updater(目录通过可变闭包注入)。</summary>
    private GameUpdater CreateUpdaterFor(string gameRoot)
    {
        var loader = new GameManifestLoader(new HttpClient());
        var downloader = new DownloadEngine(new HttpClient());
        var installer = new UpdateInstaller();
        var paths = new GamePathResolver(() => gameRoot);
        return new GameUpdater(
            loader, downloader, installer, paths, _tmpDir,
            _db, logger: NullLogger<GameUpdater>.Instance);
    }
}
