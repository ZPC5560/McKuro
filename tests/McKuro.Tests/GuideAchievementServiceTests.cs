using System.Net;
using System.Text;
using McKuro.Core.Services.CloudGame;
using McKuro.Core.Services.Guide;
using McKuro.Core.Services.Settings;

namespace McKuro.Tests;

/// <summary>
/// GuideAchievementService 编排测试:已登录(x-token 已存)前提下,
/// GetAchievementAsync 走 introduction/list(取最高赞) → introduction/info。
/// 用本地 HttpListener 模拟 guide-server。
/// </summary>
public class GuideAchievementServiceTests
{
    private sealed class FakeSettings : ISettingsService
    {
        public AppSettings Current { get; } = new();
        public void Save() { }
        public Task SaveAsync(CancellationToken ct = default) => Task.CompletedTask;
        public void Reload() { }
    }

    private static (string BaseUrl, HttpListener Listener) StartGuideServer(Dictionary<string, string> responses)
    {
        var listener = new HttpListener();
        var prefix = $"http://127.0.0.1:{GetFreePort()}/";
        listener.Prefixes.Add(prefix);
        listener.Start();
        _ = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                try
                {
                    var ctx = await listener.GetContextAsync();
                    var path = ctx.Request.Url!.AbsolutePath;
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
        return (prefix, listener);
    }

    private static int GetFreePort()
    {
        var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        l.Start();
        var port = ((System.Net.IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    private static GuideAchievementService CreateService(string guideBaseUrl, FakeSettings settings)
    {
        var cloud = new CloudGameService(new HttpClient(), "test-device");
        var api = new GuideApiClient(new HttpClient(), guideBaseUrl);
        return new GuideAchievementService(cloud, api, settings);
    }

    [Fact]
    public async Task GetAchievementAsync_Picks_Top_Liked_Guide()
    {
        var (baseUrl, listener) = StartGuideServer(new Dictionary<string, string>
        {
            ["/introduction/list"] =
                """
                {"code":200,"message":"ok","data":[
                  {"id":10162,"role":{"roleGbId":"1209","star":5,"texts":[{"language":"zh-Hans","name":"莫宁"}]},"likeCount":10},
                  {"id":10161,"role":{"roleGbId":"1209","star":5,"texts":[{"language":"zh-Hans","name":"莫宁"}]},"likeCount":99}
                ]}
                """,
            ["/introduction/info"] =
                """{"code":200,"message":"ok","data":{"id":10161,"grade":"SS","role":{"roleGbId":"1209","star":5,"texts":[{"language":"zh-Hans","name":"莫宁"}]}}}""",
        });
        try
        {
            var settings = new FakeSettings();
            settings.Current.GuideToken = "test-token";
            var service = CreateService(baseUrl, settings);
            var info = await service.GetAchievementAsync("莫宁", 1209);
            Assert.NotNull(info);
            Assert.Equal("SS", info!.Grade);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task GetAchievementAsync_Returns_Null_Without_Token()
    {
        var (baseUrl, listener) = StartGuideServer(new Dictionary<string, string>());
        try
        {
            var settings = new FakeSettings(); // GuideToken 为空
            var service = CreateService(baseUrl, settings);
            var info = await service.GetAchievementAsync("莫宁", 1209);
            Assert.Null(info);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task GetAchievementAsync_Returns_Null_For_Invalid_CardRoleId()
    {
        var (baseUrl, listener) = StartGuideServer(new Dictionary<string, string>());
        try
        {
            var settings = new FakeSettings();
            settings.Current.GuideToken = "test-token";
            var service = CreateService(baseUrl, settings);
            var info = await service.GetAchievementAsync("漂泊者", 0);
            Assert.Null(info);
        }
        finally
        {
            listener.Stop();
        }
    }
}
