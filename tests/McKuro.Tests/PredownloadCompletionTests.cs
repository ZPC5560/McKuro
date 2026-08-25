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
/// </summary>
public class PredownloadCompletionTests : IDisposable
{
    private readonly string _tmpDir;
    private readonly AppDatabase _db;

    public PredownloadCompletionTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "McKuro_pd_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmpDir);
        _db = new AppDatabase(_tmpDir);
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
        Directory.CreateDirectory(Path.Combine(_tmpDir, "predownload", "2.1.0"));

        Assert.Null(FindStaging(updater, "2.1.0", GameServerType.Official, "2.0.0"));
    }

    private GameUpdater CreateUpdater()
    {
        var loader = new GameManifestLoader(new HttpClient());
        var downloader = new DownloadEngine(new HttpClient());
        var installer = new UpdateInstaller();
        var paths = new GamePathResolver(() => @"D:\games\wuthering");
        return new GameUpdater(
            loader, downloader, installer, paths, _tmpDir,
            _db, logger: NullLogger<GameUpdater>.Instance);
    }

    private void WriteMeta(string version, string? sourceVersion, GameServerType serverType, bool completed)
    {
        var staging = Path.Combine(_tmpDir, "predownload", version);
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
