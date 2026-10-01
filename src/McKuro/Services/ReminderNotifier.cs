using Avalonia.Threading;
using McKuro.Core.Services.Notification;
using McKuro.Core.Services.Settings;

namespace McKuro.Services;

/// <summary>提醒类别(同时用于推导悬浮通知卡片的图标与强调色)。</summary>
public enum NotificationKind
{
    /// <summary>游戏签到状态。</summary>
    Sign,
    /// <summary>版本/卡池活动临期。</summary>
    ActivityEnding,
    /// <summary>接口登录会话状态(库街区/云鸣潮/攻略站)。</summary>
    AccountSession,
    /// <summary>周本(战歌重奏)本周收尾。</summary>
    WeeklyBoss,
    /// <summary>每日活跃度。</summary>
    DailyLiveness,
}

/// <summary>
/// 提醒通知器:提醒调度器的出口。按「稳定 Key + 当日台账」去重 ——
/// 同一提醒(同 Key)一天只弹一次悬浮通知,重启不重复打扰(台账持久化到 notifications.json);
/// 类别开关由设置页配置(Notif*Enabled),关闭的类别直接跳过(台账不记录,重新开启后下轮恢复);
/// 总开关(NotifEnabled)关闭时全部跳过 —— 判定统一走 <see cref="NotificationPreferences"/>。
/// </summary>
public sealed class ReminderNotifier
{
    private readonly NotificationStore _store;
    private readonly object _gate = new();
    private List<StoredShownReminder> _shown;

    public ReminderNotifier(string appDataDir)
    {
        _store = new NotificationStore(appDataDir);
        _shown = _store.Load().Shown;
    }

    /// <summary>提醒触发(可在后台线程调用):去重通过后触发 <see cref="Raised"/>(已调度回 UI 线程)。
    /// 诊断(<c>McKuro_UI_HEARTBEAT=1</c>):打印「Raise 调用 → UI 线程实际派发」的延迟,
    /// 用于定位"通知过一会儿才显示"是卡在后台、卡在 UI 队列,还是卡在渲染。</summary>
    public void Raise(string key, NotificationKind kind, string title, string message, string? navKey)
    {
        if (!IsKindEnabled(kind))
        {
            return;
        }
        var today = DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        lock (_gate)
        {
            if (_shown.Any(s => s.Key == key && s.Day == today))
            {
                return;
            }
            _shown.Add(new StoredShownReminder { Key = key, Day = today });
            _store.Save(new ReminderLedger { Shown = _shown.ToList() });
        }
        var draft = new RaisedReminder(kind, title, message, navKey);
        var raisedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        var diagnose = Environment.GetEnvironmentVariable("McKuro_UI_HEARTBEAT") == "1";
        if (diagnose)
        {
            var onUi = Dispatcher.UIThread.CheckAccess();
            System.Console.Error.WriteLine(
                $"MCKURO-NOTIF raise kind={kind} onUiThread={onUi} t={DateTime.Now:HH:mm:ss.fff}");
        }
        if (Dispatcher.UIThread.CheckAccess())
        {
            Raised?.Invoke(draft);
            ReportDispatchLatency(raisedAt, diagnose, kind);
        }
        else
        {
            Dispatcher.UIThread.Post(() =>
            {
                Raised?.Invoke(draft);
                ReportDispatchLatency(raisedAt, diagnose, kind);
            });
        }
    }

    /// <summary>打印后台 Raise → UI 派发完成 的耗时(诊断用)。</summary>
    private static void ReportDispatchLatency(long raisedAt, bool diagnose, NotificationKind kind)
    {
        if (!diagnose)
        {
            return;
        }
        var ms = System.Diagnostics.Stopwatch.GetElapsedTime(raisedAt).TotalMilliseconds;
        System.Console.Error.WriteLine($"MCKURO-NOTIF dispatched kind={kind} latency={ms:F0}ms");
    }
    /// <summary>提醒已触发(悬浮通知弹出;参数为类别/标题/内容/前往页 key)。</summary>
    public event Action<RaisedReminder>? Raised;

    private static bool IsKindEnabled(NotificationKind kind)
        => NotificationPreferences.IsEnabled(kind);
}

/// <summary>一条已触发待展示的提醒。</summary>
public sealed record RaisedReminder(NotificationKind Kind, string Title, string Message, string? NavKey);

/// <summary>悬浮提醒卡片条目(主窗口右上角堆叠;8 秒自动消失,可手动关闭/前往)。</summary>
public sealed partial class ReminderCardItem
{
    public required NotificationKind Kind { get; init; }
    public required string Title { get; init; }
    public required string Message { get; init; }
    public string? NavKey { get; init; }

    /// <summary>手动关闭(主窗口 VM 订阅移除卡片)。</summary>
    public event Action? DismissRequested;

    /// <summary>点击「前往」(主窗口 VM 订阅导航并移除卡片)。</summary>
    public event Action<string>? GoRequested;

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void Dismiss() => DismissRequested?.Invoke();

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void Go() => GoRequested?.Invoke(NavKey ?? "");
}

/// <summary>提醒类别 → 图标(悬浮卡片用;图标名均已在 FluentIcons 枚举中核对)。</summary>
public sealed class NotificationKindIconConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly NotificationKindIconConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is NotificationKind kind
            ? kind switch
            {
                NotificationKind.Sign => FluentIcons.Common.Icon.CalendarCheckmark,
                NotificationKind.ActivityEnding => FluentIcons.Common.Icon.CalendarStar,
                NotificationKind.AccountSession => FluentIcons.Common.Icon.PersonCircle,
                NotificationKind.WeeklyBoss => FluentIcons.Common.Icon.Trophy,
                NotificationKind.DailyLiveness => FluentIcons.Common.Icon.Fire,
                _ => FluentIcons.Common.Icon.Info,
            }
            : FluentIcons.Common.Icon.Info;

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>提醒类别 → 强调色(状态语义:签到蓝/活动橙/登录红/周本紫/活跃度橙红)。</summary>
public sealed class NotificationKindBrushConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly NotificationKindBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        var color = value is NotificationKind kind
            ? kind switch
            {
                NotificationKind.Sign => Avalonia.Media.Color.Parse("#3B82F6"),
                NotificationKind.ActivityEnding => Avalonia.Media.Color.Parse("#F59E0B"),
                NotificationKind.AccountSession => Avalonia.Media.Color.Parse("#EF4444"),
                NotificationKind.WeeklyBoss => Avalonia.Media.Color.Parse("#8B5CF6"),
                NotificationKind.DailyLiveness => Avalonia.Media.Color.Parse("#F97316"),
                _ => Avalonia.Media.Color.Parse("#64748B"),
            }
            : Avalonia.Media.Color.Parse("#64748B");
        return new Avalonia.Media.SolidColorBrush(color);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}
