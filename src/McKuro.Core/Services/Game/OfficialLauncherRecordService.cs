using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using McKuro.Core.Models.Game;
using McKuro.Core.Services.Settings;
using Microsoft.Extensions.Logging;

namespace McKuro.Core.Services.Game;

/// <summary>
/// 把「官方启动器(KRLauncher)格式」的安装记录写回游戏目录,让用户**用 McKuro 装好的游戏
/// 也能被官方启动器识别为已安装**,避免官方「定位游戏目录」时判未安装而触发整包重下。
/// <para>
/// <b>问题背景:</b>McKuro 原来只写各分包标记 <c>launcherDownloadConfig/{pack}.json</c>
/// (见 <see cref="ResourceLevelService"/> 与 <see cref="GameUpdater"/> 的标记刷新)。
/// 但官方启动器判「已安装」读的是**顶层** <c>launcherDownloadConfig.json</c> 里的
/// <c>bundles</c> 字典;只有分包标记时那份顶层记录不存在,官方就认为这个目录没装过。
/// 症状是:文件明明全在,用户切回官方启动器却看到"安装"按钮,点击后从头下 80+GB。
/// </para>
/// <para>
/// <b>写入范围(三个文件):</b>
/// <list type="number">
///   <item><c>launcherDownloadConfig.json</c> —— 顶层安装状态(<c>appId</c> + <c>bundles</c>)。</item>
///   <item><c>launcherDownloadConfig/{pack}.json</c> —— 各分包版本(已存在则幂等跳过)。</item>
///   <item><c>LocalGameResources.json</c> —— 全量文件台账(官方 best-effort 读,缺失不报错)。</item>
/// </list>
/// </para>
/// <para>
/// <b>两阶段提交:</b>前两个文件先全部写成 <c>*.tmp</c>,任何一个生成失败就整体放弃(游戏目录零改动);
/// 全部生成成功后才逐个 <c>File.Move(overwrite)</c>,替换阶段任一失败则把已生效的文件回滚到旧内容。
/// 这样不会出现"顶层记录写了、分包版本没写"这种半套状态 —— 那比完全不写更糟(官方会按半套记录去算增量)。
/// </para>
/// <para>
/// <b>版本取本地实际值而非服务器最新值:</b>若把服务器版本写进记录,而本地文件其实是旧的,
/// 用户切到官方启动器后会被判"已最新",更新永远不触发。宁可留空让官方走修复流程。
/// </para>
/// </summary>
public sealed class OfficialLauncherRecordService
{
    /// <summary>官方顶层安装配置文件名(位于游戏目录根)。</summary>
    public const string InstalledConfigFileName = "launcherDownloadConfig.json";

    /// <summary>官方分包版本目录名(与顶层文件名同前缀但不同物:一个是文件、一个是目录)。</summary>
    public const string PackVersionDirName = "launcherDownloadConfig";

    /// <summary>官方本地资源清单文件名。</summary>
    public const string LocalIndexFileName = "LocalGameResources.json";

    private readonly GamePathResolver _paths;
    private readonly GameManifestLoader _loader;
    private readonly ISettingsService? _settings;
    private readonly ILogger<OfficialLauncherRecordService> _logger;

    public OfficialLauncherRecordService(
        GamePathResolver paths,
        GameManifestLoader loader,
        ISettingsService? settings = null,
        ILogger<OfficialLauncherRecordService>? logger = null)
    {
        _paths = paths;
        _loader = loader;
        _settings = settings;
        _logger = logger ?? NullLogger<OfficialLauncherRecordService>.Instance;
    }

    // ---------- 定位 ----------

    /// <summary>
    /// 检查当前设置里的游戏目录是否具备写入条件(目录存在、非空、有游戏主程序)。
    /// 任一不满足都返回带原因的状态,调用方据此给用户可读提示而不是抛异常。
    /// </summary>
    public OfficialRecordLocateResult LocateGameFolder(string? gameRootDir = null)
    {
        var root = gameRootDir ?? _paths.GameRootDir;
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return new OfficialRecordLocateResult
            {
                State = OfficialRecordLocateState.GameFolderMissing,
                Message = "未设置游戏目录,或目录不存在",
            };
        }

        // 枚举失败(ACL 受限等)按"非空"继续:后面还有主程序检查兜底
        try
        {
            if (!Directory.EnumerateFileSystemEntries(root).Any())
            {
                return new OfficialRecordLocateResult
                {
                    State = OfficialRecordLocateState.GameFolderEmpty,
                    GameFolder = root,
                    Message = "游戏目录是空的,请确认选对了目录",
                };
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            _logger.LogWarning(ex, "游戏目录枚举失败,按非空继续: {Dir}", root);
        }

        // 官方判定也依赖主程序存在性;缺失时写记录没有意义
        if (!File.Exists(Path.Combine(root, _paths.GameExeName)))
        {
            return new OfficialRecordLocateResult
            {
                State = OfficialRecordLocateState.GameExeMissing,
                GameFolder = root,
                Message = $"目录里没有 {_paths.GameExeName},请确认这是游戏安装目录",
            };
        }

        var existing = ReadInstalledConfig(root);
        return new OfficialRecordLocateResult
        {
            State = OfficialRecordLocateState.Ready,
            GameFolder = root,
            HasExistingRecord = existing is not null,
            ExistingBundles = existing?.Bundles.Keys.ToList() ?? [],
        };
    }

    // ---------- 组装 ----------

    /// <summary>
    /// 按当前渠道的分包索引组装安装记录。
    /// <para>
    /// 主组合 = 用户当前资源等级(设置里的 ResourceLevel);「附加组合」= 目录里其它**完整安装**的
    /// 画质组合(官方 <c>bundles</c> 字典天然支持多节点并存,HD 与 UHD 可以同时登记)。
    /// </para>
    /// <para>
    /// 返回 null 表示无法组装(未设置目录 / 渠道无分包协议 / 索引拉取失败 / 一个组合都没装),
    /// 调用方给可读提示即可,不要当成异常。
    /// </para>
    /// </summary>
    public async Task<OfficialInstallRecord?> BuildInstallRecordAsync(
        GameServerType serverType,
        CancellationToken ct = default)
    {
        var root = _paths.GameRootDir;
        var indexUrl = KuroEndpoints.ResourcePackIndexForServerType(serverType);
        if (string.IsNullOrEmpty(root) || indexUrl is null)
        {
            // WeGame 等渠道没有该协议端点,官方启动器也不是 KRLauncher —— 这里没有可写的东西。
            return null;
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
            _logger.LogWarning(ex, "拉取分包索引失败,无法组装官方安装记录");
            return null;
        }

        if (index?.ResourcePacks is null || index.Bundles is null)
        {
            return null;
        }

        // 已安装的包 = <b>标记文件存在</b>(即文件确实在盘上),而不是"标记版本与服务端一致"。
        // 这里与 ResourceLevelService 的删除/规划口径<b>故意不同</b>:
        // 那份记录的用途是「告诉官方启动器磁盘上有什么」,所以"装过但还没更新到最新"的包
        // 同样要登记(版本写它本地的旧值)。若按"版本必须一致"判,还没更新的用户会被判成
        // 「一个组合都没装」→ 写不出记录 → 正好落回"官方启动器判未安装"的坑里。
        var installedPacks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var packName in index.ResourcePacks.Keys)
        {
            if (ResourceLevelService.ReadInstalledMarkerVersion(root, packName) is not null)
            {
                installedPacks.Add(packName);
            }
        }

        // 主组合:设置里的等级;没设置就挑第一个"完整安装"的组合,避免只写附加节点
        var primaryBundle = ResourceLevelPlanner.LevelToBundleName(_settings?.Current.ResourceLevel);
        if (!index.Bundles.ContainsKey(primaryBundle)
            || !ResourceLevelPlanner.IsBundleInstalled(index, primaryBundle, installedPacks))
        {
            var fallback = index.Bundles.Keys.FirstOrDefault(b =>
                ResourceLevelPlanner.IsBundleInstalled(index, b, installedPacks));
            if (fallback is null)
            {
                _logger.LogInformation("目录里没有任何完整安装的画质组合,跳过官方记录写入");
                return null;
            }
            primaryBundle = fallback;
        }

        var additional = new List<OfficialBundleRecord>();
        foreach (var (bundleName, bundle) in index.Bundles)
        {
            if (string.Equals(bundleName, primaryBundle, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (bundle.ResourcePacks is not { Count: > 0 }
                || !ResourceLevelPlanner.IsBundleInstalled(index, bundleName, installedPacks))
            {
                continue;
            }
            additional.Add(new OfficialBundleRecord(bundleName, BuildPackVersions(root, bundle)));
        }

        var appId = ResolveAppId(serverType);
        if (string.IsNullOrEmpty(appId))
        {
            _logger.LogWarning("未知渠道 {Server},无法确定官方 appId", serverType);
            return null;
        }

        return new OfficialInstallRecord(appId, primaryBundle, BuildPackVersions(root, index.Bundles[primaryBundle]))
        {
            AdditionalBundles = additional,
        };
    }

    /// <summary>
    /// 该画质组合的逐分包版本,取<b>本地标记里的实际版本</b>(不是服务器版本)。
    /// 标记缺失的包给空串,写入时跳过 —— 诚实留白交给官方修复流程,胜过伪造"最新"。
    /// </summary>
    private static IReadOnlyList<OfficialPackVersion> BuildPackVersions(string root, KuroResourceBundle bundle) =>
        (bundle.ResourcePacks ?? [])
            .Select(name => new OfficialPackVersion(
                name,
                ResourceLevelService.ReadInstalledMarkerVersion(root, name) ?? ""))
            .ToList();

    /// <summary>各渠道的官方 appId(= resId)。国际服 / B站 / 官服分别是 50004 / 10004 / 10003。</summary>
    private static string? ResolveAppId(GameServerType serverType) => serverType switch
    {
        GameServerType.Official => "10003",
        GameServerType.Bilibili => "10004",
        GameServerType.Global => "50004",
        GameServerType.Unknown => "10003", // 未识别渠道:按官服假设,与端点选择的保守方向一致
        _ => null,
    };

    // ---------- 写入 ----------

    /// <summary>
    /// 写入顶层安装配置 + 各分包版本(两阶段提交,见类注释)。
    /// </summary>
    public async Task<OfficialRecordWriteResult> WriteInstallRecordAsync(
        string gameFolder,
        OfficialInstallRecord record,
        CancellationToken ct = default)
    {
        var staged = new List<(string TempPath, string TargetPath, string RelativePath)>();
        var failed = new List<string>();

        try
        {
            await StageInstalledConfigAsync(gameFolder, record, staged, failed, ct).ConfigureAwait(false);
            await StagePackVersionsAsync(gameFolder, record, staged, failed, ct).ConfigureAwait(false);
            foreach (var additional in record.AdditionalBundles)
            {
                await StagePackVersionsAsync(
                    gameFolder,
                    new OfficialInstallRecord(record.AppId, additional.BundleName, additional.Packs),
                    staged,
                    failed,
                    ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            CleanupStaged(staged);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "官方安装记录生成异常");
            failed.Add(InstalledConfigFileName);
            CleanupStaged(staged);
            return new OfficialRecordWriteResult { FailedFiles = Distinct(failed) };
        }

        if (failed.Count > 0)
        {
            // 有任何一项没生成出来就整体放弃:半套记录比不写更糟
            CleanupStaged(staged);
            _logger.LogWarning("官方安装记录生成不完整,本次写入全部不生效: {Files}", string.Join(", ", Distinct(failed)));
            return new OfficialRecordWriteResult { FailedFiles = Distinct(failed) };
        }

        return await CommitStagedAsync(staged, failed, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 替换阶段:逐个 Move 并记录旧内容用于回滚。任一失败即把所有已生效文件恢复原状。
    /// </summary>
    private async Task<OfficialRecordWriteResult> CommitStagedAsync(
        List<(string TempPath, string TargetPath, string RelativePath)> staged,
        List<string> failed,
        CancellationToken ct)
    {
        var written = new List<string>();
        var backups = new List<(string TargetPath, string? OldContent, bool OldExists)>();

        try
        {
            foreach (var (tempPath, targetPath, relativePath) in staged)
            {
                if (string.IsNullOrEmpty(tempPath))
                {
                    // 幂等项:内容与目标一致,没生成临时文件,直接计成功
                    written.Add(relativePath);
                    continue;
                }

                var oldExists = File.Exists(targetPath);
                backups.Add((targetPath, oldExists ? await File.ReadAllTextAsync(targetPath, ct).ConfigureAwait(false) : null, oldExists));
                try
                {
                    File.Move(tempPath, targetPath, true);
                    written.Add(relativePath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "官方记录替换失败: {Path}", targetPath);
                    failed.Add(relativePath);
                    throw; // 进入回滚
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            foreach (var (targetPath, oldContent, oldExists) in backups)
            {
                try
                {
                    if (oldExists)
                    {
                        await File.WriteAllTextAsync(targetPath, oldContent ?? "", ct).ConfigureAwait(false);
                    }
                    else if (File.Exists(targetPath))
                    {
                        File.Delete(targetPath);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "官方记录回滚失败: {Path}", targetPath);
                }
            }

            CleanupStaged([.. staged.Where(s => !written.Contains(s.RelativePath, StringComparer.OrdinalIgnoreCase))]);
            _logger.LogWarning("官方记录替换阶段失败,已回滚全部已生效文件,游戏目录保持旧状态");
            return new OfficialRecordWriteResult { FailedFiles = Distinct(failed) };
        }

        return new OfficialRecordWriteResult
        {
            WrittenFiles = Distinct(written),
            FailedFiles = Distinct(failed),
        };
    }

    /// <summary>
    /// 生成顶层 <c>launcherDownloadConfig.json</c> 的临时文件。
    /// <b>合并语义:</b>保留文件里已有的其它 bundle 节点与 appId,只覆盖本次这个组合 ——
    /// 否则用户从其它启动器带过来的画质记录会被这里抹掉。
    /// </summary>
    private async Task StageInstalledConfigAsync(
        string gameFolder,
        OfficialInstallRecord record,
        List<(string TempPath, string TargetPath, string RelativePath)> staged,
        List<string> failed,
        CancellationToken ct)
    {
        var path = Path.Combine(gameFolder, InstalledConfigFileName);
        var config = ReadInstalledConfig(gameFolder) ?? new OfficialInstalledConfig();

        config.AppId = record.AppId;

        var primaryVersion = record.Packs.Count > 0 ? record.Packs[0].Version : "";
        config.Bundles[record.BundleName] = new OfficialBundleInstalledInfo
        {
            Version = primaryVersion,
            State = "",
            ResourcePacks = [.. record.Packs.Select(p => p.Name)],
        };

        foreach (var additional in record.AdditionalBundles)
        {
            config.Bundles[additional.BundleName] = new OfficialBundleInstalledInfo
            {
                Version = additional.Packs.Count > 0 ? additional.Packs[0].Version : "",
                State = "",
                ResourcePacks = [.. additional.Packs.Select(p => p.Name)],
            };
        }

        var content = JsonSerializer.Serialize(config, OfficialLauncherJsonContext.Default.OfficialInstalledConfig);
        await StageFileAsync(path, content, InstalledConfigFileName, staged, failed, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 生成各分包版本文件的临时文件。内容已与目标一致的幂等项不生成临时文件(直接计成功)。
    /// 版本为空的分包跳过:写空版本等于告诉官方"版本未知",不如不写。
    /// </summary>
    private async Task StagePackVersionsAsync(
        string gameFolder,
        OfficialInstallRecord record,
        List<(string TempPath, string TargetPath, string RelativePath)> staged,
        List<string> failed,
        CancellationToken ct)
    {
        var dir = Path.Combine(gameFolder, PackVersionDirName);
        foreach (var pack in record.Packs)
        {
            if (string.IsNullOrWhiteSpace(pack.Name) || string.IsNullOrWhiteSpace(pack.Version))
            {
                continue;
            }

            var path = Path.Combine(dir, pack.Name + ".json");
            var relativePath = $"{PackVersionDirName}/{pack.Name}.json";
            var content = JsonSerializer.Serialize(
                new OfficialPackVersionConfig { PackName = pack.Name, Version = pack.Version },
                OfficialLauncherJsonContext.Default.OfficialPackVersionConfig);

            if (File.Exists(path)
                && (await File.ReadAllTextAsync(path, ct).ConfigureAwait(false)).Trim() == content.Trim())
            {
                staged.Add((string.Empty, path, relativePath));
                continue;
            }

            await StageFileAsync(path, content, relativePath, staged, failed, ct).ConfigureAwait(false);
        }
    }

    private async Task StageFileAsync(
        string path,
        string content,
        string relativePath,
        List<(string TempPath, string TargetPath, string RelativePath)> staged,
        List<string> failed,
        CancellationToken ct)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }
            var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
            await File.WriteAllTextAsync(tempPath, content, ct).ConfigureAwait(false);
            staged.Add((tempPath, path, relativePath));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "官方记录生成失败: {Path}", path);
            failed.Add(relativePath);
        }
    }

    private void CleanupStaged(List<(string TempPath, string TargetPath, string RelativePath)> staged)
    {
        foreach (var (tempPath, _, _) in staged)
        {
            try
            {
                if (!string.IsNullOrEmpty(tempPath) && File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "官方记录临时文件清理失败: {Path}", tempPath);
            }
        }
    }

    // ---------- 资源清单 ----------

    /// <summary>
    /// 写 <c>LocalGameResources.json</c>:把主组合 + 全部附加组合的分包清单合并成一份文件台账。
    /// <para>
    /// 合并规则:各组合的分包各自拉 indexFile,按 <c>dest</c> 去重(common 被所有组合共享,
    /// 内容天然一致自动合一;各画质独有分包互相补充),得到目录实际承载的全部文件。
    /// </para>
    /// <para>
    /// 失败<b>不影响</b>已写入的安装记录:官方对这份清单的所有读取点都是 best-effort,
    /// 缺它只是少省一次校验,不会触发重下。
    /// </para>
    /// </summary>
    public async Task<OfficialRecordWriteResult> WriteResourceIndexAsync(
        string gameFolder,
        OfficialInstallRecord record,
        CancellationToken ct = default)
    {
        var written = new List<string>();
        var failed = new List<string>();
        try
        {
            var files = await CollectManifestFilesAsync(record, ct).ConfigureAwait(false);
            if (files.Count == 0)
            {
                _logger.LogWarning("官方资源清单为空,跳过 {File} 写入", LocalIndexFileName);
                return new OfficialRecordWriteResult { FailedFiles = [LocalIndexFileName] };
            }

            var indexRecord = new OfficialGameResourceRecord
            {
                Resource = [.. files
                    .Where(f => !string.IsNullOrWhiteSpace(f.Path))
                    .GroupBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .Select(f => new OfficialResourceFileInfo
                    {
                        Dest = f.Path.Replace('\\', '/'),
                        Size = f.Size,
                        Md5 = f.Md5 ?? "",
                        FromFolder = null,
                        ChunkInfos = f.ChunkInfos.Count == 0
                            ? null
                            : [.. f.ChunkInfos.Select(c => new OfficialResourceChunkInfo
                            {
                                Start = c.Start,
                                End = c.End,
                                Md5 = c.Md5,
                            })],
                    })],
            };

            var path = Path.Combine(gameFolder, LocalIndexFileName);
            var content = JsonSerializer.Serialize(indexRecord, OfficialLauncherJsonContext.Default.OfficialGameResourceRecord);
            if (await TryWriteAtomicAsync(path, content, ct).ConfigureAwait(false))
            {
                written.Add(LocalIndexFileName);
            }
            else
            {
                failed.Add(LocalIndexFileName);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "官方资源清单写入失败(不影响安装记录)");
            failed.Add(LocalIndexFileName);
        }

        return new OfficialRecordWriteResult { WrittenFiles = written, FailedFiles = failed };
    }

    /// <summary>
    /// 拉取记录里各画质组合的全部分包清单并按路径合并。
    /// 复用 <see cref="GameManifestLoader.LoadResourcePackAsync"/>(与切换画质走同一条解析路径,
    /// 避免两份 dest/md5/chunkInfos 解析实现漂移)。
    /// </summary>
    private async Task<List<GameFileEntry>> CollectManifestFilesAsync(
        OfficialInstallRecord record,
        CancellationToken ct)
    {
        var root = _paths.GameRootDir;
        var serverType = _settings?.Current.ServerType ?? GameServerType.Official;
        var indexUrl = KuroEndpoints.ResourcePackIndexForServerType(serverType);
        if (string.IsNullOrEmpty(root) || indexUrl is null)
        {
            return [];
        }

        var index = await _loader.LoadResourcePackIndexAsync(indexUrl, ct).ConfigureAwait(false);
        if (index?.ResourcePacks is null || index.Bundles is null)
        {
            return [];
        }

        var cdn = index.CdnList?
            .Where(c => !string.IsNullOrWhiteSpace(c.Url))
            .OrderBy(c => c.P == 0 ? int.MaxValue : c.P)
            .FirstOrDefault()?.Url;
        if (string.IsNullOrWhiteSpace(cdn))
        {
            return [];
        }

        var bundleNames = new[] { record.BundleName }
            .Concat(record.AdditionalBundles.Select(b => b.BundleName))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var files = new List<GameFileEntry>();
        foreach (var bundleName in bundleNames)
        {
            ct.ThrowIfCancellationRequested();
            foreach (var packName in ResourceLevelPlanner.ResolvePackNames(index, bundleName))
            {
                if (!index.ResourcePacks.TryGetValue(packName, out var pack))
                {
                    continue;
                }
                var load = await _loader.LoadResourcePackAsync(pack, cdn, ct: ct).ConfigureAwait(false);
                if (load is { Success: true, Manifest: not null })
                {
                    files.AddRange(load.Manifest.Files);
                }
                else
                {
                    _logger.LogWarning("拉取分包 {Pack} 清单失败,该包文件未计入官方资源清单: {Msg}", packName, load.Message);
                }
            }
        }

        return files;
    }

    private static async Task<bool> TryWriteAtomicAsync(string path, string content, CancellationToken ct)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }
            var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
            try
            {
                await File.WriteAllTextAsync(tempPath, content, ct).ConfigureAwait(false);
                File.Move(tempPath, path, true);
                return true;
            }
            finally
            {
                try
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch
                {
                    // 临时文件清理失败不影响结果
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // ---------- 移除 ----------

    /// <summary>
    /// 移除本工具写入的三个记录。
    /// <para>
    /// 分包目录<b>只删 .json</b>,目录本身与其中的非记录内容一律保留 ——
    /// 那里可能还有官方自己写的东西,不能整目录删。
    /// </para>
    /// </summary>
    public OfficialRecordWriteResult RemoveRecords(string gameFolder)
    {
        var removed = new List<string>();
        var failed = new List<string>();

        TryDelete(Path.Combine(gameFolder, InstalledConfigFileName), removed, failed);
        TryDeleteDirectory(Path.Combine(gameFolder, PackVersionDirName), removed, failed);
        TryDelete(Path.Combine(gameFolder, LocalIndexFileName), removed, failed);

        return new OfficialRecordWriteResult { RemovedEntries = removed, FailedFiles = failed };
    }

    /// <summary>目录中是否存在任意官方记录(安装配置或资源清单)。</summary>
    public bool HasAnyRecord(string gameFolder) =>
        File.Exists(Path.Combine(gameFolder, InstalledConfigFileName))
        || File.Exists(Path.Combine(gameFolder, LocalIndexFileName));

    private void TryDeleteDirectory(string path, List<string> removed, List<string> failed)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                return;
            }
            foreach (var file in Directory.GetFiles(path, "*.json"))
            {
                var relative = $"{PackVersionDirName}/{Path.GetFileName(file)}";
                try
                {
                    File.Delete(file);
                    removed.Add(relative);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "官方记录删除失败: {Path}", file);
                    failed.Add(relative);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "官方记录目录访问失败: {Path}", path);
            failed.Add($"{PackVersionDirName}/");
        }
    }

    private void TryDelete(string path, List<string> removed, List<string> failed)
    {
        try
        {
            if (!File.Exists(path))
            {
                return;
            }
            File.Delete(path);
            removed.Add(Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "官方记录删除失败: {Path}", path);
            failed.Add(Path.GetFileName(path));
        }
    }

    // ---------- 读取 ----------

    /// <summary>读取现有顶层记录;文件不存在/损坏/字段异常一律返回 null(按"没有记录"处理)。</summary>
    public OfficialInstalledConfig? ReadInstalledConfig(string gameFolder) =>
        Deserialize<OfficialInstalledConfig>(
            Path.Combine(gameFolder, InstalledConfigFileName),
            OfficialLauncherJsonContext.Default.OfficialInstalledConfig);

    private static T? Deserialize<T>(string path, JsonTypeInfo<T> typeInfo)
    {
        try
        {
            if (!File.Exists(path))
            {
                return default;
            }
            return JsonSerializer.Deserialize(File.ReadAllText(path), typeInfo);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return default;
        }
    }

    private static List<string> Distinct(List<string> items) =>
        [.. items.Distinct(StringComparer.OrdinalIgnoreCase)];
}
