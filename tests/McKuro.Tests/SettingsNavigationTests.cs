using McKuro.Core.Services.Settings;
using McKuro.ViewModels;

namespace McKuro.Tests;

/// <summary>
/// 设置页导航模型测试(横向一级分类 + 二级子标签 + 面板可见性)。
/// <para>
/// 面板可见性只由 <see cref="SettingsViewModel.IsCategoryPanelActive"/> 这一套规则决定,
/// XAML 里的十个 IsVisible 全部派生自它。这里覆盖"同时只能有一个面板可见"这一核心不变量 ——
/// 它一旦被破坏,表现为设置页同时叠出两块内容或整片空白。
/// </para>
/// </summary>
public class SettingsNavigationTests : IDisposable
{
    private readonly SettingsViewModel _vm;
    private readonly bool _originalNotifEnabled;

    public SettingsNavigationTests()
    {
        // SettingsViewModel 构造期即读写 AppServices.Settings / Downloader,必须先初始化 DI。
        InitializeServices();
        _originalNotifEnabled = McKuro.Services.AppServices.Settings.Current.NotifEnabled;
        _vm = new SettingsViewModel();
    }

    public void Dispose()
    {
        // AppServices 是进程级单例:恢复被本测试改动过的总开关,
        // 否则同一进程内后续测试的 SettingsViewModel 会读到上个测试留下的值(顺序相关失败)。
        McKuro.Services.AppServices.Settings.Current.NotifEnabled = _originalNotifEnabled;
    }

    private static void InitializeServices()
    {
        // AppServices.Initialize 只跑一次(进程级静态)。数据目录必须带进程 id:
        // 用固定路径会让上一次测试运行落盘的 settings.json 被下一次运行读进来,
        // 造成"同一测试在两次运行中结果不同"的假失败。
        var dataDir = Path.Combine(
            Path.GetTempPath(),
            $"mckuro-nav-{Environment.ProcessId}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dataDir);
        try
        {
            McKuro.Services.AppServices.Initialize(dataDir, Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
        }
        catch (InvalidOperationException)
        {
            // 已被同进程内其他测试初始化:直接复用(此时数据目录不可控,故断言不依赖落盘路径)
        }
    }

    /// <summary>全部内容面板的可见性快照(顺序与 XAML 中的十个面板一致)。</summary>
    private (string Name, bool Visible)[] Panels() =>
    [
        (nameof(SettingsViewModel.IsAppearanceInterfaceActive), _vm.IsAppearanceInterfaceActive),
        (nameof(SettingsViewModel.IsAppearanceVideoActive), _vm.IsAppearanceVideoActive),
        (nameof(SettingsViewModel.IsLive2DActive), _vm.IsLive2DActive),
        (nameof(SettingsViewModel.IsGameDirActive), _vm.IsGameDirActive),
        (nameof(SettingsViewModel.IsGameLaunchActive), _vm.IsGameLaunchActive),
        (nameof(SettingsViewModel.IsGameRepairActive), _vm.IsGameRepairActive),
        (nameof(SettingsViewModel.IsNotificationsActive), _vm.IsNotificationsActive),
        (nameof(SettingsViewModel.IsDownloadActive), _vm.IsDownloadActive),
        (nameof(SettingsViewModel.IsAboutPlatformActive), _vm.IsAboutPlatformActive),
        (nameof(SettingsViewModel.IsAboutUpdateActive), _vm.IsAboutUpdateActive),
    ];

    private void AssertExactlyOnePanelVisible(string expected)
    {
        var visible = Panels().Where(p => p.Visible).ToList();
        Assert.Single(visible);
        Assert.Equal(expected, visible[0].Name);
    }

    [Fact]
    public void Categories_Are_Six_With_Expected_Keys()
    {
        Assert.Equal(6, _vm.Categories.Count);
        Assert.Equal(
            [
                SettingsCategoryKeys.Appearance, SettingsCategoryKeys.Live2D, SettingsCategoryKeys.Game,
                SettingsCategoryKeys.Notifications, SettingsCategoryKeys.Download, SettingsCategoryKeys.About,
            ],
            _vm.Categories.Select(c => c.Key));
    }

    [Fact]
    public void Categories_All_Have_Titles()
    {
        // 文案取自 LanguageService:键名写错会退化成键本身,这里拦住空/未解析的标题
        Assert.All(_vm.Categories, c =>
        {
            Assert.False(string.IsNullOrWhiteSpace(c.Title), $"分类 {c.Key} 标题为空");
            Assert.DoesNotContain("Section.", c.Title);
        });
    }

    [Fact]
    public void Starts_On_First_Category_With_Only_Its_Panel_Visible()
    {
        Assert.NotNull(_vm.SelectedCategory);
        Assert.Equal(SettingsCategoryKeys.Appearance, _vm.SelectedCategory!.Key);
        Assert.True(_vm.SelectedCategory.IsSelected);
        AssertExactlyOnePanelVisible(nameof(SettingsViewModel.IsAppearanceInterfaceActive));
    }

    [Fact]
    public void Every_Category_Selection_Shows_Exactly_One_Panel()
    {
        // 逐个分类切过去:每个分类都必须落地到某个可见面板(不能出现"切过去一片空白")
        foreach (var category in _vm.Categories)
        {
            _vm.SelectCategoryCommand.Execute(category);
            Assert.Equal(category.Key, _vm.SelectedCategory!.Key);
            Assert.Single(Panels(), p => p.Visible);
            Assert.True(category.IsSelected);
            Assert.All(_vm.Categories.Where(c => !ReferenceEquals(c, category)), c => Assert.False(c.IsSelected));
        }
    }

    [Theory]
    [InlineData(SettingsCategoryKeys.Game, SettingsCategoryKeys.GameDir, nameof(SettingsViewModel.IsGameDirActive))]
    [InlineData(SettingsCategoryKeys.Game, SettingsCategoryKeys.GameLaunch, nameof(SettingsViewModel.IsGameLaunchActive))]
    [InlineData(SettingsCategoryKeys.Game, SettingsCategoryKeys.GameRepair, nameof(SettingsViewModel.IsGameRepairActive))]
    [InlineData(SettingsCategoryKeys.About, SettingsCategoryKeys.AboutPlatform, nameof(SettingsViewModel.IsAboutPlatformActive))]
    [InlineData(SettingsCategoryKeys.About, SettingsCategoryKeys.AboutUpdate, nameof(SettingsViewModel.IsAboutUpdateActive))]
    public void SubTab_Selection_Switches_To_That_Panel(string categoryKey, string subKey, string expectedPanel)
    {
        var category = _vm.Categories.Single(c => c.Key == categoryKey);
        _vm.SelectCategoryCommand.Execute(category);
        _vm.SelectSubTabCommand.Execute(subKey);

        Assert.Equal(subKey, _vm.SelectedCategory!.ActivePanelKey);
        AssertExactlyOnePanelVisible(expectedPanel);
    }

    [Fact]
    public void ShowSubNav_Only_For_Categories_With_Multiple_SubTabs()
    {
        // 多子项的分类(外观/游戏/关于)显示左列;无二级的(通知/下载/Live2D)内容区铺满
        foreach (var category in _vm.Categories)
        {
            _vm.SelectCategoryCommand.Execute(category);
            Assert.Equal(category.HasSubNav, _vm.ShowSubNav);
            Assert.Equal(category.HasSubNav, category.SubTabs.Count > 1);
        }
    }

    [Fact]
    public void SubNav_Preserves_Selection_When_Returning_To_A_Category()
    {
        var game = _vm.Categories.Single(c => c.Key == SettingsCategoryKeys.Game);
        _vm.SelectCategoryCommand.Execute(game);
        _vm.SelectSubTabCommand.Execute(SettingsCategoryKeys.GameRepair);
        Assert.Equal(SettingsCategoryKeys.GameRepair, game.SelectedSubTab!.Key);

        // 切到别的分类再回来:应回到上次的子标签,而不是重置到第一个
        _vm.SelectCategoryCommand.Execute(_vm.Categories.Single(c => c.Key == SettingsCategoryKeys.Download));
        _vm.SelectCategoryCommand.Execute(game);
        Assert.Equal(SettingsCategoryKeys.GameRepair, game.SelectedSubTab!.Key);
        AssertExactlyOnePanelVisible(nameof(SettingsViewModel.IsGameRepairActive));
    }

    [Fact]
    public void SubTab_Selection_Is_Tracked_For_Highlighting()
    {
        var game = _vm.Categories.Single(c => c.Key == SettingsCategoryKeys.Game);
        _vm.SelectCategoryCommand.Execute(game);
        _vm.SelectSubTabCommand.Execute(SettingsCategoryKeys.GameRepair);

        Assert.All(game.SubTabs, tab =>
            Assert.Equal(tab.Key == SettingsCategoryKeys.GameRepair, tab.IsSelected));
    }

    [Fact]
    public void Selections_Replace_Rather_Than_Accumulate_In_SubNav()
    {
        var about = _vm.Categories.Single(c => c.Key == SettingsCategoryKeys.About);
        _vm.SelectCategoryCommand.Execute(about);
        Assert.Equal(2, _vm.CurrentSubTabs.Count);   // 外观残留的子标签不应留在左列
        Assert.All(_vm.CurrentSubTabs, t => Assert.Contains(t, about.SubTabs));
    }

    [Fact]
    public void Unknown_SubTab_Key_Is_Ignored()
    {
        var game = _vm.Categories.Single(c => c.Key == SettingsCategoryKeys.Game);
        _vm.SelectCategoryCommand.Execute(game);
        var before = _vm.SelectedCategory!.ActivePanelKey;

        _vm.SelectSubTabCommand.Execute("does-not-exist");
        Assert.Equal(before, _vm.SelectedCategory!.ActivePanelKey);
    }

    [Fact]
    public void Notification_MasterSwitch_Defaults_On()
    {
        // 通知分类是"总开关打开后才展开二级"的形态:默认必须是开的,否则二级菜单默认不可见。
        // (持久化往返由 SettingsServiceTests/NotificationPreferencesTests 用独立目录覆盖)
        Assert.True(new AppSettings().NotifEnabled);
    }

    [Fact]
    public void Notification_MasterSwitch_Toggle_Writes_Through_To_Settings()
    {
        _vm.NotifEnabled = false;
        Assert.False(McKuro.Services.AppServices.Settings.Current.NotifEnabled);

        _vm.NotifEnabled = true;
        Assert.True(McKuro.Services.AppServices.Settings.Current.NotifEnabled);
    }

    [Fact]
    public void Notification_Category_Keys_Match_Panel_Keys()
    {
        // 通知分类没有子标签:它的二级以"就地展开"呈现,面板键固定为 Single
        var notif = _vm.Categories.Single(c => c.Key == SettingsCategoryKeys.Notifications);
        Assert.False(notif.HasSubNav);
        Assert.Equal(SettingsCategoryKeys.Single, notif.ActivePanelKey);

        _vm.SelectCategoryCommand.Execute(notif);
        AssertExactlyOnePanelVisible(nameof(SettingsViewModel.IsNotificationsActive));
    }
}
