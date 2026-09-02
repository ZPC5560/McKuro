using System.Text.Json;
using System.Text.Json.Serialization;

namespace McKuro.Core.Services.Wallpaper;

/// <summary>一条可用作动态壁纸的视频条目(来自 Wallpaper Engine 包或本地选择)。</summary>
public sealed record WallpaperVideoEntry(
    string Title,
    string VideoPath,
    string? CoverPath,
    string FolderPath);

/// <summary>
/// Wallpaper Engine 壁纸目录扫描器:识别视频类壁纸(project.json type=video),供启动页自定义动态壁纸使用。
/// 目录形态(实测 431960 工坊包):
/// - 单壁纸目录:自身含 project.json(title/file/preview/type 字段,type 大小写混杂 video/Video)
/// - 父目录:workshop\content\431960 或 projects 根,子目录各为一个壁纸包(只扫一层)
/// scene/web 类型由 WE 私有引擎渲染,无视频文件可复用,跳过。
/// 封面回退链:preview 字段 → preview.jpg → preview0.jpg → preview.gif → 目录内首个图片。
/// </summary>
public static class WallpaperEngineScanner
{
    private static readonly string[] VideoExtensions = [".mp4", ".webm", ".mkv", ".avi", ".mov", ".wmv"];
    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".webp", ".gif"];

    /// <summary>扫描目录(自身或一级子目录)下的全部视频壁纸;解析失败/无视频的包静默跳过。</summary>
    public static IReadOnlyList<WallpaperVideoEntry> Scan(string root)
    {
        var result = new List<WallpaperVideoEntry>();
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return result;
        }

        // 自身是壁纸包 → 只解析它;否则把每个含 project.json 的一级子目录当壁纸包
        IEnumerable<string> candidates;
        if (File.Exists(Path.Combine(root, "project.json")))
        {
            candidates = [root];
        }
        else
        {
            try
            {
                candidates = Directory.EnumerateDirectories(root)
                    .Where(d => File.Exists(Path.Combine(d, "project.json")));
            }
            catch (Exception)
            {
                return result;
            }
        }

        foreach (var dir in candidates)
        {
            var entry = TryParseWallpaper(dir);
            if (entry is not null)
            {
                result.Add(entry);
            }
        }

        return result
            .OrderBy(e => e.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static WallpaperVideoEntry? TryParseWallpaper(string dir)
    {
        try
        {
            var json = File.ReadAllText(Path.Combine(dir, "project.json"));
            var project = JsonSerializer.Deserialize(json, WallpaperJsonContext.Default.WallpaperProject);
            if (project is null)
            {
                return null;
            }

            // 仅视频类壁纸可复用(scene/web 无视频文件);type 大小写混杂,忽略大小写
            if (!string.Equals(project.Type?.Trim(), "video", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var videoPath = ResolveVideoPath(dir, project.File);
            if (videoPath is null)
            {
                return null;
            }

            var coverPath = FindCoverIn(dir, project.Preview);
            var title = string.IsNullOrWhiteSpace(project.Title)
                ? Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar))
                : project.Title.Trim();

            return new WallpaperVideoEntry(title, videoPath, coverPath, dir);
        }
        catch (Exception)
        {
            return null; // 单个包损坏不影响整体扫描
        }
    }

    private static string? ResolveVideoPath(string dir, string? fileField)
    {
        if (!string.IsNullOrWhiteSpace(fileField))
        {
            var p = Path.GetFullPath(Path.Combine(dir, fileField.Trim()));
            // 防越界:file 字段含 .. 指向包外时忽略,回退到目录内探测
            if (p.StartsWith(dir.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase) && File.Exists(p))
            {
                return p;
            }
        }
        try
        {
            return Directory.EnumerateFiles(dir)
                .FirstOrDefault(f => VideoExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>按回退链解析目录内的封面图;找不到返回 null。previewField 为 project.json 的 preview 字段(可空)。</summary>
    public static string? FindCoverIn(string dir, string? previewField = null)
    {
        if (!string.IsNullOrWhiteSpace(previewField))
        {
            var p = Path.GetFullPath(Path.Combine(dir, previewField.Trim()));
            if (File.Exists(p))
            {
                return p;
            }
        }
        // 回退链:常见封面文件名 → 目录内首个图片(实测 5/20 包无 preview.jpg,仅有 preview.gif)
        foreach (var name in new[] { "preview.jpg", "preview0.jpg", "preview.gif", "preview.webp", "preview.png" })
        {
            var p = Path.Combine(dir, name);
            if (File.Exists(p))
            {
                return p;
            }
        }
        try
        {
            return Directory.EnumerateFiles(dir)
                .FirstOrDefault(f => ImageExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception)
        {
            return null;
        }
    }
}

/// <summary>project.json 模型(仅需字段;WE 写出的键为小写)。</summary>
public sealed class WallpaperProject
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("file")]
    public string? File { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("preview")]
    public string? Preview { get; set; }
}

[JsonSerializable(typeof(WallpaperProject))]
public sealed partial class WallpaperJsonContext : JsonSerializerContext;
