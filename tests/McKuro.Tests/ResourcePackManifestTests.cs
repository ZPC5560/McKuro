using System.Net;
using System.Text;
using McKuro.Core.Models.Game;
using McKuro.Core.Services.Game;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace McKuro.Tests;

/// <summary>
/// 新版协议端点(<c>.../{appKey}/G152/official/index.json</c>)的「当前正式版本」清单加载。
/// <para>
/// <b>为什么需要这条通道:</b>实测 2026-11-12(3.8.0 正式发布当天),旧协议端点
/// <c>default.config.version</c> 仍是 3.7.1,而新版协议端点四个资源包都是 3.8.0 ——
/// 官方把正式版本只发布到了 <c>resourcePacks</c> 上。只读旧端点就会永远报「已是最新」,
/// 表现就是「官方启动器提示更新、我们没提示」。
/// </para>
/// </summary>
public class ResourcePackManifestTests
{
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

    private const string Cdn = "https://cdn.example.com/";

    /// <summary>新协议索引:两个包 + 一个等级组合。</summary>
    private static string IndexJson(string packVersion) => $$"""
    {
      "cdnList": [ { "url": "{{Cdn}}", "P": 1 } ],
      "config": { "keyFileCheckList": [ "Wuthering Waves.exe" ] },
      "bundles": { "HD": { "resourcePacks": [ "common", "hd" ] } },
      "resourcePacks": {
        "common": { "version": "{{packVersion}}", "indexFile": "g/common/indexFile.json", "baseUrl": "g/common/zip/" },
        "hd":     { "version": "{{packVersion}}", "indexFile": "g/hd/indexFile.json",     "baseUrl": "g/hd/zip/" }
      }
    }
    """;

    private static string PackJson(string dest) => $$"""
    { "resource": [ { "dest": "{{dest}}", "md5": "m", "size": 1024 } ] }
    """;

    [Fact]
    public async Task Merges_All_Packs_Of_The_Bundle_Into_One_Manifest()
    {
        const string indexUrl = "https://prod.example.com/official/index.json";
        var responses = new Dictionary<string, string>
        {
            [indexUrl] = IndexJson("3.8.0"),
            [Cdn + "g/common/indexFile.json"] = PackJson("Client/Binaries/Win64/Common.dll"),
            [Cdn + "g/hd/indexFile.json"] = PackJson("Client/Content/HD/pakchunk0.pak"),
        };
        var loader = new GameManifestLoader(
            new HttpClient(new FakeHandler(responses)), NullLogger.Instance);

        var result = await loader.LoadResourcePackManifestAsync(indexUrl, "HD");

        Assert.NotNull(result);
        Assert.True(result!.Success);
        var manifest = result.Manifest!;
        // 版本号取自包版本 —— 新协议节点自身没有 version,这正是不能复用 KuroUpdateData 的原因。
        Assert.Equal("3.8.0", manifest.Version);
        // 两个包的文件都要在:漏一个装完就会缺文件。
        Assert.Equal(2, manifest.Files.Count);
        Assert.Contains(manifest.Files, f => f.Path.EndsWith("Common.dll"));
        Assert.Contains(manifest.Files, f => f.Path.EndsWith("pakchunk0.pak"));
        // 关键文件来自根节点 config.keyFileCheckList。
        Assert.Equal(["Wuthering Waves.exe"], manifest.KeyFiles);
    }

    [Fact]
    public async Task Download_Url_Is_Built_From_Cdn_And_PackBaseUrl()
    {
        // 每包有自己的 baseUrl;拼错就会 404,而版本/大小判断看不出来(要到下载才炸)。
        const string indexUrl = "https://prod.example.com/official/index.json";
        var responses = new Dictionary<string, string>
        {
            [indexUrl] = IndexJson("3.8.0"),
            [Cdn + "g/common/indexFile.json"] = PackJson("Client/Binaries/Win64/Common.dll"),
            [Cdn + "g/hd/indexFile.json"] = PackJson("Client/Content/HD/pakchunk0.pak"),
        };
        var loader = new GameManifestLoader(
            new HttpClient(new FakeHandler(responses)), NullLogger.Instance);

        var manifest = (await loader.LoadResourcePackManifestAsync(indexUrl, "HD"))!.Manifest!;

        var common = manifest.Files.Single(f => f.Path.EndsWith("Common.dll"));
        var hd = manifest.Files.Single(f => f.Path.EndsWith("pakchunk0.pak"));
        Assert.Equal(Cdn + "g/common/zip/Client/Binaries/Win64/Common.dll", common.Url);
        Assert.Equal(Cdn + "g/hd/zip/Client/Content/HD/pakchunk0.pak", hd.Url);
    }

    [Fact]
    public async Task Returns_Null_When_Index_Has_No_ResourcePacks()
    {
        // 旧协议端点没有 resourcePacks:必须返回 null 让调用方回退,而不是当成"已是最新"。
        const string indexUrl = "https://prod.example.com/old/index.json";
        var json = """
        {
          "default": { "cdnList": [ { "url": "https://cdn.example.com/", "P": 1 } ], "version": "3.7.1" }
        }
        """;
        var loader = new GameManifestLoader(
            new HttpClient(new FakeHandler(new Dictionary<string, string> { [indexUrl] = json })),
            NullLogger.Instance);

        Assert.Null(await loader.LoadResourcePackManifestAsync(indexUrl, "HD"));
    }

    [Fact]
    public async Task Returns_Null_When_A_Pack_Manifest_Is_Missing()
    {
        // 少一个包的清单 = 清单不完整。宁可回退旧端点,也不能交出缺文件的清单
        // (否则 ComputeDiff 会把整包内容都当成"要下载")。
        const string indexUrl = "https://prod.example.com/official/index.json";
        var responses = new Dictionary<string, string>
        {
            [indexUrl] = IndexJson("3.8.0"),
            [Cdn + "g/common/indexFile.json"] = PackJson("Client/Binaries/Win64/Common.dll"),
            // hd 的清单故意缺失(404)
        };
        var loader = new GameManifestLoader(
            new HttpClient(new FakeHandler(responses)), NullLogger.Instance);

        Assert.Null(await loader.LoadResourcePackManifestAsync(indexUrl, "HD"));
    }

    [Fact]
    public async Task Newer_Pack_Version_Wins_When_Packs_Disagree()
    {
        // 服务端可能把 common 与等级包的版本写成不同值(发布过渡期)。
        // 取较高者:否则会把"已经更生的包"当成旧版,导致漏更新。
        const string indexUrl = "https://prod.example.com/official/index.json";
        var json = $$"""
        {
          "cdnList": [ { "url": "{{Cdn}}", "P": 1 } ],
          "bundles": { "HD": { "resourcePacks": [ "common", "hd" ] } },
          "resourcePacks": {
            "common": { "version": "3.7.1", "indexFile": "g/common/indexFile.json", "baseUrl": "g/common/zip/" },
            "hd":     { "version": "3.8.0", "indexFile": "g/hd/indexFile.json",     "baseUrl": "g/hd/zip/" }
          }
        }
        """;
        var responses = new Dictionary<string, string>
        {
            [indexUrl] = json,
            [Cdn + "g/common/indexFile.json"] = PackJson("Client/Binaries/Win64/Common.dll"),
            [Cdn + "g/hd/indexFile.json"] = PackJson("Client/Content/HD/pakchunk0.pak"),
        };
        var loader = new GameManifestLoader(
            new HttpClient(new FakeHandler(responses)), NullLogger.Instance);

        Assert.Equal("3.8.0", (await loader.LoadResourcePackManifestAsync(indexUrl, "HD"))!.Manifest!.Version);
    }
}
