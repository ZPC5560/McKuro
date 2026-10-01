using McKuro.Core.Models.Game;
using McKuro.Core.Services.Settings;
using Microsoft.Extensions.Logging;

namespace McKuro.Core.Services.Game;

/// <summary>资源等级切换结果。</summary>
public sealed class ResourceLevelResult
{
    public required bool Success { get; init; }
    public string? Message { get; init; }

    /// <summary>是否未做任何事(等级已装好)。</summary>
    public bool AlreadyInstalled { get; init; }

    /// <summary>本次下载并安装的文件数。</summary>
    public int InstalledFiles { get; init; }

    /// <summary>实际下载字节数。</summary>
    public long DownloadedBytes { get; init; }
}

/// <summary>
/// 资源等级(画质包)下载与安装。
/// <para>
/// 官方新版协议把游戏资源按等级拆成 common + {hd|sd|uhd} 三个包,<c>bundles</c> 描述组合关系
/// (HD=[common,hd]、SD=[common,sd]、UHD=[common,uhd])。各等级包落盘目录互不重叠,common 共享,
/// 因此切换到更高(或更低)画质 = 下载目标等级包 + 缺失的 common,安装即直接落位到游戏目录,
/// 既不需要删除已有等级的文件,也不存在覆盖冲突。
/// </para>
/// <para>
/// 说明:实测请求未安装的等级时游戏并不会崩溃(会自行回退),因此本功能的目标是让**实际画质
/// 与用户选择一致**,而不是修复启动问题(启动崩溃只由缺少 -krqlv 引起)。
/// </para>
/// </summary>
public sealed class ResourceLevelService
{
    private readonly GameManifestLoader _loader;
    private readonly DownloadEngine _downloader;
    private readonly GamePathResolver _paths;
    private readonly ISettingsService? _settings;
    private readonly Func<GameServerType, string?>? _indexUrlProvider;
    private readonly ILogger<ResourceLevelService> _logger;

    public ResourceLevelService(
        GameManifestLoader loader,
        DownloadEngine downloader,
        GamePathResolver paths,
        ISettingsService? settings = null,
        Func<GameServerType, string?>? indexUrlProvider = null,
        ILogger<ResourceLevelService>? logger = null)
    {
        _loader = loader;
        _downloader = downloader;
        _paths = paths;
        _settings = settings;
        _indexUrlProvider = indexUrlProvider;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ResourceLevelService>.Instance;
    }

    /// <summary>当前渠道是否支持资源等级切换(仅国服官方提供新版协议端点)。</summary>
    public static bool SupportsResourceLevels(GameServerType serverType) =>
        KuroEndpoints.ResourcePackIndexForServerType(serverType) is not null;

    private string? ResolveIndexUrl(GameServerType serverType) =>
        _indexUrlProvider is not null
            ? _indexUrlProvider(serverType)
            : KuroEndpoints.ResourcePackIndexForServerType(serverType);

    /// <summary>
    /// 读取各资源等级及其体积/是否已安装(供官方样式的「选择资源等级」面板展示)。
    /// 不支持该协议的渠道返回空列表。
    /// </summary>
    public async Task<IReadOnlyList<ResourceLevelOption>> GetLevelOptionsAsync(
        GameServerType serverType,
        CancellationToken ct = default)
    {
        var root = _paths.GameRootDir;
        var indexUrl = ResolveIndexUrl(serverType);
        if (string.IsNullOrEmpty(root) || indexUrl is null)
        {
            return [];
        }

        try
        {
            var index = await _loader.LoadResourcePackIndexAsync(indexUrl, ct).ConfigureAwait(false);
            if (index?.ResourcePacks is null || index.Bundles is null)
            {
                return [];
            }

            // 已安装标记:官方在 launcherDownloadConfig/{uhd,hd,sd}.json 留标记,优先采信(免去几十 GB 的全量校验)。
            var options = new List<ResourceLevelOption>();
            foreach (var (level, value, bundleName) in ResourceLevelPlanner.EnumerateLevels(index))
            {
                var packs = ResourceLevelPlanner.ResolvePackNames(index, bundleName);
                long total = 0;
                bool installed = true;
                foreach (var packName in packs)
                {
                    if (!index.ResourcePacks.TryGetValue(packName, out var pack))
                    {
                        installed = false;
                        continue;
                    }

                    // 包清单未加载时,回退 index.json 声明的 size(够 UI 展示)
                    total += ResourceLevelPlanner.PackBytes(pack);
                    if (!HasInstalledMarker(root, packName))
                    {
                        installed = false;
                    }
                }

                options.Add(new ResourceLevelOption
                {
                    Level = level,
                    Value = value,
                    BundleName = bundleName,
                    TotalBytes = total,
                    Installed = installed,
                });
            }
            return options;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "读取资源等级列表失败");
            return [];
        }
    }

    /// <summary>官方已安装标记:&lt;游戏目录&gt;/launcherDownloadConfig/{packName}.json。</summary>
    private static bool HasInstalledMarker(string gameRoot, string packName)
    {
        try
        {
            return File.Exists(Path.Combine(gameRoot, "launcherDownloadConfig", packName.ToLowerInvariant() + ".json"));
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// 计算切换到目标等级需要下载的内容。已安装则返回 <see cref="ResourceLevelPlan.HasWork"/> = false。
    /// </summary>
    public async Task<ResourceLevelPlan?> PlanAsync(
        GameServerType serverType,
        string targetLevel,
        CancellationToken ct = default)
    {
        var root = _paths.GameRootDir;
        var indexUrl = ResolveIndexUrl(serverType);
        if (string.IsNullOrEmpty(root) || indexUrl is null)
        {
            return null;
        }

        var normalized = LaunchArguments.Normalize(targetLevel);
        KuroResourcePackIndex? index;
        try
        {
            index = await _loader.LoadResourcePackIndexAsync(indexUrl, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 网络/HTTP 失败不应炸到 UI:返回 null 由调用方给出可读提示
            _logger.LogWarning(ex, "获取资源等级 index.json 失败");
            return null;
        }

        if (index?.ResourcePacks is null)
        {
            return null;
        }

        var bundleName = ResourceLevelPlanner.LevelToBundleName(normalized);
        var packNames = ResourceLevelPlanner.ResolvePackNames(index, bundleName);

        // 选 CDN:优先官方 P 优先级(0 视为最低)。
        var cdn = index.CdnList?
            .Where(c => !string.IsNullOrWhiteSpace(c.Url))
            .OrderBy(c => c.P == 0 ? int.MaxValue : c.P)
            .FirstOrDefault()?.Url;
        if (string.IsNullOrWhiteSpace(cdn))
        {
            return null;
        }

        var plan = new ResourceLevelPlan { TargetLevel = normalized, BundleName = bundleName };

        // 只下载尚未装好的包(common 缺失时才补,避免重复下载几十 GB)
        bool allInstalled = true;
        var packages = new List<(string PackName, GameManifest Manifest)>();
        foreach (var packName in packNames)
        {
            if (!index.ResourcePacks.TryGetValue(packName, out var pack))
            {
                continue;
            }

            if (HasInstalledMarker(root, packName))
            {
                continue;
            }

            allInstalled = false;
            var load = await _loader.LoadResourcePackAsync(pack, cdn, ct: ct).ConfigureAwait(false);
            if (!load.Success || load.Manifest is null)
            {
                _logger.LogWarning("加载资源包 {Pack} 清单失败: {Msg}", packName, load.Message);
                return null;
            }

            packages.Add((packName, load.Manifest));
        }

        return new ResourceLevelPlan
        {
            TargetLevel = normalized,
            BundleName = bundleName,
            AlreadyInstalled = allInstalled,
            Packages = packages,
        };
    }

    /// <summary>
    /// 执行资源等级切换:下载目标等级包并直接安装到游戏目录(边下边装,不占双份磁盘)。
    /// </summary>
    public async Task<ResourceLevelResult> SwitchAsync(
        GameServerType serverType,
        string targetLevel,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        var root = _paths.GameRootDir;
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
        {
            return new ResourceLevelResult { Success = false, Message = CoreStrings.T("Launcher.NoGameDir", "未设置游戏目录") };
        }

        if (!SupportsResourceLevels(serverType) && _indexUrlProvider is null)
        {
            return new ResourceLevelResult
            {
                Success = false,
                Message = CoreStrings.T("Launcher.LevelUnsupported", "当前渠道暂不支持资源等级切换(仅国服官方)"),
            };
        }

        var plan = await PlanAsync(serverType, targetLevel, ct).ConfigureAwait(false);
        if (plan is null)
        {
            return new ResourceLevelResult { Success = false, Message = CoreStrings.T("Launcher.LevelPlanFailed", "获取资源等级清单失败") };
        }

        if (plan.AlreadyInstalled || !plan.HasWork)
        {
            // 已装好:仍要记录用户选择,避免"选了却不生效"
            ApplyResourceLevelSetting(plan.TargetLevel);
            return new ResourceLevelResult { Success = true, AlreadyInstalled = true, Message = null };
        }

        int installed = 0;
        long downloaded = 0;
        try
        {
            foreach (var (packName, manifest) in plan.Packages)
            {
                ct.ThrowIfCancellationRequested();

                // 直接落位到游戏目录(边下边装):各等级目录互不重叠,不会覆盖其它等级
                var (success, failures) = await _downloader.DownloadManyAsync(
                    manifest.Files,
                    baseUrl: "",
                    destDir: root,
                    progress,
                    ct,
                    streamingGameRoot: root).ConfigureAwait(false);

                if (failures.Count > 0)
                {
                    _logger.LogWarning("资源包 {Pack} 安装存在失败项: {First}", packName, failures[0]);
                }

                if (success == 0 && manifest.Files.Count > 0)
                {
                    return new ResourceLevelResult
                    {
                        Success = false,
                        Message = failures.Count > 0
                            ? CoreStrings.F("Launcher.LevelInstallFailed", $"画质包安装失败: {failures[0]}", failures[0])
                            : CoreStrings.T("Launcher.LevelInstallFailedShort", "画质包安装失败"),
                    };
                }

                installed += success;
                downloaded += manifest.Files.Where(f => f.Size > 0).Sum(f => f.Size);

                // 安装完成即写官方标记文件,与官方启动器的「已安装」判据保持一致
                WriteInstalledMarker(root, packName, manifest.Version);
            }

            ApplyResourceLevelSetting(plan.TargetLevel);
            return new ResourceLevelResult
            {
                Success = true,
                InstalledFiles = installed,
                DownloadedBytes = downloaded,
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "切换资源等级失败");
            return new ResourceLevelResult { Success = false, Message = ex.Message };
        }
    }

    /// <summary>写入官方已安装标记 launcherDownloadConfig/{pack}.json。</summary>
    private void WriteInstalledMarker(string gameRoot, string packName, string version)
    {
        try
        {
            var dir = Path.Combine(gameRoot, "launcherDownloadConfig");
            Directory.CreateDirectory(dir);
            var payload = $"{{\"packName\":\"{packName.ToLowerInvariant()}\",\"version\":\"{version}\"}}";
            File.WriteAllText(Path.Combine(dir, packName.ToLowerInvariant() + ".json"), payload);
        }
        catch (Exception ex)
        {
            // 标记写入失败不影响资源本身已安装的事实;下次按标记判断会重复下载,
            // 但 .part/已完成文件会被 FileDownloader 的跳过逻辑快速命中。
            _logger.LogWarning(ex, "写入资源包标记失败: {Pack}", packName);
        }
    }

    /// <summary>把用户选择的等级写入设置(与设置页共用同一字段)。</summary>
    private void ApplyResourceLevelSetting(string level)
    {
        if (_settings is null)
        {
            return;
        }

        _settings.Current.ResourceLevel = level;
        _settings.Save();
    }
}
