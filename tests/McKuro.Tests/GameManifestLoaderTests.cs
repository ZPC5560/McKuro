using System.Net;
using System.Text;
using McKuro.Core.Models.Game;
using McKuro.Core.Services.Game;

namespace McKuro.Tests;

public class GameManifestLoaderTests
{
    /// <summary>用内存响应模拟 HTTP,避免真实网络。</summary>
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
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                };
                return Task.FromResult(response);
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private const string IndexJson = """
    {
      "default": {
        "cdnList": [ { "url": "https://cdn.example.com/launcher/", "ping": 10 } ],
        "resources": "game/G152/10003_xxx/resource.json",
        "resourcesBasePath": "game/G152/10003_xxx/",
        "version": "2.3.0",
        "config": { "downloadLimit": 8 }
      },
      "keyFileCheckList": [ "Client/Binaries/Win64/Client-Win64-Shipping.exe" ],
      "gameResourceList": {
        "resource": [
          { "dest": "Client/Binaries/Win64/Client-Win64-Shipping.exe", "md5": "abc123", "size": 1024 },
          { "dest": "Client/Saved/Config/Client.ini", "md5": "def456", "size": 512 }
        ]
      }
    }
    """;

    private const string ResourceJson = """
    {
      "resource": [
        { "dest": "Client/Binaries/Win64/Client-Win64-Shipping.exe", "md5": "abc123", "size": 1024 },
        { "dest": "Client/Saved/Config/Client.ini", "md5": "def456", "size": 512 },
        { "dest": "WutheringWaves.exe", "md5": "aaa111", "size": 2048 }
      ]
    }
    """;

    [Fact]
    public async Task LoadKuroAsync_ParsesManifestAndBuildsCdnUrls()
    {
        var indexUrl = "https://prod-cn-alicdn-gamestarter.kurogame.com/launcher/game/G152/10003/index.json";
        var handler = new FakeHandler(new Dictionary<string, string>
        {
            [indexUrl] = IndexJson,
        });
        var http = new HttpClient(handler);
        var loader = new GameManifestLoader(http);

        var result = await loader.LoadKuroAsync(indexUrl);

        Assert.True(result.Success);
        Assert.NotNull(result.Manifest);
        Assert.Equal("2.3.0", result.Manifest!.Version);
        Assert.Equal(2, result.Manifest.Files.Count);

        // 文件 URL = CDN + resourcesBasePath + dest
        var exe = result.Manifest.Files.First(f => f.Path == "Client/Binaries/Win64/Client-Win64-Shipping.exe");
        Assert.Equal("https://cdn.example.com/launcher/game/G152/10003_xxx/Client/Binaries/Win64/Client-Win64-Shipping.exe", exe.Url);
        Assert.Equal("abc123", exe.Md5);
        Assert.Equal(1024, exe.Size);
        Assert.Equal("2.3.0", result.ServerVersion);
        Assert.Contains("Client/Binaries/Win64/Client-Win64-Shipping.exe", result.Manifest.KeyFiles);
    }

    [Fact]
    public async Task LoadKuroAsync_LoadsFullResourceJsonWhenAvailable()
    {
        var indexUrl = "https://cdn.example.com/index.json";
        var resourceUrl = "https://cdn.example.com/launcher/game/G152/10003_xxx/resource.json";
        var handler = new FakeHandler(new Dictionary<string, string>
        {
            [indexUrl] = IndexJson,
            [resourceUrl] = ResourceJson,
        });
        var http = new HttpClient(handler);
        var loader = new GameManifestLoader(http);

        var result = await loader.LoadKuroAsync(indexUrl);

        Assert.True(result.Success);
        // resource.json 覆盖内嵌清单,包含 3 个文件
        Assert.Equal(3, result.Manifest!.Files.Count);
        Assert.Contains(result.Manifest.Files, f => f.Path == "WutheringWaves.exe");
    }

    [Fact]
    public async Task LoadKuroAsync_PredownloadFlag_UsesPredownloadSection()
    {
        var indexUrl = "https://cdn.example.com/index.json";
        var predownloadJson = """
        {
          "default": { "cdnList": [ { "url": "https://cdn.example.com/", "ping": 1 } ],
                       "resources": "r.json", "resourcesBasePath": "base/", "version": "2.2.0" },
          "predownload": { "cdnList": [ { "url": "https://cdn.example.com/", "ping": 1 } ],
                           "resources": "r2.json", "resourcesBasePath": "base2/", "version": "2.3.0" },
          "gameResourceList": {
            "resource": [ { "dest": "a.bin", "md5": "m1", "size": 10 } ]
          }
        }
        """;
        var handler = new FakeHandler(new Dictionary<string, string> { [indexUrl] = predownloadJson });
        var http = new HttpClient(handler);
        var loader = new GameManifestLoader(http);

        var result = await loader.LoadKuroAsync(indexUrl, preDownload: true);

        Assert.True(result.Success);
        Assert.Equal("2.3.0", result.Manifest!.Version);
        Assert.Equal("2.3.0", result.PredownloadVersion);
        Assert.True(result.HasPredownload);
    }

    [Fact]
    public async Task LoadKuroAsync_NetworkError_ReturnsFailure()
    {
        var http = new HttpClient(new FakeHandler(new Dictionary<string, string>()));
        var loader = new GameManifestLoader(http);

        var result = await loader.LoadKuroAsync("https://cdn.example.com/404.json");

        Assert.False(result.Success);
        Assert.Null(result.Manifest);
    }
}
