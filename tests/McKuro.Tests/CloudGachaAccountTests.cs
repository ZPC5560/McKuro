using System.Text.Json;
using McKuro.Core.Models.CloudGame;
using McKuro.Core.Services.Gacha;
using McKuro.Core.Services.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace McKuro.Tests;

/// <summary>
/// 云鸣潮多账号服务契约测试(账号页云鸣潮卡片与库街区卡片同款多账号能力)。
/// 覆盖:
/// ① 旧版单账号字段(CloudLoginDataJson/Name/Phone)加载时自动迁移为多账号列表,登录态不丢;
/// ② 列表已有数据时旧字段不被无条件清空(降级重升场景);
/// ③ 空手机号的迁移条目获得稳定 Id(GUID),可按索引切换、可被退出移除(不再是死条目);
/// ④ 按索引切换当前账号(SwitchToIndex),越界拒绝且不改当前账号;
/// ⑤ 退出登录仅移除当前账号,其余已保存账号不受影响;
/// ⑥ 当前指针缺失/失效时回退第一个账号并落盘。
/// (AccountViewModel 依赖 AppServices 静态容器,与 KuroAccountServiceTests 相同,在服务层验证。)
/// </summary>
public class CloudGachaAccountTests : IDisposable
{
    private readonly string _dir;

    public CloudGachaAccountTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "McKuro-cga-" + Guid.NewGuid().ToString("N"));
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

    // 账号管理路径不触碰云 API 与抽卡同步流水线,构造时传 null 即可隔离验证
    private static CloudGachaService CreateService(SettingsService settings)
        => new(null!, null!, settings);

    private static CloudAccount NewAccount(string id, string phone, string name = "")
        => new()
        {
            Id = id,
            Phone = phone,
            Name = name.Length > 0 ? name : "user-" + id[^4..],
            LoginDataJson = """{"username":"u","phoneToken":"t"}""",
        };

    [Fact]
    public void Legacy_SingleAccount_Fields_Migrate_Into_Account_List()
    {
        // 模拟旧版 settings.json:只有单账号字段
        var legacy = new AppSettings
        {
            CloudLoginDataJson = """{"username":"old","phoneToken":"tok"}""",
            CloudLoginName = "old-user",
            CloudLoginPhone = "13800000001",
        };
        File.WriteAllText(
            Path.Combine(_dir, "settings.json"),
            JsonSerializer.Serialize(legacy, SettingsJsonContext.Default.AppSettings));

        var settings = new SettingsService(_dir, NullLogger<SettingsService>.Instance);
        var svc = CreateService(settings);

        var account = Assert.Single(svc.GetAccounts());
        Assert.Equal("13800000001", account.Phone);
        // 有手机号:Id 直接取手机号(与登录 upsert 的键一致)
        Assert.Equal("13800000001", account.Id);
        Assert.Equal("old-user", account.Name);
        Assert.Equal("""{"username":"old","phoneToken":"tok"}""", account.LoginDataJson);
        Assert.True(svc.HasSavedLogin);
        Assert.Equal("13800000001", svc.SavedLoginPhone);
        Assert.Equal("old-user", svc.SavedLoginName);
        // 迁移发生时旧字段清空,统一走列表读写
        Assert.Equal("", settings.Current.CloudLoginDataJson);
    }

    [Fact]
    public void Legacy_Fields_Not_Wiped_When_List_Already_Has_Accounts()
    {
        // 降级到旧版重写的场景:列表已有数据,旧字段还留着"降级期间的会话"——迁移不得无条件清空
        var settings = new SettingsService(_dir, NullLogger<SettingsService>.Instance);
        settings.Current.CloudAccounts.Add(NewAccount("13800000001", "13800000001"));
        settings.Current.CloudLoginDataJson = """{"username":"downgrade","phoneToken":"tok2"}""";
        settings.Current.CloudLoginPhone = "13800000009";
        settings.Save();

        var reloaded = new SettingsService(_dir, NullLogger<SettingsService>.Instance);
        Assert.Single(reloaded.Current.CloudAccounts);
        Assert.Equal("""{"username":"downgrade","phoneToken":"tok2"}""", reloaded.Current.CloudLoginDataJson);
    }

    [Fact]
    public void Empty_Phone_Migrated_Entry_Gets_Stable_Id_And_Is_Switchable_Removable()
    {
        // 旧数据无手机号:迁移生成非空 GUID Id;按索引可切换、退出可移除(评审反馈的"死条目"回归)
        var legacy = new AppSettings
        {
            CloudLoginDataJson = """{"username":"old","phoneToken":"tok"}""",
            CloudLoginName = "old-user",
            CloudLoginPhone = "",
        };
        File.WriteAllText(
            Path.Combine(_dir, "settings.json"),
            JsonSerializer.Serialize(legacy, SettingsJsonContext.Default.AppSettings));

        var settings = new SettingsService(_dir, NullLogger<SettingsService>.Instance);
        var svc = CreateService(settings);

        var account = Assert.Single(svc.GetAccounts());
        Assert.False(string.IsNullOrWhiteSpace(account.Id));
        Assert.Equal("", account.Phone);
        Assert.True(svc.HasSavedLogin);

        // 再加一个正常账号,验证按索引切换 + 移除空手机号条目
        settings.Current.CloudAccounts.Add(NewAccount("13800000002", "13800000002"));
        settings.Save();
        Assert.True(svc.SwitchToIndex(1));
        Assert.Equal("13800000002", svc.SavedLoginPhone);
        Assert.True(svc.SwitchToIndex(0));
        Assert.Equal("", svc.SavedLoginPhone);

        // 退出当前(空手机号条目):按 Id 移除,不再删不掉
        svc.Logout();
        var remaining = Assert.Single(svc.GetAccounts());
        Assert.Equal("13800000002", remaining.Phone);
    }

    [Fact]
    public void SwitchToIndex_Changes_Current_But_Rejects_OutOfRange()
    {
        var settings = new SettingsService(_dir, NullLogger<SettingsService>.Instance);
        settings.Current.CloudAccounts.Add(NewAccount("13800000001", "13800000001"));
        settings.Current.CloudAccounts.Add(NewAccount("13800000002", "13800000002"));
        settings.Current.CurrentCloudAccountId = "13800000001";
        settings.Save();

        var svc = CreateService(settings);
        Assert.Equal("13800000001", svc.SavedLoginPhone);

        Assert.True(svc.SwitchToIndex(1));
        Assert.Equal("13800000002", svc.SavedLoginPhone);
        Assert.Equal("user-0002", svc.SavedLoginName);

        Assert.False(svc.SwitchToIndex(2));
        Assert.False(svc.SwitchToIndex(-1));
        // 失败的切换不改当前账号
        Assert.Equal("13800000002", svc.SavedLoginPhone);
    }

    [Fact]
    public void Logout_Removes_Current_And_Keeps_Other_Accounts()
    {
        var settings = new SettingsService(_dir, NullLogger<SettingsService>.Instance);
        settings.Current.CloudAccounts.Add(NewAccount("13800000001", "13800000001"));
        settings.Current.CloudAccounts.Add(NewAccount("13800000002", "13800000002"));
        settings.Current.CurrentCloudAccountId = "13800000002";
        settings.Save();

        var svc = CreateService(settings);
        svc.Logout();

        var remaining = Assert.Single(svc.GetAccounts());
        Assert.Equal("13800000001", remaining.Phone);
        // 当前指针已清空:回退到剩余第一个账号(仍为已登录态)
        Assert.Equal("13800000001", svc.SavedLoginPhone);
        Assert.True(svc.HasSavedLogin);

        // 再退出一次:移除最后一个账号 → 未登录
        svc.Logout();
        Assert.Empty(svc.GetAccounts());
        Assert.False(svc.HasSavedLogin);
        Assert.Equal("", svc.SavedLoginPhone);
    }

    [Fact]
    public void Missing_CurrentId_Falls_Back_To_First_And_Persists()
    {
        var settings = new SettingsService(_dir, NullLogger<SettingsService>.Instance);
        settings.Current.CloudAccounts.Add(NewAccount("13800000001", "13800000001"));
        settings.Current.CurrentCloudAccountId = "";
        settings.Save();

        var svc = CreateService(settings);

        Assert.True(svc.HasSavedLogin);
        Assert.Equal("13800000001", svc.SavedLoginPhone);
        // 回退结果已落盘:重载后当前指针稳定
        var reloaded = new SettingsService(_dir, NullLogger<SettingsService>.Instance);
        Assert.Equal("13800000001", reloaded.Current.CurrentCloudAccountId);
    }
}
