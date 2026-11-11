using System.Net;
using System.Text;
using System.Text.Json;
using McKuro.Core.Models.Game;
using McKuro.Core.Services.Game;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace McKuro.Tests;

/// <summary>
/// 分级预下载(3.8 起官方的全量 / 精简)。
/// <para>
/// 协议要点(2026-11-11 实测,勿凭印象改):
/// ① 旧协议端点的 <c>predownload</c> 恒为 null,分级预载只在新版协议端点;
/// ② 新版协议的 <c>predownload</c> 只有 <c>bundles</c> + <c>resourcePacks</c>,
///    <b>没有</b> <c>version</c> —— 版本号在每个包自己身上;
/// ③ 全量/精简的判据是补丁清单顶层的 <c>tagGroups.min.exclude_prefixes</c>,
///    命中的<b>分组</b>整组不下载。
/// </para>
/// </summary>
public class PredownloadSpecTests
{
    /// <summary>用内存响应模拟 HTTP。</summary>
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

    /// <summary>两份分组:一份全落在排除前缀内(精简要丢),一份不落(必须保留)。</summary>
    private static string PatchIndexJson(long keepBytes, long dropBytes) => $$"""
    {
      "tagGroups": { "min": { "exclude_prefixes": [ "Client/Saved/Resources/" ] } },
      "resource": [
        { "dest": "keep_group_0.krpdiff", "md5": "m1", "size": {{keepBytes}} },
        { "dest": "drop_group_0.krpdiff", "md5": "m2", "size": {{dropBytes}} }
      ],
      "groupInfos": [
        { "dest": "keep_group_0.krpdiff",
          "srcFiles": [ { "dest": "Client/Content/Paks/pakchunk0.pak", "md5": "s1", "size": 100 } ],
          "dstFiles": [ { "dest": "Client/Content/Paks/pakchunk0.pak", "md5": "d1", "size": 200 } ] },
        { "dest": "drop_group_0.krpdiff",
          "srcFiles": [ { "dest": "Client/Saved/Resources/Option_Pack_X/a.pak", "md5": "s2", "size": 300 } ],
          "dstFiles": [ { "dest": "Client/Saved/Resources/Option_Pack_X/a.pak", "md5": "d2", "size": 400 } ] }
      ]
    }
    """;

    [Fact]
    public async Task Minimal_Drops_Groups_Whose_Targets_All_Match_Exclude_Prefixes()
    {
        var url = Cdn + "patch/indexFile.json";
        var http = new HttpClient(new FakeHandler(new Dictionary<string, string> { [url] = PatchIndexJson(1000, 9000) }));
        var loader = new GameManifestLoader(http, NullLogger.Instance);

        var full = await loader.LoadPatchAsync(url, Cdn, ct: CancellationToken.None, spec: PredownloadSpec.Full);
        var minimal = await loader.LoadPatchAsync(url, Cdn, ct: CancellationToken.None, spec: PredownloadSpec.Minimal);

        Assert.True(full.Success);
        Assert.True(minimal.Success);
        // 全量:两个差分包都要(1000 + 9000)
        Assert.Equal(10000, full.Manifest!.Files.Sum(f => f.Size));
        // 精简:整组命中的那个不要,只剩 1000
        Assert.Equal(1000, minimal.Manifest!.Files.Sum(f => f.Size));
        Assert.Single(minimal.Manifest.PatchPlan!.DiffGroups);
        Assert.Equal(2, full.Manifest.PatchPlan!.DiffGroups.Count);
    }

    [Fact]
    public async Task Minimal_Falls_Back_To_Full_When_TagGroups_Missing()
    {
        // 3.7 及以前的清单没有 tagGroups:两档必须等价,不能凭空少下内容。
        var url = Cdn + "patch/indexFile.json";
        var json = """
        {
          "resource": [ { "dest": "a.krpdiff", "md5": "m", "size": 500 } ],
          "groupInfos": [
            { "dest": "a.krpdiff",
              "srcFiles": [ { "dest": "Client/Saved/Resources/x.pak", "md5": "s", "size": 1 } ],
              "dstFiles": [ { "dest": "Client/Saved/Resources/x.pak", "md5": "d", "size": 2 } ] }
          ]
        }
        """;
        var http = new HttpClient(new FakeHandler(new Dictionary<string, string> { [url] = json }));
        var loader = new GameManifestLoader(http, NullLogger.Instance);

        var full = await loader.LoadPatchAsync(url, Cdn, ct: CancellationToken.None, spec: PredownloadSpec.Full);
        var minimal = await loader.LoadPatchAsync(url, Cdn, ct: CancellationToken.None, spec: PredownloadSpec.Minimal);

        Assert.Equal(500, full.Manifest!.Files.Sum(f => f.Size));
        Assert.Equal(500, minimal.Manifest!.Files.Sum(f => f.Size));
    }

    [Fact]
    public void Group_With_Mixed_Targets_Is_Never_Dropped()
    {
        // 分组是原子单位:只要有一条 dst 不在排除前缀下,整组必须保留(否则差分合不出来)。
        var patch = JsonSerializer.Deserialize("""
        {
          "tagGroups": { "min": { "exclude_prefixes": [ "Client/Saved/Resources/" ] } },
          "resource": [ { "dest": "g.krpdiff", "md5": "m", "size": 700 } ],
          "groupInfos": [
            { "dest": "g.krpdiff",
              "srcFiles": [],
              "dstFiles": [
                { "dest": "Client/Saved/Resources/a.pak", "md5": "d1", "size": 1 },
                { "dest": "Client/Content/Paks/b.pak", "md5": "d2", "size": 2 }
              ] }
          ]
        }
        """, GameJsonContext.Default.KuroPatchIndex)!;

        var prefixes = PredownloadPlanner.ExcludePrefixes(patch);
        var dropped = PredownloadPlanner.DroppedGroupDests(patch, prefixes);

        Assert.Empty(dropped);
        Assert.Equal(700, PredownloadPlanner.DownloadBytes(patch, PredownloadSpec.Minimal));
    }

    [Fact]
    public void Group_With_Empty_Targets_Is_Kept()
    {
        // 没有判据就不猜:空 dstFiles 的分组一律保留(宁可多下一次,也不制造半个差分)。
        var patch = JsonSerializer.Deserialize("""
        {
          "tagGroups": { "min": { "exclude_prefixes": [ "Client/Saved/Resources/" ] } },
          "resource": [ { "dest": "g.krpdiff", "md5": "m", "size": 300 } ],
          "groupInfos": [ { "dest": "g.krpdiff", "srcFiles": [], "dstFiles": [] } ]
        }
        """, GameJsonContext.Default.KuroPatchIndex)!;

        var prefixes = PredownloadPlanner.ExcludePrefixes(patch);
        Assert.Empty(PredownloadPlanner.DroppedGroupDests(patch, prefixes));
        Assert.Equal(300, PredownloadPlanner.DownloadBytes(patch, PredownloadSpec.Minimal));
    }

    [Fact]
    public void LargestFile_Is_Tracked_For_Disk_Estimate()
    {
        // 所需空间 = 下载体积 + 最大单文件(复现官方 13.48 = 12.08 + 1.40 的口径)。
        var patch = JsonSerializer.Deserialize("""
        {
          "resource": [
            { "dest": "a.krpdiff", "md5": "m1", "size": 100 },
            { "dest": "b.krpdiff", "md5": "m2", "size": 900 }
          ]
        }
        """, GameJsonContext.Default.KuroPatchIndex)!;

        Assert.Equal(1000, PredownloadPlanner.DownloadBytes(patch, PredownloadSpec.Full));
        Assert.Equal(900, PredownloadPlanner.LargestFileBytes(patch, PredownloadSpec.Full));
    }

    [Fact]
    public async Task LoadPredownloadInfo_Reads_New_Protocol_Node_Without_Version()
    {
        // 实测新协议:predownload 节点没有 version,版本号在每个包自己的 version 里。
        const string indexUrl = "https://prod.example.com/official/index.json";
        var indexJson = """
        {
          "cdnList": [ { "url": "https://cdn.example.com/", "P": 1 } ],
          "resourcePacks": { "common": { "version": "3.7.1" }, "hd": { "version": "3.7.1" } },
          "bundles": { "HD": { "resourcePacks": [ "common", "hd" ] } },
          "predownload": {
            "bundles": { "HD": { "resourcePacks": [ "common", "hd" ] } },
            "resourcePacks": {
              "common": {
                "version": "3.8.0",
                "indexFile": "pd/common/3.7.1/indexFile.json",
                "baseUrl": "pd/common/3.7.1/resources",
                "patchConfig": [ { "version": "3.7.1", "size": 1000, "indexFile": "pd/common/3.7.1/indexFile.json", "baseUrl": "pd/common/3.7.1/resources" } ]
              },
              "hd": {
                "version": "3.8.0",
                "indexFile": "pd/hd/3.7.1/indexFile.json",
                "baseUrl": "pd/hd/3.7.1/resources",
                "patchConfig": [ { "version": "3.7.1", "size": 2000, "indexFile": "pd/hd/3.7.1/indexFile.json", "baseUrl": "pd/hd/3.7.1/resources" } ]
              }
            }
          }
        }
        """;
        var responses = new Dictionary<string, string>
        {
            [indexUrl] = indexJson,
            [Cdn + "pd/common/3.7.1/indexFile.json"] = PatchIndexJson(500, 1500),
            [Cdn + "pd/hd/3.7.1/indexFile.json"] = PatchIndexJson(400, 1200),
        };
        var loader = new GameManifestLoader(
            new HttpClient(new FakeHandler(responses)), NullLogger.Instance);

        var info = await loader.LoadPredownloadInfoAsync(indexUrl, "HD", "3.7.1");

        Assert.NotNull(info);
        // 版本取自包版本(节点自身没有 version)
        Assert.Equal("3.8.0", info!.TargetVersion);
        Assert.Equal(["common", "hd"], info.PackNames);
        // 两包的全量 / 精简都按同一判据算:(500+1500) + (400+1200)
        Assert.Equal(3600, info.FullBytes);
        Assert.Equal(900, info.MinimalBytes);
        Assert.True(info.SupportsMinimal);
        Assert.Equal(2, info.Packs.Count);
    }

    [Fact]
    public async Task LoadPredownloadInfo_Returns_Null_When_Node_Absent()
    {
        // 旧协议端点的真实现状:predownloadSwitch=1 但没有 predownload 节点。
        const string indexUrl = "https://prod.example.com/old/index.json";
        var indexJson = """
        {
          "cdnList": [ { "url": "https://cdn.example.com/", "P": 1 } ],
          "resourcePacks": { "common": { "version": "3.7.1" } },
          "bundles": { "HD": { "resourcePacks": [ "common" ] } }
        }
        """;
        var loader = new GameManifestLoader(
            new HttpClient(new FakeHandler(new Dictionary<string, string> { [indexUrl] = indexJson })),
            NullLogger.Instance);

        Assert.Null(await loader.LoadPredownloadInfoAsync(indexUrl, "HD", "3.7.1"));
    }
}
