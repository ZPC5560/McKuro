namespace McKuro.Core.Services.Game;

/// <summary>游戏更新器接口。</summary>
public interface IGameUpdater
{
    Task<UpdateCheckResult> CheckUpdateAsync(GameServerType serverType, CancellationToken ct = default);

    Task<(bool Success, string? StagingDir, string? Message)> PreDownloadAsync(
        GameServerType serverType,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default);

    Task<(bool Success, string? Message)> InstallAsync(
        GameServerType serverType,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default);

    /// <summary>
    /// 修复游戏:对比清单重新下载并安装缺失/损坏的文件。
    /// <paramref name="skipPaths"/> 中的相对路径将被跳过校验(用户配置的"跳过校验文件")。
    /// </summary>
    Task<(bool Success, string? Message)> RepairGameAsync(
        GameServerType serverType,
        IReadOnlySet<string>? skipPaths = null,
        bool deleteSkipped = false,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default);

    /// <summary>获取预下载清单的下载体积与所需磁盘空间(供 UI 显示下载/磁盘预估,参考 Haiyu Config.Size/UnCompressSize)。</summary>
    Task<(long DownloadBytes, long DiskBytes)> GetPredownloadEstimateAsync(GameServerType serverType, CancellationToken ct = default);

    bool LaunchGame(out string? error);

    /// <summary>
    /// 解析实际用于启动的游戏 exe 完整路径(用户指定 → 根 exe → 客户端 exe,找不到返回 null)。
    /// 供进程监控按进程名探测游戏运行状态。
    /// </summary>
    string? ResolveLaunchExePath();

    /// <summary>本地 DLSS/XeSS 图形组件版本(对齐 Haiyu GetLocalDLSSAsync)。</summary>
    IReadOnlyList<LocalFileVersion> GetLocalGraphicsComponentVersions();

    /// <summary>
    /// 手动指定本地已装版本(场景:用官方启动器等外部渠道更新后,本地版本记录滞后)。
    /// 只写版本记录、不触碰任何游戏文件(即「跳过校验」);格式非法或未设置游戏目录返回 false。
    /// </summary>
    bool TrySetInstalledVersion(string version);

    /// <summary>
    /// 「指定本地已装版本」下拉的候选:服务端当前版本 + 补丁清单里的历史版本 + 本地记录(去重,新→旧排序)。
    /// <para>
    /// 有意<b>不含</b>预载(predownload)版本 —— 那是尚未安装的未来版本,而调用方会把它写进
    /// <see cref="TrySetInstalledVersion"/>;写下去会让"检查更新"误判为已是最新。
    /// </para>
    /// <para>
    /// 网络失败时静默降级为只返回本地记录。<b>注意</b>:取消时会抛
    /// <see cref="OperationCanceledException"/>(调用方传了 token 时),不吞掉。
    /// </para>
    /// </summary>
    Task<IReadOnlyList<string>> GetKnownVersionsAsync(GameServerType serverType, CancellationToken ct = default);
}
