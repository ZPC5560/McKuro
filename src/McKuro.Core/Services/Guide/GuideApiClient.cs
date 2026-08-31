using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using McKuro.Core.Models.Guide;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace McKuro.Core.Services.Guide;

/// <summary>mcguide 攻略站 API 异常。<see cref="Code"/> 为服务端业务码(如 1001=登录过期)。</summary>
public sealed class GuideApiException(string message, int? code = null) : Exception(message)
{
    /// <summary>服务端业务码(信封 code 字段);未知时为 null。</summary>
    public int? Code { get; } = code;

    /// <summary>mcguide 会话过期业务码(信封 {"message":"登录过期","code":1001})。</summary>
    public const int SessionExpiredCode = 1001;
}

/// <summary>
/// mcguide 攻略站(guide-server.aki-game.com) API 客户端。
/// <para>流程(对齐抓包):登录后先 <c>/user/login/sdk</c> 换 x-token(服务端动态生成 innerToken),
/// 再 <c>/user/player/list</c> + <c>/user/player/choose</c> 选定玩家,
/// 最后 <c>/introduction/list</c> 取攻略、<c>/introduction/info</c> 拿养成达成度。</para>
/// </summary>
public sealed class GuideApiClient
{
    public const string BaseUrl = "https://guide-server.aki-game.com";

    // 固定请求头(对齐抓包)
    private const string FeTraceId = "ykeInmHpxJJRpVYY14shlxquJs4VJyaT";
    private const string Language = "zh-Hans";
    private const string Origin = "https://mcguide.kurogames.com";
    private const string Referer = "https://mcguide.kurogames.com/";

    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly ILogger<GuideApiClient> _logger;

    /// <summary>构造。baseUrl 仅在测试注入本地服务器时覆盖。</summary>
    public GuideApiClient(HttpClient http, string? baseUrl = null, ILogger<GuideApiClient>? logger = null)
    {
        _http = http;
        _baseUrl = baseUrl ?? BaseUrl;
        _logger = logger ?? NullLogger<GuideApiClient>.Instance;
    }

    private string Root => _baseUrl.TrimEnd('/');

    /// <summary>用 SDK 登录结果换 x-token(服务端返回,含动态 innerToken)。</summary>
    public async Task<string?> LoginSdkAsync(string cUid, string cName, string accessToken, CancellationToken ct = default)
    {
        var payload = new GuideLoginSdkRequest { CUid = cUid, CName = cName, AccessToken = accessToken };
        var json = await PostJsonAsync("/user/login/sdk", JsonSerializer.Serialize(payload, GuideJsonContext.Default.GuideLoginSdkRequest), ct).ConfigureAwait(false);
        var env = JsonSerializer.Deserialize(json, GuideJsonContext.Default.GuideEnvelopeGuideLoginToken);
        if (env is not { Code: 200 })
        {
            // 登录链路失败必须显式暴露(原先静默返回 null,上层只提示"未返回 x-token",无法定位真实原因)
            throw new GuideApiException($"guide sdk 登录失败: {env?.Message ?? $"code={env?.Code}"}", env?.Code);
        }
        return env.Data?.Token;
    }

    /// <summary>玩家列表。</summary>
    public async Task<List<GuidePlayerItem>> GetPlayerListAsync(string xToken, CancellationToken ct = default)
    {
        var json = await GetAsync($"/user/player/list?_t={Timestamp()}", xToken, ct).ConfigureAwait(false);
        var env = JsonSerializer.Deserialize(json, GuideJsonContext.Default.GuideEnvelopeListGuidePlayerItem);
        if (env is { Code: 200, Data: not null })
        {
            return env.Data;
        }
        throw new GuideApiException($"获取玩家列表失败: {env?.Message ?? $"code={env?.Code}"}", env?.Code);
    }

    /// <summary>选择玩家。</summary>
    public async Task<GuideChooseData?> ChoosePlayerAsync(string xToken, long playerId, string serverId, CancellationToken ct = default)
    {
        var payload = new GuideChoosePlayerRequest { PlayerId = playerId, ServerId = serverId };
        var json = await PostJsonAsync(
            "/user/player/choose",
            JsonSerializer.Serialize(payload, GuideJsonContext.Default.GuideChoosePlayerRequest),
            xToken,
            ct).ConfigureAwait(false);
        var env = JsonSerializer.Deserialize(json, GuideJsonContext.Default.GuideEnvelopeGuideChooseData);
        if (env is { Code: 200, Data: not null })
        {
            return env.Data;
        }
        throw new GuideApiException($"选择玩家失败: {env?.Message ?? $"code={env?.Code}"}", env?.Code);
    }

    /// <summary>角色攻略列表(按点赞数降序;取第一篇为默认攻略)。</summary>
    public async Task<List<GuideIntroductionItem>> GetIntroductionListAsync(string xToken, string roleGbId, CancellationToken ct = default)
    {
        var path = $"/introduction/list?roleGbId={roleGbId}&_t={Timestamp()}";
        var json = await GetAsync(path, xToken, ct).ConfigureAwait(false);
        var env = DeserializeEnvelope(json, path, GuideJsonContext.Default.GuideEnvelopeListGuideIntroductionItem);
        if (env is { Code: 200, Data: not null })
        {
            return env.Data
                .OrderByDescending(i => i.LikeCount)
                .ToList();
        }
        throw new GuideApiException($"获取攻略列表失败: {env?.Message ?? $"code={env?.Code}"}", env?.Code);
    }

    /// <summary>攻略详情(养成达成度)。</summary>
    public async Task<GuideIntroductionInfo?> GetIntroductionInfoAsync(string xToken, string roleGbId, long id, CancellationToken ct = default)
    {
        var path = $"/introduction/info?roleGbId={roleGbId}&id={id}&_t={Timestamp()}";
        var json = await GetAsync(path, xToken, ct).ConfigureAwait(false);
        var env = DeserializeEnvelope(json, path, GuideJsonContext.Default.GuideEnvelopeGuideIntroductionInfo);
        if (env is { Code: 200 })
        {
            return env.Data;
        }
        throw new GuideApiException($"获取攻略详情失败: {env?.Message ?? $"code={env?.Code}"}", env?.Code);
    }

    /// <summary>反序列化信封;失败时抛出带原始 JSON 的异常(便于定位椿/珂莱塔这类特殊响应)。</summary>
    private static T DeserializeEnvelope<T>(string json, string path, JsonTypeInfo<T> typeInfo)
    {
        try
        {
            return JsonSerializer.Deserialize(json, typeInfo) ?? throw new JsonException("响应 data 为空");
        }
        catch (JsonException ex)
        {
            throw new GuideApiException(
                $"{path} 响应解析失败: {ex.Message}\n原始响应(前500)={Truncate(json, 500)}");
        }
    }

    // ---------------- 私有请求封装 ----------------

    private async Task<string> GetAsync(string path, string xToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Root + path);
        ApplyHeaders(request, xToken);
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        // 每请求完整响应降 Debug + IsEnabled 守卫(默认 Information 门槛不落盘,免急切 Truncate)。
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("mcguide 请求: GET {Path} 响应前300={Resp}", path, Truncate(json));
        }
        return json;
    }

    private async Task<string> PostJsonAsync(string path, string payload, CancellationToken ct)
        => await PostJsonAsync(path, payload, null, ct).ConfigureAwait(false);

    private async Task<string> PostJsonAsync(string path, string payload, string? xToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Root + path)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        ApplyHeaders(request, xToken);
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        // POST body 含登录手机号等凭据:降 Debug,默认不落盘(对齐 KujiequApiClient 的处理)。
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("mcguide 请求: POST {Path} body={Body} 响应前300={Resp}", path, payload, Truncate(json));
        }
        return json;
    }

    private static string Truncate(string s, int max = 300)
        => s.Length <= max ? s : s[..max] + "…";

    private static void ApplyHeaders(HttpRequestMessage request, string? xToken)
    {
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
        request.Headers.TryAddWithoutValidation("Accept-Language", "zh-CN,zh;q=0.9,zh-TW;q=0.8,zh-HK;q=0.7,en-US;q=0.6,en;q=0.5");
        request.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate, br, zstd");
        request.Headers.TryAddWithoutValidation("x-fe-trace-id", FeTraceId);
        request.Headers.TryAddWithoutValidation("x-language", Language);
        if (!string.IsNullOrWhiteSpace(xToken))
        {
            request.Headers.TryAddWithoutValidation("x-token", xToken);
        }
        request.Headers.TryAddWithoutValidation("Origin", Origin);
        request.Headers.TryAddWithoutValidation("Referer", Referer);
    }

    private static string Timestamp()
        => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
}
