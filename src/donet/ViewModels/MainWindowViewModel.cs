using CommunityToolkit.Mvvm.ComponentModel;

namespace donet.ViewModels;

/// <summary>导航页条目。</summary>
public sealed class NavigationItem : ObservableObject
{
    public required string Title { get; init; }
    public required string Icon { get; init; }
    public required ViewModelBase ViewModel { get; init; }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

/// <summary>主窗口(Haiyu Shell 风格):左侧 60px 图标导航 + 内容区。</summary>
public sealed partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty]
    private ViewModelBase? _currentPage;

    [ObservableProperty]
    private NavigationItem? _selectedNavigationItem;

    public List<NavigationItem> NavigationItems { get; }

    private readonly HomeViewModel _home;
    private readonly LauncherViewModel _launcher;
    private readonly GachaViewModel _gacha;
    private readonly RolesViewModel _roles;
    private readonly ToolkitViewModel _toolkit;
    private readonly SettingsViewModel _settings;

    public MainWindowViewModel()
    {
        _home = new HomeViewModel();
        _launcher = new LauncherViewModel();
        _gacha = new GachaViewModel();
        _roles = new RolesViewModel();
        _toolkit = new ToolkitViewModel(_launcher, _gacha, _roles);
        _settings = new SettingsViewModel();
        _toolkit.NavigateRequested = NavigateTo;

        NavigationItems =
        [
            new NavigationItem { Title = "主页", Icon = "🏠", ViewModel = _home },
            new NavigationItem { Title = "鸣潮", Icon = "🌊", ViewModel = _launcher },
            new NavigationItem { Title = "抽卡分析", Icon = "🎴", ViewModel = _gacha },
            new NavigationItem { Title = "角色数据", Icon = "👤", ViewModel = _roles },
            new NavigationItem { Title = "工具箱", Icon = "🧰", ViewModel = _toolkit },
            new NavigationItem { Title = "设置", Icon = "⚙️", ViewModel = _settings },
        ];

        _selectedNavigationItem = NavigationItems[0];
        NavigateTo(NavigationItems[0]);
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
        // 匹配导航列表中的同标题项(工具箱传入的可能是新实例)
        var matched = NavigationItems.FirstOrDefault(n => n.Title == item.Title) ?? item;
        foreach (var nav in NavigationItems)
        {
            nav.IsSelected = ReferenceEquals(nav, matched);
        }
        CurrentPage = matched.ViewModel;
        SelectedNavigationItem = matched;
    }
}
