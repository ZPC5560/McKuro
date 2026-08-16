using McKuro.Core.Models.Kuro;

namespace McKuro.Core.Services.Kuro;

/// <summary>
/// 库街区 API 客户端聚合接口(便于 VM 解耦,避免直接依赖大型单一类)。
/// <para>由 <see cref="KuroClient"/> 实现;不暴露实现细节。</para>
/// </summary>
public interface IKuroClient
{
    /// <summary>当前出口 IP(风控头)。</summary>
    string Ip { get; }

    /// <summary>启动时探测出口 IP(失败静默)。</summary>
    Task InitAsync(CancellationToken ct = default);

    // ---- 账号与登录 ----
    Task<AccountMine?> GetWavesMineAsync(KuroAccount account, CancellationToken ct = default);
    Task<bool> IsLoginAsync(KuroAccount account, CancellationToken ct = default);
    Task<SMSResultModel?> SendSMSAsync(string mobile, string geeTestData, string deviceId, CancellationToken ct = default);
    Task<AccountModel?> LoginAsync(string mobile, string code, string deviceId, CancellationToken ct = default);

    // ---- 角色与签到 ----
    Task<GamerRoil?> GetGamerAsync(KuroAccount account, int gameId, CancellationToken ct = default);
    Task<SignInResult?> SignInAsync(KuroAccount account, GameRoilDataItem item, CancellationToken ct = default);
    Task<SignInInfo?> GetSignInDataAsync(KuroAccount account, GameRoilDataItem item, CancellationToken ct = default);
    Task<SignRecordInfo?> GetSignRecordAsync(KuroAccount account, GameRoilDataItem item, CancellationToken ct = default);
    Task<KuroClientReturnCode<KuroClientSignInModel>?> SignInClientAsync(KuroAccount account, CancellationToken ct = default);

    // ---- 每日任务 ----
    Task<KuroClientReturnCode<KuroClientHomeFeedModel>?> FeedHomeListsAsync(KuroAccount account, HomeFeedOption option, CancellationToken ct = default);
    Task<KuroClientReturnCode<bool>?> PostIdLikeAsync(KuroAccount account, HomeFeedLikeOption option, CancellationToken ct = default);
    Task<KuroClientReturnCode<bool>?> SharedPostIdAsync(KuroAccount account, HomeFeedSharedOption option, CancellationToken ct = default);
    Task<KuroClientReturnCode<KuroClientPostPageDetail>?> GetFeedPageDetailAsync(KuroAccount account, HomeFeedPostDetailOption option, CancellationToken ct = default);
    Task<KuroClientReturnCode<KuroEncourageProcessModel>?> GetEncourageProcessAsync(KuroAccount account, EncourageProcessOption option, CancellationToken ct = default);
    Task<KuroClientReturnCode<EncourageTotalGoldModel>?> GetEncourageTotalGoldAsync(KuroAccount account, CancellationToken ct = default);

    // ---- 扫码登录 ----
    Task<ScanScreenModel?> PostQrValueAsync(KuroAccount account, string qrText, CancellationToken ct = default);
    Task<QRLoginResult?> QRLoginAsync(KuroAccount account, string qrText, string verifyCode, string id, CancellationToken ct = default);
    Task<SMSModel?> GetQrCodeAsync(KuroAccount account, string qrCode, CancellationToken ct = default);
}
