using McKuro.Core.Services.Notification;
using McKuro.ViewModels;

namespace McKuro.Tests;

/// <summary>
/// 提醒卡片「前往」的页内定位测试。
/// <para>
/// 回归背景:深塔 / 终焉矩阵 / 冥歌海墟各自是一条提醒,但三者共用塔页(靠页签区分)。
/// 提醒只带页面 key <c>Tower</c>,于是点「前往」永远落在默认的「逆境深塔」页签 ——
/// 用户收到「终焉矩阵即将刷新」,点过去看到的却是深塔。现在 key 带页内后缀(<c>Tower#1</c>)。
/// </para>
/// <para>
/// 页签序号是页面结构与提醒判定的<b>跨层契约</b>(TowerView.axaml 的 TabItem 顺序 ↔
/// TowerReminderPolicy.TabIndexOf),任一侧改了另一侧没跟,就会跳到错误的页签。
/// </para>
/// </summary>
public class TowerReminderNavigationTests
{
    // ---------- key 拼装 ↔ 解析 ----------

    [Theory]
    [InlineData(TowerKind.DeepTower, 0)]
    [InlineData(TowerKind.Matrix, 1)]
    [InlineData(TowerKind.Slash, 2)]
    public void TowerNavKey_Carries_PlayKind_TabIndex(TowerKind kind, int expectedTab)
    {
        var key = TowerReminderPolicy.TowerNavKey(kind);
        Assert.Equal($"Tower#{expectedTab}", key);

        var (pageKey, tab) = NavigationKeys.ParseTowerTab(key);
        Assert.Equal(NavigationKeys.Tower, pageKey);
        Assert.Equal(expectedTab, tab);
    }

    /// <summary>Core 层与 UI 层的常量必须同值(两处独立定义,靠本测试防漂移)。</summary>
    [Fact]
    public void Core_Constants_Match_Ui_NavigationKeys()
    {
        Assert.Equal(NavigationKeys.Tower, TowerReminderPolicy.TowerPageKey);
        Assert.Equal(NavigationKeys.TabSeparator, TowerReminderPolicy.TabSeparator);
    }

    [Fact]
    public void ParseTowerTab_Without_Suffix_Keeps_Current_Tab()
    {
        var (pageKey, tab) = NavigationKeys.ParseTowerTab(NavigationKeys.Tower);
        Assert.Equal(NavigationKeys.Tower, pageKey);
        Assert.Null(tab);   // null = 不动页签(其它页面的常见情形)
    }

    [Fact]
    public void ParseTowerTab_Other_Page_Keys_Pass_Through()
    {
        foreach (var key in new[] { NavigationKeys.Home, NavigationKeys.Activity, NavigationKeys.Account })
        {
            var (pageKey, tab) = NavigationKeys.ParseTowerTab(key);
            Assert.Equal(key, pageKey);
            Assert.Null(tab);
        }
    }

    [Theory]
    [InlineData("Tower#")]      // 空后缀
    [InlineData("Tower#abc")]   // 非数字
    [InlineData("Tower#-1")]    // 负数(不匹配 None 样式)
    [InlineData("#1")]          // 缺页面 key
    public void ParseTowerTab_Malformed_Suffix_Falls_Back_To_Plain_Key(string key)
    {
        // 畸形后缀不做定位,整串当页面 key 返回:宁可跳到页面,也不要点了毫无反应
        var (pageKey, tab) = NavigationKeys.ParseTowerTab(key);
        Assert.Equal(key, pageKey);
        Assert.Null(tab);
    }

    [Fact]
    public void ParseTowerTab_Handles_Null_And_Empty()
    {
        Assert.Equal(("", (int?)null), NavigationKeys.ParseTowerTab(null));
        Assert.Equal(("", (int?)null), NavigationKeys.ParseTowerTab(""));
    }

    // ---------- 映射与页面页签的一致性 ----------

    /// <summary>
    /// 提醒映射出的页签必须落在塔页页签范围内,且与 <c>TowerView.axaml</c> 的顺序一致:
    /// 0=逆境深塔、1=终焉矩阵、2=冥歌海墟。
    /// </summary>
    [Fact]
    public void TabIndex_Map_Matches_TowerView_Tab_Order()
    {
        Assert.Equal(TowerViewModel.TabDeepTower, TowerReminderPolicy.TabIndexOf(TowerKind.DeepTower));
        Assert.Equal(TowerViewModel.TabMatrix, TowerReminderPolicy.TabIndexOf(TowerKind.Matrix));
        Assert.Equal(TowerViewModel.TabSlash, TowerReminderPolicy.TabIndexOf(TowerKind.Slash));

        Assert.Equal(0, TowerViewModel.TabDeepTower);
        Assert.Equal(1, TowerViewModel.TabMatrix);
        Assert.Equal(2, TowerViewModel.TabSlash);
    }

    [Theory]
    [InlineData(TowerKind.DeepTower)]
    [InlineData(TowerKind.Matrix)]
    [InlineData(TowerKind.Slash)]
    public void TabIndex_Is_Within_TowerTabCount(TowerKind kind)
    {
        var tab = TowerReminderPolicy.TabIndexOf(kind);
        Assert.InRange(tab, 0, TowerViewModel.TowerTabCount - 1);
    }
}
