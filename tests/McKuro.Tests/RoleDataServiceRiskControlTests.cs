using System.Net;
using System.Text;
using System.Text.Json;
using McKuro.Core.Infrastructure;
using McKuro.Core.Models.Kuro;
using McKuro.Core.Models.Roles;
using McKuro.Core.Services.Kuro;
using McKuro.Core.Services.Roles;
using McKuro.Core.Services.Settings;
using Microsoft.Extensions.Logging.Abstractions;

namespace McKuro.Tests;

/// <summary>
/// 角色数据「列表/详情分离」链路测试(2026-08 优化):
/// 1) 页面加载/同步(LoadRoleListAsync)只查角色列表,不请求任何 getRoleDetail;
/// 2) 列表项合并本地缓存已同步过的详情(加载后详情区不空白);
/// 3) 点击角色按需拉详情(LoadRoleDetailAsync)单发 getRoleDetail;触发极验风控时报 GeeTest、
///    不弹验证页、不上行验证票据,且不得覆盖已有完整缓存;
/// 4) 详情成功后按 cardRoleId 回写缓存。
/// </summary>
public class RoleDataServiceRiskControlTests : IDisposable
{
    private const string Token = "test-token";
    private const string RoleId = "103242935";
    private const string UserId = "u-1";

    private static readonly string DetailOkBody =
        """{"code":200,"data":"{\"role\":{\"roleId\":1304,\"roleName\":\"秧秧\"},\"level\":90,\"chainList\":[{\"order\":1,\"unlocked\":true}],\"weaponData\":{\"weapon\":{\"weaponName\":\"晨光\"}},\"skillList\":[{\"level\":1,\"skill\":{\"name\":\"剑心\"}}],\"roleAttributeList\":[{\"attributeName\":\"攻击\",\"attributeValue\":\"123\"}]}","msg":"","success":true}""";

    private readonly string _tmpDir;

    public RoleDataServiceRiskControlTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "McKuro_geet_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmpDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tmpDir, recursive: true);
        }
        catch (Exception)
        {
            // 忽略
        }
    }

    /// <summary>按路径返回脚本化响应的库街区模拟(首个 getRoleDetail 按配置触发极验风控)。</summary>
    private sealed class MockKuroHandler : HttpMessageHandler
    {
        public int DetailCalls { get; private set; }

        /// <summary>已接收的请求 (路径, body),用于断言未上行验证票据/未批量拉详情。</summary>
        public readonly List<(string Path, string Body)> Requests = new();

        /// <summary>首个 getRoleDetail 是否触发极验风控(其余调用返回正常详情)。</summary>
        public bool FirstDetailGeeTest { get; init; } = true;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            lock (Requests)
            {
                Requests.Add((path, body));
            }
            var responseBody = path switch
            {
                "/gamer/role/list" =>
                    """{"code":200,"success":true,"data":[{"roleId":"103242935","userId":"u-1","gameId":2,"serverId":"s-1","roleName":"秧秧"}]}""",
                "/aki/roleBox/requestToken" =>
                    """{"code":200,"data":"{\"accessToken\":\"at-abc\"}","msg":"成功","success":true}""",
                "/aki/roleBox/akiBox/roleData" =>
                    """{"code":200,"data":"{\"roleList\":[{\"roleId\":1304,\"roleName\":\"秧秧\",\"level\":90,\"breach\":6,\"chainUnlockNum\":6,\"starLevel\":5,\"attributeId\":1,\"attributeName\":\"气动\",\"weaponTypeId\":2,\"weaponTypeName\":\"迅刀\"}]}","msg":"","success":true}""",
                "/aki/roleBox/akiBox/getRoleDetail" => NextDetail(),
                _ => """{"code":404}""",
            };
            var resp = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            };
            return await Task.FromResult(resp);
        }

        private string NextDetail()
        {
            DetailCalls++;
            return DetailCalls == 1 && FirstDetailGeeTest
                ? """{"geeTest":true}"""
                : DetailOkBody;
        }
    }

    private (RoleDataService Service, MockKuroHandler Handler, AppDatabase Db) CreateService(bool firstDetailGeeTest = true)
    {
        var handler = new MockKuroHandler { FirstDetailGeeTest = firstDetailGeeTest };
        var http = new HttpClient(handler);
        var api = new KujiequApiClient(http, baseUrl: "http://127.0.0.1:1");
        var kuro = new KuroClient(http);
        var settings = new SettingsService(_tmpDir, NullLogger<SettingsService>.Instance);
        var accounts = new KuroAccountService(settings);
        accounts.AddOrUpdate(new KuroAccount { UserId = UserId, Token = Token, DeviceId = "test-device" });
        var db = new AppDatabase(_tmpDir);
        var service = new RoleDataService(
            api,
            localReader: null!,
            db,
            kuro,
            accounts,
            NullLogger<RoleDataService>.Instance);
        return (service, handler, db);
    }

    private static RoleDetail CompleteRole(int cardId, string name) => new()
    {
        Role = new RoleInfo { RoleId = cardId, RoleName = name, StarLevel = 5 },
        WeaponData = new WeaponData { Weapon = new WeaponInfo { WeaponName = "晨光" } },
        Skills = [new SkillInfo { SkillLevel = 1, Skill = new SkillBase { SkillName = "剑心" } }],
        Attributes = [new RoleAttribute { AttributeName = "攻击", AttributeValue = "123" }],
    };

    private static void InsertCache(AppDatabase db, string accountId, string playerId, List<RoleDetail> roles)
    {
        var json = JsonSerializer.Serialize(roles, RoleJsonContext.Default.ListRoleDetail);
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO role_cache (account_id, player_id, json, update_time)
            VALUES ($account, $player, $json, '2026-01-01')
            """;
        cmd.Parameters.AddWithValue("$account", accountId);
        cmd.Parameters.AddWithValue("$player", playerId);
        cmd.Parameters.AddWithValue("$json", json);
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public async Task ListSync_Does_Not_Request_Any_RoleDetail()
    {
        var (service, handler, db) = CreateService();
        using (db)
        {
            var result = await service.LoadRoleListAsync(Token, RoleId);

            Assert.True(result.IsSuccess);
            Assert.Equal(RoleDataSource.Kujiequ, result.Source);
            var role = Assert.Single(result.Roles);
            Assert.False(role.IsDetailComplete); // 页面加载只有基础列表
            Assert.Equal(0, handler.DetailCalls); // 不请求任何 getRoleDetail
            Assert.DoesNotContain(handler.Requests, r => r.Path == "/aki/roleBox/akiBox/getRoleDetail");
        }
    }

    [Fact]
    public async Task ListSync_Merges_Cached_Detail_So_Detail_Panel_Not_Blank()
    {
        var (service, handler, db) = CreateService();
        using (db)
        {
            InsertCache(db, UserId, RoleId, [CompleteRole(1304, "秧秧")]);

            var result = await service.LoadRoleListAsync(Token, RoleId);

            Assert.True(result.IsSuccess);
            var role = Assert.Single(result.Roles);
            Assert.True(role.IsDetailComplete); // 上次同步的详情合并进新列表 → 详情区不空白
            Assert.Equal(0, handler.DetailCalls);
        }
    }

    [Fact]
    public async Task Detail_GeeTest_Returns_Flag_Without_Verifier_And_Keeps_Cache()
    {
        var (service, handler, db) = CreateService();
        using (db)
        {
            InsertCache(db, UserId, RoleId, [CompleteRole(1304, "秧秧")]);

            var result = await service.LoadRoleDetailAsync(Token, RoleId, 1304);

            Assert.True(result.GeeTest);
            Assert.Null(result.Detail);
            Assert.Equal(1, handler.DetailCalls); // 单发一次,不重试
            Assert.All(
                handler.Requests.Where(r => r.Path == "/aki/roleBox/akiBox/getRoleDetail"),
                r => Assert.DoesNotContain("geeTestData=", r.Body));
            // 风控不覆盖已有完整缓存
            Assert.True(Assert.Single(service.LoadFromCache(UserId, RoleId).Roles).IsDetailComplete);
        }
    }

    [Fact]
    public async Task Detail_After_ListSync_Reuses_AccessToken()
    {
        var (service, handler, _) = CreateService(firstDetailGeeTest: false);
        await service.LoadRoleListAsync(Token, RoleId);

        var result = await service.LoadRoleDetailAsync(Token, RoleId, 1304);

        Assert.NotNull(result.Detail);
        Assert.False(result.GeeTest);
        Assert.True(result.Detail!.IsDetailComplete);
        // 列表同步已换取令牌 → 详情按需加载不重复 getGamer/requestToken,只单发一次 getRoleDetail
        Assert.Equal(1, handler.Requests.Count(r => r.Path == "/aki/roleBox/requestToken"));
        Assert.Equal(1, handler.DetailCalls);
    }

    [Fact]
    public async Task Detail_Success_Writes_Cache_By_CardId()
    {
        var (service, handler, db) = CreateService(firstDetailGeeTest: false);
        using (db)
        {
            // 无缓存时点击角色:详情成功后按 cardRoleId 写入缓存
            var result = await service.LoadRoleDetailAsync(Token, RoleId, 1304);

            Assert.NotNull(result.Detail);
            var cached = service.LoadFromCache(UserId, RoleId);
            Assert.True(cached.IsSuccess);
            var cachedRole = Assert.Single(cached.Roles);
            Assert.True(cachedRole.IsDetailComplete);
            Assert.Equal("晨光", cachedRole.WeaponData?.Weapon?.WeaponName);
        }
    }

    [Fact]
    public async Task Detail_Without_Configured_Inputs_Returns_Empty()
    {
        var (service, handler, _) = CreateService();
        var result = await service.LoadRoleDetailAsync("", RoleId, 1304);

        Assert.Null(result.Detail);
        Assert.False(result.GeeTest);
        Assert.Equal(0, handler.DetailCalls);
    }
}
