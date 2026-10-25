using McKuro.ViewModels;

namespace McKuro.Tests;

/// <summary>
/// 账号页「退出登录」多账号门禁测试(2026-10-25 列表样式改版):
/// 无账号按钮隐藏(第二道防线:命令里还要判空);单账号直接可退;
/// 多账号必须先点选一行 —— 退出的是点选项,防止误退默认账号。
/// </summary>
public class AccountLogoutGateTests
{
    [Fact]
    public void No_Accounts_Cannot_Logout()
    {
        // 0 个账号:按钮已隐藏,门禁仍返回 false 兜底(命令内还有判空防御)
        Assert.False(AccountLogoutGate.CanLogout(0, hasPicked: false));
        Assert.False(AccountLogoutGate.CanLogout(0, hasPicked: true));
    }

    [Fact]
    public void Single_Account_Logouts_Without_Picking()
    {
        // 单账号无歧义:不需点选直接可退
        Assert.True(AccountLogoutGate.CanLogout(1, hasPicked: false));
        Assert.True(AccountLogoutGate.CanLogout(1, hasPicked: true));
    }

    [Fact]
    public void Multiple_Accounts_Require_Picking()
    {
        // 多账号未点选:禁用(点退出只提示,不执行)
        Assert.False(AccountLogoutGate.CanLogout(2, hasPicked: false));
        Assert.False(AccountLogoutGate.CanLogout(3, hasPicked: false));
        // 点选后可用
        Assert.True(AccountLogoutGate.CanLogout(2, hasPicked: true));
        Assert.True(AccountLogoutGate.CanLogout(3, hasPicked: true));
    }
}
