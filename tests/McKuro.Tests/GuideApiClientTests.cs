using System.Net;
using System.Text;
using McKuro.Core.Services.Guide;

namespace McKuro.Tests;

/// <summary>
/// GuideApiClient 测试(mcguide 攻略站:login/sdk → player/list/choose → introduction/list/info)。
/// 用本地 HttpListener 模拟 guide-server,验证请求路径/x-token 头与响应解析。
/// </summary>
public class GuideApiClientTests
{
    private const string XToken = "eyJ4dG9rZW4iOiJ0ZXN0In0";

    /// <summary>启动本地服务器,按请求路径返回脚本中登记的响应。</summary>
    private static (string BaseUrl, HttpListener Listener, List<string> ReceivedBodies, List<string> ReceivedTokens) StartServer(
        Dictionary<string, string> responses)
    {
        var receivedBodies = new List<string>();
        var receivedTokens = new List<string>();
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
                    using var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
                    receivedBodies.Add(reader.ReadToEnd());
                    receivedTokens.Add(ctx.Request.Headers["x-token"] ?? "");

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
        return (prefix, listener, receivedBodies, receivedTokens);
    }

    private static int GetFreePort()
    {
        var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        l.Start();
        var port = ((System.Net.IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    private static GuideApiClient CreateApi(string baseUrl)
        => new(new HttpClient(), baseUrl);

    [Fact]
    public async Task LoginSdkAsync_Returns_Token_From_Envelope()
    {
        var (baseUrl, listener, bodies, _) = StartServer(new Dictionary<string, string>
        {
            ["/user/login/sdk"] =
                """{"code":200,"message":"ok","data":{"token":"eyJjVWlkIjoiMTIzIn0"}}""",
        });
        try
        {
            var api = CreateApi(baseUrl);
            var token = await api.LoginSdkAsync("526781653", "U536781653A", "at-123");
            Assert.Equal("eyJjVWlkIjoiMTIzIn0", token);
            var body = Assert.Single(bodies);
            Assert.Contains("\"cUid\":\"526781653\"", body);
            Assert.Contains("\"cName\":\"U536781653A\"", body);
            Assert.Contains("\"accessToken\":\"at-123\"", body);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task GetPlayerListAsync_Parses_Players_And_Sends_XToken()
    {
        var (baseUrl, listener, _, tokens) = StartServer(new Dictionary<string, string>
        {
            ["/user/player/list"] =
                """{"code":200,"message":"ok","data":[{"playerId":103242935,"playerName":"以椿为鸣","serverId":"srv1","serverName":"国服","level":80}]}""",
        });
        try
        {
            var api = CreateApi(baseUrl);
            var players = await api.GetPlayerListAsync(XToken);
            var p = Assert.Single(players);
            Assert.Equal(103242935, p.PlayerId);
            Assert.Equal("以椿为鸣", p.PlayerName);
            Assert.Equal("srv1", p.ServerId);
            Assert.Equal(80, p.Level);
            Assert.Equal(XToken, Assert.Single(tokens));
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task GetPlayerListAsync_Throws_On_Non200()
    {
        var (baseUrl, listener, _, _) = StartServer(new Dictionary<string, string>
        {
            ["/user/player/list"] =
                """{"code":401,"message":"token 无效","data":null}""",
        });
        try
        {
            var api = CreateApi(baseUrl);
            var ex = await Assert.ThrowsAsync<GuideApiException>(() => api.GetPlayerListAsync(XToken));
            Assert.Contains("获取玩家列表失败", ex.Message);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task ChoosePlayerAsync_Sends_PlayerId_And_ServerId()
    {
        var (baseUrl, listener, bodies, _) = StartServer(new Dictionary<string, string>
        {
            ["/user/player/choose"] =
                """{"code":200,"message":"ok","data":{"profile":{"cUid":"526781653","channelId":201,"chosenPlayer":{"playerId":103242935,"playerName":"以椿为鸣","serverId":"srv1","serverName":"国服","level":80}}}}""",
        });
        try
        {
            var api = CreateApi(baseUrl);
            var profile = await api.ChoosePlayerAsync(XToken, 103242935, "srv1");
            Assert.Equal("以椿为鸣", profile?.Profile?.ChosenPlayer?.PlayerName);
            Assert.Equal(201, profile?.Profile?.ChannelId);
            var body = Assert.Single(bodies);
            Assert.Contains("\"playerId\":103242935", body);
            Assert.Contains("\"serverId\":\"srv1\"", body);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task GetIntroductionListAsync_Orders_By_LikeCount_Desc()
    {
        var (baseUrl, listener, _, _) = StartServer(new Dictionary<string, string>
        {
            ["/introduction/list"] =
                """
                {"code":200,"message":"ok","data":[
                  {"id":10162,"role":{"roleGbId":"1209","star":5,"texts":[{"language":"zh-Hans","name":"莫宁"}]},"likeCount":10,"collectCount":5},
                  {"id":10161,"role":{"roleGbId":"1209","star":5,"texts":[{"language":"zh-Hans","name":"莫宁"}]},"likeCount":99,"collectCount":20}
                ]}
                """,
        });
        try
        {
            var api = CreateApi(baseUrl);
            var list = await api.GetIntroductionListAsync(XToken, "1209");
            Assert.Equal(2, list.Count);
            Assert.Equal(10161, list[0].Id); // 点赞高的在前
            Assert.Equal(10162, list[1].Id);
            Assert.Equal("莫宁", list[0].Role?.Name);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task GetIntroductionInfoAsync_Parses_Achievement()
    {
        var (baseUrl, listener, _, _) = StartServer(new Dictionary<string, string>
        {
            ["/introduction/info"] =
                """
                {"code":200,"message":"ok","data":{
                  "id":10177,
                  "role":{"roleGbId":"1108","star":5,"texts":[{"language":"zh-Hans","name":"绯雪"}]},
                  "grade":"SS",
                  "roleAttribute":{"items":[
                    {"gbId":"8-2","texts":[{"language":"zh-Hans","name":"暴击"}],"recommendAmount":"65.0%","currentAmount":"74.2%","isFinished":true},
                    {"gbId":"9-2","texts":[{"language":"zh-Hans","name":"暴击伤害"}],"recommendAmount":"270.0%","currentAmount":"264.2%","isFinished":false}
                  ],"isFinished":false},
                  "roleResonance":{"items":[
                    {"resonanceSequence":1,"texts":[{"language":"zh-Hans","name":"一链"}],"isAcquired":true},
                    {"resonanceSequence":2,"texts":[{"language":"zh-Hans","name":"二链"}],"isAcquired":false}
                  ],"isFinished":false},
                  "echo":{"current":{"echoAttributes":[{"cost":4,"currentLevel":25,"isFinishedMaxLevel":true,"isFinished":true,"attribute":{"texts":[{"language":"zh-Hans","name":"暴击伤害"}]}}]},"isFinished":true},
                  "weapon":{"items":[{"gbId":"21020086","star":5,"texts":[{"language":"zh-Hans","name":"灼霜"}],"isAcquired":true,"isFinished":true}]}
                }}
                """,
        });
        try
        {
            var api = CreateApi(baseUrl);
            var info = await api.GetIntroductionInfoAsync(XToken, "1108", 10177);
            Assert.NotNull(info);
            Assert.Equal("SS", info!.Grade);
            Assert.Equal("绯雪", info.Role?.Name);
            Assert.Equal(1, info.RoleAttribute?.FinishedCount); // 暴击已达标
            Assert.Equal(2, info.RoleAttribute?.TotalCount);
            Assert.Equal(1, info.RoleResonance?.AcquiredCount);
            Assert.Equal(2, info.RoleResonance?.TotalCount);
            Assert.Equal("暴击", info.RoleAttribute?.Items?[0].Name);
            Assert.Equal("74.2%", info.RoleAttribute?.Items?[0].CurrentAmount);
            Assert.True(info.Echo?.Current?.EchoAttributes?[0].IsFinishedMaxLevel);
            Assert.Equal("灼霜", info.Weapon?.Items?[0].Name);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task GetIntroductionInfoAsync_Throws_On_Non200()
    {
        var (baseUrl, listener, _, _) = StartServer(new Dictionary<string, string>
        {
            ["/introduction/info"] =
                """{"code":500,"message":"攻略不存在","data":null}""",
        });
        try
        {
            var api = CreateApi(baseUrl);
            var ex = await Assert.ThrowsAsync<GuideApiException>(
                () => api.GetIntroductionInfoAsync(XToken, "1209", 1));
            Assert.Contains("获取攻略详情失败", ex.Message);
        }
        finally
        {
            listener.Stop();
        }
    }
}
