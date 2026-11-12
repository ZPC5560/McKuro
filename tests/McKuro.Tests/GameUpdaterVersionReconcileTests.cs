using System.Net;
using System.Text;
using System.Text.Json;
using McKuro.Core.Services.Game;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace McKuro.Tests;

/// <summary>
/// 「官方启动器更新后 McKuro 仍报有更新」的回归(2026-11-12 用户反馈)。
/// <para>
/// <b>症状:</b>用户用官方启动器把游戏更到 3.8.0,McKuro 仍显示本地 <c>3.7.1</c> 并提示
/// 「可升级 3.8」—— 点更新等于把已经装好的几十 GB 重下一遍。
/// </para>
/// <para>
/// <b>根因:</b><c>installed_versions</c> 只在 McKuro 自己的安装/修复收口处写,
/// 外部渠道(官方启动器)更新不会碰它 → 记录永远滞后。而官方启动器会在
/// <c>&lt;游戏目录&gt;/launcherDownloadConfig/{pack}.json</c> 写下它实际装好的包版本,
/// 那是这台机器上关于"已装版本"最可信的证据。
/// </para>
/// <para>
/// <b>校正的边界</b>(这里逐条钉死,防止把"没装好"误判成"已最新"导致更新入口消失):
/// 必须每个包都有标记、版本完全一致、能解析、且<b>比本地记录新</b>;
/// 并且不能比线上版本还新(预载/灰度)。
/// </para>
/// </summary>
public sealed class GameUpdaterVersionReconcileTests : IDisposable
{
    private const string IndexUrl = "https://cdn.test/launcher/game/G152/index.json";
    private const string CdnBase = "https://cdn.test/launcher/";
    private const string ServerVersion = "3.8.0";

    private readonly string _tmpDir;
    private readonly string _gameRoot;

    public GameUpdaterVersionReconcileTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "McKuro_rec_" + Guid.NewGuid().ToString("N"));
        _gameRoot = Path.Combine(Path.GetTempPath(), "McKuro_rec_game_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmpDir);
        Directory.CreateDirectory(_gameRoot);
        // 关键文件落盘,让 CheckUpdate 走"已安装"分支(而不是"未安装")
        Directory.CreateDirectory(Path.Combine(_gameRoot, "Client", "Binaries", "Win64"));
        File.WriteAllText(
            Path.Combine(_gameRoot, "Client", "Binaries", "Win64", "Client-Win64-Shipping.exe"),
            "stub");
        // 根 exe 是 IsGameInstalled 的判据;缺它会被当成"未安装",永远按"有更新"处理
        File.WriteAllText(Path.Combine(_gameRoot, GamePathResolver.ExeRootName), "stub");
    }

    /// <summary>写官方启动器的分包标记(<c>launcherDownloadConfig/{pack}.json</c>)。</summary>
    private void WriteOfficialMarker(string packName, string? version)
    {
        var dir = Path.Combine(_gameRoot, "launcherDownloadConfig");
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(new { packName, version });
        File.WriteAllText(Path.Combine(dir, packName + ".json"), json);
    }

    private void WriteLocalRecord(string version)
    {
        var marker = Path.Combine(_tmpDir, "installed_versions.json");
        var root = GameUpdater.NormalizeRoot(_gameRoot);
        File.WriteAllText(marker, JsonSerializer.Serialize(new Dictionary<string, string> { [root] = version }));
    }

    private sealed class MapHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, string> _responses;

        public MapHandler(Dictionary<string, string> responses) => _responses = responses;

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

    private GameUpdater CreateUpdater(string serverVersion = ServerVersion)
    {
        var index = $$"""
        {
          "default": {
            "cdnList": [ { "url": "{{CdnBase}}", "ping": 10, "P": 1 } ],
            "resourcesBasePath": "game/G152/",
            "version": "{{serverVersion}}"
          },
          "keyFileCheckList": [ "Client/Binaries/Win64/Client-Win64-Shipping.exe" ],
          "gameResourceList": {
            "resource": [
              { "dest": "Client/Binaries/Win64/Client-Win64-Shipping.exe", "md5": "abc", "size": 4 }
            ]
          }
        }
        """;
        var handler = new MapHandler(new() { [IndexUrl] = index });
        return new GameUpdater(
            new GameManifestLoader(new HttpClient(handler), NullLogger.Instance),
            new DownloadEngine(new HttpClient(handler)),
            new UpdateInstaller(),
            new GamePathResolver(() => _gameRoot),
            _tmpDir,
            indexUrlProvider: _ => IndexUrl,
            packIndexUrlProvider: null);
    }

    /// <summary>
    /// 主场景:记录 3.7.1、官方标记已是 3.8.0 → 必须判「已是最新」并把记录校正到 3.8.0。
    /// </summary>
    [Fact]
    public async Task OfficialMarkersNewer_ReconcilesRecord_AndNoUpdateReported()
    {
        WriteLocalRecord("3.7.1");
        WriteOfficialMarker("common", "3.8.0");
        WriteOfficialMarker("hd", "3.8.0");

        var result = await CreateUpdater().CheckUpdateAsync(GameServerType.Official);

        Assert.True(result.Success, result.Message);
        Assert.False(result.HasUpdate, "游戏其实已是 3.8.0,不能再报有更新");
        Assert.Equal("3.8.0", result.InstalledVersion);
    }

    /// <summary>确实落后时仍要报更新:标记是 3.7.1、线上 3.8.0。</summary>
    [Fact]
    public async Task OfficialMarkersOlder_StillReportsUpdate()
    {
        WriteLocalRecord("3.7.1");
        WriteOfficialMarker("common", "3.7.1");
        WriteOfficialMarker("hd", "3.7.1");

        var result = await CreateUpdater().CheckUpdateAsync(GameServerType.Official);

        Assert.True(result.HasUpdate);
        Assert.Equal("3.7.1", result.InstalledVersion);
    }

    /// <summary>
    /// 某个包缺标记(装到一半/官方只写了一部分)时不可信,不得据此宣布已最新。
    /// </summary>
    [Fact]
    public async Task MissingPackMarker_DoesNotReconcile()
    {
        WriteLocalRecord("3.7.1");
        WriteOfficialMarker("common", "3.8.0");
        // 没有 hd.json

        var result = await CreateUpdater().CheckUpdateAsync(GameServerType.Official);

        Assert.Equal("3.7.1", result.InstalledVersion);
    }

    /// <summary>各包版本不一致 = 更新中断,不得校正。</summary>
    [Fact]
    public async Task DivergentMarkers_DoNotReconcile()
    {
        WriteLocalRecord("3.7.1");
        WriteOfficialMarker("common", "3.8.0");
        WriteOfficialMarker("hd", "3.7.1");

        var result = await CreateUpdater().CheckUpdateAsync(GameServerType.Official);

        Assert.Equal("3.7.1", result.InstalledVersion);
    }

    /// <summary>标记比线上还新(预载/灰度)时不能据此判「已最新」,否则更新入口会消失。</summary>
    [Fact]
    public async Task MarkersNewerThanServer_DoNotReconcile()
    {
        WriteLocalRecord("3.7.1");
        WriteOfficialMarker("common", "3.9.0");
        WriteOfficialMarker("hd", "3.9.0");

        var result = await CreateUpdater().CheckUpdateAsync(GameServerType.Official);

        // 记录保持不动,并如实报"有更新"(交给正常流程)
        Assert.Equal("3.7.1", result.InstalledVersion);
        Assert.True(result.HasUpdate);
    }

    /// <summary>标记版本不合法(脏值)时不校正。</summary>
    [Fact]
    public async Task MalformedMarkerVersion_DoesNotReconcile()
    {
        WriteLocalRecord("3.7.1");
        WriteOfficialMarker("common", "3.8.0-hotfix");
        WriteOfficialMarker("hd", "3.8.0-hotfix");

        var result = await CreateUpdater().CheckUpdateAsync(GameServerType.Official);

        Assert.Equal("3.7.1", result.InstalledVersion);
    }

    /// <summary>标记<b>更旧</b>时不得把记录改旧(旧标记只可能是残留)。</summary>
    [Fact]
    public async Task MarkersOlderThanRecord_DoNotDowngradeRecord()
    {
        WriteLocalRecord("3.8.0");
        WriteOfficialMarker("common", "3.7.0");
        WriteOfficialMarker("hd", "3.7.0");

        var result = await CreateUpdater().CheckUpdateAsync(GameServerType.Official);

        Assert.Equal("3.8.0", result.InstalledVersion);
        Assert.False(result.HasUpdate);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmpDir, recursive: true); } catch (IOException) { }
        try { Directory.Delete(_gameRoot, recursive: true); } catch (IOException) { }
    }
}
