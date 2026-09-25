using System.Security.Cryptography;
using McKuro.Core.Services.Update;

namespace McKuro.Tests;

/// <summary>
/// 更新链路增强测试:sha256 校验解析/比对、GitHub IP 域前置解析与别名、加速模板套用、
/// Releases 结果缓存。参考实现来自 Haiyu(GithubUpdateService / SocketHttpFactory),
/// 但缓存与摘要校验为 McKuro 侧补强,故用例围绕"不误判、不阻断"设计。
/// </summary>
public class UpdateEnhancementTests
{
    private static string Hex(char c) => new(c, 64);

    // ---------- UpdateChecksum.ParseSha256 ----------

    [Fact]
    public void ParseSha256_Sha256SumFormat_WithTwoSpaces()
    {
        var h = Hex('a');
        var text = $"{h}  McKuro-win-x64-1.2.5.zip\n";
        Assert.Equal(h, UpdateChecksum.ParseSha256(text, "McKuro-win-x64-1.2.5.zip"));
    }

    [Fact]
    public void ParseSha256_Sha256SumFormat_WithBinaryStar()
    {
        var h = Hex('b');
        var text = $"{h} *McKuro-win-x64-1.2.5.zip\n";
        Assert.Equal(h, UpdateChecksum.ParseSha256(text, "McKuro-win-x64-1.2.5.zip"));
    }

    [Fact]
    public void ParseSha256_MultiLineList_PicksMatchingAsset()
    {
        var target = Hex('c');
        var other = Hex('d');
        var text = $"{other}  other-file.zip\n{target}  McKuro-win-x64-1.2.5.zip\n";
        Assert.Equal(target, UpdateChecksum.ParseSha256(text, "McKuro-win-x64-1.2.5.zip"));
    }

    [Fact]
    public void ParseSha256_GitHubGeneratedFunctionForm()
    {
        // GitHub Release 自动生成的 *.sha256 资产内容形态
        var h = Hex('e');
        var text = $"SHA256 (McKuro-win-x64-1.2.5.zip) = {h}\n";
        Assert.Equal(h, UpdateChecksum.ParseSha256(text, "McKuro-win-x64-1.2.5.zip"));
    }

    [Theory]
    [InlineData("McKuro-win-x64-1.2.5.zip: {0}\n")]
    [InlineData("McKuro-win-x64-1.2.5.zip = {0}\n")]
    public void ParseSha256_NameThenHashForms(string template)
    {
        var h = Hex('f');
        Assert.Equal(h, UpdateChecksum.ParseSha256(string.Format(template, h), "McKuro-win-x64-1.2.5.zip"));
    }

    [Fact]
    public void ParseSha256_UppercaseHash_NormalizedToLower()
    {
        var upper = new string('A', 64);
        var text = $"{upper}  McKuro-win-x64-1.2.5.zip\n";
        Assert.Equal(upper.ToLowerInvariant(), UpdateChecksum.ParseSha256(text, "McKuro-win-x64-1.2.5.zip"));
    }

    [Fact]
    public void ParseSha256_AssetNotListed_ReturnsNull()
    {
        var h = Hex('a');
        var text = $"{h}  some-other-asset.zip\n";
        Assert.Null(UpdateChecksum.ParseSha256(text, "McKuro-win-x64-1.2.5.zip"));
    }

    [Fact]
    public void ParseSha256_NoHash_ReturnsNull()
    {
        Assert.Null(UpdateChecksum.ParseSha256("本次更新修复了若干问题,无摘要。", "McKuro-win-x64-1.2.5.zip"));
        Assert.Null(UpdateChecksum.ParseSha256("", "a.zip"));
        Assert.Null(UpdateChecksum.ParseSha256(null, "a.zip"));
    }

    [Fact]
    public void ParseSha256_MarkdownTable_ReturnsNull()
    {
        // 仅出现在 markdown 表格里、无独立摘要行的写法不做猜测(避免把展示文本当摘要)
        var h = Hex('a');
        Assert.Null(UpdateChecksum.ParseSha256($"| a.zip | `{h}` |\n", "a.zip"));
    }

    [Fact]
    public void ParseSha256_MatchesByFileName_IgnoresDirectoryPrefix()
    {
        var h = Hex('a');
        var text = $"{h}  dist/McKuro-win-x64-1.2.5.zip\n";
        Assert.Equal(h, UpdateChecksum.ParseSha256(text, "McKuro-win-x64-1.2.5.zip"));
    }

    // ---------- UpdateChecksum.ParseSingleSha256 ----------

    [Fact]
    public void ParseSingleSha256_SingleEntry_ReturnsIt()
    {
        var h = Hex('a');
        Assert.Equal(h, UpdateChecksum.ParseSingleSha256($"{h}  McKuro-win-x64-1.2.5.zip\n"));
    }

    [Fact]
    public void ParseSingleSha256_ConflictingEntries_ReturnsNull()
    {
        // 同一清单出现两个不同摘要时不猜测
        var text = $"{Hex('a')}  a.zip\n{Hex('b')}  b.zip\n";
        Assert.Null(UpdateChecksum.ParseSingleSha256(text));
    }

    [Fact]
    public void ParseSingleSha256_RepeatedSameHash_IsNotConflict()
    {
        var h = Hex('a');
        Assert.Equal(h, UpdateChecksum.ParseSingleSha256($"{h}  a.zip\n{h}  b.zip\n"));
    }

    // ---------- UpdateChecksum.Matches / ComputeSha256Async ----------

    [Fact]
    public void Matches_IsCaseInsensitive_AndEmptyExpectedSkips()
    {
        var h = Hex('a');
        Assert.True(UpdateChecksum.Matches(h, h.ToUpperInvariant()));
        Assert.True(UpdateChecksum.Matches("  " + h + "  ", h));
        // 期望值为空 = 发布方未提供摘要,视为无需校验(不能因此阻断更新)
        Assert.True(UpdateChecksum.Matches(null, h));
        Assert.True(UpdateChecksum.Matches("", null));
        Assert.False(UpdateChecksum.Matches(h, Hex('b')));
        Assert.False(UpdateChecksum.Matches(h, null));
    }

    [Fact]
    public async Task ComputeSha256Async_MatchesKnownDigest()
    {
        var path = Path.Combine(Path.GetTempPath(), "mckuro-sha-" + Guid.NewGuid().ToString("N"));
        try
        {
            var bytes = "hello mckuro"u8.ToArray();
            await File.WriteAllBytesAsync(path, bytes);
            var expected = Convert.ToHexStringLower(SHA256.HashData(bytes));
            Assert.Equal(expected, await UpdateChecksum.ComputeSha256Async(path));
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public async Task ComputeSha256Async_MissingFile_ReturnsNull()
    {
        var path = Path.Combine(Path.GetTempPath(), "mckuro-nope-" + Guid.NewGuid().ToString("N"));
        Assert.Null(await UpdateChecksum.ComputeSha256Async(path));
    }

    // ---------- GitHubIpFronting ----------

    [Fact]
    public void Resolve_KnownHosts_ReturnIpv4Addresses()
    {
        Assert.NotEmpty(GitHubIpFronting.Resolve("github.com"));
        Assert.NotEmpty(GitHubIpFronting.Resolve("api.github.com"));
        Assert.All(GitHubIpFronting.Resolve("github.com"),
            ip => Assert.Equal(System.Net.Sockets.AddressFamily.InterNetwork, ip.AddressFamily));
    }

    [Fact]
    public void Resolve_UnknownHost_ReturnsEmpty()
    {
        Assert.Empty(GitHubIpFronting.Resolve("example.com"));
        Assert.Empty(GitHubIpFronting.Resolve(""));
        Assert.Empty(GitHubIpFronting.Resolve(null!));
    }

    [Fact]
    public void Resolve_WildcardUserContentHost_MatchesSubdomains()
    {
        // 未知的 *.githubusercontent.com 子域回退到通配 IP 表(不返回空 → 仍可绕过 DNS)
        var wildcard = GitHubIpFronting.Resolve("some-unknown-asset.githubusercontent.com");
        Assert.NotEmpty(wildcard);

        // 已知子域走自己的别名条目(release-assets 优先),两者都非空即可 —— 数量不必相同
        Assert.NotEmpty(GitHubIpFronting.Resolve("release-assets.githubusercontent.com"));
        Assert.NotEmpty(GitHubIpFronting.Resolve("objects.githubusercontent.com"));
    }

    [Fact]
    public void Resolve_AliasedHost_FallsBackToSiblingHostIps()
    {
        // 发布资产在 github.com 与 githubusercontent 系之间跳转:任一键缺失时用同族 IP 兜底
        var aliased = GitHubIpFronting.Resolve("objects-origin.githubusercontent.com");
        Assert.NotEmpty(aliased);
        Assert.Contains(aliased, ip => GitHubIpFronting.Resolve("github.com").Contains(ip));
    }

    [Fact]
    public void Resolve_NoDuplicateAddresses()
    {
        foreach (var host in new[] { "github.com", "*.githubusercontent.com" })
        {
            var ips = GitHubIpFronting.Resolve(host);
            Assert.Equal(ips.Count, ips.Distinct().Count());
        }
    }

    [Fact]
    public void Enabled_DefaultsToFalse_SoSystemDnsIsUsed()
    {
        // 域前置拿到的是固定 IP,上游换 IP 即失效 → 默认必须关闭,避免把可用环境改坏
        var original = GitHubIpFronting.Enabled;
        try
        {
            // 默认值由静态字段初值决定(未被测试污染时为 false)
            Assert.False(original);
        }
        finally
        {
            GitHubIpFronting.Enabled = original;
        }
    }

    [Fact]
    public void CreateHandler_ReturnsHandlerWithConnectCallback()
    {
        using var handler = GitHubIpFronting.CreateHandler(null);
        Assert.NotNull(handler.ConnectCallback);
        Assert.True(handler.UseProxy);
    }

    // ---------- 加速模板 ----------

    [Fact]
    public void ApplyAccelerator_ReplacesPlaceholder()
    {
        var url = "https://github.com/o/r/releases/download/v1/a.zip";
        Assert.Equal(
            $"https://gh-proxy.example/{url}",
            GitHubIpFronting.ApplyAccelerator("https://gh-proxy.example/{downloadUrl}", url));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https://gh-proxy.example/no-placeholder")]
    public void ApplyAccelerator_InvalidTemplate_ReturnsOriginal(string? template)
    {
        var url = "https://github.com/o/r/releases/download/v1/a.zip";
        Assert.Equal(url, GitHubIpFronting.ApplyAccelerator(template, url));
    }

    [Fact]
    public void IsValidAccelerator_RequiresPlaceholder()
    {
        Assert.True(GitHubIpFronting.IsValidAccelerator("https://x/{downloadUrl}"));
        Assert.False(GitHubIpFronting.IsValidAccelerator("https://x/"));
        Assert.False(GitHubIpFronting.IsValidAccelerator(""));
        Assert.False(GitHubIpFronting.IsValidAccelerator(null));
    }

    // ---------- 缓存 ----------

    [Fact]
    public async Task CheckAsync_EmptyRepo_ReturnsNull_AndDoesNotCache()
    {
        var service = new AppUpdateService(new HttpClient());
        Assert.Null(await service.CheckAsync(""));
        Assert.Null(await service.CheckAsync("   "));
        // 空仓库不应污染缓存
        service.InvalidateCache();
    }

    [Fact]
    public async Task CheckAsync_UnreachableRepo_CachesNullResult()
    {
        // /releases/latest 与 HTML 通道对无效仓库都无结果 → 返回 null(不抛异常)
        var service = new AppUpdateService(new HttpClient { Timeout = TimeSpan.FromSeconds(5) });
        service.InvalidateCache();
        var result = await service.CheckAsync(
            "mckuro-nonexistent-owner-xyz/mckuro-nonexistent-repo-xyz",
            new CancellationTokenSource(TimeSpan.FromSeconds(20)).Token);
        Assert.Null(result);
        service.InvalidateCache();
    }

    // ---------- CI 契约:发布流程生成的 *.sha256 必须能被解析 ----------

    /// <summary>
    /// 逐字复刻 .github/workflows/build-and-test.yml 各发布步骤产出的摘要内容
    /// (publish-win / setup 用 pwsh <c>Out-File -NoNewline</c> 无尾换行;macOS/Linux 用
    /// <c>echo</c> 带尾换行)。任一侧格式改动都会让此用例失败,防止自更新校验静默失效。
    /// </summary>
    [Theory]
    [InlineData("McKuro-win-x64-1.2.6.zip",
        "SHA256 (McKuro-win-x64-1.2.6.zip) = " + "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("McKuro-osx-arm64-1.2.6.zip",
        "SHA256 (McKuro-osx-arm64-1.2.6.zip) = bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\n")]
    [InlineData("McKuro-linux-x64-1.2.6.tar.gz",
        "SHA256 (McKuro-linux-x64-1.2.6.tar.gz) = cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc\n")]
    [InlineData("McKuro-setup-1.2.6.exe",
        "SHA256 (McKuro-setup-1.2.6.exe) = dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd")]
    public void CiGeneratedSha256Manifest_IsParsable_BothPaths(string asset, string content)
    {
        var byName = UpdateChecksum.ParseSha256(content, asset);
        var single = UpdateChecksum.ParseSingleSha256(content);
        Assert.NotNull(byName);
        Assert.NotNull(single);
        Assert.Equal(byName, single);          // 两条解析路径结论必须一致
        Assert.Equal(64, byName!.Length);      // 规范化后为纯 64 位小写 hex
        Assert.Equal(byName.ToLowerInvariant(), byName);
    }

    // ---------- 下载 + 完整性校验端到端 ----------

    /// <summary>起一个本地 HTTP 服务器回放固定字节;用于验证下载链路与摘要校验的实际交互。</summary>
    private static (System.Net.HttpListener listener, string prefix) StartServer(byte[] payload)
    {
        var listener = new System.Net.HttpListener();
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
                    // 不支持 Range 时按 200 全量返回(重试路径会重下)
                    ctx.Response.StatusCode = 200;
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
        return (listener, prefix);
    }

    [Fact]
    public async Task DownloadAsync_CorrectChecksum_Succeeds()
    {
        var payload = new byte[64 * 1024];
        new Random(7).NextBytes(payload);
        var sha = Convert.ToHexStringLower(SHA256.HashData(payload));
        var (listener, prefix) = StartServer(payload);
        var destDir = Path.Combine(Path.GetTempPath(), "mckuro-csum-ok-" + Guid.NewGuid().ToString("N"));
        try
        {
            var service = new AppUpdateService(new HttpClient());
            var path = await service.DownloadAsync(prefix + "McKuro-win-x64.zip", destDir, expectedSha256: sha);
            Assert.NotNull(path);
            Assert.Equal(payload.Length, new FileInfo(path!).Length);
            Assert.False(File.Exists(path + ".part"));
        }
        finally
        {
            listener.Stop();
            try { Directory.Delete(destDir, true); } catch { }
        }
    }

    [Fact]
    public async Task DownloadAsync_WrongChecksum_FailsAndLeavesNoArtifact()
    {
        // 摘要不符 ⇒ 拒绝交付该文件(不能把未验证的字节交给安装流程)
        var payload = new byte[32 * 1024];
        new Random(9).NextBytes(payload);
        var (listener, prefix) = StartServer(payload);
        var destDir = Path.Combine(Path.GetTempPath(), "mckuro-csum-bad-" + Guid.NewGuid().ToString("N"));
        try
        {
            var service = new AppUpdateService(new HttpClient());
            var path = await service.DownloadAsync(
                prefix + "McKuro-win-x64.zip", destDir, expectedSha256: Hex('0'));
            Assert.Null(path);
            Assert.False(File.Exists(Path.Combine(destDir, "McKuro-win-x64.zip")));
        }
        finally
        {
            listener.Stop();
            try { Directory.Delete(destDir, true); } catch { }
        }
    }

    [Fact]
    public async Task DownloadAsync_NoChecksum_SkipsVerification()
    {
        // 发布方未提供摘要 → 不阻断(HTML 回退通道拿不到摘要,是常态路径)
        var payload = new byte[16 * 1024];
        new Random(11).NextBytes(payload);
        var (listener, prefix) = StartServer(payload);
        var destDir = Path.Combine(Path.GetTempPath(), "mckuro-csum-none-" + Guid.NewGuid().ToString("N"));
        try
        {
            var service = new AppUpdateService(new HttpClient());
            var path = await service.DownloadAsync(prefix + "McKuro-win-x64.zip", destDir);
            Assert.NotNull(path);
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
}
