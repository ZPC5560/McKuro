using System.Net;
using System.Text;
using McKuro.Core.Services.Game;
using Xunit;

namespace McKuro.Tests;

/// <summary>
/// 「指定本地游戏版本」下拉的<b>候选来源</b>回归(2026-11-12 用户反馈)。
/// <para>
/// <b>症状:</b>用户用官方启动器把游戏更到 3.8.0 后,McKuro 仍显示本地 3.7.1 并提示可升级 3.8;
/// 但打开「手动指定本地游戏版本」下拉,里面<b>没有 3.8</b> —— 用户因此无法把版本记录改成实际值。
/// </para>
/// <para>
/// <b>根因:</b><see cref="GameUpdater.GetKnownVersionsAsync"/> 只读旧协议端点的
/// <c>default</c> 节点。官方自 3.8 起把正式版本发布到新版协议端点,旧端点
/// <c>default.config.version</c> 仍停在 3.7.1 —— 候选自然只到 3.7.1。
/// (检查更新早就改走新版协议了,所以会出现「能提示升级 3.8、却不能选 3.8」的不一致。)
/// </para>
/// <para>
/// 这里按两种端点分别断言:候选必须是新旧协议的<b>并集</b>,且新→旧排序。
/// </para>
/// </summary>
public sealed class GameUpdaterKnownVersionsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "McKuroKnown_" + Guid.NewGuid().ToString("N"));

    private const string OldIndexUrl = "https://old.example.com/launcher/game/G152/10003_x/index.json";
    private const string PackIndexUrl = "https://new.example.com/launcher/game/10003_x/G152/official/index.json";
    private const string Cdn = "https://cdn.example.com";

    public GameUpdaterKnownVersionsTests() => Directory.CreateDirectory(_dir);

    /// <summary>用内存响应模拟 HTTP;未命中的 URL 一律 404。</summary>
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, string> _responses;

        public FakeHandler(Dictionary<string, string> responses) => _responses = responses;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            if (_responses.TryGetValue(url, out var body))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    /// <summary>旧协议 index.json:版本滞留在 <paramref name="version"/>。</summary>
    private static string OldIndexJson(string version, string? patchConfig = null) => $$"""
    {
      "default": {
        "cdnList": [ { "url": "{{Cdn}}/", "ping": 10 } ],
        "resourcesBasePath": "game/G152/10003_x/",
        "version": "{{version}}",
        "config": { "patchConfig": [ {{patchConfig ?? ""}} ] }
      },
      "keyFileCheckList": [ "Client/Binaries/Win64/Client-Win64-Shipping.exe" ],
      "gameResourceList": {
        "resource": [
          { "dest": "Client/Binaries/Win64/Client-Win64-Shipping.exe", "md5": "abc", "size": 1024 }
        ]
      }
    }
    """;

    /// <summary>新版协议 index.json:<c>resourcePacks</c> 承载正式版本。</summary>
    private static string PackIndexJson(
        string version,
        string? patchVersions = null,
        string? predownloadVersion = null) => $$"""
    {
      "cdnList": [ { "url": "{{Cdn}}/", "ping": 10 } ],
      "resourcePacks": {
        "common": {
          "version": "{{version}}",
          "indexFile": "pack/common/indexFile.json",
          "patchConfig": [ {{patchVersions ?? ""}} ]
        },
        "hd": { "version": "{{version}}", "indexFile": "pack/hd/indexFile.json" }
      },
      "bundles": { "HD": { "resourcePacks": [ "common", "hd" ] } },
      {{(predownloadVersion is null ? "" : $$"""
      "predownload": {
        "bundles": { "HD": { "resourcePacks": [ "common", "hd" ] } },
        "resourcePacks": { "common": { "version": "{{predownloadVersion}}" } }
      },
      """)}}
      "config": { "keyFileCheckList": [ "Client/Binaries/Win64/Client-Win64-Shipping.exe" ] }
    }
    """;

    private GameUpdater MakeUpdater(Dictionary<string, string> responses, bool withPackEndpoint = true)
    {
        var http = new HttpClient(new FakeHandler(responses));
        return new GameUpdater(
            new GameManifestLoader(http),
            new DownloadEngine(http),
            new UpdateInstaller(),
            new GamePathResolver(() => _dir),
            _dir,
            // 显式指定新旧两个端点,避免单测连外网(构造器对 indexUrlProvider 的语法约定)
            indexUrlProvider: _ => OldIndexUrl,
            packIndexUrlProvider: withPackEndpoint ? _ => PackIndexUrl : null);
    }

    /// <summary>
    /// 回归主因:官方启动器把游戏更到 3.8.0、旧协议端点仍报 3.7.1 时,
    /// 下拉必须包含 3.8(否则用户「检测到 3.7.1 可升 3.8」却选不到 3.8)。
    /// </summary>
    [Fact]
    public async Task Candidates_IncludeNewProtocolVersion_WhenOldEndpointLags()
    {
        var updater = MakeUpdater(new()
        {
            [OldIndexUrl] = OldIndexJson("3.7.1"),
            [PackIndexUrl] = PackIndexJson("3.8.0"),
        });

        var versions = await updater.GetKnownVersionsAsync(GameServerType.Official);

        Assert.Contains("3.8.0", versions);
        // 旧端点版本仍是合法候选(纯老版本渠道/过渡期),不能被新协议挡掉
        Assert.Contains("3.7.1", versions);
        // 新→旧排序:3.8.0 在最前
        Assert.Equal("3.8.0", versions[0]);
    }

    /// <summary>新版协议端点不可用(404/网络)时必须回退旧协议,不能变成"没有候选"。</summary>
    [Fact]
    public async Task Candidates_FallBackToOldProtocol_WhenPackEndpointMissing()
    {
        var updater = MakeUpdater(new() { [OldIndexUrl] = OldIndexJson("3.7.1") });

        var versions = await updater.GetKnownVersionsAsync(GameServerType.Official);

        Assert.Equal(["3.7.1"], versions);
    }

    /// <summary>
    /// 新版协议把补丁表搬到了各包的 <c>patchConfig</c>;旧协议的
    /// <c>default.config.patchConfig</c> 自 3.8 起已空 → 历史版本必须从新协议取。
    /// </summary>
    [Fact]
    public async Task Candidates_IncludePatchSourceVersions_FromNewProtocol()
    {
        var updater = MakeUpdater(new()
        {
            [OldIndexUrl] = OldIndexJson("3.7.1"),
            [PackIndexUrl] = PackIndexJson(
                "3.8.0",
                patchVersions: """{ "version": "3.7.1", "indexFile": "patch/3.7.1/indexFile.json" }"""),
        });

        var versions = await updater.GetKnownVersionsAsync(GameServerType.Official);

        Assert.Contains("3.8.0", versions);
        Assert.Contains("3.7.1", versions);
        Assert.Equal(["3.8.0", "3.7.1"], versions);
    }

    /// <summary>
    /// 预载版本<b>不得</b>进候选:它是"未来版本",磁盘上还没有。
    /// 若被当已装版本写下去,检查更新会得到「已是最新」而隐藏更新入口。
    /// </summary>
    [Fact]
    public async Task Candidates_ExcludePredownloadVersion()
    {
        var updater = MakeUpdater(new()
        {
            [OldIndexUrl] = OldIndexJson("3.7.1"),
            [PackIndexUrl] = PackIndexJson("3.8.0", predownloadVersion: "3.9.0"),
        });

        var versions = await updater.GetKnownVersionsAsync(GameServerType.Official);

        Assert.Contains("3.8.0", versions);
        Assert.DoesNotContain("3.9.0", versions);
    }

    /// <summary>两个端点都拿不到时仍返回本地记录(离线可操作)。</summary>
    [Fact]
    public async Task Candidates_IncludeLocalRecord_WhenNetworkFails()
    {
        var updater = MakeUpdater([]);
        Assert.True(updater.TrySetInstalledVersion("3.6.0"));

        var versions = await updater.GetKnownVersionsAsync(GameServerType.Official);

        Assert.Equal(["3.6.0"], versions);
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
