using CommunityToolkit.Mvvm.ComponentModel;
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

    public List<NavigationItem> NavigationItems { get; }

    private readonly Dictionary<string, NavigationItem> _navByKey;
    private readonly IMessenger _messenger;

    public MainWindowViewModel() : this(WeakReferenceMessenger.Default)
    {
    }

    public MainWindowViewModel(IMessenger messenger)
    {
        _messenger = messenger;

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
        var settings = new SettingsViewModel();

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

        _selectedNavigationItem = NavigationItems[0];
        NavigateTo(_selectedNavigationItem);

        _messenger.Register<MainWindowViewModel, NavigationRequestedMessage>(this, (recipient, message) =>
        {
            if (recipient._navByKey.TryGetValue(message.Value, out var nav))
            {
                recipient.NavigateTo(nav);
            }
        });
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
