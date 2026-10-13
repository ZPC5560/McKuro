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

    // ---------- 删除资源包(回收磁盘) ----------

    /// <summary>
    /// 列出当前<b>已安装</b>的包及其可否删除(供 UI 展示「删除」按钮)。
    /// <para>
    /// 用于让用户回收旧画质包占用的磁盘(如从 UHD 降到 SD 后可删 uhd,实测 61.5 GB)。
    /// 共享包 common 与「当前唯一可用等级」的包会被标为不可删,理由见
    /// <see cref="ResourceLevelPlanner.CanDeletePack"/>。
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<ResourcePackInfo>> GetInstalledPacksAsync(
        GameServerType serverType,
        CancellationToken ct = default)
    {
        var root = _paths.GameRootDir;
        var indexUrl = ResolveIndexUrl(serverType);
        if (string.IsNullOrEmpty(root) || indexUrl is null)
        {
            return [];
        }

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
            _logger.LogWarning(ex, "读取资源包列表失败");
            return [];
        }

        if (index?.ResourcePacks is null)
        {
            return [];
        }

        var installed = GetInstalledPackNames(root, index);
        var current = _settings?.Current.ResourceLevel;
        var result = new List<ResourcePackInfo>();
        foreach (var (packName, pack) in index.ResourcePacks)
        {
            if (!installed.Contains(packName))
            {
                continue;
            }

            var reason = ResourceLevelPlanner.CanDeletePack(index, packName, current, installed);
            result.Add(new ResourcePackInfo
            {
                PackName = packName,
                SizeBytes = ResourceLevelPlanner.PackBytes(pack),
                IsShared = reason == ResourceLevelPlanner.PackDeleteBlockReason.SharedPack,
                CanDelete = reason == ResourceLevelPlanner.PackDeleteBlockReason.None,
                BlockReason = reason switch
                {
                    ResourceLevelPlanner.PackDeleteBlockReason.SharedPack =>
                        CoreStrings.T("Launcher.PackShared", "共享包(所有画质等级都用到),不能删除"),
                    ResourceLevelPlanner.PackDeleteBlockReason.ActiveLevelOnly =>
                        CoreStrings.T("Launcher.PackActiveLevelOnly", "这是当前唯一可用的画质等级,删除后游戏可能无法正常启动"),
                    _ => null,
                },
            });
        }

        // 大包在前,便于用户优先看到能回收多少空间
        return [.. result.OrderByDescending(p => p.SizeBytes)];
    }

    /// <summary>已安装的包名集合(依据官方 launcherDownloadConfig 标记)。</summary>
    private static HashSet<string> GetInstalledPackNames(string gameRoot, KuroResourcePackIndex index)
    {
        var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (index.ResourcePacks is null)
        {
            return installed;
        }

        foreach (var (packName, _) in index.ResourcePacks)
        {
            if (HasInstalledMarker(gameRoot, packName))
            {
                installed.Add(packName);
            }
        }
        return installed;
    }

    /// <summary>
    /// 删除某个已安装的资源包(按包清单逐个删文件 + 删官方安装标记),回收磁盘。
    /// <para>
    /// <b>安全前置(比上游更严)</b>:共享包、以及「当前唯一可用等级」的包会被拒绝。
    /// 若删除的包属于当前等级且存在其它完整等级,会自动把设置回退到那个等级,
    /// 避免用户停留在"设置指向残缺等级"的状态(上游只提示、不处理)。
    /// </para>
    /// <para>
    /// 只删该包清单里列出的文件;清单外的文件不动(避免误删用户资源或其它包的文件)。
    /// </para>
    /// </summary>
    public async Task<ResourcePackDeleteResult> DeletePackAsync(
        GameServerType serverType,
        string packName,
        IProgress<ResourcePackDeleteProgress>? progress = null,
        CancellationToken ct = default)
    {
        var root = _paths.GameRootDir;
        var indexUrl = ResolveIndexUrl(serverType);
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root) || indexUrl is null)
        {
            return ResourcePackDeleteResult.Fail(CoreStrings.T("Launcher.NoGameDir", "未设置游戏目录"));
        }

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
            _logger.LogWarning(ex, "删除资源包前读取索引失败");
            return ResourcePackDeleteResult.Fail(CoreStrings.T("Launcher.LevelPlanFailed", "获取资源等级清单失败"));
        }

        if (index?.ResourcePacks is null || !index.ResourcePacks.TryGetValue(packName, out var pack))
        {
            return ResourcePackDeleteResult.Fail(CoreStrings.F("Launcher.PackNotFound", $"未找到资源包: {packName}", packName));
        }

        var installed = GetInstalledPackNames(root, index);
        if (!installed.Contains(packName))
        {
            return ResourcePackDeleteResult.Fail(CoreStrings.F("Launcher.PackNotInstalled", $"该资源包未安装: {packName}", packName));
        }

        // 安全闸:共享包 / 当前唯一等级 → 拒绝
        var current = _settings?.Current.ResourceLevel;
        var reason = ResourceLevelPlanner.CanDeletePack(index, packName, current, installed);
        if (reason != ResourceLevelPlanner.PackDeleteBlockReason.None)
        {
            return ResourcePackDeleteResult.Fail(reason == ResourceLevelPlanner.PackDeleteBlockReason.SharedPack
                ? CoreStrings.T("Launcher.PackShared", "共享包(所有画质等级都用到),不能删除")
                : CoreStrings.T("Launcher.PackActiveLevelOnly", "这是当前唯一可用的画质等级,删除后游戏可能无法正常启动"));
        }

        // 取出包清单(只有清单里的文件才允许删)
        var cdn = index.CdnList?
            .Where(c => !string.IsNullOrWhiteSpace(c.Url))
            .OrderBy(c => c.P == 0 ? int.MaxValue : c.P)
            .FirstOrDefault()?.Url;
        if (string.IsNullOrWhiteSpace(cdn))
        {
            return ResourcePackDeleteResult.Fail(CoreStrings.T("Launcher.LevelPlanFailed", "获取资源等级清单失败"));
        }

        var load = await _loader.LoadResourcePackAsync(pack, cdn, ct: ct).ConfigureAwait(false);
        if (!load.Success || load.Manifest is null)
        {
            return ResourcePackDeleteResult.Fail(CoreStrings.F("Launcher.PackManifestFailed", $"读取资源包清单失败: {packName}", packName));
        }

        // 回退等级:仅当删的是**当前生效等级**自己的包时才需要回退。
        // 删无关等级(如当前 SD、删 UHD)绝不能顺手改用户的画质选择 —— 那会把
        // 「回收磁盘」变成「静默改画质」。判据由 planner 依 currentLevel 判定。
        var fallback = ResourceLevelPlanner.NeedsFallbackAfterDelete(index, packName, current, installed)
            ? ResourceLevelPlanner.FallbackLevelAfterDelete(index, packName, installed)
            : null;

        var files = load.Manifest.Files;
        long freed = 0;
        int deleted = 0, failed = 0;
        for (var i = 0; i < files.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var entry = files[i];
            progress?.Report(new ResourcePackDeleteProgress
            {
                PackName = packName,
                Done = i,
                Total = files.Count,
            });

            try
            {
                var path = GameFilePath.CombineUnderRoot(root, entry.Path);
                if (!File.Exists(path))
                {
                    continue;
                }

                var length = new FileInfo(path).Length;
                File.Delete(path);
                freed += length;
                deleted++;
            }
            catch (Exception ex)
            {
                // 单个文件删不掉(占用/权限)不中断整体,计入失败
                failed++;
                _logger.LogDebug(ex, "删除资源文件失败: {Path}", entry.Path);
            }
        }

        progress?.Report(new ResourcePackDeleteProgress
        {
            PackName = packName,
            Done = files.Count,
            Total = files.Count,
        });

        // 删完标记:标记还在的话会被判为"已安装",下次切换等级不会重新下载
        DeleteInstalledMarker(root, packName);

        if (fallback is not null)
        {
            ApplyResourceLevelSetting(fallback);
            _logger.LogInformation("资源包 {Pack} 已删除,画质等级回退为 {Level}", packName, fallback);
        }

        return new ResourcePackDeleteResult
        {
            Success = true,
            DeletedFiles = deleted,
            FailedFiles = failed,
            FreedBytes = freed,
            FallbackLevel = fallback,
        };
    }

    /// <summary>删除官方已安装标记;失败只记日志(标记残留会让该包被误判为已安装)。</summary>
    private void DeleteInstalledMarker(string gameRoot, string packName)
    {
        try
        {
            var path = Path.Combine(gameRoot, "launcherDownloadConfig", packName.ToLowerInvariant() + ".json");
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "删除资源包标记失败: {Pack}", packName);
        }
    }
}

/// <summary>可删除的资源包信息(供 UI 展示)。</summary>
public sealed class ResourcePackInfo
{
    public required string PackName { get; init; }

    /// <summary>包体字节数(用于展示"可回收 X GB")。</summary>
    public long SizeBytes { get; init; }

    /// <summary>是否为共享包(common)。</summary>
    public bool IsShared { get; init; }

    /// <summary>是否允许删除。</summary>
    public bool CanDelete { get; init; }

    /// <summary>不可删除的原因(可直接展示);可删时为 null。</summary>
    public string? BlockReason { get; init; }
}

/// <summary>资源包删除进度。</summary>
public sealed class ResourcePackDeleteProgress
{
    public required string PackName { get; init; }
    public required int Done { get; init; }
    public required int Total { get; init; }
}

/// <summary>资源包删除结果。</summary>
public sealed class ResourcePackDeleteResult
{
    public required bool Success { get; init; }
    public string? Message { get; init; }

    /// <summary>成功删除的文件数。</summary>
    public int DeletedFiles { get; init; }

    /// <summary>删除失败的文件数(占用/权限)。</summary>
    public int FailedFiles { get; init; }

    /// <summary>回收的字节数。</summary>
    public long FreedBytes { get; init; }

    /// <summary>删除后自动回退到的等级(仅当删的是当前等级的包);未回退为 null。</summary>
    public string? FallbackLevel { get; init; }

    public static ResourcePackDeleteResult Fail(string message) => new() { Success = false, Message = message };
}
