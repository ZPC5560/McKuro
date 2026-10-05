using McKuro.Core.Models.Game;
using McKuro.Core.Services.Game;

namespace McKuro.Tests;

/// <summary>
/// 版本下拉的历史版本提取(对齐 Haiyu SelectGameFolderViewModelV2):
/// 从服务端补丁清单 patchConfig[].indexFile 路径里取出各补丁的目标版本。
/// </summary>
public sealed class GameUpdaterVersionListTests
{
    private static KuroUpdateData MakeUpdate(params string?[] indexFiles)
    {
        var config = new KuroConfig { PatchConfig = [] };
        foreach (var indexFile in indexFiles)
        {
            config.PatchConfig.Add(new KuroPatchConfig { IndexFile = indexFile });
        }
        return new KuroUpdateData { Version = "3.7.0", Config = config };
    }

    /// <summary>
    /// <b>真实</b>补丁路径形态(取自线上 index.json):游戏版本号在路径里出现<b>两次</b>
    /// (<c>/3.7.0/&lt;hash&gt;/resource/10003/3.7.0/&lt;补丁版本&gt;/indexFile.json</c>),
    /// 后面才是该补丁的目标版本。Haiyu 靠 <c>Distinct()</c> 去掉重复的游戏版本号后再取 [1]。
    /// </summary>
    private static string RealPath(string patchVersion) =>
        $"launcher/game/G152/10003/3.7.0/zcRdpyNBiSNVKeMUrEhfyRZmOEZArRGE/resource/10003/3.7.0/{patchVersion}/indexFile.json";

    [Fact]
    public void ExtractHistoricalVersions_RealPathWithRepeatedGameVersion_TakesPatchVersion()
    {
        // 回归:此前漏了 Haiyu 的 Distinct(),[1] 取到的是重复的游戏版本(3.7.0),
        // 全部条目都产出同一个值 → 去重后下拉只剩一个候选(用户报"只获取到 3.7 单个版本")
        var update = MakeUpdate(RealPath("1.0.0"), RealPath("3.6.1"), RealPath("2.5.0"));

        var versions = GameUpdater.ExtractHistoricalVersions(update);

        Assert.Equal(["3.6.1", "2.5.0", "1.0.0"], versions);
    }

    [Fact]
    public void ExtractHistoricalVersions_RealPathShape_DoesNotLeakGameVersion()
    {
        var update = MakeUpdate(RealPath("3.6.1"));

        var versions = GameUpdater.ExtractHistoricalVersions(update);

        Assert.Equal(["3.6.1"], versions);
        Assert.DoesNotContain("3.7.0", versions); // 游戏版本号绝不能混进历史版本列表
    }

    [Fact]
    public void ExtractHistoricalVersions_PrefersDeclaredVersionField()
    {
        // 清单里 version 是一等字段(实测与路径推导 48/48 一致):有它就不必依赖路径解析
        var config = new KuroConfig
        {
            PatchConfig = [new KuroPatchConfig { IndexFile = null, Version = "3.6.1" }],
        };
        var update = new KuroUpdateData { Version = "3.7.0", Config = config };

        Assert.Equal(["3.6.1"], GameUpdater.ExtractHistoricalVersions(update));
    }

    [Fact]
    public void ExtractHistoricalVersions_DeclaredVersionWinsOverPath()
    {
        // 即便路径形态变化,显式声明的补丁版本也应优先
        var config = new KuroConfig
        {
            PatchConfig = [new KuroPatchConfig { IndexFile = "a/1.0.0/whatever/indexFile.json", Version = "9.9.9" }],
        };
        var update = new KuroUpdateData { Version = "3.7.0", Config = config };

        Assert.Equal(["9.9.9"], GameUpdater.ExtractHistoricalVersions(update));
    }

    [Fact]
    public void ExtractHistoricalVersions_BlankDeclaredVersion_FallsBackToPath()
    {
        var config = new KuroConfig
        {
            PatchConfig = [new KuroPatchConfig { IndexFile = RealPath("2.5.0"), Version = "   " }],
        };
        var update = new KuroUpdateData { Version = "3.7.0", Config = config };

        Assert.Equal(["2.5.0"], GameUpdater.ExtractHistoricalVersions(update));
    }

    [Fact]
    public void ExtractHistoricalVersions_TakesSecondVersionFromPath_NewestFirst()
    {
        // 每条补丁路径含「源版本 → 目标版本」两个版本号:Haiyu 取第 2 个作为该补丁目标版本
        var update = MakeUpdate(
            "game/3.6.1/official/3.6.0/indexFile.json",
            "game/3.7.0/official/3.6.1/indexFile.json",
            "game/3.5.3/official/3.5.2/indexFile.json");

        var versions = GameUpdater.ExtractHistoricalVersions(update);

        Assert.Equal(["3.6.1", "3.6.0", "3.5.2"], versions);
    }

    [Fact]
    public void ExtractHistoricalVersions_DeduplicatesPreservingNumericOrder()
    {
        var update = MakeUpdate(
            "game/3.6.0/a/3.5.3/indexFile.json",
            "game/3.7.0/b/3.5.3/indexFile.json",
            "game/3.6.0/c/3.5.2/indexFile.json");

        var versions = GameUpdater.ExtractHistoricalVersions(update);

        Assert.Equal(["3.5.3", "3.5.2"], versions);
    }

    [Fact]
    public void ExtractHistoricalVersions_SortsNumericallyNotLexically()
    {
        // 字符串排序会把 "3.10.0" 排在 "3.9.0" 前面;数值排序必须让 3.10.0 在前
        var update = MakeUpdate(
            "a/1.0.0/3.9.0/indexFile.json",
            "b/1.0.0/3.10.0/indexFile.json",
            "c/1.0.0/3.2.0/indexFile.json");

        var versions = GameUpdater.ExtractHistoricalVersions(update);

        Assert.Equal(["3.10.0", "3.9.0", "3.2.0"], versions);
    }

    [Fact]
    public void ExtractHistoricalVersions_IgnoresDirtyAndSingleVersionPaths()
    {
        var update = MakeUpdate(
            null,                                     // 空路径
            "",                                       // 空串
            "game/latest/indexFile.json",             // 完全没有版本号
            "game/3.6.0/indexFile.json",              // 只有一个版本号(取不到目标版本)
            "game/x/3.6.0/y/3.5.0/indexFile.json");   // 有效项

        var versions = GameUpdater.ExtractHistoricalVersions(update);

        Assert.Equal(["3.5.0"], versions);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-version-here.json")]
    public void ExtractHistoricalVersions_NoUsableInput_ReturnsEmpty(string? indexFile)
    {
        var update = MakeUpdate(indexFile);
        Assert.Empty(GameUpdater.ExtractHistoricalVersions(update));
    }

    [Fact]
    public void ExtractHistoricalVersions_MissingConfigOrPatchConfig_ReturnsEmpty()
    {
        Assert.Empty(GameUpdater.ExtractHistoricalVersions(null));
        Assert.Empty(GameUpdater.ExtractHistoricalVersions(new KuroUpdateData()));
        Assert.Empty(GameUpdater.ExtractHistoricalVersions(new KuroUpdateData { Config = new KuroConfig() }));
    }

    [Fact]
    public void ExtractHistoricalVersions_AcceptsFourSegmentVersions()
    {
        var update = MakeUpdate("a/1.0.0/3.6.1.12345/indexFile.json");

        var versions = GameUpdater.ExtractHistoricalVersions(update);

        Assert.Equal(["3.6.1.12345"], versions);
    }

    /// <summary>
    /// 回归(评审:候选校验不对称):<see cref="GameUpdater.GetKnownVersionsAsync"/> 的本地记录与
    /// 服务端版本都进同一个下拉,而 <c>TrySetInstalledVersion</c> 会拒绝不合规格式 ——
    /// 下拉又是<b>不可编辑</b>的,混进异常版本串会让用户选中后只得到「版本号格式无效」而无法完成操作。
    /// <see cref="GameUpdater.ExtractHistoricalVersions"/> 必须一致地过滤掉不合规形态。
    /// <para>
    /// 实测口径:提取用的正则只吃数字与点,故 <c>3.6.1-hotfix</c> 会被提成合法的 <c>3.6.1</c>(收);
    /// 真正能被这条门挡下的是段数超限(如 5 段)。
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("a/1.0.0/1.2.3.4.5/indexFile.json", false)]      // 5 段(超 4)→ 拒
    [InlineData("a/1.0.0/3.6.1-hotfix/indexFile.json", true)]    // 后缀被正则剥掉 → 提成 3.6.1 → 收
    [InlineData("a/1.0.0/3.6/indexFile.json", true)]             // 2 段 → 收
    [InlineData("a/1.0.0/3.6.1/indexFile.json", true)]           // 3 段 → 收
    public void ExtractHistoricalVersions_OnlyKeepsParseableVersions(string indexFile, bool expectAccepted)
    {
        var versions = GameUpdater.ExtractHistoricalVersions(MakeUpdate(indexFile));

        if (expectAccepted)
        {
            Assert.Single(versions);
        }
        else
        {
            Assert.Empty(versions);
        }
    }
}
