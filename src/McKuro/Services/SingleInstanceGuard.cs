using System;
using System.Threading;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace McKuro.Services;

/// <summary>
/// 单实例进程守卫:首个实例持有命名 <see cref="Mutex"/> 直到进程退出;
/// 后续实例通过命名 <see cref="EventWaitHandle"/> 通知已有实例「唤起主窗口」(含隐藏到托盘的状态)后自行退出。
/// <para>
/// 命名同步原语跨平台可用(Windows 内核对象;Unix 由 .NET 以文件模拟),NativeAOT 安全(纯 BCL 无反射)。
/// Mutex/EventWaitHandle 均存根引用,防止 GC 终结器提前释放命名句柄。
/// </para>
/// </summary>
public static class SingleInstanceGuard
{
    private const string MutexName = "Local\\McKuro_SingleInstance";
    private const string ShowEventName = "Local\\McKuro_RequestShow";

    private static Mutex? _mutex;
    private static EventWaitHandle? _showEvent;

    /// <summary>本进程是否为主实例(成功 <see cref="TryAcquire"/> 后置真)。</summary>
    public static bool IsPrimary { get; private set; }

    /// <summary>尝试成为主实例。返回 false 表示已有实例在运行,调用方应 <see cref="SignalExistingInstance"/> 后退出。</summary>
    public static bool TryAcquire()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (createdNew)
        {
            IsPrimary = true;
            return true;
        }
        _mutex.Dispose();
        _mutex = null;
        return false;
    }

    /// <summary>次实例:通知主实例唤起主窗口(尽力而为;主实例正在退出/未监听时静默)。</summary>
    public static void SignalExistingInstance()
    {
        try
        {
            using var ev = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            ev.Set();
        }
        catch (Exception)
        {
            // 主实例可能正在退出:静默即可,次实例照常结束
        }
    }

    /// <summary>
    /// 主实例启动唤起监听(后台线程,AutoReset 每次消费一个信号)。
    /// 非主实例(含冒烟模式跳过单实例的场景)为空操作,避免测试实例抢消费真实次实例的唤起信号。
    /// </summary>
    public static void StartActivationListener()
    {
        if (!IsPrimary)
        {
            return;
        }
        var ev = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _showEvent = ev; // 根引用:命名事件被 GC 终结会破坏监听
        var thread = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    ev.WaitOne();
                }
                catch (Exception)
                {
                    return; // 句柄失效等异常:结束监听,不影响主流程
                }
                Dispatcher.UIThread.Post(ActivateMainWindow);
            }
        })
        {
            IsBackground = true,
            Name = "McKuro-ActivationListener",
        };
        thread.Start();
    }

    private static void ActivateMainWindow()
    {
        // 启动极早期(主窗口尚未创建)收到的唤起信号:窗口随后本来就会显示,忽略即可。
        if (Application.Current?.ApplicationLifetime
            is IClassicDesktopStyleApplicationLifetime { MainWindow: MainWindow win })
        {
            win.RestoreFromHidden();
        }
    }
}
