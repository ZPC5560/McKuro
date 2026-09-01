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

    public List<NavigationItem> NavigationItems { get; }

    /// <summary>设置页实例(自更新状态与命令供主窗口更新弹窗绑定;子 VM 全部启动即建,天然单例)。</summary>
    public SettingsViewModel SettingsPage => _settings;

    /// <summary>发现新版本的询问弹窗(自动检查触发;AutoInstall 开启时不弹,直接升级)。</summary>
    [ObservableProperty]
    private bool _appUpdatePromptVisible;

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

        NavigationItems =
        [
            new NavigationItem { Title = "主页",        Icon = Icon.Home,               Key = NavigationKeys.Home,      ViewModel = home },
            new NavigationItem { Title = "鸣潮",        Icon = Icon.Play,               Key = NavigationKeys.Launcher, ViewModel = launcher },
            new NavigationItem { Title = "抽卡分析",    Icon = Icon.Gauge,              Key = NavigationKeys.Gacha,    ViewModel = gacha },
            new NavigationItem { Title = "角色数据",    Icon = Icon.Person,             Key = NavigationKeys.Roles,    ViewModel = roles },
            new NavigationItem { Title = "签到",        Icon = Icon.CalendarCheckmark,  Key = NavigationKeys.Sign,     ViewModel = sign },
            new NavigationItem { Title = "活动",        Icon = Icon.CalendarStar,       Key = NavigationKeys.Activity, ViewModel = activity },
            new NavigationItem { Title = "资讯",        Icon = Icon.BookOpen,           Key = NavigationKeys.Wiki,     ViewModel = wiki },
            new NavigationItem { Title = "兑换码",      Icon = Icon.TicketDiagonal,     Key = NavigationKeys.RedeemCodes, ViewModel = redeem },
            new NavigationItem { Title = "游玩统计",    Icon = Icon.Timer,              Key = NavigationKeys.PlayTime,  ViewModel = playTime },
            new NavigationItem { Title = "深塔海墟",    Icon = Icon.BuildingSkyscraper, Key = NavigationKeys.Tower,     ViewModel = tower },
            new NavigationItem { Title = "账号",        Icon = Icon.PersonCircle,       Key = NavigationKeys.Account,   ViewModel = account },
            new NavigationItem { Title = "设置",        Icon = Icon.Settings,           Key = NavigationKeys.Settings, ViewModel = settings },
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
            await _settings.CheckAppUpdateAsync();
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

    partial void OnSelectedNavigationItemChanged(NavigationItem? value)
    {
        if (value is not null)
        {
            NavigateTo(value);
        }
    }

    public void NavigateTo(NavigationItem item)
    {
        foreach (var nav in NavigationItems)
        {
            nav.IsSelected = ReferenceEquals(nav, item);
        }
        CurrentPage = item.ViewModel;
        SelectedNavigationItem = item;
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
