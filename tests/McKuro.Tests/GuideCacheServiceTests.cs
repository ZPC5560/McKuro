using McKuro.Core.Infrastructure;
using McKuro.Core.Models.Guide;
using McKuro.Core.Services.Guide;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace McKuro.Tests;

/// <summary>
/// 攻略缓存服务回归(评审反馈:本次唯一的持久化新逻辑此前零测试)。
/// 覆盖:往返、null 详情不覆盖、账号维度隔离、时间戳分列互不"续命"、负缓存、PruneExcept。
/// </summary>
public class GuideCacheServiceTests : IDisposable
{
    private readonly string _dir;
    private readonly AppDatabase _db;
    private readonly GuideCacheService _svc;

    public GuideCacheServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "McKuro-gcs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _db = new AppDatabase(_dir);
        _svc = new GuideCacheService(_db, NullLogger<GuideCacheService>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (Exception)
        {
            // 忽略清理失败
        }
    }

    private static List<GuideIntroductionItem> MakeList(params long[] ids)
        => ids.Select(id => new GuideIntroductionItem { Id = id, LikeCount = id }).ToList();

    private static GuideIntroductionInfo Detail(long id)
        => new() { Id = id, Grade = $"G{id}" };

    [Fact]
    public void SaveList_And_SaveDetail_RoundTrip()
    {
        _svc.SaveList("acctA", 100, MakeList(7, 8), selectedId: 7);
        _svc.SaveDetail("acctA", 100, Detail(7), selectedId: 7);

        var entry = _svc.TryGet("acctA", 100);
        Assert.NotNull(entry);
        Assert.Equal(2, entry!.List.Count);
        Assert.Equal(7, entry.SelectedId);
        Assert.NotNull(entry.Detail);
        Assert.True(_svc.IsListFresh(entry));
        Assert.True(_svc.IsDetailFresh(entry));
    }

    [Fact]
    public void SaveDetail_Null_Does_Not_Wipe_Existing_Detail()
    {
        // 评审反馈的核心回归:"列表成功/详情失败"不得把上次的详情清成空
        _svc.SaveDetail("acctA", 100, Detail(7), selectedId: 7);
        _svc.SaveDetail("acctA", 100, detail: null, selectedId: 9);

        var entry = _svc.TryGet("acctA", 100);
        Assert.NotNull(entry!.Detail);
        Assert.Equal(7, entry.Detail!.Id);
    }

    [Fact]
    public void Accounts_Are_Isolated()
    {
        // 评审反馈:达成度是 per-account 数据,换攻略账号不得命中上一账号快照
        _svc.SaveDetail("acctA", 100, Detail(7), selectedId: 7);
        Assert.Null(_svc.TryGet("acctB", 100));

        _svc.SaveList("acctB", 100, MakeList(9), selectedId: 9);
        var b = _svc.TryGet("acctB", 100);
        Assert.NotNull(b);
        Assert.Null(b!.Detail); // acctA 的详情不泄漏
        Assert.Single(b.List);
    }

    [Fact]
    public void Timestamps_Are_Independent_Per_Payload()
    {
        // 评审反馈:旧表共用 update_time,SaveRoleInfo 会把超期攻略"续命"为新鲜
        _svc.SaveList("acctA", 100, MakeList(7), selectedId: 7);
        _svc.SaveDetail("acctA", 100, Detail(7), selectedId: 7);

        // 手工把列表时间戳打旧(模拟超期),详情保持新鲜
        using (var cmd = _db.Connection.CreateCommand())
        {
            cmd.CommandText = "UPDATE guide_cache SET list_update_time = '0001-01-01T00:00:00.0000000Z' WHERE account_id='acctA' AND card_role_id=100";
            cmd.ExecuteNonQuery();
        }

        var stale = _svc.TryGet("acctA", 100);
        Assert.False(_svc.IsListFresh(stale));
        Assert.False(_svc.IsListKnownFresh(stale));
        Assert.True(_svc.IsDetailFresh(stale)); // 详情不受列表超期影响

        // role_info 保存不得把超期的列表重新"续命"
        _svc.SaveRoleInfo("acctA", 100, new GuideRoleInfoData());
        var after = _svc.TryGet("acctA", 100);
        Assert.False(_svc.IsListKnownFresh(after));
        Assert.True(_svc.IsDetailFresh(after));
    }

    [Fact]
    public void Empty_List_Is_Cached_As_Fresh_Negative_Result()
    {
        // 评审反馈:无攻略角色要享受负缓存,否则每次进页重发请求
        _svc.SaveList("acctA", 100, [], selectedId: 0);
        var entry = _svc.TryGet("acctA", 100);
        Assert.NotNull(entry);
        Assert.Empty(entry!.List);
        Assert.False(_svc.IsListFresh(entry));    // "有列表数据"仍为 false
        Assert.True(_svc.IsListKnownFresh(entry)); // "已知结果"为 true → 命中负缓存
    }

    [Fact]
    public void PruneExcept_Removes_Other_Accounts_And_Stale_Roles()
    {
        _svc.SaveList("acctA", 100, MakeList(1), selectedId: 1);
        _svc.SaveList("acctA", 200, MakeList(1), selectedId: 1);
        _svc.SaveList("acctB", 100, MakeList(1), selectedId: 1); // 其他账号 → 应清

        _svc.PruneExcept("acctA", keepCardRoleIds: [100]);

        Assert.NotNull(_svc.TryGet("acctA", 100));
        Assert.Null(_svc.TryGet("acctA", 200)); // 本账号孤儿 → 清
        Assert.Null(_svc.TryGet("acctB", 100)); // 其他账号 → 清
    }

    [Fact]
    public void PruneExcept_With_Empty_Keep_Only_Clears_Other_Accounts()
    {
        // 空集合 = "未知当前角色列表":不得误删本账号数据
        _svc.SaveList("acctA", 100, MakeList(1), selectedId: 1);
        _svc.SaveList("acctB", 100, MakeList(1), selectedId: 1);

        _svc.PruneExcept("acctA", keepCardRoleIds: []);

        Assert.NotNull(_svc.TryGet("acctA", 100));
        Assert.Null(_svc.TryGet("acctB", 100));
    }

    [Theory]
    // ParseRecommendedChains:定死支持范围(半角/全角、HTML 标签、去重、越界)
    [InlineData("<p>推荐共鸣链2与共鸣链4</p>", new int[] { 2, 4 })]
    [InlineData("共鸣链 6 提升巨大", new int[] { 6 })]
    [InlineData("共鸣链２、共鸣链５", new int[] { 2, 5 })]
    [InlineData("共鸣链2 共鸣链2", new int[] { 2 })]
    [InlineData("不推荐任何链", new int[] { })]
    [InlineData("C2 或 2链 写法不识别", new int[] { })]
    [InlineData(null, new int[] { })]
    [InlineData("共鸣链7 越界忽略", new int[] { })]
    public void ParseRecommendedChains_Supported_Notation(string? text, int[] expected)
    {
        Assert.Equal(expected, GuideAchievementService.ParseRecommendedChains(text));
    }
}
