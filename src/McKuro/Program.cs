using Avalonia;
using System;
using McKuro.Services;

namespace McKuro;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static int Main(string[] args)
    {
        // 单实例:重复启动唤起已有窗口后退出。
        // 冒烟/自检模式(McKuro_SMOKE / McKuro_GEETEST_SMOKE)放行并行——验证脚本需要在
        // 用户实例运行期间也能独立起实例,且次实例秒退会让冒烟"假通过"掩盖问题。
        var smokeMode = Environment.GetEnvironmentVariable("McKuro_SMOKE") == "1"
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("McKuro_GEETEST_SMOKE"));
        if (!smokeMode && !SingleInstanceGuard.TryAcquire())
        {
            SingleInstanceGuard.SignalExistingInstance();
            return 0;
        }
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
