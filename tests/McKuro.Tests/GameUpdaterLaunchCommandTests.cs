using McKuro.Core.Services.Game;
using McKuro.Core.Services.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace McKuro.Tests;

/// <summary>
/// 启动命令行的端到端接线:GameUpdater 必须把设置里的资源等级代入 <c>-krqlv</c>。
/// <para>
/// 背景(实测):游戏以 <c>Wuthering Waves.exe</c> 启动时,缺少 <c>-krqlv</c>
/// 会直接弹出 "The Game has crashed and will close" 并退出;
/// 官方启动器固定传 <c>["-krqlv=hd"]</c>。McKuro 此前默认只传一个孤立的
/// <c>Client</c> token,既不满足必需参数也不构成有效命令行。
/// </para>
/// </summary>
public class GameUpdaterLaunchCommandTests
{
    private sealed class StubSettings(AppSettings settings) : ISettingsService
    {
        public AppSettings Current { get; } = settings;
        public void Save() { }
        public Task SaveAsync(CancellationToken ct = default) => Task.CompletedTask;
        public void Reload() { }
    }

    private static GameUpdater CreateUpdater(AppSettings settings) => new(
        null!,
        null!,
        null!,
        new GamePathResolver(() => @"D:\games\wuthering"),
        Path.GetTempPath(),
        database: null,
        settings: new StubSettings(settings),
        logger: NullLogger<GameUpdater>.Instance);

    private static GameUpdater CreateUpdaterForRoot(AppSettings settings, string root) => new(
        null!,
        null!,
        null!,
        new GamePathResolver(() => root),
        Path.GetTempPath(),
        database: null,
        settings: new StubSettings(settings),
        logger: NullLogger<GameUpdater>.Instance);

    [Fact]
    public void Default_Settings_Produce_Required_ResourceLevel_Switch()
    {
        // 全新安装(未设置资源等级)也必须产出 -krqlv=hd,否则"点击启动无反应"
        var cmd = CreateUpdater(new AppSettings()).BuildLaunchCommandLine();

        Assert.Equal("Client -krqlv=hd", cmd);
    }

    [Theory]
    [InlineData("uhd", "Client -krqlv=uhd")]
    [InlineData("hd", "Client -krqlv=hd")]
    [InlineData("sd", "Client -krqlv=sd")]
    public void ResourceLevel_Setting_Flows_Into_Command_Line(string level, string expected)
    {
        var settings = new AppSettings { ResourceLevel = level };

        Assert.Equal(expected, CreateUpdater(settings).BuildLaunchCommandLine());
    }

    [Fact]
    public void Dx11_And_Custom_Arguments_Compose_With_ResourceLevel()
    {
        var settings = new AppSettings
        {
            ResourceLevel = "hd",
            UseDx11 = true,
            StartGameArguments = "-notexturestreaming",
        };

        Assert.Equal(
            "Client -dx11 -slno -krqlv=hd -notexturestreaming",
            CreateUpdater(settings).BuildLaunchCommandLine());
    }

    [Fact]
    public void Legacy_Manual_Krqlv_In_Custom_Arguments_Is_Not_Duplicated()
    {
        // 未迁移的旧配置也不能让命令行出现两个 -krqlv
        var settings = new AppSettings
        {
            ResourceLevel = "hd",
            StartGameArguments = "-krqlv=uhd",
        };

        var cmd = CreateUpdater(settings).BuildLaunchCommandLine();

        Assert.Equal("Client -krqlv=hd", cmd);
    }

    [Fact]
    public void Detected_Installed_Bundle_Is_Used_When_Setting_Is_Empty()
    {
        // SD/UHD 用户没设过等级:必须按已装资源包启动,不能一律按 hd
        var root = Path.Combine(Path.GetTempPath(), "McKuro-upd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "launcherDownloadConfig"));
        try
        {
            File.WriteAllText(Path.Combine(root, "launcherDownloadConfig", "sd.json"), "{}");

            var cmd = CreateUpdaterForRoot(new AppSettings(), root).BuildLaunchCommandLine();

            Assert.Equal("Client -krqlv=sd", cmd);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Explicit_Setting_Wins_Over_Detected_Bundle()
    {
        var root = Path.Combine(Path.GetTempPath(), "McKuro-upd2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "launcherDownloadConfig"));
        try
        {
            File.WriteAllText(Path.Combine(root, "launcherDownloadConfig", "sd.json"), "{}");

            var settings = new AppSettings { ResourceLevel = "uhd" };
            var cmd = CreateUpdaterForRoot(settings, root).BuildLaunchCommandLine();

            Assert.Equal("Client -krqlv=uhd", cmd);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Falls_Back_To_Official_Default_When_Nothing_Detected()
    {
        var cmd = CreateUpdaterForRoot(new AppSettings(), Path.GetTempPath()).BuildLaunchCommandLine();

        Assert.Equal("Client -krqlv=hd", cmd);
    }
}
