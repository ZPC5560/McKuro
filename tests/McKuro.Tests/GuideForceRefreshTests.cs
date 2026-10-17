using System.Net;
using System.Text;
using McKuro.Core.Infrastructure;
using McKuro.Core.Services.CloudGame;
using McKuro.Core.Services.Guide;
using McKuro.Core.Services.Settings;
using Microsoft.Extensions.Logging.Abstractions;

namespace McKuro.Tests;

/// <summary>
/// 「手动同步必须真的去攻略站拉一次」的服务层回归:
/// <see cref="GuideAchievementService.GetIntroductionListAsync"/> /
/// <see cref="GuideAchievementService.GetAchievementByIdAsync"/> 的 forceRefresh 契约。
/// <para>
/// 背景(用户报障):角色数据页点「同步」后攻略站数据没有同步过来,要切走再切回才刷新。
/// 根因是攻略数据只经「选中角色变化」这条副作用链路拉取,而该链路命中 24h 本地缓存时
/// 不发任何网络请求 —— 点同步对攻略区等于没动作。
/// </para>
/// <para>
/// 这些用例**统计真实 HTTP 请求次数**,而不是自己复算缓存判定,所以去掉
/// forceRefresh 的 bypass(把 <c>!forceRefresh &amp;&amp;</c> 改回无条件缓存命中)会让
/// 「强制刷新仍走网络」用例真实失败。
/// </para>
/// </summary>
public class GuideForceRefreshTests : IDisposable
{
    private readonly string _dir;
    private readonly AppDatabase _db;
    private readonly GuideCacheService _cache;

    public GuideForceRefreshTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "McKuro-guide-force-" + Guid.NewGuid().ToString("N"));
        _db = new AppDatabase(_dir);
        _cache = new GuideCacheService(_db, NullLogger<GuideCacheService>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            // 临时目录清理失败不影响断言
        }
        GC.SuppressFinalize(this);
    }

    private sealed class FakeSettings : ISettingsService
    {
        public AppSettings Current { get; } = new();
        public void Save() { }
        public Task SaveAsync(CancellationToken ct = default) => Task.CompletedTask;
        public void Reload() { }
    }

    /// <summary>本地假攻略站:按路径返回固定响应,并统计每条路径收到的请求次数。</summary>
    private sealed class FakeGuideServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly Dictionary<string, int> _hits = [];
        private readonly object _gate = new();

        public string BaseUrl { get; }

        public FakeGuideServer(Dictionary<string, string> responses)
        {
            var port = GetFreePort();
            BaseUrl = $"http://127.0.0.1:{port}/";
            _listener.Prefixes.Add(BaseUrl);
            _listener.Start();
            _ = Task.Run(async () =>
            {
                while (_listener.IsListening)
                {
                    try
                    {
                        var ctx = await _listener.GetContextAsync();
                        var path = ctx.Request.Url!.AbsolutePath;
                        lock (_gate)
                        {
                            _hits[path] = _hits.GetValueOrDefault(path) + 1;
                        }
                        if (responses.TryGetValue(path, out var body))
                        {
                            var bytes = Encoding.UTF8.GetBytes(body);
                            ctx.Response.StatusCode = 200;
                            ctx.Response.ContentLength64 = bytes.Length;
                            await ctx.Response.OutputStream.WriteAsync(bytes);
                        }
                        else
                        {
                            ctx.Response.StatusCode = 404;
                        }
                        ctx.Response.Close();
                    }
                    catch
                    {
                        break;
                    }
                }
            });
        }

        public int Hits(string path)
        {
            lock (_gate)
            {
                return _hits.GetValueOrDefault(path);
            }
        }

        private static int GetFreePort()
        {
            var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            l.Start();
            var port = ((System.Net.IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return port;
        }

        public void Dispose() => _listener.Stop();
    }

    private GuideAchievementService CreateService(string baseUrl)
    {
        var settings = new FakeSettings();
        settings.Current.GuideToken = "test-token";
        var cloud = new CloudGameService(new HttpClient(), "test-device");
        var api = new GuideApiClient(new HttpClient(), baseUrl);
        return new GuideAchievementService(
            cloud, api, settings, NullLogger<GuideAchievementService>.Instance, _cache);
    }

    private static Dictionary<string, string> Responses(string list, string info) => new()
    {
        ["/introduction/list"] = list,
        ["/introduction/info"] = info,
    };

    private const string ListBody =
        """{"code":200,"message":"ok","data":[{"id":10161,"role":{"roleGbId":"1209","star":5,"texts":[{"language":"zh-Hans","name":"莫宁"}]},"likeCount":99}]}""";

    private const string InfoBody =
        """{"code":200,"message":"ok","data":{"id":10161,"grade":"SS","role":{"roleGbId":"1209","star":5,"texts":[{"language":"zh-Hans","name":"莫宁"}]}}}""";

    [Fact]
    public async Task GetIntroductionListAsync_Serves_Fresh_Cache_Without_Network()
    {
        using var server = new FakeGuideServer(Responses(ListBody, InfoBody));
        var service = CreateService(server.BaseUrl);

        var first = await service.GetIntroductionListAsync("莫宁", 1209);
        Assert.Single(first);

        // 第二次:缓存已新鲜,不应再打接口(这是既有的"点角色秒开"行为,不能被修复破坏)
        var second = await service.GetIntroductionListAsync("莫宁", 1209);
        Assert.Single(second);
        Assert.Equal(1, server.Hits("/introduction/list"));
    }

    [Fact]
    public async Task GetIntroductionListAsync_ForceRefresh_Bypasses_Fresh_Cache()
    {
        using var server = new FakeGuideServer(Responses(ListBody, InfoBody));
        var service = CreateService(server.BaseUrl);

        await service.GetIntroductionListAsync("莫宁", 1209);
        Assert.Equal(1, server.Hits("/introduction/list"));

        // 手动同步:即便缓存新鲜也必须真的去攻略站拉一次。
        // 去掉 service 里的 forceRefresh bypass 时,这里会停在 1 次请求 → 用例失败。
        var forced = await service.GetIntroductionListAsync("莫宁", 1209, forceRefresh: true);
        Assert.Single(forced);
        Assert.Equal(2, server.Hits("/introduction/list"));
    }

    [Fact]
    public async Task GetAchievementByIdAsync_ForceRefresh_Bypasses_Fresh_Cache()
    {
        using var server = new FakeGuideServer(Responses(ListBody, InfoBody));
        var service = CreateService(server.BaseUrl);

        var first = await service.GetAchievementByIdAsync("莫宁", 1209, 10161);
        Assert.Equal("SS", first!.Grade);
        Assert.Equal(1, server.Hits("/introduction/info"));

        // 非强制:命中新鲜详情缓存
        var cached = await service.GetAchievementByIdAsync("莫宁", 1209, 10161);
        Assert.Equal("SS", cached!.Grade);
        Assert.Equal(1, server.Hits("/introduction/info"));

        // 强制(手动同步):必须再走一次网络
        var forced = await service.GetAchievementByIdAsync("莫宁", 1209, 10161, forceRefresh: true);
        Assert.Equal("SS", forced!.Grade);
        Assert.Equal(2, server.Hits("/introduction/info"));
    }

    [Fact]
    public async Task GetAchievementAsync_ForceRefresh_Bypasses_Both_List_And_Detail_Cache()
    {
        using var server = new FakeGuideServer(Responses(ListBody, InfoBody));
        var service = CreateService(server.BaseUrl);

        var first = await service.GetAchievementAsync("莫宁", 1209);
        Assert.Equal("SS", first!.Grade);
        Assert.Equal(1, server.Hits("/introduction/list"));
        Assert.Equal(1, server.Hits("/introduction/info"));

        // 手动同步入口(默认取最高赞攻略):列表与详情都必须重拉
        var forced = await service.GetAchievementAsync("莫宁", 1209, forceRefresh: true);
        Assert.Equal("SS", forced!.Grade);
        Assert.Equal(2, server.Hits("/introduction/list"));
        Assert.Equal(2, server.Hits("/introduction/info"));
    }

    [Fact]
    public async Task GetIntroductionListAsync_ForceRefresh_Still_Uses_Cache_When_Network_Fails()
    {
        // 攻略站不可用时,强制刷新也必须回退到已有缓存(不能让界面空着)
        using var failing = new FakeGuideServer([]);
        var service = CreateService(failing.BaseUrl);

        // 先用一个能用的服务把缓存写热
        using var ok = new FakeGuideServer(Responses(ListBody, InfoBody));
        var warm = CreateService(ok.BaseUrl);
        Assert.Single(await warm.GetIntroductionListAsync("莫宁", 1209));

        // 同一个 _cache:强制刷新打不通网络 → 回退缓存而不是抛异常
        var fallback = await service.GetIntroductionListAsync("莫宁", 1209, forceRefresh: true);
        Assert.Single(fallback);
    }
}
