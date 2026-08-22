using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using McKuro.Core.Models.Roles;
using McKuro.Core.Models.Tower;
using McKuro.Core.Models.User;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace McKuro.Core.Services.Roles;

/// <summary>库街区 API 异常。</summary>
public sealed class KujiequApiException(string message) : Exception(message);

/// <summary>
/// 库街区 (kurobbs) 数据中心 API 客户端(角色养成数据)。
/// <para>流程与参数对齐 WutheringWavesTool(leck995) 的 <c>com.kuro.kujiequ</c> 包:
/// 先 <c>aki/roleBox/requestToken</c> 换取 B-At 令牌,再用固定 serverId/gameId + 条目 roleId 请求角色数据;
/// 角色详情 getRoleDetail 的 <c>id</c> 传 roleList 每项的角色 ID(cardRoleId)。</para>
/// </summary>
public sealed class KujiequApiClient
{
    public const string BaseUrl = "https://api.kurobbs.com";

    // 端点(与 WutheringWavesTool ApiConfig 一致)
    public const string RequestTokenUrl = BaseUrl + "/aki/roleBox/requestToken";
    public const string RefreshDataUrl = BaseUrl + "/aki/roleBox/akiBox/refreshData";
    public const string RoleDataUrl = BaseUrl + "/aki/roleBox/akiBox/roleData";
    public const string RoleDetailUrl = BaseUrl + "/aki/roleBox/akiBox/getRoleDetail";
    public const string NewTowerUrl = BaseUrl + "/aki/roleBox/akiBox/newTowerDetail";
    public const string SlashUrl = BaseUrl + "/aki/roleBox/akiBox/slashDetail";
    public const string DailyDataUrl = BaseUrl + "/gamer/widget/game3/getData";
    public const string BaseDataUrl = BaseUrl + "/aki/roleBox/akiBox/baseData";

    // 固定参数(对齐 WutheringWavesTool ApiConfig.PARAM_SERVER_ID / PARAM_GAME_ID)
    public const string ParamServerId = "76402e5b20be2c39f095a152090afddc";
    public const string ParamGameId = "3";

    /// <summary>库街区 WebView 安卓 UA(对齐 WutheringWavesTool getBaseBuilder)。</summary>
    private const string AndroidUa =
        "Mozilla/5.0 (Linux; Android 9; 23116PN5BC Build/PQ3A.190605.02201427; wv) "
        + "AppleWebKit/537.36 (KHTML, like Gecko) Version/4.0 "
        + "Chrome/124.0.6367.82 Mobile Safari/537.36 Kuro/2.5.0 KuroGameBox/2.5.0";

    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly ILogger<KujiequApiClient> _logger;

    /// <summary>公网 IP(用于 Devcode 头,对齐 WutheringWavesTool:IP + ", " + UA;为空则仅 UA)。</summary>
    public string? PublicIp { get; set; }

    /// <summary>构造。baseUrl 仅在测试注入本地服务器时覆盖。</summary>
    public KujiequApiClient(HttpClient http, string? baseUrl = null, ILogger<KujiequApiClient>? logger = null)
    {
        _http = http;
        _baseUrl = baseUrl ?? BaseUrl;
        _logger = logger ?? NullLogger<KujiequApiClient>.Instance;
    }

    /// <summary>requestToken 端点(对齐 Haiyu)。</summary>
    public string RequestTokenUrlValue => _baseUrl.TrimEnd('/') + "/aki/roleBox/requestToken";

    /// <summary>refreshData 端点。</summary>
    public string RefreshDataUrlValue => _baseUrl.TrimEnd('/') + "/aki/roleBox/akiBox/refreshData";

    /// <summary>roleData 端点。</summary>
    public string RoleDataUrlValue => _baseUrl.TrimEnd('/') + "/aki/roleBox/akiBox/roleData";

    /// <summary>getRoleDetail 端点。</summary>
    public string RoleDetailUrlValue => _baseUrl.TrimEnd('/') + "/aki/roleBox/akiBox/getRoleDetail";

    /// <summary>newTowerDetail 端点。</summary>
    public string NewTowerUrlValue => _baseUrl.TrimEnd('/') + "/aki/roleBox/akiBox/newTowerDetail";

    /// <summary>slashDetail 端点。</summary>
    public string SlashUrlValue => _baseUrl.TrimEnd('/') + "/aki/roleBox/akiBox/slashDetail";

    /// <summary>getData(每日数据)端点。</summary>
    public string DailyDataUrlValue => _baseUrl.TrimEnd('/') + "/gamer/widget/game3/getData";

    /// <summary>baseData(数据中心玩家基础资料)端点。</summary>
    public string BaseDataUrlValue => _baseUrl.TrimEnd('/') + "/aki/roleBox/akiBox/baseData";

    /// <summary>外层响应信封(data 类型随接口而异:字符串或布尔)。</summary>
    public sealed class KujiequEnvelope
    {
        [JsonPropertyName("code")] public int Code { get; set; }
        [JsonPropertyName("data")] public JsonElement? Data { get; set; }
        [JsonPropertyName("msg")] public string? Msg { get; set; }
        [JsonPropertyName("success")] public bool Success { get; set; }
    }

    /// <summary>requestToken 响应(data 内层)。</summary>
    public sealed class KujiequAccessToken
    {
        [JsonPropertyName("accessToken")] public string AccessToken { get; set; } = "";
    }

    /// <summary>角色列表接口内层(data 内层,对齐 WutheringWavesTool GameRoleDataTask)。</summary>
    public sealed class RoleListEnvelope
    {
        [JsonPropertyName("roleList")] public List<RoleListEnvelopeItem>? RoleList { get; set; }
    }

    /// <summary>roleList 单项(平铺基础信息,对齐 WutheringWavesTool Role)。</summary>
    public sealed class RoleListEnvelopeItem
    {
        [JsonPropertyName("roleId")] public int RoleId { get; set; }
        [JsonPropertyName("roleName")] public string RoleName { get; set; } = "";
        [JsonPropertyName("roleIconUrl")] public string RoleIconUrl { get; set; } = "";
        [JsonPropertyName("rolePicUrl")] public string RolePicUrl { get; set; } = "";
        [JsonPropertyName("level")] public int Level { get; set; }
        [JsonPropertyName("breach")] public int Breach { get; set; }
        [JsonPropertyName("chainUnlockNum")] public int ChainUnlockNum { get; set; }
        [JsonPropertyName("starLevel")] public int StarLevel { get; set; }
        [JsonPropertyName("attributeId")] public int AttributeId { get; set; }
        [JsonPropertyName("attributeName")] public string AttributeName { get; set; } = "";
        [JsonPropertyName("weaponTypeId")] public int WeaponTypeId { get; set; }
        [JsonPropertyName("weaponTypeName")] public string WeaponTypeName { get; set; } = "";
        [JsonPropertyName("acronym")] public string Acronym { get; set; } = "";
    }

    /// <summary>
    /// 换取角色数据访问令牌(requestToken 接口,对齐 WutheringWavesTool BaseTask.requestToken)。
    /// </summary>
    /// <param name="token">库街区登录 token。</param>
    /// <param name="deviceId">设备 ID(did 头)。</param>
    /// <param name="roleId">玩家角色条目 RoleId。</param>
    /// <param name="userId">库街区用户 ID。</param>
    public async Task<string?> GetAccessTokenAsync(
        string token,
        string deviceId,
        string roleId,
        string userId,
        string? source = null,
        CancellationToken ct = default)
    {
        source ??= "android";
        var headers = new Dictionary<string, string>
        {
            { "Accept", "application/json, text/plain, */*" },
            { "Accept-Encoding", "gzip, deflate" },
            { "Accept-Language", "zh-CN,zh;q=0.9,en-US;q=0.8,en;q=0.7" },
            { "devCode", AndroidUa },
            { "did", deviceId },
            { "source", source },
            { "token", token },
        };
        var body = $"serverId={ParamServerId}&roleId={roleId}&userId={userId}";
        // 令牌换取失败不抛异常(静默返回 null,由上层给出友好提示)
        var env = await SendEnvelopeAsync(RequestTokenUrlValue, headers, body, ct, throwOnError: false).ConfigureAwait(false);
        var dataStr = GetDataString(env);
        if (dataStr is null)
        {
            _logger.LogWarning("requestToken 返回空 data: roleId={RoleId} code={Code} msg={Msg}",
                roleId, env?.Code, env?.Msg);
            return null;
        }
        var access = JsonSerializer.Deserialize(dataStr, KujiequJsonContext.Default.KujiequAccessToken);
        return string.IsNullOrEmpty(access?.AccessToken) ? null : access.AccessToken;
    }

    /// <summary>
    /// 获取角色列表基础信息(roleData 接口,对齐 WutheringWavesTool GameRoleDataTask)。
    /// </summary>
    public async Task<IReadOnlyList<RoleDetail>> GetRoleDataAsync(
        string accessToken,
        string deviceId,
        string roleId,
        string? source = null,
        CancellationToken ct = default)
    {
        var headers = BuildWebHeader(accessToken, deviceId);
        var body = $"serverId={ParamServerId}&roleId={roleId}&gameId={ParamGameId}";
        var env = await SendEnvelopeAsync(RoleDataUrlValue, headers, body, ct).ConfigureAwait(false);
        var dataStr = GetDataString(env);
        if (dataStr is null)
        {
            return [];
        }

        var list = JsonSerializer.Deserialize(dataStr, KujiequJsonContext.Default.RoleListEnvelope);
        if (list?.RoleList is null)
        {
            _logger.LogWarning("roleData 返回空 roleList: data={Data}", Truncate(dataStr, 300));
            return [];
        }

        _logger.LogInformation("roleData 角色列表: 共{Count}项 首项roleId={FirstRoleId} 条目roleId={EntryRoleId}",
            list.RoleList.Count, list.RoleList.FirstOrDefault()?.RoleId, roleId);

        var result = new List<RoleDetail>(list.RoleList.Count);
        foreach (var item in list.RoleList)
        {
            result.Add(new RoleDetail
            {
                Level = item.Level,
                Role = new RoleInfo
                {
                    RoleId = item.RoleId,
                    RoleName = item.RoleName,
                    RoleIconUrl = item.RoleIconUrl,
                    RolePicUrl = item.RolePicUrl,
                    Level = item.Level,
                    Breach = item.Breach,
                    ChainUnlockNum = item.ChainUnlockNum,
                    StarLevel = item.StarLevel,
                    AttributeId = item.AttributeId,
                    AttributeName = item.AttributeName,
                    WeaponTypeId = item.WeaponTypeId,
                    WeaponTypeName = item.WeaponTypeName,
                    Acronym = item.Acronym,
                },
            });
        }
        return result;
    }

    /// <summary>
    /// 获取单个角色完整养成详情(getRoleDetail 接口,对齐 WutheringWavesTool GameRoleDetailTask)。
    /// </summary>
    /// <param name="accessToken">B-At 令牌。</param>
    /// <param name="deviceId">设备 ID(did 头)。</param>
    /// <param name="roleId">玩家角色条目 RoleId(body roleId)。</param>
    /// <param name="targetRoleId">目标角色 ID(roleList 项 roleId,body id)。</param>
    public async Task<RoleDetail?> GetRoleDetailAsync(
        string accessToken,
        string deviceId,
        string roleId,
        int targetRoleId,
        string? source = null,
        CancellationToken ct = default)
    {
        var headers = BuildWebHeader(accessToken, deviceId);
        // body: 固定 serverId/gameId + 条目 roleId + id=该角色在 roleList 中的 roleId(对齐 GameRoleDetailTask)
        var body = $"serverId={ParamServerId}&roleId={roleId}&gameId={ParamGameId}&id={targetRoleId}&channelId=19&countryCode=1";
        var env = await SendEnvelopeAsync(RoleDetailUrlValue, headers, body, ct).ConfigureAwait(false);
        var dataStr = GetDataString(env);
        if (dataStr is null)
        {
            _logger.LogWarning("getRoleDetail 返回空 data: roleId={RoleId} id={TargetRoleId} code={Code} msg={Msg}",
                roleId, targetRoleId, env?.Code, env?.Msg);
            return null;
        }
        return JsonSerializer.Deserialize(dataStr, RoleJsonContext.Default.RoleDetail);
    }

    /// <summary>
    /// 获取深塔(终焉矩阵)数据(newTowerDetail 接口,对齐 WutheringWavesTool NewTowerDataDetailTask)。
    /// </summary>
    public async Task<NewTowerData?> GetNewTowerAsync(
        string accessToken,
        string deviceId,
        string roleId,
        string? source = null,
        CancellationToken ct = default)
    {
        var headers = BuildWebHeader(accessToken, deviceId);
        var body = $"serverId={ParamServerId}&roleId={roleId}";
        var env = await SendEnvelopeAsync(NewTowerUrlValue, headers, body, ct).ConfigureAwait(false);
        var dataStr = GetDataString(env);
        if (dataStr is null)
        {
            _logger.LogWarning("newTowerDetail 返回空 data: roleId={RoleId} code={Code} msg={Msg}",
                roleId, env?.Code, env?.Msg);
            return null;
        }
        return JsonSerializer.Deserialize(dataStr, TowerJsonContext.Default.NewTowerData);
    }

    /// <summary>
    /// 获取海墟(再生海域)数据(slashDetail 接口,对齐 WutheringWavesTool SlashDataDetailTask)。
    /// </summary>
    public async Task<SlashData?> GetSlashAsync(
        string accessToken,
        string deviceId,
        string roleId,
        string? source = null,
        CancellationToken ct = default)
    {
        var headers = BuildWebHeader(accessToken, deviceId);
        // 海墟参数在 query(无 body),对齐 WutheringWavesTool SlashDataDetailTask
        var query = $"gameId={ParamGameId}&serverId={ParamServerId}&roleId={roleId}";
        var env = await SendEnvelopeAsync(SlashUrlValue + "?" + query, headers, "", ct).ConfigureAwait(false);
        var dataStr = GetDataString(env);
        if (dataStr is null)
        {
            _logger.LogWarning("slashDetail 返回空 data: roleId={RoleId} code={Code} msg={Msg}",
                roleId, env?.Code, env?.Msg);
            return null;
        }
        return JsonSerializer.Deserialize(dataStr, TowerJsonContext.Default.SlashData);
    }

    /// <summary>
    /// 获取角色每日数据(体力/活跃度/周本等,getData 接口,对齐 WutheringWavesTool UserDailyDataTask)。
    /// 参数走 query,data 为对象(非字符串)。
    /// </summary>
    public async Task<RoleDailyData?> GetRoleDailyDataAsync(
        string accessToken,
        string deviceId,
        string roleId,
        string userId,
        string? source = null,
        CancellationToken ct = default)
    {
        var headers = BuildWebHeader(accessToken, deviceId);
        var query = $"type=2&roleId={roleId}&sizeType=1&gameId={ParamGameId}&serverId={ParamServerId}";
        var env = await SendEnvelopeAsync(DailyDataUrlValue + "?" + query, headers, "", ct).ConfigureAwait(false);
        if (env is null || env.Data is not { } data || data.ValueKind != JsonValueKind.Object)
        {
            _logger.LogWarning("getData 返回空 data: roleId={RoleId} code={Code} msg={Msg}",
                roleId, env?.Code, env?.Msg);
            return null;
        }
        try
        {
            return JsonSerializer.Deserialize(data.GetRawText(), UserJsonContext.Default.RoleDailyData);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "getData 反序列化失败: roleId={RoleId}", roleId);
            return null;
        }
    }

    /// <summary>
    /// 获取数据中心玩家基础资料(akiBox/baseData,对齐 Haiyu GetGamerBassDataAsync):
    /// 游玩天数/注册时间/等级/周本(战歌重奏)图标等;失败返回 null。
    /// </summary>
    public async Task<GamerBaseData?> GetGamerBaseDataAsync(
        string accessToken,
        string deviceId,
        string roleId,
        string? source = null,
        CancellationToken ct = default)
    {
        var headers = BuildWebHeader(accessToken, deviceId);
        var body = $"gameId={ParamGameId}&roleId={roleId}&serverId={ParamServerId}&channelId=19&countryCode=1";
        KujiequEnvelope? env;
        try
        {
            env = await SendEnvelopeAsync(BaseDataUrlValue, headers, body, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "baseData 请求异常: roleId={RoleId}", roleId);
            return null;
        }
        var dataStr = GetDataString(env);
        if (dataStr is null)
        {
            _logger.LogWarning("baseData 返回空 data: roleId={RoleId} code={Code} msg={Msg}",
                roleId, env?.Code, env?.Msg);
            return null;
        }
        return JsonSerializer.Deserialize(dataStr, UserJsonContext.Default.GamerBaseData);
    }

    /// <summary>
    /// 刷新服务器角色缓存(refreshData 接口,对齐 WutheringWavesTool ApiConfig.REFRESH_URL)。
    /// </summary>
    public async Task<bool> RefreshDataAsync(
        string accessToken,
        string deviceId,
        string roleId,
        string? source = null,
        CancellationToken ct = default)
    {
        var headers = BuildWebHeader(accessToken, deviceId);
        var body = $"serverId={ParamServerId}&roleId={roleId}&gameId={ParamGameId}";
        var env = await SendEnvelopeAsync(RefreshDataUrlValue, headers, body, ct).ConfigureAwait(false);
        return env is { Code: 200 };
    }

    /// <summary>构造数据中心请求头(对齐 WutheringWavesTool getBuilder:B-At + Devcode + Did + Source)。</summary>
    private Dictionary<string, string> BuildWebHeader(string accessToken, string deviceId)
    {
        // Devcode:公网 IP + ", " + UA(对齐 WutheringWavesTool getDevCode,含 IP 特征避免风控)
        var devCode = string.IsNullOrWhiteSpace(PublicIp)
            ? AndroidUa
            : $"{PublicIp}, {AndroidUa}";
        return new Dictionary<string, string>
        {
            { "Accept", "application/json, text/plain, */*" },
            { "Accept-Encoding", "gzip, deflate" },
            { "Accept-Language", "zh-CN,zh;q=0.9,en-US;q=0.8,en;q=0.7" },
            { "User-Agent", AndroidUa },
            { "did", deviceId },
            { "source", "android" },
            { "devCode", devCode },
            { "b-at", accessToken },
        };
    }

    /// <summary>从信封取 data 字符串;data 缺失/非字符串(如 refreshData 的布尔)时返回 null。</summary>
    private static string? GetDataString(KujiequEnvelope? env)
    {
        if (env is null || env.Data is not { } data || data.ValueKind != JsonValueKind.String)
        {
            return null;
        }
        return data.GetString();
    }

    /// <summary>发送 POST 并解析外层信封;throwOnError 且 code != 200 时抛 <see cref="KujiequApiException"/>。</summary>
    private async Task<KujiequEnvelope?> SendEnvelopeAsync(
        string url,
        Dictionary<string, string> headers,
        string body,
        CancellationToken ct,
        bool throwOnError = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        foreach (var (key, value) in headers)
        {
            request.Headers.TryAddWithoutValidation(key, value);
        }
        request.Content = new StringContent(body, Encoding.UTF8, "application/x-www-form-urlencoded");

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        _logger.LogInformation("库街区数据中心请求: url={Url} body={Body} 响应前300={Resp}", url, body, Truncate(json, 300));

        var env = JsonSerializer.Deserialize(json, KujiequJsonContext.Default.KujiequEnvelope);
        if (throwOnError && env is not null && env.Code != 200)
        {
            throw new KujiequApiException($"库街区接口返回错误: {env.Msg}");
        }
        return env;
    }

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : s[..max] + "…";
}

[JsonSerializable(typeof(KujiequApiClient.KujiequEnvelope))]
[JsonSerializable(typeof(KujiequApiClient.KujiequAccessToken))]
[JsonSerializable(typeof(KujiequApiClient.RoleListEnvelope))]
[JsonSerializable(typeof(KujiequApiClient.RoleListEnvelopeItem))]
public sealed partial class KujiequJsonContext : JsonSerializerContext;
