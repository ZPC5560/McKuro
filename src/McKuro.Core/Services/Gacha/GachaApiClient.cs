using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using McKuro.Core.Models.Gacha;
namespace McKuro.Core.Services.Gacha;

/// <summary>
/// 鸣潮抽卡记录查询客户端(gmserver-api)。
/// <para>国服: https://gmserver-api.aki-game2.com/gacha/record/query</para>
/// <para>国际服: https://gmserver-api.aki-game2.net/gacha/record/query</para>
/// </summary>
public sealed class GachaApiClient
{
    public const string CnEndpoint = "https://gmserver-api.aki-game2.com/gacha/record/query";
    public const string GlobalEndpoint = "https://gmserver-api.aki-game2.net/gacha/record/query";

    private readonly HttpClient _http;
    private readonly Microsoft.Extensions.Logging.ILogger<GachaApiClient> _logger;

    public GachaApiClient(HttpClient http, Microsoft.Extensions.Logging.ILogger<GachaApiClient>? logger = null)
    {
        _http = http;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<GachaApiClient>.Instance;
    }

    /// <summary>查询指定卡池类型的抽卡记录。</summary>
    public async Task<IReadOnlyList<GachaRecord>> QueryAsync(
        GachaRecordRequest request,
        CardPoolType poolType,
        CancellationToken ct = default)
    {
        var body = new GachaQueryRequest
        {
            PlayerId = request.PlayerId,
            RecordId = request.RecordId,
            CardPoolId = request.CardPoolId,
            CardPoolType = (int)poolType,
            ServerId = request.ServerId,
            LanguageCode = request.Language,
        };

        var endpoint = request.IsChinaServer ? CnEndpoint : GlobalEndpoint;
        using var req = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(body, GachaJsonContext.Default.GachaQueryRequest),
        };
        // gmserver-api 需要浏览器 UA(无 UA 会返回"请求游戏获取日志异常")
        req.Headers.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/139.0.0.0 Safari/537.36 Edg/139.0.0.0");
        using var response = await _http.SendAsync(req, ct).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        GachaQueryResponse? result;
        try
        {
            result = JsonSerializer.Deserialize(content, GachaJsonContext.Default.GachaQueryResponse);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "抽卡记录响应解析失败,响应前400: {Body}", content.Length <= 400 ? content : content[..400]);
            throw;
        }
        if (result is null)
        {
            return [];
        }
        if (result.Code != 0)
        {
            throw new GachaApiException($"查询抽卡记录失败: {result.Msg}");
        }

        var records = result.Data ?? [];
        foreach (var record in records)
        {
            record.PlayerId = request.PlayerId;
        }
        return records;
    }

    /// <summary>查询全部卡池类型的记录。</summary>
    public async Task<IReadOnlyDictionary<CardPoolType, IReadOnlyList<GachaRecord>>> QueryAllAsync(
        GachaRecordRequest request,
        CancellationToken ct = default)
    {
        var result = new Dictionary<CardPoolType, IReadOnlyList<GachaRecord>>();
        foreach (var type in CardPoolTypeValues.All)
        {
            try
            {
                var records = await QueryAsync(request, type, ct).ConfigureAwait(false);
                result[type] = records;
            }
            catch (GachaApiException)
            {
                // 单个卡池失败不中断整体
                result[type] = [];
            }
        }
        return result;
    }
}

/// <summary>gmserver-api 异常。</summary>
public sealed class GachaApiException(string message) : Exception(message);

[JsonSerializable(typeof(GachaQueryRequest))]
[JsonSerializable(typeof(GachaQueryResponse))]
[JsonSerializable(typeof(GachaRecord))]
[JsonSerializable(typeof(List<GachaRecord>))]
public sealed partial class GachaJsonContext : JsonSerializerContext;
