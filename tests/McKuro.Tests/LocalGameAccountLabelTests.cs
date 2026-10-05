using McKuro.Core.Services.User;
using McKuro.ViewModels;

namespace McKuro.Tests;

/// <summary>
/// 「接口账号 → 游戏角色」显示口径的纯逻辑(账号页三张卡片 + 签到页共用):
/// 匹配优先级、多角色选取、格式化边界。不发网络请求、不依赖 DI 容器。
/// <para>回归背景:三个接口返回的都是<b>接口账号</b>(库街区 UID / U123…A / 手机号),
/// 用户要看的却是游戏角色昵称 + 游戏角色 UID(与首页账号卡片、抽卡页 player_id 同口径)。</para>
/// </summary>
public sealed class LocalGameAccountLabelTests
{
    private static LocalLauncherPlayer Player(
        string kuroUid = "526781653",
        string username = "U536781653A",
        string roleId = "103242935",
        string roleName = "以椿为鸣",
        string phone = "16605248023") =>
        new()
        {
            KuroUid = kuroUid,
            Username = username,
            RoleId = roleId,
            RoleName = roleName,
            ServerName = "China",
            Phone = phone,
        };

    // ---------- 格式化 ----------

    [Fact]
    public void Format_NameAndRoleId_UsesMiddleDotSeparator()
    {
        Assert.Equal("以椿为鸣 · 103242935", LocalGameAccountLabel.Format("以椿为鸣", "103242935"));
    }

    [Fact]
    public void Format_TrimsSurroundingWhitespace()
    {
        Assert.Equal("以椿为鸣 · 103242935", LocalGameAccountLabel.Format("  以椿为鸣  ", " 103242935 "));
    }

    [Fact]
    public void Format_OnlyRoleId_ReturnsRoleId()
    {
        Assert.Equal("103242935", LocalGameAccountLabel.Format("", "103242935"));
    }

    [Fact]
    public void Format_OnlyRoleName_ReturnsRoleName()
    {
        // 昵称有、ID 缺失时不能显示成 "以椿为鸣 · "
        Assert.Equal("以椿为鸣", LocalGameAccountLabel.Format("以椿为鸣", null));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("   ", "   ")]
    public void Format_NothingUsable_ReturnsEmpty(string? name, string? id)
    {
        // 不编造占位符:调用方靠空串判定"没匹配到",回退接口自己的文案
        Assert.Equal("", LocalGameAccountLabel.Format(name, id));
    }

    // ---------- 匹配优先级 ----------

    [Fact]
    public void Match_ByPhone()
    {
        var players = new List<LocalLauncherPlayer> { Player() };

        var hit = LocalGameAccountLabel.Match(players, phone: "16605248023");

        Assert.Same(players[0], hit);
    }

    [Fact]
    public void Match_ByUserName_WhenPhoneMissing()
    {
        // 云鸣潮旧数据可能没有手机号,但账号名(U…A)在
        var players = new List<LocalLauncherPlayer> { Player() };

        var hit = LocalGameAccountLabel.Match(players, userName: "U536781653A");

        Assert.Same(players[0], hit);
    }

    [Fact]
    public void Match_ByKuroUid_WhenPhoneAndNameMissing()
    {
        var players = new List<LocalLauncherPlayer> { Player() };

        var hit = LocalGameAccountLabel.Match(players, kuroUid: "526781653");

        Assert.Same(players[0], hit);
    }

    [Fact]
    public void Match_PhoneWinsOverOtherKeys()
    {
        // 手机号是三个接口唯一共有且稳定的标识:同一手机号命中时不应被其它键带偏
        var byPhone = Player(kuroUid: "1", username: "U1A", roleId: "111", roleName: "手机号命中");
        var byName = Player(kuroUid: "2", username: "U2A", roleId: "222", roleName: "账号名命中");

        var hit = LocalGameAccountLabel.Match([byPhone, byName], phone: "16605248023", userName: "U2A");

        Assert.Same(byPhone, hit);
    }

    [Fact]
    public void Match_UserNameCaseInsensitive()
    {
        var players = new List<LocalLauncherPlayer> { Player(username: "U536781653A") };

        Assert.Same(players[0], LocalGameAccountLabel.Match(players, userName: "u536781653a"));
    }

    [Fact]
    public void Match_NoOverlap_ReturnsNull()
    {
        // 匹配不到宁可不显示,也不能退而取第一个角色(会串号显示别人的昵称)
        var players = new List<LocalLauncherPlayer> { Player() };

        Assert.Null(LocalGameAccountLabel.Match(players, phone: "18800000000"));
    }

    [Fact]
    public void Match_NullOrEmptyLists_ReturnsNull()
    {
        Assert.Null(LocalGameAccountLabel.Match(null, phone: "16605248023"));
        Assert.Null(LocalGameAccountLabel.Match([], phone: "16605248023"));
    }

    [Fact]
    public void Match_AllKeysEmpty_ReturnsNull()
    {
        // 未登录/无手机号的接口账号不能随机匹配到本地任意角色
        var players = new List<LocalLauncherPlayer> { Player() };

        Assert.Null(LocalGameAccountLabel.Match(players, phone: "", userName: "  ", kuroUid: null));
    }

    // ---------- 同一凭证多角色 ----------

    [Fact]
    public void Match_MultipleRolesSameAccount_PrefersRequestedRoleId()
    {
        // 同一库街区账号可能有多个服务器角色:必须按当前在用的角色 UID 选中
        var china = Player(roleId: "103242935", roleName: "以椿为鸣");
        var global = Player(roleId: "900000001", roleName: "Global Role");

        var hit = LocalGameAccountLabel.Match([china, global], phone: "16605248023", preferredRoleId: "900000001");

        Assert.Same(global, hit);
    }

    [Fact]
    public void Match_MultipleRoles_UnknownPreferredRoleId_FallsBackToFirst()
    {
        var china = Player(roleId: "103242935", roleName: "以椿为鸣");
        var global = Player(roleId: "900000001", roleName: "Global Role");

        var hit = LocalGameAccountLabel.Match([china, global], phone: "16605248023", preferredRoleId: "999");

        Assert.Same(china, hit);
    }

    // ---------- Resolve 组合入口 ----------

    [Fact]
    public void Resolve_Matched_RendersNameAndRoleId()
    {
        var players = new List<LocalLauncherPlayer> { Player() };

        Assert.Equal("以椿为鸣 · 103242935", LocalGameAccountLabel.Resolve(players, phone: "16605248023"));
    }

    [Fact]
    public void Resolve_NotMatched_ReturnsNull()
    {
        var players = new List<LocalLauncherPlayer> { Player() };

        Assert.Null(LocalGameAccountLabel.Resolve(players, phone: "18800000000"));
    }

    [Fact]
    public void Resolve_MatchedButNoUsableText_ReturnsNull()
    {
        // 角色昵称与 UID 都拿不到时,不能返回 " · "(会渲染成一行空分隔符)
        var players = new List<LocalLauncherPlayer>
        {
            Player(roleId: "", roleName: ""),
        };

        Assert.Null(LocalGameAccountLabel.Resolve(players, phone: "16605248023"));
    }

    // ---------- 三个接口各自的调用形态(与 ViewModel 传参一致) ----------

    [Fact]
    public void Resolve_ThreeInterfaces_SamePhone_ShareSameGameRole()
    {
        // 账号页三张卡片:库街区(手机号)、云鸣潮(手机号/账号名)、mcguide(手机号/账号名/CUid)
        // 三者指向同一手机号时必须显示同一个游戏角色身份
        var players = new List<LocalLauncherPlayer> { Player() };

        var kuro = LocalGameAccountLabel.Resolve(players, phone: "16605248023", userName: null, kuroUid: "15714568");
        var cloud = LocalGameAccountLabel.Resolve(players, phone: "16605248023", userName: "U536781653A");
        var guide = LocalGameAccountLabel.Resolve(players, phone: "16605248023", userName: "U536781653A", kuroUid: "526781653");

        Assert.Equal("以椿为鸣 · 103242935", kuro);
        Assert.Equal(kuro, cloud);
        Assert.Equal(kuro, guide);
    }

    [Fact]
    public void Resolve_KuroUidIsKuroBbsId_NotGameRoleId()
    {
        // 库街区的 UserId(15714568)不是游戏角色 UID:按它匹配不到本地凭证(凭证存的是库街区 UID 526781653),
        // 此时必须返回 null 让调用方回退,绝不能把库街区 UID 当成游戏角色 ID 显示
        var players = new List<LocalLauncherPlayer> { Player() };

        Assert.Null(LocalGameAccountLabel.Resolve(players, phone: null, userName: null, kuroUid: "15714568"));
    }

    // ---------- ResolveOrFallback(账号页/签到页只关心"总要显示点什么") ----------

    [Fact]
    public void ResolveOrFallback_Matched_IgnoresFallback()
    {
        var players = new List<LocalLauncherPlayer> { Player() };

        var text = LocalGameAccountLabel.ResolveOrFallback(
            players, "16605248023", null, null, fallback: "接口账号名");

        Assert.Equal("以椿为鸣 · 103242935", text);
    }

    [Theory]
    [InlineData("18800000000")]
    [InlineData("")]
    [InlineData(null)]
    public void ResolveOrFallback_NotMatched_UsesFallback(string? phone)
    {
        var players = new List<LocalLauncherPlayer> { Player() };

        var text = LocalGameAccountLabel.ResolveOrFallback(
            players, phone, null, null, fallback: "接口账号名");

        Assert.Equal("接口账号名", text);
    }

    [Fact]
    public void ResolveOrFallback_PlayersNotLoadedYet_UsesFallback()
    {
        // 首帧本地角色还没枚举完:不能显示空,也不能显示半截信息
        var text = LocalGameAccountLabel.ResolveOrFallback(
            null, "16605248023", null, null, fallback: "接口账号名");

        Assert.Equal("接口账号名", text);
    }

    // ---------- Join(多账号标题栏) ----------

    [Fact]
    public void Join_MultipleRoles_UsesSlashSeparator()
    {
        // 签到页 3 个账号:曾经只显示"3 个库街区账号",现在逐个列出游戏角色身份
        var text = LocalGameAccountLabel.Join(["以椿为鸣 · 103242935", "慕容星言 · 103050791", "星落成海 · 117352329"]);

        Assert.Equal("以椿为鸣 · 103242935 / 慕容星言 · 103050791 / 星落成海 · 117352329", text);
    }

    [Fact]
    public void Join_SkipsEmptyEntries()
    {
        // 某个账号两样信息都拿不到时不能留下「 /  / 」这种空洞
        Assert.Equal("A · 1 / C · 3", LocalGameAccountLabel.Join(["A · 1", "", "   ", null, "C · 3"]));
    }

    [Fact]
    public void Join_AllEmpty_ReturnsEmpty()
    {
        Assert.Equal("", LocalGameAccountLabel.Join([null, "", "  "]));
    }

    [Fact]
    public void Join_CustomSeparator_ForTooltip()
    {
        Assert.Equal("A · 1\nB · 2", LocalGameAccountLabel.Join(["A · 1", "B · 2"], "\n"));
    }

    [Fact]
    public void Join_SingleEntry_NoSeparator()
    {
        Assert.Equal("以椿为鸣 · 103242935", LocalGameAccountLabel.Join(["以椿为鸣 · 103242935"]));
    }
}
