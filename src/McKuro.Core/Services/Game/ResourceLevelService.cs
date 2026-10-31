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

    /// <summary>
    /// 读取「本地已安装的游戏主版本」(由 DI 注入,通常是 GameUpdater 的版本记录)。
    /// <para>
    /// 用于<b>版本落后判定</b>:见 <see cref="IsPackInstalledAtVersion(string, string, string?, string?)"/>。
    /// 为 null 时退化为"只比标记里的包版本"(旧行为)。
    /// </para>
    /// </summary>
    private readonly Func<string?>? _installedVersionProvider;

    public ResourceLevelService(
        GameManifestLoader loader,
        DownloadEngine downloader,
        GamePathResolver paths,
        ISettingsService? settings = null,
        Func<GameServerType, string?>? indexUrlProvider = null,
        ILogger<ResourceLevelService>? logger = null,
        Func<string?>? installedVersionProvider = null)
    {
        _loader = loader;
        _downloader = downloader;
        _paths = paths;
        _settings = settings;
        _indexUrlProvider = indexUrlProvider;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ResourceLevelService>.Instance;
        _installedVersionProvider = installedVersionProvider;
    }

    /// <summary>
    /// 当前渠道是否支持资源等级切换。
    /// <para>判据是「该渠道有没有新版协议端点」:国服官方 / 哔哩哔哩 / 国际服三家都有,
    /// WeGame 没有(见 <see cref="KuroEndpoints.ResourcePackIndexForServerType"/> 里的实测记录)。</para>
    /// </summary>
    public static bool SupportsResourceLevels(GameServerType serverType) =>
        KuroEndpoints.ResourcePackIndexForServerType(serverType) is not null;

    private string? ResolveIndexUrl(GameServerType serverType) =>
        _indexUrlProvider is not null
            ? _indexUrlProvider(serverType)
            : KuroEndpoints.ResourcePackIndexForServerType(serverType);

    /// <summary>
    /// 读取各资源等级及其体积/是否已安装(供官方样式的「选择资源等级」面板展示),
    /// 顺带取出官方「画质怎么选」说明页链接(同一个 index.json 里就有,不额外发请求)。
    /// 不支持该协议的渠道返回空结果。
    /// </summary>
    public async Task<ResourceLevelOptions> GetLevelOptionsAsync(
        GameServerType serverType,
        CancellationToken ct = default)
    {
        var root = _paths.GameRootDir;
        var indexUrl = ResolveIndexUrl(serverType);
        if (string.IsNullOrEmpty(root) || indexUrl is null)
        {
            return new ResourceLevelOptions();
        }

        try
        {
            var index = await _loader.LoadResourcePackIndexAsync(indexUrl, ct).ConfigureAwait(false);
            if (index?.ResourcePacks is null || index.Bundles is null)
            {
                return new ResourceLevelOptions();
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
                    // 版本一致才算已安装:只判标记存在会把"材质包只更版本"的情形永久漏掉。
                    if (!IsPackInstalled(root, packName, pack.Version))
                    {
                        installed = false;
                    }
                }

                // 「实际还需下载多少」只累加本地缺失的包:common 已装时不该再算进去,
                // 否则已装 HD 的用户切 UHD 会被多报 37.95 GB(实测 99.45 vs 61.50)。
                var missing = ResourceLevelPlanner.MissingBytesFor(
                    index,
                    bundleName,
                    packName => !index.ResourcePacks.TryGetValue(packName, out var p)
                        || IsPackInstalled(root, packName, p.Version));

                options.Add(new ResourceLevelOption
                {
                    Level = level,
                    Value = value,
                    BundleName = bundleName,
                    TotalBytes = total,
                    DownloadBytes = missing,
                    Installed = installed,
                });
            }

            return new ResourceLevelOptions
            {
                Items = options,
                HintUrl = ResourceLevelPlanner.LevelHintUrl(index, CoreStrings.CurrentLanguage),
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "读取资源等级列表失败");
            return new ResourceLevelOptions();
        }
    }

    /// <summary>
    /// 资源包名能否安全地拼进文件名:非空、不超过 64 字符,只含 ASCII 字母/数字/<c>_</c>/<c>-</c>
    /// (等价 <c>^[a-z0-9_-]+$</c>,忽略大小写)。
    /// <para>
    /// 包名来自服务端 index.json,会被拼成 <c>launcherDownloadConfig/{name}.json</c> 用于写入与删除;
    /// 含 <c>..\..\</c> 的名字可以写/删游戏目录外的 .json。真实包名只有 common/hd/sd/uhd 这类短词。
    /// </para>
    /// </summary>
    internal static bool IsSafePackName(string? packName)
    {
        if (string.IsNullOrEmpty(packName) || packName.Length > 64)
        {
            return false;
        }
        foreach (var c in packName)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '_' && c != '-')
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// 官方已安装标记:&lt;游戏目录&gt;/launcherDownloadConfig/{packName}.json。
    /// 包名不合法(见 <see cref="IsSafePackName"/>)时返回 null,调用方一律按"没有标记/不写"处理。
    /// </summary>
    internal static string? MarkerPath(string gameRoot, string packName) =>
        IsSafePackName(packName)
            ? Path.Combine(gameRoot, OfficialLauncherRecordService.PackVersionDirName, packName.ToLowerInvariant() + ".json")
            : null;

    /// <summary>
    /// 标记文件内容(与官方启动器同格式:<c>{"packName":"hd","version":"3.7.0"}</c>)。
    /// 用源生成序列化而不是字符串插值:版本号带引号/反斜杠时插值会生成非法 JSON。
    /// </summary>
    internal static string MarkerJson(string packName, string? version) =>
        System.Text.Json.JsonSerializer.Serialize(
            new OfficialPackVersionConfig { PackName = packName.ToLowerInvariant(), Version = version ?? "" },
            OfficialLauncherJsonContext.Default.OfficialPackVersionConfig);

    /// <summary>
    /// 读取官方标记里记录的包版本(文件不存在/损坏/无 version 字段返回 null)。
    /// <para>
    /// 标记是官方启动器的文件,内容形如 <c>{"packName":"hd","version":"3.7.0"}</c>。
    /// 这里用 <see cref="System.Text.Json.JsonDocument"/> 只取 version,不引入序列化契约 ——
    /// 官方随时可能加字段,按需读数比反序列化成固定类型更耐改。
    /// </para>
    /// </summary>
    internal static string? ReadInstalledMarkerVersion(string gameRoot, string packName)
    {
        try
        {
            var path = MarkerPath(gameRoot, packName);
            if (path is null || !File.Exists(path))
            {
                return null;
            }

            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.TryGetProperty("version", out var version)
                && version.ValueKind == System.Text.Json.JsonValueKind.String
                ? version.GetString()
                : null;
        }
        catch (Exception)
        {
            // 标记损坏/被占用:当作"未知版本",由调用方按需重新下载(保守方向)。
            return null;
        }
    }

    /// <summary>
    /// 标记是否表明该包<b>已安装且版本与期望一致</b>。
    /// <para>
    /// <b>这里为什么要比版本,而不是只判文件存在:</b>
    /// 早先只判 <c>File.Exists</c>,后果是服务端<b>只提升材质包版本、主版本号不变</b>时
    /// (鸣潮 3.7.0 → 3.7.1 即为典型:四个包 version 全变 3.7.1,而检查更新的主版本比较根本看不出差异),
    /// 该包会被永久判定为"已装好",此后永远不再更新。
    /// </para>
    /// <para>
    /// 标记缺失版本字段(旧版 McKuro 或第三方工具写的)时按"版本未知"处理 → 需重新校核,
    /// 方向是保守的(宁可多下一次,也不把旧包当成新包)。
    /// </para>
    /// </summary>
    /// <param name="installedGameVersion">
    /// 本地已安装的游戏<b>主版本</b>;用于抑制"老用户被重复提醒"的误报,见
    /// <see cref="IsPackInstalled(string, string, string?, string?)"/>。为 null 时不做该抑制。
    /// </param>
    internal static bool IsPackInstalledAtVersion(
        string gameRoot,
        string packName,
        string? expectedVersion,
        string? installedGameVersion = null)
    {
        var recorded = ReadInstalledMarkerVersion(gameRoot, packName);
        if (recorded is null)
        {
            return false;
        }

        // 服务端没给版本(清单异常)时,只认"标记存在且有版本",不谎报落后。
        if (string.IsNullOrWhiteSpace(expectedVersion))
        {
            return true;
        }

        if (string.Equals(recorded.Trim(), expectedVersion.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // 走到这里 = 标记版本落后于服务端。但对"主版本已经追平"的老用户来说,
        // 这几乎总是<b>标记陈旧</b>而不是文件陈旧 —— 主版本是用手头这份清单升上来的,
        // 材质包随之一起更新过,只是当时没回写标记。详见 IsSupersededByInstalledGameVersion。
        return IsSupersededByInstalledGameVersion(expectedVersion, installedGameVersion);
    }

    /// <summary>
    /// 「老用户主版本已追上、但材质包标记停在旧版本」的判定。
    /// <para>
    /// <b>要解决的误报:</b>McKuro 在本次改动之前从不回写材质包标记(只有「切换画质」才写),
    /// 所以用主流程一路更新上来的老用户,标记会停在他们上次切画质的版本。
    /// 引入版本比对后,这些用户会看到"材质包需要重新下载"——而实际几十 GB 文件早就是新的。
    /// </para>
    /// <para>
    /// <b>判据:本地已安装主版本 ≥ 材质包提供的版本。</b>
    /// 依据是厂商的发布方式:材质包 version 始终与生产版本号同步(实测 3.7.1 的
    /// common/hd/sd/uhd 四个包 version 全是 3.7.1)。因此"主版本已经 3.7.1 及以上"
    /// 就说明本地是用 ≥3.7.1 的清单装出来的,其中当然包含 3.7.1 的材质包。
    /// </para>
    /// <para>
    /// <b>为什么这个放宽不会退化成"永不更新材质包":</b>
    /// <list type="number">
    /// <item>它需要本地主版本<b>真的追平</b>才生效;主版本只要还落后,仍按"标记落后 → 需更新"处理
    ///       (见 <c>Plan_Still_Reports_Stale_Marker_When_Main_Version_Behind</c>);</item>
    /// <item>标记<b>缺失</b>时根本走不到这里(前面已返回 false),真没装的包照常下载;</item>
    /// <item>"只提升材质包版本、主版本不变"时,服务端包版本会<b>高于</b>本地主版本,
    ///       条件不成立 → 正确要求更新(这正是上游要修的那个 bug)。</item>
    /// </list>
    /// </para>
    /// </summary>
    private static bool IsSupersededByInstalledGameVersion(
        string expectedVersion,
        string? installedGameVersion)
    {
        if (string.IsNullOrWhiteSpace(installedGameVersion))
        {
            return false;
        }

        // 本地主版本必须 >= 材质包版本,才认为材质包是被那次安装一并带上的。
        if (ComparePackVersions(installedGameVersion, expectedVersion) < 0)
        {
            return false;
        }

        // 版本必须可解析,否则无法判断"是标记陈旧"还是"文件真的没装",保守起见要求重装。
        return TryParseVersion(installedGameVersion, out _) && TryParseVersion(expectedVersion, out _);
    }

    /// <summary>
    /// 版本比较(数值逐段,缺失段按 0 补齐)。任一侧解析失败时回退字符串比较。
    /// <para>
    /// 与 <c>GameUpdater.CompareVersionStrings</c> 同语义,但这里只需要符号,
    /// 且必须能解析"3.7.1"这类三段版本 —— <see cref="Version"/> 会把缺失段当 -1,
    /// 直接比较 "3.7.1" 与 "3.7.1.0" 会得出错误结论,所以自己按段比较。
    /// </para>
    /// </summary>
    internal static int ComparePackVersions(string? left, string? right)
    {
        if (!TryParseVersion(left, out var l) || !TryParseVersion(right, out var r))
        {
            return string.Compare(
                (left ?? "").Trim(),
                (right ?? "").Trim(),
                StringComparison.OrdinalIgnoreCase);
        }

        var max = Math.Max(l.Length, r.Length);
        for (var i = 0; i < max; i++)
        {
            var lv = i < l.Length ? l[i] : 0;
            var rv = i < r.Length ? r[i] : 0;
            if (lv != rv)
            {
                return lv < rv ? -1 : 1;
            }
        }
        return 0;
    }

    /// <summary>把 "3.7.1" 解析成数值段数组;含非数字段时失败。</summary>
    internal static bool TryParseVersion(string? text, out int[] segments)
    {
        segments = [];
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Trim().Split('.');
        if (parts.Length is < 1 or > 4)
        {
            return false;
        }

        var result = new int[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], out var value) || value < 0)
            {
                return false;
            }
            result[i] = value;
        }

        segments = result;
        return true;
    }

    /// <summary>
    /// 实例版判定:在 <see cref="IsPackInstalledAtVersion"/> 基础上带上本地主版本抑制。
    /// </summary>
    private bool IsPackInstalled(string gameRoot, string packName, string? expectedVersion) =>
        IsPackInstalledAtVersion(
            gameRoot,
            packName,
            expectedVersion,
            _installedVersionProvider?.Invoke());

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
            .Where(c => GameManifestLoader.IsHttpsUrl(c.Url)) // 只取 HTTPS(L5):http 条目会被 LoadResourcePackAsync 拒绝
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

            // 版本一致才算已装好:服务端只提升材质包版本时,标记里的旧版本会让这里判成"落后"。
            if (IsPackInstalled(root, packName, pack.Version))
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
                Message = CoreStrings.T("Launcher.LevelUnsupported", "当前渠道暂不支持资源等级切换"),
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

                // 只要有文件没装成功就不能写标记。
                // 旧行为是"部分失败也照写":标记一旦落盘,该包就被永久判为已安装,
                // 而版本比对又只看标记,于是残缺包再也无法自愈(只能删客户端重下)。
                // 这里直接失败返回,让用户重试;已下的文件由 LocalFileMatches 复用,不会重下。
                if (failures.Count > 0)
                {
                    return new ResourceLevelResult
                    {
                        Success = false,
                        InstalledFiles = installed,
                        DownloadedBytes = downloaded,
                        Message = CoreStrings.F(
                            "Launcher.LevelInstallPartial",
                            $"画质包 {packName} 有 {failures.Count} 个文件未安装成功,已放弃标记该包(可重试,已下载的文件会被复用): {failures[0]}",
                            packName,
                            failures.Count,
                            failures[0]),
                    };
                }

                // 安装完成即写官方标记文件,与官方启动器的「已安装」判据保持一致。
                // 版本取自<b>清单声明的包版本</b>而非文件名:下一次 PlanAsync 靠它判断是否需要更新。
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
            var path = MarkerPath(gameRoot, packName);
            if (path is null)
            {
                _logger.LogWarning("资源包名不合法,跳过写标记: {Pack}", packName);
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, MarkerJson(packName, version));
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

    /// <summary>
    /// 已安装的包名集合(依据官方 launcherDownloadConfig 标记)。
    /// <para>
    /// 判据含<b>版本一致</b>(见 <see cref="IsPackInstalledAtVersion"/>):这里用于"能删哪些包"的
    /// 安全判定,把版本落后的包算作未安装是保守方向 —— 不会让用户误以为某个包仍在正常提供资源。
    /// </para>
    /// </summary>
    private HashSet<string> GetInstalledPackNames(string gameRoot, KuroResourcePackIndex index)
    {
        var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (index.ResourcePacks is null)
        {
            return installed;
        }

        foreach (var (packName, pack) in index.ResourcePacks)
        {
            if (IsPackInstalled(gameRoot, packName, pack.Version))
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
            .Where(c => GameManifestLoader.IsHttpsUrl(c.Url)) // 只取 HTTPS(L5):http 条目会被 LoadResourcePackAsync 拒绝
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
            var path = MarkerPath(gameRoot, packName);
            if (path is not null && File.Exists(path))
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
