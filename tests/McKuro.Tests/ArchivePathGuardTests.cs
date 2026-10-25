using McKuro.Core.Infrastructure;

namespace McKuro.Tests;

/// <summary>
/// 归档/清单路径越界判定测试(2026-10-25 评审发现 zip-slip 前缀绕过后的回归锁定)。
/// <para>
/// 缺陷形态:此前用 <c>target.StartsWith(root, Ordinal)</c> —— 少了末尾目录分隔符,
/// 于是 root=<c>C:\x\app</c> 时,条目 <c>../app2/evil.dll</c> 归一化成的
/// <c>C:\x\app2\evil.dll</c> 前缀相同却被放行,可写到目录外。
/// </para>
/// </summary>
public class ArchivePathGuardTests
{
    private static string Root => Path.Combine(Path.GetTempPath(), "McKuro-guard", "app");
    private static string Sibling => Path.Combine(Path.GetTempPath(), "McKuro-guard", "app2");

    [Fact]
    public void Normal_Child_Path_Is_Inside()
    {
        var target = Path.GetFullPath(Path.Combine(Root, "sub", "file.dll"));
        Assert.True(ArchivePathGuard.IsUnderRoot(Root, target));
    }

    [Fact]
    public void DotDot_Escape_Is_Rejected()
    {
        var target = Path.GetFullPath(Path.Combine(Root, "..", "evil.dll"));
        Assert.False(ArchivePathGuard.IsUnderRoot(Root, target));
    }

    /// <summary>
    /// 核心回归:兄弟目录名字以 root 为前缀(<c>app</c> vs <c>app2</c>),
    /// 旧写法会放行,新写法必须拒绝。
    /// </summary>
    [Fact]
    public void Sibling_Directory_Sharing_Prefix_Is_Rejected()
    {
        var target = Path.GetFullPath(Path.Combine(Sibling, "evil.dll"));
        Assert.False(ArchivePathGuard.IsUnderRoot(Root, target));
        // 反向确认它不是"恰好被归一化掉":目标确实落在兄弟目录里
        Assert.StartsWith(Path.GetFullPath(Sibling), target, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Root_Itself_Is_Not_A_File_Target()
    {
        // root 自身(目录)不算"在目录内"的文件目标:条目名不可能为空,防御性拒绝
        Assert.False(ArchivePathGuard.IsUnderRoot(Root, Path.GetFullPath(Root)));
    }

    [Fact]
    public void Absolute_Entry_Path_Is_Rejected()
    {
        var absolute = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "outside.dll"));
        Assert.False(ArchivePathGuard.IsSafeRelativePath(absolute));
    }

    [Theory]
    [InlineData("../x.dll")]
    [InlineData("a/../../x.dll")]
    [InlineData("a/./b.dll")]
    [InlineData("..\\x.dll")]
    public void Unsafe_Relative_Paths_Are_Rejected(string relative)
        => Assert.False(ArchivePathGuard.IsSafeRelativePath(relative));

    [Theory]
    [InlineData("sub/file.dll")]
    [InlineData("sub\\file.dll")]
    [InlineData("a/b/c.bin")]
    public void Safe_Relative_Paths_Are_Accepted(string relative)
        => Assert.True(ArchivePathGuard.IsSafeRelativePath(relative));

    [Fact]
    public void CombineUnderRoot_Throws_On_Escape()
    {
        Assert.Throws<InvalidDataException>(() => ArchivePathGuard.CombineUnderRoot(Root, "../evil.dll"));
    }

    [Fact]
    public void CombineUnderRoot_Resolves_Normal_Child()
    {
        var combined = ArchivePathGuard.CombineUnderRoot(Root, "sub/file.dll");
        Assert.True(ArchivePathGuard.IsUnderRoot(Root, combined));
        Assert.EndsWith(Path.Combine("sub", "file.dll"), combined, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Windows 下大小写不同的合法路径不应被拒(旧实现用 Ordinal 会误拒)。
    /// </summary>
    [Fact]
    public void Case_Differences_Are_Tolerated_On_Windows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        var upperRoot = Root.ToUpperInvariant();
        var target = Path.GetFullPath(Path.Combine(Root, "sub", "file.dll"));
        Assert.True(ArchivePathGuard.IsUnderRoot(upperRoot, target));
    }
}
