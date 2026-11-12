using System.Net;
using System.Text;
using McKuro.Core.Models.Game;
using McKuro.Core.Services.Game;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace McKuro.Tests;

/// <summary>
/// 「补全可选资源」计划与「正式更新规格」选项。
/// <para>
/// 场景:用户当初选了精简(丢掉了 <c>Option_Pack_*</c> 这类可后补资源),之后想一次性备齐。
/// 计划必须只挑被精简丢弃的分组,不能把常规文件一起算进去。
/// </para>
/// </summary>
public class ResourceTopUpTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, string> _responses;
        public FakeHandler(Dictionary<string, string> r) => _responses = r;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            var url = req.RequestUri!.ToString();
            if (_responses.TryGetValue(url, out var body))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private const string Cdn = "https://cdn.example.com/";

    private static string IndexJson() => $$"""
    {
      "cdnList": [ { "url": "{{Cdn}}", "P": 1 } ],
      "bundles": { "HD": { "resourcePacks": [ "common", "hd" ] } },
      "resourcePacks": {
        "common": { "version": "3.8.0", "indexFile": "c/i.json", "baseUrl": "c/zip/",
          "patchConfig": [ { "version": "3.7.1", "size": 5000, "indexFile": "c/371/i.json", "baseUrl": "c/371/resources" } ] },
        "hd": { "version": "3.8.0", "indexFile": "h/i.json", "baseUrl": "h/zip/",
          "patchConfig": [ { "version": "3.7.1", "size": 3000, "indexFile": "h/371/i.json", "baseUrl": "h/371/resources" } ] }
      }
    }
    """;

    /// <summary>补丁清单:一个保留分组 + 一个被精简丢弃的分组。</summary>
    private static string PatchJson(string keepDest, string dropDest) => $$"""
    {
      "tagGroups": { "min": { "exclude_prefixes": [ "Client/Saved/Resources/" ] } },
      "resource": [
        { "dest": "keep.krpdiff", "md5": "m1", "size": 1000 },
        { "dest": "drop.krpdiff", "md5": "m2", "size": 2000 }
      ],
      "groupInfos": [
        { "dest": "keep.krpdiff", "srcFiles": [],
          "dstFiles": [ { "dest": "{{keepDest}}", "md5": "d1", "size": 10 } ] },
        { "dest": "drop.krpdiff", "srcFiles": [],
          "dstFiles": [ { "dest": "{{dropDest}}", "md5": "d2", "size": 20 } ] }
      ]
    }
    """;

    private static GameManifestLoader Loader(string indexUrl, Dictionary<string, string> extra)
    {
        var all = new Dictionary<string, string> { [indexUrl] = IndexJson() };
        foreach (var kv in extra) all[kv.Key] = kv.Value;
        return new GameManifestLoader(new HttpClient(new FakeHandler(all)), NullLogger.Instance);
    }

    [Fact]
    public async Task TopUp_Plan_Only_Contains_Dropped_Optional_Groups()
    {
        const string indexUrl = "https://prod.example.com/official/index.json";
        var loader = Loader(indexUrl, new()
        {
            [Cdn + "c/371/i.json"] = PatchJson("Client/Content/Paks/a.pak", "Client/Saved/Resources/Option_Pack_X/b.pak"),
            [Cdn + "h/371/i.json"] = PatchJson("Client/Content/HD/c.pak", "Client/Saved/Resources/Option_Pack_Y/d.pak"),
        });

        var plan = await loader.LoadResourceTopUpPlanAsync(indexUrl, "HD", "3.7.1");

        Assert.NotNull(plan);
        // 只有被精简丢弃的 2 个差分包(drop.krpdiff × 2 包),保留分组不该算进来。
        Assert.Equal(2, plan!.FileCount);
        Assert.Equal(4000, plan.DownloadBytes);
        Assert.Equal(2, plan.Packs.Count);
    }

    [Fact]
    public async Task TopUp_Plan_Targets_Are_Final_Paths_Not_Diff_Packages()
    {
        // <b>这是初版误报的根因回归测试。</b>
        // 检测"缺不缺"必须看「差分应用后应落地的目标文件」(dstFiles),不能看「要下载的差分包」
        // —— .krpdiff 本来就不在游戏目录里,拿它判断会让完整版也被报成"精简,可补 4.78 GB"。
        const string indexUrl = "https://prod.example.com/official/index.json";
        var loader = Loader(indexUrl, new()
        {
            [Cdn + "c/371/i.json"] = PatchJson("Client/Content/Paks/a.pak", "Client/Saved/Resources/Option_Pack_X/b.pak"),
            [Cdn + "h/371/i.json"] = PatchJson("Client/Content/HD/c.pak", "Client/Saved/Resources/Option_Pack_Y/d.pak"),
        });

        var plan = await loader.LoadResourceTopUpPlanAsync(indexUrl, "HD", "3.7.1");

        Assert.NotNull(plan);
        // 目标文件是 .pak(应用差分后应存在),不是 .krpdiff(要下载的包)。
        Assert.All(plan!.TargetPaths, p => Assert.DoesNotContain(".krpdiff", p, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(plan.TargetPaths, p => p.EndsWith("Option_Pack_X/b.pak", StringComparison.Ordinal));
        Assert.Contains(plan.TargetPaths, p => p.EndsWith("Option_Pack_Y/d.pak", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TopUp_Plan_Is_Null_When_Nothing_Was_Dropped()
    {
        // 服务端没给排除规则(3.7 及以前)→ 没有"精简"之分 → 不该给补全入口。
        const string indexUrl = "https://prod.example.com/official/index.json";
        var patchNoExclude = """
        {
          "resource": [ { "dest": "keep.krpdiff", "md5": "m1", "size": 1000 } ],
          "groupInfos": [ { "dest": "keep.krpdiff", "srcFiles": [],
            "dstFiles": [ { "dest": "Client/Content/Paks/a.pak", "md5": "d1", "size": 10 } ] } ]
        }
        """;
        var loader = Loader(indexUrl, new()
        {
            [Cdn + "c/371/i.json"] = patchNoExclude,
            [Cdn + "h/371/i.json"] = patchNoExclude,
        });

        Assert.Null(await loader.LoadResourceTopUpPlanAsync(indexUrl, "HD", "3.7.1"));
    }

    [Fact]
    public async Task Update_Patch_Plan_Reports_Both_Spec_Sizes()
    {
        const string indexUrl = "https://prod.example.com/official/index.json";
        var loader = Loader(indexUrl, new()
        {
            [Cdn + "c/371/i.json"] = PatchJson("Client/Content/Paks/a.pak", "Client/Saved/Resources/Option_Pack_X/b.pak"),
            [Cdn + "h/371/i.json"] = PatchJson("Client/Content/HD/c.pak", "Client/Saved/Resources/Option_Pack_Y/d.pak"),
        });

        var plan = await loader.LoadUpdatePatchPlanAsync(indexUrl, "HD", "3.7.1");

        Assert.NotNull(plan);
        // 全量:两个分组都要(1000+2000)×2 = 6000;精简:只留 keep(1000)×2 = 2000。
        Assert.Equal(6000, plan!.FullBytes);
        Assert.Equal(2000, plan.MinimalBytes);
        Assert.True(plan.SupportsMinimal);
        // 默认规格是全量,DownloadBytes 跟着它走。
        Assert.Equal(6000, plan.DownloadBytes);
    }

    [Fact]
    public async Task Update_Patch_Plan_With_Minimal_Spec_Reports_Minimal_Bytes()
    {
        const string indexUrl = "https://prod.example.com/official/index.json";
        var loader = Loader(indexUrl, new()
        {
            [Cdn + "c/371/i.json"] = PatchJson("Client/Content/Paks/a.pak", "Client/Saved/Resources/Option_Pack_X/b.pak"),
            [Cdn + "h/371/i.json"] = PatchJson("Client/Content/HD/c.pak", "Client/Saved/Resources/Option_Pack_Y/d.pak"),
        });

        var plan = await loader.LoadUpdatePatchPlanAsync(
            indexUrl, "HD", "3.7.1", CancellationToken.None, PredownloadSpec.Minimal);

        Assert.Equal(PredownloadSpec.Minimal, plan!.Spec);
        Assert.Equal(2000, plan.DownloadBytes);
    }
}
