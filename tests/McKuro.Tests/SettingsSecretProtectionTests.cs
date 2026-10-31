using McKuro.Core.Infrastructure;
using McKuro.Core.Models.CloudGame;
using McKuro.Core.Models.Kuro;
using McKuro.Core.Services.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace McKuro.Tests;

/// <summary>
/// settings.json 敏感字段落盘加密(Windows DPAPI)+ 旧明文透明迁移 + 坏文件备份。
/// 非 Windows 上仍明文保存,与密文相关的断言只在 Windows 上执行。
/// </summary>
public class SettingsSecretProtectionTests : IDisposable
{
    private readonly string _dir;

    public SettingsSecretProtectionTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "McKuro-secret-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* ignore */ }
    }

    private string SettingsPath => Path.Combine(_dir, "settings.json");

    private SettingsService NewService() => new(_dir, NullLogger<SettingsService>.Instance);

    [Fact]
    public void Secrets_RoundTrip_And_Are_Not_Plaintext_On_Windows()
    {
        var s = NewService();
        s.Current.KujiequToken = "kj-token-secret";
        s.Current.GuideToken = "guide-token-secret";
        s.Current.GuidePhone = "13900000001";
        s.Current.KuroAccounts.Add(new KuroAccount
        {
            UserId = "u1",
            Token = "kuro-token-secret",
            DeviceId = "device-secret",
            Mobile = "13900000002",
        });
        s.Current.CloudAccounts.Add(new CloudAccount
        {
            Id = "13900000003",
            Phone = "13900000003",
            Name = "cloud",
            LoginDataJson = """{"phoneToken":"cloud-phone-token-secret"}""",
        });
        s.Current.CurrentCloudAccountId = "13900000003";
        s.Save();

        var reloaded = NewService().Current;
        Assert.Equal("kj-token-secret", reloaded.KujiequToken);
        Assert.Equal("guide-token-secret", reloaded.GuideToken);
        Assert.Equal("13900000001", reloaded.GuidePhone);
        var kuro = Assert.Single(reloaded.KuroAccounts);
        Assert.Equal("u1", kuro.UserId);
        Assert.Equal("kuro-token-secret", kuro.Token);
        Assert.Equal("device-secret", kuro.DeviceId);
        Assert.Equal("13900000002", kuro.Mobile);
        var cloud = Assert.Single(reloaded.CloudAccounts);
        Assert.Equal("13900000003", cloud.Id);
        Assert.Equal("13900000003", cloud.Phone);
        Assert.Equal("""{"phoneToken":"cloud-phone-token-secret"}""", cloud.LoginDataJson);
        Assert.Equal("13900000003", reloaded.CurrentCloudAccountId);

        if (OperatingSystem.IsWindows())
        {
            var raw = File.ReadAllText(SettingsPath);
            Assert.Contains(SecretProtector.DpapiPrefix, raw);
            Assert.DoesNotContain("token-secret", raw);
            Assert.DoesNotContain("device-secret", raw);
            Assert.DoesNotContain("1390000000", raw);
        }
    }

    [Fact]
    public void Legacy_Plaintext_Is_Read_Then_Encrypted_On_Next_Save()
    {
        File.WriteAllText(SettingsPath,
            """{"GameRootDir":"D:\\Wuwa","KujiequToken":"legacy-token","KuroAccounts":[{"UserId":"u1","Token":"legacy-kuro","DeviceId":"legacy-dev","Mobile":"13800000000"}]}""");

        var s = NewService();
        Assert.Equal("legacy-token", s.Current.KujiequToken);
        var account = Assert.Single(s.Current.KuroAccounts);
        Assert.Equal("legacy-kuro", account.Token);
        Assert.Equal("legacy-dev", account.DeviceId);
        Assert.Equal("13800000000", account.Mobile);

        s.Save();

        if (OperatingSystem.IsWindows())
        {
            var raw = File.ReadAllText(SettingsPath);
            Assert.DoesNotContain("legacy-token", raw);
            Assert.DoesNotContain("legacy-kuro", raw);
            Assert.DoesNotContain("13800000000", raw);
        }

        var reloaded = NewService().Current;
        Assert.Equal("D:\\Wuwa", reloaded.GameRootDir);
        Assert.Equal("legacy-token", reloaded.KujiequToken);
        Assert.Equal("legacy-kuro", Assert.Single(reloaded.KuroAccounts).Token);
    }

    [Fact]
    public void Undecryptable_Secret_Becomes_Empty_Without_Losing_Other_Settings()
    {
        // 设置文件从别的电脑/用户拷来:DPAPI 密文解不开。不能抛异常(那样整份设置会回退默认值)。
        File.WriteAllText(SettingsPath,
            """{"GameRootDir":"/games","DownloadConcurrency":5,"KujiequToken":"dpapi:v1:AAAA","GuideToken":"dpapi:v1:not-base64!","KuroAccounts":[{"UserId":"u1","Token":"dpapi:v1:AAAA"}]}""");

        var s = NewService().Current;

        Assert.Equal("/games", s.GameRootDir);
        Assert.Equal(5, s.DownloadConcurrency);
        Assert.Equal("", s.KujiequToken);
        Assert.Equal("", s.GuideToken);
        var account = Assert.Single(s.KuroAccounts);
        Assert.Equal("u1", account.UserId);
        Assert.Equal("", account.Token);
        Assert.Empty(Directory.GetFiles(_dir, "settings.json.corrupt-*"));
    }

    [Fact]
    public void Corrupt_File_Is_Backed_Up_Before_Defaults_Are_Used()
    {
        const string broken = """{"GameRootDir":"/games","KuroAccounts":[{"UserId":""";
        File.WriteAllText(SettingsPath, broken);

        var s = NewService();

        Assert.Equal("", s.Current.GameRootDir);
        var backup = Assert.Single(Directory.GetFiles(_dir, "settings.json.corrupt-*"));
        Assert.Equal(broken, File.ReadAllText(backup));

        // 之后保存不会碰备份
        s.Current.GameRootDir = "/new";
        s.Save();
        Assert.Equal(broken, File.ReadAllText(backup));
        Assert.Equal("/new", NewService().Current.GameRootDir);
    }

    [Fact]
    public void Protector_PassesThrough_Empty_And_Plain_Values()
    {
        Assert.Equal("", SecretProtector.Protect(""));
        Assert.Equal("", SecretProtector.Unprotect(""));
        Assert.Equal("plain", SecretProtector.Unprotect("plain"));

        var once = SecretProtector.Protect("abc");
        // 已是密文时不重复加密
        Assert.Equal(once, SecretProtector.Protect(once));
        Assert.Equal("abc", SecretProtector.Unprotect(once));
        if (OperatingSystem.IsWindows())
        {
            Assert.StartsWith(SecretProtector.DpapiPrefix, once);
        }
        else
        {
            Assert.Equal("abc", once);
        }
    }
}
