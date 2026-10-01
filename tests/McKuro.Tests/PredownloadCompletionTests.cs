using System.Text.Json;
using McKuro.Core.Infrastructure;
using McKuro.Core.Services.Game;
using McKuro.Core.Services.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace McKuro.Tests;

/// <summary>
/// 预下载完成判定(对齐上游 1.6 修复:预下载完成后按钮禁用并显示「预下载完成」):
/// predownload.json 标记 Completed 且版本/来源版本/渠道匹配时 FindStaging 命中,
/// CheckUpdateAsync 据此把 HasPredownload 短路为 false 并置 PredownloadCompleted。
///
/// 预载暂存目录位于**游戏目录**下的 DiffData/&lt;版本&gt;(不占系统盘,安装时同卷改名);
/// 旧版位于数据目录 predownload/&lt;版本&gt;,由 [过渡代码] 迁移函数搬到新位置,
/// 迁移尚未跑过时 FindStaging 仍能从旧位置命中。
/// </summary>
public class PredownloadCompletionTests : IDisposable
{
    private readonly string _tmpDir;
    private readonly string _appDataDir;
    private readonly string _gameRoot;
    private readonly AppDatabase _db;

    public PredownloadCompletionTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "McKuro_pd_" + Guid.NewGuid().ToString("N"));
        _appDataDir = Path.Combine(_tmpDir, "appdata");
        _gameRoot = Path.Combine(_tmpDir, "game");
        Directory.CreateDirectory(_appDataDir);
        Directory.CreateDirectory(_gameRoot);
        _db = new AppDatabase(_appDataDir);
    }

    public void Dispose()
    {
        _db.Dispose();
        try
        {
            Directory.Delete(_tmpDir, recursive: true);
        }
        catch (Exception)
        {
            // 忽略
        }
    }

    [Fact]
    public void Completed_Meta_Matching_Version_Source_Server_Hits()
    {
        var updater = CreateUpdater();
        WriteMeta(version: "2.1.0", sourceVersion: "2.0.0", serverType: GameServerType.Official, completed: true);

        var staging = FindStaging(updater, version: "2.1.0", serverType: GameServerType.Official, sourceVersion: "2.0.0");
        Assert.NotNull(staging);
        // 落盘位置必须是游戏目录下的 DiffData,而不是数据目录
        Assert.StartsWith(Path.Combine(_gameRoot, GamePathResolver.DiffDataDirName), staging!);
    }

    [Fact]
    public void Incomplete_Meta_Does_Not_Hit()
    {
        var updater = CreateUpdater();
        WriteMeta("2.1.0", "2.0.0", GameServerType.Official, completed: false);

        Assert.Null(FindStaging(updater, "2.1.0", GameServerType.Official, "2.0.0"));
    }

    [Fact]
    public void Source_Version_Mismatch_Does_Not_Hit()
    {
        // 本地版本已变化(如已装上新版本):旧预载包不得再被消费
        var updater = CreateUpdater();
        WriteMeta("2.1.0", "2.0.0", GameServerType.Official, completed: true);

        Assert.Null(FindStaging(updater, "2.1.0", GameServerType.Official, "2.0.5"));
    }

    [Fact]
    public void Server_Type_Mismatch_Does_Not_Hit()
    {
        var updater = CreateUpdater();
        WriteMeta("2.1.0", "2.0.0", GameServerType.Bilibili, completed: true);

        Assert.Null(FindStaging(updater, "2.1.0", GameServerType.Official, "2.0.0"));
    }

    [Fact]
    public void Missing_Marker_File_Does_Not_Hit()
    {
        var updater = CreateUpdater();
        Directory.CreateDirectory(StageDir("2.1.0"));

        Assert.Null(FindStaging(updater, "2.1.0", GameServerType.Official, "2.0.0"));
    }

    /// <summary>[过渡代码] 迁移尚未跑过时,旧位置(数据目录 predownload)的预载包仍应被消费。</summary>
    [Fact]
    public void Legacy_Location_Still_Hits_Before_Migration()
    {
        var updater = CreateUpdater();
        WriteLegacyMeta("2.1.0", "2.0.0", GameServerType.Official, completed: true);

        var staging = FindStaging(updater, "2.1.0", GameServerType.Official, "2.0.0");
        Assert.NotNull(staging);
        Assert.StartsWith(LegacyStageDir("2.1.0"), staging!);
    }

    /// <summary>[过渡代码] 迁移把旧位置的预载包搬到游戏目录 DiffData,并清掉旧目录。</summary>
    [Fact]
    public async Task Migration_Moves_Legacy_Predownload_Into_Game_DiffData()
    {
        var updater = CreateUpdater();
        WriteLegacyMeta("2.1.0", "2.0.0", GameServerType.Official, completed: true);
        // 附一个数据文件,确认按相对路径一并搬运
        var payload = Path.Combine(LegacyStageDir("2.1.0"), "Client", "Content", "pakchunk0.pak");
        Directory.CreateDirectory(Path.GetDirectoryName(payload)!);
        File.WriteAllText(payload, "payload");

        await updater.MigrateLegacyPredownloadAsync(_gameRoot);

        var moved = Path.Combine(StageDir("2.1.0"), "Client", "Content", "pakchunk0.pak");
        Assert.True(File.Exists(moved), "数据文件应被搬到 DiffData 下");
        Assert.Equal("payload", File.ReadAllText(moved));
        Assert.True(File.Exists(Path.Combine(StageDir("2.1.0"), "predownload.json")), "标记文件应在迁移后一并就位");
        Assert.False(Directory.Exists(LegacyStageDir("2.1.0")), "迁移完成后应清掉旧版本目录");
        // 迁移后仍能从新位置命中
        Assert.NotNull(FindStaging(updater, "2.1.0", GameServerType.Official, "2.0.0"));
    }

    /// <summary>[过渡代码] 没有旧数据时迁移是空操作,不创建 DiffData。</summary>
    [Fact]
    public async Task Migration_Without_Legacy_Data_Is_Noop()
    {
        var updater = CreateUpdater();

        await updater.MigrateLegacyPredownloadAsync(_gameRoot);

        Assert.False(Directory.Exists(Path.Combine(_gameRoot, GamePathResolver.DiffDataDirName)));
    }

    private GameUpdater CreateUpdater()
    {
        var loader = new GameManifestLoader(new HttpClient());
        var downloader = new DownloadEngine(new HttpClient());
        var installer = new UpdateInstaller();
        var paths = new GamePathResolver(() => _gameRoot);
        return new GameUpdater(
            loader, downloader, installer, paths, _appDataDir,
            _db, logger: NullLogger<GameUpdater>.Instance);
    }

    /// <summary>预载暂存目录(游戏目录下 DiffData/&lt;版本&gt;)。</summary>
    private string StageDir(string version) =>
        Path.Combine(_gameRoot, GamePathResolver.DiffDataDirName, version);

    /// <summary>旧版预载暂存目录(数据目录下 predownload/&lt;版本&gt;)。</summary>
    private string LegacyStageDir(string version) =>
        Path.Combine(_appDataDir, "predownload", version);

    private void WriteMeta(string version, string? sourceVersion, GameServerType serverType, bool completed) =>
        WriteMetaTo(StageDir(version), version, sourceVersion, serverType, completed);

    private void WriteLegacyMeta(string version, string? sourceVersion, GameServerType serverType, bool completed) =>
        WriteMetaTo(LegacyStageDir(version), version, sourceVersion, serverType, completed);

    private static void WriteMetaTo(
        string staging,
        string version,
        string? sourceVersion,
        GameServerType serverType,
        bool completed)
    {
        Directory.CreateDirectory(staging);
        var meta = new PreDownloadMeta
        {
            Version = version,
            SourceVersion = sourceVersion,
            ServerType = serverType,
            Completed = completed,
            PatchIndexUrl = "https://cdn.example/patch/indexFile.json",
            DownloadBaseUrl = "https://cdn.example/patch",
        };
        File.WriteAllText(
            Path.Combine(staging, "predownload.json"),
            JsonSerializer.Serialize(meta, GameMetaJsonContext.Default.PreDownloadMeta));
    }

    private static string? FindStaging(GameUpdater updater, string version, GameServerType serverType, string? sourceVersion)
    {
        var method = typeof(GameUpdater).GetMethod("FindStaging",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        return method.Invoke(updater, [version, serverType, sourceVersion]) as string;
    }
}
