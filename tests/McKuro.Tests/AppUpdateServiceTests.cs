using System.Text.Json;
using McKuro.Core.Services.Game;
using McKuro.Core.Services.Update;

namespace McKuro.Tests;

/// <summary>应用自更新测试(对齐 Haiyu UpdateAppViewModel)。</summary>
public class AppUpdateServiceTests
{
    [Theory]
    [InlineData("1.0.0", "1.0.0", false)]
    [InlineData("1.0.0", "1.0.0.0", false)]
    [InlineData("1.0.0", "1.2.0", true)]
    [InlineData("1.2.0", "1.0.0", false)]
    [InlineData("1.5.0", "2.0.0", true)]
    public void IsNewer_Compares_Versions(string current, string remote, bool expected)
    {
        Assert.Equal(expected, AppUpdateService.IsNewer(current, remote));
    }

    [Fact]
    public void GitHub_Release_Json_Deserializes_With_SourceGen_Context()
    {
        var json = """
            {
              "tag_name": "v2.1.0",
              "assets": [
                {
                  "name": "McKuro-Setup-2.1.0.exe",
                  "size": 52428800,
                  "browser_download_url": "https://github.com/owner/repo/releases/download/v2.1.0/McKuro-Setup-2.1.0.exe"
                },
                {
                  "name": "McKuro-win-x64.zip",
                  "size": 1024,
                  "browser_download_url": "https://github.com/owner/repo/releases/download/v2.1.0/McKuro-win-x64.zip"
                }
              ]
            }
            """;
        var release = JsonSerializer.Deserialize(json, GitHubJsonContext.Default.GitHubRelease);
        Assert.NotNull(release);
        Assert.Equal("v2.1.0", release!.TagName);
        Assert.Equal(2, release.Assets!.Count);

        // 应优先选 exe 安装包
        var asset = release.Assets.FirstOrDefault(a =>
            a.Name?.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) == true);
        Assert.NotNull(asset);
        Assert.Equal("McKuro-Setup-2.1.0.exe", asset!.Name);
    }

    [Fact]
    public async Task CheckAsync_Empty_Repo_Returns_Null()
    {
        var service = new AppUpdateService(new HttpClient());
        Assert.Null(await service.CheckAsync(""));
        Assert.Null(await service.CheckAsync("   "));
    }

    [Fact]
    public async Task Download_From_Local_Http_Server()
    {
        // 简易本地 HTTP 服务器模拟 GitHub asset 下载
        var payload = new byte[256 * 1024];
        new Random(42).NextBytes(payload);

        using var listener = new System.Net.HttpListener();
        var prefix = $"http://127.0.0.1:{GetFreePort()}/";
        listener.Prefixes.Add(prefix);
        listener.Start();
        _ = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                try
                {
                    var ctx = await listener.GetContextAsync();
                    ctx.Response.ContentLength64 = payload.Length;
                    await ctx.Response.OutputStream.WriteAsync(payload);
                    ctx.Response.Close();
                }
                catch
                {
                    break;
                }
            }
        });

        var service = new AppUpdateService(new HttpClient());
        var destDir = Path.Combine(Path.GetTempPath(), "mckuro-upd-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = await service.DownloadAsync(prefix + "McKuro-Setup.exe", destDir);
            Assert.NotNull(path);
            Assert.True(File.Exists(path!));
            Assert.Equal(payload.Length, new FileInfo(path!).Length);
        }
        finally
        {
            listener.Stop();
            try { Directory.Delete(destDir, true); } catch { }
        }
    }

    private static int GetFreePort()
    {
        var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        l.Start();
        var port = ((System.Net.IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    [Fact]
    public void GraphicsComponents_Empty_Dir_Returns_Empty()
    {
        var paths = new GamePathResolver(() => Path.GetTempPath() + "\\no-such-dir-" + Guid.NewGuid().ToString("N"));
        var updater = new GameUpdater(null!, null!, null!, paths, Path.GetTempPath());
        Assert.Empty(updater.GetLocalGraphicsComponentVersions());
    }

    [Fact]
    public void GraphicsComponents_Missing_Dll_Reports_NotFound()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mckuro-gfx-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var paths = new GamePathResolver(() => dir);
            var updater = new GameUpdater(null!, null!, null!, paths, Path.GetTempPath());
            var versions = updater.GetLocalGraphicsComponentVersions();
            Assert.Equal(3, versions.Count);
            Assert.All(versions, v => Assert.Equal("未找到文件", v.Version));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
