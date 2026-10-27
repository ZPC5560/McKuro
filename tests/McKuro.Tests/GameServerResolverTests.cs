using McKuro.Core.Services.Game;
using McKuro.Core.Services.Settings;
using Xunit;

namespace McKuro.Tests;

/// <summary>
/// 「当前生效渠道」的唯一判定点。
/// <para>
/// 回归背景:启动页、资讯页、首页、残留自检、官方记录写回原本各写一份
/// 「设置优先 / 自动检测兜底」的三元表达式。只要有一处漏写或写反,就会出现
/// 「设置里切了国际服、某页还用官服」—— 用户看到的就是"区服切换没生效"。
/// 这组测试锁住判定顺序本身。
/// </para>
/// </summary>
public class GameServerResolverTests : IDisposable
{
    private readonly string _root;

    public GameServerResolverTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "McKuro-srv-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    /// <summary>造一个带指定渠道 SDK 目录的游戏目录,模拟"自动检测会得到 serverType"。</summary>
    private void MakeGameDirWith(GameServerType type)
    {
        var binaries = Path.Combine(_root, "Client", "Binaries", "Win64", "ThirdParty");
        switch (type)
        {
            case GameServerType.Bilibili:
                Directory.CreateDirectory(Path.Combine(binaries, "KrPcSdk_Mainland", "KRSDKRes", "Bilibili"));
                break;
            case GameServerType.WeGame:
                Directory.CreateDirectory(Path.Combine(binaries, "KrPcSdk_Mainland", "KRSDKRes", "wegame"));
                break;
            case GameServerType.Global:
                Directory.CreateDirectory(Path.Combine(binaries, "KrPcSdk_Global"));
                break;
            case GameServerType.Official:
                Directory.CreateDirectory(Path.Combine(binaries, "KrPcSdk_Mainland"));
                break;
        }
    }

    private GamePathResolver Paths() => new(() => _root);

    [Fact]
    public void Explicit_Setting_Wins_Over_Auto_Detection()
    {
        MakeGameDirWith(GameServerType.Official); // 自动检测会得到官服
        var settings = new StubSettings(new AppSettings { GameRootDir = _root, ServerType = GameServerType.Global });

        // 设置里明确选了国际服 → 必须用国际服(这正是"设置里切区服应生效"的核心)
        Assert.Equal(GameServerType.Global, GameServerResolver.Resolve(settings, Paths()));
    }

    [Theory]
    [InlineData(GameServerType.Bilibili, GameServerType.Bilibili)]
    [InlineData(GameServerType.WeGame, GameServerType.WeGame)]
    [InlineData(GameServerType.Global, GameServerType.Global)]
    [InlineData(GameServerType.Official, GameServerType.Official)]
    public void Unknown_Setting_Falls_Back_To_Detection(GameServerType detected, GameServerType expected)
    {
        MakeGameDirWith(detected);
        var settings = new StubSettings(new AppSettings { GameRootDir = _root, ServerType = GameServerType.Unknown });

        Assert.Equal(expected, GameServerResolver.Resolve(settings, Paths()));
    }

    [Fact]
    public void Everything_Unknown_Returns_Unknown()
    {
        // 空目录 + 设置未指定 → 检测不出渠道
        var settings = new StubSettings(new AppSettings { GameRootDir = _root, ServerType = GameServerType.Unknown });

        Assert.Equal(GameServerType.Unknown, GameServerResolver.Resolve(settings, Paths()));
    }

    [Fact]
    public void Null_Settings_Still_Detects()
    {
        MakeGameDirWith(GameServerType.Global);

        Assert.Equal(GameServerType.Global, GameServerResolver.Resolve(null, Paths()));
    }

    [Fact]
    public void Setting_Takes_Effect_Immediately_After_Change()
    {
        // 同一进程内改设置后立刻解析:不能有缓存导致"切了要重启才生效"
        MakeGameDirWith(GameServerType.Official);
        var appSettings = new AppSettings { GameRootDir = _root, ServerType = GameServerType.Unknown };
        var settings = new StubSettings(appSettings);

        Assert.Equal(GameServerType.Official, GameServerResolver.Resolve(settings, Paths()));

        appSettings.ServerType = GameServerType.Bilibili;

        Assert.Equal(GameServerType.Bilibili, GameServerResolver.Resolve(settings, Paths()));
    }

    private sealed class StubSettings(AppSettings s) : ISettingsService
    {
        public AppSettings Current { get; } = s;
        public void Save() { }
        public Task SaveAsync(CancellationToken ct = default) => Task.CompletedTask;
        public void Reload() { }
    }
}
