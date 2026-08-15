using donet.Core.Models.Game;
using donet.Core.Services.Game;

namespace donet.Tests;

public class UpdateInstallerTests : IDisposable
{
    private readonly string _gameDir;
    private readonly string _stagingDir;

    public UpdateInstallerTests()
    {
        _gameDir = Path.Combine(Path.GetTempPath(), "donet_game_" + Guid.NewGuid().ToString("N"));
        _stagingDir = Path.Combine(Path.GetTempPath(), "donet_stage_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_gameDir);
        Directory.CreateDirectory(_stagingDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_gameDir, recursive: true);
            Directory.Delete(_stagingDir, recursive: true);
        }
        catch (Exception)
        {
            // 忽略
        }
    }

    private static GameFileEntry Entry(string path, string content)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        var md5 = Convert.ToHexStringLower(System.Security.Cryptography.MD5.HashData(bytes));
        return new GameFileEntry { Path = path, Size = bytes.Length, Md5 = md5 };
    }

    [Fact]
    public void ComputeDiff_MissingFiles_Listed()
    {
        var manifest = new GameManifest
        {
            Version = "1.0",
            Files = [Entry("Client/Data/a.bin", "hello"), Entry("Client/Data/b.bin", "world")],
        };

        // 只放一个文件
        Directory.CreateDirectory(Path.Combine(_gameDir, "Client", "Data"));
        File.WriteAllText(Path.Combine(_gameDir, "Client", "Data", "a.bin"), "hello");

        var installer = new UpdateInstaller();
        var diff = installer.ComputeDiff(manifest, _gameDir);

        Assert.Single(diff.ToDownload);
        Assert.Equal("Client/Data/b.bin", diff.ToDownload[0].Path);
    }

    [Fact]
    public void ComputeDiff_CorruptedFile_Listed()
    {
        var manifest = new GameManifest
        {
            Version = "1.0",
            Files = [Entry("Client/Data/a.bin", "hello")],
        };

        Directory.CreateDirectory(Path.Combine(_gameDir, "Client", "Data"));
        File.WriteAllText(Path.Combine(_gameDir, "Client", "Data", "a.bin"), "corrupted!!");

        var installer = new UpdateInstaller();
        var diff = installer.ComputeDiff(manifest, _gameDir);
        Assert.Single(diff.ToDownload);
    }

    [Fact]
    public void InstallFromStaging_MovesFilesAndVerifies()
    {
        var manifest = new GameManifest
        {
            Version = "1.0",
            Files = [Entry("Client/Data/a.bin", "hello"), Entry("Client/Data/b.bin", "world")],
        };

        // 暂存目录放两个文件
        foreach (var f in manifest.Files)
        {
            var staged = Path.Combine(_stagingDir, f.Path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
            File.WriteAllBytes(staged, System.Text.Encoding.UTF8.GetBytes(f.Path.EndsWith("a.bin") ? "hello" : "world"));
        }

        var installer = new UpdateInstaller();
        var (installed, failures) = installer.InstallFromStaging(_stagingDir, _gameDir, manifest);

        Assert.Empty(failures);
        Assert.Equal(2, installed);
        Assert.True(File.Exists(Path.Combine(_gameDir, "Client", "Data", "a.bin")));
        Assert.Equal("hello", File.ReadAllText(Path.Combine(_gameDir, "Client", "Data", "a.bin")));
    }

    [Fact]
    public void InstallFromStaging_BadHash_NotInstalled()
    {
        var manifest = new GameManifest
        {
            Version = "1.0",
            Files = [Entry("Client/Data/a.bin", "hello")],
        };

        // 暂存文件内容与清单不符
        var staged = Path.Combine(_stagingDir, "Client", "Data", "a.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
        File.WriteAllText(staged, "WRONG CONTENT");

        var installer = new UpdateInstaller();
        var (installed, failures) = installer.InstallFromStaging(_stagingDir, _gameDir, manifest);

        Assert.Equal(0, installed);
        Assert.NotEmpty(failures);
        Assert.False(File.Exists(Path.Combine(_gameDir, "Client", "Data", "a.bin")));
    }
}
