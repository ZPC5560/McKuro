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
    {
        _loader = loader;
        _downloader = downloader;
        _installer = installer;
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

    /// <summary>预下载:将需要更新的文件下载到暂存目录,不触碰游戏目录。</summary>
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
        var diff = _installer.ComputeDiff(manifest, root);
        if (!diff.HasChanges)
        {
            return (true, null, "游戏已是最新版本,无需下载");
        }

        // 预下载目标版本号记录,暂存目录按版本隔离
        var staging = Path.Combine(_appDataDir, "predownload", manifest.Version);
        if (Directory.Exists(staging))
        {
            Directory.Delete(staging, recursive: true);
        }

        // 文件均带完整 CDN 下载地址(entry.Url),baseUrl 留空即可
        var (success, failures) = await _downloader.DownloadManyAsync(
            diff.ToDownload,
            baseUrl: "",
            staging,
            progress,
            ct).ConfigureAwait(false);

        if (failures.Count > 0)
        {
            return (false, staging, $"有 {failures.Count} 个文件下载失败: {failures[0]}");
        }

        // 记录预下载元信息
        var meta = new PreDownloadMeta { Version = manifest.Version, ServerType = serverType };
        var json = JsonSerializer.Serialize(meta, GameMetaJsonContext.Default.PreDownloadMeta);
        await File.WriteAllTextAsync(Path.Combine(staging, "predownload.json"), json, ct).ConfigureAwait(false);

        return (true, staging, null);
    }

    /// <summary>
    /// 执行安装:若存在预下载暂存目录则从暂存安装,否则直接下载并安装。
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
            return (false, load.Message);
        }

        var manifest = load.Manifest;
        var diff = _installer.ComputeDiff(manifest, root);

        // 优先使用预下载暂存
        string? stagingDir = FindStaging(manifest.Version);
        if (stagingDir is not null)
        {
            var (installed, failures) = _installer.InstallFromStaging(stagingDir, root, manifest);
            if (failures.Count > 0)
            {
                return (false, $"安装失败: {failures[0]}");
            }

            WriteInstalledVersion(root, manifest.Version);
            return (true, $"已从预下载安装 {installed} 个文件");
        }

        if (!diff.HasChanges)
        {
            WriteInstalledVersion(root, manifest.Version);
            return (true, "游戏已是最新版本");
        }

        // 直接下载并安装(安装到临时目录,完成后整体搬移)
        var tempInstall = Path.Combine(_appDataDir, "install_tmp", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempInstall);
        var baseUrl = "";
        var (success, failures2) = await _downloader.DownloadManyAsync(
            diff.ToDownload,
            baseUrl,
            tempInstall,
            progress,
            ct).ConfigureAwait(false);

        if (failures2.Count > 0)
        {
            return (false, $"下载失败: {failures2[0]}");
        }

        var (installed2, failures3) = _installer.InstallFromStaging(tempInstall, root, manifest);
        if (failures3.Count > 0)
        {
            return (false, $"安装失败: {failures3[0]}");
        }

        try
        {
            Directory.Delete(tempInstall, recursive: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "清理临时安装目录失败: {Dir}", tempInstall);
        }

        WriteInstalledVersion(root, manifest.Version);
        return (true, $"已安装 {installed2} 个文件,版本 {manifest.Version}");
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
        var diff = _installer.ComputeDiff(manifest, root, skipPaths);
        if (!diff.HasChanges)
        {
            return (true, "游戏文件完整,无需修复");
        }

        // 下载缺失/损坏文件到临时目录,再整体安装
        var tempInstall = Path.Combine(_appDataDir, "repair_tmp", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempInstall);
        var (success, failures) = await _downloader.DownloadManyAsync(
            diff.ToDownload,
            baseUrl: "",
            tempInstall,
            progress,
            ct).ConfigureAwait(false);

        if (failures.Count > 0)
        {
            return (false, $"下载失败: {failures[0]}");
        }

        var (installed, installFailures) = _installer.InstallFromStaging(tempInstall, root, manifest);
        if (installFailures.Count > 0)
        {
            return (false, $"安装失败: {installFailures[0]}");
        }

        try
        {
            Directory.Delete(tempInstall, recursive: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "清理修复临时目录失败: {Dir}", tempInstall);
        }

        // 删除被跳过的文件(对齐 Haiyu:修复将缓存全部删除,保持与服务器最新一致)
        if (deleteSkipped && skipPaths is not null)
        {
            int deleted = DeleteSkippedFiles(root, skipPaths);
            _logger.LogInformation("修复时删除被跳过文件 {Count} 个", deleted);
        }

        WriteInstalledVersion(root, manifest.Version);
        return (true, $"修复完成:重新下载 {installed} 个文件,版本 {manifest.Version}");
    }

    private static int DeleteSkippedFiles(string gameRoot, IReadOnlySet<string> skipPaths)
    {
        int deleted = 0;
        foreach (var relative in skipPaths)
        {
            var full = Path.Combine(gameRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            try
            {
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

    private string? FindStaging(string version)
    {
        var staging = Path.Combine(_appDataDir, "predownload", version);
        if (Directory.Exists(staging) && File.Exists(Path.Combine(staging, "predownload.json")))
        {
            return staging;
        }
        return null;
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
    [JsonPropertyName("serverType")] public GameServerType ServerType { get; set; }
    [JsonPropertyName("time")] public DateTime Time { get; set; } = DateTime.Now;
}

[JsonSerializable(typeof(PreDownloadMeta))]
[JsonSerializable(typeof(Dictionary<string, string>))]
public sealed partial class GameMetaJsonContext : JsonSerializerContext;
