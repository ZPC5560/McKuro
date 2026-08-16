using System.Net;
using System.Net.Http.Headers;
using System.Text;
using McKuro.Core.Models.Kuro;

namespace McKuro.Core.Services.Kuro;

/// <summary>库街区 API 异常。</summary>
public sealed class KuroApiException(string message) : Exception(message);

/// <summary>
/// 库街区 (kurobbs) API 客户端聚合 facade:登录、角色、签到、库街区每日任务、扫码。
/// <para>
/// 端点与请求方式参考 Haiyu 的 <c>Haiyu.KuroClient</c>。
/// 按职责拆分为 partial 文件:<c>KuroClient.Auth.cs</c>(登录/验证码)、<c>KuroClient.Gamer.cs</c>(角色/签到/每日任务)、<c>KuroClient.Scan.cs</c>(扫码登录);
/// HTTP 共享逻辑由 <see cref="KuroHttpClient"/> 提供。
/// </para>
/// </summary>
public sealed partial class KuroClient : IKuroClient
{
    /// <summary>库街区 API 根地址。</summary>
    public const string BaseUrl = "https://api.kurobbs.com";

    /// <inheritdoc/>
    public string Ip => _kuro.Ip;

    private readonly HttpClient _inner;
    private readonly KuroHttpClient _kuro;

    public KuroClient(HttpClient http)
    {
        _inner = http;
        _kuro = new KuroHttpClient(http);
    }

    /// <inheritdoc/>
    public async Task InitAsync(CancellationToken ct = default)
        => await _kuro.InitAsync(ct).ConfigureAwait(false);

    /// <summary>构造设备指纹头(android 客户端风格)。</summary>
    private Dictionary<string, string> GetDeviceHeader(KuroAccount? account = null)
        => _kuro.GetDeviceHeader(account);

    /// <summary>构造库街区风格的 POST 请求。</summary>
    private HttpRequestMessage BuildPost(string url, Dictionary<string, string> headers, Dictionary<string, string>? form = null)
        => _kuro.BuildPost(url, headers, form);

    /// <summary>发送 POST 并读取响应字符串。</summary>
    private async Task<string> SendAndReadAsync(string url, Dictionary<string, string> headers, Dictionary<string, string>? form, CancellationToken ct)
        => await _kuro.SendAndReadAsync(url, headers, form, ct).ConfigureAwait(false);
}
