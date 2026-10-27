using McKuro.Core.Models.Game;
using McKuro.Core.Services.Game;
using Xunit;

namespace McKuro.Tests;

/// <summary>
/// P2:差分尺寸预估 + 官方画质说明页链接。
/// <para>
/// <b>尺寸这块的核心事实(实测国服 3.7.1):</b>common 40.17 GB + uhd 61.50 GB = 99.45 GB。
/// 已经装了 HD 的用户 common 本来就有,切 UHD 只需下 61.50 GB。
/// 卡片若显示 99.45,等于多报 37.95 GB —— 足够让人直接放弃切换。
/// 所以「游戏大小」(这一档多大) 与「需下载」(你还差多少) 必须是两个数。
/// </para>
/// </summary>
public class ResourceLevelSizeEstimateTests
{
    private const long GiB = 1024L * 1024 * 1024;

    private static KuroResourcePackIndex Index()
    {
        var index = new KuroResourcePackIndex
        {
            ResourcePacks = new Dictionary<string, KuroResourcePack>
            {
                ["common"] = new() { Version = "3.7.1", Size = 40 * GiB },
                ["hd"] = new() { Version = "3.7.1", Size = 44 * GiB },
                ["sd"] = new() { Version = "3.7.1", Size = 21 * GiB },
                ["uhd"] = new() { Version = "3.7.1", Size = 61 * GiB },
            },
            Bundles = new Dictionary<string, KuroResourceBundle>
            {
                ["HD"] = new() { ResourcePacks = ["common", "hd"] },
                ["SD"] = new() { ResourcePacks = ["common", "sd"] },
                ["UHD"] = new() { ResourcePacks = ["common", "uhd"] },
            },
        };
        return index;
    }

    [Fact]
    public void MissingBytes_Does_Not_Count_Already_Installed_Shared_Pack()
    {
        var index = Index();
        // 用户已完整安装 HD:common + hd 都在
        var installed = new HashSet<string> { "common", "hd" };

        var missing = ResourceLevelPlanner.MissingBytesFor(index, "UHD", installed.Contains);

        // 只需下 uhd(61 GB),不能把已经有的 common(40 GB) 再算一遍
        Assert.Equal(61 * GiB, missing);
        // 对照:总量仍然是 101 GB —— 两个数语义不同,不能混用
        Assert.Equal(101 * GiB, ResourceLevelPlanner.TotalBytesFor(index, "UHD"));
    }

    [Fact]
    public void MissingBytes_Equals_Total_When_Nothing_Installed()
    {
        var index = Index();

        var missing = ResourceLevelPlanner.MissingBytesFor(index, "HD", _ => false);

        Assert.Equal(ResourceLevelPlanner.TotalBytesFor(index, "HD"), missing);
        Assert.Equal(84 * GiB, missing);
    }

    [Fact]
    public void MissingBytes_Is_Zero_When_Level_Already_Installed()
    {
        var index = Index();
        var installed = new HashSet<string> { "common", "hd" };

        Assert.Equal(0, ResourceLevelPlanner.MissingBytesFor(index, "HD", installed.Contains));
    }

    [Fact]
    public void MissingBytes_Counts_Missing_Common_Too()
    {
        // 全新用户切 SD:common 与 sd 都缺 → 两个都要算
        var index = Index();

        Assert.Equal(61 * GiB, ResourceLevelPlanner.MissingBytesFor(index, "SD", _ => false));
    }

    // ---------- 官方画质说明页链接 ----------

    private static KuroResourcePackIndex IndexWithHint(params (string Lang, string Url)[] hints)
    {
        var index = Index();
        index.Config = new KuroResourcePackRootConfig
        {
            ResourceLevelHint = hints.ToDictionary(h => h.Lang, h => h.Url),
        };
        return index;
    }

    [Fact]
    public void LevelHint_Prefers_Exact_Language()
    {
        var index = IndexWithHint(("zh-Hans", "https://cn.example/5478"), ("en", "https://en.example/5513"));

        Assert.Equal("https://cn.example/5478", ResourceLevelPlanner.LevelHintUrl(index, "zh-Hans"));
        Assert.Equal("https://en.example/5513", ResourceLevelPlanner.LevelHintUrl(index, "en"));
    }

    [Fact]
    public void LevelHint_Falls_Back_To_Short_Language_Code()
    {
        // 界面语言带地区(en-US),官方键是短码(en)
        var index = IndexWithHint(("zh-Hans", "https://cn.example/x"), ("en", "https://en.example/y"));

        Assert.Equal("https://en.example/y", ResourceLevelPlanner.LevelHintUrl(index, "en-US"));
    }

    [Fact]
    public void LevelHint_Preserves_Regional_Official_Keys()
    {
        // 国际服有 pt-BR 这种带地区的官方键:精确匹配优先,不能被短码截走
        var index = IndexWithHint(
            ("pt", "https://wrong.example/pt"),
            ("pt-BR", "https://right.example/pt-BR"));

        Assert.Equal("https://right.example/pt-BR", ResourceLevelPlanner.LevelHintUrl(index, "pt-BR"));
    }

    [Fact]
    public void LevelHint_Falls_Back_To_English_Then_Any()
    {
        var onlyJa = IndexWithHint(("ja", "https://jp.example/x"));
        Assert.Equal("https://jp.example/x", ResourceLevelPlanner.LevelHintUrl(onlyJa, "zh-Hans"));

        var withEn = IndexWithHint(("ja", "https://jp.example/x"), ("en", "https://en.example/y"));
        Assert.Equal("https://en.example/y", ResourceLevelPlanner.LevelHintUrl(withEn, "zh-Hans"));
    }

    [Fact]
    public void LevelHint_Returns_Null_When_Node_Absent()
    {
        // 国服/B站实测只给 zh-Hans,但字段本身可能整个缺失 —— 此时 UI 隐藏入口
        Assert.Null(ResourceLevelPlanner.LevelHintUrl(Index(), "zh-Hans"));
        Assert.Null(ResourceLevelPlanner.LevelHintUrl(IndexWithHint(), "zh-Hans"));
    }

    [Fact]
    public void LevelHint_Ignores_Blank_Urls()
    {
        var index = IndexWithHint(("zh-Hans", ""), ("en", "https://en.example/y"));

        Assert.Equal("https://en.example/y", ResourceLevelPlanner.LevelHintUrl(index, "zh-Hans"));
    }
}
