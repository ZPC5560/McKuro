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

/// <summary>主窗口:左侧导航 + 内容区。</summary>
public sealed partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty]
    private ViewModelBase? _currentPage;

    [ObservableProperty]
    private NavigationItem? _selectedNavigationItem;

    public List<NavigationItem> NavigationItems { get; }

    private readonly LauncherViewModel _launcher;
    private readonly GachaViewModel _gacha;
    private readonly RolesViewModel _roles;
    private readonly SettingsViewModel _settings;

    public MainWindowViewModel()
    {
        _launcher = new LauncherViewModel();
        _gacha = new GachaViewModel();
        _roles = new RolesViewModel();
        _settings = new SettingsViewModel();

        NavigationItems =
        [
            new NavigationItem { Title = "启动器", Icon = "home", ViewModel = _launcher },
            new NavigationItem { Title = "抽卡分析", Icon = "gacha", ViewModel = _gacha },
            new NavigationItem { Title = "角色养成", Icon = "roles", ViewModel = _roles },
            new NavigationItem { Title = "设置", Icon = "settings", ViewModel = _settings },
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
        foreach (var nav in NavigationItems)
        {
            nav.IsSelected = ReferenceEquals(nav, item);
        }
        CurrentPage = item.ViewModel;
    }
}
