using Avalonia.Media.Imaging;
using McKuro.Core.Services.Settings;
using Microsoft.Extensions.Logging;

namespace McKuro.Services;

/// <summary>
/// 管理壁纸的跨平台本地副本。用户原图被复制到应用数据目录，原文件移动或删除后仍能继续显示。
/// </summary>
public sealed class WallpaperService
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp",
    };

    private readonly string _wallpaperDirectory;
    private readonly ISettingsService _settings;
    private readonly ILogger<WallpaperService> _logger;

    public WallpaperService(
        string appDataDir,
        ISettingsService settings,
        ILogger<WallpaperService>? logger = null)
    {
        _wallpaperDirectory = Path.Combine(appDataDir, "wallpapers");
        _settings = settings;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<WallpaperService>.Instance;
        Directory.CreateDirectory(_wallpaperDirectory);

        var configured = _settings.Current.WallpaperPath;
        CurrentWallpaperPath = IsManagedFile(configured) && File.Exists(configured) ? configured : "";
        if (!string.Equals(configured, CurrentWallpaperPath, StringComparison.Ordinal))
        {
            _settings.Current.WallpaperPath = CurrentWallpaperPath;
        }
    }

    public string CurrentWallpaperPath { get; private set; }

    public bool HasWallpaper => !string.IsNullOrEmpty(CurrentWallpaperPath) && File.Exists(CurrentWallpaperPath);

    public event EventHandler? WallpaperChanged;

    /// <summary>复制并验证用户壁纸，返回应用数据目录中的新路径。</summary>
    public async Task<string> SetWallpaperAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new ArgumentException("壁纸路径不能为空", nameof(sourcePath));
        }

        sourcePath = Path.GetFullPath(sourcePath);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("找不到壁纸文件", sourcePath);
        }

        var extension = Path.GetExtension(sourcePath);
        if (!SupportedExtensions.Contains(extension))
        {
            throw new InvalidDataException("仅支持 PNG、JPG、JPEG 和 WebP 壁纸");
        }

        var fileInfo = new FileInfo(sourcePath);
        if (fileInfo.Length <= 0 || fileInfo.Length > 32L * 1024 * 1024)
        {
            throw new InvalidDataException("壁纸文件必须大于 0 且不超过 32 MB");
        }

        await Task.Run(() => ValidateBitmap(sourcePath), cancellationToken).ConfigureAwait(false);

        Directory.CreateDirectory(_wallpaperDirectory);
        var targetPath = Path.Combine(_wallpaperDirectory, $"wallpaper-{Guid.NewGuid():N}{extension.ToLowerInvariant()}");
        var tempPath = targetPath + ".tmp";
        try
        {
            await using (var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            await using (var target = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
            }

            File.Move(tempPath, targetPath);
            var previousPath = CurrentWallpaperPath;
            CurrentWallpaperPath = targetPath;
            _settings.Current.WallpaperPath = targetPath;
            _settings.Save();
            DeleteManagedFile(previousPath);
            WallpaperChanged?.Invoke(this, EventArgs.Empty);
            return targetPath;
        }
        catch
        {
            TryDelete(tempPath);
            TryDelete(targetPath);
            throw;
        }
    }

    /// <summary>恢复为应用默认背景，并清理由本服务托管的壁纸副本。</summary>
    public void ClearWallpaper()
    {
        var previousPath = CurrentWallpaperPath;
        CurrentWallpaperPath = "";
        _settings.Current.WallpaperPath = "";
        _settings.Save();
        DeleteManagedFile(previousPath);
        WallpaperChanged?.Invoke(this, EventArgs.Empty);
    }

    private static void ValidateBitmap(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var bitmap = new Bitmap(stream);
        var size = bitmap.PixelSize;
        if (size.Width < 320 || size.Height < 180)
        {
            throw new InvalidDataException("壁纸分辨率过低，至少需要 320×180");
        }
    }

    private bool IsManagedFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            var fullPath = Path.GetFullPath(path);
            var root = Path.GetFullPath(_wallpaperDirectory) + Path.DirectorySeparatorChar;
            return fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private void DeleteManagedFile(string? path)
    {
        if (!IsManagedFile(path) || string.Equals(path, CurrentWallpaperPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        TryDelete(path);
    }

    private void TryDelete(string? path)
    {
        if (!IsManagedFile(path) || string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "清理旧壁纸副本失败: {Path}", path);
        }
    }
}
