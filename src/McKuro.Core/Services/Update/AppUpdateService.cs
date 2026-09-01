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
}

/// <summary>
/// 应用自更新服务(对齐 Haiyu 的 IUpdateService + UpdateAppViewModel):
/// 检查 GitHub Releases 最新版、下载安装包;版本比较复用 GameUpdater.IsVersionOlder 语义。
/// 资产规则:优先 zip 绿色包(McKuro-win-x64-*.zip,解压替换),其次 exe 安装包(静默安装)。
/// 检查通道:API 优先(匿名限 60 次/小时/IP),失败自动回退 HTML 通道(302 取 tag +
/// expanded_assets 取资产),共享 IP 配额耗尽或部分网络 api.github.com 不可达时仍可更新。
/// </summary>
public sealed class AppUpdateService
{
    private readonly HttpClient _http;

    public AppUpdateService(HttpClient http)
    {
        _http = http;
    }

    /// <summary>版本比较:当前版本低于远程版本(需更新);复用 GameUpdater 的数值比较语义。</summary>
    public static bool IsNewer(string currentVersion, string remoteVersion) =>
        McKuro.Core.Services.Game.GameUpdater.IsVersionOlder(currentVersion, remoteVersion);

    /// <summary>检查指定 GitHub 仓库(owner/repo)的最新 Release:API 优先,失败回退 HTML 通道。</summary>
    public async Task<AppUpdateInfo?> CheckAsync(string repo, CancellationToken ct = default)
    {
        var trimmed = repo.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return null;
        }

        return await CheckViaApiAsync(trimmed, ct).ConfigureAwait(false)
            ?? await CheckViaHtmlAsync(trimmed, ct).ConfigureAwait(false);
    }

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

            var asset = release.Assets?
                .Where(a => !string.IsNullOrWhiteSpace(a.Name) &&
                            (a.Name!.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                             a.Name!.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(a => a.Name!.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();
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
            };
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
                .Where(n => n.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                            || n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                .ToList();
            var asset = names.FirstOrDefault(n => n.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                ?? names.FirstOrDefault();
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
            };
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>下载安装包到目标目录,返回本地路径;失败返回 null。</summary>
    public async Task<string?> DownloadAsync(
        string url,
        string destDir,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        try
        {
            Directory.CreateDirectory(destDir);
            var fileName = Path.GetFileName(new Uri(url).AbsolutePath);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = "McKuro-Setup.exe";
            }
            var destPath = Path.Combine(destDir, fileName);

            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? -1;

            await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var target = new FileStream(destPath, FileMode.Create, FileAccess.Write,
                FileShare.None, 128 * 1024, useAsync: true);
            var buffer = new byte[128 * 1024];
            long downloaded = 0;
            while (true)
            {
                int read = await source.ReadAsync(buffer, ct).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }
                await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                downloaded += read;
                progress?.Report(total > 0 ? (double)downloaded / total : 0);
            }
            await target.FlushAsync(ct).ConfigureAwait(false);
            return destPath;
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

[JsonSerializable(typeof(GitHubRelease))]
[JsonSerializable(typeof(List<GitHubAsset>))]
public sealed partial class GitHubJsonContext : JsonSerializerContext;
