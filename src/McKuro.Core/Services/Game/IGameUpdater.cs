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

    bool LaunchGame(out string? error);

    /// <summary>本地 DLSS/XeSS 图形组件版本(对齐 Haiyu GetLocalDLSSAsync)。</summary>
    IReadOnlyList<LocalFileVersion> GetLocalGraphicsComponentVersions();
}
