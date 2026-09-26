using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FluentIcons.Common;
using McKuro.Services;

namespace McKuro.ViewModels;

/// <summary>导航页条目。</summary>
public sealed class NavigationItem : ObservableObject
{
    public required string Title { get; init; }
    public required Icon Icon { get; init; }
    public required string Key { get; init; }
    public required ViewModelBase ViewModel { get; init; }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

/// <summary>主窗口(Haiyu Shell 风格):左侧 60px 图标导航 + 内容区。</summary>
/// <remarks>
/// 订阅 <see cref="NavigationRequestedMessage"/> 实现跨 ViewModel 导航,避免子页面持有主窗口引用。
/// </remarks>
public sealed partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty]
    private ViewModelBase? _currentPage;

    [ObservableProperty]
    private NavigationItem? _selectedNavigationItem;

    /// <summary>导航栏账号头像(本地磁盘缓存路径;空 = 无缓存,显示默认守岸人图标)。</summary>
    [ObservableProperty]
    private string _navAvatarPath = "";

    /// <summary>导航栏是否已有真实账号头像可显示。</summary>
    public bool HasNavAvatar => !string.IsNullOrEmpty(NavAvatarPath);

    partial void OnNavAvatarPathChanged(string value) => OnPropertyChanged(nameof(HasNavAvatar));

    partial void OnToastTextChanged(string value) => OnPropertyChanged(nameof(ToastVisible));

    /// <summary>显示悬浮通知:立即展示,3 秒后自动关闭;连续触发时重置计时。</summary>
    public void ShowToast(string text)
    {
        _toastCts?.Cancel();
        _toastCts?.Dispose();
        _toastCts = new CancellationTokenSource();
        ToastText = text;

        var token = _toastCts.Token;
        _ = AutoHideToastAsync(token);
    }

    private async Task AutoHideToastAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3), token).ConfigureAwait(false);
            ToastText = "";
        }
        catch (OperationCanceledException)
        {
            // 新通知到来时取消旧计时,由新通知接管
        }
    }

    public List<NavigationItem> NavigationItems { get; }

    /// <summary>设置页实例(自更新状态与命令供主窗口更新弹窗绑定;子 VM 全部启动即建,天然单例)。</summary>
    public SettingsViewModel SettingsPage => _settings;

    // ---------- 悬浮提醒通知(除设置页外全局显示) ----------

    /// <summary>同时堆叠的提醒卡片上限(超出挤掉最旧的)。</summary>
    private const int MaxReminderCards = 3;

    /// <summary>提醒卡片自动消失时长。</summary>
    private static readonly TimeSpan ReminderCardLifetime = TimeSpan.FromSeconds(8);

    /// <summary>当前堆叠的提醒卡片(右上角,新的在下)。</summary>
    public System.Collections.ObjectModel.ObservableCollection<ReminderCardItem> ReminderCards { get; } = [];

    /// <summary>悬浮提醒是否可见:有卡片且当前页不是设置页(设置页承载提醒开关配置,不叠加遮挡)。</summary>
    public bool IsReminderOverlayVisible => ReminderCards.Count > 0 && CurrentPage is not SettingsViewModel;

    /// <summary>订阅提醒通知器:弹出卡片并启动自动消失计时。</summary>
    private void HookReminders()
    {
        AppServices.Reminders.Raised += reminder => ShowReminderCard(new ReminderCardItem
        {
            Kind = reminder.Kind,
            Title = reminder.Title,
            Message = reminder.Message,
            NavKey = reminder.NavKey,
        });
    }

    private void ShowReminderCard(ReminderCardItem card)
    {
        while (ReminderCards.Count >= MaxReminderCards)
        {
            ReminderCards.RemoveAt(0);
        }
        card.DismissRequested += () => RemoveReminderCard(card);
        card.GoRequested += _ => RemoveReminderCard(card);
        ReminderCards.Add(card);
        OnPropertyChanged(nameof(IsReminderOverlayVisible));

        // 诊断:卡片入列(数据层可见)→ 实际渲染完成。用 DispatcherPriority.Render + 再等一帧
        // (Background 回调),区分"只是排进了渲染队列"与"这一帧真的画完了"。
        if (McKuro.Services.UiHeartbeat.Enabled)
        {
            var addedAt = System.Diagnostics.Stopwatch.GetTimestamp();
            System.Console.Error.WriteLine($"MCKURO-NOTIF card_added kind={card.Kind} t={DateTime.Now:HH:mm:ss.fff}");
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                var renderMs = System.Diagnostics.Stopwatch.GetElapsedTime(addedAt).TotalMilliseconds;
                System.Console.Error.WriteLine(
                    $"MCKURO-NOTIF card_render_queued kind={card.Kind} afterAdd={renderMs:F0}ms t={DateTime.Now:HH:mm:ss.fff}");
                // 渲染优先级回调之后的第一帧(Background 低于 Render,必然晚于本次渲染提交)
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    var paintMs = System.Diagnostics.Stopwatch.GetElapsedTime(addedAt).TotalMilliseconds;
                    System.Console.Error.WriteLine(
                        $"MCKURO-NOTIF card_painted kind={card.Kind} afterAdd={paintMs:F0}ms t={DateTime.Now:HH:mm:ss.fff}");
                }, Avalonia.Threading.DispatcherPriority.Background);
            }, Avalonia.Threading.DispatcherPriority.Render);
        }

        // 自动消失:每张卡片独立计时(UI 线程 DispatcherTimer;先到先移除)
        var timer = new Avalonia.Threading.DispatcherTimer { Interval = ReminderCardLifetime };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            RemoveReminderCard(card);
        };
        timer.Start();
    }

    private void RemoveReminderCard(ReminderCardItem card)
    {
        if (ReminderCards.Remove(card))
        {
            OnPropertyChanged(nameof(IsReminderOverlayVisible));
        }
    }

    /// <summary>发现新版本的询问弹窗(自动检查触发;AutoInstall 开启时不弹,直接升级)。</summary>
    [ObservableProperty]
    private bool _appUpdatePromptVisible;

    /// <summary>悬浮通知(Toast)当前文案;空 = 不显示。</summary>
    [ObservableProperty]
    private string _toastText = "";

    /// <summary>悬浮通知是否可见(有文案且未到 3 秒自动关闭)。</summary>
    public bool ToastVisible => !string.IsNullOrEmpty(ToastText);

    private CancellationTokenSource? _toastCts;

    private readonly SettingsViewModel _settings;
    private readonly Dictionary<string, NavigationItem> _navByKey;
    private readonly IMessenger _messenger;

    public MainWindowViewModel() : this(WeakReferenceMessenger.Default)
    {
    }

    public MainWindowViewModel(IMessenger messenger)
    {
        _messenger = messenger;

        // 启动即用磁盘缓存头像占位(主页每次刷新都会把头像落盘到 icon_cache/avatar,按 userId)
        var navAccount = AppServices.KuroAccounts.Current;
        if (navAccount is not null && !string.IsNullOrEmpty(navAccount.UserId))
        {
            var cached = AppServices.IconCache.GetCachedIconPath("avatar", IconDiskCacheService.Safe(navAccount.UserId));
            if (cached is not null)
            {
                NavAvatarPath = cached;
            }
        }

        // 主页解析出新头像(下载落盘)后即时切换
        WeakReferenceMessenger.Default.Register<MainWindowViewModel, AvatarResolvedMessage>(this,
            static (recipient, message) => recipient.NavAvatarPath = message.Value);

        // 悬浮通知(兑换码复制成功等):主窗口统一展示,3 秒自动关闭
        WeakReferenceMessenger.Default.Register<MainWindowViewModel, ShowToastMessage>(this,
            static (recipient, message) => recipient.ShowToast(message.Value));

        // 悬浮提醒(签到/活动/登录/周本/活跃度):提醒通知器触发 → 右上角卡片(设置页外显示)
        HookReminders();

        // 诊断冒烟(McKuro_SMOKE_NOTIF=1):预置三类示例提醒供悬浮通知 UI 验证;不触碰网络
        if (Environment.GetEnvironmentVariable("McKuro_SMOKE_NOTIF") == "1")
        {
            SeedSmokeReminders();
        }

        var home = new HomeViewModel();
        var launcher = new LauncherViewModel();
        var gacha = new GachaViewModel();
        var roles = new RolesViewModel(_messenger);
        var sign = new SignViewModel();
        var activity = new ActivityViewModel();
        var wiki = new WikiViewModel();
        var redeem = new RedemptionCodeViewModel();
        var playTime = new PlayTimeViewModel();
        var tower = new TowerViewModel();
        var account = new AccountViewModel();
        var settings = _settings = new SettingsViewModel();

        // 导航标题走 LanguageService(App 启动时已按设置加载语言;重启后切换生效)
        NavigationItems =
        [
            new NavigationItem { Title = LanguageService.Format("Nav.Home"),        Icon = Icon.Home,               Key = NavigationKeys.Home,      ViewModel = home },
            new NavigationItem { Title = LanguageService.Format("Nav.Launcher"),    Icon = Icon.Play,               Key = NavigationKeys.Launcher, ViewModel = launcher },
            new NavigationItem { Title = LanguageService.Format("Nav.Gacha"),       Icon = Icon.Gauge,              Key = NavigationKeys.Gacha,    ViewModel = gacha },
            new NavigationItem { Title = LanguageService.Format("Nav.Roles"),       Icon = Icon.Person,             Key = NavigationKeys.Roles,    ViewModel = roles },
            new NavigationItem { Title = LanguageService.Format("Nav.Sign"),        Icon = Icon.CalendarCheckmark,  Key = NavigationKeys.Sign,     ViewModel = sign },
            new NavigationItem { Title = LanguageService.Format("Nav.Activity"),    Icon = Icon.CalendarStar,       Key = NavigationKeys.Activity, ViewModel = activity },
            new NavigationItem { Title = LanguageService.Format("Nav.Wiki"),        Icon = Icon.BookOpen,           Key = NavigationKeys.Wiki,     ViewModel = wiki },
            new NavigationItem { Title = LanguageService.Format("Nav.RedeemCodes"), Icon = Icon.TicketDiagonal,     Key = NavigationKeys.RedeemCodes, ViewModel = redeem },
            new NavigationItem { Title = LanguageService.Format("Nav.PlayTime"),    Icon = Icon.Timer,              Key = NavigationKeys.PlayTime,  ViewModel = playTime },
            new NavigationItem { Title = LanguageService.Format("Nav.Tower"),       Icon = Icon.BuildingSkyscraper, Key = NavigationKeys.Tower,     ViewModel = tower },
            new NavigationItem { Title = LanguageService.Format("Nav.Account"),     Icon = Icon.PersonCircle,       Key = NavigationKeys.Account,   ViewModel = account },
            new NavigationItem { Title = LanguageService.Format("Nav.Settings"),    Icon = Icon.Settings,           Key = NavigationKeys.Settings, ViewModel = settings },
        ];

        _navByKey = NavigationItems.ToDictionary(n => n.Key, StringComparer.Ordinal);

        // 初始页按设置选择:Home(主页,默认)/ Launcher(鸣潮启动页)
        var startKey = string.Equals(AppServices.Settings.Current.StartupPage, "Launcher", StringComparison.OrdinalIgnoreCase)
            ? NavigationKeys.Launcher
            : NavigationKeys.Home;
        _selectedNavigationItem = _navByKey.GetValueOrDefault(startKey) ?? NavigationItems[0];
        NavigateTo(_selectedNavigationItem);

        _messenger.Register<MainWindowViewModel, NavigationRequestedMessage>(this, (recipient, message) =>
        {
            if (recipient._navByKey.TryGetValue(message.Value, out var nav))
            {
                recipient.NavigateTo(nav);
            }
        });

        // 启动自动检查应用更新(冒烟模式跳过,保持冒烟无网络副作用)
        if (AppServices.Settings.Current.AppUpdateAutoCheck
            && Environment.GetEnvironmentVariable("McKuro_SMOKE") != "1")
        {
            _ = AutoCheckAppUpdateAsync();
        }
    }

    /// <summary>启动延迟自动检查:发现新版按 AutoInstall 直接静默升级,或弹窗询问。</summary>
    private async Task AutoCheckAppUpdateAsync()
    {
        try
        {
            // 延迟让启动页视频/账号头像等先走,不抢带宽与 UI
            await Task.Delay(5000);
            // 走缓存通道:用户若在 5 分钟内手动点过检查,启动检查复用结果,不重复占用匿名 API 配额
            await _settings.CheckAppUpdateCachedAsync();
            Console.Error.WriteLine($"MCKURO-UPDATE auto: available={_settings.AppUpdateAvailable} autoInstall={AppServices.Settings.Current.AppUpdateAutoInstall} status={_settings.AppUpdateStatusText}");
            if (!_settings.AppUpdateAvailable)
            {
                return;
            }
            if (AppServices.Settings.Current.AppUpdateAutoInstall)
            {
                await _settings.DownloadAppUpdateAsync();
            }
            else
            {
                AppUpdatePromptVisible = true;
            }
        }
        catch (Exception)
        {
            // 自动检查失败静默(离线/限流等):设置页手动检查仍可用
        }
    }

    /// <summary>弹窗「立即更新」:隐藏弹窗并走完整自动链(下载→替换→重启)。</summary>
    [RelayCommand]
    private async Task UpdateNowAsync()
    {
        AppUpdatePromptVisible = false;
        await _settings.DownloadAppUpdateAsync();
    }

    /// <summary>弹窗「稍后再说」:本次启动不再提示(下次启动会再问)。</summary>
    [RelayCommand]
    private void UpdateLater() => AppUpdatePromptVisible = false;

    /// <summary>弹窗「跳过此版本」:持久跳过(该版本不再提示)。</summary>
    [RelayCommand]
    private void UpdateSkip()
    {
        _settings.SkipAppUpdateCommand.Execute(null);
        AppUpdatePromptVisible = false;
    }

    /// <summary>冒烟模式(McKuro_SMOKE_NOTIF=1)示例提醒:覆盖三类不同图标配别的提醒供 UI 验证。</summary>
    private void SeedSmokeReminders()
    {
        var r = AppServices.Reminders;
        r.Raise("smoke:activity", NotificationKind.ActivityEnding,
            LanguageService.Format("Notif.ActivityTitle"),
            LanguageService.Format("Notif.ActivityMessage", "溯洄·角色唤取", "14小时", "09-26"), NavigationKeys.Activity);
        r.Raise("smoke:login", NotificationKind.AccountSession,
            LanguageService.Format("Notif.LoginKuroTitle"),
            LanguageService.Format("Notif.LoginSessionExpired", "登录已过期"), NavigationKeys.Account);
        r.Raise("smoke:liveness", NotificationKind.DailyLiveness,
            LanguageService.Format("Notif.LivenessTitle"),
            LanguageService.Format("Notif.LivenessMessage", 100), NavigationKeys.Home);
    }

    partial void OnSelectedNavigationItemChanged(NavigationItem? value)
    {
        if (value is not null)
        {
            NavigateTo(value);
        }
    }

    /// <summary>页面切换后重估悬浮提醒可见性(设置页不显示提醒卡片)。</summary>
    partial void OnCurrentPageChanged(ViewModelBase? value)
        => OnPropertyChanged(nameof(IsReminderOverlayVisible));

    public void NavigateTo(NavigationItem item)
    {
        foreach (var nav in NavigationItems)
        {
            nav.IsSelected = ReferenceEquals(nav, item);
        }
        CurrentPage = item.ViewModel;
        SelectedNavigationItem = item;
        // 页面切换留痕(诊断"某页数据不刷新/没触发重载"这类问题;stderr 非用户可见通道)
        System.Console.Error.WriteLine($"MCKURO-NAV -> {item.Key}");
        // 导航到启动页时自动检查更新(移除手动检查按钮后)
        if (item.ViewModel is LauncherViewModel launcher)
        {
            launcher.OnNavigatedTo();
        }
        // 导航到账号页时校验各接口登录态是否过期
        if (item.ViewModel is AccountViewModel account)
        {
            account.OnNavigatedTo();
        }
        // 导航到深塔/海墟页时重新拉取战绩(页面 VM 只在启动时构造一次,否则永远是启动那刻的快照)
        if (item.ViewModel is TowerViewModel tower)
        {
            tower.OnNavigatedTo();
        }
    }

    /// <summary>通过字符串 key 导航(供消息接收)。</summary>
    public void NavigateToKey(string key)
    {
        if (_navByKey.TryGetValue(key, out var nav))
        {
            NavigateTo(nav);
        }
    }
}
