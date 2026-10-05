using McKuro.Core.Services.User;
using McKuro.ViewModels;

namespace McKuro.Tests;

/// <summary>
/// 首页账号切换弹窗的展示模型(不依赖 DI 容器):
/// 选中互斥、空态判定、列表项文本格式。
/// </summary>
public sealed class AccountSwitchSelectionTests
{
    private static HomeAccountOption Option(string key, string display = "x") =>
        new() { Key = key, Display = display };

    [Fact]
    public void Constructor_MarksMatchingOptionSelected()
    {
        var auto = Option("");
        var a = Option("111|103242935");
        var b = Option("222|103050791");

        var selection = new AccountSwitchSelection([auto, a, b], "222|103050791");

        Assert.True(b.IsSelected);
        Assert.False(auto.IsSelected);
        Assert.False(a.IsSelected);
        Assert.Same(b, selection.Selected);
    }

    [Fact]
    public void Constructor_UnknownBoundKey_FallsBackToFirstOption()
    {
        // 绑定的角色已不在本地凭证里(游戏里登出/换绑):弹窗不能出现"无选中项"
        var auto = Option("");
        var a = Option("111|1");

        var selection = new AccountSwitchSelection([auto, a], "ghost|999");

        Assert.Same(auto, selection.Selected);
        Assert.True(auto.IsSelected);
        Assert.False(a.IsSelected);
    }

    [Fact]
    public void Select_IsMutuallyExclusive()
    {
        var auto = Option("");
        var a = Option("111|1");
        var b = Option("222|2");
        var selection = new AccountSwitchSelection([auto, a, b], "");

        selection.Select(b);

        Assert.True(b.IsSelected);
        Assert.False(auto.IsSelected);
        Assert.False(a.IsSelected);
        Assert.Same(b, selection.Selected);
    }

    [Fact]
    public void Select_Null_IsNoOp()
    {
        var auto = Option("");
        var selection = new AccountSwitchSelection([auto], "");

        selection.Select(null);

        Assert.True(auto.IsSelected);
        Assert.Same(auto, selection.Selected);
    }

    [Fact]
    public void HasChoice_RequiresMoreThanAuto()
    {
        Assert.False(new AccountSwitchSelection([Option("")], "").HasChoice);
        Assert.True(new AccountSwitchSelection([Option(""), Option("111|1")], "").HasChoice);
    }

    [Fact]
    public void Rebuild_ForRefresh_PreservesUserPendingSelection()
    {
        // 弹窗「刷新」重建 selection 时,用户刚点选但尚未确定的那一项必须仍是选中态,
        // 否则刷新会把用户的选择悄悄弹回持久化的绑定值
        var rebuilt = new AccountSwitchSelection([Option(""), Option("111|1"), Option("222|2")], "222|2");

        Assert.Equal("222|2", rebuilt.Selected?.Key);
    }

    [Fact]
    public void Rebuild_ForRefresh_WhenPendingKeyVanished_FallsBackToFirst()
    {
        // 刷新后该账号已从本地凭证消失(游戏里登出):不能出现"无选中项"
        var rebuilt = new AccountSwitchSelection([Option(""), Option("111|1")], "ghost|999");

        Assert.Same(rebuilt.Options[0], rebuilt.Selected);
    }

    private static LocalLauncherPlayer Player(string uid = "526781653", string username = "U526781653A",
        string roleId = "103242935", string roleName = "以椿为鸣") =>
        new() { KuroUid = uid, Username = username, RoleId = roleId, RoleName = roleName, ServerName = "China" };

    [Fact]
    public void BuildAccountDisplay_MatchesHaiyuCardText()
    {
        Assert.Equal("China - U526781653A - 以椿为鸣", HomeViewModel.BuildAccountDisplay(Player()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildAccountDisplay_WithoutRoleName_OmitsTrailingSegment(string? roleName)
    {
        var player = Player(roleName: roleName ?? "");

        Assert.Equal("China - U526781653A", HomeViewModel.BuildAccountDisplay(player));
    }

    [Fact]
    public void BuildAccountDisplay_WithoutUsername_FallsBackToNumericUid()
    {
        // 缓存里 username 缺失时退回数字 UID,不能显示成 "China -  - 昵称"
        var player = Player(username: "");

        Assert.Equal("China - 526781653 - 以椿为鸣", HomeViewModel.BuildAccountDisplay(player));
    }

    [Fact]
    public void BindKey_CarriesBothIdentities()
    {
        // 同一库街区账号可能有多个服务器角色:绑定键必须带角色 UID,否则换绑会选错角色
        Assert.Equal("526781653|103242935", Player().BindKey);
    }

    [Fact]
    public void HomeAccountOption_HasRoleId_TracksRoleIdText()
    {
        var option = Option("111|1");
        Assert.False(option.HasRoleId);

        option.RoleIdText = "103242935";
        Assert.True(option.HasRoleId);

        option.RoleIdText = "  ";
        Assert.False(option.HasRoleId);
    }

    [Fact]
    public void HomeAccountOption_RaisesPropertyChanged()
    {
        var option = Option("111|1");
        var raised = new List<string>();
        option.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        option.Display = "China - U1 - 新昵称";
        option.RoleIdText = "103242935";
        option.IsSelected = true;

        Assert.Contains(nameof(HomeAccountOption.Display), raised);
        Assert.Contains(nameof(HomeAccountOption.RoleIdText), raised);
        Assert.Contains(nameof(HomeAccountOption.HasRoleId), raised);
        Assert.Contains(nameof(HomeAccountOption.IsSelected), raised);
    }

    // ---------- 刷新反馈(曾完全静默:用户点了不知道成没成) ----------

    [Fact]
    public void ClassifyRefresh_NoChange_ReportsUnchanged()
    {
        Assert.Equal(AccountRefreshOutcome.Unchanged,
            HomeViewModel.ClassifyRefresh(fresh: true, ["526781653|103242935"], ["526781653|103242935"]));
    }

    [Fact]
    public void ClassifyRefresh_NewAccount_ReportsAdded()
    {
        Assert.Equal(AccountRefreshOutcome.Added,
            HomeViewModel.ClassifyRefresh(fresh: true, ["526781653|1"], ["526781653|1", "92500864|2"]));
    }

    [Fact]
    public void ClassifyRefresh_RemovedAccount_ReportsAdded()
    {
        // 游戏里登出某账号 → 列表变短也算"有变化",不能报无变化
        Assert.Equal(AccountRefreshOutcome.Added,
            HomeViewModel.ClassifyRefresh(fresh: true, ["a|1", "b|2"], ["a|1"]));
    }

    [Fact]
    public void ClassifyRefresh_SameCountButDifferentIds_ReportsAdded()
    {
        // 换号场景:数量相同但角色 UID 不同,必须被判为有变化
        Assert.Equal(AccountRefreshOutcome.Added,
            HomeViewModel.ClassifyRefresh(fresh: true, ["a|1"], ["a|999"]));
    }

    [Fact]
    public void ClassifyRefresh_NotFresh_ReportsFailedEvenWhenUnchanged()
    {
        // 核心回归:本次枚举没成功(沿用旧数据)时,内容必然"与刷新前一致",
        // 若据此报"无变化"就是假消息(接口被限流/未装启动器时用户会被误导)
        Assert.Equal(AccountRefreshOutcome.Failed,
            HomeViewModel.ClassifyRefresh(fresh: false, ["526781653|103242935"], ["526781653|103242935"]));
    }

    [Fact]
    public void ClassifyRefresh_NotFreshWithEmptyLists_ReportsFailed()
    {
        Assert.Equal(AccountRefreshOutcome.Failed,
            HomeViewModel.ClassifyRefresh(fresh: false, [], []));
    }

    [Fact]
    public void RefreshOutcome_MapsToDistinctStatusText_AndWarnFlag()
    {
        var selection = new AccountSwitchSelection([Option("")], "");

        Assert.False(selection.HasRefreshStatus); // 未刷新时不占位

        selection.ApplyRefreshOutcome(AccountRefreshOutcome.Unchanged);
        var unchanged = selection.RefreshStatus;
        Assert.True(selection.HasRefreshStatus);
        Assert.False(selection.RefreshFailed);
        Assert.True(selection.ShowRefreshOk);
        Assert.False(selection.ShowRefreshFailed);

        selection.ApplyRefreshOutcome(AccountRefreshOutcome.Failed);
        Assert.True(selection.RefreshFailed); // 失败必须带警示标记
        Assert.True(selection.ShowRefreshFailed);
        Assert.False(selection.ShowRefreshOk);
        Assert.NotEqual(unchanged, selection.RefreshStatus); // 两种结果文案必须不同
    }

    [Fact]
    public void ApplyRefreshOutcome_RaisesVisibilityNotifications()
    {
        var selection = new AccountSwitchSelection([Option("")], "");
        var raised = new List<string>();
        selection.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        selection.ApplyRefreshOutcome(AccountRefreshOutcome.Failed);

        Assert.Contains(nameof(AccountSwitchSelection.HasRefreshStatus), raised);
        Assert.Contains(nameof(AccountSwitchSelection.ShowRefreshFailed), raised);
        Assert.Contains(nameof(AccountSwitchSelection.ShowRefreshOk), raised);
    }

    [Fact]
    public void IsRefreshing_RaisesPropertyChanged_ForButtonDisable()
    {
        var selection = new AccountSwitchSelection([Option("")], "");
        var raised = new List<string>();
        selection.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        selection.IsRefreshing = true;

        Assert.Contains(nameof(AccountSwitchSelection.IsRefreshing), raised);
    }

    // ---------- 「清除已保存的绑定值」的边界(评审:限流时误清 = 数据丢失) ----------

    /// <summary>
    /// 回归:凭证确实已从启动器移除(登出/换绑)且列表已加载 → 允许清空绑定并回退«自动»。
    /// </summary>
    [Fact]
    public void ShouldClearBinding_RemovedCredential_WithLoadedList_Clears()
    {
        Assert.True(HomeViewModel.ShouldClearBinding("526781653|103242935", localPlayerCount: 2, credentialGone: true));
    }

    /// <summary>
    /// 回归(核心数据丢失路径):列表非空但<b>没能确认</b>凭证已消失时必须保留绑定。
    /// <para>
    /// 单凭证 queryPlayerInfo 失败(1005 限流/瞬时错误)会被静默跳过、其余账号照常返回,
    /// 于是"列表里没有这个账号"既可能是真登出,也可能只是这次没查到 —— 后者若清空,
    /// 用户选好的账号就被无声抹掉了。
    /// </para>
    /// </summary>
    [Fact]
    public void ShouldClearBinding_CredentialNotConfirmedGone_KeepsBinding()
    {
        Assert.False(HomeViewModel.ShouldClearBinding("526781653|103242935", localPlayerCount: 2, credentialGone: false));
    }

    /// <summary>列表还没加载完(首次进入)时不能清 —— 否则会把用户的绑定误删。</summary>
    [Fact]
    public void ShouldClearBinding_EmptyList_KeepsBinding()
    {
        Assert.False(HomeViewModel.ShouldClearBinding("526781653|103242935", localPlayerCount: 0, credentialGone: true));
    }

    /// <summary>未处于绑定态(«自动»)时无需清理。</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ShouldClearBinding_NoBinding_DoesNotClear(string? bound)
    {
        Assert.False(HomeViewModel.ShouldClearBinding(bound, localPlayerCount: 3, credentialGone: true));
    }

    /// <summary>
    /// 回归(评测中自查发现的自身缺陷):"凭证已消失"的结论是针对<b>某个具体键</b>算出来的。
    /// 用户刚选定新账号(ApplyAccountSelection → RefreshDailyAsync → RefreshState →
    /// RebuildHomeAccountOptions)会复用同一 VM,若把旧键的结论套用到新键上,
    /// 就会出现"刚选好立刻被清掉"。
    /// <para>
    /// <b>本测试直接驱动真正的守卫</b> <see cref="HomeViewModel.CredentialGoneFor"/> ——
    /// 早先版本自己算了一遍键比较再断言纯函数 <c>ShouldClearBinding</c>,
    /// 结果把生产代码里的键核对整段删掉测试依然全绿(等于没保护),已修正。
    /// </para>
    /// </summary>
    [Fact]
    public void CredentialGoneFor_StaleVerdictForAnotherKey_IsNotAppliedToNewKey()
    {
        const string oldKey = "526781653|103242935";
        const string newKey = "92500864|555";

        // 旧键被判为"已消失"
        Assert.True(HomeViewModel.CredentialGoneFor(oldKey, gone: true, verdictKey: oldKey));

        // 关键:新键不得继承旧键的结论(这正是"刚选好就被清掉"的防线)
        Assert.False(HomeViewModel.CredentialGoneFor(newKey, gone: true, verdictKey: oldKey));
    }

    /// <summary>判定为"未消失"(限流/无法判定)时,即便键一致也不得触发清除。</summary>
    [Fact]
    public void CredentialGoneFor_NotGone_IsFalseEvenForSameKey()
    {
        const string key = "526781653|103242935";
        Assert.False(HomeViewModel.CredentialGoneFor(key, gone: false, verdictKey: key));
    }

    /// <summary>空/缺失的绑定值无论如何都不算"已消失"(避免误判为已绑定)。</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void CredentialGoneFor_NoBoundValue_IsFalse(string? bound)
    {
        Assert.False(HomeViewModel.CredentialGoneFor(bound, gone: true, verdictKey: bound));
    }

    /// <summary>
    /// 端到端串起来:旧键结论 + 新键当前绑定 ⇒ 不得清除(两步合起来才是完整防线);
    /// 对照:当前绑定的正是被判定的那个键 ⇒ 应清除。
    /// </summary>
    [Fact]
    public void ShouldClearBinding_StaleVerdictForAnotherKey_MustNotClearNewBinding()
    {
        const string oldKey = "526781653|103242935";
        const string newKey = "92500864|555";

        // 旧键判定为已消失,但用户已改绑到新键 → 新键不得被清
        var goneForNewKey = HomeViewModel.CredentialGoneFor(newKey, gone: true, verdictKey: oldKey);
        Assert.False(HomeViewModel.ShouldClearBinding(newKey, localPlayerCount: 2, credentialGone: goneForNewKey));

        // 对照:绑定的正是被判定的键 → 应清除
        var goneForOldKey = HomeViewModel.CredentialGoneFor(oldKey, gone: true, verdictKey: oldKey);
        Assert.True(HomeViewModel.ShouldClearBinding(oldKey, localPlayerCount: 2, credentialGone: goneForOldKey));
    }
}
