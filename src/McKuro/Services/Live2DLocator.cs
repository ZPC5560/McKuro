using System.Runtime.InteropServices;

namespace McKuro.Services;

/// <summary>
/// Live2D 运行环境定位:
/// - 平台支持:Sparkle.Live2DView(OpenGL 渲染 Cubism)当前仅面向 Windows x64;
/// - Cubism Core(Live2DCubismCore.dll)有独立许可,不随仓库/安装包分发,
///   按以下顺序探测:① 程序目录根(安装包未来内置时自动生效);② 程序目录 live2d\;
///   ③ 用户数据目录 live2d\(设置页"打开目录"引导放置)。命中非程序目录时通过
///   AddDllDirectory 加入 DLL 搜索路径;首选放置目录见 EnsurePreferredDir(程序目录可写即用程序目录)。
/// </summary>
public static class Live2DLocator
{
    private const string CoreDll = "Live2DCubismCore.dll";

    /// <summary>当前平台是否支持 Live2D 渲染。</summary>
    public static bool IsSupported => OperatingSystem.IsWindows()
        && System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.X64;

    /// <summary>程序目录下的 live2d 目录(便携优先:与 exe 同级,绿色安装可直接放置模型与 Core)。</summary>
    public static string ProgramDir => Path.Combine(AppContext.BaseDirectory, "live2d");

    /// <summary>用户数据目录 live2d(%AppData%/McKuro/live2d):程序目录无写权限时的回退。</summary>
    public static string UserDir => Path.Combine(McKuro.Services.AppServices.AppDataDir, "live2d");

    private static string? _preferredDir;

    /// <summary>
    /// 首选 live2d 目录(放置 Cubism Core 与模型):程序目录可写(便携安装)时优先程序目录,
    /// 无写权限(如装进 Program Files)才回退用户数据目录。首次调用探测并缓存,返回前确保目录存在。
    /// 设置页"打开模型目录"与默认模型发现都基于它。
    /// </summary>
    public static string EnsurePreferredDir()
    {
        if (_preferredDir is not null)
        {
            return _preferredDir;
        }
        foreach (var dir in new[] { ProgramDir, UserDir })
        {
            try
            {
                Directory.CreateDirectory(dir);
                var probe = Path.Combine(dir, ".write-probe");
                File.WriteAllText(probe, "");
                File.Delete(probe);
                _preferredDir = dir;
                return dir;
            }
            catch (Exception)
            {
                // 无写权限 → 尝试下一个候选
            }
        }
        _preferredDir = UserDir;
        return UserDir;
    }

    /// <summary>Cubism Core 是否可加载(存在且架构匹配——按 P/Invoke 实际解析结果为准,此处仅文件探测)。</summary>
    public static bool IsCoreAvailable => FindCore() is not null;

    /// <summary>在目录树中递归定位 &lt;name&gt;.model3.json 并返回其所在目录;找不到返回 null。</summary>
    public static string? FindModelDir(string? root, string name)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root) || string.IsNullOrWhiteSpace(name))
        {
            return null;
        }
        try
        {
            var hit = Directory.EnumerateFiles(root, name + ".model3.json", SearchOption.AllDirectories)
                .FirstOrDefault();
            return hit is null ? null : Path.GetDirectoryName(hit);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// 在目录下递归发现一个默认模型:未导入/未选择模型时兜底,让放置到 live2d 目录的模型
    /// 零配置生效。优先 Hiyori(Live2D 官方示例模型,随 Cubism SDK 分发),其余取字典序第一个。
    /// 返回 (所在目录, 裸模型名);目录无效或没有模型返回 null。
    /// </summary>
    public static (string Dir, string Name)? FindDefaultModel(string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
        {
            return null;
        }
        try
        {
            var hit = Directory.EnumerateFiles(dir, "*.model3.json", SearchOption.AllDirectories)
                .Select(f => (Dir: Path.GetDirectoryName(f)!, Name: NormalizeModelName(Path.GetFileName(f))))
                .Where(m => !string.IsNullOrEmpty(m.Name))
                .OrderBy(m => m.Name == "Hiyori" ? 0 : 1)
                .ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            return hit.Name is null ? null : hit;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// 归一化模型名为 Sparkle.Live2DView 要求的裸名(加载时由库拼 ".model3.json")。
    /// 兼容历史数据:早期扫描用 GetFileNameWithoutExtension 只剥了 ".json",
    /// 把 "Hiyori.model3" 存进了设置,按此名加载会找不到文件。
    /// </summary>
    public static string NormalizeModelName(string? name)
    {
        var n = name?.Trim() ?? "";
        if (n.EndsWith(".model3.json", StringComparison.OrdinalIgnoreCase))
        {
            n = n[..^".model3.json".Length];
        }
        if (n.EndsWith(".model3", StringComparison.OrdinalIgnoreCase))
        {
            n = n[..^".model3".Length];
        }
        return n;
    }

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

    /// <summary>Core 候选目录:程序目录根(live2dcubismcore.dll 直接放安装根)→ 程序目录 live2d\ → 用户数据 live2d\。</summary>
    public static IEnumerable<string> SearchDirs()
    {
        yield return AppContext.BaseDirectory;
        yield return ProgramDir;
        yield return UserDir;
    }

    /// <summary>
    /// 把 live2d 候选目录加入 DLL 搜索路径(模型加载前调用一次;程序目录场景无需此步)。
    /// 失败静默 —— Core 在程序目录时不需要,目录不存在时设置页有引导。
    /// </summary>
    public static void EnsureDllSearchPath()
    {
        if (!OperatingSystem.IsWindows() || !IsSupported)
        {
            return;
        }
        if (FindCore() is null)
        {
            return;
        }
        try
        {
            var added = false;
            foreach (var dir in SearchDirs().Skip(1))
            {
                if (!Directory.Exists(dir))
                {
                    continue;
                }
                var ptr = Marshal.StringToHGlobalUni(dir);
                if (AddDllDirectory(ptr))
                {
                    added = true;
                }
                Marshal.FreeHGlobal(ptr);
            }
            if (added)
            {
                SetDefaultDllDirectories(0x00001000); // LOAD_LIBRARY_SEARCH_DEFAULT_DIRS(含应用目录+用户目录)
            }
        }
        catch (Exception)
        {
            // Core 位于程序目录根时无需此步;加目录失败按默认搜索路径兜底
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AddDllDirectory(nint directory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetDefaultDllDirectories(uint flags);
}
