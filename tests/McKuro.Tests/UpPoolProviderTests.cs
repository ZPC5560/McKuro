using System.Net;
using McKuro.Core.Models.Gacha;
using McKuro.Core.Services.Gacha;
using Microsoft.Extensions.Logging.Abstractions;

namespace McKuro.Tests;

/// <summary>
/// UP/歪 判定回归测试。
/// 根因:RemoteUpPoolProvider 此前用 five_maps(全量五星目录)当"当期 UP"——
/// 导致所有历史限定都被误判为 UP,且若接口尚未把新角色(如当期 UP 穗穗)写进目录
/// 则会误判为歪。修复后改用 pool_list 按生效时间过滤的当期 UP 集合。
/// 使用合成 fixture + 固定 TimeProvider,保证测试长期稳定(不受真实卡池轮换影响)。
/// </summary>
public sealed class UpPoolProviderTests
{
    private sealed class StubHttpMessageHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }
    }

    /// <summary>固定时间 2026-06-15(在合成 fixture 的生效期 2026-01-01~2026-12-31 内)。</summary>
    private sealed class FixedTimeProvider : TimeProvider
    {
        private static readonly DateTimeOffset Now = new(2026, 6, 15, 12, 0, 0, TimeSpan.FromHours(8));
        public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime();
        public override TimeZoneInfo LocalTimeZone =>
            TimeZoneInfo.CreateCustomTimeZone("CST", TimeSpan.FromHours(8), "CST", "CST");
    }

    private static string ReadFixture()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "up_pool_synthetic.json");
        if (!File.Exists(path))
        {
            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
            path = Path.Combine(root, "tests", "McKuro.Tests", "Fixtures", "up_pool_synthetic.json");
        }
        return File.ReadAllText(path);
    }

    private static RemoteUpPoolProvider CreateProvider()
        => new(new HttpClient(new StubHttpMessageHandler(ReadFixture())),
            new FixedTimeProvider(), NullLogger<RemoteUpPoolProvider>.Instance);

    [Fact]
    public async Task CurrentUpSet_Contains_ActiveRoleUps()
    {
        var upIds = await CreateProvider().GetUpIdsAsync();

        Assert.True(upIds.TryGetValue(CardPoolType.RoleActivity, out var roleSet), "RoleActivity 应有 UP 集合");
        Assert.True(roleSet!.Contains(1110), "当期角色活动池 UP 集合应包含穗穗(1110)");
        Assert.True(roleSet.Contains(1210), "当期角色活动池 UP 集合应包含爱弥斯(1210)");
    }

    [Fact]
    public async Task CurrentUpSet_Excludes_Resident_And_Includes_Limited()
    {
        var upIds = await CreateProvider().GetUpIdsAsync();

        Assert.True(upIds.TryGetValue(CardPoolType.RoleActivity, out var roleSet), "RoleActivity 应有 UP 集合");
        // 限定角色(穗穗/爱弥斯/忌炎,pool_type 空)都应算 UP
        Assert.True(roleSet!.Contains(1110), "限定穗穗(1110)应在 UP 集合");
        Assert.True(roleSet.Contains(1210), "限定爱弥斯(1210)应在 UP 集合");
        Assert.True(roleSet.Contains(1404), "限定忌炎(1404)应在 UP 集合");
        // 常驻角色(卡卡罗,pool_type=0)应排除(判歪)
        Assert.False(roleSet.Contains(1301),
            $"常驻卡卡罗(1301)不应在 UP 集合,实际={string.Join(",", roleSet.OrderBy(x => x))}");
    }

    [Fact]
    public async Task CurrentUpSet_Contains_ActiveWeaponUps()
    {
        var upIds = await CreateProvider().GetUpIdsAsync();

        Assert.True(upIds.TryGetValue(CardPoolType.WeaponsActivity, out var weaponSet), "WeaponsActivity 应有 UP 集合");
        Assert.True(weaponSet!.Contains(21050096), "当期武器活动池 UP 集合应包含栖霞饮露(21050096)");
        Assert.True(weaponSet.Contains(21020076), "当期武器活动池 UP 集合应包含永远的启明星(21020076)");
    }

    [Fact]
    public async Task GachaAnalysis_Marks_ActiveUp_As_Up_Not_OffBanner()
    {
        var upIds = await CreateProvider().GetUpIdsAsync();

        // 当期角色活动池抽到穗穗(1110,当期 UP)→ 应为 UP,不是歪
        var record = new GachaRecord
        {
            CardPoolType = "角色活动",
            ResourceId = 1110,
            QualityLevel = 5,
            ResourceType = "角色",
            Name = "穗穗",
            Time = "2026-06-10 12:00:00",
        };
        var result = new GachaAnalysisService().Analyze("player1", [record], upIds);

        var rolePool = result[CardPoolType.RoleActivity];
        Assert.NotNull(rolePool);
        var entry = Assert.Single(rolePool!.FiveStarEntries);
        Assert.False(entry.IsOffBanner, "穗穗(当期 UP)不应被判定为歪(off-banner)");
    }

    [Fact]
    public async Task GachaAnalysis_Marks_OffBanner_As_歪()
    {
        var upIds = await CreateProvider().GetUpIdsAsync();

        // 角色活动池抽到卡卡罗(1301,常驻 pool_type=0,不在 UP 集合)→ 应为歪
        var record = new GachaRecord
        {
            CardPoolType = "角色活动",
            ResourceId = 1301,
            QualityLevel = 5,
            ResourceType = "角色",
            Name = "卡卡罗",
            Time = "2026-06-10 12:00:00",
        };
        var result = new GachaAnalysisService().Analyze("player1", [record], upIds);

        var rolePool = result[CardPoolType.RoleActivity];
        Assert.NotNull(rolePool);
        var entry = Assert.Single(rolePool!.FiveStarEntries);
        Assert.True(entry.IsOffBanner, "卡卡罗(常驻)应被判定为歪(off-banner)");
    }

    [Fact]
    public async Task ResidentPools_HaveNoUpIds()
    {
        var upIds = await CreateProvider().GetUpIdsAsync();

        // 常驻池无 UP 概念:不提供 UP 集合,五星不做歪/UP 判定(修复:武器常驻被误标"歪")
        Assert.False(upIds.ContainsKey(CardPoolType.RoleResident), "角色常驻不应提供 UP 集合");
        Assert.False(upIds.ContainsKey(CardPoolType.WeaponsResident), "武器常驻不应提供 UP 集合");
    }

    [Fact]
    public async Task Fallback_Does_Not_Treat_Resident_Roles_As_Up()
    {
        // 走到兜底分支的前提:主循环一个 UP 都没收集到。这份数据里 five_maps 的条目
        // 全部是 pool_type=0(常驻),所以主路径把它们都排除了 → roleIds 为空 → 进兜底。
        // 兜底拿的是「全量目录」,若不剔除常驻,卡卡罗(1301)就会被当成 UP,
        // 于是常驻角色永远不会被判"歪" —— 恰好把最该统计的那一半洗成 UP。
        const string body = """
            {"code":0,"data":{"five_group_config":{"five_maps":[
                {"name":"卡卡罗","item_id":1301,"weapon_id":21010015,"pool_type":0},
                {"name":"安可","item_id":1203,"weapon_id":21010012,"pool_type":0}]}}}
            """;
        var provider = new RemoteUpPoolProvider(
            new HttpClient(new StubHttpMessageHandler(body)),
            new FixedTimeProvider(), NullLogger<RemoteUpPoolProvider>.Instance);

        var upIds = await provider.GetUpIdsAsync();

        Assert.True(upIds.TryGetValue(CardPoolType.RoleActivity, out var roleSet), "RoleActivity 应有 UP 集合");
        Assert.False(roleSet!.Contains(1301), "兜底模式不应把常驻五星角色(卡卡罗 1301)当 UP");
        Assert.False(roleSet.Contains(1203), "兜底模式不应把常驻五星角色(安可 1203)当 UP");
    }

    [Fact]
    public async Task Fallback_Excludes_All_Resident_Five_Star_Roles()
    {
        // 五个常驻五星角色全在 five_maps 且都标了 pool_type=0 → 兜底后应一个都不剩
        const string body = """
            {"code":0,"data":{"five_group_config":{"five_maps":[
                {"name":"卡卡罗","item_id":1301,"pool_type":0},{"name":"安可","item_id":1203,"pool_type":0},
                {"name":"维里奈","item_id":1405,"pool_type":0},{"name":"凌阳","item_id":1104,"pool_type":0},
                {"name":"鉴心","item_id":1503,"pool_type":0}]}}}
            """;
        var provider = new RemoteUpPoolProvider(
            new HttpClient(new StubHttpMessageHandler(body)),
            new FixedTimeProvider(), NullLogger<RemoteUpPoolProvider>.Instance);

        var upIds = await provider.GetUpIdsAsync();

        Assert.True(upIds.TryGetValue(CardPoolType.RoleActivity, out var roleSet));
        Assert.Empty(roleSet!);
    }

    [Fact]
    public async Task Limited_Roles_Still_Enter_Up_Set_Through_Main_Path()
    {
        // 对照组:限定角色(pool_type 缺省/非 0)照常进 UP 集合,剔除常驻不影响这条主路径
        const string body = """
            {"code":0,"data":{"five_group_config":{"five_maps":[
                {"name":"今汐","item_id":1304},{"name":"卡卡罗","item_id":1301,"pool_type":0}]}}}
            """;
        var provider = new RemoteUpPoolProvider(
            new HttpClient(new StubHttpMessageHandler(body)),
            new FixedTimeProvider(), NullLogger<RemoteUpPoolProvider>.Instance);

        var upIds = await provider.GetUpIdsAsync();

        Assert.True(upIds.TryGetValue(CardPoolType.RoleActivity, out var roleSet));
        Assert.Contains(1304, roleSet!);
        Assert.DoesNotContain(1301, roleSet!);
    }
}
