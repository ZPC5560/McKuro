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

    /// <summary>
    /// 服务端预告的预载版本本地已完整下载(predownload.json 标记完成)。
    /// 此时 UI 应禁用预下载按钮并显示「预下载完成」,
    /// 对齐上游 1.6 修复:避免下载完成后按钮仍可点、重复触发无响应的预下载。
    /// </summary>
    public bool PredownloadCompleted { get; init; }

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
    /// <summary>预下载标记文件名(位于暂存目录内)。</summary>
    private const string MarkerFileName = "predownload.json";

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
                Message = CoreStrings.T("Core.Updater.NoGameDirSet", "请先在设置中指定游戏安装目录"),
            };
        }

        var indexUrl = _indexUrlProvider(serverType);
        var load = await _loader.LoadKuroAsync(indexUrl, preDownload: false, ct).ConfigureAwait(false);
        if (!load.Success || load.Manifest is null)
        {
            return new UpdateCheckResult { Success = false, Message = load.Message ?? CoreStrings.T("Core.Updater.ManifestFailed", "获取更新清单失败") };
        }

        var manifest = load.Manifest;
        // 防御:服务端版本缺失视为拉取失败(避免空版本强制"有更新")
        if (string.IsNullOrWhiteSpace(manifest.Version))
        {
            return new UpdateCheckResult { Success = false, Message = CoreStrings.T("Core.Updater.ManifestNoVersion", "更新清单缺少版本号") };
        }
        var installedVersion = ReadInstalledVersion(root);
        var notInstalled = !_paths.IsGameInstalled;

        // [过渡代码 · 下个版本移除] 启动即后台把旧版数据目录里的预载包搬到游戏目录 DiffData。
        // 不 await:几十 GB 的跨盘搬迁可能要几分钟,不能卡住「检查更新」;
        // 真正要消费预载包的 PreDownloadAsync / InstallAsync 会 await 同一个任务。
        StartLegacyPredownloadMigrationInBackground();

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

        // 预载版本已完整下载到本地时,不再把 HasPredownload 置真(按钮显示「预下载完成」并禁用)。
        // 服务器存在 predownload 节点本身不代表还需要下载。
        var predownloadCompleted = false;
        if (load.HasPredownload && !string.IsNullOrWhiteSpace(load.Predownload?.Version))
        {
            predownloadCompleted = FindStaging(load.Predownload.Version, serverType, installedVersion) is not null;
        }

        return new UpdateCheckResult
        {
            Success = true,
            ServerVersion = manifest.Version,
            InstalledVersion = installedVersion,
            HasUpdate = hasUpdate,
            HasPredownload = load.HasPredownload && !predownloadCompleted,
            PredownloadCompleted = predownloadCompleted,
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
            return (false, null, CoreStrings.T("Launcher.NoGameDir", "未设置游戏目录"));
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
            return (false, null, CoreStrings.T("Core.Updater.NoPredownload", "当前没有可用的预载版本"));
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
            return (false, null, CoreStrings.T("Core.Updater.PredownloadNotFound", "未找到与本地版本匹配的官方预载补丁"));
        }

        var patchLoad = await _loader.LoadPatchAsync(
            patchUrl,
            baseUrl: cdnPrefix + "/" + (patchConfig.BaseUrl ?? "").TrimStart('/'),
            indexFileMd5: patchConfig.IndexFileMd5,
            ct: ct).ConfigureAwait(false);
        if (!patchLoad.Success || patchLoad.Manifest is null || patchLoad.Manifest.Files.Count == 0)
        {
            return (false, null, patchLoad.Message ?? CoreStrings.T("Core.Updater.PredownloadManifestUnavailable", "预载补丁清单不可用"));
        }

        // 为补丁文件补全下载地址(CDN + FromFolder + dest,与 Haiyu GetBaseUrl 一致)
        var patchFiles = patchLoad.Manifest.Files;
        // 预下载目标版本号记录,暂存目录按版本隔离。保留已有 .part 文件以支持断点续传。
        var downloadBytes = patchFiles.Sum(f => f.Size);
        // 暂存目录在游戏目录下的 DiffData:下载盘即游戏盘。
        var staging = _paths.PredownloadStagingDir(targetVersion);
        if (string.IsNullOrEmpty(staging))
        {
            return (false, null, CoreStrings.T("Launcher.NoGameDir", "未设置游戏目录"));
        }
        // [过渡代码 · 下个版本移除] 先把旧版数据目录里的预载包搬到游戏目录,再继续(可断点续传)。
        await MigrateLegacyPredownloadAsync(root, progress).WaitAsync(ct).ConfigureAwait(false);
        if (!HasFreeSpace(staging, downloadBytes))
        {
            return (false, null, CoreStrings.F("Core.Updater.DownloadDiskFull", $"预载下载盘空间不足: {FormatBytes(downloadBytes)}", FormatBytes(downloadBytes)));
        }
        var requiredBytes = patchConfig.Ext?.RequiredDiskSpace ?? patchConfig.UnCompressSize ?? 0;
        if (!HasFreeSpace(root, requiredBytes))
        {
            return (false, null, CoreStrings.F("Core.Updater.GameDiskFull", $"游戏盘空间不足: {FormatBytes(requiredBytes)}", FormatBytes(requiredBytes)));
        }
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
            CurrentFile = CoreStrings.F("Core.Updater.FilesFound", $"发现 {patchFiles.Count} 个待下载文件,开始下载…", patchFiles.Count),
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
            return (false, staging, CoreStrings.F("Core.Updater.DownloadFailures", $"有 {failures.Count} 个文件下载失败: {failures[0]}", failures.Count, failures[0]));
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
            return (false, CoreStrings.T("Launcher.NoGameDir", "未设置游戏目录"));
        }

        var indexUrl = _indexUrlProvider(serverType);
        var load = await _loader.LoadKuroAsync(indexUrl, preDownload: false, ct).ConfigureAwait(false);
        if (!load.Success || load.Manifest is null)
        {
            return (false, load.Message ?? CoreStrings.T("Core.Updater.ManifestFailed", "获取更新清单失败"));
        }

        var manifest = load.Manifest;
        var installedVersion = ReadInstalledVersion(root);
        // [过渡代码 · 下个版本移除] 等旧位置的预载包搬完再找暂存目录,
        // 否则升级后第一次点「安装更新」会在旧位置消费预载包(跨卷复制,慢)。
        await MigrateLegacyPredownloadAsync(root, progress).WaitAsync(ct).ConfigureAwait(false);
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
                    // 补丁包临时目录也在游戏目录下的 DiffData:下载盘即游戏盘,装完即删。
                    var tempDir = _paths.NewInstallTempDir();
                    if (string.IsNullOrEmpty(tempDir))
                    {
                        return (false, CoreStrings.T("Launcher.NoGameDir", "未设置游戏目录"));
                    }
                    if (!HasFreeSpace(tempDir, patchDownloadBytes))
                    {
                        return (false, CoreStrings.F("Core.Updater.DownloadDiskFull", $"更新下载盘空间不足: {FormatBytes(patchDownloadBytes)}", FormatBytes(patchDownloadBytes)));
                    }
                    if (!HasFreeSpace(root, patchDiskBytes))
                    {
                        return (false, CoreStrings.F("Core.Updater.GameDiskFull", $"游戏盘空间不足: {FormatBytes(patchDiskBytes)}", FormatBytes(patchDiskBytes)));
                    }
                    stagingDir = tempDir;
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
                    return (true, CoreStrings.F("Core.Updater.PatchInstalled", $"补丁安装完成,版本 {manifest.Version}", manifest.Version));
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
        return (true, fullResult.Message ?? CoreStrings.F("Core.Updater.Updated", $"更新完成,版本 {manifest.Version}", manifest.Version));
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
                CurrentFile = CoreStrings.F("Launcher.Verifying", $"正在校验本地文件 {p.Checked}/{p.Total}…", p.Checked, p.Total),
            }));
        }

        var diff = await Task.Run(
            () => _installer.ComputeDiff(manifest, root, progress: diffProgress, ct: ct),
            ct).ConfigureAwait(false);
        if (!diff.HasChanges)
        {
            return (true, CoreStrings.T("Core.Updater.FilesComplete", "游戏文件完整,无需额外下载"));
        }

        var downloadBytes = diff.ToDownload.Sum(file => file.Size);
        // 流式安装:直接下载到游戏根目录,边下载边更新(替换前自动备份到 .McKuro_backup)。
        // 不再需要 appData 上的一份临时暂存,磁盘余量只需满足游戏盘(下载体积 + 备份体积)。
        // 保守预留备份体积:被替换的旧文件会复制到备份目录,按下载体积估算。
        var requiredOnDisk = downloadBytes * 2;
        if (!HasFreeSpace(root, requiredOnDisk))
        {
            return (false, CoreStrings.F("Core.Updater.GameDiskFull", $"游戏盘空间不足(含备份预留): {FormatBytes(requiredOnDisk)}", FormatBytes(requiredOnDisk)));
        }

        try
        {
            var (_, failures) = await _downloader.DownloadManyAsync(
                diff.ToDownload,
                baseUrl: "",
                root,
                progress,
                ct,
                streamingGameRoot: root).ConfigureAwait(false);
            if (failures.Count > 0)
            {
                return (false, CoreStrings.F("Core.Updater.DownloadFailedFirst", $"下载失败: {failures[0]}", failures[0]));
            }
        }
        finally
        {
            // 清理残留的 .part(取消/失败时未移动的文件;成功时不存在)
            foreach (var entry in diff.ToDownload)
            {
                try
                {
                    var part = GameFilePath.CombineUnderRoot(root, entry.Path) + ".part";
                    if (File.Exists(part))
                    {
                        File.Delete(part);
                    }
                }
                catch
                {
                    // 忽略清理失败
                }
            }
        }

        // 末次复检降级为抽样核对:本次下载的每个文件在 FileDownloader 落位前已做 MD5 校验,
        // InstallFromStaging 安装前又校验一遍,全盘重读数万文件属冗余(分钟级)。
        // 抽样优先覆盖关键文件,并随机补充至最多 40 个。
        var (verified, sampleMessage) = await Task.Run(
            () => QuickVerifyAfterInstall(manifest, root, ct), ct).ConfigureAwait(false);
        return verified
            ? (true, CoreStrings.F("Core.Updater.FilesPatched", $"已补齐 {diff.ToDownload.Count} 个文件", diff.ToDownload.Count))
            : (false, sampleMessage!);
    }

    /// <summary>
    /// 安装后的抽样核对:关键文件全部复检 + 随机补充至最多 40 个,替代全量 MD5 复检。
    /// 任一抽样失败即返回失败(保守,触发下次修复兜底)。
    /// </summary>
    private static (bool Success, string? Message) QuickVerifyAfterInstall(
        GameManifest manifest,
        string root,
        CancellationToken ct)
    {
        var files = manifest.Files;
        if (files.Count == 0)
        {
            return (true, null);
        }

        var keySet = new HashSet<string>(manifest.KeyFiles, StringComparer.OrdinalIgnoreCase);
        var sample = new List<GameFileEntry>();
        foreach (var f in files)
        {
            if (keySet.Contains(f.Path))
            {
                sample.Add(f);
            }
        }

        var target = Math.Min(40, files.Count);
        var needed = target - sample.Count;
        if (needed > 0)
        {
            var pool = files.Where(f => !keySet.Contains(f.Path)).ToList();
            while (needed > 0 && pool.Count > 0)
            {
                var idx = Random.Shared.Next(pool.Count);
                sample.Add(pool[idx]);
                pool.RemoveAt(idx);
                needed--;
            }
        }

        foreach (var entry in sample)
        {
            ct.ThrowIfCancellationRequested();
            var localPath = GameFilePath.CombineUnderRoot(root, entry.Path);
            if (!File.Exists(localPath)
                || (!string.IsNullOrEmpty(entry.Md5) && !FileDownloader.VerifyLocalFile(localPath, entry)))
            {
                return (false, CoreStrings.F("Core.Updater.VerifySampleFailed", $"安装后抽样校验未通过: {entry.Path}", entry.Path));
            }
        }
        return (true, null);
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

    // ================== [过渡代码 · 下个版本整段删除] ==================
    // 旧版把预载包放在数据目录 %AppData%\McKuro\predownload\<版本>,吃掉系统盘空间且安装时
    // 必须跨卷复制;现改为游戏目录下的 DiffData\<版本>(与游戏同盘,安装即同卷改名)。
    // 这里负责把旧位置已有的预载包搬到新位置,让已经预下载好的用户不必重新下载几十 GB。
    //
    // 移除清单(删净后行为依旧正确 —— 新位置找不到就等于没预载过):
    //   1. 本区块的字段与方法(含 CleanLegacyInstallTemp);
    //   2. FindStaging 中的 LegacyStagingDir 回退分支;
    //   3. CheckUpdateAsync / PreDownloadAsync / InstallAsync 里的 MigrateLegacyPredownloadAsync 调用。
    // ==========================================================================

    private readonly object _legacyPredownloadSync = new();
    private Task? _legacyPredownloadTask;

    /// <summary>[过渡代码] 旧版预下载根目录(数据目录下的 predownload)。</summary>
    private string LegacyPredownloadRoot => Path.Combine(_appDataDir, "predownload");

    /// <summary>[过渡代码] 旧位置中指定版本的暂存目录。</summary>
    private string LegacyStagingDir(string version) => Path.Combine(LegacyPredownloadRoot, version);

    /// <summary>
    /// [过渡代码] 把旧版数据目录里的预载包搬到游戏目录 DiffData 下;并发调用共享同一次执行。
    /// 逐文件移动、标记文件最后搬,因此中途被打断时旧目录仍是一个完整可用的预载包,下次可续跑。
    /// 迁移失败不影响功能:<see cref="FindStaging"/> 会继续在旧位置找到预载包。
    /// </summary>
    /// <remarks>
    /// 共享任务刻意不绑定任何调用方的 CancellationToken(否则先调用方的取消会毒化后续调用方),
    /// 调用方若要支持自己的取消,请对返回的 Task 使用 <c>WaitAsync(ct)</c>。
    /// </remarks>
    internal Task MigrateLegacyPredownloadAsync(
        string gameRoot,
        IProgress<DownloadProgress>? progress = null)
    {
        if (string.IsNullOrEmpty(gameRoot) || string.IsNullOrEmpty(_paths.DiffDataDir) || !HasLegacyWork())
        {
            return Task.CompletedTask;
        }

        lock (_legacyPredownloadSync)
        {
            _legacyPredownloadTask ??= RunLegacyPredownloadMigrationAsync(progress);
            return _legacyPredownloadTask;
        }
    }

    /// <summary>[过渡代码] 数据目录里是否还有需要处理的旧版遗留(预载包或补丁临时目录)。</summary>
    private bool HasLegacyWork() =>
        Directory.Exists(LegacyPredownloadRoot) || Directory.Exists(Path.Combine(_appDataDir, "install_tmp"));

    /// <summary>
    /// [过渡代码] 后台触发一次旧预载目录迁移(不阻塞检查更新/界面)。
    /// 与 <see cref="MigrateLegacyPredownloadAsync"/> 共享同一个任务,重复调用无副作用。
    /// </summary>
    private void StartLegacyPredownloadMigrationInBackground()
    {
        var root = _paths.GameRootDir;
        if (string.IsNullOrEmpty(root) || !HasLegacyWork())
        {
            return;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                await MigrateLegacyPredownloadAsync(root).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "后台迁移旧预载目录失败(不影响功能)");
            }
        });
    }

    private async Task RunLegacyPredownloadMigrationAsync(IProgress<DownloadProgress>? progress)
    {
        var legacyRoot = LegacyPredownloadRoot;
        var destRoot = _paths.DiffDataDir!;
        var succeeded = false;
        try
        {
            List<string> versionDirs;
            try
            {
                versionDirs = Directory.Exists(legacyRoot)
                    ? Directory.EnumerateDirectories(legacyRoot)
                        .Where(dir => File.Exists(Path.Combine(dir, MarkerFileName)))
                        .ToList()
                    : [];
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "枚举旧预载目录失败: {Path}", legacyRoot);
                CleanLegacyInstallTemp();
                succeeded = true;
                return;
            }
            if (versionDirs.Count == 0)
            {
                // 没有待迁移的旧预载包,清掉空壳根目录后收工
                TryDeleteDirectory(legacyRoot);
                succeeded = true;
                CleanLegacyInstallTemp();
                return;
            }

            _logger.LogInformation(
                "开始迁移旧预载目录到游戏目录: {From} → {To}({Count} 个版本)",
                legacyRoot,
                destRoot,
                versionDirs.Count);
            var migratingText = CoreStrings.T("Core.Updater.MigratingPredownload", "正在把已预下载的数据迁移到游戏目录…");
            progress?.Report(new DownloadProgress
            {
                CurrentFile = migratingText,
                FileIndex = 0,
                FileTotal = 0,
                BytesDownloaded = 0,
                BytesTotal = 0,
                SpeedBps = 0,
                StageText = migratingText,
            });

            var allMoved = true;
            foreach (var legacyVersionDir in versionDirs)
            {
                try
                {
                    if (!await MigrateLegacyVersionAsync(legacyVersionDir, destRoot).ConfigureAwait(false))
                    {
                        allMoved = false;
                    }
                }
                catch (Exception ex)
                {
                    allMoved = false;
                    _logger.LogWarning(ex, "迁移旧预载版本失败,保留原目录: {Path}", legacyVersionDir);
                }
            }

            succeeded = allMoved;
            if (allMoved)
            {
                TryDeleteDirectory(legacyRoot);
                _logger.LogInformation("旧预载目录迁移完成: {Path}", legacyRoot);
            }
            CleanLegacyInstallTemp();
        }
        finally
        {
            // 中途失败时清掉缓存的任务,让下次调用(下次启动或下次预下载)重试;
            // 否则一次失败会把迁移永久锁死。成功时保留已完成任务,重复调用即空操作。
            if (!succeeded)
            {
                lock (_legacyPredownloadSync)
                {
                    _legacyPredownloadTask = null;
                }
            }
        }
    }

    /// <summary>
    /// [过渡代码] 清掉旧版留在数据目录的空壳 install_tmp。补丁临时目录已挪到游戏目录下的
    /// DiffData/install_tmp,旧目录里的内容都是上次安装的残留临时文件,不会再被复用。
    /// </summary>
    private void CleanLegacyInstallTemp()
    {
        var legacy = Path.Combine(_appDataDir, "install_tmp");
        if (Directory.Exists(legacy))
        {
            TryDeleteDirectory(legacy);
            _logger.LogInformation("已清理旧版补丁临时目录: {Path}", legacy);
        }
    }

    /// <summary>[过渡代码] 迁移单个版本的预载目录到 DiffData 下的同名目录;返回是否已处理完毕。</summary>
    private async Task<bool> MigrateLegacyVersionAsync(string legacyVersionDir, string destRoot)
    {
        var version = Path.GetFileName(legacyVersionDir);
        var destDir = Path.Combine(destRoot, version);
        var markerName = MarkerFileName;

        if (Directory.Exists(destDir))
        {
            // 新位置已有同版本目录:标记已就位说明搬完了,清掉旧副本;
            // 否则(新位置是正在下载的半成品)保留旧数据,等下次再判断。
            var marker = Path.Combine(destDir, markerName);
            if (File.Exists(marker))
            {
                TryDeleteDirectory(legacyVersionDir);
                return true;
            }
            _logger.LogWarning("新位置已有未完成的同版本预载目录,跳过迁移: {Path}", destDir);
            return false;
        }

        List<string> files;
        try
        {
            files = Directory.EnumerateFiles(legacyVersionDir, "*", SearchOption.AllDirectories).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "枚举旧预载文件失败: {Path}", legacyVersionDir);
            return false;
        }
        if (files.Count == 0)
        {
            // 空目录没有迁移价值,直接清掉
            TryDeleteDirectory(legacyVersionDir);
            return true;
        }

        // 标记文件必须最后搬:标记出现在新目录即代表文件已全部就位,
        // 因此中途被打断时旧目录仍是完整可用的预载包而非半成品。
        files = files
            .OrderBy(f => string.Equals(Path.GetFileName(f), markerName, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ToList();

        Directory.CreateDirectory(destDir);
        var moved = 0;
        var failed = 0;
        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(legacyVersionDir, file);
            var dest = Path.Combine(destDir, relative);
            var destParent = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(destParent))
            {
                Directory.CreateDirectory(destParent);
            }

            try
            {
                if (File.Exists(dest) && new FileInfo(dest).Length == new FileInfo(file).Length)
                {
                    // 上次迁移已搬过这个文件(同大小视为已就位),删掉旧副本即可续跑
                    File.Delete(file);
                    continue;
                }
                MoveFileAcrossVolumes(file, dest);
                moved++;
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogWarning(ex, "迁移预载文件失败(保留旧副本): {Path}", file);
            }
        }

        if (failed > 0)
        {
            _logger.LogWarning("旧预载版本迁移未完整完成: {Version}(成功 {Moved}/{Total},失败 {Failed})",
                version, moved, files.Count, failed);
            return false;
        }

        _logger.LogInformation("已迁移旧预载版本到游戏目录: {Version}({Moved}/{Total} 个文件)", version, moved, files.Count);
        TryDeleteDirectory(legacyVersionDir);
        return true;
    }

    /// <summary>[过渡代码] 跨卷移动文件:同卷走改名,跨卷退化为复制 + 删源。</summary>
    private static void MoveFileAcrossVolumes(string source, string dest)
    {
        try
        {
            File.Move(source, dest, overwrite: true);
        }
        catch (IOException)
        {
            // 跨卷时改名会失败(目标在不同卷),显式退化为复制 + 删源
            File.Copy(source, dest, overwrite: true);
            File.Delete(source);
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
            return (false, CoreStrings.T("Launcher.NoGameDir", "未设置游戏目录"));
        }

        var indexUrl = _indexUrlProvider(serverType);
        var load = await _loader.LoadKuroAsync(indexUrl, preDownload: false, ct).ConfigureAwait(false);
        if (!load.Success || load.Manifest is null)
        {
            return (false, load.Message ?? CoreStrings.T("Core.Updater.ManifestFailed", "获取更新清单失败"));
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
                CurrentFile = CoreStrings.F("Launcher.Verifying", $"正在检查本地文件 {p.Checked}/{p.Total}…", p.Checked, p.Total),
            }));
        var diff = await Task.Run(
            () => _installer.ComputeDiff(manifest, root, skipPaths, diffProgress, ct),
            ct).ConfigureAwait(false);
        if (!diff.HasChanges)
        {
            return (true, CoreStrings.T("Core.Updater.FilesCompleteRepair", "游戏文件完整,无需修复"));
        }

        // 流式修复:直接下载到游戏根目录,边下载边修复(替换前自动备份到 .McKuro_backup)。
        // 磁盘余量只需满足游戏盘(下载体积 + 备份预留)。
        var repairDownloadBytes = diff.ToDownload.Sum(file => file.Size);
        var requiredOnDisk = repairDownloadBytes * 2;
        if (!HasFreeSpace(root, requiredOnDisk))
        {
            return (false, CoreStrings.F("Core.Updater.GameDiskFull", $"游戏盘空间不足(含备份预留): {FormatBytes(requiredOnDisk)}", FormatBytes(requiredOnDisk)));
        }

        try
        {
            var (_, failures) = await _downloader.DownloadManyAsync(
                diff.ToDownload,
                baseUrl: "",
                root,
                progress,
                ct,
                streamingGameRoot: root).ConfigureAwait(false);
            if (failures.Count > 0)
            {
                return (false, CoreStrings.F("Core.Updater.DownloadFailedFirst", $"下载失败: {failures[0]}", failures[0]));
            }

            // 与 Haiyu 一致:仅在用户选择删除跳过文件时清理,不擅自删除游戏目录中的额外文件。
            if (deleteSkipped && skipPaths is not null)
            {
                int deleted = DeleteSkippedFiles(root, skipPaths);
                _logger.LogInformation("修复时删除被跳过文件 {Count} 个", deleted);
            }

            WriteInstalledVersion(root, manifest.Version);
            return (true, CoreStrings.F("Core.Updater.RepairDone", $"修复完成:重新下载 {diff.ToDownload.Count} 个文件,版本 {manifest.Version}", diff.ToDownload.Count, manifest.Version));
        }
        finally
        {
            // 清理残留的 .part(取消/失败时未移动的文件;成功时不存在)
            foreach (var entry in diff.ToDownload)
            {
                try
                {
                    var part = GameFilePath.CombineUnderRoot(root, entry.Path) + ".part";
                    if (File.Exists(part))
                    {
                        File.Delete(part);
                    }
                }
                catch
                {
                    // 忽略清理失败
                }
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
    /// 按当前设置组装游戏启动命令行(供启动与测试共用)。
    /// <para>
    /// 资源等级优先级:用户设置 → 按游戏目录里已安装的资源包自动探测(官方启动器在
    /// <c>launcherDownloadConfig\</c> 下留标记)→ 官方默认 hd。
    /// <c>-krqlv</c> 始终存在(缺失会导致游戏启动即崩溃)。
    /// </para>
    /// </summary>
    public string BuildLaunchCommandLine()
    {
        var s = _settings?.Current;
        return LaunchArguments.Build(s?.UseDx11 == true, ResolveResourceLevel(s), s?.StartGameArguments);
    }

    /// <summary>解析生效的资源等级:显式设置优先,其次按已装资源包探测,最后回退官方默认。</summary>
    private string ResolveResourceLevel(AppSettings? settings)
    {
        if (!string.IsNullOrWhiteSpace(settings?.ResourceLevel))
        {
            return settings!.ResourceLevel!;
        }

        return LaunchArguments.DetectInstalledResourceLevel(_paths.GameRootDir)
            ?? LaunchArguments.DefaultResourceLevel;
    }

    /// <summary>
    /// 启动游戏(对齐官方启动器:可选 exe + `Client -dx11 -slno -krqlv={资源等级} {自定义参数}` 命令行)。
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
            // 对齐官方启动器:Client -dx11 -slno -krqlv=hd {自定义参数}
            // -krqlv 是鸣潮 3.x 起必需的引导参数,缺失会导致游戏启动即崩溃退出。
            var args = BuildLaunchCommandLine();

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
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
        // 现行位置:游戏目录下 DiffData/<版本>。
        var staging = _paths.PredownloadStagingDir(version);
        var hit = InspectStaging(staging, version, serverType, sourceVersion);
        if (hit is not null)
        {
            return hit;
        }
        // [过渡代码 · 下个版本删除] 回退到旧位置(数据目录 predownload/<版本>):
        // 迁移尚未跑过(如用户升级后直接点「安装更新」)时,旧位置的完整预载包仍应被消费。
        return InspectStaging(LegacyStagingDir(version), version, serverType, sourceVersion);
    }

    /// <summary>校验暂存目录:目录与标记存在、标记完整且版本/来源版本/渠道匹配时返回该目录,否则 null。</summary>
    private string? InspectStaging(string? staging, string version, GameServerType? serverType, string? sourceVersion)
    {
        if (string.IsNullOrEmpty(staging))
        {
            return null;
        }
        var marker = Path.Combine(staging, MarkerFileName);
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
