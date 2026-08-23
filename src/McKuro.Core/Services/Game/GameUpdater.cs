using System.Text.Json;
using System.Text.Json.Serialization;
using McKuro.Core.Infrastructure;
using McKuro.Core.Models.Game;
using McKuro.Core.Services.Settings;
using Microsoft.Extensions.Logging;

namespace McKuro.Core.Services.Game;

/// <summary>更新检查结果。</summary>
public sealed class UpdateCheckResult
{
    public required bool Success { get; init; }
    public string? Message { get; init; }

    /// <summary>服务端版本。</summary>
    public string? ServerVersion { get; init; }

    /// <summary>本地已装版本(无则为 null)。</summary>
    public string? InstalledVersion { get; init; }

    /// <summary>是否有更新(或未安装)。</summary>
    public bool HasUpdate { get; init; }

    /// <summary>是否有预下载。</summary>
    public bool HasPredownload { get; init; }

    public string? PredownloadVersion { get; init; }

    /// <summary>需要下载的文件。</summary>
    public IReadOnlyList<GameFileEntry> FilesToDownload { get; init; } = [];

    public long TotalBytes => FilesToDownload.Sum(f => f.Size);

    /// <summary>是否完全未安装(缺少关键文件)。</summary>
    public bool NotInstalled { get; init; }
}

/// <summary>本地图形组件(DLSS/XeSS 等)版本信息。</summary>
public sealed class LocalFileVersion
{
    public required string DisplayName { get; init; }
    public string Version { get; init; } = "";
}

/// <summary>游戏更新编排:检查更新、预下载、安装、启动。</summary>
public sealed class GameUpdater : IGameUpdater
{
    private readonly GameManifestLoader _loader;
    private readonly DownloadEngine _downloader;
    private readonly UpdateInstaller _installer;
    private readonly PatchInstaller _patchInstaller;
    private readonly GamePathResolver _paths;
    private readonly string _appDataDir;
    private readonly AppDatabase? _database;
    private readonly ISettingsService? _settings;
    private readonly Func<GameServerType, string> _indexUrlProvider;
    private readonly ILogger<GameUpdater> _logger;

    public GameUpdater(
        GameManifestLoader loader,
        DownloadEngine downloader,
        UpdateInstaller installer,
        GamePathResolver paths,
        string appDataDir,
        AppDatabase? database = null,
        ISettingsService? settings = null,
        Func<GameServerType, string>? indexUrlProvider = null,
        ILogger<GameUpdater>? logger = null)
        : this(
            loader,
            downloader,
            installer,
            new PatchInstaller(installer),
            paths,
            appDataDir,
            database,
            settings,
            indexUrlProvider,
            logger)
    {
    }

    public GameUpdater(
        GameManifestLoader loader,
        DownloadEngine downloader,
        UpdateInstaller installer,
        PatchInstaller patchInstaller,
        GamePathResolver paths,
        string appDataDir,
        AppDatabase? database = null,
        ISettingsService? settings = null,
        Func<GameServerType, string>? indexUrlProvider = null,
        ILogger<GameUpdater>? logger = null)
    {
        _loader = loader;
        _downloader = downloader;
        _installer = installer;
        _patchInstaller = patchInstaller;
        _paths = paths;
        _appDataDir = appDataDir;
        _database = database;
        _settings = settings;
        _indexUrlProvider = indexUrlProvider ?? KuroEndpoints.ForServerType;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<GameUpdater>.Instance;
    }

    /// <summary>获取预下载清单的下载体积与所需磁盘空间(供 UI 显示下载/磁盘预估,参考 Haiyu Config.Size/UnCompressSize)。</summary>
    public async Task<(long DownloadBytes, long DiskBytes)> GetPredownloadEstimateAsync(
        GameServerType serverType,
        CancellationToken ct = default)
    {
        try
        {
            var indexUrl = _indexUrlProvider(serverType);
            var load = await _loader.LoadKuroAsync(indexUrl, preDownload: true, ct).ConfigureAwait(false);
            if (!load.Success)
            {
                return (0, 0);
            }
            return (load.PredownloadDownloadBytes, load.PredownloadDiskBytes);
        }
        catch
        {
            return (0, 0);
        }
    }

    /// <summary>检查更新(依据当前渠道的 index.json)。</summary>
    /// <remarks>
    /// 只比较版本号判断是否有更新,不做全量文件 MD5 校验(数万文件会耗时数分钟,
    /// 表现为"一直在检查状态");文件级差异留给预下载/安装阶段计算。
    /// </remarks>
    public async Task<UpdateCheckResult> CheckUpdateAsync(
        GameServerType serverType,
        CancellationToken ct = default)
    {
        var root = _paths.GameRootDir;
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
        {
            return new UpdateCheckResult
            {
                Success = false,
                Message = "请先在设置中指定游戏安装目录",
            };
        }

        var indexUrl = _indexUrlProvider(serverType);
        var load = await _loader.LoadKuroAsync(indexUrl, preDownload: false, ct).ConfigureAwait(false);
        if (!load.Success || load.Manifest is null)
        {
            return new UpdateCheckResult { Success = false, Message = load.Message ?? "获取更新清单失败" };
        }

        var manifest = load.Manifest;
        // 防御:服务端版本缺失视为拉取失败(避免空版本强制"有更新")
        if (string.IsNullOrWhiteSpace(manifest.Version))
        {
            return new UpdateCheckResult { Success = false, Message = "更新清单缺少版本号" };
        }
        var installedVersion = ReadInstalledVersion(root);
        var notInstalled = !_paths.IsGameInstalled;

        // 自愈:游戏已安装但本地无版本记录(用户用官方启动器装好后首次设置目录),
        // 用清单关键文件做廉价存在性校验,通过则记录版本并判定无更新(否则每次检查都误报有更新)
        var hasUpdate = notInstalled;
        if (installedVersion is null && !notInstalled)
        {
            var verified = VerifyKeyFiles(root, manifest.KeyFiles);
            if (verified)
            {
                WriteInstalledVersion(root, manifest.Version);
                installedVersion = manifest.Version;
            }
            else
            {
                hasUpdate = true;
            }
        }
        else if (!notInstalled)
        {
            hasUpdate = IsVersionOlder(installedVersion, manifest.Version);
        }

        return new UpdateCheckResult
        {
            Success = true,
            ServerVersion = manifest.Version,
            InstalledVersion = installedVersion,
            HasUpdate = hasUpdate,
            HasPredownload = load.HasPredownload,
            PredownloadVersion = load.PredownloadVersion,
            FilesToDownload = [],
            NotInstalled = notInstalled,
        };
    }

    /// <summary>校验清单关键文件是否存在(廉价存在性检查,不做全量 MD5)。</summary>
    private bool VerifyKeyFiles(string root, IReadOnlyList<string> keyFiles)
    {
        if (keyFiles.Count == 0)
        {
            // 无关键文件定义:保守视为未通过(交由版本比较决定)
            return false;
        }
        try
        {
            foreach (var rel in keyFiles)
            {
                var full = Path.Combine(root, rel);
                if (!File.Exists(full))
                {
                    return false;
                }
            }
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "校验关键文件失败: {Root}", root);
            return false;
        }
    }

    /// <summary>
    /// 判断本地版本是否低于服务端版本(需更新)。
    /// 对齐 Haiyu GetGameContextStatusAsync 的 localV &lt; serverV 数值比较语义,
    /// 但修正 .NET Version 把缺失段视为 -1 的坑("2.2.0" 会被判小于 "2.2.0.0"):
    /// 这里把段数补齐(缺失按 0)后逐段数值比较;
    /// 解析失败(如带尾缀/非数字)时回退忽略大小写与首尾空白的字符串比较。
    /// </summary>
    internal static bool IsVersionOlder(string? installed, string server)
    {
        if (string.IsNullOrWhiteSpace(installed))
        {
            // 无本地版本记录:视为未记录,保守提示更新
            return true;
        }
        if (TryParseVersion(installed.Trim(), out var a) && TryParseVersion(server.Trim(), out var b))
        {
            var len = Math.Max(a.Length, b.Length);
            for (var i = 0; i < len; i++)
            {
                var av = i < a.Length ? a[i] : 0;
                var bv = i < b.Length ? b[i] : 0;
                if (av != bv)
                {
                    return av < bv;
                }
            }
            return false;
        }
        return !string.Equals(installed.Trim(), server.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>把 "2.2.0" 拆成数字段 [2,2,0];非纯数字/超过 4 段返回 false。</summary>
    private static bool TryParseVersion(string s, out int[] parts)
    {
        parts = [];
        var segments = s.Split('.');
        if (segments.Length is 0 or > 4)
        {
            return false;
        }
        var list = new List<int>(segments.Length);
        foreach (var seg in segments)
        {
            if (!int.TryParse(seg, out var n) || n < 0)
            {
                return false;
            }
            list.Add(n);
        }
        parts = list.ToArray();
        return true;
    }

    /// <summary>预下载:按官方补丁清单下载差异文件到暂存目录,不触碰游戏目录、不做全量 MD5 校验。</summary>
    /// <remarks>
    /// 对齐 Haiyu 预下载逻辑:从 predownload.config.patchConfig 找到匹配本地版本的补丁 →
    /// 下载该补丁的 indexFile.json(差异清单) → 仅下载清单中的文件。不做 ComputeDiff 全量校验,
    /// 因此数万文件的游戏也能立即开始下载。
    /// </remarks>
    /// <returns>暂存目录路径(供稍后安装)。</returns>
    public async Task<(bool Success, string? StagingDir, string? Message)> PreDownloadAsync(
        GameServerType serverType,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        var root = _paths.GameRootDir;
        if (string.IsNullOrEmpty(root))
        {
            return (false, null, "未设置游戏目录");
        }

        var indexUrl = _indexUrlProvider(serverType);
        var load = await _loader.LoadKuroAsync(indexUrl, preDownload: false, ct).ConfigureAwait(false);
        if (!load.Success || load.Manifest is null)
        {
            return (false, null, load.Message);
        }

        var manifest = load.Manifest;
        var targetVersion = load.Predownload?.Version;
        if (string.IsNullOrWhiteSpace(targetVersion))
        {
            return (false, null, "当前没有可用的预载版本");
        }
        // 本地安装版本
        var installedVersion = ReadInstalledVersion(root);

        // 找匹配本地版本的补丁项(predownload.config.patchConfig,纯内存匹配,无二次网络请求)
        var (patchUrl, cdnPrefix, patchConfig) = await ResolvePatchFromIndexAsync(
            load.Predownload,
            load.DefaultData,
            installedVersion,
            ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(patchUrl) || patchConfig is null)
        {
            // Haiyu 只接受从当前本地版本精确匹配到的预载补丁,不能误把默认版本差异当未来版本预载。
            return (false, null, "未找到与本地版本匹配的官方预载补丁");
        }

        var patchLoad = await _loader.LoadPatchAsync(
            patchUrl,
            baseUrl: cdnPrefix + "/" + (patchConfig.BaseUrl ?? "").TrimStart('/'),
            indexFileMd5: patchConfig.IndexFileMd5,
            ct: ct).ConfigureAwait(false);
        if (!patchLoad.Success || patchLoad.Manifest is null || patchLoad.Manifest.Files.Count == 0)
        {
            return (false, null, patchLoad.Message ?? "预载补丁清单不可用");
        }

        // 为补丁文件补全下载地址(CDN + FromFolder + dest,与 Haiyu GetBaseUrl 一致)
        var patchFiles = patchLoad.Manifest.Files;
        // 预下载目标版本号记录,暂存目录按版本隔离。保留已有 .part 文件以支持断点续传。
        var downloadBytes = patchFiles.Sum(f => f.Size);
        if (!HasFreeSpace(_appDataDir, downloadBytes))
        {
            return (false, null, $"预载下载盘空间不足: {FormatBytes(downloadBytes)}");
        }
        var requiredBytes = patchConfig.Ext?.RequiredDiskSpace ?? patchConfig.UnCompressSize ?? 0;
        if (!HasFreeSpace(root, requiredBytes))
        {
            return (false, null, $"游戏盘空间不足: {FormatBytes(requiredBytes)}");
        }
        var staging = Path.Combine(_appDataDir, "predownload", targetVersion);
        Directory.CreateDirectory(staging);
        var existingMeta = await ReadPreDownloadMetaAsync(staging, ct).ConfigureAwait(false);
        if (existingMeta is null
            ? Directory.EnumerateFileSystemEntries(staging).Any()
            : !string.Equals(existingMeta.Version, targetVersion, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(existingMeta.SourceVersion, installedVersion, StringComparison.OrdinalIgnoreCase)
                || existingMeta.ServerType != serverType)
        {
            Directory.Delete(staging, recursive: true);
            Directory.CreateDirectory(staging);
        }

        var meta = new PreDownloadMeta
        {
            Version = targetVersion,
            SourceVersion = installedVersion,
            ServerType = serverType,
            Completed = false,
            PatchIndexUrl = patchUrl,
            IndexFileMd5 = patchConfig.IndexFileMd5,
            BaseUrl = patchConfig.BaseUrl,
            DownloadBaseUrl = patchLoad.Manifest.PatchPlan?.BaseUrl,
        };
        var metaPath = Path.Combine(staging, "predownload.json");
        await File.WriteAllTextAsync(
            metaPath,
            JsonSerializer.Serialize(meta, GameMetaJsonContext.Default.PreDownloadMeta),
            ct).ConfigureAwait(false);

        progress?.Report(new DownloadProgress
        {
            FileIndex = 0,
            FileTotal = patchFiles.Count,
            BytesDownloaded = 0,
            BytesTotal = patchFiles.Sum(f => f.Size),
            SpeedBps = 0,
            CurrentFile = $"发现 {patchFiles.Count} 个待下载文件,开始下载…",
        });

        // 下载补丁差异文件到暂存目录
        var (success, failures) = await _downloader.DownloadManyAsync(
            patchFiles,
            baseUrl: "",
            staging,
            progress,
            ct).ConfigureAwait(false);

        if (failures.Count > 0)
        {
            return (false, staging, $"有 {failures.Count} 个文件下载失败: {failures[0]}");
        }

        // 仅在全部包完成 MD5 校验后将预载标记切换为可安装。
        meta.Completed = true;
        await File.WriteAllTextAsync(
            metaPath,
            JsonSerializer.Serialize(meta, GameMetaJsonContext.Default.PreDownloadMeta),
            ct).ConfigureAwait(false);

        return (true, staging, null);
    }

    /// <summary>精确匹配本地版本的补丁，并探测承载 indexFile 的可用 CDN。</summary>
    private async Task<(string? Url, string? CdnPrefix, KuroPatchConfig? PatchConfig)> ResolvePatchFromIndexAsync(
        KuroUpdateData? patchData,
        KuroUpdateData? cdnData,
        string? installedVersion,
        CancellationToken ct)
    {
        if (patchData?.Config?.PatchConfig is not { Count: > 0 }
            || string.IsNullOrWhiteSpace(installedVersion))
        {
            return (null, null, null);
        }

        // 必须与当前本地资源版本精确匹配。Haiyu 不会把最新项误当成任意本地版本的补丁。
        var match = patchData.Config.PatchConfig.FirstOrDefault(p =>
            p.Version is not null
            && string.Equals(p.Version, installedVersion, StringComparison.OrdinalIgnoreCase));
        if (match is null || string.IsNullOrWhiteSpace(match.IndexFile))
        {
            return (null, null, null);
        }

        var cdnPrefix = await _loader.SelectCdnAsync(cdnData?.CdnList, match.IndexFile, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(cdnPrefix))
        {
            return (null, null, null);
        }
        cdnPrefix = cdnPrefix.TrimEnd('/');
        return (cdnPrefix + "/" + match.IndexFile.TrimStart('/'), cdnPrefix, match);
    }

    /// <summary>
    /// 执行安装:优先消费已完成预载,否则匹配当前本地版本的官方补丁,最后回退完整清单。
    /// </summary>
    public async Task<(bool Success, string? Message)> InstallAsync(
        GameServerType serverType,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        var root = _paths.GameRootDir;
        if (string.IsNullOrEmpty(root))
        {
            return (false, "未设置游戏目录");
        }

        var indexUrl = _indexUrlProvider(serverType);
        var load = await _loader.LoadKuroAsync(indexUrl, preDownload: false, ct).ConfigureAwait(false);
        if (!load.Success || load.Manifest is null)
        {
            return (false, load.Message ?? "获取更新清单失败");
        }

        var manifest = load.Manifest;
        var installedVersion = ReadInstalledVersion(root);
        var stagingDir = FindStaging(manifest.Version, serverType, installedVersion);
        ManifestLoadResult? patchLoad = null;

        if (stagingDir is not null)
        {
            var meta = await ReadPreDownloadMetaAsync(stagingDir, ct).ConfigureAwait(false);
            if (meta?.PatchIndexUrl is not null && meta.DownloadBaseUrl is not null)
            {
                patchLoad = await _loader.LoadPatchAsync(
                    meta.PatchIndexUrl,
                    meta.DownloadBaseUrl,
                    meta.IndexFileMd5,
                    ct).ConfigureAwait(false);
                if (!patchLoad.Success || patchLoad.Manifest?.PatchPlan is null)
                {
                    _logger.LogWarning("预载补丁清单重新加载失败,回退当前版本更新: {Message}", patchLoad.Message);
                    patchLoad = null;
                    stagingDir = null;
                }
            }
            else
            {
                stagingDir = null;
            }
        }

        // 只有默认节点已经切换到预载目标版本时,才消费预载目录。
        // 服务器存在 predownload 节点本身不会触发未来版本下载。
        if (stagingDir is null)
        {
            var (patchUrl, cdnPrefix, patchConfig) = await ResolvePatchFromIndexAsync(
                load.DefaultData,
                load.DefaultData,
                installedVersion,
                ct).ConfigureAwait(false);
            if (patchUrl is not null && cdnPrefix is not null && patchConfig is not null)
            {
                var patchBaseUrl = cdnPrefix + "/" + (patchConfig.BaseUrl ?? "").TrimStart('/');
                patchLoad = await _loader.LoadPatchAsync(
                    patchUrl,
                    patchBaseUrl,
                    patchConfig.IndexFileMd5,
                    ct).ConfigureAwait(false);
                if (patchLoad.Success && patchLoad.Manifest?.PatchPlan is not null)
                {
                    var patchFiles = patchLoad.Manifest.Files;
                    var patchDownloadBytes = patchFiles.Sum(f => f.Size);
                    var patchDiskBytes = patchConfig.Ext?.RequiredDiskSpace ?? patchConfig.UnCompressSize ?? 0;
                    if (!HasFreeSpace(_appDataDir, patchDownloadBytes))
                    {
                        return (false, $"更新下载盘空间不足: {FormatBytes(patchDownloadBytes)}");
                    }
                    if (!HasFreeSpace(root, patchDiskBytes))
                    {
                        return (false, $"游戏盘空间不足: {FormatBytes(patchDiskBytes)}");
                    }
                    stagingDir = Path.Combine(_appDataDir, "install_tmp", Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(stagingDir);
                    var (_, failures) = await _downloader.DownloadManyAsync(
                        patchFiles,
                        "",
                        stagingDir,
                        progress,
                        ct).ConfigureAwait(false);
                    if (failures.Count > 0)
                    {
                        _logger.LogWarning("官方补丁下载失败,回退完整清单: {Message}", failures[0]);
                        patchLoad = null;
                        TryDeleteDirectory(stagingDir);
                        stagingDir = null;
                    }
                }
                else
                {
                    _logger.LogWarning("官方补丁清单不可用,回退完整清单: {Message}", patchLoad.Message);
                    patchLoad = null;
                }
            }
        }

        if (stagingDir is not null && patchLoad?.Manifest?.PatchPlan is not null)
        {
            var patchResult = await _patchInstaller.InstallAsync(
                patchLoad.Manifest.PatchPlan,
                stagingDir,
                root,
                patchLoad.Manifest.Files,
                progress,
                ct).ConfigureAwait(false);
            if (patchResult.Success)
            {
                var verified = await EnsureManifestCompleteAsync(manifest, root, progress, ct).ConfigureAwait(false);
                if (verified.Success)
                {
                    DeletePatchFiles(root, patchLoad.Manifest.PatchPlan.DeleteFiles, manifest);
                    WriteInstalledVersion(root, manifest.Version);
                    TryDeleteDirectory(stagingDir);
                    return (true, $"补丁安装完成,版本 {manifest.Version}");
                }
                _logger.LogWarning("补丁安装后最终校验未通过,回退完整清单: {Message}", verified.Message);
            }
            else
            {
                _logger.LogWarning("补丁安装失败,回退完整清单: {Message}", patchResult.Message);
            }

            // 补丁已尝试应用但未完成时,其预载包不能再复用,否则会对已变更目录重复打差分。
            TryDeleteDirectory(stagingDir);
        }

        var fullResult = await EnsureManifestCompleteAsync(manifest, root, progress, ct).ConfigureAwait(false);
        if (!fullResult.Success)
        {
            return fullResult;
        }
        WriteInstalledVersion(root, manifest.Version);
        return (true, fullResult.Message ?? $"更新完成,版本 {manifest.Version}");
    }

    private async Task<(bool Success, string? Message)> EnsureManifestCompleteAsync(
        GameManifest manifest,
        string root,
        IProgress<DownloadProgress>? progress,
        CancellationToken ct)
    {
        IProgress<DiffProgress>? diffProgress = null;
        if (progress is not null)
        {
            diffProgress = new Progress<DiffProgress>(p => progress.Report(new DownloadProgress
            {
                FileIndex = p.Checked,
                FileTotal = Math.Max(p.Total, 1),
                BytesDownloaded = 0,
                BytesTotal = 0,
                SpeedBps = 0,
                CurrentFile = $"正在校验本地文件 {p.Checked}/{p.Total}…",
            }));
        }

        var diff = await Task.Run(
            () => _installer.ComputeDiff(manifest, root, progress: diffProgress, ct: ct),
            ct).ConfigureAwait(false);
        if (!diff.HasChanges)
        {
            return (true, "游戏文件完整,无需额外下载");
        }

        var downloadBytes = diff.ToDownload.Sum(file => file.Size);
        if (!HasFreeSpace(_appDataDir, downloadBytes))
        {
            return (false, $"下载盘空间不足: {FormatBytes(downloadBytes)}");
        }
        var tempInstall = Path.Combine(_appDataDir, "install_tmp", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempInstall);
        try
        {
            var (_, failures) = await _downloader.DownloadManyAsync(
                diff.ToDownload,
                baseUrl: "",
                tempInstall,
                progress,
                ct).ConfigureAwait(false);
            if (failures.Count > 0)
            {
                return (false, $"下载失败: {failures[0]}");
            }

            var installed = await Task.Run(
                () => _installer.InstallFromStaging(tempInstall, root, manifest),
                ct).ConfigureAwait(false);
            if (installed.Failures.Count > 0)
            {
                return (false, $"安装失败: {installed.Failures[0]}");
            }
        }
        finally
        {
            TryDeleteDirectory(tempInstall);
        }

        var remaining = await Task.Run(
            () => _installer.ComputeDiff(manifest, root, ct: ct),
            ct).ConfigureAwait(false);
        return remaining.HasChanges
            ? (false, $"最终校验仍有 {remaining.ToDownload.Count} 个文件不完整")
            : (true, $"已补齐 {diff.ToDownload.Count} 个文件");
    }

    private static void DeletePatchFiles(string gameRoot, IEnumerable<string> relativePaths, GameManifest targetManifest)
    {
        var targetPaths = targetManifest.Files
            .Select(file => file.Path.Replace('/', Path.DirectorySeparatorChar))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var relative in relativePaths)
        {
            try
            {
                var normalizedRelative = relative.Replace('/', Path.DirectorySeparatorChar);
                if (targetPaths.Contains(normalizedRelative))
                {
                    continue;
                }
                var path = GameFilePath.CombineUnderRoot(gameRoot, relative);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // 单个旧资源删除失败不阻断已完成的安装。
            }
        }
    }

    private static void TryDeleteDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // 临时目录被占用时保留,下次启动可清理。
        }
    }

    /// <summary>
    /// 修复游戏:对比清单重新下载并安装缺失/损坏的文件。
    /// 对齐 Haiyu 的 RepirGame:跳过用户配置的校验文件,并按设置决定是否删除被跳过的文件。
    /// </summary>
    public async Task<(bool Success, string? Message)> RepairGameAsync(
        GameServerType serverType,
        IReadOnlySet<string>? skipPaths = null,
        bool deleteSkipped = false,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        var root = _paths.GameRootDir;
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
        {
            return (false, "未设置游戏目录");
        }

        var indexUrl = _indexUrlProvider(serverType);
        var load = await _loader.LoadKuroAsync(indexUrl, preDownload: false, ct).ConfigureAwait(false);
        if (!load.Success || load.Manifest is null)
        {
            return (false, load.Message ?? "获取更新清单失败");
        }

        var manifest = load.Manifest;
        IProgress<DiffProgress>? diffProgress = progress is null
            ? null
            : new Progress<DiffProgress>(p => progress.Report(new DownloadProgress
            {
                FileIndex = p.Checked,
                FileTotal = Math.Max(p.Total, 1),
                BytesDownloaded = 0,
                BytesTotal = 0,
                SpeedBps = 0,
                CurrentFile = $"正在检查本地文件 {p.Checked}/{p.Total}…",
            }));
        var diff = await Task.Run(
            () => _installer.ComputeDiff(manifest, root, skipPaths, diffProgress, ct),
            ct).ConfigureAwait(false);
        if (!diff.HasChanges)
        {
            return (true, "游戏文件完整,无需修复");
        }

        // 下载缺失/损坏文件到临时目录,再整体安装
        var repairDownloadBytes = diff.ToDownload.Sum(file => file.Size);
        if (!HasFreeSpace(_appDataDir, repairDownloadBytes))
        {
            return (false, $"修复下载盘空间不足: {FormatBytes(repairDownloadBytes)}");
        }
        var tempInstall = Path.Combine(_appDataDir, "repair_tmp", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempInstall);
        try
        {
            var (_, failures) = await _downloader.DownloadManyAsync(
                diff.ToDownload,
                baseUrl: "",
                tempInstall,
                progress,
                ct).ConfigureAwait(false);
            if (failures.Count > 0)
            {
                return (false, $"下载失败: {failures[0]}");
            }

            var (installed, installFailures) = await Task.Run(
                () => _installer.InstallFromStaging(tempInstall, root, manifest),
                ct).ConfigureAwait(false);
            if (installFailures.Count > 0)
            {
                return (false, $"安装失败: {installFailures[0]}");
            }

            // 与 Haiyu 一致:仅在用户选择删除跳过文件时清理,不擅自删除游戏目录中的额外文件。
            if (deleteSkipped && skipPaths is not null)
            {
                int deleted = DeleteSkippedFiles(root, skipPaths);
                _logger.LogInformation("修复时删除被跳过文件 {Count} 个", deleted);
            }

            WriteInstalledVersion(root, manifest.Version);
            return (true, $"修复完成:重新下载 {installed} 个文件,版本 {manifest.Version}");
        }
        finally
        {
            try
            {
                Directory.Delete(tempInstall, recursive: true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "清理修复临时目录失败: {Dir}", tempInstall);
            }
        }
    }

    private static int DeleteSkippedFiles(string gameRoot, IReadOnlySet<string> skipPaths)
    {
        int deleted = 0;
        foreach (var relative in skipPaths)
        {
            try
            {
                var full = GameFilePath.CombineUnderRoot(gameRoot, relative);
                if (File.Exists(full))
                {
                    File.Delete(full);
                    deleted++;
                }
            }
            catch (Exception)
            {
                // 忽略单个文件删除失败
            }
        }
        return deleted;
    }

    /// <summary>
    /// 启动游戏(对齐 Haiyu StartGameAsync:可选 exe + `Client -dx11 -slno {自定义参数}` 命令行)。
    /// </summary>
    public bool LaunchGame(out string? error)
    {
        var root = _paths.GameRootDir;
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
        {
            error = "未找到游戏安装目录";
            return false;
        }

        // 解析启动 exe:用户指定 → Wuthering Waves.exe → Client-Win64-Shipping.exe
        var s = _settings?.Current;
        var exeName = s?.StartGameExeName;
        var exe = ResolveLaunchExe(root, string.IsNullOrWhiteSpace(exeName) ? null : exeName);
        if (exe is null || !File.Exists(exe))
        {
            error = "未找到游戏主程序";
            return false;
        }

        try
        {
            // 对齐 Haiyu WavesLauncheOption.ToString():Client -dx11 -slno {arguments}
            var args = new System.Text.StringBuilder("Client");
            if (s?.UseDx11 == true)
            {
                args.Append(" -dx11 -slno");
            }
            var extra = s?.StartGameArguments?.Trim();
            if (!string.IsNullOrEmpty(extra))
            {
                args.Append(' ').Append(extra);
            }

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = exe,
                Arguments = args.ToString(),
                WorkingDirectory = root,
                UseShellExecute = true,
            };
            System.Diagnostics.Process.Start(psi);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "启动游戏可执行文件失败: {Exe}", exe);
            error = ex.Message;
            return false;
        }
    }

    /// <summary>按优先级解析启动 exe 路径(用户指定 → 根 exe → 客户端 exe)。</summary>
    private string? ResolveLaunchExe(string root, string? userSpecified)
    {
        if (!string.IsNullOrWhiteSpace(userSpecified))
        {
            var p = Path.Combine(root, userSpecified.Trim());
            if (File.Exists(p))
            {
                return p;
            }
        }
        var rootExe = Path.Combine(root, _paths.GameExeName);
        if (File.Exists(rootExe))
        {
            return rootExe;
        }
        var clientExe = Path.Combine(root, GamePathResolver.ExeClientRelative);
        return File.Exists(clientExe) ? clientExe : null;
    }

    /// <inheritdoc/>
    public string? ResolveLaunchExePath()
    {
        var root = _paths.GameRootDir;
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
        {
            return null;
        }
        var exeName = _settings?.Current.StartGameExeName;
        return ResolveLaunchExe(root, string.IsNullOrWhiteSpace(exeName) ? null : exeName);
    }

    /// <summary>本地 DLSS/XeSS 组件版本(对齐 Haiyu GetLocalDLSSAsync / GetLocalXeSSGenerateAsync)。</summary>
    public IReadOnlyList<LocalFileVersion> GetLocalGraphicsComponentVersions()
    {
        var root = _paths.GameRootDir;
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
        {
            return [];
        }

        var result = new List<LocalFileVersion>();
        foreach (var (fileName, displayName) in GraphicsComponents)
        {
            try
            {
                var file = Directory
                    .GetFiles(root, fileName, SearchOption.AllDirectories)
                    .FirstOrDefault();
                if (file is null)
                {
                    result.Add(new LocalFileVersion { DisplayName = displayName, Version = "未找到文件" });
                    continue;
                }
                var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(file);
                result.Add(new LocalFileVersion
                {
                    DisplayName = displayName,
                    Version = $"{info.FileMajorPart}.{info.FileMinorPart}.{info.FileBuildPart}.{info.FilePrivatePart}",
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "读取图形组件版本失败: {Name}", displayName);
                result.Add(new LocalFileVersion { DisplayName = displayName, Version = "读取失败" });
            }
        }
        return result;
    }

    private static readonly (string FileName, string DisplayName)[] GraphicsComponents =
    [
        ("nvngx_dlss.dll", "DLSS"),
        ("nvngx_dlssg.dll", "DLSS 帧生成"),
        ("libxess.dll", "XeSS"),
    ];

    private static bool HasFreeSpace(string path, long requiredBytes)
    {
        if (requiredBytes <= 0)
        {
            return true;
        }
        try
        {
            var fullPath = Path.GetFullPath(path);
            var root = Path.GetPathRoot(fullPath);
            return !string.IsNullOrEmpty(root) && new DriveInfo(root).AvailableFreeSpace >= requiredBytes;
        }
        catch
        {
            return true;
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }
        var units = new[] { "KB", "MB", "GB", "TB" };
        double value = bytes;
        var index = -1;
        do
        {
            value /= 1024;
            index++;
        } while (value >= 1024 && index < units.Length - 1);
        return $"{value:0.##} {units[index]}";
    }

    private string? FindStaging(string version, GameServerType? serverType = null, string? sourceVersion = null)
    {
        var staging = Path.Combine(_appDataDir, "predownload", version);
        var marker = Path.Combine(staging, "predownload.json");
        if (!Directory.Exists(staging) || !File.Exists(marker))
        {
            return null;
        }
        try
        {
            var meta = JsonSerializer.Deserialize(File.ReadAllText(marker), GameMetaJsonContext.Default.PreDownloadMeta);
            if (meta is null
                || !meta.Completed
                || string.IsNullOrWhiteSpace(meta.PatchIndexUrl)
                || string.IsNullOrWhiteSpace(meta.DownloadBaseUrl)
                || !string.Equals(meta.Version, version, StringComparison.OrdinalIgnoreCase)
                || (serverType is not null && meta.ServerType != serverType.Value)
                || (sourceVersion is not null
                    && !string.Equals(meta.SourceVersion, sourceVersion, StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }
            return staging;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "读取预载标记失败: {Path}", marker);
            return null;
        }
    }

    private static async Task<PreDownloadMeta?> ReadPreDownloadMetaAsync(string staging, CancellationToken ct)
    {
        var marker = Path.Combine(staging, "predownload.json");
        if (!File.Exists(marker))
        {
            return null;
        }
        try
        {
            await using var stream = File.OpenRead(marker);
            return await JsonSerializer.DeserializeAsync(
                stream,
                GameMetaJsonContext.Default.PreDownloadMeta,
                ct).ConfigureAwait(false);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }

    private string? ReadInstalledVersion(string gameRoot)
    {
        gameRoot = NormalizeRoot(gameRoot);
        if (_database is not null)
        {
            MigrateLegacyMarkerIfNeeded();
            return _database.GetInstalledVersion(gameRoot);
        }

        // 回退:旧 JSON 标记文件(无 SQLite 注入时)
        var marker = Path.Combine(_appDataDir, "installed_versions.json");
        if (!File.Exists(marker))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(marker);
            var dict = JsonSerializer.Deserialize(json, GameMetaJsonContext.Default.DictionaryStringString);
            return dict is not null && dict.TryGetValue(gameRoot, out var v) ? v : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "读取已安装版本失败,视为未安装: {Path}", marker);
            return null;
        }
    }

    private void WriteInstalledVersion(string gameRoot, string version)
    {
        gameRoot = NormalizeRoot(gameRoot);
        if (_database is not null)
        {
            _database.SetInstalledVersion(gameRoot, version);
            return;
        }

        var marker = Path.Combine(_appDataDir, "installed_versions.json");
        Dictionary<string, string> dict;
        if (File.Exists(marker))
        {
            try
            {
                var json = File.ReadAllText(marker);
                dict = JsonSerializer.Deserialize(json, GameMetaJsonContext.Default.DictionaryStringString) ?? [];
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "已安装版本文件损坏,重置: {Path}", marker);
                dict = [];
            }
        }
        else
        {
            dict = [];
        }

        dict[gameRoot] = version;
        Directory.CreateDirectory(_appDataDir);
        File.WriteAllText(marker, JsonSerializer.Serialize(dict, GameMetaJsonContext.Default.DictionaryStringString));
    }

    /// <summary>规范化目录 key:绝对路径 + 去尾部斜杠 + 统一大小写(消除路径漂移导致版本记录失效)。</summary>
    internal static string NormalizeRoot(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            return root;
        }
        try
        {
            var full = Path.GetFullPath(root).TrimEnd('\\', '/');
            return OperatingSystem.IsWindows() ? full.ToLowerInvariant() : full;
        }
        catch (Exception)
        {
            return root.TrimEnd('\\', '/');
        }
    }

    /// <summary>
    /// 一次性迁移:若存在旧版 installed_versions.json 且 SQLite 表为空,导入旧数据并移除 JSON。
    /// 幂等,多实例并发下由 SQLite 的 UPSERT 保证最终一致。
    /// </summary>
    private bool _legacyMigrated;

    private void MigrateLegacyMarkerIfNeeded()
    {
        if (_legacyMigrated || _database is null)
        {
            return;
        }
        _legacyMigrated = true;

        var marker = Path.Combine(_appDataDir, "installed_versions.json");
        if (!File.Exists(marker))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(marker);
            var dict = JsonSerializer.Deserialize(json, GameMetaJsonContext.Default.DictionaryStringString);
            if (dict is null || dict.Count == 0)
            {
                File.Delete(marker);
                return;
            }

            foreach (var (root, version) in dict)
            {
                var key = NormalizeRoot(root);
                if (_database.GetInstalledVersion(key) is null)
                {
                    _database.SetInstalledVersion(key, version);
                }
            }

            File.Delete(marker);
            _logger.LogInformation("已将旧 installed_versions.json 迁移到 SQLite,共 {Count} 条", dict.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "迁移旧已安装版本标记失败,保留 JSON: {Path}", marker);
        }
    }
}

/// <summary>预下载元信息。</summary>
public sealed class PreDownloadMeta
{
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("sourceVersion")] public string? SourceVersion { get; set; }
    [JsonPropertyName("serverType")] public GameServerType ServerType { get; set; }
    [JsonPropertyName("time")] public DateTime Time { get; set; } = DateTime.Now;
    [JsonPropertyName("completed")] public bool Completed { get; set; }
    [JsonPropertyName("patchIndexUrl")] public string? PatchIndexUrl { get; set; }
    [JsonPropertyName("indexFileMd5")] public string? IndexFileMd5 { get; set; }
    [JsonPropertyName("baseUrl")] public string? BaseUrl { get; set; }
    [JsonPropertyName("downloadBaseUrl")] public string? DownloadBaseUrl { get; set; }
}

[JsonSerializable(typeof(PreDownloadMeta))]
[JsonSerializable(typeof(Dictionary<string, string>))]
public sealed partial class GameMetaJsonContext : JsonSerializerContext;
