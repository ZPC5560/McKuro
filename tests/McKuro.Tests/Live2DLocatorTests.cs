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
}
