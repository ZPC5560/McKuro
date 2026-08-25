using McKuro.Core.Models.Kuro;
using McKuro.Core.Services.Kuro;
using McKuro.Core.Services.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace McKuro.Tests;

/// <summary>
/// 账号页库街区卡片的「退出登录 / 移除」合并回归测试。
/// AccountViewModel 的 <c>Logout</c> 与原 <c>RemoveAccount</c> 功能完全相同(都只做
/// <c>KuroAccounts.Remove(current.UserId)</c>),已合并为单一的「退出登录」入口。
/// 本测试锁定该合并动作所依赖的服务契约:
/// ① 当前账号被移除后 Current(即 ViewModel 的 HasKuroLogin)变为 null —— 对应「退出登录」后按钮态翻转;
/// ② 仅移除当前账号,不影响其他已保存账号;
/// ③ 移除非当前账号时,当前登录态保持不动。
/// (注释:ViewModel 依赖 AppServices 静态容器 + Avalonia DispatcherTimer,无法在单元测试中隔离构造,
/// 故在本服务层验证合并动作的实际行为。)
/// </summary>
public class KuroAccountServiceTests : IDisposable
{
    private readonly string _dir;

    public KuroAccountServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "McKuro-kas-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (Exception)
        {
            // 忽略清理失败
        }
    }

    private static KuroAccount NewAccount(string uid, string mobile = "")
        => new()
        {
            UserId = uid,
            Token = "token-" + uid,
            DeviceId = "device-" + uid,
            Mobile = mobile,
            Nickname = "nick-" + uid,
        };

    private KuroAccountService CreateService()
        => new(new SettingsService(_dir, NullLogger<SettingsService>.Instance));

    [Fact]
    public void AddOrUpdate_Makes_Account_Current()
    {
        var svc = CreateService();
        Assert.Null(svc.Current);

        svc.AddOrUpdate(NewAccount("u1"));

        Assert.NotNull(svc.Current);
        Assert.Equal("u1", svc.Current!.UserId);
    }

    [Fact]
    public void Remove_Of_Current_Clears_Current_And_Keeps_Others()
    {
        var svc = CreateService();
        svc.AddOrUpdate(NewAccount("u1"));
        svc.AddOrUpdate(NewAccount("u2"));
        Assert.Equal("u2", svc.Current!.UserId);

        // 合并后的「退出登录」动作对当前账号做 Remove(Current.UserId)
        svc.Remove(svc.Current!.UserId);

        // 当前登录态被注销(→ ViewModel HasKuroLogin = false),其余账号保留
        Assert.Null(svc.Current);
        var remaining = Assert.Single(svc.GetAccounts());
        Assert.Equal("u1", remaining.UserId);
    }

    [Fact]
    public void Remove_Of_NonCurrent_Leaves_Current_Intact()
    {
        var svc = CreateService();
        svc.AddOrUpdate(NewAccount("u1"));
        svc.AddOrUpdate(NewAccount("u2"));

        svc.Remove("u1");

        Assert.Equal("u2", svc.Current!.UserId);
        Assert.Single(svc.GetAccounts());
    }

    [Fact]
    public void Current_Flips_To_Null_After_Logout_Equivalent()
    {
        var svc = CreateService();

        // 未登录 → Current 为 null(对应 HasKuroLogin=false)
        Assert.Null(svc.Current);

        svc.AddOrUpdate(NewAccount("u1"));
        Assert.NotNull(svc.Current);

        // 退出登录 = 移除当前账号
        svc.Remove(svc.Current!.UserId);
        Assert.Null(svc.Current);
        Assert.Empty(svc.GetAccounts());
    }
}
