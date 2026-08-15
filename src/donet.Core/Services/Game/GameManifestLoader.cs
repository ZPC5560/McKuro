using System.Text.Json;
using System.Text.Json.Serialization;
using donet.Core.Models.Game;

namespace donet.Core.Services.Game;

/// <summary>清单加载结果。</summary>
public sealed class ManifestLoadResult
{
    public required bool Success { get; init; }
    public string? Message { get; init; }
    public GameManifest? Manifest { get; init; }
    public string? ServerVersion { get; init; }
    public bool HasPredownload { get; init; }
    public string? PredownloadVersion { get; init; }
}

/// <summary>
/// 游戏更新清单加载器:
/// 支持两类来源——1) 通用 GameManifest JSON;2) 库洛官方 index.json 协议(自动适配)。
/// </summary>
public sealed class GameManifestLoader
{
    private readonly HttpClient _http;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        TypeInfoResolver = GameJsonContext.Default,
    };

    public GameManifestLoader(HttpClient http)
    {
        _http = http;
    }

    /// <summary>
    /// 从库洛官方 index.json 加载当前版本清单。
    /// </summary>
    /// <param name="indexUrl">index.json 地址(不同服务器渠道)。</param>
    /// <param name="preDownload">为 true 时加载预下载清单。</param>
    public async Task<ManifestLoadResult> LoadKuroAsync(
        string indexUrl,
        bool preDownload = false,
        CancellationToken ct = default)
    {
        try
        {
            using var indexResponse = await _http.GetAsync(indexUrl, ct).ConfigureAwait(false);
            indexResponse.EnsureSuccessStatusCode();
            var indexJson = await indexResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var index = JsonSerializer.Deserialize(indexJson, GameJsonContext.Default.KuroIndex);
            if (index is null)
            {
                return Fail("解析 index.json 失败");
            }

            var updateData = preDownload ? index.Predownload : index.Default;
            if (updateData is null)
            {
                return new ManifestLoadResult
                {
                    Success = false,
                    Message = preDownload ? "当前无预下载内容" : "index.json 缺少 default 节点",
                };
            }

            var resourceJsonUrl = updateData.ResourceJsonUrl;
            var fileList = index.GameResourceList?.Resource ?? [];
            if (!string.IsNullOrEmpty(resourceJsonUrl) && preDownload is false)
            {
                // 完整文件清单在 resource.json 中
                try
                {
                    using var resourceResponse = await _http.GetAsync(resourceJsonUrl, ct).ConfigureAwait(false);
                    resourceResponse.EnsureSuccessStatusCode();
                    var resourceJson = await resourceResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    var resourceList = JsonSerializer.Deserialize(resourceJson, GameJsonContext.Default.KuroGameResourceList);
                    if (resourceList?.Resource is { Count: > 0 })
                    {
                        fileList = resourceList.Resource;
                    }
                }
                catch (Exception)
                {
                    // resource.json 不可用时退回 index.json 内嵌清单
                }
            }

            var cdnUrl = updateData.CdnList?.FirstOrDefault()?.Url?.TrimEnd('/') ?? "";
            var basePath = (updateData.ResourcesBasePath ?? "").TrimStart('/');
            var manifest = new GameManifest
            {
                Version = updateData.Version ?? "",
                KeyFiles = index.KeyFileCheckList ?? [],
            };
            foreach (var file in fileList)
            {
                if (string.IsNullOrWhiteSpace(file.Dest))
                {
                    continue;
                }

                manifest.Files.Add(new GameFileEntry
                {
                    Path = NormalizePath(file.Dest),
                    Size = file.Size ?? 0,
                    Md5 = file.Md5 ?? "",
                    // 下载地址 = CDN + resourcesBasePath + dest
                    Url = !string.IsNullOrEmpty(cdnUrl) ? $"{cdnUrl}/{basePath}{file.Dest}" : null,
                });
            }

            return new ManifestLoadResult
            {
                Success = true,
                Manifest = manifest,
                ServerVersion = manifest.Version,
                HasPredownload = index.Predownload is not null,
                PredownloadVersion = index.Predownload?.Version,
            };
        }
        catch (Exception ex)
        {
            return Fail($"获取更新清单失败: {ex.Message}");
        }
    }

    /// <summary>从通用 GameManifest JSON 地址加载。</summary>
    public async Task<ManifestLoadResult> LoadGenericAsync(
        string manifestUrl,
        CancellationToken ct = default)
    {
        try
        {
            using var response = await _http.GetAsync(manifestUrl, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var manifest = await JsonSerializer.DeserializeAsync(
                stream,
                GameJsonContext.Default.GameManifest,
                ct).ConfigureAwait(false);
            if (manifest is null)
            {
                return Fail("解析清单失败");
            }

            return new ManifestLoadResult
            {
                Success = true,
                Manifest = manifest,
                ServerVersion = manifest.Version,
            };
        }
        catch (Exception ex)
        {
            return Fail($"获取清单失败: {ex.Message}");
        }
    }

    private static ManifestLoadResult Fail(string message) =>
        new() { Success = false, Message = message };

    private static string NormalizePath(string path) =>
        path.Replace('\\', '/').TrimStart('/');
}

[JsonSerializable(typeof(KuroIndex))]
[JsonSerializable(typeof(KuroUpdateData))]
[JsonSerializable(typeof(KuroCdnData))]
[JsonSerializable(typeof(KuroConfig))]
[JsonSerializable(typeof(KuroGameResourceList))]
[JsonSerializable(typeof(KuroFileInfo))]
[JsonSerializable(typeof(KuroChunkInfo))]
[JsonSerializable(typeof(GameManifest))]
[JsonSerializable(typeof(GameFileEntry))]
[JsonSerializable(typeof(List<GameFileEntry>))]
public sealed partial class GameJsonContext : JsonSerializerContext;
