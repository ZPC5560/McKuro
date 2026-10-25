namespace McKuro.Services;

/// <summary>
/// 主窗口「界面外观」类设置的进程内变更通知。
/// <para>
/// 导航栏自动隐藏 / 窗口操作栏自动隐藏这类设置需要立刻作用到已经打开的
/// <see cref="McKuro.MainWindow"/>(而不是等下次启动),但设置页 ViewModel 不该直接持有窗口引用。
/// 因此约定:设置页改完 <see cref="McKuro.Core.Services.Settings.AppSettings"/> 后调用
/// <see cref="NotifyChanged"/>,主窗口订阅后自行重读配置并应用。
/// </para>
/// <para>
/// 事件在调用方线程(<b>UI 线程</b>,设置页统一在 UI 线程改开关)同步触发;
/// 订阅方若可能在后台线程触发,需要自行 <c>Dispatcher.UIThread.Post</c>。
/// </para>
/// </summary>
public static class UiSettingsNotifier
{
    /// <summary>界面外观设置已变更(导航栏/窗口操作栏自动隐藏等)。</summary>
    public static event Action? Changed;

    /// <summary>通知所有订阅者重读界面外观设置。无订阅者时是安全的空操作。</summary>
    public static void NotifyChanged() => Changed?.Invoke();
}
