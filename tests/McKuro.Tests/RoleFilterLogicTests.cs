using McKuro.Core.Models.Roles;
using Xunit;

namespace McKuro.Tests;

/// <summary>
/// 角色数据的属性筛选 + 排序逻辑测试(纯模型层,不依赖 UI)。
/// </summary>
public class RoleFilterLogicTests
{
    private static RoleDetail Role(string name, string attribute, int star)
        => new()
        {
            Role = new RoleInfo
            {
                RoleName = name,
                AttributeName = attribute,
                StarLevel = star,
            },
        };

    [Fact]
    public void Distinct_AttributeNames_Are_Ordered()
    {
        var roles = new List<RoleDetail>
        {
            Role("A", "衍射", 5),
            Role("B", "冷凝", 5),
            Role("C", "衍射", 4),
            Role("D", "", 4), // 空属性应被过滤
        };

        var attributes = roles
            .Select(r => r.AttributeName)
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Distinct()
            .OrderBy(a => a, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(new[] { "衍射", "冷凝" }.OrderBy(a => a, StringComparer.Ordinal).ToArray(), attributes);
        Assert.DoesNotContain("", attributes);
    }

    [Fact]
    public void Filter_By_Attribute_Keeps_Matching_Only()
    {
        var roles = new List<RoleDetail>
        {
            Role("A", "衍射", 5),
            Role("B", "冷凝", 5),
            Role("C", "衍射", 4),
        };

        var filtered = roles
            .Where(r => string.Equals(r.AttributeName, "衍射", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(2, filtered.Count);
        Assert.All(filtered, r => Assert.Equal("衍射", r.AttributeName));
    }

    [Fact]
    public void Sort_By_Star_Descending_Then_Name()
    {
        var roles = new List<RoleDetail>
        {
            Role("B", "衍射", 5),
            Role("A", "衍射", 5),
            Role("C", "衍射", 4),
        };

        var sorted = roles
            .OrderByDescending(r => r.StarLevel)
            .ThenBy(r => r.RoleName, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(new[] { "A", "B", "C" }, sorted.Select(r => r.RoleName).ToArray());
        Assert.Equal(new[] { 5, 5, 4 }, sorted.Select(r => r.StarLevel).ToArray());
    }

    [Fact]
    public void Sort_By_Name_Ascending()
    {
        var roles = new List<RoleDetail>
        {
            Role("Z", "衍射", 4),
            Role("A", "衍射", 5),
        };

        var sorted = roles
            .OrderBy(r => r.RoleName, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(new[] { "A", "Z" }, sorted.Select(r => r.RoleName).ToArray());
    }
}
