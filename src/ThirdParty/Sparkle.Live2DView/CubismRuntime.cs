using System.Reflection;
using System.Runtime.InteropServices;
using Live2DCSharpSDK.App;
using Live2DCSharpSDK.Framework;

namespace Sparkle.Live2DView;

// CubismFramework 是进程级状态，重复 StartUp 会把内部资源弄乱。
internal static class CubismRuntime
{
    private static readonly object SyncRoot = new();
    private static bool _started;
    private static bool _resolverRegistered;

    /// <summary>Cubism Core 的库名(SDK 用 DllImport/LibraryImport("Live2DCubismCore"))。</summary>
    private const string CoreLibraryName = "Live2DCubismCore";

    public static void EnsureStarted()
    {
        if (_started) return;

        lock (SyncRoot)
        {
            if (_started) return;

            RegisterCoreResolver();

            CubismFramework.StartUp(
                new LAppAllocator(),
                new CubismOption
                {
                    LogFunction = message => System.Diagnostics.Debug.WriteLine($"[Cubism] {message}"),
                    LoggingLevel = LAppDefine.CubismLoggingLevel
                });

            _started = true;
        }
    }

    /// <summary>
    /// 为 <c>Live2DCubismCore</c> 注册原生库解析器,使其能从程序目录的 <c>live2d/</c> 子目录加载。
    ///
    /// <para><b>为什么需要</b>:Core 随包放在 <c>live2d/</c> 而不是可执行文件同级。
    /// Windows 上 <c>Live2DLocator.EnsureDllSearchPath</c> 用 AddDllDirectory 把它加进搜索路径,
    /// 但 macOS 没有等价机制 —— dyld 只查 <c>@rpath</c> / <c>DYLD_LIBRARY_PATH</c> / 系统目录,
    /// 不会搜索应用的子目录。于是 macOS 上表现为
    /// <c>DllNotFoundException: Unable to load shared library 'Live2DCubismCore'</c>
    /// (文件明明存在、dlopen 也能成功),已实测。</para>
    ///
    /// <para><b>必须注册在声明 DllImport 的那个程序集上</b>:解析器按程序集生效,
    /// <c>csm*</c> 的 DllImport 声明在 <c>Live2DCSharpSDK.Framework</c> 里,
    /// 注册到本程序集(<c>Sparkle.Live2DView</c>)不会有任何效果 —— 已实测踩到。</para>
    ///
    /// <para><b>为什么用解析器而不是 Environment.SetEnvironmentVariable("DYLD_LIBRARY_PATH")</b>:
    /// dyld 在进程启动时读取该变量,运行时再设置无效。</para>
    ///
    /// <para>注册失败不抛出 —— 让后续 StartUp 走默认解析,由调用方按异常处理。</para>
    /// </summary>
    private static void RegisterCoreResolver()
    {
        if (_resolverRegistered) return;
        _resolverRegistered = true;

        try
        {
            // Core 类型的宿主程序集就是 DllImport 的声明处
            var coreAssembly = typeof(CubismFramework).Assembly;
            NativeLibrary.SetDllImportResolver(coreAssembly, ResolveCore);
        }
        catch (Exception)
        {
            // 同一程序集重复注册会抛 InvalidOperationException:已有解析器时保持原样
        }
    }

    private static IntPtr ResolveCore(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, CoreLibraryName, StringComparison.Ordinal))
        {
            return IntPtr.Zero;   // 交给默认解析
        }

        foreach (var candidate in CoreCandidates())
        {
            if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out var handle))
            {
                return handle;
            }
        }

        return IntPtr.Zero;
    }

    /// <summary>按平台枚举候选文件名与目录(程序目录根、live2d/)。</summary>
    private static IEnumerable<string> CoreCandidates()
    {
        var baseDir = AppContext.BaseDirectory;
        var fileName =
            OperatingSystem.IsWindows() ? "Live2DCubismCore.dll"
            : OperatingSystem.IsMacOS() ? "libLive2DCubismCore.dylib"
            : "libLive2DCubismCore.so";

        yield return Path.Combine(baseDir, fileName);
        yield return Path.Combine(baseDir, "live2d", fileName);
    }
}
