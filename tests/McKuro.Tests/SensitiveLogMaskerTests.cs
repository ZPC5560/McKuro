using McKuro.Core.Services.Roles;

namespace McKuro.Tests;

/// <summary>
/// 调试日志脱敏(L17):requestToken / mcguide user/login/sdk 的请求体与响应含令牌,
/// 用户分享 Debug 日志时不得一并泄露。
/// </summary>
public class SensitiveLogMaskerTests
{
    [Fact]
    public void Masks_Json_Token_Fields_Including_Escaped_Data_String()
    {
        // requestToken 响应:data 为再转义一层的 JSON 字符串
        const string resp = """{"code":200,"data":"{\"accessToken\":\"eyJhbGciOi.secret\",\"expire\":3600}"}""";
        var masked = SensitiveLogMasker.Mask(resp);
        Assert.DoesNotContain("eyJhbGciOi.secret", masked);
        Assert.Contains("\\\"accessToken\\\":\\\"***", masked);
        Assert.Contains("expire", masked);

        // mcguide 登录:请求体 accessToken、响应 token
        var body = SensitiveLogMasker.Mask("""{"cUid":"1","cName":"n","accessToken":"abc123"}""");
        Assert.Equal("""{"cUid":"1","cName":"n","accessToken":"***"}""", body);
        var login = SensitiveLogMasker.Mask("""{"code":200,"data":{"token":"x-token-value"}}""");
        Assert.Equal("""{"code":200,"data":{"token":"***"}}""", login);
    }

    [Fact]
    public void Masks_Form_Token_Fields_And_Keeps_Other_Params()
    {
        Assert.Equal("roleId=1&token=***&serverId=2", SensitiveLogMasker.Mask("roleId=1&token=secret&serverId=2"));
        Assert.Equal("token=***", SensitiveLogMasker.Mask("token=secret"));
        // 不误伤非令牌字段
        Assert.Equal("gameId=3&roleId=100&serverId=76402e5b", SensitiveLogMasker.Mask("gameId=3&roleId=100&serverId=76402e5b"));
    }

    [Fact]
    public void Null_Or_Empty_Returns_Empty()
    {
        Assert.Equal("", SensitiveLogMasker.Mask(null));
        Assert.Equal("", SensitiveLogMasker.Mask(""));
    }
}
