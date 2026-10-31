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

    /// <summary>
    /// 本地 DLSS/XeSS 图形组件版本(对齐 Haiyu GetLocalDLSSAsync)。
    /// 同步读盘(已改为探测已知位置 + 剪枝遍历 + 缓存),UI 线程请用 <see cref="GetLocalGraphicsComponentVersionsAsync"/>。
    /// </summary>
    IReadOnlyList<LocalFileVersion> GetLocalGraphicsComponentVersions();

    /// <summary>在线程池上执行 <see cref="GetLocalGraphicsComponentVersions"/>(供 UI 线程调用,不阻塞界面)。</summary>
    Task<IReadOnlyList<LocalFileVersion>> GetLocalGraphicsComponentVersionsAsync(CancellationToken ct = default);

    /// <summary>
    /// 读取本地已安装的游戏主版本(未设置目录/无记录时返回 null)。
    /// <para>
    /// 只读、不触网、不做校验。供材质包版本判定使用:主版本已追平服务端时,
    /// 说明材质包是被那次安装一并带上的,不应因为标记陈旧而重复下载几十 GB。
    /// </para>
    /// </summary>
    string? GetInstalledVersion();

    /// <summary>
    /// 该游戏目录是否存在「安装未完成」标记(下载/安装进行中被打断)。
    /// <para>
    /// 判据与 <see cref="GamePathResolver.IsGameInstalled"/> 互补:后者只看根 exe 是否存在,
    /// 而下载途中根 exe 可能已经落盘 —— 此时"看起来装好了"其实客户端 exe 还没下完,
    /// 重启后按钮会错误地变成「启动游戏」,既无法继续下载、点启动也必然失败。
    /// 本标记在安装开始前写入、成功写已装版本后清除,是「是否真装完」的权威依据。
    /// </para>
    /// </summary>
    bool HasIncompleteInstall();

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

    /// <summary>
    /// 安装完成后的体积自检:扫描游戏目录里<b>启动器自身留下的残留/重复文件</b>
    /// (替换前备份、孤儿 .part、补丁与差分临时目录),并与服务端清单总量比对,
    /// 供 UI 判断是否需要弹窗询问删除。
    /// <para>
    /// 有意<b>不做</b>"清单外即垃圾"的清扫:其它画质资源包、存档、日志本来就不在本次清单里。
    /// 详见 <see cref="ResidueScanner"/> 的安全边界说明。
    /// </para>
    /// <para>后台线程执行,不抛异常(扫描失败返回空报告),不阻断安装结果。</para>
    /// </summary>
    /// <param name="ct">取消令牌。</param>
    /// <param name="progress">
    /// 扫描进度(可选)。整盘统计可能要遍历数万文件,耗时数秒到数十秒,
    /// UI 应据此显示"正在检查…"而不是让按钮点下去毫无反应。
    /// </param>
    Task<ResidueScanResult> ScanResidueAsync(
        CancellationToken ct = default,
        IProgress<ResidueScanProgress>? progress = null);

    /// <summary>删除 <see cref="ScanResidueAsync"/> 报告的残留,返回 (回收字节, 失败条数)。</summary>
    (long FreedBytes, int Failures) DeleteResidue(ResidueScanResult result);
}
