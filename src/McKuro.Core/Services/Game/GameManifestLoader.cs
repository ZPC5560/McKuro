using System.Text.Json;
using System.Text.Json.Serialization;
using McKuro.Core.Models.Game;

namespace McKuro.Core.Services.Game;

/// <summary>清单加载结果。</summary>
public sealed class ManifestLoadResult
{
    public required bool Success { get; init; }
    public string? Message { get; init; }
    public GameManifest? Manifest { get; init; }
    public string? ServerVersion { get; init; }
    public bool HasPredownload { get; init; }
    public string? PredownloadVersion { get; init; }

    /// <summary>预下载下载体积(预下载节点的 config.patchConfig 最新项 size;0 表示未知)。</summary>
    public long PredownloadDownloadBytes { get; init; }

    /// <summary>预下载所需磁盘空间(ext.requiredDiskSpace;0 表示未知)。</summary>
    public long PredownloadDiskBytes { get; init; }
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
            if (!string.IsNullOrEmpty(resourceJsonUrl))
            {
                // 完整文件清单在 resource.json 中(默认清单与预下载节点各有自己的 resource.json)
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

            // 预下载体积:预下载节点无 cdnList,resource.json 拿不到全量清单,
            // 改用 config.patchConfig 最新项(官方"预下载"显示值)或 config 本身
            var (pdDownload, pdDisk) = ExtractPredownloadSizes(index.Predownload);

            return new ManifestLoadResult
            {
                Success = true,
                Manifest = manifest,
                ServerVersion = manifest.Version,
                HasPredownload = index.Predownload is not null,
                PredownloadVersion = index.Predownload?.Version,
                PredownloadDownloadBytes = pdDownload,
                PredownloadDiskBytes = pdDisk,
            };
        }
        catch (Exception ex)
        {
            return Fail($"获取更新清单失败: {ex.Message}");
        }
    }

    /// <summary>从预下载节点提取下载体积与所需磁盘空间(优先最新 patchConfig,回退 config)。</summary>
    private static (long Download, long Disk) ExtractPredownloadSizes(KuroUpdateData? predownload)
    {
        if (predownload?.Config is null)
        {
            return (0, 0);
        }
        var cfg = predownload.Config;
        // 最新补丁为 patchConfig 最后一项
        KuroPatchConfig? latest = cfg.PatchConfig is { Count: > 0 } ? cfg.PatchConfig[^1] : null;
        var download = latest?.Size ?? cfg.Size ?? 0;
        var disk = latest?.Ext?.RequiredDiskSpace ?? 0;
        if (disk <= 0 && latest?.UnCompressSize is { } un and > 0)
        {
            disk = un;
        }
        return (download, disk);
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

    /// <summary>
    /// 加载官方补丁清单(indexFile.json)。补丁清单只列出"需要更新的差异/zip 文件",
    /// 不做全量 MD5 校验 —— 对齐 Haiyu 的预下载逻辑(patchConfig → indexFile → 差异下载)。
    /// 下载 URL = CDN + entry.FromFolder + entry.Dest(Haiyu GetBaseUrl 用 FromFolder)。
    /// </summary>
    public async Task<ManifestLoadResult> LoadPatchAsync(
        string patchIndexUrl,
        CancellationToken ct = default)
    {
        try
        {
            using var response = await _http.GetAsync(patchIndexUrl, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var patch = JsonSerializer.Deserialize(json, GameJsonContext.Default.KuroPatchIndex);
            if (patch is null)
            {
                return Fail("解析补丁清单失败");
            }

            var manifest = new GameManifest
            {
                Version = "",
            };
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void AddEntry(KuroPatchEntry? e, string? fromFolder)
            {
                if (e?.Dest is null || !seen.Add(e.Dest))
                {
                    return;
                }
                var folder = e.FromFolder ?? fromFolder ?? "";
                manifest.Files.Add(new GameFileEntry
                {
                    Path = NormalizePath(e.Dest),
                    Size = e.Size ?? 0,
                    Md5 = e.Md5 ?? "",
                    // 相对 CDN 的下载地址(FromFolder 通常为 .../zip/)
                    Url = folder.TrimStart('/') + "/" + NormalizePath(e.Dest),
                });
            }

            if (patch.Resource is { Count: > 0 })
            {
                foreach (var e in patch.Resource)
                {
                    AddEntry(e, null);
                }
            }
            else
            {
                foreach (var f in patch.PatchInfos ?? [])
                {
                    foreach (var e in f.Entries ?? [])
                    {
                        AddEntry(e, null);
                    }
                }
            }
            foreach (var z in patch.ZipInfos ?? [])
            {
                foreach (var e in z.Entries ?? [])
                {
                    AddEntry(e, null);
                }
            }

            return new ManifestLoadResult
            {
                Success = true,
                Manifest = manifest,
            };
        }
        catch (Exception ex)
        {
            return Fail($"获取补丁清单失败: {ex.Message}");
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
[JsonSerializable(typeof(KuroPatchConfig))]
[JsonSerializable(typeof(KuroPatchExt))]
[JsonSerializable(typeof(KuroGameResourceList))]
[JsonSerializable(typeof(KuroFileInfo))]
[JsonSerializable(typeof(KuroChunkInfo))]
[JsonSerializable(typeof(KuroPatchIndex))]
[JsonSerializable(typeof(KuroPatchFile))]
[JsonSerializable(typeof(KuroPatchEntry))]
[JsonSerializable(typeof(KuroPatchGroup))]
[JsonSerializable(typeof(KuroPatchZip))]
[JsonSerializable(typeof(GameManifest))]
[JsonSerializable(typeof(GameFileEntry))]
[JsonSerializable(typeof(List<GameFileEntry>))]
public sealed partial class GameJsonContext : JsonSerializerContext;
