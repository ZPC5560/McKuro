using System.IO.Compression;
using McKuro.Core.Models.Game;
using McKuro.Core.Services.Game;

namespace McKuro.Tests;

public sealed class PatchInstallerTests : IDisposable
{
    private readonly string _staging = Path.Combine(Path.GetTempPath(), "McKuro_patch_stage_" + Guid.NewGuid().ToString("N"));
    private readonly string _game = Path.Combine(Path.GetTempPath(), "McKuro_patch_game_" + Guid.NewGuid().ToString("N"));

    public PatchInstallerTests()
    {
        Directory.CreateDirectory(_staging);
        Directory.CreateDirectory(_game);
    }

    public void Dispose()
    {
        TryDelete(_staging);
        TryDelete(_game);
    }

    [Fact]
    public async Task InstallAsync_RejectsZipPathTraversal()
    {
        var package = new GameFileEntry { Path = "update.krzip", Size = 0, Md5 = "" };
        var plan = new GamePatchPlan();
        plan.ZipPackages.Add(new GamePatchPackage { Package = package });
        var archivePath = Path.Combine(_staging, package.Path);
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            using var stream = archive.CreateEntry("../outside.txt").Open();
            await stream.WriteAsync("unsafe"u8.ToArray());
        }

        var result = await new PatchInstaller(new UpdateInstaller()).InstallAsync(
            plan,
            _staging,
            _game,
            [package]);

        Assert.False(result.Success);
        Assert.Contains("越界", result.Message);
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(_game)!, "outside.txt")));
        // 解压已开始(可能已写入部分条目):如实报告"动过游戏目录"
        Assert.True(result.GameFolderModified);
    }

    /// <summary>
    /// M2:补丁包缺失这类"还没碰游戏目录"的失败必须报告 GameFolderModified = false,
    /// 调用方据此保留预载包,而不是删掉后回退到需要更多空间的完整下载。
    /// </summary>
    [Fact]
    public async Task InstallAsync_FailureBeforeTouchingGame_ReportsNotModified()
    {
        var package = new GameFileEntry { Path = "missing.krzip", Size = 0, Md5 = "" };
        var plan = new GamePatchPlan();
        plan.ZipPackages.Add(new GamePatchPackage { Package = package });
        var callbacks = 0;

        var result = await new PatchInstaller(new UpdateInstaller()).InstallAsync(
            plan,
            _staging,
            _game,
            [package],
            onGameFolderModified: () => callbacks++);

        Assert.False(result.Success);
        Assert.False(result.GameFolderModified);
        Assert.Equal(0, callbacks);
    }

    /// <summary>普通资源已移入游戏目录:成功结果同样带 GameFolderModified,回调只触发一次。</summary>
    [Fact]
    public async Task InstallAsync_OrdinaryFiles_ReportsModified_AndCallbackOnce()
    {
        var bytes = "hello"u8.ToArray();
        var entry = new GameFileEntry
        {
            Path = "Client/a.bin",
            Size = bytes.Length,
            Md5 = Convert.ToHexStringLower(System.Security.Cryptography.MD5.HashData(bytes)),
        };
        Directory.CreateDirectory(Path.Combine(_staging, "Client"));
        await File.WriteAllBytesAsync(Path.Combine(_staging, "Client", "a.bin"), bytes);
        var callbacks = 0;

        var result = await new PatchInstaller(new UpdateInstaller()).InstallAsync(
            new GamePatchPlan(),
            _staging,
            _game,
            [entry],
            onGameFolderModified: () => callbacks++);

        Assert.True(result.Success, result.Message);
        Assert.True(result.GameFolderModified);
        Assert.Equal(1, callbacks);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(_game, "Client", "a.bin")));
    }

    private static void TryDelete(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Ignore test cleanup failures.
        }
    }
}
