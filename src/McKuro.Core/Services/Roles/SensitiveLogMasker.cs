using System.Text.RegularExpressions;

namespace McKuro.Core.Services.Roles;

/// <summary>
/// 调试日志脱敏:把请求体/响应中的令牌字段值替换为 <c>***</c>。
/// <para>用户为报 bug 打开 Debug 日志时会整份分享日志文件;requestToken / user/login/sdk 等接口的
/// 请求体与响应含 accessToken / x-token,原样写盘等于把登录凭据一并交出去(评审反馈)。</para>
/// <para>同时覆盖 JSON(含 data 字段里再转义一层的 <c>\"accessToken\":\"…\"</c>)与表单
/// (<c>token=…&amp;</c>)两种形态。<b>须在截断之前调用</b>,否则截断点落在令牌中间时正则匹配不到。</para>
/// </summary>
internal static partial class SensitiveLogMasker
{
    /// <summary>JSON 键值:"accessToken":"…" / \"token\":\"…\"(键名不区分大小写)。</summary>
    [GeneratedRegex(@"(\\?""(?:accessToken|access_token|refreshToken|refresh_token|token|x-token|b-at)\\?""\s*:\s*\\?"")[^""\\]*",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex JsonTokenField();

    /// <summary>表单键值:token=… / accessToken=…(到 &amp; 或结尾)。</summary>
    [GeneratedRegex(@"((?:^|&)(?:accessToken|access_token|refreshToken|token)=)[^&]*",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FormTokenField();

    /// <summary>返回脱敏后的文本(null → 空串)。</summary>
    public static string Mask(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }
        var masked = JsonTokenField().Replace(text, "$1***");
        return FormTokenField().Replace(masked, "$1***");
    }
}
