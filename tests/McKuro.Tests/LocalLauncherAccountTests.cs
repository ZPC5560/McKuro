using McKuro.Core.Services.User;

namespace McKuro.Tests;

/// <summary>
/// 本地启动器登录凭证的纯逻辑:UID 格式化与 oauthCode 解密。
/// 不读真实缓存、不发网络请求(那些是集成路径,单测里用假数据覆盖判定逻辑)。
/// </summary>
public sealed class LocalLauncherAccountTests
{
    [Fact]
    public void ToUid_RendersIntegerWithoutDecimalPoint()
    {
        // 缓存里 id 是 JSON 数字(Haiyu 用 double 接收),直接 ToString 会变成 "526781653" 或带小数
        var account = new LauncherCacheAccount { Id = 526781653 };

        Assert.Equal("526781653", LocalGameDailyDataService.ToUid(account));
    }

    [Fact]
    public void ToUid_TruncatesFractionalValue()
    {
        Assert.Equal("123", LocalGameDailyDataService.ToUid(new LauncherCacheAccount { Id = 123.0 }));
    }

    [Fact]
    public void XorDecrypt_IsSymmetricRoundTrip()
    {
        // 启动器用「每字符异或 5」混淆 oauthCode:同一运算既是加密也是解密
        const string plain = "abcdefghij0123456789";

        var obfuscated = LocalGameDailyDataService.XorDecrypt(plain, 5);
        Assert.NotEqual(plain, obfuscated);
        Assert.Equal(plain, LocalGameDailyDataService.XorDecrypt(obfuscated, 5));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void XorDecrypt_EmptyInput_ReturnsEmpty(string? data)
    {
        Assert.Equal("", LocalGameDailyDataService.XorDecrypt(data ?? "", 5));
    }

    [Fact]
    public void XorDecrypt_KeepsLengthAndHandlesNonAscii()
    {
        var decoded = LocalGameDailyDataService.XorDecrypt("abc", 5);

        Assert.Equal(3, decoded.Length);
        Assert.Equal((char)('a' ^ 5), decoded[0]);
    }

    /// <summary>
    /// 回归(评审:绑定值被限流误清 → 用户已保存的选号被静默抹掉):
    /// <see cref="LocalGameDailyDataService.HasCredential"/> 是"该凭证是否真的已从启动器移除"
    /// 的唯一判据,必须与网络枚举结果解耦 —— 单凭证 queryPlayerInfo 失败会被静默跳过、
    /// 其余账号照常返回,所以"枚举结果里没有"证明不了它没了。
    /// </summary>
    [Fact]
    public void HasCredential_TrueWhenPresent_FalseWhenAbsent()
    {
        var accounts = new List<LauncherCacheAccount>
        {
            new() { Id = 111, Username = "A" },
            new() { Id = 222, Username = "B" },
        };

        Assert.True(LocalGameDailyDataService.HasCredential(accounts, "111"));
        Assert.True(LocalGameDailyDataService.HasCredential(accounts, "222"));
        Assert.False(LocalGameDailyDataService.HasCredential(accounts, "333"));
    }

    /// <summary>
    /// 读不到缓存文件(accounts=null)或未提供 UID 时必须返回"无法判定"(null),
    /// 调用方据此保守处理、不清除用户已保存的绑定值。
    /// </summary>
    [Fact]
    public void HasCredential_UnknownInput_ReturnsNull()
    {
        Assert.Null(LocalGameDailyDataService.HasCredential(null, "111"));   // 文件读不到
        Assert.Null(LocalGameDailyDataService.HasCredential([], null));       // 未提供 UID
        Assert.Null(LocalGameDailyDataService.HasCredential([], ""));         // 空 UID
        Assert.Null(LocalGameDailyDataService.HasCredential([], "  "));       // 空白 UID
    }

    /// <summary>空列表不等于"无法判定":它明确表示缓存里一个凭证都没有(凭证确实都不在了)。</summary>
    [Fact]
    public void HasCredential_EmptyListWithUid_ReportsAbsent()
    {
        Assert.False(LocalGameDailyDataService.HasCredential([], "111"));
    }
}
