using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using McKuro.Core.Models.Kuro;

namespace McKuro.Core.Services.Kuro;

/// <summary>
/// 库街区 API 共用的 HTTP/Header 助手。
/// <para>
/// 把 <c>BuildPost</c> / <c>GetDeviceHeader</c> / <c>SendTaskRequestAsync</c> 从 <c>KuroClient</c> 抽出,
/// 使后续拆分为 <c>KuroAuthService</c> / <c>KuroRoleService</c> 等独立服务时可复用底层传输。
/// </para>
/// </summary>
internal sealed class KuroHttpClient
{
    private readonly HttpClient _http;

    public KuroHttpClient(HttpClient http)
    {
        _http = http;
    }

    /// <summary>当前探测到的出口 IP(部分接口风控要求)。</summary>
    public string Ip { get; private set; } = "";

    /// <summary>异步探测出口 IP,失败静默。</summary>
    public async Task InitAsync(CancellationToken ct = default)
    {
        try
        {
            Ip = await _http.GetStringAsync("https://event.kurobbs.com/event/ip", ct).ConfigureAwait(false);
        }
        catch (Exception)
        {
            Ip = "";
        }
    }

    /// <summary>构造库街区风格的 POST 请求(含 IP/User-Agent/版本等公共头)。</summary>
    public HttpRequestMessage BuildPost(
        string url,
        Dictionary<string, string> headers,
        Dictionary<string, string>? form = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        foreach (var (key, value) in headers)
        {
            request.Headers.TryAddWithoutValidation(key, value);
        }

        var encoded = new FormUrlEncodedContent(form ?? []);
        var query = encoded.ReadAsStringAsync().GetAwaiter().GetResult();
        request.Content = new StringContent(query, Encoding.UTF8, "application/x-www-form-urlencoded");
        return request;
    }

    /// <summary>构造设备指纹头(android 客户端风格)。</summary>
    public Dictionary<string, string> GetDeviceHeader(KuroAccount? account = null)
    {
        var dict = new Dictionary<string, string>
        {
            { "ip", Ip },
            { "Accept", "application/json, text/plain, */*" },
            { "Accept-Encoding", "gzip, deflate" },
            { "Accept-Language", "zh-CN,zh;q=0.9,en-US;q=0.8,en;q=0.7" },
            { "source", "android" },
            { "devCode", account?.DeviceId ?? "" },
            { "version", "3.1.2" },
            { "versionCode", "30102" },
            { "lang", "zh-Hans" },
            { "countryCode", "CN" },
            { "channelId", "8" },
            { "User-Agent", "okhttp/3.11.0" },
        };
        if (account is not null && !string.IsNullOrEmpty(account.Token))
        {
            dict.Add("token", account.Token);
        }
        return dict;
    }

    /// <summary>通用任务型 POST 请求(响应 code != 200 返回 null)。</summary>
    public async Task<KuroClientReturnCode<T>?> SendTaskRequestAsync<T>(
        KuroAccount account,
        string url,
        Dictionary<string, string> form,
        JsonTypeInfo<KuroClientReturnCode<T>> jsonTypeInfo,
        CancellationToken ct)
    {
        using var request = BuildPost(url, GetDeviceHeader(account), form);
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var result = JsonSerializer.Deserialize(json, jsonTypeInfo);
        if (result is null || result.Code != 200)
        {
            return null;
        }
        return result;
    }

    /// <summary>发送原始 POST 并读取响应字符串。</summary>
    public async Task<string> SendAndReadAsync(
        string url,
        Dictionary<string, string> headers,
        Dictionary<string, string>? form,
        CancellationToken ct)
    {
        using var request = BuildPost(url, headers, form);
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }
}
