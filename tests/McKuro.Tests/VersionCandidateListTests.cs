using McKuro.Views;

namespace McKuro.Tests;

/// <summary>
/// 「指定本地游戏版本」下拉的候选装配(纯逻辑)。
/// <para>
/// 回归背景:该下拉曾是可编辑的(<c>IsEditable="True"</c>),框里会出现文本光标、还能手输任意值;
/// 改为「只能从候选里选」后,必须保证<b>当前本地记录即使不在服务端候选里也能被选中</b>,
/// 否则用户遇到"游戏版本比清单更新"时就没法确认原值了。
/// </para>
/// </summary>
public sealed class VersionCandidateListTests
{
    [Fact]
    public void Build_SelectsRequestedVersion_WhenPresent()
    {
        var (items, index) = VersionCandidateList.Build(["3.7.0", "3.6.1", "3.6.0"], "3.6.1");

        Assert.Equal(["3.7.0", "3.6.1", "3.6.0"], items);
        Assert.Equal(1, index);
        Assert.Equal("3.6.1", items[index]);
    }

    [Fact]
    public void Build_InsertsRequestedVersion_WhenMissing()
    {
        // 核心回归:当前本地记录 3.7.0 不在服务端候选里 —— 必须补进列表并选中,
        // 否则下拉不可编辑后用户无法保留原值
        var (items, index) = VersionCandidateList.Build(["3.6.1", "3.6.0"], "3.7.0");

        Assert.Equal(["3.7.0", "3.6.1", "3.6.0"], items);
        Assert.Equal(0, index);
    }

    [Fact]
    public void Build_MissingRequestedVersion_IsNotDuplicated()
    {
        var (items, index) = VersionCandidateList.Build(["3.6.1", "3.6.0"], "3.6.0");

        Assert.Equal(["3.6.1", "3.6.0"], items);
        Assert.Equal(1, index);
    }

    [Fact]
    public void Build_PreservesServerOrder_WhenRequestedVersionPresent()
    {
        // 服务端已按新→旧排序:目标版本在候选里时必须原样保留顺序,不能把它提到最前
        var (items, index) = VersionCandidateList.Build(["3.7.0", "3.6.1", "3.6.0"], "3.6.0");

        Assert.Equal(["3.7.0", "3.6.1", "3.6.0"], items);
        Assert.Equal(2, index);
    }

    [Fact]
    public void Build_NoRequestedVersion_SelectsFirst()
    {
        var (items, index) = VersionCandidateList.Build(["3.7.0", "3.6.1"], null);

        Assert.Equal(["3.7.0", "3.6.1"], items);
        Assert.Equal(0, index);
    }

    [Fact]
    public void Build_OnlyRequestedVersion_ProducesSingleItem()
    {
        // 网络失败时 refresher 可能返回空列表:仍然要能显示当前版本
        var (items, index) = VersionCandidateList.Build([], "3.7.0");

        Assert.Equal(["3.7.0"], items);
        Assert.Equal(0, index);
    }

    [Fact]
    public void Build_NothingAtAll_ReturnsEmptyWithNoSelection()
    {
        var (items, index) = VersionCandidateList.Build([], null);

        Assert.Empty(items);
        Assert.Equal(-1, index);
    }

    [Fact]
    public void Build_DeduplicatesCaseInsensitively()
    {
        var (items, _) = VersionCandidateList.Build(["3.6.1", "3.6.1"], "3.6.1");

        Assert.Equal(["3.6.1"], items);
    }

    [Theory]
    [InlineData("  3.6.1  ", "3.6.1")]
    [InlineData("", "3.6.1")]      // 空白目标值 → 退回第一个候选(不清空列表)
    [InlineData("   ", "3.6.1")]
    public void Build_TrimsAndIgnoresBlankRequestedVersion(string requested, string expectedFirst)
    {
        var (items, index) = VersionCandidateList.Build(["3.6.1", "3.7.0"], requested);

        Assert.Equal(expectedFirst, items[index]);
        Assert.DoesNotContain("", items);
    }

    [Fact]
    public void Build_SkipsBlankCandidates()
    {
        var (items, _) = VersionCandidateList.Build([null!, "", "  ", "3.6.1"], null);

        Assert.Equal(["3.6.1"], items);
    }

    [Fact]
    public void Build_MatchesRequestedVersionCaseInsensitively()
    {
        // 目标值大小写不同但视为同一版本 → 命中原项,不重复插入
        var (items, index) = VersionCandidateList.Build(["3.6.1"], "3.6.1");

        Assert.Equal(["3.6.1"], items);
        Assert.Equal(0, index);
    }
}
