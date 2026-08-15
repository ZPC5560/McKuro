using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using donet.Core.Models.Roles;

namespace donet.Core.Services.Roles;

/// <summary>库街区 API 异常。</summary>
public sealed class KujiequApiException(string message) : Exception(message);

/// <summary>
/// 库街区 (kurobbs) API 客户端,获取账号角色养成数据。
/// <para>接口端点与请求方式参考 WutheringWavesTool 的 com.kuro.kujiequ。</para>
/// <para>注意:官方接口自 2024 年起响应不再加密,可直接解析 JSON。</para>
/// </summary>
public sealed class KujiequApiClient
{
    public const string BaseUrl = "https://api.kurobbs.com";

    // 端点
    public const string RefreshDataUrl = BaseUrl + "/aki/roleBox/akiBox/refreshData";
    public const string RoleDataUrl = BaseUrl + "/aki/roleBox/akiBox/roleData";
    public const string RoleDetailUrl = BaseUrl + "/aki/roleBox/akiBox/getRoleDetail";
    public const string GameDataUrl = BaseUrl + "/gamer/widget/game3/refresh";
    public const string BaseDataUrl = BaseUrl + "/aki/roleBox/akiBox/baseData";

    // 固定参数
    public const string ParamServerId = "76402e5b20be2c39f095a152090afddc";
    public const string ParamGameId = "3";

    private static readonly string[] SourceValues = ["android", "h5", "ios", "web"];

    private readonly HttpClient _http;

    public KujiequApiClient(HttpClient http)
    {
        _http = http;
    }

    private HttpRequestMessage BuildPost(string url, string? token, string? source, string? body = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36 Edg/126.0.0.0");
        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.TryAddWithoutValidation("token", token);
        }
        if (!string.IsNullOrEmpty(source))
        {
            request.Headers.TryAddWithoutValidation("source", source);
        }

        if (body is not null)
        {
            request.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/x-www-form-urlencoded");
        }
        else
        {
            request.Content = new StringContent("", System.Text.Encoding.UTF8, "application/x-www-form-urlencoded");
        }
        return request;
    }

    /// <summary>
    /// 刷新服务器角色缓存(获取最新数据前调用)。
    /// </summary>
    public async Task<bool> RefreshDataAsync(
        string token,
        string roleId,
        string? source = null,
        CancellationToken ct = default)
    {
        source ??= SourceValues[0];
        var body = $"serverId={ParamServerId}&roleId={roleId}&gameId={ParamGameId}";
        using var request = BuildPost(RefreshDataUrl, token, source, body);
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("code", out var code) && code.GetInt32() == 0;
    }

    /// <summary>
    /// 获取当前账号的角色养成数据(roleData 接口)。
    /// </summary>
    public async Task<IReadOnlyList<RoleDetail>> GetRoleDataAsync(
        string token,
        string roleId,
        string? source = null,
        CancellationToken ct = default)
    {
        source ??= SourceValues[0];
        var body = $"serverId={ParamServerId}&roleId={roleId}&gameId={ParamGameId}";
        using var request = BuildPost(RoleDataUrl, token, source, body);
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return ParseRoleData(json);
    }

    private static IReadOnlyList<RoleDetail> ParseRoleData(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("code", out var code) && code.GetInt32() != 0)
        {
            var msg = root.TryGetProperty("msg", out var m) ? m.GetString() : "";
            throw new KujiequApiException($"库街区接口返回错误: {msg}");
        }

        var roles = new List<RoleDetail>();
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
        {
            return roles;
        }

        // data 可能是 { roleData: [...] } 或直接是数组
        JsonElement roleDataArray;
        if (data.TryGetProperty("roleData", out var rd) && rd.ValueKind == JsonValueKind.Array)
        {
            roleDataArray = rd;
        }
        else if (data.ValueKind == JsonValueKind.Array)
        {
            roleDataArray = data;
        }
        else
        {
            return roles;
        }

        var options = new JsonSerializerOptions { TypeInfoResolver = RoleJsonContext.Default };
        foreach (var element in roleDataArray.EnumerateArray())
        {
            try
            {
                var detail = element.Deserialize(RoleJsonContext.Default.RoleDetail);
                if (detail is not null)
                {
                    roles.Add(detail);
                }
            }
            catch (Exception)
            {
                // 单条解析失败跳过
            }
        }

        return roles;
    }

    /// <summary>
    /// 获取玩家基础信息(baseData 接口)。
    /// </summary>
    public async Task<JsonNode?> GetBaseDataAsync(
        string token,
        string roleId,
        string? source = null,
        CancellationToken ct = default)
    {
        source ??= SourceValues[0];
        var body = $"serverId={ParamServerId}&roleId={roleId}&gameId={ParamGameId}";
        using var request = BuildPost(BaseDataUrl, token, source, body);
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return JsonNode.Parse(json);
    }
}

[JsonSerializable(typeof(RoleDetail))]
[JsonSerializable(typeof(RoleInfo))]
[JsonSerializable(typeof(WeaponData))]
[JsonSerializable(typeof(WeaponInfo))]
[JsonSerializable(typeof(SkillInfo))]
[JsonSerializable(typeof(ChainInfo))]
[JsonSerializable(typeof(EchoInfo))]
[JsonSerializable(typeof(PhantomData))]
[JsonSerializable(typeof(RoleAttribute))]
[JsonSerializable(typeof(RoleDataResponse))]
[JsonSerializable(typeof(List<RoleDetail>))]
public sealed partial class RoleJsonContext : JsonSerializerContext;
