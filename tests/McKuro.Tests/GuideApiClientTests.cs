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

    [Fact]
    public async Task GetRoleInfoAsync_Parses_All_Skill_Videos_And_RolePlays()
    {
        // 回归:技能演示视频不在 introduction/info(那里只有 keynoteSkills 的 1 个),
        // 而在 role/info 的 data.skills[].videoUrl —— 实测心的 5 个技能全有 mp4。
        // 此前用 introduction/info 当数据源,导致"只有首个技能有视频、其余显示无视频"。
        var (baseUrl, listener, _, tokens) = StartServer(new Dictionary<string, string>
        {
            ["/role/info"] =
                """
                {"code":200,"message":"ok","data":{
                  "roleGbId":"1311","star":5,
                  "texts":[{"language":"zh-Hans","name":"心","skillDisplay":"基础连招:同奏→延奏离场"}],
                  "rolePlays":[
                    {"gbId":"2","pictureUrl":"http://img/t1.png"},
                    {"gbId":"39","pictureUrl":"http://img/t2.png"}
                  ],
                  "skills":[
                    {"gbId":"1014-1311","pictureUrl":"http://img/s1.png","videoUrl":"http://v/1.mp4","skillType":{"texts":[{"language":"zh-Hans","name":"常态攻击"}]},"texts":[{"language":"zh-Hans","name":"万相生华","description":"普攻描述"}]},
                    {"gbId":"1015-1311","pictureUrl":"http://img/s2.png","videoUrl":"http://v/2.mp4","skillType":{"texts":[{"language":"zh-Hans","name":"共鸣技能"}]},"texts":[{"language":"zh-Hans","name":"踏月归心"}]},
                    {"gbId":"1020-1311","pictureUrl":"http://img/s3.png","videoUrl":"http://v/3.mp4","skillType":{"texts":[{"language":"zh-Hans","name":"共鸣回路"}]},"texts":[{"language":"zh-Hans","name":"万相流转,此心自明"}]},
                    {"gbId":"1016-1311","pictureUrl":"http://img/s4.png","videoUrl":"http://v/4.mp4","skillType":{"texts":[{"language":"zh-Hans","name":"共鸣解放"}]},"texts":[{"language":"zh-Hans","name":"引枢作清辉"}]},
                    {"gbId":"1019-1311","pictureUrl":"http://img/s5.png","videoUrl":"http://v/5.mp4","skillType":{"texts":[{"language":"zh-Hans","name":"变奏技能"}]},"texts":[{"language":"zh-Hans","name":"乘兴一顾"}]}
                  ]
                }}
                """,
        });
        try
        {
            var api = CreateApi(baseUrl);
            var data = await api.GetRoleInfoAsync(XToken, "1311");

            Assert.NotNull(data);
            Assert.Equal("心", data!.Name);
            // 5 个技能全部带视频(核心契约:技能演示列表数据源)
            Assert.Equal(5, data.Skills?.Count);
            Assert.All(data.Skills!, s => Assert.True(s.HasVideo, $"{s.Name} 应有演示视频"));
            Assert.Equal(5, data.Skills!.Count(s => s.HasVideo));
            // 技能名/类型/图标/描述映射
            Assert.Equal("万相生华", data.Skills![0].Name);
            Assert.Equal("常态攻击", data.Skills[0].TypeName);
            Assert.Equal("http://img/s1.png", data.Skills[0].PictureUrl);
            Assert.Equal("普攻描述", data.Skills[0].Description);
            // 评审反馈:逐项锁死 VideoUrl 字符串(此前只断 HasVideo 布尔,映射错位测不出来)
            Assert.Equal("http://v/1.mp4", data.Skills[0].VideoUrl);
            Assert.Equal("http://v/2.mp4", data.Skills[1].VideoUrl);
            Assert.Equal("http://v/3.mp4", data.Skills[2].VideoUrl);
            Assert.Equal("http://v/4.mp4", data.Skills[3].VideoUrl);
            Assert.Equal("http://v/5.mp4", data.Skills[4].VideoUrl);
            // 角色特点图标
            Assert.Equal(2, data.RolePlays?.Count);
            Assert.Equal("http://img/t2.png", data.RolePlays?[1].PictureUrl);
            // 连招文本
            Assert.Contains("延奏离场", data.Texts?[0].SkillDisplay);
            // 请求带 x-token
            Assert.Contains(XToken, tokens);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task GetRoleInfoAsync_Throws_On_Non200()
    {
        var (baseUrl, listener, _, _) = StartServer(new Dictionary<string, string>
        {
            ["/role/info"] = """{"code":500,"message":"server error","data":null}""",
        });
        try
        {
            var api = CreateApi(baseUrl);
            var ex = await Assert.ThrowsAsync<GuideApiException>(
                () => api.GetRoleInfoAsync(XToken, "1311"));
            Assert.Contains("获取角色资料失败", ex.Message);
        }
        finally
        {
            listener.Stop();
        }
    }
}
