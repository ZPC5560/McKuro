using System.Text.RegularExpressions;

namespace McKuro.Tests;

/// <summary>
/// XAML 资源引用完整性守卫。
/// <para>
/// 回归背景:RolesView.axaml 曾引用 <c>{StaticResource StringIsEmpty}</c> 但该转换器从未定义 ——
/// 编译期不报错(XAML 编译只校验 xml 结构),运行时实例化 RolesView 抛异常并被 Avalonia 吞掉,
/// 表现为**角色页整片空白**(用户反馈"点击角色数据页面卡死/空白")。此测试把这类问题挡在 CI。
/// </para>
/// <para>
/// 判定口径(评审反馈重构):"应用定义集" = App.axaml 的全部 x:Key ∪ 被扫描视图自身定义的 x:Key,
/// 不再手工维护白名单(旧白名单没跟上 App.axaml 新增键,导致守卫确定性误红);
/// 仅主题命名空间(前缀 <c>Semi</c>,由 Irihi.Semi 主题包提供)按前缀放行。
/// 扫描范围 = Views 目录下全部 .axaml(旧版只查 RolesView,其余视图裸奔)。
/// </para>
/// </summary>
public class XamlResourceReferenceTests
{
    /// <summary>XAML 里 xx:Key / x:Key 定义的资源名。</summary>
    private static HashSet<string> ParseDefinedKeys(string xaml)
        => Regex.Matches(xaml, @"(?:x|xc|vm|roles|guide):Key=""([^""]+)""")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>XAML 里 StaticResource / DynamicResource 引用的资源名。</summary>
    private static HashSet<string> ParseUsedResources(string xaml)
        => Regex.Matches(xaml, @"(?:Static|Dynamic)Resource\s+([A-Za-z0-9_]+)")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>定位仓库根(测试输出目录在 tests/.../bin/Release/net10.0,向上找 src/McKuro)。</summary>
    private static string RepositoryRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && dir is not null; i++)
        {
            if (Directory.Exists(Path.Combine(dir, "src", "McKuro")))
            {
                return dir;
            }
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new DirectoryNotFoundException("未能定位仓库根(src/McKuro 所在目录)");
    }

    /// <summary>App.axaml 定义的全部资源键(应用级定义集;两主题字典的键都算)。</summary>
    private static HashSet<string> AppDefinedKeys()
    {
        var path = Path.Combine(RepositoryRoot(), "src", "McKuro", "App.axaml");
        Assert.True(File.Exists(path), $"App.axaml 不存在: {path}");
        return ParseDefinedKeys(File.ReadAllText(path));
    }

    [Theory]
    [MemberData(nameof(ViewFiles))]
    public void StaticResource_References_Are_All_Defined(string viewPath)
    {
        var appDefined = AppDefinedKeys();
        var xaml = File.ReadAllText(viewPath);
        var defined = ParseDefinedKeys(xaml);
        var used = ParseUsedResources(xaml);
        var viewName = Path.GetFileName(viewPath);

        var missing = used
            .Where(u => !appDefined.Contains(u) && !defined.Contains(u) && !u.StartsWith("Semi", StringComparison.Ordinal))
            .OrderBy(u => u, StringComparer.Ordinal)
            .ToList();

        Assert.True(missing.Count == 0,
            $"{viewName} 引用了未定义的资源(运行时会静默失败导致整页空白): {string.Join(", ", missing)}");
    }

    /// <summary>Views 目录下全部 .axaml(新增视图自动纳入,无需维护清单)。</summary>
    public static TheoryData<string> ViewFiles()
    {
        var data = new TheoryData<string>();
        var dir = Path.Combine(RepositoryRoot(), "src", "McKuro", "Views");
        Assert.True(Directory.Exists(dir), $"Views 目录不存在: {dir}");
        foreach (var file in Directory.GetFiles(dir, "*.axaml").OrderBy(f => f, StringComparer.Ordinal))
        {
            data.Add(file);
        }
        Assert.True(data.Count > 0, "Views 目录下没有任何 .axaml");
        return data;
    }

    [Fact]
    public void RolesView_Has_All_Named_Sections_For_Navigation()
    {
        // 锚点导航(code-behind FindControl)依赖这些命名段;缺失会让导航静默失效
        var path = Path.Combine(RepositoryRoot(), "src", "McKuro", "Views", "RolesView.axaml");
        var xaml = File.ReadAllText(path);
        foreach (var name in new[]
                 {
                     "SecOverview", "SecAttributes", "SecSkills", "SecChains",
                     "SecEchoes", "SecRecommend", "SecTeammates",
                 })
        {
            Assert.Contains($"x:Name=\"{name}\"", xaml, StringComparison.Ordinal);
        }
    }
}
