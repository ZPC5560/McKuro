using System.Text.Json;
using McKuro.Core.Infrastructure;
using McKuro.Core.Models.Roles;
using McKuro.Core.Services.Roles;
using Microsoft.Extensions.Logging.Abstractions;

namespace McKuro.Tests;

/// <summary>
/// 角色数据完整性 + 缓存回退测试:
/// 1) 详情被极验风控时(只有基础列表)不得覆盖完整缓存;
/// 2) LoadFromCache 在当前账号行缺失/不完整时回退旧版空账号键的完整缓存。
/// </summary>
public class RoleCacheFallbackTests : IDisposable
{
    private readonly string _tmpDir;

    public RoleCacheFallbackTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), "McKuro_rcf_" + Guid.NewGuid().ToString("N"));
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

    private static RoleDetail CompleteRole(string name, int cardId = 1) => new()
    {
        Role = new RoleInfo { RoleName = name, RoleId = cardId, StarLevel = 5 },
        WeaponData = new WeaponData { Weapon = new WeaponInfo { WeaponName = "晨光" } },
        Skills = [new SkillInfo { SkillLevel = 1, Skill = new SkillBase { SkillName = "剑心" } }],
        Attributes = [new RoleAttribute { AttributeName = "攻击", AttributeValue = "123" }],
    };

    private static RoleDetail BaseOnlyRole(string name, int cardId = 1) => new()
    {
        Role = new RoleInfo { RoleName = name, RoleId = cardId, StarLevel = 5 },
    };

    private static List<RoleDetail> SerializeRoundTrip(List<RoleDetail> roles)
        => JsonSerializer.Deserialize(
            JsonSerializer.Serialize(roles, RoleJsonContext.Default.ListRoleDetail),
            RoleJsonContext.Default.ListRoleDetail) ?? [];

    private static RoleDataService CreateService(AppDatabase db) => new(
        api: null!,
        localReader: null!,
        db: db,
        kuro: null!,
        accounts: null!,
        logger: NullLogger<RoleDataService>.Instance);

    private static void Insert(AppDatabase db, string accountId, string playerId, List<RoleDetail> roles)
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IsDetailComplete_True_Only_When_All_Sections_Present(bool complete)
    {
        var role = complete
            ? CompleteRole("秧秧")
            : new RoleDetail
            {
                Role = new RoleInfo { RoleName = "秧秧" },
                WeaponData = new WeaponData { Weapon = new WeaponInfo { WeaponName = "晨光" } },
                Skills = [new SkillInfo { SkillLevel = 1 }],
            };
        Assert.Equal(complete, role.IsDetailComplete);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void MergeMissingSections_Fills_Cached_Detail_Into_Fresh_List_Item(bool cachedComplete, bool freshComplete)
    {
        var fresh = new RoleDetail { Role = new RoleInfo { RoleId = 1304, RoleName = "秧秧", Level = 90 } };
        if (freshComplete)
        {
            fresh.WeaponData = new WeaponData { Weapon = new WeaponInfo { WeaponName = "晨光" } };
            fresh.Skills = [new SkillInfo { SkillLevel = 1, Skill = new SkillBase { SkillName = "剑心" } }];
            fresh.Attributes = [new RoleAttribute { AttributeName = "攻击", AttributeValue = "123" }];
        }
        var cached = cachedComplete ? CompleteRole("秧秧") : BaseOnlyRole("秧秧");

        RoleDataService.MergeMissingSections(fresh, cached);

        // 列表项保留基础信息(等级以列表为准),缺失的详情区块由缓存补全
        Assert.Equal(90, fresh.Role!.Level);
        Assert.Equal(cachedComplete || freshComplete, fresh.IsDetailComplete);
    }

    [Fact]
    public void LoadFromCache_Prefers_Account_Row_When_Complete()
    {
        using var db = new AppDatabase(_tmpDir);
        Insert(db, "account-a", "player-1", [CompleteRole("秧秧-新"), CompleteRole("凌阳")]);
        Insert(db, "", "player-1", [CompleteRole("秧秧-旧")]);

        var result = CreateService(db).LoadFromCache("account-a", "player-1");
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Roles.Count);
        Assert.Equal("秧秧-新", result.Roles[0].RoleName);
        Assert.Equal(RoleDataSource.Local, result.Source);
    }

    [Fact]
    public void LoadFromCache_Fills_Detail_From_Legacy_Row_Keeping_Account_Role_Set()
    {
        using var db = new AppDatabase(_tmpDir);
        // 当前账号行:列表同步写入,角色集合最新但大多只有基础信息(详情按点击补)
        Insert(db, "account-a", "player-1", [BaseOnlyRole("秧秧")]);
        // 旧版空账号键:上次完整同步(含详情)
        Insert(db, "", "player-1", [CompleteRole("秧秧"), CompleteRole("凌阳")]);

        var result = CreateService(db).LoadFromCache("account-a", "player-1");

        // 角色集合以当前账号行为准(它的角色列表最新),旧缓存只用来**补详情**:
        // 不能因为"整行不完整"就把账号行换成旧的空账号键缓存 —— 那会把刚同步到的
        // 整份角色列表退回旧版那几条,表现为"重开后角色又变少了"。
        Assert.True(result.IsSuccess);
        var role = Assert.Single(result.Roles);
        Assert.Equal("秧秧", role.RoleName);
        Assert.True(role.IsDetailComplete); // 详情由旧版缓存补全
        Assert.Contains("旧版", result.Message ?? "");
    }

    [Fact]
    public void LoadFromCache_Keeps_Account_Role_Set_Even_When_Legacy_Has_More_Roles()
    {
        using var db = new AppDatabase(_tmpDir);
        // 账号行 3 个角色(列表同步结果),旧版缓存 2 个但都带详情。
        // 注意各角色需用不同 cardRoleId(按 cardRoleId 匹配补详情)。
        Insert(db, "account-a", "player-1",
            [BaseOnlyRole("秧秧", 1001), BaseOnlyRole("凌阳", 1002), BaseOnlyRole("安可", 1003)]);
        Insert(db, "", "player-1", [CompleteRole("秧秧", 1001), CompleteRole("凌阳", 1002)]);

        var result = CreateService(db).LoadFromCache("account-a", "player-1");

        // 角色数量以账号行为准(3),不因旧缓存只有 2 个而被截断
        Assert.Equal(3, result.Roles.Count);
        Assert.Equal(2, result.Roles.Count(r => r.IsDetailComplete));
    }

    [Fact]
    public void LoadFromCache_FallsBack_To_Legacy_Row_When_Account_Row_Missing()
    {
        using var db = new AppDatabase(_tmpDir);
        Insert(db, "", "player-1", [CompleteRole("秧秧")]);

        var result = CreateService(db).LoadFromCache("account-a", "player-1");
        Assert.True(result.IsSuccess);
        Assert.Single(result.Roles);
        Assert.True(result.Roles[0].IsDetailComplete);
    }

    [Fact]
    public void LoadFromCache_Keeps_Incomplete_Account_Row_When_No_Complete_Legacy()
    {
        using var db = new AppDatabase(_tmpDir);
        Insert(db, "account-a", "player-1", [BaseOnlyRole("秧秧")]);

        var result = CreateService(db).LoadFromCache("account-a", "player-1");
        // 没有更完整的缓存时,保留基础列表(页面仍可展示角色卡片)
        Assert.True(result.IsSuccess);
        Assert.Single(result.Roles);
        Assert.False(result.Roles[0].IsDetailComplete);
    }

    [Fact]
    public void SerializeRoundTrip_Keeps_Detail_Completeness()
    {
        // 验证序列化往返后 IsDetailComplete 不变(缓存写入/读出依赖此性质)
        var roles = SerializeRoundTrip([CompleteRole("秧秧"), BaseOnlyRole("凌阳")]);
        Assert.True(roles[0].IsDetailComplete);
        Assert.False(roles[1].IsDetailComplete);
    }
}
