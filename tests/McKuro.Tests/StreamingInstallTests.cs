using System.Net;
using System.Text;
using System.Text.Json;
using McKuro.Core.Infrastructure;
using McKuro.Core.Models.Game;
using McKuro.Core.Services.Game;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace McKuro.Tests;

/// <summary>
/// 「边下载边更新」流式安装端到端测试(3.6 → 3.6.1):
/// 全量清单路径直接把下载文件落位到游戏根目录,替换前备份旧文件到 .McKuro_backup,
/// 不再经过 appData 的 install_tmp 暂存目录。
/// 用带闸门的 HTTP handler 模拟 CDN,证明「下载尚未全部完成时,已下载文件已就位」。
/// </summary>
public sealed class StreamingInstallTests : IDisposable
{
    private readonly string _tmpDir;
    private readonly string _gameRoot;
    private readonly AppDatabase _db;

    // 模拟 CDN 固定地址
    private const string IndexUrl = "https://cdn.test/launcher/game/G152/index.json";
    private const string CdnBase = "https://cdn.test/launcher/";
    private const string ResourcePath = "game/G152/resource.json";
    private const string BasePath = "game/G152/";
    private const string ExePath = "WutheringWaves.exe";

    public StreamingInstallTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "McKuro_stream_" + Guid.NewGuid().ToString("N"));
        _gameRoot = Path.Combine(Path.GetTempPath(), "McKuro_stream_game_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmpDir);
        Directory.CreateDirectory(Path.Combine(_gameRoot, "Client", "Data"));
        _db = new AppDatabase(_tmpDir);
    }

    public void Dispose()
    {
        _db.Dispose();
        try
        {
            Directory.Delete(_tmpDir, recursive: true);
        }
        catch
        {
            // 忽略清理失败
        }
        try
        {
            Directory.Delete(_gameRoot, recursive: true);
        }
        catch
        {
            // 忽略清理失败
        }
    }

    [Fact]
    public async Task InstallAsync_StreamsFilesIntoGameDir_BacksUpOld_AndWritesVersion()
    {
        // 3.6.1 清单内容
        byte[] exeBytes = Encoding.UTF8.GetBytes("exe-3.6.1");
        byte[] patchedBytes = Encoding.UTF8.GetBytes("patched-3.6.1");
        byte[] newBytes = Encoding.UTF8.GetBytes("new-3.6.1");
        const string patchedPath = "Client/Data/patched.pak";
        const string newPath = "Client/Data/new.pak";

        // 本地 3.6.0:exe 已匹配(不下载)、patched.pak 是旧内容(需替换)、new.pak 缺失(需下载)
        WriteLocal(ExePath, "exe-3.6.1");
        WriteLocal(patchedPath, "patched-3.6.0");

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gatedUrl = CdnBase + BasePath + patchedPath;
        var cdn = new Dictionary<string, byte[]>
        {
            [CdnBase + BasePath + ExePath] = exeBytes,
            [gatedUrl] = patchedBytes,
            [CdnBase + BasePath + newPath] = newBytes,
        };
        var responses = BuildResponses("3.6.1", cdn, new[] { ExePath, patchedPath, newPath });
        var handler = new GateCdnHandler(responses, gatedUrl, gate.Task);

        var loader = new GameManifestLoader(new HttpClient(handler));
        var downloader = new DownloadEngine(new HttpClient(handler), maxConcurrency: 8);
        var updater = new GameUpdater(
            loader,
            downloader,
            new UpdateInstaller(),
            new GamePathResolver(() => _gameRoot),
            _tmpDir,
            _db,
            indexUrlProvider: _ => IndexUrl,
            logger: NullLogger<GameUpdater>.Instance);

        // 预置本地已装版本 3.6.0(key 需与游戏根目录规范化一致)
        _db.SetInstalledVersion(GameUpdater.NormalizeRoot(_gameRoot), "3.6.0");

        var installTask = updater.InstallAsync(GameServerType.Official, ct: CancellationToken.None);

        // —— 边下载边更新:patched.pak 被闸门卡在下载中,new.pak 应已流式落位 ——
        var newPakFull = Path.Combine(_gameRoot, newPath.Replace('/', Path.DirectorySeparatorChar));
        await WaitUntilAsync(() => File.Exists(newPakFull), TimeSpan.FromSeconds(10));
        Assert.Equal("new-3.6.1", File.ReadAllText(newPakFull));

        // 此时被闸门卡住的 patched.pak 仍是旧内容(尚未被替换),说明替换发生在落位瞬间
        var patchedFull = Path.Combine(_gameRoot, patchedPath.Replace('/', Path.DirectorySeparatorChar));
        Assert.Equal("patched-3.6.0", File.ReadAllText(patchedFull));

        // 放行闸门,等待安装完成
        gate.SetResult();
        var result = await installTask;

        Assert.True(result.Success, result.Message);
        Assert.Equal("3.6.1", _db.GetInstalledVersion(GameUpdater.NormalizeRoot(_gameRoot)));

        // patched.pak 已替换为新内容
        Assert.Equal("patched-3.6.1", File.ReadAllText(patchedFull));

        // 旧文件已备份到 .McKuro_backup
        var backup = Path.Combine(_gameRoot, ".McKuro_backup", patchedPath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(backup), "替换前旧文件应备份到 .McKuro_backup");
        Assert.Equal("patched-3.6.0", File.ReadAllText(backup));

        // 未使用 appData 暂存目录(流式直接落位,不留 install_tmp)
        Assert.False(Directory.Exists(Path.Combine(_tmpDir, "install_tmp")), "流式安装不应创建 install_tmp 暂存目录");

        // 无 .part 残留
        Assert.Empty(Directory.EnumerateFiles(_gameRoot, "*.part", SearchOption.AllDirectories));

        // 已匹配的 exe 未被重新下载/替换(内容保持原样)
        Assert.Equal("exe-3.6.1", File.ReadAllText(Path.Combine(_gameRoot, ExePath)));
    }

    [Fact]
    public async Task InstallAsync_AllFilesMatching_SkipsDownload_NoStreamingSideEffects()
    {
        byte[] exeBytes = Encoding.UTF8.GetBytes("exe-3.6.1");
        byte[] patchedBytes = Encoding.UTF8.GetBytes("patched-3.6.1");
        WriteLocal(ExePath, "exe-3.6.1");
        WriteLocal("Client/Data/patched.pak", "patched-3.6.1");

        var responses = BuildResponses("3.6.1", new Dictionary<string, byte[]>
        {
            [CdnBase + BasePath + ExePath] = exeBytes,
            [CdnBase + BasePath + "Client/Data/patched.pak"] = patchedBytes,
        }, new[] { ExePath, "Client/Data/patched.pak" });
        var handler = new GateCdnHandler(responses, gatedUrl: null, gate: Task.CompletedTask);

        var loader = new GameManifestLoader(new HttpClient(handler));
        var downloader = new DownloadEngine(new HttpClient(handler), maxConcurrency: 8);
        var updater = new GameUpdater(
            loader,
            downloader,
            new UpdateInstaller(),
            new GamePathResolver(() => _gameRoot),
            _tmpDir,
            _db,
            indexUrlProvider: _ => IndexUrl,
            logger: NullLogger<GameUpdater>.Instance);

        var result = await updater.InstallAsync(GameServerType.Official, ct: CancellationToken.None);

        Assert.True(result.Success, result.Message);
        // 无差异 → 无需下载,直接返回「文件完整」
        Assert.Contains("完整", result.Message);
        // 未创建备份目录(没有文件被替换)
        Assert.False(Directory.Exists(Path.Combine(_gameRoot, ".McKuro_backup")));
        // 版本仍写入(InstallAsync 成功后统一写版本)
        Assert.Equal("3.6.1", _db.GetInstalledVersion(GameUpdater.NormalizeRoot(_gameRoot)));
    }

    /// <summary>构造 index.json + resource.json + CDN 文件响应,md5/size 按 CDN 实际字节计算。</summary>
    private static Dictionary<string, byte[]> BuildResponses(
        string version,
        Dictionary<string, byte[]> cdnFiles,
        string[] manifestPaths)
    {
        // 每条 manifest 路径 → 对应 CDN URL → 真实 md5/size
        var entries = manifestPaths.Select(path =>
        {
            var url = CdnBase + BasePath + path;
            cdnFiles.TryGetValue(url, out var bytes);
            var md5 = bytes is null ? "" : Convert.ToHexStringLower(System.Security.Cryptography.MD5.HashData(bytes));
            return $$"""{ "dest": "{{path}}", "md5": "{{md5}}", "size": {{bytes?.Length ?? 0}} }""";
        });
        var resources = "[" + string.Join(",", entries) + "]";

        var index = $$"""
        {
          "default": {
            "cdnList": [ { "url": "{{CdnBase}}", "ping": 10, "P": 1 } ],
            "resources": "{{ResourcePath}}",
            "resourcesBasePath": "{{BasePath}}",
            "version": "{{version}}",
            "config": { "downloadLimit": 8, "size": 1234, "unCompressSize": 1234 }
          },
          "keyFileCheckList": [ "{{ExePath}}" ],
          "gameResourceList": { "resource": {{resources}} }
        }
        """;
        var resource = $$"""{ "resource": {{resources}} }""";

        var responses = new Dictionary<string, byte[]>
        {
            [IndexUrl] = Encoding.UTF8.GetBytes(index),
            [CdnBase + ResourcePath] = Encoding.UTF8.GetBytes(resource),
        };
        foreach (var (url, bytes) in cdnFiles)
        {
            responses[url] = bytes;
        }
        return responses;
    }

    private void WriteLocal(string relative, string content)
    {
        var full = Path.Combine(_gameRoot, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.Elapsed > timeout)
            {
                throw new TimeoutException("等待流式落位超时");
            }
            await Task.Delay(25);
        }
    }

    /// <summary>
    /// 伪 CDN handler:按 URL 返回文件内容;指定 URL 被闸门阻塞,用于模拟慢文件以证明边下载边更新。
    /// </summary>
    private sealed class GateCdnHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, byte[]> _files;
        private readonly string? _gatedUrl;
        private readonly Task _gate;

        public GateCdnHandler(Dictionary<string, byte[]> files, string? gatedUrl, Task gate)
        {
            _files = files;
            _gatedUrl = gatedUrl;
            _gate = gate;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            if (_gatedUrl is not null && url == _gatedUrl)
            {
                await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            if (_files.TryGetValue(url, out var body))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(body),
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }
}