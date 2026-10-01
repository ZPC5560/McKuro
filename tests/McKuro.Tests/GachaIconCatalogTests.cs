using System.Net;
using System.Text;
using McKuro.Core.Models.Gacha;
using McKuro.Core.Services.Gacha;
using Microsoft.Extensions.Logging.Abstractions;

namespace McKuro.Tests;

/// <summary>
/// 图鉴图标目录动态拉取回归测试。
/// <para>
/// 背景:<see cref="IconCatalog"/> 原本是手写 ResourceId 字典,漏收新角色(心 1311)时
/// GetRoleIconUrl 返回空串 → 抽卡分析页静默留白。修复后由本服务在运行时拉取
/// avatar.json/weapon.json 注入目录,限定与常驻新增都自动覆盖。
/// </para>
/// <para>
/// 注意:静态字典已补入 1311 等条目作离线兜底,因此<b>断言一律使用"静态字典里不存在"的 ID</b>
/// (1399/2399 等合成 ID),否则测试会在静态兜底上假通过,无法证明远程目录真的生效。
/// </para>
/// 使用合成 fixture + 固定 TimeProvider,不依赖真实网络与真实图鉴内容。
/// </summary>
/// <remarks>
/// 加入 <see cref="IconCatalogCollection"/>:本测试会替换 IconCatalog 的进程级远程目录静态状态,
/// 必须与断言静态兜底行为的 LauncherInfoTests 串行,否则并行执行时互相干扰。
/// </remarks>
[Collection(IconCatalogCollection.Name)]
public sealed class GachaIconCatalogTests : IDisposable
{
    /// <summary>1399 = 合成"新角色"(静态字典必无),1301 = 常驻卡卡罗(静态字典已有)。</summary>
    private const string RoleBody = """
        [{"id":1311,"name":"心","icon":"T_IconRoleHead256_75_UI","star":5},
         {"id":1399,"name":"新限定角色","icon":"T_IconRoleHead256_77_UI","star":5},
         {"id":1301,"name":"卡卡罗","icon":"T_IconRoleHead256_18_UI","star":5}]
        """;

    /// <summary>2399 = 合成"新武器"(静态字典必无)。</summary>
    private const string WeaponBody = """
        [{"id":21050116,"name":"玉阙玄华","icon":"T_IconWeapon21050116_UI","star":5},
         {"id":2399,"name":"新常驻武器","icon":"T_IconWeapon2399_UI","star":5}]
        """;

    private const string NewRoleUrl = "https://mc.appfeng.com/ui/avatar/T_IconRoleHead256_77_UI.png";
    private const string NewWeaponUrl = "https://mc.appfeng.com/ui/weapon/T_IconWeapon2399_UI.png";

    /// <summary>忌炎(1404)在静态字典中且不在本 fixture 的远程目录里,用于验证兜底回落。</summary>
    private const string StaticOnlyRoleUrl = "https://mc.appfeng.com/ui/avatar/T_IconRoleHead256_11_UI.png";

    private readonly string _dir;
    private readonly List<string> _requestedUrls = [];

    public GachaIconCatalogTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "McKuro-icons-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        GachaIconCatalogService.ResetForTesting();
    }

    public void Dispose()
    {
        GachaIconCatalogService.ResetForTesting();
        try { Directory.Delete(_dir, recursive: true); } catch { /* ignore */ }
    }

    /// <summary>按 URL 返回不同 body 的桩;未登记 URL 返回 500(用于模拟单边失败)。</summary>
    private sealed class StubHandler(Func<string, string?> bodyFor, List<string> log) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            log.Add(url);
            var body = bodyFor(url);
            if (body is null)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static bool IsRoleUrl(string url) => url.Contains("avatar.json", StringComparison.Ordinal);
    private static bool IsWeaponUrl(string url) => url.Contains("weapon.json", StringComparison.Ordinal);

    private GachaIconCatalogService CreateService(Func<string, string?> bodyFor, TimeProvider? time = null)
        => new(new HttpClient(new StubHandler(bodyFor, _requestedUrls)),
            _dir,
            time,
            NullLogger<GachaIconCatalogService>.Instance);

    private static string? FullCatalog(string url)
        => IsRoleUrl(url) ? RoleBody : IsWeaponUrl(url) ? WeaponBody : null;

    // ---------------- 远程目录覆盖(核心修复) ----------------

    [Fact]
    public void StaticDictionary_IsStandalone_WithoutRemoteCatalog()
    {
        // 防呆:合成 ID 在纯静态字典下必须是空串,否则下面"远程使其可解析"的断言会假通过
        Assert.Equal("", IconCatalog.GetRoleIconUrl(1399));
        Assert.Equal("", IconCatalog.GetWeaponIconUrl(2399));
        Assert.False(IconCatalog.HasRemoteCatalog);
    }

    [Fact]
    public async Task Refresh_MakesNewRoleResolvable_WithoutCodeChange()
    {
        Assert.Equal("", IconCatalog.GetRoleIconUrl(1399)); // 前提:静态字典没有

        await CreateService(FullCatalog).RefreshAsync();

        Assert.Equal(NewRoleUrl, IconCatalog.GetRoleIconUrl(1399));
    }

    [Fact]
    public async Task Refresh_CoversResidentMembers_NotOnlyLimitedOnes()
    {
        // 用户明确要求:常驻池新增也要能显示图标。拉的是"全量图鉴",不区分限定/常驻,故天然覆盖。
        await CreateService(FullCatalog).RefreshAsync();

        // 常驻角色(卡卡罗 1301)可解析
        Assert.Equal("https://mc.appfeng.com/ui/avatar/T_IconRoleHead256_18_UI.png",
            IconCatalog.GetRoleIconUrl(1301));
        // 合成的新常驻武器也可解析(静态字典里必无 → 只能来自远程)
        Assert.Equal(NewWeaponUrl, IconCatalog.GetWeaponIconUrl(2399));
    }

    [Fact]
    public async Task RemoteCatalog_TakesPrecedence_OverStaticFallback()
    {
        // 远程给出与静态字典不同的文件名时,应以远程为准(远程是权威来源)
        const string remoteRole = """[{"id":1404,"name":"忌炎","icon":"T_IconRoleHead256_999_UI","star":5}]""";
        await CreateService(url => IsRoleUrl(url) ? remoteRole : null).RefreshAsync();

        Assert.Equal("https://mc.appfeng.com/ui/avatar/T_IconRoleHead256_999_UI.png",
            IconCatalog.GetRoleIconUrl(1404));
    }

    [Fact]
    public async Task StaticFallback_StillWorks_WhenRemoteMissesId()
    {
        // 远程目录没有该 ID(如接口尚未来得及收录)→ 回落静态字典,不能变空串
        await CreateService(url => IsRoleUrl(url) ? """[{"id":1399,"icon":"X_UI"}]""" : null).RefreshAsync();

        Assert.Equal(StaticOnlyRoleUrl, IconCatalog.GetRoleIconUrl(1404));
    }

    [Fact]
    public async Task UnknownId_ReturnsEmptyString()
    {
        await CreateService(FullCatalog).RefreshAsync();
        Assert.Equal("", IconCatalog.GetRoleIconUrl(999999));
        Assert.Equal("", IconCatalog.GetWeaponIconUrl(999999));
    }

    // ---------------- 失败降级 ----------------

    [Fact]
    public async Task NetworkFailure_KeepsStaticFallback_AndDoesNotThrow()
    {
        var service = CreateService(_ => null); // 全部 500

        await service.RefreshAsync(); // 不应抛

        // 静态兜底仍可用,且不该写出空目录缓存(避免覆盖掉已有的好数据)
        Assert.Equal(StaticOnlyRoleUrl, IconCatalog.GetRoleIconUrl(1404));
        Assert.False(File.Exists(service.CachePath), "全失败时不应写出缓存");
    }

    [Fact]
    public async Task MalformedJson_FallsBackWithoutThrow()
    {
        await CreateService(url => IsRoleUrl(url) ? "{ not-json" : null).RefreshAsync();

        Assert.Equal(StaticOnlyRoleUrl, IconCatalog.GetRoleIconUrl(1404));
    }

    [Fact]
    public async Task PartialFailure_PreservesPreviouslyCachedSide()
    {
        var time = new FixedTimeProvider(DateTimeOffset.Parse("2026-06-15T00:00:00Z"));
        // 第一轮:两边都成功,新武器可解析
        await CreateService(FullCatalog, time).RefreshAsync();
        Assert.Equal(NewWeaponUrl, IconCatalog.GetWeaponIconUrl(2399));

        // 第二轮:缓存过期 + 武器接口挂掉,角色接口正常 → 武器侧应保留上一轮结果,而非被清空
        time.Now = time.Now.AddDays(2);
        await CreateService(url => IsRoleUrl(url) ? RoleBody : null, time).RefreshAsync();

        Assert.Equal(NewWeaponUrl, IconCatalog.GetWeaponIconUrl(2399));
        Assert.Equal(NewRoleUrl, IconCatalog.GetRoleIconUrl(1399));
    }

    // ---------------- 缓存与 TTL ----------------

    [Fact]
    public async Task Refresh_WritesCache_AndCachedCatalogAppliesOnNextStart()
    {
        var service = CreateService(FullCatalog);
        await service.RefreshAsync();
        Assert.True(File.Exists(service.CachePath));

        // 模拟下次启动:清空内存目录后仅凭磁盘缓存恢复(首帧渲染前必须可用的路径)
        GachaIconCatalogService.ResetForTesting();
        Assert.Equal("", IconCatalog.GetRoleIconUrl(1399));

        CreateService(_ => null).ApplyCachedCatalog();

        Assert.Equal(NewRoleUrl, IconCatalog.GetRoleIconUrl(1399));
    }

    [Fact]
    public async Task FreshCache_SkipsNetworkEntirely()
    {
        var time = new FixedTimeProvider(DateTimeOffset.Parse("2026-06-15T00:00:00Z"));
        await CreateService(FullCatalog, time).RefreshAsync();
        _requestedUrls.Clear();

        // 1 小时后仍新鲜(24h TTL)→ 不应发起任何请求
        time.Now = time.Now.AddHours(1);
        await CreateService(FullCatalog, time).RefreshAsync();

        Assert.Empty(_requestedUrls);
    }

    [Fact]
    public async Task ExpiredCache_TriggersRefetch()
    {
        var time = new FixedTimeProvider(DateTimeOffset.Parse("2026-06-15T00:00:00Z"));
        await CreateService(FullCatalog, time).RefreshAsync();
        _requestedUrls.Clear();

        // 25 小时后过期 → 应重新拉取
        time.Now = time.Now.AddHours(25);
        await CreateService(FullCatalog, time).RefreshAsync();

        Assert.Contains(_requestedUrls, IsRoleUrl);
        Assert.Contains(_requestedUrls, IsWeaponUrl);
    }

    [Fact]
    public void ApplyCachedCatalog_WithNoCache_LeavesStaticFallbackIntact()
    {
        CreateService(_ => null).ApplyCachedCatalog(); // 无缓存文件

        Assert.False(IconCatalog.HasRemoteCatalog);
        Assert.Equal(StaticOnlyRoleUrl, IconCatalog.GetRoleIconUrl(1404));
    }

    [Fact]
    public void CorruptCacheFile_IsIgnored()
    {
        File.WriteAllText(Path.Combine(_dir, "icon_catalog.json"), "{ not-json");
        CreateService(_ => null).ApplyCachedCatalog();

        Assert.False(IconCatalog.HasRemoteCatalog);
        Assert.Equal(StaticOnlyRoleUrl, IconCatalog.GetRoleIconUrl(1404));
    }

    [Fact]
    public void EmptyCacheFile_IsIgnored()
    {
        File.WriteAllText(Path.Combine(_dir, "icon_catalog.json"),
            """{"version":1,"fetchedAt":"2026-06-15T00:00:00+00:00","roles":{},"weapons":{}}""");
        CreateService(_ => null).ApplyCachedCatalog();

        Assert.False(IconCatalog.HasRemoteCatalog);
    }

    // ---------------- 目录解析健壮性 ----------------

    [Fact]
    public async Task ItemsWithMissingIconOrId_AreSkipped()
    {
        const string messy = """
            [{"id":0,"icon":"T_IconRoleHead256_1_UI"},
             {"id":1404,"icon":""},
             {"id":1404,"name":"忌炎","icon":null},
             {"id":1503,"name":"维里奈","icon":"T_IconRoleHead256_3_UI","star":5}]
            """;
        await CreateService(url => IsRoleUrl(url) ? messy : null).RefreshAsync();

        Assert.Equal("https://mc.appfeng.com/ui/avatar/T_IconRoleHead256_3_UI.png",
            IconCatalog.GetRoleIconUrl(1503));
        // 1404 的所有条目 icon 都无效 → 回落静态字典而非空串
        Assert.Equal(StaticOnlyRoleUrl, IconCatalog.GetRoleIconUrl(1404));
    }

    [Fact]
    public async Task GetIconUrl_ByRecordType_UsesRemoteCatalog()
    {
        await CreateService(FullCatalog).RefreshAsync();

        // 用静态字典里没有的 ID,确保断言真的走远程目录
        var role = new GachaRecord { ResourceId = 1399, ResourceType = "角色", QualityLevel = 5 };
        var weapon = new GachaRecord { ResourceId = 2399, ResourceType = "武器", QualityLevel = 5 };

        Assert.Equal(NewRoleUrl, IconCatalog.GetIconUrl(role));
        Assert.Equal(NewWeaponUrl, IconCatalog.GetIconUrl(weapon));
    }
}
