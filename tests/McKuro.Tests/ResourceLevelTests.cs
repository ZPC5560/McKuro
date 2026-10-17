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

    // ---------- 删除资源包的安全规则 ----------

    private static HashSet<string> Installed(params string[] packs) =>
        new(packs, StringComparer.OrdinalIgnoreCase);

    /// <summary>common 是共享包(被 HD/SD/UHD 三个组合引用),一律不可删。</summary>
    [Fact]
    public void IsSharedPack_Common_IsShared_LevelPacks_AreNot()
    {
        var index = Index();

        Assert.True(ResourceLevelPlanner.IsSharedPack(index, "common"));
        Assert.True(ResourceLevelPlanner.IsSharedPack(index, "COMMON")); // 大小写不敏感
        Assert.False(ResourceLevelPlanner.IsSharedPack(index, "hd"));
        Assert.False(ResourceLevelPlanner.IsSharedPack(index, "sd"));
        Assert.False(ResourceLevelPlanner.IsSharedPack(index, "uhd"));
    }

    /// <summary>
    /// 关键安全边界:删掉「当前唯一可用等级」自己的包会让游戏落到残缺状态,必须拒绝。
    /// (这是对上游的加固 —— Haiyu 只做提示,不阻止。)
    /// </summary>
    [Fact]
    public void CanDeletePack_Rejects_When_It_Is_The_Only_Usable_Level()
    {
        var index = Index();
        var onlyUhd = Installed("common", "uhd");

        Assert.Equal(
            ResourceLevelPlanner.PackDeleteBlockReason.ActiveLevelOnly,
            ResourceLevelPlanner.CanDeletePack(index, "uhd", "uhd", onlyUhd));
        // common 即使在这种场景也仍然是"共享包"优先
        Assert.Equal(
            ResourceLevelPlanner.PackDeleteBlockReason.SharedPack,
            ResourceLevelPlanner.CanDeletePack(index, "common", "uhd", onlyUhd));
    }

    /// <summary>存在其它完整等级时,允许删当前等级的包(调用方负责回退等级)。</summary>
    [Fact]
    public void CanDeletePack_Allows_When_Another_Complete_Level_Exists()
    {
        var index = Index();

        // 三档都在:删 uhd 可以,因为 hd/sd 仍完整
        var all = Installed("common", "hd", "sd", "uhd");
        Assert.Equal(
            ResourceLevelPlanner.PackDeleteBlockReason.None,
            ResourceLevelPlanner.CanDeletePack(index, "uhd", "uhd", all));

        // 删的不是当前等级(当前 uhd,删 sd):可以
        Assert.Equal(
            ResourceLevelPlanner.PackDeleteBlockReason.None,
            ResourceLevelPlanner.CanDeletePack(index, "sd", "uhd", all));
    }

    /// <summary>共享包在<b>任何</b>场景都不可删。</summary>
    [Fact]
    public void CanDeletePack_Always_Rejects_Shared_Pack()
    {
        var index = Index();
        foreach (var current in new[] { "uhd", "hd", "sd" })
        {
            Assert.Equal(
                ResourceLevelPlanner.PackDeleteBlockReason.SharedPack,
                ResourceLevelPlanner.CanDeletePack(index, "common", current, Installed("common", "hd", "sd", "uhd")));
        }
    }

    /// <summary>回退等级取「已完整安装」的等级中排序最高者(极致 → 高清 → 流畅)。</summary>
    [Fact]
    public void FallbackLevelAfterDelete_Prefers_Highest_Complete_Level()
    {
        var index = Index();

        // 三档都在,删 uhd → 回退 hd(比 sd 高)
        Assert.Equal("hd", ResourceLevelPlanner.FallbackLevelAfterDelete(index, "uhd", Installed("common", "hd", "sd", "uhd")));
        // 只有 common+sd,删 uhd(虽然 uhd 没装)→ 回退 sd
        Assert.Equal("sd", ResourceLevelPlanner.FallbackLevelAfterDelete(index, "uhd", Installed("common", "sd")));
    }

    /// <summary>没有任何其它完整等级时返回 null(调用方据此拒绝删除)。</summary>
    [Fact]
    public void FallbackLevelAfterDelete_Returns_Null_When_No_Alternative()
    {
        var index = Index();

        Assert.Null(ResourceLevelPlanner.FallbackLevelAfterDelete(index, "uhd", Installed("common", "uhd")));
        Assert.Null(ResourceLevelPlanner.FallbackLevelAfterDelete(index, "sd", Installed("common", "sd")));
        // 只有等级包、没有 common → 没有任何等级是完整的
        Assert.Null(ResourceLevelPlanner.FallbackLevelAfterDelete(index, "uhd", Installed("hd", "uhd")));
    }

    /// <summary>IsBundleInstalled 要求该组合的全部包(含 common)都在。</summary>
    [Fact]
    public void IsBundleInstalled_Requires_All_Packs_Including_Common()
    {
        var index = Index();

        Assert.True(ResourceLevelPlanner.IsBundleInstalled(index, "HD", Installed("common", "hd")));
        Assert.False(ResourceLevelPlanner.IsBundleInstalled(index, "HD", Installed("hd"))); // 缺 common
        Assert.False(ResourceLevelPlanner.IsBundleInstalled(index, "HD", Installed("common")));
    }

    /// <summary>
    /// <b>回归(评审发现)</b>:删「与当前等级无关」的包只是在回收磁盘,
    /// <b>绝不能</b>顺手改用户的画质选择 —— 否则「回收磁盘」变成「静默改画质」。
    /// <para>
    /// 修复前:当前 SD、删 UHD 时仍会算回退(取最高的完整等级 HD)并写回设置。
    /// </para>
    /// </summary>
    [Fact]
    public void NeedsFallbackAfterDelete_Only_When_Deleting_Current_Levels_Pack()
    {
        var index = Index();
        var all = Installed("common", "hd", "sd", "uhd");

        // 当前 SD:删无关的 UHD/HD 都不应回退
        Assert.False(ResourceLevelPlanner.NeedsFallbackAfterDelete(index, "uhd", "sd", all));
        Assert.False(ResourceLevelPlanner.NeedsFallbackAfterDelete(index, "hd", "sd", all));
        // 当前 SD:删自己的包要回退
        Assert.True(ResourceLevelPlanner.NeedsFallbackAfterDelete(index, "sd", "sd", all));
        // 共享包永不触发回退
        Assert.False(ResourceLevelPlanner.NeedsFallbackAfterDelete(index, "common", "hd", all));
    }

    /// <summary>当前等级的包被删、且没有其它完整等级时,NeedsFallback 为 true 但回退为 null(调用方据此拒绝)。</summary>
    [Fact]
    public void NeedsFallbackAfterDelete_True_But_No_Target_When_Nothing_Else_Installed()
    {
        var index = Index();
        var onlyUhd = Installed("common", "uhd");

        Assert.True(ResourceLevelPlanner.NeedsFallbackAfterDelete(index, "uhd", "uhd", onlyUhd));
        Assert.Null(ResourceLevelPlanner.FallbackLevelAfterDelete(index, "uhd", onlyUhd));
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

    /// <summary>
    /// 写官方格式的安装标记(带 version)。
    /// <para>
    /// 必须带 version:官方标记本来就是 <c>{"packName":"hd","version":"3.7.0"}</c> 这个形状
    /// (实测本机 D:\Wuthering Waves\launcherDownloadConfig\hd.json 即是),
    /// 而"是否已安装"的判据已改为<b>版本一致</b>——只写 packName 的标记会被判为版本未知→需重装。
    /// </para>
    /// </summary>
    private void MarkInstalled(params string[] packs)
    {
        foreach (var p in packs)
        {
            File.WriteAllText(
                Path.Combine(_root, "launcherDownloadConfig", p + ".json"),
                $$"""{"packName":"{{p}}","version":"{{TestPackVersion}}"}""");
        }
    }

    /// <summary>测试清单里所有包的版本(见 <see cref="IndexJson"/>)。</summary>
    private const string TestPackVersion = "3.7.0";

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

    private ResourceLevelService CreateService(
        Dictionary<string, string> responses,
        AppSettings? settings = null,
        string? installedGameVersion = null)
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
            logger: NullLogger<ResourceLevelService>.Instance,
            installedVersionProvider: installedGameVersion is null ? null : () => installedGameVersion);
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

    // ---------- 删除资源包(端到端) ----------

    private string UhdManifestUrl => Cdn + "c/uhd/indexFile.json";
    private string CommonManifestUrl => Cdn + "c/common/indexFile.json";

    /// <summary>在游戏目录里实际创建文件,模拟已安装的包内容。</summary>
    private void CreatePackFiles(params (string Rel, int Bytes)[] files)
    {
        foreach (var (rel, bytes) in files)
        {
            var path = Path.Combine(_root, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, new byte[bytes]);
        }
    }

    [Fact]
    public async Task GetInstalledPacks_Lists_Installed_And_Blocks_Shared()
    {
        MarkInstalled("common", "hd", "uhd");
        var svc = CreateService(new Dictionary<string, string> { [IndexUrl] = IndexJson() });

        var packs = await svc.GetInstalledPacksAsync(GameServerType.Official);

        // 只列已安装的(sd 未装)
        Assert.Equal(["uhd", "hd", "common"], packs.Select(p => p.PackName)); // 按体积降序
        var common = packs.Single(p => p.PackName == "common");
        Assert.True(common.IsShared);
        Assert.False(common.CanDelete);
        Assert.NotNull(common.BlockReason);
    }

    /// <summary>
    /// 端到端:删 uhd 会真正删掉清单里的文件、回收字节、清掉安装标记,并在有其它完整等级时回退。
    /// </summary>
    [Fact]
    public async Task DeletePack_Removes_Files_Clears_Marker_And_Falls_Back()
    {
        MarkInstalled("common", "hd", "uhd");
        CreatePackFiles(("Client/Content/UHD/a.pak", 300), ("Client/Content/UHD/b.pak", 200));
        var settings = new AppSettings { ResourceLevel = "uhd" };
        var svc = CreateService(new Dictionary<string, string>
        {
            [IndexUrl] = IndexJson(),
            [UhdManifestUrl] = PackManifestJson(("Client/Content/UHD/a.pak", 300), ("Client/Content/UHD/b.pak", 200)),
        }, settings);

        var result = await svc.DeletePackAsync(GameServerType.Official, "uhd");

        Assert.True(result.Success, result.Message);
        Assert.Equal(2, result.DeletedFiles);
        Assert.Equal(0, result.FailedFiles);
        Assert.Equal(500, result.FreedBytes);
        // 实际文件已删
        Assert.False(File.Exists(Path.Combine(_root, "Client", "Content", "UHD", "a.pak")));
        Assert.False(File.Exists(Path.Combine(_root, "Client", "Content", "UHD", "b.pak")));
        // 安装标记已清(否则下次切换等级不会重新下载)
        Assert.False(File.Exists(Path.Combine(_root, "launcherDownloadConfig", "uhd.json")));
        // 有其它完整等级(hd)→ 自动回退
        Assert.Equal("hd", result.FallbackLevel);
        Assert.Equal("hd", settings.ResourceLevel);
    }

    /// <summary>共享包 common 必须被拒绝,且<b>不删任何文件</b>。</summary>
    [Fact]
    public async Task DeletePack_Refuses_Shared_Common_And_Deletes_Nothing()
    {
        MarkInstalled("common", "hd", "uhd");
        CreatePackFiles(("Client/Content/Common/c.pak", 100));
        var svc = CreateService(new Dictionary<string, string>
        {
            [IndexUrl] = IndexJson(),
            [CommonManifestUrl] = PackManifestJson(("Client/Content/Common/c.pak", 100)),
        });

        var result = await svc.DeletePackAsync(GameServerType.Official, "common");

        Assert.False(result.Success);
        Assert.Equal(0, result.DeletedFiles);
        Assert.True(File.Exists(Path.Combine(_root, "Client", "Content", "Common", "c.pak")));
        Assert.True(File.Exists(Path.Combine(_root, "launcherDownloadConfig", "common.json")));
    }

    /// <summary>删除当前唯一可用等级必须被拒(否则游戏可能无法正常启动)。</summary>
    [Fact]
    public async Task DeletePack_Refuses_Only_Usable_Level()
    {
        MarkInstalled("common", "uhd"); // 只有 UHD 一档完整
        CreatePackFiles(("Client/Content/UHD/a.pak", 300));
        var settings = new AppSettings { ResourceLevel = "uhd" };
        var svc = CreateService(new Dictionary<string, string>
        {
            [IndexUrl] = IndexJson(),
            [UhdManifestUrl] = PackManifestJson(("Client/Content/UHD/a.pak", 300)),
        }, settings);

        var result = await svc.DeletePackAsync(GameServerType.Official, "uhd");

        Assert.False(result.Success);
        Assert.Equal(0, result.DeletedFiles);
        Assert.True(File.Exists(Path.Combine(_root, "Client", "Content", "UHD", "a.pak")));
        Assert.Equal("uhd", settings.ResourceLevel); // 设置不变
    }

    /// <summary>未安装的包不能删(避免误删/空操作)。</summary>
    [Fact]
    public async Task DeletePack_Refuses_Not_Installed_Pack()
    {
        MarkInstalled("common", "hd"); // uhd 未装
        var svc = CreateService(new Dictionary<string, string> { [IndexUrl] = IndexJson() });

        var result = await svc.DeletePackAsync(GameServerType.Official, "uhd");

        Assert.False(result.Success);
        Assert.Equal(0, result.DeletedFiles);
    }

    /// <summary>
    /// <b>回归(端到端,评审发现)</b>:当前等级是 SD,删除无关的 UHD 时
    /// <b>不得</b>改动用户的画质等级设置。修复前这里会被写成 HD。
    /// </summary>
    [Fact]
    public async Task DeletePack_Unrelated_Pack_Does_Not_Change_Current_Level()
    {
        MarkInstalled("common", "hd", "sd", "uhd");
        CreatePackFiles(("Client/Content/UHD/a.pak", 300));
        var settings = new AppSettings { ResourceLevel = "sd" }; // 当前是 SD
        var svc = CreateService(new Dictionary<string, string>
        {
            [IndexUrl] = IndexJson(),
            [UhdManifestUrl] = PackManifestJson(("Client/Content/UHD/a.pak", 300)),
        }, settings);

        var result = await svc.DeletePackAsync(GameServerType.Official, "uhd");

        Assert.True(result.Success, result.Message);
        Assert.Equal(1, result.DeletedFiles);
        // 关键:删无关包不该回退、不该改设置
        Assert.Null(result.FallbackLevel);
        Assert.Equal("sd", settings.ResourceLevel);
    }

    /// <summary>对照:删除<b>当前等级</b>自己的包时,仍然要回退并写回设置。</summary>
    [Fact]
    public async Task DeletePack_Current_Levels_Pack_Still_Falls_Back()
    {
        MarkInstalled("common", "hd", "uhd");
        CreatePackFiles(("Client/Content/UHD/a.pak", 300));
        var settings = new AppSettings { ResourceLevel = "uhd" }; // 当前是 UHD
        var svc = CreateService(new Dictionary<string, string>
        {
            [IndexUrl] = IndexJson(),
            [UhdManifestUrl] = PackManifestJson(("Client/Content/UHD/a.pak", 300)),
        }, settings);

        var result = await svc.DeletePackAsync(GameServerType.Official, "uhd");

        Assert.True(result.Success, result.Message);
        Assert.Equal("hd", result.FallbackLevel);
        Assert.Equal("hd", settings.ResourceLevel);
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

    // ---------- 分包版本比对(对齐 Haiyu 1.4.5「材质分包更新游戏问题」) ----------

    /// <summary>写一条版本被篡改的标记,模拟"服务端已升版本、本地仍是旧版本"。</summary>
    private void MarkInstalledAtVersion(string pack, string version)
    {
        File.WriteAllText(
            Path.Combine(_root, "launcherDownloadConfig", pack + ".json"),
            $$"""{"packName":"{{pack}}","version":"{{version}}"}""");
    }

    /// <summary>common + hd 的 indexFile 响应(任一新测试需要真的加载包清单时必须提供)。</summary>
    private Dictionary<string, string> CommonAndHdManifests() => new()
    {
        [IndexUrl] = IndexJson(),
        [Cdn + "c/common/indexFile.json"] = PackManifestJson(("Client/Binaries/Win64/a.exe", 10)),
        [Cdn + "c/hd/indexFile.json"] = PackManifestJson(("Client/Content/HD/p.pak", 20)),
    };

    /// <summary>
    /// <b>核心回归:材质包只提升版本(主版本号不变)时必须判为"需要更新"。</b>
    /// <para>
    /// 这是 Haiyu 1.4.5 修的那个问题,而 McKuro 原本更严重:标记只判文件存在,
    /// 于是服务端只改材质包 version 时该包被永久判为已装好,永远不再更新。
    /// 实测线上 3.7.1 四个包 version 全部变动,而主版本比较完全看不出差异。
    /// </para>
    /// </summary>
    [Fact]
    public async Task Plan_Treats_Stale_Pack_Version_As_Not_Installed()
    {
        // 标记存在、但版本落后一个 patch —— 旧实现只看 File.Exists,会误判为已装好。
        MarkInstalledAtVersion("common", "3.6.0");
        MarkInstalledAtVersion("hd", "3.6.0");
        var svc = CreateService(CommonAndHdManifests());

        var plan = await svc.PlanAsync(GameServerType.Official, "hd");

        Assert.NotNull(plan);
        Assert.False(plan.AlreadyInstalled);
        Assert.True(plan.HasWork);
        // common 与 hd 都要重装
        Assert.Equal(["common", "hd"], plan.Packages.Select(p => p.PackName));
    }

    /// <summary>版本一致时必须仍然判为已装好,不能因为新逻辑而无谓重下几十 GB。</summary>
    [Fact]
    public async Task Plan_Treats_Matching_Pack_Version_As_Installed()
    {
        MarkInstalled("common", "hd");
        var svc = CreateService(new Dictionary<string, string> { [IndexUrl] = IndexJson() });

        var plan = await svc.PlanAsync(GameServerType.Official, "hd");

        Assert.NotNull(plan);
        Assert.True(plan.AlreadyInstalled);
        Assert.False(plan.HasWork);
    }

    /// <summary>
    /// 标记缺 version 字段(旧版 McKuro 或第三方工具写的)时按"版本未知"处理 → 需重新校核。
    /// 方向保守:宁可多下一次,也不能把旧包当成新包。
    /// </summary>
    [Fact]
    public async Task Plan_Treats_Versionless_Marker_As_Not_Installed()
    {
        File.WriteAllText(Path.Combine(_root, "launcherDownloadConfig", "hd.json"), """{"packName":"hd"}""");
        File.WriteAllText(Path.Combine(_root, "launcherDownloadConfig", "common.json"), """{"packName":"common"}""");
        var svc = CreateService(CommonAndHdManifests());

        var plan = await svc.PlanAsync(GameServerType.Official, "hd");

        Assert.NotNull(plan);
        Assert.False(plan.AlreadyInstalled);
        Assert.Equal(["common", "hd"], plan.Packages.Select(p => p.PackName));
    }

    /// <summary>损坏的标记(非 JSON)不得让整个流程崩溃,按版本未知处理。</summary>
    [Fact]
    public async Task Plan_Survives_Corrupt_Marker()
    {
        File.WriteAllText(Path.Combine(_root, "launcherDownloadConfig", "hd.json"), "<not-json>");
        var svc = CreateService(CommonAndHdManifests());

        var plan = await svc.PlanAsync(GameServerType.Official, "hd");

        Assert.NotNull(plan);
        Assert.False(plan.AlreadyInstalled);
    }

    /// <summary>版本号比较必须忽略大小写与首尾空白,避免服务端格式微调导致无谓重下。</summary>
    [Fact]
    public void IsPackInstalledAtVersion_Normalizes_Whitespace_And_Case()
    {
        MarkInstalledAtVersion("hd", " 3.7.0 ");

        Assert.True(ResourceLevelService.IsPackInstalledAtVersion(_root, "hd", "3.7.0"));
        Assert.False(ResourceLevelService.IsPackInstalledAtVersion(_root, "hd", "3.7.1"));
        Assert.False(ResourceLevelService.IsPackInstalledAtVersion(_root, "sd", "3.7.0"));
    }

    /// <summary>服务端没给版本(清单异常)时不谎报落后:有版本记录的标记即视为已装。</summary>
    [Fact]
    public void IsPackInstalledAtVersion_With_Unknown_Expected_Accepts_Recorded()
    {
        MarkInstalledAtVersion("hd", "3.7.0");

        Assert.True(ResourceLevelService.IsPackInstalledAtVersion(_root, "hd", null));
        Assert.True(ResourceLevelService.IsPackInstalledAtVersion(_root, "hd", ""));
    }

    /// <summary>已安装列表也必须按版本判定,版本落后不该被算作可用包。</summary>
    [Fact]
    public async Task GetInstalledPacks_Excludes_Stale_Version()
    {
        MarkInstalledAtVersion("common", TestPackVersion);
        MarkInstalledAtVersion("hd", "3.6.0");   // 版本落后
        MarkInstalledAtVersion("uhd", TestPackVersion);
        var svc = CreateService(new Dictionary<string, string> { [IndexUrl] = IndexJson() });

        var packs = await svc.GetInstalledPacksAsync(GameServerType.Official);

        var names = packs.Select(p => p.PackName).ToList();
        Assert.Contains("common", names);
        Assert.Contains("uhd", names);
        Assert.DoesNotContain("hd", names);
    }

    // ---------- 老用户误报抑制(用户明确要求:已更新过版本的不要再提醒) ----------

    /// <summary>
    /// <b>核心需求:老用户的标记停在旧版本、但主版本已经追平时,不得再提示材质包更新。</b>
    /// <para>
    /// 场景来由:McKuro 早先只有「切换画质」会写标记,用主流程一路更新上来的用户标记会永久停在
    /// 他们上次切画质时的版本(实测本机:主版本 3.7.1,而 hd.json 仍记着 3.7.0)。
    /// 若不做抑制,这些老用户会被提示重新下载 45 GB —— 而文件其实早已是新的。
    /// </para>
    /// </summary>
    [Fact]
    public async Task Plan_Does_Not_Report_Stale_Marker_When_Main_Version_Caught_Up()
    {
        // 标记停在 3.6.0,但本地主版本已经是 3.7.0(= 线上包版本)
        MarkInstalledAtVersion("common", "3.6.0");
        MarkInstalledAtVersion("hd", "3.6.0");
        var svc = CreateService(
            new Dictionary<string, string> { [IndexUrl] = IndexJson() },
            installedGameVersion: TestPackVersion);

        var plan = await svc.PlanAsync(GameServerType.Official, "hd");

        Assert.NotNull(plan);
        Assert.True(plan.AlreadyInstalled);
        Assert.False(plan.HasWork);
        Assert.Empty(plan.Packages);
    }

    /// <summary>
    /// 老用户方案下,"是否已安装"面板同样要显示为已安装(不能一边说没问题、一边显示未安装)。
    /// </summary>
    [Fact]
    public async Task GetLevelOptions_Marks_Installed_When_Main_Version_Caught_Up()
    {
        MarkInstalledAtVersion("common", "3.6.0");
        MarkInstalledAtVersion("hd", "3.6.0");
        var svc = CreateService(
            new Dictionary<string, string> { [IndexUrl] = IndexJson() },
            installedGameVersion: TestPackVersion);

        var options = await svc.GetLevelOptionsAsync(GameServerType.Official);

        Assert.True(options.Single(o => o.Value == "hd").Installed);
    }

    /// <summary>
    /// <b>抑制不能过头:主版本还落后时,标记落后仍然要判为需要更新。</b>
    /// 否则就退化成"永远不更新材质包",和修复前的 bug 一模一样。
    /// </summary>
    [Fact]
    public async Task Plan_Still_Reports_Stale_Marker_When_Main_Version_Behind()
    {
        MarkInstalledAtVersion("common", "3.6.0");
        MarkInstalledAtVersion("hd", "3.6.0");
        var svc = CreateService(
            IntHd(),
            installedGameVersion: "3.6.0"); // 主版本也还是旧的

        var plan = await svc.PlanAsync(GameServerType.Official, "hd");

        Assert.NotNull(plan);
        Assert.False(plan.AlreadyInstalled);
        Assert.True(plan.HasWork);
        Assert.Equal(["common", "hd"], plan.Packages.Select(p => p.PackName));
    }

    /// <summary>
    /// 抑制只作用于"标记陈旧"。标记<b>缺失</b>(从未装过该包)时必须照常下载,
    /// 不能因为主版本追平就认为材质包存在。
    /// </summary>
    [Fact]
    public async Task Plan_Still_Downloads_Pack_When_Marker_Missing()
    {
        // 只标了 common,hd 从未安装过
        MarkInstalledAtVersion("common", "3.6.0");
        var svc = CreateService(
            new Dictionary<string, string>
            {
                [IndexUrl] = IndexJson(),
                [Cdn + "c/hd/indexFile.json"] = PackManifestJson(("Client/Content/HD/p.pak", 20)),
            },
            installedGameVersion: TestPackVersion);

        var plan = await svc.PlanAsync(GameServerType.Official, "hd");

        Assert.NotNull(plan);
        Assert.False(plan.AlreadyInstalled);
        Assert.Equal(["hd"], plan.Packages.Select(p => p.PackName));
    }

    /// <summary>主版本未知(读取失败)时不启用抑制,保持保守判定。</summary>
    [Fact]
    public async Task Plan_Does_Not_Suppress_When_Installed_Version_Unknown()
    {
        MarkInstalledAtVersion("common", "3.6.0");
        MarkInstalledAtVersion("hd", "3.6.0");
        var svc = CreateService(IntHd(), installedGameVersion: null);

        var plan = await svc.PlanAsync(GameServerType.Official, "hd");

        Assert.NotNull(plan);
        Assert.False(plan.AlreadyInstalled);
    }

    /// <summary>common + hd 的 indexFile。</summary>
    private Dictionary<string, string> IntHd() => new()
    {
        [IndexUrl] = IndexJson(),
        [Cdn + "c/common/indexFile.json"] = PackManifestJson(("Client/Binaries/Win64/a.exe", 10)),
        [Cdn + "c/hd/indexFile.json"] = PackManifestJson(("Client/Content/HD/p.pak", 20)),
    };

    [Theory]
    [InlineData("3.7.0", "3.7.0", 0)]
    [InlineData("3.7.1", "3.7.0", 1)]
    [InlineData("3.7.0", "3.7.1", -1)]
    [InlineData("3.7", "3.7.0", 0)]      // 缺失段按 0 补齐(不能用 Version 类的 -1 语义)
    [InlineData("3.7.1", "3.7", 1)]
    [InlineData("3.10.0", "3.9.0", 1)]   // 数值比较而非字符串比较
    public void ComparePackVersions_Is_Numeric(string left, string right, int expected)
    {
        Assert.Equal(expected, Math.Sign(ResourceLevelService.ComparePackVersions(left, right)));
    }

    [Fact]
    public void ComparePackVersions_Falls_Back_To_String_For_Unparsable()
    {
        // 解析不了时退化成字符串比较,不崩
        Assert.Equal(0, ResourceLevelService.ComparePackVersions("abc", "abc"));
        Assert.NotEqual(0, Math.Sign(ResourceLevelService.ComparePackVersions("abc", "abd")));
        Assert.Equal(0, ResourceLevelService.ComparePackVersions(null, ""));
    }
}
