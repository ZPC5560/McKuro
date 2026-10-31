using Avalonia.Threading;
using McKuro.Core.Infrastructure;
using Microsoft.Extensions.Logging;

namespace McKuro.Services;

/// <summary>
/// 全局未处理异常记录:AppDomain / TaskScheduler / Avalonia UI 线程三个出口。
/// <para>
/// 代码后置里有大量 async 事件处理器,逃逸的异常会让进程退出且不留任何痕迹;这里只负责<b>记日志</b>,
/// 不改变崩溃语义(UI 线程异常不置 Handled,进程照常终止,避免带着损坏状态继续运行)。
/// </para>
/// <para>
/// 日志优先走 <see cref="AppServices.LoggerFactory"/>;容器尚未初始化(Program.Main 早期)时
/// 直接用 <see cref="FileLoggerProvider"/> 写到与 <see cref="AppServices.LogDir"/> 相同的目录。
/// </para>
/// </summary>
public static class GlobalExceptionLogger
{
    private const string Category = "McKuro.Unhandled";

    private static int _processHandlersRegistered;
    private static int _dispatcherHandlerRegistered;

    /// <summary>进程级处理器(AppDomain + TaskScheduler):Program.Main 第一行调用,不依赖 Avalonia。</summary>
    public static void RegisterProcessHandlers()
    {
        if (Interlocked.Exchange(ref _processHandlersRegistered, 1) == 1)
        {
            return;
        }
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    /// <summary>Avalonia UI 线程处理器:框架初始化后(App.OnFrameworkInitializationCompleted)调用。</summary>
    public static void RegisterDispatcherHandler()
    {
        if (Interlocked.Exchange(ref _dispatcherHandlerRegistered, 1) == 1)
        {
            return;
        }
        Dispatcher.UIThread.UnhandledException += OnDispatcherUnhandledException;
    }

    private static void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        => Write(LogLevel.Critical, "AppDomain.UnhandledException", e.ExceptionObject as Exception,
            e.ExceptionObject is Exception ? null : e.ExceptionObject?.ToString());

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Write(LogLevel.Error, "TaskScheduler.UnobservedTaskException", e.Exception, null);
        e.SetObserved();
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        => Write(LogLevel.Critical, "Dispatcher.UIThread.UnhandledException", e.Exception, null);

    private static void Write(LogLevel level, string source, Exception? exception, string? detail)
    {
        try
        {
            var factory = AppServices.LoggerFactory;
            if (factory is not null)
            {
                factory.CreateLogger(Category).Log(level, exception, "未处理异常 ({Source}) {Detail}", source, detail ?? "");
                return;
            }
            // 容器未初始化:直接写文件日志(目录规则与 AppServices.Initialize 一致)
            using var provider = new FileLoggerProvider(FallbackLogDir());
            provider.CreateLogger(Category).Log(level, exception, "未处理异常 ({Source}) {Detail}", source, detail ?? "");
        }
        catch (Exception)
        {
            // 记录失败不能再抛(处于异常出口,二次异常会掩盖原始问题)
        }
        try
        {
            Console.Error.WriteLine($"MCKURO-UNHANDLED {source}: {exception?.GetType().Name}: {exception?.Message ?? detail}");
        }
        catch (Exception)
        {
            // 无控制台
        }
    }

    private static string FallbackLogDir()
        => !string.IsNullOrEmpty(AppServices.LogDir)
            ? AppServices.LogDir
            : OperatingSystem.IsWindows()
                ? Path.Combine(AppContext.BaseDirectory, "logs")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "McKuro", "logs");
}
