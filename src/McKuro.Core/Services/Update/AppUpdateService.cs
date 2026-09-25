using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace McKuro.Core.Services.Update;

/// <summary>应用更新信息(来自 GitHub Release)。</summary>
public sealed class AppUpdateInfo
{
    public required string Version { get; init; }
    public required string AssetName { get; init; }
    public required long AssetSize { get; init; }
    public required string DownloadUrl { get; init; }

    /// <summary>发布方提供的 sha256(小写 hex);未提供时为 null,此时跳过校验不阻断更新。</summary>
    public string? Sha256 { get; init; }

    /// <summary>Release 页面地址(供 UI「查看发布说明」跳转);可能为 null。</summary>
    public string? ReleaseUrl { get; init; }
}

/// <summary>
/// 应用自更新服务(对齐 Haiyu 的 IUpdateService + UpdateAppViewModel):
/// 检查 GitHub Releases 最新版、下载安装包;版本比较复用 GameUpdater.IsVersionOlder 语义。
/// 资产规则:<see cref="PickAsset"/>——先按当前平台过滤(win/osx 各自的 zip 优先,
/// Windows 回退 setup exe 静默安装;Linux 无自动更新资产不提示),防止跨平台误下(如 Windows 下到 osx 包)。
/// 检查通道:API 优先(匿名限 60 次/小时/IP),失败自动回退 HTML 通道(302 取 tag +
/// expanded_assets 取资产),共享 IP 配额耗尽或部分网络 api.github.com 不可达时仍可更新。
/// </summary>
public sealed class AppUpdateService
{
    private readonly HttpClient _http;

    /// <summary>Releases 结果缓存(对齐 Haiyu 的 5 分钟 <c>_cacheInfo</c>):避免启动自动检查 +
    /// 用户手动点击在短时间内重复打满匿名 API 配额(60 次/小时/IP)。</summary>
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly SemaphoreSlim _cacheGate = new(1, 1);
    private AppUpdateInfo? _cached;
    private DateTime _cachedAt;

    public AppUpdateService(HttpClient http)
    {
        _http = http;
    }

    /// <summary>版本比较:当前版本低于远程版本(需更新);复用 GameUpdater 的数值比较语义。</summary>
    public static bool IsNewer(string currentVersion, string remoteVersion) =>
        McKuro.Core.Services.Game.GameUpdater.IsVersionOlder(currentVersion, remoteVersion);

    /// <summary>检查指定 GitHub 仓库(owner/repo)的最新 Release:API 优先,失败回退 HTML 通道。
    /// 结果缓存 5 分钟(<paramref name="forceRefresh"/> 为真时绕过缓存,供用户手动点「检查更新」)。</summary>
    public async Task<AppUpdateInfo?> CheckAsync(
        string repo,
        CancellationToken ct = default,
        bool forceRefresh = false,
        string? accelerator = null,
        string? mirrorUrl = null)
    {
        var trimmed = repo.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return null;
        }

        if (!forceRefresh && TryGetCached(out var cached))
        {
            return ApplyAccelerator(cached!, accelerator);
        }

        await _cacheGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // 双检:并发调用(启动自动检查 + 用户手动点击)只发一次网络请求
            if (!forceRefresh && TryGetCached(out cached))
            {
                return ApplyAccelerator(cached!, accelerator);
            }

            var info = await CheckViaMirrorAsync(mirrorUrl, ct).ConfigureAwait(false)
                ?? await CheckViaApiAsync(trimmed, ct).ConfigureAwait(false)
                ?? await CheckViaHtmlAsync(trimmed, ct).ConfigureAwait(false);

            _cached = info;
            _cachedAt = DateTime.UtcNow;
            return ApplyAccelerator(info, accelerator);
        }
        finally
        {
            _cacheGate.Release();
        }
    }

    /// <summary>把加速模板套到下载地址上(模板无效时原样返回)。</summary>
    private static AppUpdateInfo? ApplyAccelerator(AppUpdateInfo? info, string? accelerator)
    {
        if (info is null || !GitHubIpFronting.IsValidAccelerator(accelerator))
        {
            return info;
        }
        var accelerated = GitHubIpFronting.ApplyAccelerator(accelerator, info.DownloadUrl);
        return string.Equals(accelerated, info.DownloadUrl, StringComparison.Ordinal)
            ? info
            : new AppUpdateInfo
            {
                Version = info.Version,
                AssetName = info.AssetName,
                AssetSize = info.AssetSize,
                DownloadUrl = accelerated,
                Sha256 = info.Sha256,
                ReleaseUrl = info.ReleaseUrl,
            };
    }

    private bool TryGetCached(out AppUpdateInfo? info)
    {
        info = _cached;
        return info is not null && DateTime.UtcNow - _cachedAt <= CacheTtl;
    }

    /// <summary>清空缓存(测试与「强制刷新」用)。</summary>
    public void InvalidateCache()
    {
        _cached = null;
        _cachedAt = default;
    }

    /// <summary>
    /// 镜像源(mirrorchyan,对齐 Haiyu <c>MirrorUpdateService</c>):国内可达性最好的一条通道。
    /// 仅在用户显式配置镜像地址时启用;返回的版本/资产交给统一平台过滤规则,避免镜像与实际平台错配。
    /// 镜像响应中带 md5 而非 sha256,故此处只透出大小,不做摘要绑定。
    /// </summary>
    private async Task<AppUpdateInfo?> CheckViaMirrorAsync(string? mirrorUrl, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(mirrorUrl))
        {
            return null;
        }
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, mirrorUrl.Trim());
            request.Headers.TryAddWithoutValidation("User-Agent", "McKuro");
            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }
            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var mirror = JsonSerializer.Deserialize(json, GitHubJsonContext.Default.MirrorResponse);
            if (mirror is null || mirror.Code != 0 || mirror.Data is null)
            {
                return null;
            }
            if (string.IsNullOrWhiteSpace(mirror.Data.Url) || string.IsNullOrWhiteSpace(mirror.Data.VersionName))
            {
                return null;
            }

            return new AppUpdateInfo
            {
                Version = mirror.Data.VersionName.TrimStart('v', 'V'),
                AssetName = Path.GetFileName(new Uri(mirror.Data.Url).AbsolutePath),
                AssetSize = mirror.Data.Filesize ?? 0,
                DownloadUrl = mirror.Data.Url,
                Sha256 = null,
                ReleaseUrl = $"https://github.com/{RepoOf(mirror.Data)}/releases",
            };
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string RepoOf(MirrorData data) =>
        string.IsNullOrWhiteSpace(data.Repo) ? "ZPC5560/McKuro" : data.Repo;

    /// <summary>标准通道:GitHub Releases API(匿名限 60 次/小时/IP)。</summary>
    private async Task<AppUpdateInfo?> CheckViaApiAsync(string trimmed, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"https://api.github.com/repos/{trimmed}/releases/latest");
            request.Headers.TryAddWithoutValidation("User-Agent", "McKuro");
            request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }
            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var release = JsonSerializer.Deserialize(json, GitHubJsonContext.Default.GitHubRelease);
            if (release is null || string.IsNullOrWhiteSpace(release.TagName))
            {
                return null;
            }

            var picked = PickAsset(release.Assets?.Select(a => a.Name) ?? []);
            var asset = picked is null
                ? null
                : release.Assets!.FirstOrDefault(a => string.Equals(a.Name, picked, StringComparison.Ordinal));
            if (asset is null || string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl))
            {
                return null;
            }

            return new AppUpdateInfo
            {
                Version = release.TagName.TrimStart('v', 'V'),
                AssetName = asset.Name!,
                AssetSize = asset.Size ?? 0,
                DownloadUrl = asset.BrowserDownloadUrl!,
                Sha256 = UpdateChecksum.ParseSha256(release.Body, asset.Name!)
                         ?? await FetchAssetSha256Async(release, asset.Name!, ct).ConfigureAwait(false),
                ReleaseUrl = string.IsNullOrWhiteSpace(release.HtmlUrl) ? null : release.HtmlUrl,
            };
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// 资产旁若附有 <c>&lt;资产名&gt;.sha256</c> 小文件则取用其摘要(发布方未在正文写摘要时的兜底)。
    /// 拉取失败一律返回 null(视为"无摘要",不阻断更新)。
    /// </summary>
    private async Task<string?> FetchAssetSha256Async(GitHubRelease release, string assetName, CancellationToken ct)
    {
        var manifest = release.Assets?.FirstOrDefault(a =>
            string.Equals(a.Name, assetName + ".sha256", StringComparison.OrdinalIgnoreCase));
        if (manifest is null || string.IsNullOrWhiteSpace(manifest.BrowserDownloadUrl))
        {
            return null;
        }
        try
        {
            var text = await _http.GetStringAsync(manifest.BrowserDownloadUrl, ct).ConfigureAwait(false);
            return UpdateChecksum.ParseSingleSha256(text)
                   ?? UpdateChecksum.ParseSha256(text, assetName);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// HTML 回退通道:匿名 API 配额耗尽/不可达(共享 IP、部分网络对 api.github.com 不稳)时使用。
    /// GET github.com/{repo}/releases/latest 会 302 到最新 tag 页,取最终 URL 解析版本号;
    /// 再抓 releases/expanded_assets/{tag} 片段解析资产下载链接(该端点无 API 配额限制;
    /// 片段不含文件大小,AssetSize 报 0,UI 侧自动省略大小)。
    /// </summary>
    private async Task<AppUpdateInfo?> CheckViaHtmlAsync(string trimmed, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://github.com/{trimmed}/releases/latest");
            request.Headers.TryAddWithoutValidation("User-Agent", "McKuro");
            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }
            var tagMatch = Regex.Match(response.RequestMessage?.RequestUri?.ToString() ?? "", @"/releases/tag/([^/?#]+)");
            if (!tagMatch.Success)
            {
                return null; // 无 Release 时 GitHub 直接 200 返回 releases 页,无 tag 段
            }
            var tag = Uri.UnescapeDataString(tagMatch.Groups[1].Value);

            var fragment = await _http.GetStringAsync(
                $"https://github.com/{trimmed}/releases/expanded_assets/{Uri.EscapeDataString(tag)}", ct)
                .ConfigureAwait(false);
            var names = Regex.Matches(fragment, $"href=\"/{Regex.Escape(trimmed)}/releases/download/[^\"/]+/([^\"?]+)\"")
                .Select(m => Uri.UnescapeDataString(m.Groups[1].Value))
                .ToList();
            var asset = PickAsset(names);
            if (asset is null)
            {
                return null;
            }

            return new AppUpdateInfo
            {
                Version = tag.TrimStart('v', 'V'),
                AssetName = asset,
                AssetSize = 0,
                DownloadUrl = $"https://github.com/{trimmed}/releases/download/{tag}/{Uri.EscapeDataString(asset)}",
                // 该通道不含 Release 正文,摘要留给下载后的 *.sha256 见证/用户自校验
                Sha256 = null,
                ReleaseUrl = $"https://github.com/{trimmed}/releases/tag/{tag}",
            };
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// 从资产名集合挑选当前平台可自动更新的资产(API 与 HTML 通道共用规则)。
    /// 命名约定:McKuro-win-x64-*.zip / McKuro-setup-*.exe / McKuro-osx-{arm64,x64}[-app].zip / McKuro-linux-x64-*.tar.gz。
    /// 规则:先按当前平台过滤(修复:Windows 曾误选 osx 包);zip 优先于 exe;
    /// mac 平铺 zip 优先于 .app.zip(后者解压会把 McKuro.app 嵌套进安装目录);
    /// Linux 无 zip 资产(tar.gz 需手动安装)→ 返回 null 不提示自动更新。
    /// platform/arch 参数仅供单测注入("win"/"osx"/"linux" + "x64"/"arm64")。
    /// </summary>
    public static string? PickAsset(IEnumerable<string?> names, string? platform = null, string? arch = null)
    {
        platform ??= System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows)
            ? "win"
            : System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX)
                ? "osx"
                : "linux";
        arch ??= System.Runtime.InteropServices.RuntimeInformation.OSArchitecture
            == System.Runtime.InteropServices.Architecture.Arm64 ? "arm64" : "x64";

        var candidates = names
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Where(n => IsPlatformAsset(n!, platform, arch))
            .ToList();

        return candidates.FirstOrDefault(n => n!.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                                              && !n.Contains(".app.", StringComparison.OrdinalIgnoreCase))
               ?? candidates.FirstOrDefault(n => n!.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
               ?? (platform == "win"
                   ? candidates.FirstOrDefault(n => n!.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                   : null);
    }

    private static bool IsPlatformAsset(string name, string platform, string arch) => platform switch
    {
        "win" => name.StartsWith("McKuro-win-", StringComparison.OrdinalIgnoreCase)
                 || name.StartsWith("McKuro-setup-", StringComparison.OrdinalIgnoreCase),
        "osx" => name.StartsWith($"McKuro-osx-{arch}", StringComparison.OrdinalIgnoreCase),
        _ => false, // linux:无自动更新资产
    };

    /// <summary>下载安装包到目标目录,返回本地路径;失败返回 null。
    /// 可靠性:独立长超时 HttpClient(共享客户端 60s 超时对大文件/慢速代理不够);
    /// 系统代理自动注入(macOS 上 DefaultProxy 不读系统设置);
    /// GitHub IP 域前置(见 <see cref="GitHubIpFronting"/>,默认关闭,应对 DNS 污染/解析不可达);
    /// 断点续传(.part 分块 + HTTP Range)+ 卡死重试(单次读取 60s 无数据即断,最多 4 次)。
    /// <paramref name="expectedSha256"/> 非空时校验下载产物,摘要不符则丢弃并重试(见 <see cref="UpdateChecksum"/>)。</summary>
    public async Task<string?> DownloadAsync(
        string url,
        string destDir,
        IProgress<double>? progress = null,
        CancellationToken ct = default,
        string? expectedSha256 = null)
    {
        const int maxAttempts = 4;
        try
        {
            Directory.CreateDirectory(destDir);
            var fileName = Path.GetFileName(new Uri(url).AbsolutePath);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = "McKuro-Setup.exe";
            }
            var destPath = Path.Combine(destDir, fileName);
            var partPath = destPath + ".part";

            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    var resumeFrom = File.Exists(partPath) ? new FileInfo(partPath).Length : 0;
                    var detectedProxy = McKuro.Core.Services.Infrastructure.SystemProxyDetector.Detect();
                    System.Console.Error.WriteLine(
                        $"MCKURO-UPDATE dl: 第 {attempt}/{maxAttempts} 次 resume={resumeFrom} proxy={(detectedProxy is null ? "默认" : detectedProxy.ToString())} fronting={GitHubIpFronting.Enabled}");
                    using var request = new HttpRequestMessage(HttpMethod.Get, url);
                    if (resumeFrom > 0)
                    {
                        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(resumeFrom, null);
                    }
                    // 域前置 handler 内部已注入系统代理并设定连接池/超时参数
                    using var downloader = new HttpClient(GitHubIpFronting.CreateHandler(detectedProxy))
                    {
                        Timeout = TimeSpan.FromMinutes(15),
                    };
                    using var response = await downloader
                        .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                        .ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();
                    var resumed = (int)response.StatusCode == 206 && resumeFrom > 0;
                    var total = response.Content.Headers.ContentLength.HasValue
                        ? response.Content.Headers.ContentLength.Value + (resumed ? resumeFrom : 0)
                        : -1;
                    if (!resumed)
                    {
                        resumeFrom = 0;
                    }

                    await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                    await using (var target = new FileStream(partPath, resumed ? FileMode.Append : FileMode.Create,
                        FileAccess.Write, FileShare.None, 128 * 1024, useAsync: true))
                    {
                        var buffer = new byte[128 * 1024];
                        var downloaded = resumeFrom;
                        while (true)
                        {
                            // 单次读取 60 秒无数据视为连接卡死 → 抛出重试(断点续传)
                            using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                            readCts.CancelAfter(TimeSpan.FromSeconds(60));
                            int read;
                            try
                            {
                                read = await source.ReadAsync(buffer, readCts.Token).ConfigureAwait(false);
                            }
                            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                            {
                                throw new TimeoutException("下载连接 60 秒无数据");
                            }
                            if (read == 0)
                            {
                                break;
                            }
                            await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                            downloaded += read;
                            progress?.Report(total > 0 ? (double)downloaded / total : 0);
                        }
                    }
                    // 完整性校验:仅在发布方提供摘要时执行。不符说明传输损坏或被篡改,
                    // 删除 .part 让下一轮从头下载(已下载字节不可信,不能续传)。
                    if (!string.IsNullOrWhiteSpace(expectedSha256))
                    {
                        var actual = await UpdateChecksum.ComputeSha256Async(partPath, ct).ConfigureAwait(false);
                        if (!UpdateChecksum.Matches(expectedSha256, actual))
                        {
                            System.Console.Error.WriteLine(
                                $"MCKURO-UPDATE dl: sha256 不符 期望={expectedSha256} 实际={actual ?? "计算失败"}");
                            try
                            {
                                File.Delete(partPath);
                            }
                            catch (Exception)
                            {
                                // 删除失败不影响重试判定,下一轮 Create 会覆盖
                            }
                            throw new InvalidDataException("下载包 sha256 校验失败");
                        }
                        System.Console.Error.WriteLine("MCKURO-UPDATE dl: sha256 校验通过");
                    }

                    File.Move(partPath, destPath, true);
                    return destPath;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return null;
                }
                catch (Exception ex)
                {
                    System.Console.Error.WriteLine(
                        $"MCKURO-UPDATE dl: 第 {attempt}/{maxAttempts} 次失败: {ex.GetType().Name} {ex.Message}");
                    if (attempt >= maxAttempts)
                    {
                        return null;
                    }
                    await Task.Delay(TimeSpan.FromSeconds(attempt * 2), CancellationToken.None).ConfigureAwait(false);
                }
            }
        }
        catch (Exception)
        {
            return null;
        }
    }
}

/// <summary>GitHub Releases API 响应模型(snake_case 字段)。</summary>
public sealed class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string? TagName { get; set; }

    /// <summary>Release 正文(Markdown);发布方常在此写 sha256 摘要。</summary>
    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; set; }

    [JsonPropertyName("assets")]
    public List<GitHubAsset>? Assets { get; set; }
}

public sealed class GitHubAsset
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("size")]
    public long? Size { get; set; }

    [JsonPropertyName("browser_download_url")]
    public string? BrowserDownloadUrl { get; set; }
}

/// <summary>镜像源(mirrorchyan 风格)响应模型,对齐 Haiyu <c>MirrorReponseModel</c>。</summary>
public sealed class MirrorResponse
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("data")]
    public MirrorData? Data { get; set; }
}

public sealed class MirrorData
{
    [JsonPropertyName("version_name")]
    public string? VersionName { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("filesize")]
    public long? Filesize { get; set; }

    /// <summary>镜像返回的源仓库("owner/repo"),仅用于拼发布页地址。</summary>
    [JsonPropertyName("repo")]
    public string? Repo { get; set; }
}

[JsonSerializable(typeof(GitHubRelease))]
[JsonSerializable(typeof(List<GitHubAsset>))]
[JsonSerializable(typeof(MirrorResponse))]
public sealed partial class GitHubJsonContext : JsonSerializerContext;
