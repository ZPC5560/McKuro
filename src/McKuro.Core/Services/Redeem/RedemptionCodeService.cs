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

    /// <summary>
    /// 构造。兑换码接口(api.999758.xyz)证书与域名不匹配,需跳过服务器证书校验;
    /// 故使用独立 HttpClient 而非共享实例,避免影响其他服务。
    /// </summary>
    public RedemptionCodeService(HttpClient? http = null, ILogger<RedemptionCodeService>? logger = null)
    {
        if (http is not null)
        {
            _http = http;
        }
        else
        {
            var handler = new HttpClientHandler
            {
                // 第三方自建接口证书不匹配,跳过校验以拉取兑换码
                ServerCertificateCustomValidationCallback =
                    (_, _, _, _) => true,
            };
            _http = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(15),
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
