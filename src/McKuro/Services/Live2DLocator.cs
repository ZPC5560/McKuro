using System.Runtime.InteropServices;

namespace McKuro.Services;

/// <summary>
/// Live2D 运行环境定位:
/// - 平台支持:Sparkle.Live2DView(OpenGL 渲染 Cubism)当前仅面向 Windows x64;
/// - Cubism Core(Live2DCubismCore.dll)有独立许可,不随仓库/安装包分发,
///   按以下顺序探测:① 程序目录(安装包未来内置时自动生效);② 用户数据目录
///   live2d\(设置页"打开目录"引导放置)。命中用户目录时通过 AddDllDirectory
///   加入 DLL 搜索路径(P/Invoke 按名称解析 Live2DCubismCore)。
/// </summary>
public static class Live2DLocator
{
    private const string CoreDll = "Live2DCubismCore.dll";

    /// <summary>当前平台是否支持 Live2D 渲染。</summary>
    public static bool IsSupported => OperatingSystem.IsWindows()
        && System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.X64;

    /// <summary>用户放置 Cubism Core 与模型的目录(%AppData%/McKuro/live2d)。</summary>
    public static string UserDir => Path.Combine(McKuro.Services.AppServices.AppDataDir, "live2d");

    /// <summary>Cubism Core 是否可加载(存在且架构匹配——按 P/Invoke 实际解析结果为准,此处仅文件探测)。</summary>
    public static bool IsCoreAvailable => FindCore() is not null;

    /// <summary>探测 Cubism Core 路径;未找到返回 null。</summary>
    public static string? FindCore()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }
        foreach (var dir in SearchDirs())
        {
            var path = Path.Combine(dir, CoreDll);
            if (File.Exists(path))
            {
                return path;
            }
        }
        return null;
    }

    /// <summary>Core 候选目录:程序目录优先,其次用户数据 live2d 目录。</summary>
    public static IEnumerable<string> SearchDirs()
    {
        yield return AppContext.BaseDirectory;
        yield return UserDir;
    }

    /// <summary>
    /// 把用户 live2d 目录加入 DLL 搜索路径(模型加载前调用一次;程序目录场景无需此步)。
    /// 失败静默 —— Core 在程序目录时不需要,目录不存在时设置页有引导。
    /// </summary>
    public static void EnsureDllSearchPath()
    {
        if (!OperatingSystem.IsWindows() || !IsSupported)
        {
            return;
        }
        var dir = UserDir;
        if (!Directory.Exists(dir) || FindCore() is null)
        {
            return;
        }
        try
        {
            var ptr = Marshal.StringToHGlobalUni(dir);
            AddDllDirectory(ptr);
            Marshal.FreeHGlobal(ptr);
            SetDefaultDllDirectories(0x00001000); // LOAD_LIBRARY_SEARCH_USER_DIRS
        }
        catch (Exception)
        {
            // Core 位于程序目录时无需此步;加目录失败按默认搜索路径兜底
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AddDllDirectory(nint directory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetDefaultDllDirectories(uint flags);
}
