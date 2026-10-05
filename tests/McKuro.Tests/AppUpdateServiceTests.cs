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

    /// <summary>
    /// 四段版本号必须完整保留:<c>Version.ToString(3)</c> 会把 1.3.3.1 截成 "1.3.3",
    /// 导致<b>已升级到 1.3.3.1 的用户仍被判为"有新版本"</b>,更新提示永远消不掉。
    /// </summary>
    [Theory]
    [InlineData(1, 3, 3, 0, "1.3.3")]      // 第四段为 0:保持既有三段展示
    [InlineData(1, 3, 3, 1, "1.3.3.1")]    // 补丁版:不得截断
    [InlineData(1, 3, 3, 2, "1.3.3.2")]
    [InlineData(1, 4, 0, 0, "1.4.0")]
    [InlineData(2, 0, 0, 1, "2.0.0.1")]
    public void FormatVersion_Keeps_Four_Segments(int major, int minor, int build, int revision, string expected)
    {
        Assert.Equal(expected, AppUpdateService.FormatVersion(new Version(major, minor, build, revision)));
    }

    [Fact]
    public void FormatVersion_Null_Falls_Back()
    {
        Assert.Equal("1.0.0", AppUpdateService.FormatVersion(null));
    }

    /// <summary>
    /// 端到端回归:安装 1.3.3.1 后检查到的最新版仍是 1.3.3.1 → 必须判定"已是最新",
    /// 否则四段版本会陷入"提示更新 → 更新完仍提示"的循环。
    /// </summary>
    [Fact]
    public void Installed_Four_Segment_Version_Is_Not_Newer_Than_Itself()
    {
        var current = AppUpdateService.FormatVersion(new Version(1, 3, 3, 1));
        Assert.False(AppUpdateService.IsNewer(current, "1.3.3.1"));
        // 而旧版(1.3.3)应当收到 1.3.3.1 的更新提示
        Assert.True(AppUpdateService.IsNewer(AppUpdateService.FormatVersion(new Version(1, 3, 3, 0)), "1.3.3.1"));
    }

    [Fact]
    public void PickAsset_NoMatchingAsset_ReturnsNull()
    {
        Assert.Null(AppUpdateService.PickAsset(["README.txt", "source.zip"], "win"));
        Assert.Null(AppUpdateService.PickAsset([], "win"));
        Assert.Null(AppUpdateService.PickAsset([null, ""], "win"));
    }

    // v1.2.0 真实资产清单(全平台发布后,Windows 曾误选 osx 包 → PickAsset 平台过滤回归)
    private static readonly string[] V120Assets =
    [
        "mckuro-1.2.0-1.x86_64.rpm",
        "McKuro-linux-x64-1.2.0.tar.gz",
        "McKuro-osx-arm64-1.2.0.app.zip",
        "McKuro-osx-arm64-1.2.0.dmg",
        "McKuro-osx-arm64-1.2.0.zip",
        "McKuro-osx-x64-1.2.0.app.zip",
        "McKuro-osx-x64-1.2.0.dmg",
        "McKuro-osx-x64-1.2.0.zip",
        "McKuro-setup-1.2.0.exe",
        "McKuro-win-x64-1.2.0.zip",
        "mckuro_1.2.0_amd64.deb",
    ];

    [Fact]
    public void PickAsset_Windows_PrefersWinZip_IgnoresForeignPlatforms()
    {
        Assert.Equal("McKuro-win-x64-1.2.0.zip", AppUpdateService.PickAsset(V120Assets, "win"));
    }

    [Fact]
    public void PickAsset_Windows_FallsBackToSetupExe()
    {
        var names = new[] { "McKuro-osx-arm64-1.2.0.zip", "McKuro-setup-1.2.0.exe" };
        Assert.Equal("McKuro-setup-1.2.0.exe", AppUpdateService.PickAsset(names, "win"));
    }

    [Theory]
    [InlineData("arm64", "McKuro-osx-arm64-1.2.0.zip")]
    [InlineData("x64", "McKuro-osx-x64-1.2.0.zip")]
    public void PickAsset_Mac_PrefersFlatZipOverAppZip(string arch, string expected)
    {
        // .app.zip 解压会把 McKuro.app 嵌套进 Contents/MacOS,平铺 zip 才与安装目录布局一致
        Assert.Equal(expected, AppUpdateService.PickAsset(V120Assets, "osx", arch));
    }

    [Fact]
    public void PickAsset_Linux_HasNoAutoUpdateAsset()
    {
        // tar.gz 无法走 zip 解压替换流程 → 不提示自动更新(手动下载)
        Assert.Null(AppUpdateService.PickAsset(V120Assets, "linux"));
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

    // ---------------- GitHub 资产摘要(自 v1.3.3.1 起取代 *.sha256 资产) ----------------

    /// <summary>资产自带摘要的标准形态是 <c>sha256:&lt;64hex&gt;</c>(API digest 与发布页文本同源)。</summary>
    [Theory]
    [InlineData("sha256:6c63835cbd58ff667ded2aad28e52d14c47cb071e828a4678b7aff0fb22551b8",
        "6c63835cbd58ff667ded2aad28e52d14c47cb071e828a4678b7aff0fb22551b8")]
    [InlineData("SHA256:6C63835CBD58FF667DED2AAD28E52D14C47CB071E828A4678B7AFF0FB22551B8",
        "6c63835cbd58ff667ded2aad28e52d14c47cb071e828a4678b7aff0fb22551b8")] // 大写归一化
    [InlineData("  sha256:0eb7b65f217f3013f38671a51d0812b68ebea852f37c010366d8f6850160ca38  ",
        "0eb7b65f217f3013f38671a51d0812b68ebea852f37c010366d8f6850160ca38")] // 首尾空白
    public void ParseDigest_Accepts_Sha256_Prefixed_Hex(string digest, string expected)
    {
        Assert.Equal(expected, UpdateChecksum.ParseDigest(digest));
    }

    /// <summary>
    /// 无摘要/其它算法一律返回 null —— 调用方据此视为"发布方未提供摘要"跳过校验,
    /// 不得把 md5 等别的算法误当成 sha256(那会把正确下载包判成损坏并反复重下)。
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("sha256:")]                 // 只有前缀
    [InlineData("sha256:abc123")]           // 不足 64 位
    [InlineData("md5:d41d8cd98f00b204e9800998ecf8427e")]
    [InlineData("6c63835cbd58ff667ded2aad28e52d14c47cb071e828a4678b7aff0fb22551b8")] // 无算法前缀
    public void ParseDigest_Rejects_Missing_Or_Other_Algorithms(string? digest)
    {
        Assert.Null(UpdateChecksum.ParseDigest(digest));
    }

    [Fact]
    public void GitHub_Asset_Digest_Deserializes_Into_Model()
    {
        // 真实 v1.3.3 响应片段(摘要与实际发布页显示一致)
        var json = """
            {
              "tag_name": "v1.3.3",
              "assets": [
                {
                  "name": "McKuro-win-x64-1.3.3.zip",
                  "size": 134813171,
                  "browser_download_url": "https://github.com/o/r/releases/download/v1.3.3/McKuro-win-x64-1.3.3.zip",
                  "digest": "sha256:6c63835cbd58ff667ded2aad28e52d14c47cb071e828a4678b7aff0fb22551b8"
                }
              ]
            }
            """;
        var release = JsonSerializer.Deserialize(json, GitHubJsonContext.Default.GitHubRelease);
        var asset = Assert.Single(release!.Assets!);
        Assert.Equal(
            "6c63835cbd58ff667ded2aad28e52d14c47cb071e828a4678b7aff0fb22551b8",
            UpdateChecksum.ParseDigest(asset.Digest));
    }

    /// <summary>各平台资产名 → 该资产独有的摘要(用于断言摘要没有串行)。</summary>
    private static readonly (string Name, string Digest)[] HtmlDigestAssets =
    [
        ("McKuro-win-x64-1.3.3.zip", "1111111111111111111111111111111111111111111111111111111111111111"),
        ("McKuro-setup-1.3.3.exe", "2222222222222222222222222222222222222222222222222222222222222222"),
        ("McKuro-osx-arm64-1.3.3.zip", "3333333333333333333333333333333333333333333333333333333333333333"),
        ("McKuro-osx-x64-1.3.3.zip", "4444444444444444444444444444444444444444444444444444444444444444"),
    ];

    /// <summary>
    /// HTML 回退通道(匿名 API 限流时的常用路径)此前完全不解析摘要。
    /// 现按资产行取 GitHub 计算的 sha256 —— 必须取自**本资产所在行**,
    /// 取错行会把正确下载包判成损坏并反复重下。
    /// <para>直接覆盖解析器:Linux 上无自动更新资产(PickAsset 返回 null),走不到该分支。</para>
    /// </summary>
    [Fact]
    public void ParseHtmlAssetDigest_Scopes_Digest_To_Its_Own_Row()
    {
        var fragment = string.Join("\n", HtmlDigestAssets.Select(a =>
            $"""
             <li class="Box-row d-flex">
               <a href="/owner/repo/releases/download/v1.3.3/{a.Name}" rel="nofollow">{a.Name}</a>
               <span class="Truncate-text">sha256:{a.Digest}</span>
             </li>
             """));

        // 每个资产都取到自己的摘要(不含别的资产行)
        foreach (var (name, digest) in HtmlDigestAssets)
        {
            Assert.Equal(digest, AppUpdateService.ParseHtmlAssetDigest(fragment, "owner/repo", name));
        }

        // 未知资产 / 空片段 → null(视为无摘要,不阻断更新)
        Assert.Null(AppUpdateService.ParseHtmlAssetDigest(fragment, "owner/repo", "McKuro-linux-x64-1.3.3.tar.gz"));
        Assert.Null(AppUpdateService.ParseHtmlAssetDigest("", "owner/repo", "McKuro-win-x64-1.3.3.zip"));
    }

    /// <summary>
    /// 端到端:HTML 回退通道在 API 限流时被启用,且所选资产带摘要。
    /// Linux 无自动更新资产(设计如此)→ 该平台不产生更新信息,断言相应跳过。
    /// </summary>
    [Fact]
    public async Task Check_Via_Html_Fallback_Reads_Per_Asset_Digest()
    {
        var fragment = string.Join("\n", HtmlDigestAssets.Select(a =>
            $"""
             <li class="Box-row d-flex">
               <a href="/owner/repo/releases/download/v1.3.3/{a.Name}" rel="nofollow">{a.Name}</a>
               <span class="Truncate-text">sha256:{a.Digest}</span>
             </li>
             """));

        var service = new AppUpdateService(new HttpClient(new HtmlFallbackHandler(fragment)));
        var info = await service.CheckAsync("owner/repo", forceRefresh: true);

        if (info is null)
        {
            // Linux:无自动更新资产,通道按设计返回空
            return;
        }
        var picked = HtmlDigestAssets.FirstOrDefault(a => a.Name == info.AssetName);
        Assert.False(picked.Name is null, $"未预期的资产:{info.AssetName}");
        Assert.Equal(picked.Digest, info.Sha256);
    }

    /// <summary>
    /// API 通道返回 <c>digest</c> 时应据此校验(不再依赖已移除的 *.sha256 资产),
    /// 且 API 优先于 HTML 回退。
    /// </summary>
    [Fact]
    public async Task Check_Via_Api_Uses_Asset_Digest()
    {
        const string digest = "6c63835cbd58ff667ded2aad28e52d14c47cb071e828a4678b7aff0fb22551b8";
        var json = $$"""
            {"tag_name":"v9.9.9","assets":[{"name":"McKuro-win-x64-9.9.9.zip","size":1024,
              "browser_download_url":"https://github.com/o/r/releases/download/v9.9.9/McKuro-win-x64-9.9.9.zip",
              "digest":"sha256:{{digest}}"}]}
            """;
        var service = new AppUpdateService(new HttpClient(new StaticJsonHandler(json)));
        var info = await service.CheckAsync("owner/repo", forceRefresh: true);

        // Linux 无自动更新资产 → 结果为空;仅在 Windows/macOS 上断言摘要
        if (info is not null)
        {
            Assert.Equal("McKuro-win-x64-9.9.9.zip", info.AssetName);
            Assert.Equal(digest, info.Sha256);
        }
    }

    /// <summary>API 返回 403(限流)时提供 HTML 回退所需的两段响应。</summary>
    private sealed class HtmlFallbackHandler(string fragment) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var uri = request.RequestUri?.ToString() ?? "";
            if (uri.Contains("api.github.com", StringComparison.OrdinalIgnoreCase))
            {
                // 匿名配额耗尽 → 触发 HTML 回退
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.Forbidden));
            }
            if (uri.Contains("expanded_assets", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(fragment),
                });
            }
            // /releases/latest:302 到 tag 页(代码从最终 URL 解析 tag)
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(""),
                RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://github.com/owner/repo/releases/tag/v1.3.3"),
            };
            return Task.FromResult(response);
        }
    }

    /// <summary>固定返回一段 Release JSON 的 handler(模拟 API 通道)。</summary>
    private sealed class StaticJsonHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(json),
            });
    }

    // 固定 Release 响应:断言只看"哪个客户端被命中",不依赖平台资产选择(Linux 无自动更新资产,结果为 null)
    private const string CannedReleaseJson =
        """{"tag_name":"v9.9.9","assets":[{"name":"McKuro-win-x64-9.9.9.zip","size":1024,"browser_download_url":"https://github.com/o/r/releases/download/v9.9.9/McKuro-win-x64-9.9.9.zip"}]}""";

    /// <summary>记录命中次数的假 handler,任何请求都返回罐头 Release JSON。</summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        public int Hits;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref Hits);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(CannedReleaseJson),
            });
        }
    }

    [Fact]
    public async Task Check_FrontingDisabled_UsesSharedClient()
    {
        var shared = new RecordingHandler();
        var service = new AppUpdateService(new HttpClient(shared));
        try
        {
            await service.CheckAsync("owner/repo", forceRefresh: true);
            Assert.True(shared.Hits >= 1);
        }
        finally
        {
            GitHubIpFronting.Enabled = false; // 静态全局,测试后必须还原
        }
    }

    [Fact]
    public async Task Check_FrontingEnabled_RoutesCheckThroughFrontingClient()
    {
        // 回归:此前域前置仅下载通道生效,DNS 污染用户开开关后"检查更新"仍走系统 DNS
        GitHubIpFronting.Enabled = true;
        var shared = new RecordingHandler();
        var fronted = new RecordingHandler();
        var service = new AppUpdateService(new HttpClient(shared));
        service.FrontingClientFactory = _ => new HttpClient(fronted);
        try
        {
            await service.CheckAsync("owner/repo", forceRefresh: true);
            Assert.True(fronted.Hits >= 1, "检查请求应走域前置客户端");
            Assert.Equal(0, shared.Hits);
        }
        finally
        {
            GitHubIpFronting.Enabled = false;
        }
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
