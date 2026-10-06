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
    public async Task GetRoleDetailResultAsync_Flags_GeeTest_Too_When_Raw_Json()
    {
        // 库街区风控真实响应:无 code/data,只有 {"geeTest":true} → 不抛异常,显式标记
        var (baseUrl, listener, _) = StartServer(new Dictionary<string, string>
        {
            ["/aki/roleBox/akiBox/getRoleDetail"] = """{"geeTest":true}""",
        });
        try
        {
            var client = CreateApi(baseUrl);
            var result = await client.GetRoleDetailResultAsync("at-1", DeviceId, RoleId, 1304);
            Assert.Null(result.Detail);
            Assert.True(result.GeeTest);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task GetRoleDetailResultAsync_Parses_Normal_Detail_Without_GeeTest()
    {
        var (baseUrl, listener, _) = StartServer(new Dictionary<string, string>
        {
            ["/aki/roleBox/akiBox/getRoleDetail"] =
                """
                {"code":200,"data":"{\"role\":{\"roleId\":103242935,\"roleName\":\"漂泊者\",\"starLevel\":5,\"level\":80,\"breach\":6,\"attributeName\":\"衍射\"},\"weaponData\":{\"weapon\":{\"weaponName\":\"千古洑流\",\"weaponStarLevel\":5},\"level\":90,\"breach\":6,\"resonLevel\":5},\"skillList\":[{\"level\":10,\"skill\":{\"id\":100,\"name\":\"普攻\"}}],\"chainList\":[{\"order\":1,\"name\":\"一链\",\"unlocked\":true}],\"roleAttributeList\":[{\"attributeName\":\"攻击\",\"attributeValue\":\"1000\"}]}","msg":"成功","success":true}
                """,
        });
        try
        {
            var client = CreateApi(baseUrl);
            var result = await client.GetRoleDetailResultAsync("at-1", DeviceId, RoleId, 1304);
            Assert.False(result.GeeTest);
            Assert.Equal("漂泊者", result.Detail?.RoleName);
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

    [Fact]
    public async Task BaseData_Sends_OfficialH5_WebView_Headers()
    {
        // 抓取数据中心请求的完整请求头,验证与官方 App H5(对齐 Haiyu GetWebHeader)一致:
        // WebView UA + Origin + X-Requested-With + devCode(IP, UA) + b-at + token
        var headers = new List<string>();
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
                    _ = reader.ReadToEnd();
                    foreach (var key in ctx.Request.Headers.AllKeys)
                    {
                        headers.Add($"{key}: {ctx.Request.Headers[key]}");
                    }
                    var body = """
                        {"code":200,"data":"{\"name\":\"test\"}","msg":"成功","success":true}
                        """;
                    var bytes = Encoding.UTF8.GetBytes(body);
                    ctx.Response.StatusCode = 200;
                    ctx.Response.ContentLength64 = bytes.Length;
                    await ctx.Response.OutputStream.WriteAsync(bytes);
                    ctx.Response.Close();
                }
                catch
                {
                    break;
                }
            }
        });
        try
        {
            var client = CreateApi(prefix);
            client.PublicIp = "203.0.113.7";
            await client.GetGamerBaseDataAsync(Token, DeviceId, RoleId);
            var text = string.Join("\n", headers);
            Assert.Contains("Origin: https://web-static.kurobbs.com", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("X-Requested-With: com.kurogame.kjq", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("b-at: " + Token, text, StringComparison.OrdinalIgnoreCase);
            // 实测:数据中心请求带 token 头会被服务端回 code=10000「参数错误」,必须禁止
            Assert.DoesNotContain("token: " + Token, text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("did: " + DeviceId, text, StringComparison.OrdinalIgnoreCase);
            var uaLine = headers.FirstOrDefault(h => h.StartsWith("User-Agent:", StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(uaLine);
            Assert.Contains("Kuro/3.1.2 KuroGameBox/3.1.2", uaLine);
            Assert.Contains("Mozilla/5.0 (Linux; Android 9; 2509FPN0BC", uaLine, StringComparison.Ordinal);
            var devCodeLine = headers.FirstOrDefault(h => h.StartsWith("devcode:", StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(devCodeLine);
            Assert.StartsWith("devcode: 203.0.113.7, Mozilla/5.0", devCodeLine, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task RefreshDataAsync_Sends_ThreeFieldBody_And_Returns_True_On_200()
    {
        // 2026-10 实测:refreshData 用数据中心头 + 仅 3 字段 body(gameId/roleId/serverId)
        // 返回 {"code":200,"data":true};body 含 channelId/countryCode 等多字段会被服务端拒绝。
        var (baseUrl, listener, bodies) = StartServer(new Dictionary<string, string>
        {
            ["/aki/roleBox/akiBox/refreshData"] =
                """{"code":200,"msg":"请求成功","data":true,"success":true}""",
        });
        try
        {
            var client = CreateApi(baseUrl);
            var ok = await client.RefreshDataAsync("at-1", DeviceId, RoleId, "server-abc");
            Assert.True(ok);
            var body = Assert.Single(bodies);
            Assert.Contains("gameId=" + KujiequApiClient.ParamGameId, body);
            Assert.Contains("roleId=" + RoleId, body);
            Assert.Contains("serverId=server-abc", body);
            // 3 字段 body:不应包含数据中心其他字段
            Assert.DoesNotContain("channelId=", body);
            Assert.DoesNotContain("countryCode=", body);
            Assert.DoesNotContain("id=", body);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task RefreshDataAsync_Returns_False_On_Non200_Or_GeeTest()
    {
        var (baseUrl, listener, _) = StartServer(new Dictionary<string, string>
        {
            ["/aki/roleBox/akiBox/refreshData"] =
                """{"code":10900,"msg":"角色查询失败，请重新选择角色","data":null,"success":false}""",
        });
        try
        {
            var client = CreateApi(baseUrl);
            var ok = await client.RefreshDataAsync("at-1", DeviceId, RoleId, "server-abc");
            Assert.False(ok);

            // 空 accessToken 在发请求前短路返回 false
            var ok2 = await client.RefreshDataAsync("", DeviceId, RoleId, "server-abc");
            Assert.False(ok2);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task RefreshDataAsync_Returns_False_On_GeeTest_Response()
    {
        // 评审反馈:旧用例名含 GeeTest 但从未走到 geeTest 分支(mock 只回 10900,
        // 第二次调用空 token 在发请求前就短路)。这里返回真实 geeTest:true 响应。
        var (baseUrl, listener, bodies) = StartServer(new Dictionary<string, string>
        {
            ["/aki/roleBox/akiBox/refreshData"] =
                """{"code":200,"msg":"","data":null,"success":true,"geeTest":true}""",
        });
        try
        {
            var client = CreateApi(baseUrl);
            var ok = await client.RefreshDataAsync("at-1", DeviceId, RoleId, "server-abc");
            Assert.False(ok);
            Assert.Single(bodies); // 确实发出了请求并命中 geeTest 分支
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task RefreshDataAsync_Skips_Request_When_ServerId_Missing()
    {
        // 评审反馈:serverId 缺失不应发出"注定被拒"的请求
        var (baseUrl, listener, bodies) = StartServer(new Dictionary<string, string>
        {
            ["/aki/roleBox/akiBox/refreshData"] =
                """{"code":200,"msg":"","data":true,"success":true}""",
        });
        try
        {
            var client = CreateApi(baseUrl);
            Assert.False(await client.RefreshDataAsync("at-1", DeviceId, RoleId, ""));
            Assert.Empty(bodies);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task RefreshDataAsync_Rethrows_On_Cancellation()
    {
        // 评审反馈:取消语义不得被"失败不阻断"的 catch-all 吞掉
        var (baseUrl, listener, _) = StartServer(new Dictionary<string, string>
        {
            ["/aki/roleBox/akiBox/refreshData"] =
                """{"code":200,"msg":"","data":true,"success":true}""",
        });
        try
        {
            var client = CreateApi(baseUrl);
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => client.RefreshDataAsync("at-1", DeviceId, RoleId, "server-abc", ct: cts.Token));
        }
        finally
        {
            listener.Stop();
        }
    }

    // ---------------- 全息战略(challengeIndex / challengeDetails) ----------------

    [Fact]
    public async Task GetChallengeIndexAsync_Parses_Regions_And_Bosses()
    {
        var (baseUrl, listener, bodies) = StartServer(new Dictionary<string, string>
        {
            ["/aki/roleBox/akiBox/challengeIndex"] =
                """
                {"code":200,"msg":"请求成功","data":"{\"isUnlock\":true,\"open\":true,\"wikiUrl\":\"https://wiki.kurobbs.com/mc/home\",\"challengeList\":[{\"sort\":130,\"country\":{\"countryId\":100004,\"countryName\":\"演武\",\"homePageImage\":\"https://img/region.png\",\"homePageIcon\":\"https://img/dir/\"},\"indexList\":[{\"bossId\":31010,\"bossName\":\"达妮娅\",\"bossLevel\":60,\"difficulty\":5,\"bossHeadIcon\":\"https://img/h.png\",\"bossIconUrl\":\"https://img/p.png\",\"contryId\":100004,\"sort\":109}]}]}","success":true}
                """,
        });
        try
        {
            var client = CreateApi(baseUrl);
            var index = await client.GetChallengeIndexAsync("at-1", DeviceId, RoleId);

            Assert.NotNull(index);
            Assert.True(index!.IsUnlock);
            Assert.Equal("https://wiki.kurobbs.com/mc/home", index.WikiUrl);
            var group = Assert.Single(index.ChallengeList!);
            Assert.Equal("演武", group.Country!.CountryName);
            var boss = Assert.Single(group.IndexList!);
            Assert.Equal(31010, boss.BossId);
            Assert.Equal(5, boss.ClearedDifficulty);  // 索引里是"已通关最高难度"
            Assert.Equal(100004, boss.CountryId);     // 服务端拼写 contryId

            var body = Assert.Single(bodies);
            Assert.Contains("roleId=" + RoleId, body);
            Assert.Contains("serverId=" + KujiequApiClient.ParamServerId, body);
            Assert.Contains("gameId=" + KujiequApiClient.ParamGameId, body);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task GetChallengeDetailsAsync_Parses_Dictionary_Keyed_By_BossId()
    {
        var (baseUrl, listener, bodies) = StartServer(new Dictionary<string, string>
        {
            ["/aki/roleBox/akiBox/challengeDetails"] =
                """
                {"code":200,"msg":"请求成功","data":"{\"isUnlock\":true,\"open\":true,\"challengeInfo\":{\"31010\":[{\"bossHeadIcon\":\"h1\",\"bossIconUrl\":\"p1\",\"bossLevel\":60,\"bossName\":\"达妮娅\",\"challengeId\":3101,\"difficulty\":1,\"passTime\":10,\"roles\":[{\"natureId\":1,\"roleName\":\"绯雪\",\"roleLevel\":90,\"roleHeadIcon\":\"r1\"}]},{\"bossHeadIcon\":\"h6\",\"bossIconUrl\":\"p6\",\"bossLevel\":100,\"bossName\":\"达妮娅\",\"challengeId\":3106,\"difficulty\":6,\"passTime\":0}]}}","success":true}
                """,
        });
        try
        {
            var client = CreateApi(baseUrl);
            var details = await client.GetChallengeDetailsAsync("at-1", DeviceId, RoleId);

            Assert.NotNull(details);
            // 字典 key 是字符串 bossId(不是数组下标)
            var tiers = Assert.Single(details!.ChallengeInfo!).Value;
            Assert.Equal(2, tiers.Count);
            var cleared = tiers.Single(t => t.Difficulty == 1);
            Assert.Equal(10, cleared.PassTime);
            Assert.Equal("绯雪", Assert.Single(cleared.Roles!).RoleName);
            // 未通关的记录:roles 键整体缺失 → null
            Assert.Null(tiers.Single(t => t.Difficulty == 6).Roles);

            var body = Assert.Single(bodies);
            Assert.Contains("channelId=19", body);
            Assert.Contains("countryCode=1", body);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task GetChallengeDetailsAsync_String_Null_Data_Returns_Null()
    {
        // 实测:roleId/serverId 无效时服务端返回 code=200 且 data 是字符串 "null"
        var (baseUrl, listener, _) = StartServer(new Dictionary<string, string>
        {
            ["/aki/roleBox/akiBox/challengeDetails"] =
                """{"code":200,"msg":"请求成功","data":"null","success":true}""",
        });
        try
        {
            var client = CreateApi(baseUrl);
            Assert.Null(await client.GetChallengeDetailsAsync("at-1", DeviceId, RoleId));
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task GetChallengeIndexAsync_Empty_List_Has_Unlock_Keys_Missing()
    {
        // 实测:roleId 不存在时 200 + challengeList:[] 且 isUnlock/open 键缺失
        var (baseUrl, listener, _) = StartServer(new Dictionary<string, string>
        {
            ["/aki/roleBox/akiBox/challengeIndex"] =
                """{"code":200,"msg":"请求成功","data":"{\"challengeList\":[],\"wikiUrl\":\"https://wiki.kurobbs.com/mc/home\"}","success":true}""",
        });
        try
        {
            var client = CreateApi(baseUrl);
            var index = await client.GetChallengeIndexAsync("at-1", DeviceId, RoleId);
            Assert.NotNull(index);
            Assert.Null(index!.IsUnlock);   // 键缺失 → null,不能当作"未解锁"以外的结论
            Assert.Null(index.Open);
            Assert.Empty(index.ChallengeList!);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task ChallengeEndpoints_Accept_Soft_Success_Code_10902()
    {
        // 深塔/矩阵/海墟允许 10902(软状态)携带可解析 data;全息战略沿用同一约定
        var (baseUrl, listener, _) = StartServer(new Dictionary<string, string>
        {
            ["/aki/roleBox/akiBox/challengeIndex"] =
                """{"code":10902,"msg":"本期无记录","data":"{\"isUnlock\":true,\"challengeList\":[]}","success":true}""",
        });
        try
        {
            var client = CreateApi(baseUrl);
            var index = await client.GetChallengeIndexAsync("at-1", DeviceId, RoleId);
            Assert.NotNull(index);
            Assert.True(index!.IsUnlock);
        }
        finally
        {
            listener.Stop();
        }
    }
}
