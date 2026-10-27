using System.Text.Json;
using McKuro.Core.Models.Game;
using McKuro.Core.Services.Game;
using McKuro.Core.Services.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace McKuro.Tests;

/// <summary>
/// 官方启动器安装记录回写(对齐 Haiyu a7867c68 / 48887aa1)。
/// <para>
/// 这组测试守的是<b>用户数据安全</b>而不是 UI:写错/写一半会让官方启动器按半套记录去算增量,
/// 比完全不写更糟;把服务器版本当本地版本写进去,会让"其实还旧着的文件"被判成最新。
/// </para>
/// </summary>
public class OfficialLauncherRecordServiceTests : IDisposable
{
    private const string IndexUrl =
        "https://prod-cn-alicdn-gamestarter.kurogame.com/launcher/game/"
        + "10003_oLNgHF1CESo51DGHN2odtp40e3oI1HfZ/G152/official/index.json";

    private const string Cdn = "https://cdn.example.com/";

    private readonly string _root;
    private readonly AppSettings _settings = new();

    public OfficialLauncherRecordServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "McKuro-offrec-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _settings.GameRootDir = _root;
        _settings.ServerType = GameServerType.Official;
        _settings.ResourceLevel = "hd";
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    // ---------- 夹具 ----------

    private static string IndexJson() => $$"""
    {
      "cdnList":[{"url":"{{Cdn}}","P":1}],
      "resourcePacks":{
        "common":{"version":"3.7.1","indexFile":"c/common.json","baseUrl":"zip/","size":100},
        "hd":{"version":"3.7.1","indexFile":"c/hd.json","baseUrl":"zip/","size":200},
        "sd":{"version":"3.7.1","indexFile":"c/sd.json","baseUrl":"zip/","size":300},
        "uhd":{"version":"3.7.1","indexFile":"c/uhd.json","baseUrl":"zip/","size":400}
      },
      "bundles":{
        "HD":{"resourcePacks":["common","hd"]},
        "SD":{"resourcePacks":["common","sd"]},
        "UHD":{"resourcePacks":["common","uhd"]}
      }
    }
    """;

    private static string PackJson(params (string Dest, int Size)[] files)
    {
        var entries = string.Join(",", files.Select(f =>
            $$"""{"dest":"{{f.Dest}}","size":{{f.Size}},"md5":"{{new string('a', 32)}}"}"""));
        return $$"""{"resource":[{{entries}}]}""";
    }

    private OfficialLauncherRecordService CreateService(Dictionary<string, string>? responses = null)
    {
        var handler = new StubHandler(responses ?? []);
        var http = new HttpClient(handler);
        return new OfficialLauncherRecordService(
            new GamePathResolver(() => _settings.GameRootDir),
            new GameManifestLoader(http),
            new StubSettings(_settings),
            NullLogger<OfficialLauncherRecordService>.Instance);
    }

    /// <summary>写一条与服务器版本一致的官方分包标记(等价于"这个包真的装好了")。</summary>
    private void MarkInstalled(params string[] packs)
    {
        var dir = Path.Combine(_root, "launcherDownloadConfig");
        Directory.CreateDirectory(dir);
        foreach (var pack in packs)
        {
            File.WriteAllText(
                Path.Combine(dir, pack + ".json"),
                $$"""{"packName":"{{pack}}","version":"3.7.1"}""");
        }
    }

    private void CreateGameExe() => File.WriteAllText(Path.Combine(_root, GamePathResolver.ExeRootName), "");

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
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(body),
                });
            }
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        }
    }

    // ---------- 定位 ----------

    [Fact]
    public void Locate_Rejects_Missing_Folder()
    {
        _settings.GameRootDir = Path.Combine(_root, "not-exists");

        var result = CreateService().LocateGameFolder();

        Assert.Equal(OfficialRecordLocateState.GameFolderMissing, result.State);
    }

    [Fact]
    public void Locate_Rejects_Empty_Folder()
    {
        var result = CreateService().LocateGameFolder();

        Assert.Equal(OfficialRecordLocateState.GameFolderEmpty, result.State);
    }

    [Fact]
    public void Locate_Rejects_Folder_Without_Game_Exe()
    {
        // 目录非空但没有主程序:官方判定也看 exe,写记录无意义
        MarkInstalled("hd");

        var result = CreateService().LocateGameFolder();

        Assert.Equal(OfficialRecordLocateState.GameExeMissing, result.State);
    }

    [Fact]
    public void Locate_Reports_Existing_Bundles()
    {
        CreateGameExe();
        File.WriteAllText(
            Path.Combine(_root, OfficialLauncherRecordService.InstalledConfigFileName),
            """{"appId":"10003","bundles":{"SD":{"version":"3.7.0","resourcePacks":["common","sd"]}}}""");

        var result = CreateService().LocateGameFolder();

        Assert.Equal(OfficialRecordLocateState.Ready, result.State);
        Assert.True(result.HasExistingRecord);
        Assert.Contains("SD", result.ExistingBundles);
    }

    // ---------- 组装 ----------

    [Fact]
    public async Task Build_Returns_Null_When_No_Bundle_Fully_Installed()
    {
        CreateGameExe();
        MarkInstalled("common"); // 只有 common:HD 缺 hd、SD 缺 sd
        var svc = CreateService(new() { [IndexUrl] = IndexJson() });

        Assert.Null(await svc.BuildInstallRecordAsync(GameServerType.Official));
    }

    [Fact]
    public async Task Build_Returns_Null_For_Channel_Without_Bundle_Protocol()
    {
        CreateGameExe();
        MarkInstalled("common", "hd");

        // WeGame 没有该端点 → 组装必然为 null(UI 据此提示"该渠道不需要写这份记录")
        Assert.Null(await CreateService().BuildInstallRecordAsync(GameServerType.WeGame));
    }

    [Fact]
    public async Task Build_Uses_Local_Marker_Versions_Not_Server_Version()
    {
        CreateGameExe();
        MarkInstalled("common", "hd");
        // 服务端已升到 3.7.1,本地标记停在 3.7.0:版本必须写本地值,
        // 否则官方启动器会以为"已最新",真正的更新永远不触发。
        File.WriteAllText(
            Path.Combine(_root, "launcherDownloadConfig", "hd.json"),
            """{"packName":"hd","version":"3.7.1"}""");
        File.WriteAllText(
            Path.Combine(_root, "launcherDownloadConfig", "common.json"),
            """{"packName":"common","version":"3.7.0"}""");
        var svc = CreateService(new() { [IndexUrl] = IndexJson() });

        var record = await svc.BuildInstallRecordAsync(GameServerType.Official);

        Assert.NotNull(record);
        Assert.Equal("10003", record.AppId);
        Assert.Equal("HD", record.BundleName);
        Assert.Equal("3.7.0", record.Packs.Single(p => p.Name == "common").Version);
        Assert.Equal("3.7.1", record.Packs.Single(p => p.Name == "hd").Version);
    }

    [Fact]
    public async Task Build_Registers_Additional_Installed_Bundles()
    {
        CreateGameExe();
        MarkInstalled("common", "hd", "uhd"); // HD 与 UHD 都完整
        var svc = CreateService(new() { [IndexUrl] = IndexJson() });

        var record = await svc.BuildInstallRecordAsync(GameServerType.Official);

        Assert.NotNull(record);
        Assert.Equal("HD", record.BundleName); // 主组合 = 设置里的等级(hd)
        var additional = Assert.Single(record.AdditionalBundles);
        Assert.Equal("UHD", additional.BundleName);
        Assert.Equal(["common", "uhd"], additional.Packs.Select(p => p.Name));
    }

    [Fact]
    public async Task Build_Falls_Back_To_An_Installed_Bundle_When_Setting_Level_Not_Installed()
    {
        CreateGameExe();
        MarkInstalled("common", "sd"); // 设置是 hd,但只装了 SD
        var svc = CreateService(new() { [IndexUrl] = IndexJson() });

        var record = await svc.BuildInstallRecordAsync(GameServerType.Official);

        Assert.NotNull(record);
        Assert.Equal("SD", record.BundleName);
        Assert.Empty(record.AdditionalBundles);
    }

    // ---------- 写入 ----------

    [Fact]
    public async Task Write_Creates_Top_Level_Record_And_Pack_Versions()
    {
        CreateGameExe();
        MarkInstalled("common", "hd");
        var svc = CreateService(new() { [IndexUrl] = IndexJson() });
        var record = await svc.BuildInstallRecordAsync(GameServerType.Official);

        var result = await svc.WriteInstallRecordAsync(_root, record!);

        Assert.False(result.HasFailed);
        // 1 个顶层 + 2 个分包(common/hd);common/hd 的标记内容已一致 → 幂等计成功
        Assert.Contains(OfficialLauncherRecordService.InstalledConfigFileName, result.WrittenFiles);

        var config = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(_root, OfficialLauncherRecordService.InstalledConfigFileName)));
        Assert.Equal("10003", config.RootElement.GetProperty("appId").GetString());
        var hd = config.RootElement.GetProperty("bundles").GetProperty("HD");
        Assert.Equal("3.7.1", hd.GetProperty("version").GetString());
        Assert.Equal(["common", "hd"], hd.GetProperty("resourcePacks").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task Write_Merges_Instead_Of_Overwriting_Existing_Bundles()
    {
        CreateGameExe();
        MarkInstalled("common", "hd");
        // 目录里已有官方自己写的 SD 节点:不能因为本次只写 HD 就把它抹掉
        File.WriteAllText(
            Path.Combine(_root, OfficialLauncherRecordService.InstalledConfigFileName),
            """{"appId":"10003","version":"3.7.0","bundles":{"SD":{"version":"3.6.0","resourcePacks":["common","sd"]}}}""");
        var svc = CreateService(new() { [IndexUrl] = IndexJson() });
        var record = await svc.BuildInstallRecordAsync(GameServerType.Official);

        await svc.WriteInstallRecordAsync(_root, record!);

        var config = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(_root, OfficialLauncherRecordService.InstalledConfigFileName)));
        var bundles = config.RootElement.GetProperty("bundles");
        Assert.True(bundles.TryGetProperty("SD", out var sd));
        Assert.Equal("3.6.0", sd.GetProperty("version").GetString()); // 旧节点的版本保持原样
        Assert.True(bundles.TryGetProperty("HD", out _));
    }

    [Fact]
    public async Task Write_Leaves_Folder_Untouched_When_Staging_Fails()
    {
        CreateGameExe();
        MarkInstalled("common", "hd");
        var svc = CreateService(new() { [IndexUrl] = IndexJson() });
        var record = await svc.BuildInstallRecordAsync(GameServerType.Official);
        Assert.NotNull(record);

        // 用一个"存在但不可写"的伪装:把顶层记录路径占用成目录,让 File.Move 必然失败
        var configPath = Path.Combine(_root, OfficialLauncherRecordService.InstalledConfigFileName);
        Directory.CreateDirectory(configPath);
        try
        {
            var result = await svc.WriteInstallRecordAsync(_root, record!);

            // 生成阶段能写 tmp,替换阶段失败 → 必须回滚且报告失败(不能留下半套)
            Assert.True(result.HasFailed);
            // 失败后不应留下任何 *.tmp 残留
            Assert.Empty(Directory.GetFiles(_root, "*.tmp", SearchOption.AllDirectories));
        }
        finally
        {
            Directory.Delete(configPath, true);
        }
    }

    [Fact]
    public async Task Write_Is_Idempotent()
    {
        CreateGameExe();
        MarkInstalled("common", "hd");
        var svc = CreateService(new() { [IndexUrl] = IndexJson() });
        var record = await svc.BuildInstallRecordAsync(GameServerType.Official);

        await svc.WriteInstallRecordAsync(_root, record!);
        var first = File.ReadAllText(
            Path.Combine(_root, OfficialLauncherRecordService.InstalledConfigFileName));
        var second = await svc.WriteInstallRecordAsync(_root, record!);

        Assert.False(second.HasFailed);
        Assert.Equal(
            first,
            File.ReadAllText(Path.Combine(_root, OfficialLauncherRecordService.InstalledConfigFileName)));
    }

    // ---------- 资源清单 ----------

    [Fact]
    public async Task WriteResourceIndex_Merges_Bundles_And_Dedupes_By_Dest()
    {
        CreateGameExe();
        MarkInstalled("common", "hd", "uhd");
        var svc = CreateService(new()
        {
            [IndexUrl] = IndexJson(),
            [Cdn + "c/common.json"] = PackJson(("Client/Content/common.pak", 10)),
            [Cdn + "c/hd.json"] = PackJson(("Client/Content/HD/a.pak", 20)),
            [Cdn + "c/uhd.json"] = PackJson(("Client/Content/UHD/b.pak", 30)),
        });
        var record = await svc.BuildInstallRecordAsync(GameServerType.Official);

        var result = await svc.WriteResourceIndexAsync(_root, record!);

        Assert.False(result.HasFailed);
        var doc = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(_root, OfficialLauncherRecordService.LocalIndexFileName)));
        var dests = doc.RootElement.GetProperty("resource").EnumerateArray()
            .Select(e => e.GetProperty("dest").GetString())
            .ToList();

        // common 被 HD/UHD 共享,只能出现一次;两个画质独有文件都保留
        Assert.Equal(3, dests.Count);
        Assert.Single(dests, d => d == "Client/Content/common.pak");
        Assert.Contains("Client/Content/HD/a.pak", dests);
        Assert.Contains("Client/Content/UHD/b.pak", dests);
    }

    [Fact]
    public async Task WriteResourceIndex_Fails_Gracefully_When_Manifests_Unavailable()
    {
        CreateGameExe();
        MarkInstalled("common", "hd");
        // 只给索引,不给分包清单 → 拉不到任何文件
        var svc = CreateService(new() { [IndexUrl] = IndexJson() });
        var record = await svc.BuildInstallRecordAsync(GameServerType.Official);

        var result = await svc.WriteResourceIndexAsync(_root, record!);

        Assert.True(result.HasFailed);
        // 关键:不能写成空清单文件糊弄官方(那会让官方误判"目录里什么都没有")
        Assert.False(File.Exists(Path.Combine(_root, OfficialLauncherRecordService.LocalIndexFileName)));
    }

    // ---------- 移除 ----------

    [Fact]
    public void Remove_Deletes_Records_But_Keeps_Directory_And_Other_Files()
    {
        CreateGameExe();
        var markerDir = Path.Combine(_root, OfficialLauncherRecordService.PackVersionDirName);
        Directory.CreateDirectory(markerDir);
        File.WriteAllText(Path.Combine(markerDir, "hd.json"), """{"packName":"hd","version":"3.7.1"}""");
        File.WriteAllText(Path.Combine(markerDir, "keep.txt"), "official own file");
        File.WriteAllText(
            Path.Combine(_root, OfficialLauncherRecordService.InstalledConfigFileName),
            """{"appId":"10003"}""");
        File.WriteAllText(Path.Combine(_root, OfficialLauncherRecordService.LocalIndexFileName), """{"resource":[]}""");
        var svc = CreateService();

        var result = svc.RemoveRecords(_root);

        Assert.False(result.HasFailed);
        Assert.False(File.Exists(Path.Combine(_root, OfficialLauncherRecordService.InstalledConfigFileName)));
        Assert.False(File.Exists(Path.Combine(_root, OfficialLauncherRecordService.LocalIndexFileName)));
        Assert.False(File.Exists(Path.Combine(markerDir, "hd.json")));
        // 目录本身与非记录内容必须保留
        Assert.True(Directory.Exists(markerDir));
        Assert.True(File.Exists(Path.Combine(markerDir, "keep.txt")));
        Assert.False(svc.HasAnyRecord(_root));
    }
}
