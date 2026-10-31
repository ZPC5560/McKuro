using System.Runtime.InteropServices;

namespace McKuro.Services;

/// <summary>
/// Live2D 运行环境定位:
/// - 平台支持:Sparkle.Live2DView 用 Avalonia 的 <c>OpenGlControlBase</c> 渲染 Cubism,
///   本身不绑定 Windows。Windows x64 / macOS(arm64、x64)/ Linux x64 均在支持范围内;
/// - Cubism Core 随包内置在程序目录的 <c>live2d/</c> 下,文件名按平台不同:
///   Windows <c>Live2DCubismCore.dll</c>、macOS <c>libLive2DCubismCore.dylib</c>、
///   Linux <c>libLive2DCubismCore.so</c>。三者都在官方 SDK 的
///   <c>Core/RedistributableFiles.txt</c> 可再分发清单内(依据 Live2D Proprietary
///   Software License Agreement 第 5 条;许可声明见 <c>Assets/live2d/NOTICE.txt</c>)。
///   用户自备的副本同样可用,按以下顺序探测:① 程序目录根 → ② 程序目录 live2d/ →
///   ③ 用户数据目录 live2d/。命中非程序目录时通过 AddDllDirectory 加入 DLL 搜索路径
///   (仅 Windows 需要);首选「可写」放置目录见 <see cref="EnsurePreferredDir"/>。
/// </summary>
public static class Live2DLocator
{
    private const string CoreDll = "Live2DCubismCore.dll";
    private const string CoreDylib = "libLive2DCubismCore.dylib";
    private const string CoreSo = "libLive2DCubismCore.so";

    /// <summary>
    /// 当前平台的 Cubism Core 文件名。
    /// <para>必须与 DllImport("Live2DCubismCore") 的解析结果一致:运行时会依次尝试
    /// <c>libLive2DCubismCore.dylib</c>/<c>.so</c>/<c>Live2DCubismCore.dll</c> 等变体,
    /// 探测阶段用同名文件才能判断「Core 是否真的可加载」。</para>
    /// </summary>
    public static string CoreFileName =>
        OperatingSystem.IsWindows() ? CoreDll
        : OperatingSystem.IsMacOS() ? CoreDylib
        : CoreSo;

    /// <summary>当前平台是否支持 Live2D 渲染(Avalonia OpenGL 可用的桌面平台)。</summary>
    public static bool IsSupported
    {
        get
        {
            var arch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture;
            if (OperatingSystem.IsWindows())
            {
                return arch == System.Runtime.InteropServices.Architecture.X64;
            }
            if (OperatingSystem.IsMacOS())
            {
                return arch is System.Runtime.InteropServices.Architecture.Arm64
                    or System.Runtime.InteropServices.Architecture.X64;
            }
            if (OperatingSystem.IsLinux())
            {
                return arch == System.Runtime.InteropServices.Architecture.X64;
            }
            return false;
        }
    }

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
    /// 定位指定模型名所在目录:先看根目录,再递归查找 &lt;name&gt;.model3.json。
    /// 首页与设置页共用这一套解析 —— 多模型分放不同子目录时,选中的模型必须与加载的一致,
    /// 否则首页会退回 FindDefaultModel 的「字典序第一个」:用户切到白金形态,首页却仍加载红黑。
    /// name 为空或未找到返回 null。
    /// </summary>
    public static string? ResolveModelDir(string? root, string? name)
    {
        var normalized = NormalizeModelName(name);
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(normalized) || !Directory.Exists(root))
        {
            return null;
        }
        try
        {
            if (File.Exists(Path.Combine(root, normalized + ".model3.json")))
            {
                return root;
            }
        }
        catch (Exception)
        {
            return null;
        }
        return FindModelDir(root, normalized);
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

    /// <summary>
    /// 可<b>递归</b>发现模型的根目录(只读探测,不创建):程序目录 live2d\ + 用户数据目录。
    ///
    /// <para><b>为什么必须同时搜程序目录</b>:随包分发的模型放在程序目录的 <c>live2d\</c> 下。
    /// 装到 Program Files 时该目录<b>不可写</b>,于是 <see cref="EnsurePreferredDir"/> 会回退到
    /// 用户数据目录 —— 而那里没有随包模型。若发现逻辑只认"首选可写目录",随包模型将永远不可见
    /// (表现为"装了却看不到模型")。故「可写目录」与「发现目录」必须是两套概念。</para>
    ///
    /// <para><b>程序根不在这里</b>:旧实现把程序根也当递归根,便携版解压到 Downloads 这类大目录时
    /// 首页构造会在 UI 线程同步遍历整棵目录树,启动卡住。平铺在 exe 同级的模型改由
    /// <see cref="FlatRoot"/> 只查顶层(见 <see cref="ResolveModelDirAnywhere"/>/<see cref="FindDefaultModelAnywhere"/>)。</para>
    /// </summary>
    public static IEnumerable<string> DiscoveryRoots()
    {
        foreach (var dir in new[] { ProgramDir, UserDir })
        {
            bool exists;
            try
            {
                exists = Directory.Exists(dir);
            }
            catch (Exception)
            {
                continue;
            }
            if (exists)
            {
                yield return dir;
            }
        }
    }

    /// <summary>程序根(exe 同级):只查顶层的模型位置(平铺放置的模型),不递归。</summary>
    public static string FlatRoot => AppContext.BaseDirectory;

    /// <summary>
    /// 在所有发现根目录中定位指定模型名(先程序目录 live2d\,再程序根顶层,再用户目录;各递归根内先根后递归)。
    /// 找不到返回 null。
    /// </summary>
    public static string? ResolveModelDirAnywhere(string? name)
    {
        var programHit = ResolveModelDir(ProgramDir, name);
        if (programHit is not null)
        {
            return programHit;
        }
        // 程序根只查顶层(平铺在 exe 同级),不递归整个程序目录
        var normalized = NormalizeModelName(name);
        try
        {
            if (!string.IsNullOrWhiteSpace(normalized)
                && File.Exists(Path.Combine(FlatRoot, normalized + ".model3.json")))
            {
                return FlatRoot;
            }
        }
        catch (Exception)
        {
            // 探测失败:继续找用户目录
        }
        foreach (var root in DiscoveryRoots())
        {
            if (string.Equals(root, ProgramDir, StringComparison.Ordinal))
            {
                continue; // 已查过
            }
            var hit = ResolveModelDir(root, name);
            if (hit is not null)
            {
                return hit;
            }
        }
        return null;
    }

    /// <summary>
    /// 在所有发现根目录中找默认模型。偏好顺序:Hiyori(官方示例)→ 内置主形态(红黑)
    /// → 其余取字典序第一个。找不到返回 null。
    ///
    /// <para><b>必须跨根统一排序,不能"每个根只挑一个"</b>:<see cref="FindDefaultModel"/> 在单个根内
    /// 只会返回一个结果(字典序第一),而随包目录里同时有「心-红黑」与「心-白金」 ——
    /// 按 Ordinal 比较「白」(U+767D) &lt; 「红」(U+7EA2),字典序第一是<b>白金</b>,
    /// 于是逐根挑选会把白金当成默认,内置主形态永远选不中(实测踩到)。</para>
    ///
    /// <paramref name="roots"/> 仅供单测注入(默认 = <see cref="DiscoveryRoots"/>)。
    /// </summary>
    public static (string Dir, string Name)? FindDefaultModelAnywhere(IEnumerable<string>? roots = null)
    {
        (string Dir, string Name)? best = null;
        var bestRank = int.MaxValue;

        // 默认根:递归根 + 程序根(仅顶层,见 DiscoveryRoots 注释)
        var searches = roots is not null
            ? roots.Select(r => (Root: r, Option: SearchOption.AllDirectories))
            : DiscoveryRoots().Select(r => (Root: r, Option: SearchOption.AllDirectories))
                .Append((Root: FlatRoot, Option: SearchOption.TopDirectoryOnly));
        foreach (var (root, option) in searches)
        {
            foreach (var (dir, name) in EnumerateModels(root, option))
            {
                var rank = PreferenceRank(name);
                if (rank < bestRank)
                {
                    bestRank = rank;
                    best = (dir, name);
                }

                if (bestRank == 0)
                {
                    return best;   // Hiyori 最高优先级,命中即可停
                }
            }
        }

        return best;
    }

    /// <summary>枚举单个根目录下的全部模型(目录, 裸名)。失败返回空序列。</summary>
    private static IEnumerable<(string Dir, string Name)> EnumerateModels(string root, SearchOption option)
    {
        List<(string Dir, string Name)> found = [];
        try
        {
            if (!Directory.Exists(root))
            {
                return found;
            }

            foreach (var file in Directory.EnumerateFiles(root, "*.model3.json", option))
            {
                var name = NormalizeModelName(Path.GetFileName(file));
                var dir = Path.GetDirectoryName(file);
                if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(dir))
                {
                    found.Add((dir, name));
                }
            }
        }
        catch (Exception)
        {
            // 无权限/枚举失败:视为该根没有模型
        }
        return found;
    }

    /// <summary>默认模型偏好:0 = Hiyori(官方示例);1 = 内置主形态;2 = 其它。</summary>
    private static int PreferenceRank(string name) => name switch
    {
        "Hiyori" => 0,
        _ when BundledDefaultName.Equals(name, StringComparison.OrdinalIgnoreCase) => 1,
        _ => 2,
    };

    /// <summary>随包内置模型的默认形态(红黑)。随包时用作未指定模型时的首选。</summary>
    public const string BundledDefaultName = "心-红黑";

    /// <summary>探测 Cubism Core 路径;未找到返回 null。文件名按平台区分(见 <see cref="CoreFileName"/>)。</summary>
    public static string? FindCore()
    {
        if (!IsSupported)
        {
            return null;
        }
        foreach (var dir in SearchDirs())
        {
            var path = Path.Combine(dir, CoreFileName);
            if (File.Exists(path))
            {
                return path;
            }
        }
        return null;
    }

    /// <summary>Core 候选目录:程序目录根 → 程序目录 live2d/ → 用户数据 live2d/。</summary>
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
