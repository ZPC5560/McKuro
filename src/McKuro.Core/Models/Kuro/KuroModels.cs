using System.Text.Json.Serialization;

namespace McKuro.Core.Models.Kuro;

/// <summary>库洛游戏类型。</summary>
public enum KuroGameType
{
    Punish = 2,
    Waves = 3,
}

/// <summary>库街区账号(登录态)。</summary>
public sealed class KuroAccount
{
    /// <summary>库街区用户 ID。</summary>
    public string UserId { get; set; } = "";

    /// <summary>访问 Token。</summary>
    public string Token { get; set; } = "";

    /// <summary>设备 ID。</summary>
    public string DeviceId { get; set; } = "";

    /// <summary>绑定手机号(可选)。</summary>
    public string Mobile { get; set; } = "";

    /// <summary>昵称(可选)。</summary>
    public string Nickname { get; set; } = "";
}

/// <summary>库街区 API 统一响应。</summary>
public sealed class KuroClientReturnCode<T>
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("data")]
    public T? Data { get; set; }

    [JsonPropertyName("msg")]
    public string? Msg { get; set; }

    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("traceId")]
    public string? TraceId { get; set; }
}

/// <summary>游戏角色列表响应。</summary>
public sealed class GamerRoil
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("msg")]
    public string? Msg { get; set; }

    [JsonPropertyName("data")]
    public List<GameRoilDataItem>? Data { get; set; }

    [JsonPropertyName("success")]
    public bool Success { get; set; }
}

/// <summary>游戏角色条目。</summary>
public sealed class GameRoilDataItem
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("userId")]
    public string? UserId { get; set; }

    [JsonPropertyName("gameId")]
    public int GameId { get; set; }

    [JsonPropertyName("serverId")]
    public string? ServerId { get; set; }

    [JsonPropertyName("serverName")]
    public string? ServerName { get; set; }

    [JsonPropertyName("roleId")]
    public string? RoleId { get; set; }

    [JsonPropertyName("roleName")]
    public string? RoleName { get; set; }

    [JsonPropertyName("isDefault")]
    public bool IsDefault { get; set; }

    [JsonPropertyName("gameHeadUrl")]
    public string? GameHeadUrl { get; set; }

    [JsonPropertyName("gameLevel")]
    public string? GameLevel { get; set; }

    [JsonPropertyName("roleScore")]
    public string? RoleScore { get; set; }

    [JsonPropertyName("roleNum")]
    public int RoleNum { get; set; }

    [JsonPropertyName("headPhotoUrl")]
    public string? HeadPhotoUrl { get; set; }
}

/// <summary>游戏签到结果(encourage/signIn/v2)。</summary>
public sealed class SignInResult
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("msg")]
    public string? Msg { get; set; }

    [JsonPropertyName("success")]
    public bool Success { get; set; }
}

/// <summary>签到状态查询(encourage/signIn/initSignInV2,判断当日是否已签)。</summary>
public sealed class SignInInfo
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("data")]
    public SignInData? Data { get; set; }

    [JsonPropertyName("msg")]
    public string? Msg { get; set; }
}

public sealed class SignInData
{
    /// <summary>今日是否已签到。</summary>
    [JsonPropertyName("isSigIn")]
    public bool IsSigIn { get; set; }

    /// <summary>累计签到天数。</summary>
    [JsonPropertyName("sigInNum")]
    public int SigInNum { get; set; }

    /// <summary>签到奖励配置(用于展示)。</summary>
    [JsonPropertyName("signInGoodsConfigs")]
    public List<SignInGoodsItem>? SignInGoodsConfigs { get; set; }
}

public sealed class SignInGoodsItem
{
    [JsonPropertyName("goodsId")]
    public int GoodsId { get; set; }

    [JsonPropertyName("goodsName")]
    public string? GoodsName { get; set; }

    [JsonPropertyName("goodsNum")]
    public int GoodsNum { get; set; }

    [JsonPropertyName("goodsUrl")]
    public string? GoodsUrl { get; set; }

    /// <summary>第几天(序号,1 起)。</summary>
    [JsonPropertyName("serialNum")]
    public int SerialNum { get; set; }

    /// <summary>是否已领取。</summary>
    [JsonPropertyName("isGain")]
    public bool IsGain { get; set; }

    /// <summary>本地计算:序号是否已签到(索引 &lt; 累计天数)。</summary>
    [JsonIgnore]
    public bool IsSigned { get; set; }
}

/// <summary>签到历史明细(encourage/signIn/queryRecordV2,对齐 WutheringWavesTool SignRecord)。</summary>
public sealed class SignRecordInfo
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("data")]
    public List<SignRecordItem>? Data { get; set; }

    [JsonPropertyName("msg")]
    public string? Msg { get; set; }
}

public sealed class SignRecordItem
{
    [JsonPropertyName("orderCode")] public string? OrderCode { get; set; }
    [JsonPropertyName("goodsName")] public string? GoodsName { get; set; }
    [JsonPropertyName("goodsNum")] public int GoodsNum { get; set; }
    [JsonPropertyName("goodsUrl")] public string? GoodsUrl { get; set; }
    [JsonPropertyName("sigInDate")] public string? SigInDate { get; set; }
    [JsonPropertyName("type")] public int Type { get; set; }
    [JsonPropertyName("roleId")] public string? RoleId { get; set; }
    [JsonPropertyName("userId")] public string? UserId { get; set; }
}

/// <summary>库街区每日签到(库洛币)结果模型。</summary>
public sealed class KuroClientSignInModel
{
    [JsonPropertyName("continueDays")]
    public int ContinueDays { get; set; }

    [JsonPropertyName("gainVoList")]
    public List<KuroClientSignInItem>? GainVoList { get; set; }

    [JsonPropertyName("geeTest")]
    public bool GeeTest { get; set; }

    [JsonPropertyName("totalSignInDay")]
    public int TotalSignInDay { get; set; }
}

public sealed class KuroClientSignInItem
{
    [JsonPropertyName("gainTyp")]
    public int GainTyp { get; set; }

    [JsonPropertyName("gainValue")]
    public int GainValue { get; set; }
}

/// <summary>库街区首页帖子流响应。</summary>
public sealed class KuroClientHomeFeedModel
{
    [JsonPropertyName("group")]
    public string? Group { get; set; }

    [JsonPropertyName("hasNext")]
    public int HasNext { get; set; }

    [JsonPropertyName("postList")]
    public List<KuroClientPost>? PostList { get; set; }

    [JsonPropertyName("recommendId")]
    public string? RecommendId { get; set; }

    [JsonPropertyName("requestId")]
    public string? RequestId { get; set; }

    [JsonPropertyName("styleType")]
    public int StyleType { get; set; }
}

/// <summary>帖子条目。</summary>
public sealed class KuroClientPost
{
    [JsonPropertyName("browseCount")]
    public string? BrowseCount { get; set; }

    [JsonPropertyName("commentCount")]
    public int CommentCount { get; set; }

    [JsonPropertyName("coverImages")]
    public List<KuroClientCoverImage>? CoverImages { get; set; }

    [JsonPropertyName("createTimestamp")]
    public string? CreateTimestamp { get; set; }

    [JsonPropertyName("gameForumId")]
    public int GameForumId { get; set; }

    [JsonPropertyName("gameId")]
    public int GameId { get; set; }

    [JsonPropertyName("gameName")]
    public string? GameName { get; set; }

    [JsonPropertyName("imgContent")]
    public List<KuroClientImgContent>? ImgContent { get; set; }

    [JsonPropertyName("imgCount")]
    public int ImgCount { get; set; }

    [JsonPropertyName("ipRegion")]
    public string? IpRegion { get; set; }

    [JsonPropertyName("isFollow")]
    public int IsFollow { get; set; }

    [JsonPropertyName("isLike")]
    public int IsLike { get; set; }

    [JsonPropertyName("isLock")]
    public int IsLock { get; set; }

    [JsonPropertyName("isPublisher")]
    public int IsPublisher { get; set; }

    [JsonPropertyName("likeCount")]
    public int LikeCount { get; set; }

    [JsonPropertyName("postContent")]
    public string? PostContent { get; set; }

    [JsonPropertyName("postId")]
    public string? PostId { get; set; }

    [JsonPropertyName("postTitle")]
    public string? PostTitle { get; set; }

    [JsonPropertyName("postType")]
    public int PostType { get; set; }

    [JsonPropertyName("showTime")]
    public string? ShowTime { get; set; }

    [JsonPropertyName("topicList")]
    public List<KuroClientTopic>? TopicList { get; set; }

    [JsonPropertyName("userHeadUrl")]
    public string? UserHeadUrl { get; set; }

    [JsonPropertyName("userId")]
    public string? UserId { get; set; }

    [JsonPropertyName("userLevel")]
    public int UserLevel { get; set; }

    [JsonPropertyName("userName")]
    public string? UserName { get; set; }

    [JsonPropertyName("identifyClassify")]
    public int? IdentifyClassify { get; set; }

    [JsonPropertyName("identifyNames")]
    public string? IdentifyNames { get; set; }

    [JsonPropertyName("newIdentifyNames")]
    public List<string>? NewIdentifyNames { get; set; }

    [JsonPropertyName("videoId")]
    public string? VideoId { get; set; }
}

public sealed class KuroClientCoverImage
{
    [JsonPropertyName("imgHeight")]
    public int ImgHeight { get; set; }

    [JsonPropertyName("imgWidth")]
    public int ImgWidth { get; set; }

    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("sourceUrl")]
    public string? SourceUrl { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }
}

public sealed class KuroClientImgContent
{
    [JsonPropertyName("imgHeight")]
    public int ImgHeight { get; set; }

    [JsonPropertyName("imgWidth")]
    public int ImgWidth { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }
}

public sealed class KuroClientTopic
{
    [JsonPropertyName("postId")]
    public string? PostId { get; set; }

    [JsonPropertyName("topicId")]
    public int TopicId { get; set; }

    [JsonPropertyName("topicName")]
    public string? TopicName { get; set; }
}

/// <summary>帖子详情响应(每日任务浏览用,仅需顶层结构)。</summary>
public sealed class KuroClientPostPageDetail
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("msg")]
    public string? Msg { get; set; }

    [JsonPropertyName("success")]
    public bool Success { get; set; }
}

/// <summary>库街区每日任务进度。</summary>
public sealed class KuroEncourageProcessModel
{
    [JsonPropertyName("currentDailyGold")]
    public int CurrentDailyGold { get; set; }

    [JsonPropertyName("growTask")]
    public List<KuroEncourageTask>? GrowTask { get; set; }

    [JsonPropertyName("dailyTask")]
    public List<KuroEncourageTask>? DailyTask { get; set; }

    [JsonPropertyName("maxDailyGold")]
    public int MaxDailyGold { get; set; }
}

public sealed class KuroEncourageTask
{
    [JsonPropertyName("completeTimes")]
    public int CompleteTimes { get; set; }

    [JsonPropertyName("gainGold")]
    public int GainGold { get; set; }

    [JsonPropertyName("needActionTimes")]
    public int NeedActionTimes { get; set; }

    [JsonPropertyName("process")]
    public double Process { get; set; }

    [JsonPropertyName("remark")]
    public string? Remark { get; set; }

    [JsonPropertyName("skipType")]
    public int SkipType { get; set; }

    [JsonPropertyName("times")]
    public int Times { get; set; }
}

public sealed class EncourageTotalGoldModel
{
    [JsonPropertyName("goldNum")]
    public double GoldNum { get; set; }
}

/// <summary>首页帖子流请求选项。</summary>
public class HomeFeedOption
{
    public string ForumId { get; set; } = "";
    public string GameId { get; set; } = "";
    public string PageIndex { get; set; } = "1";
    public string PageSize { get; set; } = "20";
    public string SearchType { get; set; } = "3";
    public string TimeType { get; set; } = "0";
    public string TopicId { get; set; } = "0";

    public virtual Dictionary<string, string> ConvertParam() => new()
    {
        { "forumId", ForumId },
        { "gameId", GameId },
        { "pageIndex", PageIndex },
        { "pageSize", PageSize },
        { "searchType", SearchType },
        { "timeType", TimeType },
        { "TopicId", TopicId },
    };

    public static HomeFeedOption CreateHomeWaves(int pageIndex, int pageSize) => new()
    {
        ForumId = "9",
        GameId = "3",
        PageIndex = pageIndex.ToString(),
        PageSize = pageSize.ToString(),
        SearchType = "3",
        TimeType = "0",
        TopicId = "0",
    };

    public static HomeFeedOption CreateHomePunish(int pageIndex, int pageSize) => new()
    {
        ForumId = "2",
        GameId = "2",
        PageIndex = pageIndex.ToString(),
        PageSize = pageSize.ToString(),
        SearchType = "3",
        TimeType = "0",
        TopicId = "0",
    };
}

/// <summary>帖子点赞选项。</summary>
public sealed class HomeFeedLikeOption : HomeFeedOption
{
    public string PostId { get; set; } = "";
    public string PostType { get; set; } = "";
    /// <summary>1 点赞,2 取消点赞。</summary>
    public string OperateType { get; set; } = "1";
    public string LikeType { get; set; } = "1";
    public string PostCommentId { get; set; } = "";
    public string PostCommentReplyId { get; set; } = "";
    public string ToUserId { get; set; } = "";

    public override Dictionary<string, string> ConvertParam() => new()
    {
        { "forumId", ForumId },
        { "gameId", GameId },
        { "likeType", LikeType },
        { "postId", PostId },
        { "postType", PostType },
        { "operateType", OperateType },
        { "postCommentId", PostCommentId },
        { "postCommentReplyId", PostCommentReplyId },
        { "toUserId", ToUserId },
    };

    public static HomeFeedLikeOption CreateLikeWaves(
        string postId, string postType, string operateType,
        string postCommentId, string postCommentReplyId, string toUserId) => new()
    {
        ForumId = "9",
        GameId = "3",
        PostId = postId,
        LikeType = "1",
        PostType = postType,
        OperateType = operateType,
        PostCommentId = postCommentId,
        PostCommentReplyId = postCommentReplyId,
        ToUserId = toUserId,
    };
}

/// <summary>帖子详情请求选项。</summary>
public sealed class HomeFeedPostDetailOption
{
    public string IsOnlyPublisher { get; set; } = "0";
    public string PostId { get; set; } = "";
    public string ShowOrderType { get; set; } = "2";

    public Dictionary<string, string> ConvertParam() => new()
    {
        { "isOnlyPublisher", IsOnlyPublisher },
        { "postId", PostId },
        { "ShowOrderType", ShowOrderType },
    };

    public static HomeFeedPostDetailOption Create(string postId) => new()
    {
        IsOnlyPublisher = "0",
        PostId = postId,
        ShowOrderType = "2",
    };
}

/// <summary>帖子分享选项。</summary>
public sealed class HomeFeedSharedOption
{
    public string GameId { get; set; } = "";
    public string PostId { get; set; } = "";

    public Dictionary<string, string> ConvertParam() => new()
    {
        { "gameId", GameId },
        { "postId", PostId },
    };

    public static HomeFeedSharedOption CreateWaves(string postId) => new() { GameId = "3", PostId = postId };
    public static HomeFeedSharedOption CreatePunish(string postId) => new() { GameId = "2", PostId = postId };
}

/// <summary>每日任务进度请求选项。</summary>
public sealed class EncourageProcessOption
{
    public string GameId { get; set; } = "3";
    public string PageIndex { get; set; } = "1";
    public string PageSize { get; set; } = "20";

    public Dictionary<string, string> ConvertParam() => new()
    {
        { "gameId", GameId },
        { "pageIndex", PageIndex },
        { "pageSize", PageSize },
    };

    public static EncourageProcessOption CreateWaves() => new() { GameId = "3" };
    public static EncourageProcessOption CreatePunish() => new() { GameId = "2" };
}

/// <summary>手机号验证码登录返回。</summary>
public sealed class AccountModel
{
    [JsonPropertyName("code")]
    public long Code { get; set; }

    [JsonPropertyName("data")]
    public AccountData? Data { get; set; }

    [JsonPropertyName("msg")]
    public string? Msg { get; set; }

    [JsonPropertyName("success")]
    public bool Success { get; set; }
}

public sealed class AccountData
{
    [JsonPropertyName("userName")]
    public string? UserName { get; set; }

    [JsonPropertyName("userId")]
    public string? UserId { get; set; }

    [JsonPropertyName("headUrl")]
    public string? HeadUrl { get; set; }

    [JsonPropertyName("token")]
    public string? Token { get; set; }
}

/// <summary>发送短信验证码返回。</summary>
public sealed class SMSResultModel
{
    [JsonPropertyName("code")]
    public long Code { get; set; }

    [JsonPropertyName("data")]
    public SMSResultData? Data { get; set; }

    [JsonPropertyName("msg")]
    public string? Msg { get; set; }

    [JsonPropertyName("success")]
    public bool Success { get; set; }
}

public sealed class SMSResultData
{
    [JsonPropertyName("geeTest")]
    public bool GeeTest { get; set; }
}

/// <summary>扫码登录:查询角色信息。</summary>
public sealed class ScanScreenModel
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("data")]
    public List<QrRoleItem>? Data { get; set; }

    [JsonPropertyName("msg")]
    public string? Msg { get; set; }

    [JsonPropertyName("success")]
    public bool Success { get; set; }
}

/// <summary>扫码登录角色条目。</summary>
public sealed class QrRoleItem
{
    [JsonPropertyName("gameId")]
    public int GameId { get; set; }

    [JsonPropertyName("gameName")]
    public string? GameName { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("isDefault")]
    public bool IsDefault { get; set; }

    [JsonPropertyName("mobile")]
    public string? Mobile { get; set; }

    [JsonPropertyName("roleId")]
    public string? RoleId { get; set; }

    [JsonPropertyName("roleName")]
    public string? RoleName { get; set; }

    [JsonPropertyName("serverId")]
    public string? ServerId { get; set; }

    [JsonPropertyName("serverName")]
    public string? ServerName { get; set; }

    [JsonPropertyName("support")]
    public bool Support { get; set; }
}

/// <summary>扫码登录确认结果。</summary>
public sealed class QRLoginResult
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("data")]
    public bool Data { get; set; }

    [JsonPropertyName("msg")]
    public string? Msg { get; set; }

    [JsonPropertyName("success")]
    public bool Success { get; set; }
}

/// <summary>发送扫码短信验证码返回。</summary>
public sealed class SMSModel
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("data")]
    public SMSData? Data { get; set; }

    [JsonPropertyName("msg")]
    public string? Msg { get; set; }

    [JsonPropertyName("success")]
    public bool Success { get; set; }
}

public sealed class SMSData
{
    [JsonPropertyName("geeTest")]
    public bool GeeTest { get; set; }
}

/// <summary>我的主页信息(登录校验用)。</summary>
public sealed class AccountMine
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("msg")]
    public string? Msg { get; set; }

    [JsonPropertyName("success")]
    public bool Success { get; set; }
}

[JsonSerializable(typeof(GamerRoil))]
[JsonSerializable(typeof(GameRoilDataItem))]
[JsonSerializable(typeof(List<GameRoilDataItem>))]
[JsonSerializable(typeof(SignInResult))]
[JsonSerializable(typeof(KuroClientSignInModel))]
[JsonSerializable(typeof(KuroClientSignInItem))]
[JsonSerializable(typeof(KuroClientReturnCode<KuroClientSignInModel>))]
[JsonSerializable(typeof(KuroClientReturnCode<KuroClientHomeFeedModel>))]
[JsonSerializable(typeof(KuroClientReturnCode<KuroClientPostPageDetail>))]
[JsonSerializable(typeof(KuroClientReturnCode<KuroEncourageProcessModel>))]
[JsonSerializable(typeof(KuroClientReturnCode<EncourageTotalGoldModel>))]
[JsonSerializable(typeof(KuroClientReturnCode<bool>))]
[JsonSerializable(typeof(KuroClientHomeFeedModel))]
[JsonSerializable(typeof(KuroClientPost))]
[JsonSerializable(typeof(List<KuroClientPost>))]
[JsonSerializable(typeof(KuroClientPostPageDetail))]
[JsonSerializable(typeof(KuroEncourageProcessModel))]
[JsonSerializable(typeof(EncourageTotalGoldModel))]
[JsonSerializable(typeof(AccountModel))]
[JsonSerializable(typeof(AccountData))]
[JsonSerializable(typeof(SMSResultModel))]
[JsonSerializable(typeof(SMSResultData))]
[JsonSerializable(typeof(ScanScreenModel))]
[JsonSerializable(typeof(QrRoleItem))]
[JsonSerializable(typeof(List<QrRoleItem>))]
[JsonSerializable(typeof(QRLoginResult))]
[JsonSerializable(typeof(SMSModel))]
[JsonSerializable(typeof(SMSData))]
[JsonSerializable(typeof(AccountMine))]
[JsonSerializable(typeof(SignInInfo))]
[JsonSerializable(typeof(SignInData))]
[JsonSerializable(typeof(SignInGoodsItem))]
[JsonSerializable(typeof(List<SignInGoodsItem>))]
[JsonSerializable(typeof(SignRecordInfo))]
[JsonSerializable(typeof(SignRecordItem))]
[JsonSerializable(typeof(List<SignRecordItem>))]
public sealed partial class KuroJsonContext : JsonSerializerContext;
