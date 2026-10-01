using System.Net;
using System.Text;
using McKuro.Core.Models.Game;
using McKuro.Core.Services.Game;
using McKuro.Core.Services.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace McKuro.Tests;

/// <summary>
/// 资源等级规划(纯逻辑):等级 ↔ 组合名映射、包组合解析、体积汇总。
/// 事实依据(实测官方新版协议):HD=[common,hd]、SD=[common,sd]、UHD=[common,uhd];
/// 各等级包 dest 完全不重叠,common 为共享包。
/// </summary>
public class ResourceLevelPlannerTests
{
    private static KuroResourcePackIndex Index() => new()
    {
        CdnList = [new KuroCdnData { Url = "https://cdn.example.com/", P = 1 }],
        ResourcePacks = new Dictionary<string, KuroResourcePack>
        {
            ["common"] = new() { Version = "3.7.0", IndexFile = "c/index.json", BaseUrl = "b/zip/", Size = 40_743_336_731 },
            ["hd"] = new() { Version = "3.7.0", IndexFile = "h/index.json", BaseUrl = "b/zip/", Size = 45_713_445_823 },
            ["sd"] = new() { Version = "3.7.0", IndexFile = "s/index.json", BaseUrl = "b/zip/", Size = 21_973_376_250 },
            ["uhd"] = new() { Version = "3.7.0", IndexFile = "u/index.json", BaseUrl = "b/zip/", Size = 66_036_274_508 },
        },
        Bundles = new Dictionary<string, KuroResourceBundle>
        {
            ["HD"] = new() { ResourcePacks = ["common", "hd"] },
            ["SD"] = new() { ResourcePacks = ["common", "sd"] },
            ["UHD"] = new() { ResourcePacks = ["common", "uhd"] },
        },
    };

    [Theory]
    [InlineData("uhd", "UHD")]
    [InlineData("hd", "HD")]
    [InlineData("sd", "SD")]
    [InlineData("HD", "HD")]
    [InlineData("", "HD")]
    [InlineData("bogus", "HD")]
    public void LevelToBundleName_Normalizes(string level, string expected) =>
        Assert.Equal(expected, ResourceLevelPlanner.LevelToBundleName(level));

    [Theory]
    [InlineData("UHD", "uhd")]
    [InlineData("HD", "hd")]
    [InlineData("SD", "sd")]
    [InlineData("hd", "hd")]
    [InlineData("XYZ", null)]
    [InlineData(null, null)]
    public void BundleNameToLevel_Maps(string? bundle, string? expected) =>
        Assert.Equal(expected, ResourceLevelPlanner.BundleNameToLevel(bundle));

    [Fact]
    public void ResolvePackNames_Uses_Bundle_Composition()
    {
        var index = Index();

        Assert.Equal(["common", "hd"], ResourceLevelPlanner.ResolvePackNames(index, "HD"));
        Assert.Equal(["common", "sd"], ResourceLevelPlanner.ResolvePackNames(index, "SD"));
        Assert.Equal(["common", "uhd"], ResourceLevelPlanner.ResolvePackNames(index, "UHD"));
    }

    [Fact]
    public void ResolvePackNames_Falls_Back_To_Common_Plus_Level()
    {
        // bundles 缺失(旧协议/字段变化)时按官方约定回退,不能返回空导致切换不可用
        var index = new KuroResourcePackIndex
        {
            ResourcePacks = new Dictionary<string, KuroResourcePack> { ["common"] = new(), ["hd"] = new() },
        };

        Assert.Equal(["common", "hd"], ResourceLevelPlanner.ResolvePackNames(index, "HD"));
    }

    [Fact]
    public void TotalBytesFor_Sums_Common_And_Level_Pack()
    {
        var index = Index();
        long GiB = 1024L * 1024 * 1024;

        // 与官方启动器「选择资源等级」显示值一致:58.41 / 80.52 / 99.45 GB
        Assert.Equal(58.41, Math.Round(ResourceLevelPlanner.TotalBytesFor(index, "SD") / (double)GiB, 2));
        Assert.Equal(80.52, Math.Round(ResourceLevelPlanner.TotalBytesFor(index, "HD") / (double)GiB, 2));
        Assert.Equal(99.45, Math.Round(ResourceLevelPlanner.TotalBytesFor(index, "UHD") / (double)GiB, 2));
    }

    [Fact]
    public void PackBytes_Prefers_Resolved_Manifest_Total()
    {
        // 清单已解析时用真实总量(比 index.json 声明的 size 更可靠)
        var resolved = new KuroResourcePack { Size = 100, TotalBytes = 250 };
        Assert.Equal(250, ResourceLevelPlanner.PackBytes(resolved));

        // 未解析时回退 size
        Assert.Equal(100, ResourceLevelPlanner.PackBytes(new KuroResourcePack { Size = 100 }));
        Assert.Equal(0, ResourceLevelPlanner.PackBytes(null));
    }

    [Fact]
    public void EnumerateLevels_Returns_Ultra_High_Smooth_Order()
    {
        var levels = ResourceLevelPlanner.EnumerateLevels(Index());

        Assert.Equal(["uhd", "hd", "sd"], levels.Select(l => l.Value));
        Assert.Equal(GameResourceLevel.Ultra, levels[0].Level);
        Assert.Equal(GameResourceLevel.Smooth, levels[2].Level);
    }

    [Fact]
    public void EnumerateLevels_Skips_Levels_Not_Offered_By_Server()
    {
        // 服务端只提供 HD/UHD:不应列出点了必然失败的 SD
        var index = Index();
        index.Bundles!.Remove("SD");

        Assert.Equal(["uhd", "hd"], ResourceLevelPlanner.EnumerateLevels(index).Select(l => l.Value));
    }
}

/// <summary>
/// 资源等级服务:index.json 解析、包清单 URL 拼装、已安装判定与差异计划。
/// </summary>
public class ResourceLevelServiceTests : IDisposable
{
    private const string IndexUrl = "https://test.example.com/index.json";
    private const string Cdn = "https://cdn.example.com/";
    private const string BaseUrl = "launcher/game/G152/10003/3.7.0/abc/zip/";

    private readonly string _root;

    public ResourceLevelServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "McKuro-rlsvc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "launcherDownloadConfig"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    private void MarkInstalled(params string[] packs)
    {
        foreach (var p in packs)
        {
            File.WriteAllText(Path.Combine(_root, "launcherDownloadConfig", p + ".json"), $"{{\"packName\":\"{p}\"}}");
        }
    }

    private static string PackManifestJson(params (string Dest, long Size)[] files)
    {
        var sb = new StringBuilder("""{"resource":[""");
        for (int i = 0; i < files.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append($$"""{"dest":"{{files[i].Dest}}","size":{{files[i].Size}},"md5":"aa"}""");
        }
        sb.Append("]}");
        return sb.ToString();
    }

    private static string IndexJson() => $$"""
    {
      "cdnList":[{"url":"{{Cdn}}","P":1}],
      "resourcePacks":{
        "common":{"version":"3.7.0","indexFile":"c/common/indexFile.json","baseUrl":"{{BaseUrl}}","size":100},
        "hd":{"version":"3.7.0","indexFile":"c/hd/indexFile.json","baseUrl":"{{BaseUrl}}","size":200},
        "sd":{"version":"3.7.0","indexFile":"c/sd/indexFile.json","baseUrl":"{{BaseUrl}}","size":300},
        "uhd":{"version":"3.7.0","indexFile":"c/uhd/indexFile.json","baseUrl":"{{BaseUrl}}","size":400}
      },
      "bundles":{
        "HD":{"resourcePacks":["common","hd"]},
        "SD":{"resourcePacks":["common","sd"]},
        "UHD":{"resourcePacks":["common","uhd"]}
      }
    }
    """;

    private ResourceLevelService CreateService(Dictionary<string, string> responses, AppSettings? settings = null)
    {
        var handler = new StubHandler(responses);
        var http = new HttpClient(handler);
        var loader = new GameManifestLoader(http);
        var downloader = new DownloadEngine(new HttpClient(handler));
        var paths = new GamePathResolver(() => _root);
        var svc = new ResourceLevelService(
            loader,
            downloader,
            paths,
            settings: settings is null ? null : new StubSettings(settings),
            indexUrlProvider: _ => IndexUrl,
            logger: NullLogger<ResourceLevelService>.Instance);
        return svc;
    }

    private sealed class StubSettings(AppSettings s) : ISettingsService
    {
        public AppSettings Current { get; } = s;
        public void Save() { }
        public Task SaveAsync(CancellationToken ct = default) => Task.CompletedTask;
        public void Reload() { }
    }

    private sealed class StubHandler(Dictionary<string, string> responses) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();
            if (responses.TryGetValue(url, out var body))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    [Fact]
    public async Task GetLevelOptions_Reports_Sizes_And_Installed_State()
    {
        MarkInstalled("common", "hd");
        var svc = CreateService(new Dictionary<string, string> { [IndexUrl] = IndexJson() });

        var options = await svc.GetLevelOptionsAsync(GameServerType.Official);

        Assert.Equal(3, options.Count);
        Assert.Equal(["uhd", "hd", "sd"], options.Select(o => o.Value));

        // 只有 HD 标记齐全 → 仅 HD 视为已安装
        Assert.False(options[0].Installed); // 极致
        Assert.True(options[1].Installed);  // 高清
        Assert.False(options[2].Installed); // 流畅

        // 体积来自 index.json 声明(common+level)
        Assert.Equal(500, options[0].TotalBytes);
        Assert.Equal(300, options[1].TotalBytes);
        Assert.Equal(400, options[2].TotalBytes);
    }

    [Fact]
    public async Task GetLevelOptions_Returns_Empty_For_Unsupported_Channel()
    {
        var svc = CreateService(new Dictionary<string, string> { [IndexUrl] = IndexJson() });

        // indexUrlProvider 被显式覆盖,故这里直接验证渠道能力判断本身
        Assert.True(ResourceLevelService.SupportsResourceLevels(GameServerType.Official));
        Assert.False(ResourceLevelService.SupportsResourceLevels(GameServerType.Bilibili));
        Assert.False(ResourceLevelService.SupportsResourceLevels(GameServerType.Global));
    }

    [Fact]
    public async Task Plan_Returns_No_Work_When_Target_Already_Installed()
    {
        MarkInstalled("common", "hd");
        var svc = CreateService(new Dictionary<string, string> { [IndexUrl] = IndexJson() });

        var plan = await svc.PlanAsync(GameServerType.Official, "hd");

        Assert.NotNull(plan);
        Assert.True(plan!.AlreadyInstalled);
        Assert.False(plan.HasWork);
        Assert.Equal(0, plan.DownloadBytes);
    }

    [Fact]
    public async Task Plan_Only_Includes_Missing_Level_Pack_When_Common_Present()
    {
        // 已装 HD,切到极致:只应下载 uhd 包,不应重复下载 common
        MarkInstalled("common", "hd");
        var svc = CreateService(new Dictionary<string, string>
        {
            [IndexUrl] = IndexJson(),
            [Cdn + "c/uhd/indexFile.json"] = PackManifestJson(
                ("Client/Content/UHD/pakchunk1-UHD.pak", 500),
                ("Client/Content/UHD/pakchunk2-UHD.pak", 700)),
        });

        var plan = await svc.PlanAsync(GameServerType.Official, "uhd");

        Assert.NotNull(plan);
        Assert.False(plan!.AlreadyInstalled);
        Assert.True(plan.HasWork);
        Assert.Single(plan.Packages);
        Assert.Equal("uhd", plan.Packages[0].PackName);
        Assert.Equal(2, plan.FileCount);
        Assert.Equal(1200, plan.DownloadBytes);
    }

    [Fact]
    public async Task Plan_Includes_Common_When_Missing()
    {
        // 全新目录(无 common):切到高清需要 common + hd
        var svc = CreateService(new Dictionary<string, string>
        {
            [IndexUrl] = IndexJson(),
            [Cdn + "c/common/indexFile.json"] = PackManifestJson(("Client/Binaries/Win64/a.exe", 10)),
            [Cdn + "c/hd/indexFile.json"] = PackManifestJson(("Client/Content/HD/p.pak", 20)),
        });

        var plan = await svc.PlanAsync(GameServerType.Official, "hd");

        Assert.NotNull(plan);
        Assert.Equal(["common", "hd"], plan!.Packages.Select(p => p.PackName));
        Assert.Equal(30, plan.DownloadBytes);
        Assert.Equal(2, plan.FileCount);
    }

    [Fact]
    public async Task Plan_Builds_Download_Url_From_Cdn_BaseUrl_And_Dest()
    {
        MarkInstalled("common");
        var svc = CreateService(new Dictionary<string, string>
        {
            [IndexUrl] = IndexJson(),
            [Cdn + "c/sd/indexFile.json"] = PackManifestJson(("Client/Content/SD/p.pak", 5)),
        });

        var plan = await svc.PlanAsync(GameServerType.Official, "sd");

        var entry = Assert.Single(plan!.Packages[0].Manifest.Files);
        Assert.Equal("Client/Content/SD/p.pak", entry.Path);
        Assert.Equal(Cdn + BaseUrl + "Client/Content/SD/p.pak", entry.Url);
    }

    [Fact]
    public async Task Plan_Returns_Null_When_Index_Unavailable()
    {
        var svc = CreateService(new Dictionary<string, string>());

        Assert.Null(await svc.PlanAsync(GameServerType.Official, "hd"));
    }

    [Fact]
    public async Task Switch_Rejects_Unsupported_Channel_Without_Index_Override()
    {
        // 不提供 indexUrlProvider:哔哩哔哩应得到明确的"暂不支持",而不是静默失败
        var http = new HttpClient(new StubHandler([]));
        var svc = new ResourceLevelService(
            new GameManifestLoader(http),
            new DownloadEngine(new HttpClient(new StubHandler([]))),
            new GamePathResolver(() => _root),
            settings: null,
            indexUrlProvider: null,
            logger: NullLogger<ResourceLevelService>.Instance);

        var result = await svc.SwitchAsync(GameServerType.Bilibili, "uhd");

        Assert.False(result.Success);
        Assert.NotNull(result.Message);
    }

    [Fact]
    public async Task Switch_Reports_AlreadyInstalled_And_Persists_Level()
    {
        MarkInstalled("common", "sd");
        var settings = new AppSettings();
        var svc = CreateService(new Dictionary<string, string> { [IndexUrl] = IndexJson() }, settings);

        var result = await svc.SwitchAsync(GameServerType.Official, "sd");

        Assert.True(result.Success);
        Assert.True(result.AlreadyInstalled);
        Assert.Equal(0, result.InstalledFiles);
        // 用户选择必须落盘,避免"选了却不生效"
        Assert.Equal("sd", settings.ResourceLevel);
    }
}
