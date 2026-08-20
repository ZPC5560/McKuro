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
