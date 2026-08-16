using System.Text.Json.Serialization;

namespace McKuro.Core.Models.CloudGame;

/// <summary>云游戏 API 统一响应。</summary>
public sealed class CloudApiResponse<T>
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("msg")]
    public string? Msg { get; set; }

    [JsonPropertyName("data")]
    public T? Data { get; set; }

    [JsonPropertyName("timestamp")]
    public long? Timestamp { get; set; }
}

/// <summary>云游戏画质档位。</summary>
public enum CloudQualityType
{
    /// <summary>流畅。</summary>
    Smooth = 0,
    /// <summary>清晰。</summary>
    Clarity = 1,
}

/// <summary>串流画质选项。</summary>
public sealed record StreamQualityOptions(
    int BitRate,
    int BitRateMin,
    int Fps,
    int Width,
    int Height,
    int CodecType,
    string StreamStrategy,
    bool EnableImageEnhancement,
    int DPI,
    CloudQualityType Type = CloudQualityType.Clarity)
{
    public int BitRateMax => BitRate;
    public string ResolutionKey => $"{Width}x{Height}";
}

/// <summary>云游戏 SDK 登录结果(手机验证码登录)。</summary>
public sealed class CloudGameLoginData
{
    [JsonPropertyName("username")]
    public string? Username { get; set; }

    [JsonPropertyName("sdkuserid")]
    public string? Sdkuserid { get; set; }

    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("loginType")]
    public int LoginType { get; set; }

    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("idStat")]
    public int IdStat { get; set; }

    [JsonPropertyName("age")]
    public int Age { get; set; }

    [JsonPropertyName("cuid")]
    public string? Cuid { get; set; }

    [JsonPropertyName("showPaw")]
    public bool ShowPaw { get; set; }

    [JsonPropertyName("bindDevStat")]
    public int BindDevStat { get; set; }

    [JsonPropertyName("autoToken")]
    public string? AutoToken { get; set; }

    [JsonPropertyName("autoTokenStatus")]
    public bool AutoTokenStatus { get; set; }

    [JsonPropertyName("firstLgn")]
    public int FirstLgn { get; set; }

    [JsonPropertyName("phoneCheck")]
    public int PhoneCheck { get; set; }

    [JsonPropertyName("phone")]
    public string? Phone { get; set; }

    [JsonPropertyName("phoneToken")]
    public string? PhoneToken { get; set; }

    [JsonPropertyName("loginDid")]
    public string? LoginDid { get; set; }
}

/// <summary>云游戏手机 Token 数据。</summary>
public sealed class PhoneTokenData
{
    [JsonPropertyName("username")]
    public string? Username { get; set; }

    [JsonPropertyName("sdkuserid")]
    public string? Sdkuserid { get; set; }

    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("loginType")]
    public int LoginType { get; set; }

    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("idStat")]
    public int IdStat { get; set; }

    [JsonPropertyName("age")]
    public int Age { get; set; }

    [JsonPropertyName("cuid")]
    public string? Cuid { get; set; }

    [JsonPropertyName("showPaw")]
    public bool ShowPaw { get; set; }

    [JsonPropertyName("bindDevStat")]
    public int BindDevStat { get; set; }

    [JsonPropertyName("autoToken")]
    public string? AutoToken { get; set; }

    [JsonPropertyName("autoTokenStatus")]
    public bool AutoTokenStatus { get; set; }

    [JsonPropertyName("firstLgn")]
    public int FirstLgn { get; set; }

    [JsonPropertyName("phoneCheck")]
    public int PhoneCheck { get; set; }

    [JsonPropertyName("phone")]
    public string? Phone { get; set; }

    [JsonPropertyName("phoneToken")]
    public string? PhoneToken { get; set; }
}

/// <summary>SDK 访问令牌。</summary>
public sealed class AccessData
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }
}

/// <summary>云游戏平台登录数据。</summary>
public sealed class EndLoginData
{
    [JsonPropertyName("token")]
    public string? Token { get; set; }

    [JsonPropertyName("uniqueId")]
    public string? UniqueId { get; set; }

    [JsonPropertyName("walletData")]
    public WalletData? WalletData { get; set; }

    [JsonPropertyName("hsstsToken")]
    public HsstsToken? HsstsToken { get; set; }
}

public sealed class HsstsToken
{
    [JsonPropertyName("ak")]
    public string? Ak { get; set; }

    [JsonPropertyName("sk")]
    public string? Sk { get; set; }

    [JsonPropertyName("token")]
    public string? Token { get; set; }
}

public sealed class WalletData
{
    [JsonPropertyName("freeTimeInfo")]
    public FreeTimeInfo? FreeTimeInfo { get; set; }

    [JsonPropertyName("payTimeInfo")]
    public PayTimeInfo? PayTimeInfo { get; set; }

    [JsonPropertyName("timeCardInfo")]
    public TimeCardInfo? TimeCardInfo { get; set; }

    [JsonPropertyName("experienceCardInfo")]
    public ExperienceCardInfo? ExperienceCardInfo { get; set; }

    [JsonPropertyName("coin")]
    public int Coin { get; set; }
}

public sealed class FreeTimeInfo
{
    [JsonPropertyName("leftSeconds")]
    public int LeftSeconds { get; set; }
}

public sealed class PayTimeInfo
{
    [JsonPropertyName("leftSeconds")]
    public int LeftSeconds { get; set; }
}

public sealed class TimeCardInfo
{
    [JsonPropertyName("expireTimeSeconds")]
    public int ExpireTimeSeconds { get; set; }
}

public sealed class ExperienceCardInfo
{
    [JsonPropertyName("day")]
    public int Day { get; set; }

    [JsonPropertyName("hour")]
    public int Hour { get; set; }

    [JsonPropertyName("minute")]
    public int Minute { get; set; }

    [JsonPropertyName("second")]
    public int Second { get; set; }
}

/// <summary>云游戏平台登录请求。</summary>
public sealed class EndLoginRequest
{
    [JsonPropertyName("loginType")]
    public int LoginType { get; set; }

    [JsonPropertyName("userId")]
    public string? UserId { get; set; }

    [JsonPropertyName("userName")]
    public string? UserName { get; set; }

    [JsonPropertyName("token")]
    public string? Token { get; set; }

    [JsonPropertyName("deviceId")]
    public string? DeviceId { get; set; }

    [JsonPropertyName("platform")]
    public string? Platform { get; set; }

    [JsonPropertyName("appVersion")]
    public string? AppVersion { get; set; }
}

/// <summary>发送短信验证码结果。</summary>
public sealed class CloudSendSMS
{
    [JsonPropertyName("codes")]
    public int Codes { get; set; }

    [JsonPropertyName("error_description")]
    public string? ErrorDescription { get; set; }
}

/// <summary>云游戏节点。</summary>
public sealed class CloudGameNode
{
    [JsonPropertyName("regionName")]
    public string? RegionName { get; set; }

    [JsonPropertyName("regionDelay")]
    public int RegionDelay { get; set; }

    [JsonPropertyName("regionScore")]
    public int RegionScore { get; set; }

    [JsonPropertyName("regionState")]
    public int RegionState { get; set; }

    [JsonPropertyName("fastWaiting")]
    public int FastWaiting { get; set; }

    [JsonPropertyName("slowWaiting")]
    public int SlowWaiting { get; set; }

    public int Delay { get; private set; }

    [JsonPropertyName("nodeList")]
    public List<NodeList>? NodeList
    {
        get => _nodeList;
        set
        {
            _nodeList = value;
            Delay = value is { Count: > 0 } ? value.Sum(x => x.Delay) : 0;
        }
    }

    private List<NodeList>? _nodeList;
}

public sealed class NodeList
{
    [JsonPropertyName("nodeId")]
    public string? NodeId { get; set; }

    [JsonPropertyName("delay")]
    public int Delay { get; set; }
}

/// <summary>节点测速网络配置。</summary>
public sealed class CloudNetworkOrgin
{
    [JsonPropertyName("lines")]
    public List<CloudNetworkOrginItem>? Lines { get; set; }

    [JsonPropertyName("nodeRefreshTime")]
    public string? NodeRefreshTime { get; set; }

    [JsonPropertyName("pingNum")]
    public string? PingNum { get; set; }

    [JsonPropertyName("timeOut")]
    public string? TimeOut { get; set; }
}

public sealed class CloudNetworkOrginItem
{
    [JsonPropertyName("nodeName")]
    public string? NodeName { get; set; }

    [JsonPropertyName("lineId")]
    public string? LineId { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("lineH5Port")]
    public string? LineH5Port { get; set; }

    [JsonPropertyName("nodeAlias")]
    public string? NodeAlias { get; set; }

    [JsonPropertyName("lineH5Addr")]
    public string? LineH5Addr { get; set; }

    [JsonPropertyName("nodeId")]
    public string? NodeId { get; set; }

    [JsonPropertyName("status")]
    public int Status { get; set; }
}

public sealed class CloudNetworkDelayItem
{
    public string? NodeId { get; set; }
    public string? NodeName { get; set; }
    public string? Addr { get; set; }
    public string? Port { get; set; }
    public int Delay { get; set; }
}

/// <summary>启动进入请求数据。</summary>
public sealed class CloudBizData
{
    [JsonPropertyName("btype")]
    public string Btype { get; set; } = string.Empty;

    [JsonPropertyName("os")]
    public string Os { get; set; } = "WINDOWS";

    [JsonPropertyName("osVer")]
    public string OsVer { get; set; } = string.Empty;

    [JsonPropertyName("clientVer")]
    public string ClientVer { get; set; } = string.Empty;

    [JsonPropertyName("osCategory")]
    public string OsCategory { get; set; } = "H5";

    [JsonPropertyName("isOneLine")]
    public int IsOneLine { get; set; } = 1;

    [JsonPropertyName("extSDK")]
    public string ExtSdk { get; set; } = "{\"certHash\":true}";

    [JsonPropertyName("ping")]
    public IEnumerable<BizCloudNode>? BizCloudNodes { get; set; }

    public CloudBizData(string osVer, string clientVer, IEnumerable<BizCloudNode> bizCloudNodes)
    {
        OsVer = osVer;
        ClientVer = clientVer;
        BizCloudNodes = bizCloudNodes;
    }
}

public sealed class BizCloudNode
{
    [JsonPropertyName("nodeId")]
    public string? NodeId { get; set; }

    [JsonPropertyName("result")]
    public string? Result { get; set; }
}

/// <summary>启动游戏请求模型。</summary>
public sealed class CommStartModel
{
    [JsonPropertyName("nodeList")]
    public List<NodeList>? NodeList { get; set; }

    [JsonPropertyName("payType")]
    public int PayType { get; set; }

    [JsonPropertyName("resourceData")]
    public ResourceData? ResourceData { get; set; }
}

public sealed class ResourceData
{
    [JsonPropertyName("wlResourceData")]
    public WlResourceData? WlResourceData { get; set; }
}

public sealed class WlResourceData
{
    [JsonPropertyName("bizData")]
    public string? BizData { get; set; }

    [JsonPropertyName("bitRate")]
    public int BitRate { get; set; }

    [JsonPropertyName("cmdLine")]
    public string? CmdLine { get; set; }

    [JsonPropertyName("codecType")]
    public int CodecType { get; set; }

    [JsonPropertyName("fps")]
    public int Fps { get; set; }

    [JsonPropertyName("gameId")]
    public string? GameId { get; set; }

    [JsonPropertyName("resolution")]
    public string? Resolution { get; set; }

    [JsonPropertyName("tenantKey")]
    public string? TenantKey { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }
}

/// <summary>启动结果。</summary>
public sealed class CommStartReponse
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("msg")]
    public string? Msg { get; set; }

    [JsonPropertyName("providerType")]
    public int ProviderType { get; set; }

    [JsonPropertyName("regionName")]
    public string? RegionName { get; set; }

    [JsonPropertyName("dispatchResult")]
    public DispatchResult? DispatchResult { get; set; }
}

/// <summary>排队信息。</summary>
public sealed class CommonQueueInfo
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("msg")]
    public string? Msg { get; set; }

    [JsonPropertyName("providerType")]
    public int ProviderType { get; set; }

    [JsonPropertyName("regionName")]
    public string? RegionName { get; set; }

    [JsonPropertyName("dispatchResult")]
    public DispatchResult? DispatchResult { get; set; }

    [JsonPropertyName("seatNo")]
    public int SeatNo { get; set; }

    [JsonPropertyName("waitingTime")]
    public int WaitingTime { get; set; }
}

public sealed class DispatchResult
{
    [JsonPropertyName("dispatchMsg")]
    public string? DispatchMsg { get; set; }

    [JsonPropertyName("roundId")]
    public string? RoundId { get; set; }

    [JsonPropertyName("reservedId")]
    public string? ReservedId { get; set; }

    [JsonPropertyName("userKey")]
    public string? UserKey { get; set; }

    [JsonPropertyName("deviceId")]
    public string? DeviceId { get; set; }

    [JsonPropertyName("allocRespJson")]
    public string? AllocRespJson { get; set; }

    [JsonPropertyName("tk")]
    public string? Tk { get; set; }
}

/// <summary>云游戏抽卡记录信息。</summary>
public sealed class RecordData
{
    [JsonPropertyName("playerId")]
    public int PlayerId { get; set; }

    [JsonPropertyName("recordId")]
    public string? RecordId { get; set; }
}

public sealed class RecardQuery
{
    [JsonPropertyName("playerId")]
    public string? PlayerId { get; set; }

    [JsonPropertyName("cardPoolId")]
    public string? CardPoolId { get; set; }

    [JsonPropertyName("cardPoolType")]
    public int CardPoolType { get; set; }

    [JsonPropertyName("serverId")]
    public string? ServerId { get; set; }

    [JsonPropertyName("languageCode")]
    public string? LanguageCode { get; set; }

    [JsonPropertyName("recordId")]
    public string? RecordId { get; set; }
}

/// <summary>云游戏抽卡记录查询响应。</summary>
public sealed class PlayerReponse
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("data")]
    public List<CloudGachaItem>? Data { get; set; }
}

public sealed class CloudGachaItem
{
    [JsonPropertyName("cardPoolType")]
    public string? CardPoolType { get; set; }

    [JsonPropertyName("resourceId")]
    public int ResourceId { get; set; }

    [JsonPropertyName("qualityLevel")]
    public int QualityLevel { get; set; }

    [JsonPropertyName("resourceType")]
    public string? ResourceType { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("time")]
    public string? Time { get; set; }
}

/// <summary>云游戏登录会话(简化,不含 ObservableObject)。</summary>
public sealed class CloudGameLoginSession
{
    public CloudGameLoginData? OrginData { get; set; }
    public PhoneTokenData? PhoneToken { get; set; }
    public AccessData? AccessData { get; set; }
    public EndLoginData? EndLoginData { get; set; }
    public string TraceId { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime SaveTime { get; set; } = DateTime.Now;
    public string GetId() => OrginData?.Username ?? "";
}

[JsonSerializable(typeof(CloudGameLoginData))]
[JsonSerializable(typeof(CloudApiResponse<CloudGameLoginData>))]
[JsonSerializable(typeof(CloudSendSMS))]
[JsonSerializable(typeof(AccessData))]
[JsonSerializable(typeof(CloudApiResponse<AccessData>))]
[JsonSerializable(typeof(PhoneTokenData))]
[JsonSerializable(typeof(CloudApiResponse<PhoneTokenData>))]
[JsonSerializable(typeof(EndLoginData))]
[JsonSerializable(typeof(CloudApiResponse<EndLoginData>))]
[JsonSerializable(typeof(EndLoginRequest))]
[JsonSerializable(typeof(WalletData))]
[JsonSerializable(typeof(CloudApiResponse<WalletData>))]
[JsonSerializable(typeof(CloudGameNode))]
[JsonSerializable(typeof(List<CloudGameNode>))]
[JsonSerializable(typeof(CloudApiResponse<List<CloudGameNode>>))]
[JsonSerializable(typeof(CloudApiResponse<bool>))]
[JsonSerializable(typeof(CloudApiResponse<bool?>))]
[JsonSerializable(typeof(NodeList))]
[JsonSerializable(typeof(List<NodeList>))]
[JsonSerializable(typeof(CloudNetworkOrgin))]
[JsonSerializable(typeof(CloudNetworkOrginItem))]
[JsonSerializable(typeof(List<CloudNetworkOrginItem>))]
[JsonSerializable(typeof(CloudBizData))]
[JsonSerializable(typeof(BizCloudNode))]
[JsonSerializable(typeof(CommStartModel))]
[JsonSerializable(typeof(ResourceData))]
[JsonSerializable(typeof(WlResourceData))]
[JsonSerializable(typeof(CommStartReponse))]
[JsonSerializable(typeof(CloudApiResponse<CommStartReponse>))]
[JsonSerializable(typeof(CommonQueueInfo))]
[JsonSerializable(typeof(CloudApiResponse<CommonQueueInfo>))]
[JsonSerializable(typeof(DispatchResult))]
[JsonSerializable(typeof(RecordData))]
[JsonSerializable(typeof(CloudApiResponse<RecordData>))]
[JsonSerializable(typeof(RecardQuery))]
[JsonSerializable(typeof(PlayerReponse))]
[JsonSerializable(typeof(CloudGachaItem))]
[JsonSerializable(typeof(List<CloudGachaItem>))]
[JsonSerializable(typeof(CloudGameLoginSession))]
public sealed partial class CloudGameJsonContext : JsonSerializerContext;
