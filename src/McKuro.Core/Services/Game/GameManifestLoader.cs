using System.Diagnostics;
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

    /// <summary>预下载节点完整数据(含 patchConfig 与 config;index.json 有预下载时非 null)。</summary>
    public KuroUpdateData? Predownload { get; init; }

    /// <summary>默认节点(用于取 CDN 列表拼预下载资源 URL)。</summary>
    public KuroUpdateData? DefaultData { get; init; }

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

            var cdnUrl = (await SelectCdnAsync(updateData.CdnList, updateData.Resources, ct).ConfigureAwait(false))?.TrimEnd('/') ?? "";
            var resourceJsonUrl = !string.IsNullOrWhiteSpace(cdnUrl) && !string.IsNullOrWhiteSpace(updateData.Resources)
                ? cdnUrl + "/" + updateData.Resources.TrimStart('/')
                : null;
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
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception)
                {
                    // resource.json 不可用时退回 index.json 内嵌清单
                }
            }

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

                var entry = new GameFileEntry
                {
                    Path = NormalizePath(file.Dest),
                    Size = file.Size ?? 0,
                    Md5 = file.Md5 ?? "",
                    // 下载地址 = CDN + resourcesBasePath + dest
                    Url = !string.IsNullOrEmpty(cdnUrl) ? $"{cdnUrl}/{basePath}{file.Dest}" : null,
                };
                CopyChunks(entry, file.ChunkInfos);
                manifest.Files.Add(entry);
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
                Predownload = index.Predownload,
                DefaultData = index.Default,
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
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

    // ---- 新版协议:按资源等级(pack)拆分资源 ----

    /// <summary>
    /// 加载新版协议的 index.json(<c>.../launcher/game/{appKey}/G152/official/index.json</c>),
    /// 解析出各资源包(resourcePacks)与资源等级组合(bundles)。
    /// 网络/HTTP 失败时抛出,由调用方(ResourceLevelService)转换为可读提示。
    /// </summary>
    /// <remarks>
    /// 该端点实测**始终返回 gzip 压缩体**(即使请求未带 Accept-Encoding),因此这里按字节读取后
    /// 自行按 gzip 魔数解压,不依赖调用方 HttpClient 的 AutomaticDecompression 配置。
    /// </remarks>
    public async Task<KuroResourcePackIndex?> LoadResourcePackIndexAsync(
        string indexUrl,
        CancellationToken ct = default)
    {
        using var response = await _http.GetAsync(indexUrl, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        payload = DecompressIfNeeded(payload, response);
        return JsonSerializer.Deserialize(payload, GameJsonContext.Default.KuroResourcePackIndex);
    }

    /// <summary>
    /// 若响应体是 gzip/zlib 压缩体则解压,否则原样返回。
    /// <para>
    /// 只按压缩魔数判断,不看 <c>Content-Encoding</c>:该端点在未启用 AutomaticDecompression 的
    /// HttpClient 下会同时带回 <c>Content-Encoding: gzip</c> 与压缩体(此时头存在≠已解压);
    /// 而启用 AutomaticDecompression 时 .NET 会移除该头并已解压(魔数不匹配 → 不重复解压)。
    /// 两种情形都由魔数判定正确。
    /// </para>
    /// </summary>
    private static byte[] DecompressIfNeeded(byte[] payload, HttpResponseMessage response)
    {
        _ = response;
        if (payload.Length < 2)
        {
            return payload;
        }

        // gzip 魔数 0x1F 0x8B
        if (payload[0] == 0x1F && payload[1] == 0x8B)
        {
            try
            {
                using var input = new System.IO.MemoryStream(payload);
                using var gzip = new System.IO.Compression.GZipStream(input, System.IO.Compression.CompressionMode.Decompress);
                using var output = new System.IO.MemoryStream();
                gzip.CopyTo(output);
                return output.ToArray();
            }
            catch (Exception)
            {
                // 解压失败:原样返回,由 JSON 解析器给出可读错误
                return payload;
            }
        }

        // zlib 头(0x78 0x01/0x5E/0x9C/0xDA)
        bool isZlib = payload[0] == 0x78
            && payload.Length > 2
            && payload[1] is 0x01 or 0x5E or 0x9C or 0xDA;
        if (!isZlib)
        {
            return payload;
        }

        try
        {
            using var zinput = new System.IO.MemoryStream(payload);
            using var zstream = new System.IO.Compression.ZLibStream(zinput, System.IO.Compression.CompressionMode.Decompress);
            using var zout = new System.IO.MemoryStream();
            zstream.CopyTo(zout);
            return zout.ToArray();
        }
        catch (Exception)
        {
            return payload;
        }
    }

    /// <summary>
    /// 加载单个资源包的清单(indexFile.json),返回包内全部文件。
    /// 下载地址 = <c>{cdn}{baseUrl}{dest}</c>(baseUrl 以 zip/ 结尾,清单内无分片信息)。
    /// 同时回填 <see cref="KuroResourcePack.FileCount"/>/<see cref="KuroResourcePack.TotalBytes"/>。
    /// </summary>
    public async Task<ManifestLoadResult> LoadResourcePackAsync(
        KuroResourcePack pack,
        string cdnUrl,
        string? expectedVersion = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(pack.IndexFile))
        {
            return Fail("资源包缺少 indexFile");
        }

        try
        {
            var indexUrl = cdnUrl.TrimEnd('/') + "/" + pack.IndexFile.TrimStart('/');
            using var response = await _http.GetAsync(indexUrl, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var payload = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            payload = DecompressIfNeeded(payload, response);
            if (!string.IsNullOrWhiteSpace(pack.IndexFileMd5) && !HashMatches(payload, pack.IndexFileMd5))
            {
                return Fail("资源包清单 MD5 校验失败");
            }

            var parsed = JsonSerializer.Deserialize(payload, GameJsonContext.Default.KuroPatchIndex);
            var files = parsed?.Resource ?? [];
            var baseUrl = (pack.BaseUrl ?? "").TrimStart('/');
            var cdn = cdnUrl.TrimEnd('/');

            var manifest = new GameManifest { Version = pack.Version ?? expectedVersion ?? "" };
            foreach (var file in files)
            {
                if (string.IsNullOrWhiteSpace(file.Dest))
                {
                    continue;
                }

                var entry = new GameFileEntry
                {
                    Path = NormalizePath(file.Dest),
                    Size = file.Size ?? 0,
                    Md5 = file.Md5 ?? "",
                    Url = cdn + "/" + baseUrl + file.Dest,
                };
                CopyChunks(entry, file.ChunkInfos);
                manifest.Files.Add(entry);
            }

            pack.FileCount = manifest.Files.Count;
            pack.TotalBytes = manifest.Files.Sum(f => f.Size);
            return new ManifestLoadResult
            {
                Success = true,
                Manifest = manifest,
                ServerVersion = manifest.Version,
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Fail($"获取资源包清单失败: {ex.Message}");
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
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
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
        string? baseUrl = null,
        string? indexFileMd5 = null,
        CancellationToken ct = default)
    {
        try
        {
            using var response = await _http.GetAsync(patchIndexUrl, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var payload = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(indexFileMd5) && !HashMatches(payload, indexFileMd5))
            {
                return Fail("补丁清单 MD5 校验失败");
            }

            var patch = JsonSerializer.Deserialize(payload, GameJsonContext.Default.KuroPatchIndex);
            if (patch is null)
            {
                return Fail("解析补丁清单失败");
            }

            var effectiveBase = (baseUrl ?? "").TrimEnd('/') + "/";
            var manifest = new GameManifest { Version = "" };
            var plan = new GamePatchPlan { BaseUrl = effectiveBase, IndexFileMd5 = indexFileMd5 };
            var packageCache = new Dictionary<string, GameFileEntry>(StringComparer.OrdinalIgnoreCase);

            GameFileEntry PackageEntry(KuroPatchEntry e, string? folder)
            {
                var key = NormalizePath(e.Dest ?? "");
                if (packageCache.TryGetValue(key, out var cached))
                {
                    return cached;
                }
                var folderPath = (folder ?? "").Trim('/');
                var relative = string.IsNullOrEmpty(folderPath) ? key : folderPath + "/" + key;
                var urlBase = folderPath.StartsWith("launcher/", StringComparison.OrdinalIgnoreCase)
                    ? effectiveBase[..Math.Max(0, effectiveBase.IndexOf("launcher/", StringComparison.OrdinalIgnoreCase))]
                    : effectiveBase;
                var entry = new GameFileEntry
                {
                    Path = key,
                    Size = e.Size ?? 0,
                    Md5 = e.Md5 ?? "",
                    Url = IsAbsoluteUrl(relative) ? relative : urlBase + relative,
                };
                CopyChunks(entry, e.ChunkInfos);
                packageCache[key] = entry;
                manifest.Files.Add(entry);
                return entry;
            }

            void AddOrdinary(KuroPatchEntry e) => PackageEntry(e, e.FromFolder);
            foreach (var e in patch.Resource ?? [])
            {
                if (e?.Dest is null)
                {
                    continue;
                }
                if (e.Dest.EndsWith(".krdiff", StringComparison.OrdinalIgnoreCase))
                {
                    plan.DiffPackages.Add(new GamePatchPackage { Package = PackageEntry(e, e.FromFolder) });
                }
                else if (e.Dest.EndsWith(".krpdiff", StringComparison.OrdinalIgnoreCase))
                {
                    // groupInfos carries the destination metadata; keep package available for download.
                    PackageEntry(e, e.FromFolder);
                }
                else if (e.Dest.EndsWith(".krzip", StringComparison.OrdinalIgnoreCase))
                {
                    plan.ZipPackages.Add(new GamePatchPackage { Package = PackageEntry(e, e.FromFolder) });
                }
                else
                {
                    AddOrdinary(e);
                }
            }

            foreach (var patchFile in patch.PatchInfos ?? [])
            {
                if (patchFile?.Dest is null)
                {
                    continue;
                }
                var package = new GamePatchPackage
                {
                    Package = PackageEntry(new KuroPatchEntry
                    {
                        Dest = patchFile.Dest,
                        Size = patchFile.Size,
                        Md5 = patchFile.Md5,
                        ChunkInfos = patchFile.ChunkInfos,
                        FromFolder = patchFile.FromFolder,
                    }, patchFile.FromFolder),
                };
                foreach (var e in patchFile.Entries ?? [])
                {
                    AddPatchEntry(package.Entries, e);
                }
                plan.DiffPackages.Add(package);
            }

            foreach (var group in patch.GroupInfos ?? [])
            {
                if (group?.Dest is null)
                {
                    continue;
                }
                var packageEntry = packageCache.TryGetValue(NormalizePath(group.Dest), out var package)
                    ? package
                    : PackageEntry(new KuroPatchEntry { Dest = group.Dest }, null);
                var patchGroup = new GamePatchGroup { Package = packageEntry };
                foreach (var e in group.SrcFiles ?? [])
                {
                    AddPatchEntry(patchGroup.SourceFiles, e);
                }
                foreach (var e in group.DstFiles ?? [])
                {
                    AddPatchEntry(patchGroup.DestinationFiles, e);
                }
                plan.DiffGroups.Add(patchGroup);
            }

            foreach (var zip in patch.ZipInfos ?? [])
            {
                if (zip?.Dest is null)
                {
                    continue;
                }
                var package = new GamePatchPackage
                {
                    Package = PackageEntry(new KuroPatchEntry
                    {
                        Dest = zip.Dest,
                        Size = zip.Size,
                        Md5 = zip.Md5,
                        ChunkInfos = zip.ChunkInfos,
                        FromFolder = zip.FromFolder,
                    }, zip.FromFolder),
                };
                foreach (var e in zip.Entries ?? [])
                {
                    AddPatchEntry(package.Entries, e);
                }
                plan.ZipPackages.Add(package);
            }

            plan.DeleteFiles.AddRange((patch.DeleteFiles ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Select(NormalizePath));
            manifest.PatchPlan = plan;
            return new ManifestLoadResult { Success = true, Manifest = manifest };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Fail($"获取补丁清单失败: {ex.Message}");
        }

        static void AddPatchEntry(List<GamePatchEntry> target, KuroPatchEntry? source)
        {
            if (source?.Dest is null)
            {
                return;
            }
            var entry = new GamePatchEntry
            {
                Path = NormalizePath(source.Dest),
                Size = source.Size ?? 0,
                Md5 = source.Md5 ?? "",
            };
            CopyChunks(entry, source.ChunkInfos);
            target.Add(entry);
        }
    }

    /// <summary>轻量探测可用 CDN,失败时遵循官方 P 优先级回退。</summary>
    /// <summary>探测指定资源路径并返回最快可用 CDN；全部失败时回退官方 P 优先级。</summary>
    public async Task<string?> SelectCdnAsync(
        IEnumerable<KuroCdnData>? cdns,
        string? probePath,
        CancellationToken ct)
    {
        var candidates = cdns?
            .Where(c => !string.IsNullOrWhiteSpace(c.Url))
            .OrderBy(c => c.P == 0 ? int.MaxValue : c.P)
            .ToArray() ?? [];
        if (candidates.Length == 0)
        {
            return null;
        }

        var probes = candidates.Select(async cdn =>
        {
            try
            {
                using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                probeCts.CancelAfter(TimeSpan.FromSeconds(2));
                var watch = Stopwatch.StartNew();
                var url = string.IsNullOrWhiteSpace(probePath)
                    ? cdn.Url
                    : cdn.Url.TrimEnd('/') + "/" + probePath.TrimStart('/');
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 0);
                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, probeCts.Token)
                    .ConfigureAwait(false);
                return response.IsSuccessStatusCode ? (Url: cdn.Url, Milliseconds: watch.ElapsedMilliseconds) : ((string Url, long Milliseconds)?)null;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return null;
            }
            catch (HttpRequestException)
            {
                return null;
            }
            catch (Exception)
            {
                return null;
            }
        });
        var results = await Task.WhenAll(probes).ConfigureAwait(false);
        return results
            .Where(r => r is not null)
            .Select(r => r!.Value)
            .OrderBy(r => r.Milliseconds)
            .Select(r => r.Url)
            .FirstOrDefault()
            ?? candidates[0].Url;
    }

    private static void CopyChunks(GameFileEntry destination, IEnumerable<KuroChunkInfo>? chunks)
    {
        if (chunks is null)
        {
            return;
        }

        foreach (var chunk in chunks)
        {
            var start = chunk.Start ?? chunk.Offset ?? -1;
            var end = chunk.End ?? (start >= 0 && chunk.Size is > 0 ? start + chunk.Size.Value - 1 : -1);
            if (start < 0 || end < start)
            {
                continue;
            }
            destination.ChunkInfos.Add(new GameChunkInfo
            {
                Start = start,
                End = end,
                Md5 = chunk.Md5 ?? "",
            });
        }
    }

    private static void CopyChunks(GamePatchEntry destination, IEnumerable<KuroChunkInfo>? chunks)
    {
        if (chunks is null)
        {
            return;
        }

        foreach (var chunk in chunks)
        {
            var start = chunk.Start ?? chunk.Offset ?? -1;
            var end = chunk.End ?? (start >= 0 && chunk.Size is > 0 ? start + chunk.Size.Value - 1 : -1);
            if (start < 0 || end < start)
            {
                continue;
            }
            destination.ChunkInfos.Add(new GameChunkInfo
            {
                Start = start,
                End = end,
                Md5 = chunk.Md5 ?? "",
            });
        }
    }

    private static bool HashMatches(byte[] payload, string expected)
    {
        var actual = Convert.ToHexStringLower(System.Security.Cryptography.MD5.HashData(payload));
        return string.Equals(actual, expected.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAbsoluteUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static ManifestLoadResult Fail(string message) =>
        new() { Success = false, Message = message };

    private static string NormalizePath(string path)
    {
        if (!GameFilePath.IsSafeRelativePath(path))
        {
            throw new InvalidDataException($"资源路径越界: {path}");
        }
        return path.Replace('\\', '/');
    }
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
[JsonSerializable(typeof(KuroResourcePackIndex))]
[JsonSerializable(typeof(KuroResourcePack))]
[JsonSerializable(typeof(KuroResourceBundle))]
[JsonSerializable(typeof(Dictionary<string, KuroResourcePack>))]
[JsonSerializable(typeof(Dictionary<string, KuroResourceBundle>))]
public sealed partial class GameJsonContext : JsonSerializerContext;
