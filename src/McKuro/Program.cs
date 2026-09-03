using Avalonia;
using System;
using System.Runtime.InteropServices;
using Avalonia.Platform;
using Avalonia.Native;
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
        // 主实例:注册当前安装目录,供 Inno 安装器自动定位既有安装(见 InstallLocationRegistry)
        InstallLocationRegistry.TryRegister();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect();

        // 启用 OpenGL 渲染模式 —— libmpv 的 GPU 渲染(StartOpenGlRendering)需要 OpenGL 上下文:
        // Windows: ANGLE(OpenGL ES over D3D11) + 软件回退
        // macOS: 原生 OpenGL(Apple 已弃用但现行可用,实际是 Metal 兼容层) + 软件回退
        // Linux: EGL/GLX + 软件回退
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            builder.With(new Win32PlatformOptions
            {
                RenderingMode = [Win32RenderingMode.AngleEgl, Win32RenderingMode.Software]
            });
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            builder.With(new AvaloniaNativePlatformOptions
            {
                RenderingMode = [AvaloniaNativeRenderingMode.OpenGl, AvaloniaNativeRenderingMode.Software]
            });
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            builder.With(new X11PlatformOptions
            {
                RenderingMode = [X11RenderingMode.Egl, X11RenderingMode.Glx, X11RenderingMode.Software]
            });

#if DEBUG
        builder.WithDeveloperTools();
#endif
        return builder.WithInterFont()
            .LogToTrace();
    }
}
