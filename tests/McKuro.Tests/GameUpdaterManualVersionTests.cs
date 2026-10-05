using McKuro.Core.Services.Game;

namespace McKuro.Tests;

/// <summary>
/// 手动指定本地游戏版本(对齐 Haiyu「选择版本/跳过校验」)的写入与格式校验:
/// 无 SQLite 注入时走 installed_versions.json 回退路径,可直接对文件断言。
/// </summary>
public sealed class GameUpdaterManualVersionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "McKuroVer_" + Guid.NewGuid().ToString("N"));
    private readonly GameUpdater _updater;

    public GameUpdaterManualVersionTests()
    {
        Directory.CreateDirectory(_dir);
        var paths = new GamePathResolver(() => _dir);
        _updater = new GameUpdater(
            new GameManifestLoader(new HttpClient()),
            new DownloadEngine(new HttpClient()),
            new UpdateInstaller(),
            paths,
            _dir);
    }

    private string MarkerPath => Path.Combine(_dir, "installed_versions.json");

    [Fact]
    public void TrySetInstalledVersion_ValidVersion_WritesMarkerTrimmed()
    {
        Assert.True(_updater.TrySetInstalledVersion(" 3.7.0 "));
        Assert.True(File.Exists(MarkerPath));
        var json = File.ReadAllText(MarkerPath);
        Assert.Contains("3.7.0", json);
        // 写入值已去首尾空白(不会带出 " 3.7.0 " 这种脏 key/value)
        Assert.DoesNotContain("\" 3.7.0 \"", json);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("3.7.0-beta")]      // 带非数字尾缀:与检查更新的版本语法一致,拒绝写入
    [InlineData("v3.7.0")]          // 前导 v 不是清单版本语法(缓存里存的是纯数字段)
    [InlineData("1.2.3.4.5")]       // 超过 4 段
    public void TrySetInstalledVersion_InvalidFormat_ReturnsFalse(string version)
    {
        Assert.False(_updater.TrySetInstalledVersion(version));
        Assert.False(File.Exists(MarkerPath));
    }

    [Fact]
    public void TrySetInstalledVersion_MultiSegmentVersion_Accepted()
    {
        Assert.True(_updater.TrySetInstalledVersion("3.7.0.12345"));
        Assert.Contains("3.7.0.12345", File.ReadAllText(MarkerPath));
    }

    [Fact]
    public void TrySetInstalledVersion_NoGameDir_ReturnsFalse()
    {
        var noRoot = new GameUpdater(
            new GameManifestLoader(new HttpClient()),
            new DownloadEngine(new HttpClient()),
            new UpdateInstaller(),
            new GamePathResolver(() => null),
            _dir);
        Assert.False(noRoot.TrySetInstalledVersion("3.7.0"));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // 临时目录清理失败不影响测试结论
        }
    }
}
