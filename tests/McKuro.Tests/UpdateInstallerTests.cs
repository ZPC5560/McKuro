using McKuro.Core.Models.Game;
using McKuro.Core.Services.Game;

namespace McKuro.Tests;

public class UpdateInstallerTests : IDisposable
{
    private readonly string _gameDir;
    private readonly string _stagingDir;

    public UpdateInstallerTests()
    {
        _gameDir = Path.Combine(Path.GetTempPath(), "McKuro_game_" + Guid.NewGuid().ToString("N"));
        _stagingDir = Path.Combine(Path.GetTempPath(), "McKuro_stage_" + Guid.NewGuid().ToString("N"));
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
    public void ComputeDiff_SkipPaths_Excluded()
    {
        var manifest = new GameManifest
        {
            Version = "1.0",
            Files = [Entry("Client/Data/a.bin", "hello"), Entry("Client/Saved/Logs/Client.log", "log")],
        };

        // 两个文件都缺失;跳过列表命中 Client.log,则它不应进入下载列表
        var skip = new HashSet<string>(["Client/Saved/Logs/Client.log"], StringComparer.OrdinalIgnoreCase);

        var installer = new UpdateInstaller();
        var diff = installer.ComputeDiff(manifest, _gameDir, skip);

        Assert.Single(diff.ToDownload);
        Assert.Equal("Client/Data/a.bin", diff.ToDownload[0].Path);
    }

    [Fact]
    public void ComputeDiff_SkipPaths_CaseInsensitive()
    {
        var manifest = new GameManifest
        {
            Version = "1.0",
            Files = [Entry("Client/Saved/Logs/Client.log", "log")],
        };

        // 用户配置路径大小写不同,仍应命中跳过
        var skip = new HashSet<string>(["client/saved/logs/client.log"], StringComparer.OrdinalIgnoreCase);

        var installer = new UpdateInstaller();
        var diff = installer.ComputeDiff(manifest, _gameDir, skip);

        Assert.Empty(diff.ToDownload);
    }

    [Fact]
    public void ComputeDiff_RejectsPathTraversal()
    {
        var manifest = new GameManifest
        {
            Version = "1.0",
            Files = [Entry("../outside.bin", "hello")],
        };

        Assert.Throws<InvalidDataException>(() =>
            new UpdateInstaller().ComputeDiff(manifest, _gameDir));
    }

    [Fact]
    public void ComputeDiff_Cancelled_Throws()
    {
        var manifest = new GameManifest
        {
            Version = "1.0",
            Files = [Entry("Client/Data/a.bin", "hello")],
        };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            new UpdateInstaller().ComputeDiff(manifest, _gameDir, ct: cts.Token));
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

    /// <summary>M3:备份是同卷移动(不再整文件复制),原路径随即腾空、内容进入 .McKuro_backup。</summary>
    [Fact]
    public void BackupFile_MovesOriginal_IntoBackupDir()
    {
        var original = Path.Combine(_gameDir, "Client", "Data", "a.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(original)!);
        File.WriteAllText(original, "old");

        UpdateInstaller.BackupFile(original, _gameDir);

        Assert.False(File.Exists(original));
        var backup = Path.Combine(_gameDir, UpdateInstaller.BackupDirName, "Client", "Data", "a.bin");
        Assert.Equal("old", File.ReadAllText(backup));

        // 同一路径再备份一次:覆盖旧备份而不是抛异常
        File.WriteAllText(original, "older");
        UpdateInstaller.BackupFile(original, _gameDir);
        Assert.Equal("older", File.ReadAllText(backup));
    }

    /// <summary>L6:工作区跳过规则按目录边界判断,DiffDataX/ 这类同前缀目录不能被当成 DiffData/。</summary>
    [Fact]
    public void RemoveObsolete_UsesDirectoryBoundary_ForWorkspaceDirs()
    {
        var manifest = new GameManifest { Version = "1.0", Files = [Entry("keep.bin", "k")] };
        void Write(string relative)
        {
            var full = Path.Combine(_gameDir, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, "x");
        }
        Write("keep.bin");
        Write("DiffData/3.7.0/patch.krdiff");
        Write("DiffDataX/stray.bin");
        Write(UpdateInstaller.BackupDirName + "/old.bin");

        var removed = new UpdateInstaller().RemoveObsolete(manifest, _gameDir);

        Assert.Equal(1, removed);
        Assert.True(File.Exists(Path.Combine(_gameDir, "keep.bin")));
        Assert.True(File.Exists(Path.Combine(_gameDir, "DiffData", "3.7.0", "patch.krdiff")));
        Assert.True(File.Exists(Path.Combine(_gameDir, UpdateInstaller.BackupDirName, "old.bin")));
        Assert.False(File.Exists(Path.Combine(_gameDir, "DiffDataX", "stray.bin")));
    }
}
