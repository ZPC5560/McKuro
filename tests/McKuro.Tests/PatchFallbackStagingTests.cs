using System.Net;
using System.Text;
using System.Text.Json;
using McKuro.Core.Infrastructure;
using McKuro.Core.Services.Game;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace McKuro.Tests;

/// <summary>
/// M2:补丁安装失败时预载包的去留。
/// <list type="bullet">
/// <item>失败发生在<b>动游戏目录之前</b>(这里用"压缩包不存在"模拟,与缺 hpatchz、空间预检失败同类):
///       预载包仍然有效,完整回退失败时必须保留,下次还能直接打补丁;</item>
/// <item>完整回退成功、已装到目标版本:预载包过期,清理掉。</item>
/// </list>
/// </summary>
public sealed class PatchFallbackStagingTests : IDisposable
{
    private const string IndexUrl = "https://cdn.test/launcher/game/G152/index.json";
    private const string CdnBase = "https://cdn.test/launcher/";
    private const string BasePath = "game/G152/";
    private const string PatchIndexUrl = "https://cdn.test/launcher/patch/indexFile.json";
    private const string FilePath = "Client/Data/a.pak";

    private readonly string _tmpDir;
    private readonly string _gameRoot;
    private readonly AppDatabase _db;

    public PatchFallbackStagingTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "McKuro_pfs_" + Guid.NewGuid().ToString("N"));
        _gameRoot = Path.Combine(Path.GetTempPath(), "McKuro_pfs_game_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmpDir);
        Directory.CreateDirectory(_gameRoot);
        _db = new AppDatabase(_tmpDir);
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_tmpDir, recursive: true); } catch { /* ignore */ }
        try { Directory.Delete(_gameRoot, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public async Task PatchFailsBeforeTouchingGame_AndFullFallbackFails_KeepsPredownload()
    {
        var staging = WriteCompletedPredownload();
        // CDN 上没有目标文件 → 完整回退下载失败
        var updater = CreateUpdater(serveFile: false);

        var result = await updater.InstallAsync(GameServerType.Official, ct: CancellationToken.None);

        Assert.False(result.Success);
        Assert.True(Directory.Exists(staging), "补丁没动过游戏目录,预载包不能被删");
        var meta = ReadMeta(staging);
        Assert.True(meta.Completed, "预载包仍应保持可安装");
        Assert.Equal("2.0.0", meta.SourceVersion);
    }

    [Fact]
    public async Task PatchFailsBeforeTouchingGame_AndFullFallbackSucceeds_CleansPredownload()
    {
        var staging = WriteCompletedPredownload();
        var updater = CreateUpdater(serveFile: true);

        var result = await updater.InstallAsync(GameServerType.Official, ct: CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.Equal("2.1.0", _db.GetInstalledVersion(GameUpdater.NormalizeRoot(_gameRoot)));
        Assert.False(Directory.Exists(staging), "已按完整清单装到目标版本,过期的预载包应清理");
    }

    private static readonly byte[] FileBytes = Encoding.UTF8.GetBytes("a-2.1.0");

    private GameUpdater CreateUpdater(bool serveFile)
    {
        _db.SetInstalledVersion(GameUpdater.NormalizeRoot(_gameRoot), "2.0.0");
        var md5 = Convert.ToHexStringLower(System.Security.Cryptography.MD5.HashData(FileBytes));
        var index = $$"""
        {
          "default": {
            "cdnList": [ { "url": "{{CdnBase}}", "ping": 10, "P": 1 } ],
            "resourcesBasePath": "{{BasePath}}",
            "version": "2.1.0"
          },
          "gameResourceList": { "resource": [ { "dest": "{{FilePath}}", "md5": "{{md5}}", "size": {{FileBytes.Length}} } ] }
        }
        """;
        // 补丁计划只有一个 krzip,而预载目录里故意没有它:PatchInstaller 在解压前就失败(未写游戏目录)
        const string patchIndex = """{ "resource": [ { "dest": "update.krzip", "md5": "", "size": 1 } ] }""";
        var responses = new Dictionary<string, byte[]>
        {
            [IndexUrl] = Encoding.UTF8.GetBytes(index),
            [PatchIndexUrl] = Encoding.UTF8.GetBytes(patchIndex),
        };
        if (serveFile)
        {
            responses[CdnBase + BasePath + FilePath] = FileBytes;
        }
        var handler = new MapHandler(responses);
        return new GameUpdater(
            new GameManifestLoader(new HttpClient(handler)),
            new DownloadEngine(new HttpClient(handler), maxConcurrency: 2),
            new UpdateInstaller(),
            new GamePathResolver(() => _gameRoot),
            _tmpDir,
            _db,
            indexUrlProvider: _ => IndexUrl,
            logger: NullLogger<GameUpdater>.Instance);
    }

    private string WriteCompletedPredownload()
    {
        var staging = Path.Combine(_gameRoot, GamePathResolver.DiffDataDirName, "2.1.0");
        Directory.CreateDirectory(staging);
        var meta = new PreDownloadMeta
        {
            Version = "2.1.0",
            SourceVersion = "2.0.0",
            ServerType = GameServerType.Official,
            Completed = true,
            PatchIndexUrl = PatchIndexUrl,
            DownloadBaseUrl = CdnBase + "patch/",
        };
        File.WriteAllText(
            Path.Combine(staging, "predownload.json"),
            JsonSerializer.Serialize(meta, GameMetaJsonContext.Default.PreDownloadMeta));
        return staging;
    }

    private static PreDownloadMeta ReadMeta(string staging) =>
        JsonSerializer.Deserialize(
            File.ReadAllText(Path.Combine(staging, "predownload.json")),
            GameMetaJsonContext.Default.PreDownloadMeta)!;

    private sealed class MapHandler(Dictionary<string, byte[]> responses) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();
            return Task.FromResult(responses.TryGetValue(url, out var body)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
