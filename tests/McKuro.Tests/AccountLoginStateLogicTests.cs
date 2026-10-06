using McKuro.Core.Models.Kuro;
using McKuro.ViewModels;

namespace McKuro.Tests;

/// <summary>
/// 账号页「库街区登录状态点」判定测试。
///
/// <para><b>回归背景</b>(用户反馈):账号页加载时不再校验登录态,只有到角色数据页点「同步」
/// 才发现登录已失效。根因是竞态 —— <c>RefreshAccounts</c> 在页面加载时被
/// <c>RefreshGameRoleLabelsAsync</c> 异步调用,它把状态无条件写成绿色,
/// 冲掉了并发进行的会话校验刚判定的「失效(橙)」。</para>
///
/// <para>判定规则已抽为 <see cref="AccountLoginStateLogic"/> 纯函数以便测试。</para>
/// </summary>
public class AccountLoginStateLogicTests
{
    [Fact]
    public void No_Account_Shows_NotLoggedIn()
    {
        Assert.Equal(InterfaceLoginState.NotLoggedIn, AccountLoginStateLogic.ResolveKuroDot(null, false));
        Assert.Equal(InterfaceLoginState.NotLoggedIn, AccountLoginStateLogic.ResolveKuroDot("", false));
    }

    [Fact]
    public void Account_Without_Known_Failure_Shows_Ok()
    {
        // 已保存登录、尚未校验出问题 → 绿点(保持"先乐观显示"的既有体验)
        Assert.Equal(InterfaceLoginState.Ok, AccountLoginStateLogic.ResolveKuroDot("u1", false));
    }

    [Fact]
    public void Known_Expired_Account_Keeps_Error_Dot_On_Repaint()
    {
        // 核心回归:会话已被判定失效后,页面重绘(RefreshAccounts)不得把橙点刷回绿点。
        // 修复前此处等价于「有账号就 Ok」,于是账号页永远看不到失效提示。
        Assert.Equal(InterfaceLoginState.Error, AccountLoginStateLogic.ResolveKuroDot("u1", true));
    }

    [Fact]
    public void Expired_State_Is_Per_Account()
    {
        // 失效标记按账号记:换到另一个未失效的账号应显示绿点,不能继承上一个账号的橙点
        Assert.Equal(InterfaceLoginState.Error, AccountLoginStateLogic.ResolveKuroDot("u1", true));
        Assert.Equal(InterfaceLoginState.Ok, AccountLoginStateLogic.ResolveKuroDot("u2", false));
    }

    [Fact]
    public void Server_Rejection_Is_Treated_As_Expired()
    {
        // 服务端明确拒绝(非 200 / 无 data)→ 判定失效
        Assert.True(AccountLoginStateLogic.IsKuroSessionExpired(new GamerRoil { Code = 10001, Data = null }));
        Assert.True(AccountLoginStateLogic.IsKuroSessionExpired(new GamerRoil { Code = 200, Data = null }));
    }

    [Fact]
    public void Successful_Response_Is_Not_Expired()
    {
        Assert.False(AccountLoginStateLogic.IsKuroSessionExpired(
            new GamerRoil { Code = 200, Data = [new GameRoilDataItem { UserId = "u1" }] }));
    }

    [Fact]
    public void Missing_Response_Does_Not_Claim_Expiry()
    {
        // 网络异常/超时拿到 null → 不得判为失效(否则会把临时网络问题误报成"异常登录")
        Assert.False(AccountLoginStateLogic.IsKuroSessionExpired(null));
    }

    [Fact]
    public void Null_Response_Is_Unreachable_Not_Valid()
    {
        // 评审回归(2026-10):null 是「无法判定」而非「确认有效」。
        // 若把它当 Valid 处理,一次网络抖动就会把刚判定的「会话失效」清回绿点 ——
        // 账号页看不到失效提示(用户反馈过的问题在网络抖动场景下复发)。
        Assert.Equal(AccountLoginStateLogic.KuroSessionProbe.Unreachable,
            AccountLoginStateLogic.ProbeKuroSession(null));
        // 三分语义各自成立:有效/被拒/不可达互不混淆
        Assert.Equal(AccountLoginStateLogic.KuroSessionProbe.Valid,
            AccountLoginStateLogic.ProbeKuroSession(
                new GamerRoil { Code = 200, Data = [new GameRoilDataItem { UserId = "u1" }] }));
        Assert.Equal(AccountLoginStateLogic.KuroSessionProbe.Rejected,
            AccountLoginStateLogic.ProbeKuroSession(new GamerRoil { Code = 10001, Data = null }));
        Assert.Equal(AccountLoginStateLogic.KuroSessionProbe.Rejected,
            AccountLoginStateLogic.ProbeKuroSession(new GamerRoil { Code = 200, Data = null }));
    }

    // ==================== 右上角「同一账号判定」 ====================

    private static AccountLoginStateLogic.InterfaceProbe Probe(string name, string phone, bool expired = false)
        => new(name, phone, expired);

    [Fact]
    public void All_Same_Phone_And_Valid_Is_Same_Account()
    {
        var verdict = AccountLoginStateLogic.ResolveVerdict(
        [
            Probe("库街区", "13800000000"),
            Probe("云鸣潮", "13800000000"),
            Probe("mcguide", "13800000000"),
        ]);
        Assert.Equal(SameAccountVerdict.Same, verdict);
    }

    [Fact]
    public void Expired_Interface_Outranks_Same_Account_Verdict()
    {
        // 核心回归(用户反馈):登录失效后右上角仍写"已登录的 3 个接口均为同一账号"。
        // 手机号一致只说明"像同一个号",不代表还能用 —— 失效必须优先暴露,否则用户看不到要重新登录。
        var verdict = AccountLoginStateLogic.ResolveVerdict(
        [
            Probe("库街区", "13800000000", expired: true),
            Probe("云鸣潮", "13800000000"),
            Probe("mcguide", "13800000000"),
        ]);
        Assert.Equal(SameAccountVerdict.SessionExpired, verdict);
    }

    [Fact]
    public void Expired_Interface_Also_Outranks_Different_And_Unknown()
    {
        // 与"不同手机号"相比:失效更需要先处理
        Assert.Equal(SameAccountVerdict.SessionExpired, AccountLoginStateLogic.ResolveVerdict(
        [
            Probe("库街区", "13800000000", expired: true),
            Probe("云鸣潮", "13900000000"),
        ]));
        // 仅 1 个接口且已失效(如库街区是唯一登录的) → 同样要提示失效而非"仅登录一个"
        Assert.Equal(SameAccountVerdict.SessionExpired, AccountLoginStateLogic.ResolveVerdict(
            [Probe("库街区", "13800000000", expired: true)]));
    }

    [Fact]
    public void ExpiredNames_Lists_Only_Expired_Interfaces()
    {
        var names = AccountLoginStateLogic.ExpiredNames(
        [
            Probe("库街区", "13800000000", expired: true),
            Probe("云鸣潮", "13800000000"),
            Probe("mcguide", "13800000000", expired: true),
        ]);
        Assert.Equal(["库街区", "mcguide"], names);
    }

    [Fact]
    public void Different_Phones_Still_Detected_When_Nothing_Expired()
    {
        Assert.Equal(SameAccountVerdict.Different, AccountLoginStateLogic.ResolveVerdict(
        [
            Probe("库街区", "13800000000"),
            Probe("云鸣潮", "13900000000"),
        ]));
    }

    [Fact]
    public void Single_Interface_Or_Missing_Phone_Is_Unknown()
    {
        Assert.Equal(SameAccountVerdict.Unknown, AccountLoginStateLogic.ResolveVerdict(
            [Probe("库街区", "13800000000")]));
        Assert.Equal(SameAccountVerdict.Unknown, AccountLoginStateLogic.ResolveVerdict(
        [
            Probe("库街区", "13800000000"),
            Probe("云鸣潮", ""),   // 缺手机号 → 信息不足
        ]));
    }
}
