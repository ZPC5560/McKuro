using McKuro.Core.Services.Wallpaper;

namespace McKuro.Tests;

/// <summary>Wallpaper Engine 壁纸目录扫描测试(合成目录结构,对齐实测 431960 工坊包形态)。</summary>
public class WallpaperEngineScannerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mckuro-we-test-" + Guid.NewGuid().ToString("N"));

    private string MakeWallpaper(string id, string projectJson, params string[] files)
    {
        var dir = Path.Combine(_root, id);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "project.json"), projectJson);
        foreach (var f in files)
        {
            File.WriteAllText(Path.Combine(dir, f), "x");
        }
        return dir;
    }

    [Fact]
    public void Scan_Finds_Video_Wallpapers_Skips_Scene_And_Web()
    {
        MakeWallpaper("100", """{"type":"video","file":"a.mp4","title":"视频A","preview":"preview.jpg"}""",
            "a.mp4", "preview.jpg");
        MakeWallpaper("101", """{"type":"scene","file":"scene.json","title":"场景"}""", "scene.json");
        MakeWallpaper("102", """{"type":"web","file":"index.html","title":"网页"}""", "index.html");

        var entries = WallpaperEngineScanner.Scan(_root);

        Assert.Single(entries);
        Assert.Equal("视频A", entries[0].Title);
        Assert.EndsWith("a.mp4", entries[0].VideoPath);
        Assert.EndsWith("preview.jpg", entries[0].CoverPath);
    }

    [Fact]
    public void Scan_Type_Is_Case_Insensitive_And_Title_Falls_Back_To_Folder()
    {
        // 实测工坊包 type 大小写混杂(video/Video);title 缺失时回退目录名
        MakeWallpaper("200", """{"type":"Video","file":"b.webm"}""", "b.webm");

        var entries = WallpaperEngineScanner.Scan(_root);

        Assert.Single(entries);
        Assert.Equal("200", entries[0].Title);
        Assert.Null(entries[0].CoverPath); // 无任何图片时封面为 null
    }

    [Fact]
    public void Scan_Cover_Fallback_Chain_Preview_Field_Missing_Uses_Gif()
    {
        // 实测形态:preview 字段指向 preview.gif(无 preview.jpg)
        MakeWallpaper("300", """{"type":"video","file":"c.mp4","preview":"preview.gif"}""",
            "c.mp4", "preview.gif");

        var entries = WallpaperEngineScanner.Scan(_root);

        Assert.Single(entries);
        Assert.EndsWith("preview.gif", entries[0].CoverPath);
    }

    [Fact]
    public void Scan_Missing_File_Field_Probes_Video_In_Folder()
    {
        MakeWallpaper("400", """{"type":"video","title":"无file字段"}""", "clip.mp4");

        var entries = WallpaperEngineScanner.Scan(_root);

        Assert.Single(entries);
        Assert.EndsWith("clip.mp4", entries[0].VideoPath);
    }

    [Fact]
    public void Scan_File_Field_Escapes_Directory_Is_Ignored_Then_Probes()
    {
        // 防越界:file 指向包外路径时不采用,回退目录内探测;目录无视频则跳过该包
        MakeWallpaper("500", """{"type":"video","file":"..\\..\\outside.mp4"}""");

        var entries = WallpaperEngineScanner.Scan(_root);

        Assert.Empty(entries);
    }

    [Fact]
    public void Scan_Single_Wallpaper_Dir_As_Root_Works()
    {
        // 直接选中单个壁纸包目录(而非工坊父目录)
        var dir = MakeWallpaper("600", """{"type":"video","file":"d.mp4","title":"单包"}""", "d.mp4");

        var entries = WallpaperEngineScanner.Scan(dir);

        Assert.Single(entries);
        Assert.Equal("单包", entries[0].Title);
    }

    [Fact]
    public void Scan_Corrupt_Json_Silently_Skipped()
    {
        MakeWallpaper("700", "{ 这不是合法 json ");
        MakeWallpaper("701", """{"type":"video","file":"e.mp4","title":"好的"}""", "e.mp4");

        var entries = WallpaperEngineScanner.Scan(_root);

        Assert.Single(entries);
        Assert.Equal("好的", entries[0].Title);
    }

    [Fact]
    public void Scan_Nonexistent_Root_Returns_Empty()
    {
        Assert.Empty(WallpaperEngineScanner.Scan(Path.Combine(_root, "no-such-dir")));
        Assert.Empty(WallpaperEngineScanner.Scan(""));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // 临时目录清理失败可忽略
        }
    }
}
