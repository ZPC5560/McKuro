using System.Net;
using System.Text;
using McKuro.Core.Services.Roles;

namespace McKuro.Tests;

/// <summary>
/// KujiequApiClient 测试(对齐 WutheringWavesTool 数据中心接口流程:
/// requestToken → roleData/getRoleDetail/refreshData,固定 serverId/gameId)。
/// 用本地 HttpListener 模拟库街区接口,验证请求路径/参数与响应解析。
/// </summary>
public class KujiequApiClientTests
{
    private const string Token = "test-token";
    private const string DeviceId = "test-device";
    private const string RoleId = "103242935";
    private const string UserId = "12345678";

    /// <summary>启动本地服务器,按请求路径返回脚本中登记的响应。</summary>
    private static (string BaseUrl, HttpListener Listener, List<string> ReceivedBodies) StartServer(
        Dictionary<string, string> responses)
    {
        var receivedBodies = new List<string>();
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
        return (prefix, listener, receivedBodies);
    }

    private static int GetFreePort()
    {
        var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        l.Start();
        var port = ((System.Net.IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    private static HttpClient CreateClient(string baseUrl)
        => new() { BaseAddress = new Uri(baseUrl) };

    private static KujiequApiClient CreateApi(string baseUrl)
        => new(CreateClient(baseUrl), baseUrl);

    [Fact]
    public async Task GetAccessTokenAsync_Parses_AccessToken_From_Envelope()
    {
        var (baseUrl, listener, bodies) = StartServer(new Dictionary<string, string>
        {
            ["/aki/roleBox/requestToken"] =
                """{"code":200,"data":"{\"accessToken\":\"at-12345\"}","msg":"成功","success":true}""",
        });
        try
        {
            var client = CreateApi(baseUrl);
            var at = await client.GetAccessTokenAsync(Token, DeviceId, RoleId, UserId);
            Assert.Equal("at-12345", at);
            var body = Assert.Single(bodies);
            Assert.Contains("roleId=" + RoleId, body);
            Assert.Contains("userId=" + UserId, body);
            Assert.Contains("serverId=" + KujiequApiClient.ParamServerId, body);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task GetAccessTokenAsync_Returns_Null_On_Non200()
    {
        var (baseUrl, listener, _) = StartServer(new Dictionary<string, string>
        {
            ["/aki/roleBox/requestToken"] =
                """{"code":500,"data":null,"msg":"Token 无效","success":false}""",
        });
        try
        {
            var client = CreateApi(baseUrl);
            var at = await client.GetAccessTokenAsync(Token, DeviceId, RoleId, UserId);
            Assert.Null(at);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task GetRoleDataAsync_Parses_RoleList_Into_RoleDetail()
    {
        var (baseUrl, listener, bodies) = StartServer(new Dictionary<string, string>
        {
            ["/aki/roleBox/akiBox/roleData"] =
                """{"code":200,"data":"{\"roleList\":[{\"roleId\":103242935,\"roleName\":\"漂泊者\",\"level\":80,\"breach\":6,\"chainUnlockNum\":4,\"starLevel\":5,\"attributeName\":\"衍射\",\"acronym\":\"PBZ\"}]}","msg":"成功","success":true}""",
        });
        try
        {
            var client = CreateApi(baseUrl);
            var roles = await client.GetRoleDataAsync("at-1", DeviceId, RoleId);
            var role = Assert.Single(roles);
            Assert.Equal("漂泊者", role.RoleName);
            Assert.Equal(80, role.Level);
            Assert.Equal(5, role.StarLevel);
            Assert.Equal("衍射", role.AttributeName);
            var body = Assert.Single(bodies);
            Assert.Contains("roleId=" + RoleId, body);
            Assert.Contains("gameId=" + KujiequApiClient.ParamGameId, body);
            Assert.Contains("serverId=" + KujiequApiClient.ParamServerId, body);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task GetRoleDataAsync_Throws_On_Non200()
    {
        var (baseUrl, listener, _) = StartServer(new Dictionary<string, string>
        {
            ["/aki/roleBox/akiBox/roleData"] =
                """{"code":500,"data":null,"msg":"角色查询失败，请重新选择角色","success":false}""",
        });
        try
        {
            var client = CreateApi(baseUrl);
            var ex = await Assert.ThrowsAsync<KujiequApiException>(
                () => client.GetRoleDataAsync("at-1", DeviceId, RoleId));
            Assert.Contains("角色查询失败", ex.Message);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task GetRoleDetailAsync_Parses_Full_Detail_With_Id_Param()
    {
        var (baseUrl, listener, bodies) = StartServer(new Dictionary<string, string>
        {
            ["/aki/roleBox/akiBox/getRoleDetail"] =
                """
                {"code":200,"data":"{\"role\":{\"roleId\":103242935,\"roleName\":\"漂泊者\",\"starLevel\":5,\"level\":80,\"breach\":6,\"attributeName\":\"衍射\"},\"weaponData\":{\"weapon\":{\"weaponName\":\"千古洑流\",\"weaponStarLevel\":5},\"level\":90,\"breach\":6,\"resonLevel\":5},\"skillList\":[{\"level\":10,\"skill\":{\"id\":100,\"name\":\"普攻\"}}],\"chainList\":[{\"order\":1,\"name\":\"一链\",\"unlocked\":true}],\"roleAttributeList\":[{\"attributeName\":\"攻击\",\"attributeValue\":\"1000\"}],\"phantomData\":{\"cost\":12,\"equipPhantomList\":[{\"level\":25,\"cost\":3,\"quality\":5,\"phantomProp\":{\"name\":\"啸谷幼猿\",\"phantomId\":12,\"iconUrl\":\"http://img/12.png\",\"quality\":5,\"cost\":3}}]}}","msg":"成功","success":true}
                """,
        });
        try
        {
            var client = CreateApi(baseUrl);
            // targetRoleId = roleList 项的 roleId(cardRoleId),应作为 body id 传参
            var detail = await client.GetRoleDetailAsync("at-1", DeviceId, RoleId, 1304);
            Assert.NotNull(detail);
            Assert.Equal("漂泊者", detail!.RoleName);
            Assert.Equal(80, detail.Role?.Level);
            Assert.Equal("千古洑流", detail.WeaponData?.DisplayName);
            Assert.Equal(5, detail.WeaponData?.Rank); // resonLevel → Rank
            var skill = Assert.Single(detail.Skills ?? []);
            Assert.Equal(10, skill.SkillLevel);
            Assert.Equal("普攻", skill.SkillName);
            var chain = Assert.Single(detail.Chains ?? []);
            Assert.Equal(1, chain.ChainNum); // order → ChainNum
            Assert.Equal("一链", chain.ChainName); // name → ChainName
            Assert.True(chain.IsUnlock); // unlocked → IsUnlock
            var attr = Assert.Single(detail.Attributes ?? []);
            Assert.Equal("攻击", attr.AttributeName);
            var echo = Assert.Single(detail.PhantomData?.Phantoms ?? []);
            Assert.Equal("啸谷幼猿", echo.PhantomName); // phantomProp.name → PhantomName
            Assert.Equal(25, echo.Level);
            var body = Assert.Single(bodies);
            Assert.Contains("id=1304", body);
            Assert.Contains("roleId=" + RoleId, body);
            Assert.Contains("channelId=19", body);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task RefreshDataAsync_Returns_True_On_200()
    {
        var (baseUrl, listener, _) = StartServer(new Dictionary<string, string>
        {
            // refreshData 真实响应:data 为布尔值
            ["/aki/roleBox/akiBox/refreshData"] =
                """{"code":200,"data":true,"msg":"成功","success":true}""",
        });
        try
        {
            var client = CreateApi(baseUrl);
            var ok = await client.RefreshDataAsync("at-1", DeviceId, RoleId);
            Assert.True(ok);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task GetGamerBaseDataAsync_Parses_BaseData_And_Sends_Expected_Form()
    {
        var (baseUrl, listener, bodies) = StartServer(new Dictionary<string, string>
        {
            // baseData(数据中心)响应:data 为 JSON 字符串(对齐 Haiyu GamerBassString)
            ["/aki/roleBox/akiBox/baseData"] =
                """
                {"code":200,"data":"{\"name\":\"以椿为鸣\",\"level\":80,\"activeDays\":818,\"creatTime\":1716441600000,\"weeklyInstCount\":1,\"weeklyInstCountLimit\":2,\"weeklyInstIconUrl\":\"https://prod-alicdn-gamestarter.kurogame.com/pcstarter/prod/game/aki/1002_3a76b8f59zPZz/weeklyInst.png\"}","msg":"成功","success":true}
                """,
        });
        try
        {
            var client = CreateApi(baseUrl);
            var baseData = await client.GetGamerBaseDataAsync(Token, DeviceId, RoleId);
            Assert.NotNull(baseData);
            Assert.Equal("以椿为鸣", baseData.Name);
            Assert.Equal(80, baseData.Level);
            Assert.Equal(818, baseData.ActiveDays);
            Assert.Equal(1716441600000, baseData.CreatTime);
            Assert.Equal(1, baseData.WeeklyInstCount);
            Assert.Equal(2, baseData.WeeklyInstCountLimit);
            Assert.Contains("weeklyInst.png", baseData.WeeklyInstIconUrl);
            var body = Assert.Single(bodies);
            Assert.Contains("gameId=" + KujiequApiClient.ParamGameId, body);
            Assert.Contains("roleId=" + RoleId, body);
            Assert.Contains("serverId=" + KujiequApiClient.ParamServerId, body);
            Assert.Contains("channelId=19", body);
            Assert.Contains("countryCode=1", body);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task GetGamerBaseDataAsync_Returns_Null_On_Non200()
    {
        var (baseUrl, listener, _) = StartServer(new Dictionary<string, string>
        {
            ["/aki/roleBox/akiBox/baseData"] =
                """{"code":500,"data":null,"msg":"查询失败","success":false}""",
        });
        try
        {
            var client = CreateApi(baseUrl);
            var baseData = await client.GetGamerBaseDataAsync(Token, DeviceId, RoleId);
            Assert.Null(baseData);
        }
        finally
        {
            listener.Stop();
        }
    }
}
