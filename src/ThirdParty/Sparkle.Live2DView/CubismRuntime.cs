using Live2DCSharpSDK.App;
using Live2DCSharpSDK.Framework;

namespace Sparkle.Live2DView;

// CubismFramework 是进程级状态，重复 StartUp 会把内部资源弄乱。
internal static class CubismRuntime
{
    private static readonly object SyncRoot = new();
    private static bool _started;

    public static void EnsureStarted()
    {
        if (_started) return;

        lock (SyncRoot)
        {
            if (_started) return;

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
}
