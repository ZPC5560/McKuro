using System.Net;
using System.Text;
using McKuro.Core.Infrastructure;
using McKuro.Core.Models.Kuro;
using McKuro.Core.Services.Kuro;
using McKuro.Core.Services.Roles;
using McKuro.Core.Services.Settings;
using Microsoft.Extensions.Logging.Abstractions;

namespace McKuro.Tests;

/// <summary>
/// 角色同步「极验风控 → 不弹验证页 → 缓存回退 + 提示」链路测试:
/// getRoleDetail 返回 {"geeTest":true} 时,服务不再调用极验验证器(角色场景验证实测无法解除,
/// 登录场景票据上行仍被服务端拒绝),直接回退上次完整缓存并返回风控提示;且不得覆盖旧缓存。
/// (refreshData 接口已被库街区停用,同步链不再包含刷新缓存步骤。)
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

    /// <summary>按路径返回脚本化响应的库街区模拟(首个 getRoleDetail 触发极验风控,之后正常)。</summary>
    private sealed class MockKuroHandler : HttpMessageHandler
    {
        public int DetailCalls { get; private set; }

        /// <summary>已接收的请求 (路径, body),用于断言未上行验证票据。</summary>
        public readonly List<(string Path, string Body)> Requests = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
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
            return await Task.FromResult(resp).ConfigureAwait(false);
        }

        private string NextDetail()
        {
            DetailCalls++;
            return DetailCalls == 1
                ? """{"geeTest":true}"""
                : DetailOkBody;
        }
    }

    private (RoleDataService Service, MockKuroHandler Handler, AppDatabase Db) CreateService()
    {
        var handler = new MockKuroHandler();
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

    [Fact]
    public async Task GeeTest_No_Verifier_And_Falls_Back_To_Complete_Cache()
    {
        var (service, handler, db) = CreateService();
        using (db)
        {
            // 先写入上一次完整同步的缓存(风控时不得覆盖)
            var json = System.Text.Json.JsonSerializer.Serialize(
                new List<McKuro.Core.Models.Roles.RoleDetail>
                {
                    new McKuro.Core.Models.Roles.RoleDetail
                    {
                        Role = new McKuro.Core.Models.Roles.RoleInfo { RoleId = 1304, RoleName = "秧秧", StarLevel = 5 },
                        WeaponData = new McKuro.Core.Models.Roles.WeaponData { Weapon = new McKuro.Core.Models.Roles.WeaponInfo { WeaponName = "晨光" } },
                        Skills = [new McKuro.Core.Models.Roles.SkillInfo { SkillLevel = 1, Skill = new McKuro.Core.Models.Roles.SkillBase { SkillName = "剑心" } }],
                        Attributes = [new McKuro.Core.Models.Roles.RoleAttribute { AttributeName = "攻击", AttributeValue = "123" }],
                    },
                },
                McKuro.Core.Models.Roles.RoleJsonContext.Default.ListRoleDetail);
            using (var cmd = db.Connection.CreateCommand())
            {
                cmd.CommandText =
                    """
                    INSERT INTO role_cache (account_id, player_id, json, update_time)
                    VALUES ($account, $player, $json, '2026-01-01')
                    """;
                cmd.Parameters.AddWithValue("$account", UserId);
                cmd.Parameters.AddWithValue("$player", RoleId);
                cmd.Parameters.AddWithValue("$json", json);
                cmd.ExecuteNonQuery();
            }

            // 新签名不再接受验证器:触发风控后直接回退缓存
            var result = await service.LoadFromKujiequAsync(Token, RoleId).ConfigureAwait(false);

            Assert.True(result.IsSuccess);
            Assert.Contains("风控", result.Message ?? "");
            Assert.True(Assert.Single(result.Roles).IsDetailComplete); // 展示的是缓存完整数据
            // 详情接口只被调用一次(不重试),且没有上行任何验证票据
            Assert.Equal(1, handler.DetailCalls);
            Assert.All(
                handler.Requests.Where(r => r.Path == "/aki/roleBox/akiBox/getRoleDetail"),
                r => Assert.DoesNotContain("geeTestData=", r.Body));
        }
    }

    [Fact]
    public async Task GeeTest_Without_Existing_Cache_Returns_Basic_List_And_Hint()
    {
        var (service, handler, _) = CreateService();
        // 无缓存
        var result = await service.LoadFromKujiequAsync(Token, RoleId).ConfigureAwait(false);

        Assert.True(result.IsSuccess);
        Assert.Contains("风控", result.Message ?? "");
        // 无缓存时回退到 roleData 基础列表(非完整详情)
        var role = Assert.Single(result.Roles);
        Assert.False(role.IsDetailComplete);
        Assert.Equal(1, handler.DetailCalls);
    }
}
