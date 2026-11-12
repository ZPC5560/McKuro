using System.Net;
using System.Text;
using McKuro.Core.Models.Game;
using McKuro.Core.Services.Game;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace McKuro.Tests;

/// <summary>
/// 正式版本的差分更新计划:各包 <c>patchConfig</c> 里「从本地版本升到目标版本」的补丁清单。
/// <para>
/// <b>为什么必须接这条:</b>新版协议没有旧协议的顶层 <c>default.config.patchConfig</c>,
/// 差分配置挪进了每个资源包自己身上。不接就只能逐文件对齐完整清单 ——
/// 实测 3.7.1→3.8.0 差分 12.08 GiB,而按完整清单补齐要多下几十 GB。
/// </para>
/// </summary>
public class UpdatePatchPlanTests
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

    private static string IndexJson(string packVersion, string fromVersion) => $$"""
    {
      "cdnList": [ { "url": "{{Cdn}}", "P": 1 } ],
      "bundles": { "HD": { "resourcePacks": [ "common", "hd" ] } },
      "resourcePacks": {
        "common": {
          "version": "{{packVersion}}", "indexFile": "g/common/indexFile.json", "baseUrl": "g/common/zip/",
          "patchConfig": [ { "version": "{{fromVersion}}", "size": 1000,
                             "indexFile": "g/common/{{fromVersion}}/indexFile.json",
                             "baseUrl": "g/common/{{fromVersion}}/resources", "indexFileMd5": "m1" } ]
        },
        "hd": {
          "version": "{{packVersion}}", "indexFile": "g/hd/indexFile.json", "baseUrl": "g/hd/zip/",
          "patchConfig": [ { "version": "{{fromVersion}}", "size": 2000,
                             "indexFile": "g/hd/{{fromVersion}}/indexFile.json",
                             "baseUrl": "g/hd/{{fromVersion}}/resources", "indexFileMd5": "m2" } ]
        }
      }
    }
    """;

    private static GameManifestLoader Loader(string indexUrl, string indexJson) =>
        new(new HttpClient(new FakeHandler(new Dictionary<string, string> { [indexUrl] = indexJson })),
            NullLogger.Instance);

    [Fact]
    public async Task Builds_Plan_From_Each_Pack_PatchConfig()
    {
        const string indexUrl = "https://prod.example.com/official/index.json";
        var loader = Loader(indexUrl, IndexJson("3.8.0", "3.7.1"));

        var plan = await loader.LoadUpdatePatchPlanAsync(indexUrl, "HD", "3.7.1");

        Assert.NotNull(plan);
        Assert.Equal("3.7.1", plan!.SourceVersion);
        Assert.Equal("3.8.0", plan.TargetVersion);
        Assert.Equal(2, plan.Packs.Count);
        // 每包各自的差分清单与 baseUrl 都要正确(拼错就 404,而版本判断看不出来)。
        var common = plan.Packs.Single(p => p.PackName == "common");
        Assert.Equal(Cdn + "g/common/3.7.1/indexFile.json", common.PatchIndexUrl);
        Assert.Equal(Cdn + "g/common/3.7.1/resources", common.BaseUrl);
        Assert.Equal(3000, plan.DownloadBytes);
    }

    [Fact]
    public async Task Returns_Null_When_Installed_Version_Has_No_Patch_Entry()
    {
        // 本地版本没有专属差分项时必须回退完整清单:
        // 拿别的版本硬套会合不出正确文件,装完校验必失败,白下一遍。
        const string indexUrl = "https://prod.example.com/official/index.json";
        var loader = Loader(indexUrl, IndexJson("3.8.0", "3.7.1"));

        Assert.Null(await loader.LoadUpdatePatchPlanAsync(indexUrl, "HD", "3.6.0"));
    }

    [Fact]
    public async Task Returns_Null_When_No_Installed_Version()
    {
        const string indexUrl = "https://prod.example.com/official/index.json";
        var loader = Loader(indexUrl, IndexJson("3.8.0", "3.7.1"));

        Assert.Null(await loader.LoadUpdatePatchPlanAsync(indexUrl, "HD", null));
    }

    [Fact]
    public async Task Returns_Null_When_One_Pack_Lacks_A_Patch_Entry()
    {
        // 只要有**一个**包匹配不到就整体回退,不做「部分差分」:
        // 少打一个包的差分,装完就缺文件。
        const string indexUrl = "https://prod.example.com/official/index.json";
        var json = $$"""
        {
          "cdnList": [ { "url": "{{Cdn}}", "P": 1 } ],
          "bundles": { "HD": { "resourcePacks": [ "common", "hd" ] } },
          "resourcePacks": {
            "common": { "version": "3.8.0", "indexFile": "g/common/i.json", "baseUrl": "g/common/zip/",
              "patchConfig": [ { "version": "3.7.1", "size": 1, "indexFile": "g/common/p.json" } ] },
            "hd": { "version": "3.8.0", "indexFile": "g/hd/i.json", "baseUrl": "g/hd/zip/",
              "patchConfig": [ { "version": "3.7.0", "size": 1, "indexFile": "g/hd/p.json" } ] }
          }
        }
        """;
        var loader = Loader(indexUrl, json);

        Assert.Null(await loader.LoadUpdatePatchPlanAsync(indexUrl, "HD", "3.7.1"));
    }
}

/// <summary>阶段进度:差分合成/解压等非下载阶段也要能推进进度条。</summary>
public class StageProgressTests
{
    private static DownloadProgress Stage(double? percent) => new()
    {
        CurrentFile = "x",
        FileIndex = 0,
        FileTotal = 0,
        BytesDownloaded = 0,
        BytesTotal = 0,
        SpeedBps = 0,
        StageText = "正在合成分组差分 (13/39)…",
        StagePercent = percent,
    };

    [Fact]
    public void Stage_With_Percent_Drives_The_Progress_Bar()
    {
        // 这正是「点了安装更新,进度条不动」的修复点:
        // 早先 Stage 阶段不动进度条,39 个分组跑几十分钟期间一直显示 0%。
        var p = Stage(33.3);
        Assert.Equal(DownloadPhase.Stage, p.Phase);
        Assert.Equal(33.3, p.DisplayPercent, 1);
    }

    [Fact]
    public void Stage_Without_Percent_Keeps_Bar_Still()
    {
        // 无法量化的阶段(如单文件搬运)保持不动,而不是乱跳。
        var p = Stage(null);
        Assert.Equal(0, p.DisplayPercent);
    }

    [Fact]
    public void Percent_Is_Clamped_To_Range()
    {
        Assert.Equal(100, Stage(180).DisplayPercent);
        Assert.Equal(0, Stage(-5).DisplayPercent);
    }

    [Fact]
    public void Download_Stage_Uses_Byte_Percent()
    {
        var p = new DownloadProgress
        {
            CurrentFile = "x", FileIndex = 1, FileTotal = 2,
            BytesDownloaded = 50, BytesTotal = 200, SpeedBps = 0,
        };
        Assert.Equal(DownloadPhase.Download, p.Phase);
        Assert.Equal(25, p.DisplayPercent);
    }
}
