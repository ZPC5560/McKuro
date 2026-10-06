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
    public void MergeMissingSections_Prefers_Echo_Block_With_Substat_Validity()
    {
        // 回归(用户反馈「部分角色看不到库街区有效词条:相里要/折枝/漂泊者/安可等」):
        // EchoProp.Valid 于 2026-10-05 才加入,更早写盘的缓存整行没有 valid(全 null)。
        // 早期合并是 `target.PhantomData ??= source.PhantomData` —— 只要 target 非 null 就沿用,
        // 于是每轮同步都把无 valid 的旧区块粘滞写回缓存,永不自愈。
        // 实测库街区实时响应里 valid 一直是完整的(null=0),所以缺的不是数据源,是覆盖策略。
        var stale = new RoleDetail
        {
            Role = new RoleInfo { RoleId = 1305, RoleName = "相里要" },
            PhantomData = EchoBlock(hasValidity: false),
        };
        var fresh = new RoleDetail
        {
            Role = new RoleInfo { RoleId = 1305, RoleName = "相里要" },
            PhantomData = EchoBlock(hasValidity: true),
        };

        RoleDataService.MergeMissingSections(fresh, stale);

        // 带判定的区块必须覆盖无判定的陈旧区块
        Assert.True(fresh.PhantomData!.HasSubstatValidity);
    }

    [Fact]
    public void MergeMissingSections_Keeps_Existing_Echo_Block_When_Source_Lacks_Validity()
    {
        // 反向保护:不得用"无判定"的旧数据覆盖"有判定"的新数据(避免修 bug 时把好数据冲掉)
        var good = new RoleDetail
        {
            Role = new RoleInfo { RoleId = 1305, RoleName = "相里要" },
            PhantomData = EchoBlock(hasValidity: true),
        };
        var stale = new RoleDetail
        {
            Role = new RoleInfo { RoleId = 1305, RoleName = "相里要" },
            PhantomData = EchoBlock(hasValidity: false),
        };

        RoleDataService.MergeMissingSections(good, stale);

        Assert.True(good.PhantomData!.HasSubstatValidity);
    }

    [Fact]
    public void MergeMissingSections_Takes_Echo_Block_When_Target_Is_Empty()
    {
        // 目标没有声骸数据 → 正常用来源补全(保持既有行为)
        var target = new RoleDetail { Role = new RoleInfo { RoleId = 1, RoleName = "秧秧" } };
        var source = new RoleDetail
        {
            Role = new RoleInfo { RoleId = 1, RoleName = "秧秧" },
            PhantomData = EchoBlock(hasValidity: true),
        };

        RoleDataService.MergeMissingSections(target, source);

        Assert.NotNull(target.PhantomData);
        Assert.True(target.PhantomData!.HasSubstatValidity);
    }

    [Theory]
    [InlineData(null, false)]   // valid=null(陈旧快照/无判定)
    [InlineData(false, true)]   // valid=false 也算"有判定"(明确无效也是一种判定)
    [InlineData(true, true)]
    public void HasSubstatValidity_Distinguishes_Missing_Judgement_From_False(bool? valid, bool expected)
    {
        var echo = new EchoInfo
        {
            SubProps = [new EchoProp { AttributeName = "暴击", AttributeValue = "6.3%", Valid = valid }],
        };
        Assert.Equal(expected, echo.HasSubstatValidity);
        Assert.Equal(expected, new PhantomData { Phantoms = [echo] }.HasSubstatValidity);
    }

    /// <summary>构造一个含一件声骸的声骸区块;<paramref name="hasValidity"/> 决定是否带 valid 判定。</summary>
    private static PhantomData EchoBlock(bool hasValidity) => new()
    {
        Phantoms =
        [
            new EchoInfo
            {
                Cost = 4,
                Level = 25,
                PhantomProp = new PhantomPropInfo { PhantomName = "梦魇·云闪之鳞" },
                SubProps =
                [
                    new EchoProp { AttributeName = "暴击", AttributeValue = "6.3%", Valid = hasValidity ? true : null },
                ],
            },
        ],
    };

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
