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
/// 角色列表落盘测试(修复「同步拿到的新角色重开就没了」):
/// <para>
/// 旧实现只在**单角色详情**拉取成功时回写缓存,列表同步本身不落盘,
/// 于是同步到的整份角色列表关掉页面即丢失、重开退回上次缓存里的那几个
/// (实机复现:缓存 3 个 vs 接口 38 个)。
/// </para>
/// <para>
/// 修复后列表同步会把合并结果整体写回,同时**不得**用只有基础信息的列表项
/// 覆盖缓存里的完整详情,重复同步也不得堆积重复行。
/// </para>
/// </summary>
public class RoleListPersistenceTests : IDisposable
{
    private const string Token = "test-token";
    private const string RoleId = "103242935";
    private const string UserId = "u-1";

    private readonly string _tmpDir;

    public RoleListPersistenceTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "McKuro_rlp_" + Guid.NewGuid().ToString("N"));
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

    /// <summary>返回 38 个角色的列表接口模拟(仅第 2 个角色在缓存中有完整详情)。</summary>
    private sealed class MockRoleListHandler(int roleCount) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = path switch
            {
                "/gamer/role/list" =>
                    """{"code":200,"success":true,"data":[{"roleId":"103242935","userId":"u-1","gameId":2,"serverId":"s-1","roleName":"秧秧"}]}""",
                "/aki/roleBox/requestToken" =>
                    """{"code":200,"data":"{\"accessToken\":\"at-abc\"}","msg":"成功","success":true}""",
                "/aki/roleBox/akiBox/roleData" => BuildRoleList(),
                _ => """{"code":404}""",
            };
            await Task.CompletedTask;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        }

        private string BuildRoleList()
        {
            var items = Enumerable.Range(1, roleCount).Select(i =>
                $$"""{"roleId":{{1000 + i}},"roleName":"角色{{i}}","level":90,"breach":6,"chainUnlockNum":6,"starLevel":5,"attributeId":1,"attributeName":"气动","weaponTypeId":2,"weaponTypeName":"迅刀"}""");
            var inner = $$"""{"roleList":[{{string.Join(",", items)}}]}""";
            var escaped = JsonSerializer.Serialize(inner);
            return $$"""{"code":200,"data":{{escaped}},"msg":"","success":true}""";
        }
    }

    private (RoleDataService Service, AppDatabase Db) CreateService(int roleCount = 38)
    {
        var http = new HttpClient(new MockRoleListHandler(roleCount));
        var api = new KujiequApiClient(http, baseUrl: "http://127.0.0.1:1");
        var kuro = new KuroClient(http);
        var settings = new SettingsService(_tmpDir, NullLogger<SettingsService>.Instance);
        var accounts = new KuroAccountService(settings);
        accounts.AddOrUpdate(new KuroAccount { UserId = UserId, Token = Token, DeviceId = "test-device" });
        var db = new AppDatabase(_tmpDir);
        var service = new RoleDataService(
            api, localReader: null!, db, kuro, accounts, NullLogger<RoleDataService>.Instance);
        return (service, db);
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
    public async Task ListSync_Persists_Full_List_So_Reopen_Keeps_New_Roles()
    {
        var (service, db) = CreateService();
        using (db)
        {
            // 缓存里只有 3 个(旧实现的表现:详情拉过的才有,列表同步不落盘)
            InsertCache(db, UserId, RoleId,
            [
                CompleteRole(1001, "角色1"),
                CompleteRole(1002, "角色2"),
                CompleteRole(1003, "角色3"),
            ]);

            var list = await service.LoadRoleListAsync(Token, RoleId);
            Assert.True(list.IsSuccess);
            Assert.Equal(38, list.Roles.Count);

            // 关键:模拟"重开页面"再读缓存,必须是同步后的 38 个而不是退回 3 个
            var cached = service.LoadFromCache(UserId, RoleId);
            Assert.True(cached.IsSuccess);
            Assert.Equal(38, cached.Roles.Count);
        }
    }

    [Fact]
    public async Task ListSync_Preserves_Cached_Detail_For_Existing_Roles()
    {
        var (service, db) = CreateService();
        using (db)
        {
            InsertCache(db, UserId, RoleId, [CompleteRole(1001, "角色1")]);

            await service.LoadRoleListAsync(Token, RoleId);

            var cached = service.LoadFromCache(UserId, RoleId);
            var role1 = cached.Roles.Single(r => r.Role?.RoleId == 1001);
            Assert.True(role1.IsDetailComplete);          // 详情未被基础列表覆盖
            Assert.Equal("晨光", role1.WeaponData?.Weapon?.WeaponName);
            // 其余角色只有基础信息,详情留给点击时按需拉取
            Assert.False(cached.Roles.Single(r => r.Role?.RoleId == 1005).IsDetailComplete);
        }
    }

    [Fact]
    public async Task Repeated_ListSync_Does_Not_Duplicate_Roles()
    {
        var (service, db) = CreateService();
        using (db)
        {
            await service.LoadRoleListAsync(Token, RoleId);
            await service.LoadRoleListAsync(Token, RoleId);
            await service.LoadRoleListAsync(Token, RoleId);

            var cached = service.LoadFromCache(UserId, RoleId);
            Assert.Equal(38, cached.Roles.Count);
            Assert.Equal(38, cached.Roles.Select(r => r.Role!.RoleId).Distinct().Count());
        }
    }

    [Fact]
    public async Task ListSync_Drops_Roles_That_The_Api_No_Longer_Returns()
    {
        var (service, db) = CreateService();
        using (db)
        {
            // 实机场景:漂泊者条目从 1406 换成 1309,旧条目残留在缓存里(两者同名 → 页面出现两个「漂泊者」)。
            // 角色集合必须以接口新列表为准,缓存只负责补详情,不能把已消失的条目一直留着。
            InsertCache(db, UserId, RoleId, [CompleteRole(9999, "已被替换的角色"), CompleteRole(1001, "角色1")]);

            await service.LoadRoleListAsync(Token, RoleId);

            var cached = service.LoadFromCache(UserId, RoleId);
            Assert.Equal(38, cached.Roles.Count);
            Assert.DoesNotContain(cached.Roles, r => r.Role?.RoleId == 9999);
            // 仍命中的角色详情不被清空
            Assert.True(cached.Roles.Single(r => r.Role?.RoleId == 1001).IsDetailComplete);
        }
    }

    [Fact]
    public void LoadFromCache_Falls_Back_To_Player_Row_When_Account_Key_Drifted()
    {
        var (service, db) = CreateService();
        using (db)
        {
            InsertCache(db, UserId, RoleId, [CompleteRole(1001, "角色1"), CompleteRole(1002, "角色2")]);

            // token 失效后签到页会自动移除账号 → CurrentKuroUserId 变空 → 角色页传空账号键。
            // 此时仍应读到该 playerId 的缓存,否则页面报「无缓存」并停在旧列表上
            // (用户看到的现象:「点了同步还是之前的缓存」)。
            var cached = service.LoadFromCache("", RoleId);

            Assert.True(cached.IsSuccess);
            Assert.Equal(2, cached.Roles.Count);
        }
    }

    [Fact]
    public void LoadFromCache_Prefers_Exact_Account_Row_Over_Player_Fallback()
    {
        var (service, db) = CreateService();
        using (db)
        {
            InsertCache(db, UserId, RoleId, [CompleteRole(1001, "新账号行")]);
            InsertCache(db, "other-account", RoleId, [CompleteRole(2001, "别人的行")]);

            var cached = service.LoadFromCache(UserId, RoleId);

            Assert.Single(cached.Roles);
            Assert.Equal("新账号行", cached.Roles[0].RoleName);
        }
    }

    [Fact]
    public async Task Empty_List_Does_Not_Wipe_Cache()
    {
        var (service, db) = CreateService(roleCount: 0);
        using (db)
        {
            InsertCache(db, UserId, RoleId, [CompleteRole(1001, "角色1"), CompleteRole(1002, "角色2")]);

            await service.LoadRoleListAsync(Token, RoleId);

            // 接口返回空列表不可覆盖已有缓存(避免一次异常把用户数据清空)
            var cached = service.LoadFromCache(UserId, RoleId);
            Assert.Equal(2, cached.Roles.Count);
            Assert.All(cached.Roles, r => Assert.True(r.IsDetailComplete));
        }
    }

    [Fact]
    public void LoadFromCache_Keeps_List_Size_And_Fills_Detail_From_Legacy_Row()
    {
        var (service, db) = CreateService();
        using (db)
        {
            // 当前账号行:38 个角色,只有 1 个带详情(列表同步写入的常态)
            var accountRoles = Enumerable.Range(1, 38)
                .Select(i => new RoleDetail { Role = new RoleInfo { RoleId = 1000 + i, RoleName = $"角色{i}" } })
                .ToList();
            accountRoles[0] = CompleteRole(1001, "角色1");
            InsertCache(db, UserId, RoleId, accountRoles);

            // 旧版空账号键:只有 2 个但都带完整详情
            InsertCache(db, "", RoleId, [CompleteRole(1001, "角色1-旧"), CompleteRole(1002, "角色2-旧")]);

            var cached = service.LoadFromCache(UserId, RoleId);

            // 角色集合以当前账号行为准(38),不被旧的 2 个换掉;仅用旧行补详情
            Assert.Equal(38, cached.Roles.Count);
            Assert.True(cached.Roles.Single(r => r.Role?.RoleId == 1001).IsDetailComplete);
            Assert.True(cached.Roles.Single(r => r.Role?.RoleId == 1002).IsDetailComplete);
            Assert.False(cached.Roles.Single(r => r.Role?.RoleId == 1005).IsDetailComplete);
            Assert.Contains("旧版", cached.Message ?? "");
        }
    }

    [Fact]
    public void LoadFromCache_Reports_Plain_Cache_When_No_Legacy_Row()
    {
        var (service, db) = CreateService();
        using (db)
        {
            InsertCache(db, UserId, RoleId, [CompleteRole(1001, "角色1")]);

            var cached = service.LoadFromCache(UserId, RoleId);

            Assert.True(cached.IsSuccess);
            Assert.Single(cached.Roles);
            Assert.Equal(RoleDataSource.Local, cached.Source);
        }
    }
}
