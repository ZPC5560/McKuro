using CommunityToolkit.Mvvm.ComponentModel;

namespace McKuro.ViewModels;

/// <summary>
/// 账号页接口账号列表条目(库街区 / 云鸣潮两列多账号列表共用)。
/// <para>
/// 展示口径与首页账号卡片、抽卡页 player_id 一致:游戏角色昵称 · 游戏角色 UID;
/// 匹配不到本地角色时退回接口账号文案,不编造游戏角色 ID。
/// </para>
/// <para>
/// 两个独立状态:<see cref="IsCurrent"/> 是接口的「当前账号」(服务层状态,「当前」徽标);
/// <see cref="IsSelected"/> 是用户<b>点选</b>的行(退出登录的目标)。
/// 多账号时「退出登录」必须先点选一行才可用 —— 防止误退默认账号;
/// 单账号无需点选,直接可退。点行同时会切换当前账号(既有语义)。
/// </para>
/// </summary>
public sealed partial class InterfaceAccountItem : ObservableObject
{
    /// <summary>主行文案(游戏角色昵称 · 游戏角色 UID,或接口账号回退文案)。</summary>
    public required string Display { get; init; }

    /// <summary>次行小字(掩码手机号等辅助信息;空则不显示)。</summary>
    public string SubText { get; init; } = "";

    public bool HasSubText => SubText.Length > 0;

    /// <summary>登录状态点(绿=正常,橙=会话失效,灰=未登录;当前仅库街区列表逐账号判失效)。</summary>
    public required InterfaceLoginState State { get; init; }

    /// <summary>是否为该接口的当前账号(「当前」徽标;随服务层状态,用户不可直接控制)。</summary>
    [ObservableProperty]
    private bool _isCurrent;

    /// <summary>是否被用户点选为「退出登录」的目标(强调色高亮;点列表行置位)。</summary>
    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>
/// 「退出登录」按钮的多账号门禁(纯函数,便于单测锁定)。
/// <para>
/// 规则:<b>0 个账号不可退</b>(按钮整体隐藏,此为第二道防线);
/// <b>恰好 1 个账号直接可退</b>(无歧义,无需点选);
/// <b>多账号必须先点选</b>一行(退出的是点选项,防止误退默认账号)。
/// </para>
/// </summary>
public static class AccountLogoutGate
{
    /// <summary>计算「退出登录」是否可用。</summary>
    /// <param name="accountCount">该接口已保存的账号数。</param>
    /// <param name="hasPicked">用户是否已在列表中点选了要退出的账号。</param>
    public static bool CanLogout(int accountCount, bool hasPicked)
        => accountCount == 1 || (accountCount > 1 && hasPicked);
}
