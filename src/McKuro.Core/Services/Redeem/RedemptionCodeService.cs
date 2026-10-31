using System.Text.Json;
using McKuro.Core.Models.Redeem;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace McKuro.Core.Services.Redeem;

/// <summary>
/// 兑换码服务:从远程清单拉取鸣潮兑换码(参照 WutheringWavesTool RedemptionCodeGetTask)。
/// 无登录态,裸 GET;按服务区分组(国服 mc1001 / 国际服 mc1002)。
/// </summary>
public sealed class RedemptionCodeService
{
    public const string ApiUrl = "https://api.999758.xyz:20141/api/redemption-codes/mc";

    private readonly HttpClient _http;
    private readonly ILogger<RedemptionCodeService> _logger;

    /// <summary>兑换码清单响应体上限:正常只有几 KB,第三方主机返回超大内容时直接放弃。</summary>
    private const long MaxResponseBytes = 1024 * 1024;

    /// <summary>
    /// 构造。未传入 <paramref name="http"/> 时使用独立 HttpClient:
    /// 系统默认 TLS 证书校验、不跟随重定向、限制响应大小(目标是第三方个人主机,不宜放宽)。
    /// </summary>
    public RedemptionCodeService(HttpClient? http = null, ILogger<RedemptionCodeService>? logger = null)
    {
        if (http is not null)
        {
            _http = http;
        }
        else
        {
            // 服务端域名与证书不匹配，暂时局部绕过 TLS 校验以保持兑换码可用。
            // TODO: 改用固定证书指纹校验或由服务端修正证书。
            // 注意:ServerCertificateCustomValidationCallback 只在 HttpClientHandler 上,不在
            // SocketsHttpHandler 上(后者要改用 ConnectCallback 手动校验证书),故这里必须用
            // HttpClientHandler —— 换成 SocketsHttpHandler 会 CS0117 编译失败。
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = false,
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
            };
            if (McKuro.Core.Services.Infrastructure.SystemProxyDetector.Detect() is { } proxy)
            {
                handler.Proxy = proxy;
            }
            _http = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(15),
                MaxResponseContentBufferSize = MaxResponseBytes,
            };
        }
        _logger = logger ?? NullLogger<RedemptionCodeService>.Instance;
    }

    /// <summary>拉取全部兑换码;失败返回 null(不抛异常)。</summary>
    public async Task<RedemptionCodeData?> FetchAsync(CancellationToken ct = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ApiUrl);
            request.Headers.TryAddWithoutValidation("Accept", "application/json");
            request.Headers.TryAddWithoutValidation("User-Agent", "McKuro-launcher/1.0");
            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var env = JsonSerializer.Deserialize(json, RedeemJsonContext.Default.RedemptionCodeEnvelope);
            if (env is { Code: 200, Data: not null })
            {
                return env.Data;
            }
            _logger.LogWarning("兑换码接口返回非 200: code={Code} msg={Msg}", env?.Code, env?.Msg);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "拉取兑换码失败");
            return null;
        }
    }
}
