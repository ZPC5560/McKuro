using McKuro.Core.Services.Game;
using McKuro.Core.Services.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace McKuro.Tests;

/// <summary>
/// 启动命令行组装:鸣潮 3.x 起 <c>-krqlv</c> 是必需的引导参数,
/// 缺失时游戏会直接以 "The Game has crashed and will close" 退出(实测复现)。
/// 官方启动器固定以 `Wuthering Waves.exe ["-krqlv=hd"]` 启动。
/// </summary>
public class LaunchArgumentsTests
{
    // ---------- 必需参数:默认必须带上 -krqlv=hd ----------

    [Fact]
    public void Build_Defaults_To_Hd_ResourceLevel()
    {
        // 未设置资源等级(空配置/首次运行)也必须产出 -krqlv=hd,否则游戏无法启动
        Assert.Equal("Client -krqlv=hd", LaunchArguments.Build(useDx11: false, null, null));
        Assert.Equal("Client -krqlv=hd", LaunchArguments.Build(useDx11: false, "", ""));
        Assert.Equal("Client -krqlv=hd", LaunchArguments.Build(useDx11: false, "   ", "   "));
    }

    [Fact]
    public void Build_Always_Contains_ResourceLevel_Switch()
    {
        // 回归锁:任何输入组合(含非法等级)都不允许产出缺少 -krqlv 的命令行
        string?[] levels = [null, "", "hd", "HD", "uhd", "sd", "bogus", "  hd  "];
        string?[] extras = [null, "", "-notexturestreaming", "-krqlv=sd", "-dx11 -krqlv=uhd"];

        foreach (var level in levels)
        {
            foreach (var extra in extras)
            {
                foreach (var dx11 in new[] { false, true })
                {
                    var cmd = LaunchArguments.Build(dx11, level, extra);
                    Assert.Contains("-krqlv=", cmd, StringComparison.Ordinal);
                }
            }
        }
    }

    [Theory]
    [InlineData("uhd", "Client -krqlv=uhd")]
    [InlineData("hd", "Client -krqlv=hd")]
    [InlineData("sd", "Client -krqlv=sd")]
    [InlineData("UHD", "Client -krqlv=uhd")]
    [InlineData(" hd ", "Client -krqlv=hd")]
    [InlineData("invalid", "Client -krqlv=hd")]
    public void Build_Normalizes_ResourceLevel(string level, string expected)
    {
        Assert.Equal(expected, LaunchArguments.Build(useDx11: false, level, null));
    }

    [Fact]
    public void Build_With_Dx11_Matches_Official_Order()
    {
        // 官方启动器 DX11 选项的命令行形态:-dx11 -slno 位于 -krqlv 之前
        Assert.Equal(
            "Client -dx11 -slno -krqlv=hd",
            LaunchArguments.Build(useDx11: true, "hd", null));
    }

    [Fact]
    public void Build_Appends_Custom_Arguments_After_Resource_Level()
    {
        Assert.Equal(
            "Client -krqlv=hd -notexturestreaming",
            LaunchArguments.Build(useDx11: false, "hd", "-notexturestreaming"));
    }

    // ---------- -krqlv 单一事实来源:自定义参数里的重复项必须被剥离 ----------

    [Theory]
    [InlineData("-krqlv=sd", "hd")]
    [InlineData("-krqlv=uhd -notexturestreaming", "hd")]
    [InlineData("-notexturestreaming -krqlv=uhd", "hd")]
    [InlineData("-KRQLV=sd", "hd")]
    [InlineData("-krqlv sd", "hd")]
    public void Build_Strips_Conflicting_ResourceLevel_From_Extra_Arguments(string extra, string level)
    {
        var cmd = LaunchArguments.Build(useDx11: false, level, extra);

        // 只允许出现一次 -krqlv(由资源等级设置决定),自定义参数里的必须被剥掉
        Assert.Equal(1, CountOccurrences(cmd, "-krqlv"));
        Assert.Contains("-krqlv=hd", cmd, StringComparison.Ordinal);
        // 非 -krqlv 的自定义参数必须保留
        if (extra.Contains("notexturestreaming", StringComparison.Ordinal))
        {
            Assert.Contains("-notexturestreaming", cmd, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void StripResourceLevel_Reports_Detected_Level_For_Migration()
    {
        // 旧配置把等级写在自定义参数里;迁移时需要把取值读出来,避免升级后画质被静默改回 hd
        var stripped = LaunchArguments.StripResourceLevel("-krqlv=uhd -notexturestreaming", out var level);

        Assert.Equal("uhd", level);
        Assert.Equal("-notexturestreaming", stripped);

        // 空格分隔写法同样识别,且取值项一并被剥离
        var spacedStripped = LaunchArguments.StripResourceLevel("-krqlv sd", out var spaced);
        Assert.Equal("sd", spaced);
        Assert.Equal(string.Empty, spacedStripped);
    }

    [Fact]
    public void StripResourceLevel_Leaves_Unrelated_Arguments_Intact()
    {
        Assert.Equal("-dx11 -slno -foo=1", LaunchArguments.StripResourceLevel("-dx11 -slno -foo=1", out var level));
        Assert.Null(level);

        // 形如 -krqlvX 的未知写法不臆测语义,原样保留
        Assert.Equal("-krqlvx", LaunchArguments.StripResourceLevel("-krqlvx", out _));
    }

    // ---------- 枚举映射 ----------

    [Theory]
    [InlineData(GameResourceLevel.Ultra, "uhd")]
    [InlineData(GameResourceLevel.High, "hd")]
    [InlineData(GameResourceLevel.Smooth, "sd")]
    public void ToValue_Maps_Levels(GameResourceLevel level, string expected)
    {
        Assert.Equal(expected, LaunchArguments.ToValue(level));
    }

    [Theory]
    [InlineData("uhd", GameResourceLevel.Ultra)]
    [InlineData("hd", GameResourceLevel.High)]
    [InlineData("sd", GameResourceLevel.Smooth)]
    [InlineData(null, GameResourceLevel.High)]
    [InlineData("nope", GameResourceLevel.High)]
    public void FromValue_RoundTrips_And_Falls_Back_To_High(string? value, GameResourceLevel expected)
    {
        Assert.Equal(expected, LaunchArguments.FromValue(value));
    }

    [Fact]
    public void ToValue_And_FromValue_Are_Inverse_For_All_Levels()
    {
        foreach (var level in Enum.GetValues<GameResourceLevel>())
        {
            Assert.Equal(level, LaunchArguments.FromValue(LaunchArguments.ToValue(level)));
        }
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0, index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }
}

/// <summary>
/// 已安装资源包探测:资源等级必须与包体匹配。
/// 官方启动器在 &lt;游戏目录&gt;\launcherDownloadConfig\ 下留标记(common.json 恒有,
/// 另有 hd.json/sd.json/uhd.json 之一),用它可以避免把 SD/UHD 用户强行按 hd 启动。
/// </summary>
public class InstalledResourceLevelDetectionTests : IDisposable
{
    private readonly string _root;

    public InstalledResourceLevelDetectionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "McKuro-lvl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "launcherDownloadConfig"));
        // common.json 恒存在,不构成等级信息
        File.WriteAllText(Path.Combine(_root, "launcherDownloadConfig", "common.json"), """{"packName":"common"}""");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    private void Mark(string fileName) =>
        File.WriteAllText(Path.Combine(_root, "launcherDownloadConfig", fileName), "{}");

    [Fact]
    public void Detects_High_Bundle()
    {
        Mark("hd.json");
        Assert.Equal("hd", LaunchArguments.DetectInstalledResourceLevel(_root));
    }

    [Fact]
    public void Detects_Smooth_Bundle()
    {
        Mark("sd.json");
        Assert.Equal("sd", LaunchArguments.DetectInstalledResourceLevel(_root));
    }

    [Fact]
    public void Detects_Ultra_Bundle()
    {
        Mark("uhd.json");
        Assert.Equal("uhd", LaunchArguments.DetectInstalledResourceLevel(_root));
    }

    [Fact]
    public void Common_Only_Yields_Null()
    {
        Assert.Null(LaunchArguments.DetectInstalledResourceLevel(_root));
    }

    [Fact]
    public void Missing_Directory_Yields_Null()
    {
        Assert.Null(LaunchArguments.DetectInstalledResourceLevel(Path.Combine(_root, "nope")));
        Assert.Null(LaunchArguments.DetectInstalledResourceLevel(null));
        Assert.Null(LaunchArguments.DetectInstalledResourceLevel(""));
    }

    [Fact]
    public void Invalid_Path_Yields_Null_Instead_Of_Throwing()
    {
        Assert.Null(LaunchArguments.DetectInstalledResourceLevel("Z:\\<>:|?*"));
    }

    [Fact]
    public void Detected_Level_Drives_Working_Command_Line()
    {
        Mark("uhd.json");

        var level = LaunchArguments.DetectInstalledResourceLevel(_root);
        var cmd = LaunchArguments.Build(useDx11: false, level, null);

        Assert.Equal("Client -krqlv=uhd", cmd);
    }
}

/// <summary>
/// 资源等级设置的持久化与旧配置迁移:
/// 早期版本用户手工把 -krqlv 写在自定义参数里,升级后必须迁移到结构化设置,
/// 否则会被默认 hd 覆盖画质(或重复出现两个 -krqlv)。
/// </summary>
public class ResourceLevelSettingsTests : IDisposable
{
    private readonly string _dir;

    public ResourceLevelSettingsTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "McKuro-rl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* ignore */ }
    }

    private SettingsService NewService() => new(_dir, NullLogger<SettingsService>.Instance);

    [Fact]
    public void Default_ResourceLevel_Is_Empty_And_Resolves_To_Hd()
    {
        // 默认不写死等级(空),由 LaunchArguments 兜底成官方默认 hd
        Assert.Equal("", NewService().Current.ResourceLevel);
        Assert.Equal("hd", LaunchArguments.Normalize(new AppSettings().ResourceLevel));
    }

    [Fact]
    public void ResourceLevel_RoundTrips()
    {
        var s = NewService();
        s.Current.ResourceLevel = "uhd";
        s.Save();

        Assert.Equal("uhd", NewService().Current.ResourceLevel);
    }

    [Fact]
    public void Legacy_Manual_ResourceLevel_Argument_Is_Migrated()
    {
        // 模拟旧版配置:等级写在自定义参数里,结构化字段缺失
        File.WriteAllText(
            Path.Combine(_dir, "settings.json"),
            """{"StartGameArguments":"-krqlv=uhd -notexturestreaming"}""");

        var s = NewService();

        // 迁移后:等级进入结构化设置,自定义参数里不再残留 -krqlv
        Assert.Equal("uhd", s.Current.ResourceLevel);
        Assert.Equal("-notexturestreaming", s.Current.StartGameArguments);
    }

    [Fact]
    public void Legacy_Migration_Does_Not_Override_Explicit_Setting()
    {
        File.WriteAllText(
            Path.Combine(_dir, "settings.json"),
            """{"ResourceLevel":"sd","StartGameArguments":"-krqlv=uhd"}""");

        var s = NewService();

        // 显式设置优先;但仍需保证命令里不出现两个 -krqlv
        Assert.Equal("sd", s.Current.ResourceLevel);
        Assert.Equal("Client -krqlv=sd", LaunchArguments.Build(false, s.Current.ResourceLevel, s.Current.StartGameArguments));
    }

    [Fact]
    public void Migrated_Settings_Produce_Working_Command_Line()
    {
        File.WriteAllText(
            Path.Combine(_dir, "settings.json"),
            """{"StartGameArguments":"-krqlv=uhd"}""");

        var s = NewService();
        var cmd = LaunchArguments.Build(false, s.Current.ResourceLevel, s.Current.StartGameArguments);

        // 迁移必须保留用户原本选择的画质
        Assert.Equal("Client -krqlv=uhd", cmd);
    }
}
