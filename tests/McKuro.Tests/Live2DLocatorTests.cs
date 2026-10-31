using McKuro.Services;
using Xunit;

namespace McKuro.Tests;

/// <summary>
/// Live2D 模型定位纯函数测试(ResolveModelDir / FindModelDir / FindDefaultModel / NormalizeModelName)。
/// 覆盖「多形态各建一个子目录」的真实布局:选中哪个形态就必须定位到哪个目录,
/// 否则首页会退回 FindDefaultModel 的字典序第一个,表现为「切形态不生效」。
/// 这些用例只碰文件系统,不需要 Cubism Core(IsSupported/IsCoreAvailable 走平台探测)。
/// </summary>
public class Live2DLocatorTests : IDisposable
{
    private readonly string _root;

    public Live2DLocatorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "McKuro-l2d-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    /// <summary>在 root 下建 <paramref name="relativeDir"/> 目录并放一个空 <paramref name="name"/>.model3.json。</summary>
    private string MakeModel(string relativeDir, string name)
    {
        var dir = relativeDir.Length == 0 ? _root : Path.Combine(_root, relativeDir);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, name + ".model3.json"), "{}");
        return dir;
    }

    [Fact]
    public void ResolveModelDir_FindsModelAtRoot()
    {
        MakeModel("", "Hiyori");

        var dir = Live2DLocator.ResolveModelDir(_root, "Hiyori");

        Assert.NotNull(dir);
        Assert.Equal(Path.GetFullPath(_root), Path.GetFullPath(dir));
    }

    [Fact]
    public void ResolveModelDir_FindsModelInSubdirectory()
    {
        // 真实布局:live2d\心-红黑\心-红黑.model3.json —— 不在根目录,只能递归找到
        var expected = MakeModel("心-红黑", "心-红黑");

        var dir = Live2DLocator.ResolveModelDir(_root, "心-红黑");

        Assert.NotNull(dir);
        Assert.Equal(Path.GetFullPath(expected), Path.GetFullPath(dir));
    }

    [Fact]
    public void ResolveModelDir_KeepsSelectedFormDistinct_WhenTwoFormsInSiblingFolders()
    {
        // 回归:两个形态同处根目录下的兄弟子目录时,选中红黑不能解析到白金
        var redBlack = MakeModel("心-红黑", "心-红黑");
        var whiteGold = MakeModel("心-白金", "心-白金");

        var pickedRed = Live2DLocator.ResolveModelDir(_root, "心-红黑");
        var pickedWhite = Live2DLocator.ResolveModelDir(_root, "心-白金");

        Assert.Equal(Path.GetFullPath(redBlack), Path.GetFullPath(pickedRed!));
        Assert.Equal(Path.GetFullPath(whiteGold), Path.GetFullPath(pickedWhite!));
        Assert.NotEqual(pickedRed, pickedWhite);
    }

    [Fact]
    public void ResolveModelDir_PrefersRoot_OverSameNamedSubdirectoryModel()
    {
        // 根目录同名模型优先(与 Sparkle.Live2DView 的「目录 + 模型名」加载约定一致)
        MakeModel("", "心");
        MakeModel("sub", "心");

        var dir = Live2DLocator.ResolveModelDir(_root, "心");

        Assert.Equal(Path.GetFullPath(_root), Path.GetFullPath(dir!));
    }

    [Fact]
    public void ResolveModelDir_ReturnsNull_ForUnknownModelOrBadRoot()
    {
        MakeModel("心-红黑", "心-红黑");

        Assert.Null(Live2DLocator.ResolveModelDir(_root, "不存在的模型"));
        Assert.Null(Live2DLocator.ResolveModelDir(_root, ""));
        Assert.Null(Live2DLocator.ResolveModelDir(_root, "   "));
        Assert.Null(Live2DLocator.ResolveModelDir(Path.Combine(_root, "没有这个目录"), "心-红黑"));
        Assert.Null(Live2DLocator.ResolveModelDir(null, "心-红黑"));
    }

    [Fact]
    public void ResolveModelDir_AcceptsNameCarryingModel3Suffix()
    {
        // 历史设置里可能存了 "Hiyori.model3" / "Hiyori.model3.json",解析前统一归一化
        var expected = MakeModel("nested", "Hiyori");

        foreach (var raw in new[] { "Hiyori", "Hiyori.model3", "Hiyori.model3.json" })
        {
            var dir = Live2DLocator.ResolveModelDir(_root, raw);
            Assert.Equal(Path.GetFullPath(expected), Path.GetFullPath(dir!));
        }
    }

    [Fact]
    public void NormalizeModelName_StripsBothSuffixForms()
    {
        Assert.Equal("Hiyori", Live2DLocator.NormalizeModelName("Hiyori"));
        Assert.Equal("Hiyori", Live2DLocator.NormalizeModelName("Hiyori.model3"));
        Assert.Equal("Hiyori", Live2DLocator.NormalizeModelName("Hiyori.model3.json"));
        Assert.Equal("Hiyori", Live2DLocator.NormalizeModelName("  Hiyori.model3.json  "));
        Assert.Equal("", Live2DLocator.NormalizeModelName(null));
        Assert.Equal("", Live2DLocator.NormalizeModelName("   "));
    }

    [Fact]
    public void FindDefaultModel_PrefersHiyori_ThenLexicographic()
    {
        MakeModel("z", "Zenobia");
        MakeModel("心-红黑", "心-红黑");
        MakeModel("h", "Hiyori");

        var pick = Live2DLocator.FindDefaultModel(_root);

        // 未指定模型时的兜底:Hiyori 优先,其次字典序
        Assert.NotNull(pick);
        Assert.Equal("Hiyori", pick!.Value.Name);
    }

    [Fact]
    public void FindDefaultModel_FallsBackToLexicographic_WhenNoHiyori()
    {
        MakeModel("b", "Beta");
        MakeModel("a", "Alpha");

        var pick = Live2DLocator.FindDefaultModel(_root);

        Assert.NotNull(pick);
        Assert.Equal("Alpha", pick!.Value.Name);
    }

    // ---------------------------------------------------------------------
    // 随包分发相关:发现根与偏好顺序
    //
    // 背景:模型随包放在程序目录的 live2d\ 下。装到 Program Files 时该目录不可写,
    // EnsurePreferredDir() 会回退到用户数据目录 —— 而随包模型不在那里。
    // 若发现逻辑只认"首选可写目录",随包模型将永远不可见。
    // ---------------------------------------------------------------------

    [Fact]
    public void DiscoveryRoots_OnlyReturnsExistingDirectories()
    {
        // 契约:发现根只包含【确实存在】的目录(纯探测,不创建)。
        // 注意不要断言"一定包含 ProgramDir" —— 随包资源只在 Windows 构建产出,
        // Linux/macOS 或纯测试环境下该目录可能不存在,那样的断言是环境依赖的假失败
        // (CI 上真的红过一次)。这里只验证"返回的都可枚举/都存在"这一真正的不变量。
        var roots = Live2DLocator.DiscoveryRoots().ToList();

        Assert.All(roots, r => Assert.True(Directory.Exists(r), $"发现根不存在:{r}"));
        Assert.Equal(roots.Distinct(StringComparer.Ordinal).Count(), roots.Count);
    }

    [Fact]
    public void DiscoveryRoots_DoesNotRecurseApplicationBaseDirectory()
    {
        // 程序根不能作为递归根:便携版解压到 Downloads 这类大目录时,首页构造会在 UI 线程
        // 遍历整棵目录树(启动卡住)。随包模型在程序目录 live2d\ 下,由 ProgramDir 覆盖。
        var roots = Live2DLocator.DiscoveryRoots().ToList();

        Assert.DoesNotContain(AppContext.BaseDirectory, roots);
    }

    [Fact]
    public void ResolveModelDirAnywhere_FindsModelFlatAtBaseDirectory_ButNotNestedOutsideLive2D()
    {
        // 平铺在 exe 同级的模型仍可用(只查顶层);程序目录里 live2d\ 之外的深层目录不再被递归。
        var name = "平铺形态-" + Guid.NewGuid().ToString("N");
        var flat = Path.Combine(AppContext.BaseDirectory, name + ".model3.json");
        var nestedDir = Path.Combine(AppContext.BaseDirectory, "深层-" + Guid.NewGuid().ToString("N"));
        var nestedName = Path.GetFileName(nestedDir);
        try
        {
            File.WriteAllText(flat, "{}");
            Directory.CreateDirectory(nestedDir);
            File.WriteAllText(Path.Combine(nestedDir, nestedName + ".model3.json"), "{}");

            Assert.Equal(AppContext.BaseDirectory, Live2DLocator.ResolveModelDirAnywhere(name));
            Assert.Null(Live2DLocator.ResolveModelDirAnywhere(nestedName));
        }
        finally
        {
            try { File.Delete(flat); } catch { /* ignore */ }
            try { Directory.Delete(nestedDir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void DiscoveryRoots_EnumeratesBundledModelPlacedUnderBaseDirectory()
    {
        // 端到端语义:把模型放到 <BaseDirectory>/live2d/<形态>/ 后,发现逻辑必须能找到它。
        // 这正是随包布局;用真实 BaseDirectory 验证,顺带覆盖 ProgramDir 参与发现这一点。
        var bundled = Path.Combine(AppContext.BaseDirectory, "live2d", "测试形态-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(bundled);
            var name = Path.GetFileName(bundled);
            File.WriteAllText(Path.Combine(bundled, name + ".model3.json"), "{}");

            var hit = Live2DLocator.ResolveModelDirAnywhere(name);

            Assert.NotNull(hit);
            Assert.Equal(name, Path.GetFileName(hit!));
        }
        finally
        {
            try { Directory.Delete(bundled, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void DiscoveryRoots_DoesNotCreateMissingDirectories()
    {
        // 纯探测:不得因为"发现"而随手建目录(否则会在用户机器上留下空目录)
        var before = Live2DLocator.DiscoveryRoots().ToList();
        var after = Live2DLocator.DiscoveryRoots().ToList();

        Assert.Equal(before, after);
    }

    [Fact]
    public void BundledDefaultName_IsTheRedBlackForm()
    {
        // 随包主形态;FindDefaultModelAnywhere 用它做第二优先级
        Assert.Equal("心-红黑", Live2DLocator.BundledDefaultName);
    }

    [Fact]
    public void FindDefaultModel_StillPrefersHiyori_OverEverythingElse()
    {
        // Hiyori(官方示例)优先级最高:用户自行放置时不应被随包模型抢走
        MakeModel("心-红黑", "心-红黑");
        MakeModel("h", "Hiyori");

        var pick = Live2DLocator.FindDefaultModel(_root);

        Assert.Equal("Hiyori", pick!.Value.Name);
    }

    [Fact]
    public void ResolveModelDirAnywhere_ReturnsNullForUnknownName()
    {
        Assert.Null(Live2DLocator.ResolveModelDirAnywhere("绝对不存在的模型名"));
        Assert.Null(Live2DLocator.ResolveModelDirAnywhere(""));
        Assert.Null(Live2DLocator.ResolveModelDirAnywhere(null));
    }

    [Fact]
    public void ResolveModelDirAnywhere_FindsBundledModelInProgramDir()
    {
        // 真实随包布局:程序目录 live2d\<形态>\<形态>.model3.json。
        // 用临时目录模拟后断言解析路径正确(不去动真实安装目录)。
        var bundled = Path.Combine(_root, "live2d");
        Directory.CreateDirectory(Path.Combine(bundled, "心-红黑"));
        File.WriteAllText(Path.Combine(bundled, "心-红黑", "心-红黑.model3.json"), "{}");

        var hit = Live2DLocator.ResolveModelDir(bundled, "心-红黑");

        Assert.NotNull(hit);
        Assert.Equal("心-红黑", Path.GetFileName(hit!));
    }

    [Fact]
    public void FindDefaultModelAnywhere_PrefersBundledRedBlack_OverWhiteGold()
    {
        // 回归(实测踩到):随包目录同时有「心-红黑」与「心-白金」。
        // 按 Ordinal 比较「白」(U+767D) < 「红」(U+7EA2),所以字典序第一是白金。
        // 若逐根只挑"字典序第一个",默认模型会变成白金,内置主形态(红黑)永远选不中。
        MakeModel("心-白金", "心-白金");
        MakeModel("心-红黑", "心-红黑");

        var pick = Live2DLocator.FindDefaultModelAnywhere([_root]);

        Assert.NotNull(pick);
        Assert.Equal("心-红黑", pick!.Value.Name);
    }

    [Fact]
    public void FindDefaultModelAnywhere_StillPrefersHiyori_AcrossRoots()
    {
        // Hiyori 优先级高于内置形态,即使内置形态在更靠前的根里
        var first = Path.Combine(_root, "rootA");
        var second = Path.Combine(_root, "rootB");
        Directory.CreateDirectory(Path.Combine(first, "心-红黑"));
        File.WriteAllText(Path.Combine(first, "心-红黑", "心-红黑.model3.json"), "{}");
        Directory.CreateDirectory(Path.Combine(second, "h"));
        File.WriteAllText(Path.Combine(second, "h", "Hiyori.model3.json"), "{}");

        var pick = Live2DLocator.FindDefaultModelAnywhere([first, second]);

        Assert.NotNull(pick);
        Assert.Equal("Hiyori", pick!.Value.Name);
    }

    [Fact]
    public void FindDefaultModelAnywhere_SkipsMissingRoots()
    {
        // 不存在/无权限的根不能让发现整体失败
        MakeModel("real", "心-红黑");

        var pick = Live2DLocator.FindDefaultModelAnywhere(
            [Path.Combine(_root, "不存在"), Path.Combine(_root, "real")]);

        Assert.NotNull(pick);
        Assert.Equal("心-红黑", pick!.Value.Name);
    }

    [Fact]
    public void FindDefaultModelAnywhere_ReturnsNull_WhenNoModelsAnywhere()
    {
        var empty = Path.Combine(_root, "empty");
        Directory.CreateDirectory(empty);

        Assert.Null(Live2DLocator.FindDefaultModelAnywhere([empty]));
    }

    [Fact]
    public void PreferenceOrder_RedBlackBeatsWhiteGold_UnderOrdinalSort()
    {
        // 锁住"为什么需要显式偏好":Ordinal 顺序确实把白金排在前面
        Assert.True(string.CompareOrdinal("心-白金", "心-红黑") < 0,
            "前提变化:字典序不再把白金排在前面,本回归的前提需重新审视");
        Assert.Equal("心-红黑", Live2DLocator.BundledDefaultName);
    }
}
