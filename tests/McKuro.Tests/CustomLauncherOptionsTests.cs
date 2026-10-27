using McKuro.Core.Models.Game;
using McKuro.Core.Services.Game;
using McKuro.Core.Services.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace McKuro.Tests;

/// <summary>
/// 自定义启动程序(mod 加载器)。
/// <para>
/// <b>最要紧的一条语义:</b>目标 exe 不存在时必须报错,<b>绝不能回退去启动原版游戏</b> ——
/// 否则用户以为 mod 生效,实际开的是原版,且没有任何提示,排查成本极高。
/// </para>
/// </summary>
public class CustomLauncherOptionsTests
{
    private static CustomLauncherOptions Make(string path, bool enabled = true, string args = "") => new()
    {
        Enabled = enabled,
        Path = path,
        Arguments = args,
    };

    [Fact]
    public void Validate_Rejects_Empty_Path()
    {
        Assert.NotNull(Make("").Validate());
    }

    [Fact]
    public void Validate_Rejects_Relative_Path()
    {
        // 相对路径会按"当前进程工作目录"解析,几乎必然指错地方
        var msg = Make("loader.exe").Validate();

        Assert.NotNull(msg);
        Assert.Contains("完整路径", msg);
    }

    [Fact]
    public void Validate_Rejects_Missing_File()
    {
        var path = Path.Combine(Path.GetTempPath(), "McKuro-no-such-" + Guid.NewGuid().ToString("N") + ".exe");

        var msg = Make(path).Validate();

        Assert.NotNull(msg);
        Assert.Contains("不存在", msg);
    }

    [Fact]
    public void Validate_Accepts_Existing_Absolute_File()
    {
        var path = Path.Combine(Path.GetTempPath(), "McKuro-cl-" + Guid.NewGuid().ToString("N") + ".exe");
        File.WriteAllBytes(path, []);
        try
        {
            Assert.Null(Make(path).Validate());
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void CreateStartInfo_Appends_LauncherArguments_After_Game_Arguments()
    {
        var path = Path.Combine(Path.GetTempPath(), "modder", "loader.exe");

        var psi = Make(path, args: "  --inject mod.dll  ").CreateStartInfo("Client -krqlv=hd -dx11");

        Assert.Equal(path, psi.FileName);
        // 游戏参数在前、启动器参数在后;两端空白被裁掉,不产生多余空格
        Assert.Equal("Client -krqlv=hd -dx11 --inject mod.dll", psi.Arguments);
        // 工作目录是启动器自己所在目录(mod 加载器的常见预期)
        Assert.Equal(Path.GetDirectoryName(path), psi.WorkingDirectory);
        Assert.True(psi.UseShellExecute);
    }

    [Fact]
    public void CreateStartInfo_Omits_Empty_Segments_Without_Stray_Spaces()
    {
        var path = Path.Combine(Path.GetTempPath(), "modder", "loader.exe");

        Assert.Equal("Client -krqlv=hd", Make(path).CreateStartInfo("Client -krqlv=hd").Arguments);
        Assert.Equal("--inject a.dll", Make(path, args: "--inject a.dll").CreateStartInfo("   ").Arguments);
    }

    [Fact]
    public void ProcessName_Comes_From_The_Custom_Program_Not_The_Game()
    {
        var path = Path.Combine(Path.GetTempPath(), "modder", "MyLoader.exe");

        Assert.Equal("MyLoader", Make(path).ProcessName);
    }

    [Fact]
    public void FromSettings_RoundTrips_All_Three_Fields()
    {
        var settings = new AppSettings
        {
            CustomLauncherEnabled = true,
            CustomLauncherPath = @"D:\ModLauncher\loader.exe",
            CustomLauncherArguments = "--inject mod.dll",
        };

        var options = CustomLauncherOptions.FromSettings(settings);

        Assert.True(options.Enabled);
        Assert.Equal(@"D:\ModLauncher\loader.exe", options.Path);
        Assert.Equal("--inject mod.dll", options.Arguments);
    }

    [Fact]
    public void FromSettings_Handles_Null_Settings()
    {
        var options = CustomLauncherOptions.FromSettings(null);

        Assert.False(options.Enabled);
        Assert.Equal("", options.Path);
    }
}

/// <summary>
/// 启动路径的安全语义:自定义启动程序不可用时必须**失败**,不得静默回退到游戏本体。
/// </summary>
public class CustomLauncherLaunchTests : IDisposable
{
    private readonly string _root;

    public CustomLauncherLaunchTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "McKuro-cl-launch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        // 造一个"看着已装好"的游戏目录:根 exe 存在 —— 若实现错误地回退,就会真的去启动它
        File.WriteAllText(Path.Combine(_root, GamePathResolver.ExeRootName), "");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    private static GameUpdater MakeUpdater(string root, AppSettings settings, GamePathResolver paths) =>
        new(
            new GameManifestLoader(new HttpClient()),
            new DownloadEngine(new HttpClient()),
            new UpdateInstaller(),
            new PatchInstaller(new UpdateInstaller()),
            paths,
            Path.Combine(root, ".data"),
            new McKuro.Core.Infrastructure.AppDatabase(Path.Combine(root, ".data")),
            settings: new StubSettings(settings),
            logger: NullLogger<GameUpdater>.Instance);

    [Fact]
    public void LaunchGame_Fails_When_Custom_Launcher_Is_Missing_And_Does_Not_Fall_Back()
    {
        var settings = new AppSettings
        {
            GameRootDir = _root,
            CustomLauncherEnabled = true,
            CustomLauncherPath = Path.Combine(_root, "not-there.exe"),
        };
        var paths = new GamePathResolver(() => _root);
        var updater = MakeUpdater(_root, settings, paths);

        var ok = updater.LaunchGame(out var error);

        Assert.False(ok);
        Assert.NotNull(error);
        Assert.Contains("自定义启动程序", error);
    }

    [Fact]
    public void ResolveLaunchExePath_Returns_Custom_Program_When_Enabled()
    {
        var custom = Path.Combine(_root, "loader.exe");
        File.WriteAllBytes(custom, []);
        var settings = new AppSettings
        {
            GameRootDir = _root,
            CustomLauncherEnabled = true,
            CustomLauncherPath = custom,
        };
        var paths = new GamePathResolver(() => _root);
        var updater = MakeUpdater(_root, settings, paths);

        // 进程监控必须跟随自定义程序,否则「游戏中」状态永远不出现
        Assert.Equal(custom, updater.ResolveLaunchExePath());
    }

    [Fact]
    public void ResolveLaunchExePath_Falls_Back_To_Game_Exe_When_Custom_Disabled()
    {
        var settings = new AppSettings
        {
            GameRootDir = _root,
            CustomLauncherEnabled = false,
            CustomLauncherPath = Path.Combine(_root, "loader.exe"),
        };
        var paths = new GamePathResolver(() => _root);
        var updater = MakeUpdater(_root, settings, paths);

        Assert.Equal(Path.Combine(_root, GamePathResolver.ExeRootName), updater.ResolveLaunchExePath());
    }

    [Fact]
    public void ResolveLaunchExePath_Returns_Null_When_Enabled_But_Missing()
    {
        // 目标不存在时不要谎报路径(否则进程监控会去盯一个永远不会出现的进程)
        var settings = new AppSettings
        {
            GameRootDir = _root,
            CustomLauncherEnabled = true,
            CustomLauncherPath = Path.Combine(_root, "gone.exe"),
        };
        var paths = new GamePathResolver(() => _root);
        var updater = MakeUpdater(_root, settings, paths);

        Assert.Null(updater.ResolveLaunchExePath());
    }

    private sealed class StubSettings(AppSettings s) : ISettingsService
    {
        public AppSettings Current { get; } = s;
        public void Save() { }
        public Task SaveAsync(CancellationToken ct = default) => Task.CompletedTask;
        public void Reload() { }
    }
}
