using McKuro.Core.Models.Kuro;
using Microsoft.Extensions.Logging;

namespace McKuro.Core.Services.Kuro;

/// <summary>签到结果摘要。</summary>
public sealed record SignResultSummary(int SuccessCount, int FailedCount, int TotalCount, string Message);

/// <summary>
/// 库街区签到服务:
/// <list type="bullet">
/// <item>游戏签到(鸣潮所有角色,每日一次)</item>
/// <item>库街区每日任务(库洛币签到 + 浏览 3 帖 + 点赞 5 帖 + 分享 1 帖)</item>
/// </list>
/// 参考 Haiyu 的 AutoKuroGameSignService / AutoKuroClientSignService。
/// </summary>
public sealed class KuroSignService
{
    private const int PageSize = 20;
    private const int BrowseTarget = 3;
    private const int LikeTarget = 5;
    private const int MaxAttempts = 3;

    private readonly KuroClient _client;
    private readonly ILogger<KuroSignService> _logger;

    public KuroSignService(KuroClient client, ILogger<KuroSignService>? logger = null)
    {
        _client = client;
        _logger = logger ?? NullLogger<KuroSignService>.Instance;
    }

    /// <summary>对单个账号执行全部游戏签到(鸣潮)。</summary>
    public async Task<SignResultSummary> SignAllGamesAsync(KuroAccount account, CancellationToken ct = default)
    {
        var success = 0;
        var failed = 0;
        var total = 0;

        foreach (var gameId in new[] { (int)KuroGameType.Waves })
        {
            ct.ThrowIfCancellationRequested();
            GamerRoil? roles;
            try
            {
                roles = await _client.GetGamerAsync(account, gameId, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "获取角色列表失败: gameId={GameId}", gameId);
                continue;
            }
            if (roles is not { Code: 200, Data: not null })
            {
                continue;
            }

            foreach (var role in roles.Data)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var sign = await WithRetryAsync(
                        () => _client.SignInAsync(account, role, ct), ct).ConfigureAwait(false);
                    if (sign is null || (sign.Code != 0 && sign.Code != 1511))
                    {
                        failed++;
                    }
                    else
                    {
                        success++;
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception)
                {
                    failed++;
                }
                total++;
            }
        }

        var message = failed == 0
            ? $"签到完成:成功 {success} 个"
            : $"签到完成:成功 {success} 个,失败 {failed} 个";
        return new SignResultSummary(success, failed, total, message);
    }

    /// <summary>对单个账号执行库街区每日任务(签到 + 浏览 + 点赞 + 分享)。</summary>
    public async Task<bool> ExecuteDailyTasksAsync(KuroAccount account, CancellationToken ct = default)
    {
        // 1. 库洛币签到
        var signResult = await WithRetryAsync(
            () => _client.SignInClientAsync(account, ct), ct).ConfigureAwait(false);
        if (signResult is null)
        {
            return false;
        }

        // 2. 随机获取一页帖子,浏览其中 3 篇
        var pageIndex = Random.Shared.Next(1, 11);
        var feedResult = await WithRetryAsync(
            () => _client.FeedHomeListsAsync(account, HomeFeedOption.CreateHomeWaves(pageIndex, PageSize), ct),
            ct).ConfigureAwait(false);
        var posts = feedResult?.Data?.PostList?
            .Where(static p => !string.IsNullOrWhiteSpace(p.PostId))
            .GroupBy(static p => p.PostId!)
            .Select(static g => g.First())
            .ToList();
        if (posts is null || posts.Count < BrowseTarget)
        {
            return false;
        }

        var browsed = 0;
        foreach (var post in posts.Take(BrowseTarget))
        {
            var detail = await WithRetryAsync(
                () => _client.GetFeedPageDetailAsync(account, HomeFeedPostDetailOption.Create(post.PostId!), ct),
                ct).ConfigureAwait(false);
            if (detail is not null)
            {
                browsed++;
            }
        }
        if (browsed < BrowseTarget)
        {
            return false;
        }

        // 3. 点赞 5 篇(优先未点赞的)
        var likePosts = posts
            .OrderBy(static p => p.IsLike == 1)
            .Take(LikeTarget)
            .ToList();
        if (likePosts.Count < LikeTarget)
        {
            return false;
        }

        var liked = 0;
        foreach (var post in likePosts)
        {
            var likeOption = HomeFeedLikeOption.CreateLikeWaves(
                post.PostId!, post.PostType.ToString(), "1", string.Empty, string.Empty, post.UserId ?? "");
            var likeResult = await WithRetryAsync(
                () => _client.PostIdLikeAsync(account, likeOption, ct), ct).ConfigureAwait(false);
            if (likeResult is not null)
            {
                liked++;
            }
        }
        if (liked < LikeTarget)
        {
            return false;
        }

        // 4. 分享 1 篇(使用帖子自身 gameId)
        var sharePost = posts[0];
        var shareResult = await WithRetryAsync(
            () => _client.SharedPostIdAsync(
                account,
                new HomeFeedSharedOption { GameId = sharePost.GameId.ToString(), PostId = sharePost.PostId! },
                ct),
            ct).ConfigureAwait(false);

        return shareResult is not null;
    }

    private async Task<T?> WithRetryAsync<T>(Func<Task<T?>> action, CancellationToken ct)
        where T : class
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var result = await action().ConfigureAwait(false);
                if (result is not null)
                {
                    return result;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "任务请求失败,第 {Attempt}/{Max} 次尝试", attempt, MaxAttempts);
                // 重试
            }
            if (attempt < MaxAttempts)
            {
                await Task.Delay(TimeSpan.FromSeconds(attempt * 2), ct).ConfigureAwait(false);
            }
        }
        return null;
    }
}
