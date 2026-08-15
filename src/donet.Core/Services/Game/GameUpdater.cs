using System.Text.Json;
using System.Text.Json.Serialization;
using donet.Core.Models.Game;

namespace donet.Core.Services.Game;

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

/// <summary>游戏更新编排:检查更新、预下载、安装、启动。</summary>
public sealed class GameUpdater
{
    private readonly GameManifestLoader _loader;
    private readonly DownloadEngine _downloader;
    private readonly UpdateInstaller _installer;
    private readonly GamePathResolver _paths;
    private readonly string _appDataDir;

    public GameUpdater(
        GameManifestLoader loader,
        DownloadEngine downloader,
        UpdateInstaller installer,
        GamePathResolver paths,
        string appDataDir)
    {
        _loader = loader;
        _downloader = downloader;
        _installer = installer;
        _paths = paths;
        _appDataDir = appDataDir;
    }

    /// <summary>检查更新(依据当前渠道的 index.json)。</summary>
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

        var indexUrl = KuroEndpoints.ForServerType(serverType);
        var load = await _loader.LoadKuroAsync(indexUrl, preDownload: false, ct).ConfigureAwait(false);
        if (!load.Success || load.Manifest is null)
        {
            return new UpdateCheckResult { Success = false, Message = load.Message ?? "获取更新清单失败" };
        }

        var manifest = load.Manifest;
        var installedVersion = ReadInstalledVersion(root);

        var diff = _installer.ComputeDiff(manifest, root);
        var notInstalled = !_paths.IsGameInstalled;

        return new UpdateCheckResult
        {
            Success = true,
            ServerVersion = manifest.Version,
            InstalledVersion = installedVersion,
            HasUpdate = notInstalled || diff.HasChanges || installedVersion != manifest.Version,
            HasPredownload = load.HasPredownload,
            PredownloadVersion = load.PredownloadVersion,
            FilesToDownload = diff.ToDownload,
            NotInstalled = notInstalled,
        };
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

        var indexUrl = KuroEndpoints.ForServerType(serverType);
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

        var baseUrl = diff.ToDownload.FirstOrDefault(f => !string.IsNullOrEmpty(f.Url))?.Url is null
            ? BuildBaseUrl(indexUrl)
            : "";

        var (success, failures) = await _downloader.DownloadManyAsync(
            diff.ToDownload,
            baseUrl,
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

        var indexUrl = KuroEndpoints.ForServerType(serverType);
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
        catch (Exception)
        {
            // 忽略清理失败
        }

        WriteInstalledVersion(root, manifest.Version);
        return (true, $"已安装 {installed2} 个文件,版本 {manifest.Version}");
    }

    /// <summary>启动游戏。</summary>
    public bool LaunchGame(out string? error)
    {
        var exe = _paths.RootExePath;
        if (exe is null || !File.Exists(exe))
        {
            error = "未找到游戏主程序";
            return false;
        }

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = exe,
                WorkingDirectory = Path.GetDirectoryName(exe) ?? "",
                UseShellExecute = true,
            };
            System.Diagnostics.Process.Start(psi);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private string? FindStaging(string version)
    {
        var staging = Path.Combine(_appDataDir, "predownload", version);
        if (Directory.Exists(staging) && File.Exists(Path.Combine(staging, "predownload.json")))
        {
            return staging;
        }
        return null;
    }

    private static string BuildBaseUrl(string indexUrl)
    {
        // index.json 同目录下通常为资源清单所在目录
        var uri = new Uri(indexUrl);
        var baseUri = new Uri(uri, ".");
        return baseUri.ToString();
    }

    private string? ReadInstalledVersion(string gameRoot)
    {
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
        catch (Exception)
        {
            return null;
        }
    }

    private void WriteInstalledVersion(string gameRoot, string version)
    {
        var marker = Path.Combine(_appDataDir, "installed_versions.json");
        Dictionary<string, string> dict;
        if (File.Exists(marker))
        {
            try
            {
                var json = File.ReadAllText(marker);
                dict = JsonSerializer.Deserialize(json, GameMetaJsonContext.Default.DictionaryStringString) ?? [];
            }
            catch (Exception)
            {
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
