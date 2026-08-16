using System.Text.Json;
using McKuro.Core.Models.Kuro;

namespace McKuro.Core.Services.Kuro;

// KuroClient 的游戏角色 / 签到 / 库街区每日任务部分(partial)。

public sealed partial class KuroClient
{
    /// <summary>获取账号在某游戏下的角色列表。</summary>
    public async Task<GamerRoil?> GetGamerAsync(KuroAccount account, int gameId, CancellationToken ct = default)
    {
        using var request = BuildPost(
            BaseUrl + "/gamer/role/list",
            GetDeviceHeader(account),
            new Dictionary<string, string> { { "gameId", gameId.ToString() } });
        using var response = await _inner.SendAsync(request, ct).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return JsonSerializer.Deserialize(json, KuroJsonContext.Default.GamerRoil);
    }

    /// <summary>游戏签到(encourage/signIn/v2)。code 0/1511 表示成功(已签到/今日已签)。</summary>
    public async Task<SignInResult?> SignInAsync(KuroAccount account, GameRoilDataItem item, CancellationToken ct = default)
    {
        using var request = BuildPost(
            BaseUrl + "/encourage/signIn/v2",
            GetDeviceHeader(account),
            new Dictionary<string, string>
            {
                { "gameId", item.GameId.ToString() },
                { "serverId", item.ServerId ?? "" },
                { "roleId", item.RoleId ?? "" },
                { "userId", item.UserId ?? "" },
                { "reqMonth", DateTime.Now.Month.ToString("D2") },
            });
        using var response = await _inner.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return JsonSerializer.Deserialize(json, KuroJsonContext.Default.SignInResult);
    }

    /// <summary>查询某角色当日签到状态(encourage/signIn/initSignInV2,data.isSigIn 为是否已签)。</summary>
    public async Task<SignInInfo?> GetSignInDataAsync(KuroAccount account, GameRoilDataItem item, CancellationToken ct = default)
    {
        using var request = BuildPost(
            BaseUrl + "/encourage/signIn/initSignInV2",
            GetDeviceHeader(account),
            new Dictionary<string, string>
            {
                { "gameId", item.GameId.ToString() },
                { "serverId", item.ServerId ?? "" },
                { "roleId", item.RoleId ?? "" },
                { "userId", item.UserId ?? "" },
            });
        using var response = await _inner.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return JsonSerializer.Deserialize(json, KuroJsonContext.Default.SignInInfo);
    }

    /// <summary>查询签到历史明细(encourage/signIn/queryRecordV2,对齐 WutheringWavesTool GetSignRecordAsync)。</summary>
    public async Task<SignRecordInfo?> GetSignRecordAsync(KuroAccount account, GameRoilDataItem item, CancellationToken ct = default)
    {
        using var request = BuildPost(
            BaseUrl + "/encourage/signIn/queryRecordV2",
            GetDeviceHeader(account),
            new Dictionary<string, string>
            {
                { "gameId", item.GameId.ToString() },
                { "serverId", item.ServerId ?? "" },
                { "roleId", item.RoleId ?? "" },
                { "userId", item.UserId ?? "" },
                { "reqMonth", DateTime.Now.Month.ToString("D2") },
            });
        using var response = await _inner.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return JsonSerializer.Deserialize(json, KuroJsonContext.Default.SignRecordInfo);
    }

    /// <summary>库街区库洛币每日签到(user/signIn)。code 200 或 1511 为正常。</summary>
    public Task<KuroClientReturnCode<KuroClientSignInModel>?> SignInClientAsync(KuroAccount account, CancellationToken ct = default)
        => _kuro.SendTaskRequestAsync(
            account,
            BaseUrl + "/user/signIn",
            new Dictionary<string, string> { { "gameId", "2" }, { "geeTestData", "" } },
            KuroJsonContext.Default.KuroClientReturnCodeKuroClientSignInModel,
            ct);

    /// <summary>首页帖子流。</summary>
    public Task<KuroClientReturnCode<KuroClientHomeFeedModel>?> FeedHomeListsAsync(
        KuroAccount account, HomeFeedOption option, CancellationToken ct = default)
        => _kuro.SendTaskRequestAsync(
            account,
            BaseUrl + "/forum/list",
            option.ConvertParam(),
            KuroJsonContext.Default.KuroClientReturnCodeKuroClientHomeFeedModel,
            ct);

    /// <summary>点赞帖子。</summary>
    public Task<KuroClientReturnCode<bool>?> PostIdLikeAsync(
        KuroAccount account, HomeFeedLikeOption option, CancellationToken ct = default)
        => _kuro.SendTaskRequestAsync(
            account,
            BaseUrl + "/forum/like",
            option.ConvertParam(),
            KuroJsonContext.Default.KuroClientReturnCodeBoolean,
            ct);

    /// <summary>分享帖子(每日任务)。</summary>
    public Task<KuroClientReturnCode<bool>?> SharedPostIdAsync(
        KuroAccount account, HomeFeedSharedOption option, CancellationToken ct = default)
        => _kuro.SendTaskRequestAsync(
            account,
            BaseUrl + "/encourage/level/shareTask",
            option.ConvertParam(),
            KuroJsonContext.Default.KuroClientReturnCodeBoolean,
            ct);

    /// <summary>帖子详情(浏览任务)。</summary>
    public Task<KuroClientReturnCode<KuroClientPostPageDetail>?> GetFeedPageDetailAsync(
        KuroAccount account, HomeFeedPostDetailOption option, CancellationToken ct = default)
        => _kuro.SendTaskRequestAsync(
            account,
            BaseUrl + "/forum/getPostDetail",
            option.ConvertParam(),
            KuroJsonContext.Default.KuroClientReturnCodeKuroClientPostPageDetail,
            ct);

    /// <summary>每日任务进度。</summary>
    public Task<KuroClientReturnCode<KuroEncourageProcessModel>?> GetEncourageProcessAsync(
        KuroAccount account, EncourageProcessOption option, CancellationToken ct = default)
        => _kuro.SendTaskRequestAsync(
            account,
            BaseUrl + "/encourage/level/getTaskProcess",
            option.ConvertParam(),
            KuroJsonContext.Default.KuroClientReturnCodeKuroEncourageProcessModel,
            ct);

    /// <summary>库洛币总额。</summary>
    public Task<KuroClientReturnCode<EncourageTotalGoldModel>?> GetEncourageTotalGoldAsync(
        KuroAccount account, CancellationToken ct = default)
        => _kuro.SendTaskRequestAsync(
            account,
            BaseUrl + "/encourage/gold/getTotalGold",
            [],
            KuroJsonContext.Default.KuroClientReturnCodeEncourageTotalGoldModel,
            ct);
}
