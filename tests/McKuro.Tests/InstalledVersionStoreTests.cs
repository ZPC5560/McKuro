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
}
