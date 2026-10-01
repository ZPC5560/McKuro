using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FluentIcons.Common;

namespace McKuro.ViewModels;

/// <summary>设置页分类/子标签的稳定 Key(面板判定与测试断言共用,避免散落字面量)。</summary>
public static class SettingsCategoryKeys
{
    // ---- 一级分类 ----
    public const string Appearance = "appearance";
    public const string Live2D = "live2d";
    public const string Game = "game";
    public const string Notifications = "notifications";
    public const string Download = "download";
    public const string About = "about";

    // ---- 二级子标签 ----
    public const string AppearanceInterface = "interface";
    public const string AppearanceVideo = "video";
    public const string GameDir = "dir";
    public const string GameLaunch = "launch";
    public const string GameRepair = "repair";
    public const string AboutPlatform = "platform";
    public const string AboutUpdate = "update";

    /// <summary>单子标签容器(无二级的类目)固定用的 key。</summary>
    public const string Single = "main";
}

/// <summary>
/// 设置页二级子标签(左侧竖排子导航的一项)。
/// <para>
/// 选中态由 ViewModel 显式维护,配合 XAML 的 <c>Classes.selected</c> 条件类渲染高亮;
/// 不用 ListBox.SelectedItem 双向绑定 —— 切换一级分类会整体替换子标签集合,
/// 绑定在重绑定瞬间会把 null 回写,导致"切完分类左列没有高亮"。
/// </para>
/// </summary>
public sealed partial class SettingsSubTabItem : ObservableObject
{
    public required string Key { get; init; }
    public required string Title { get; init; }

    private bool _isSelected;

    /// <summary>是否选中(驱动 Classes.selected)。</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

/// <summary>
/// 设置页一级分类(顶部横向菜单的一项)。
/// <para>
/// 只有 <see cref="SubTabs"/> 多于一项时才渲染左侧子导航(单子标签容器等于没有二级,
/// 此时内容区直接铺满,不多占一列宽度)。
/// </para>
/// </summary>
public sealed partial class SettingsCategoryItem : ObservableObject
{
    public required string Key { get; init; }
    public required string Title { get; init; }
    public required Icon Icon { get; init; }

    /// <summary>二级子标签(至少一项:无二级的类目放一个 Single)。</summary>
    public ObservableCollection<SettingsSubTabItem> SubTabs { get; } = [];

    /// <summary>是否显示左侧二级子导航(多于一项目标标签时才有意义)。</summary>
    public bool HasSubNav => SubTabs.Count > 1;

    private SettingsSubTabItem? _selectedSubTab;

    /// <summary>
    /// 当前选中的二级子标签。显式忽略 null 写回:切换一级分类时子导航数据源被替换,
    /// 绑定会在重绑定瞬间回写空值;忽略后不会丢失高亮。内容面板始终以
    /// <see cref="ActivePanelKey"/> 兜底,因此忽略 null 也不会让内容区变空白。
    /// </summary>
    public SettingsSubTabItem? SelectedSubTab
    {
        get => _selectedSubTab;
        set
        {
            if (value is null)
            {
                return;
            }
            SetProperty(ref _selectedSubTab, value);
        }
    }

    private bool _isSelected;

    /// <summary>是否为当前一级分类(驱动 Classes.selected)。</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    /// <summary>当前面板 Key(选中为空时回退第一项,保证内容区不空白)。</summary>
    public string ActivePanelKey => _selectedSubTab?.Key ?? SubTabs.FirstOrDefault()?.Key ?? "";
}
