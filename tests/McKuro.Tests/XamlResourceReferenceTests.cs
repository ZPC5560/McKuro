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
    /// <summary>
    /// 去掉 XAML 注释后再解析。
    /// <para>
    /// 必须去注释(评审反馈,且已在真实文件上验证):注释里的文字会被两个解析器当成真代码 ——
    /// <c>App.axaml</c> 有一段解释「原先用 ExperimentalAcrylicBorder + DynamicResource Material…」
    /// 的历史注释,包含该关键字,于是引用解析把它当成"引用了未定义的 Material"而误红;
    /// 反向也成立:被注释掉的 <c>x:Key="X"</c> 会被当作已定义,从而<em>漏掉</em>真正的未定义引用。
    /// </para>
    /// </summary>
    private static string StripComments(string xaml)
        => Regex.Replace(xaml, "<!--.*?-->", " ", RegexOptions.Singleline);

    /// <summary>XAML 里 xx:Key / x:Key 定义的资源名。</summary>
    private static HashSet<string> ParseDefinedKeys(string xaml)
        => Regex.Matches(StripComments(xaml), @"(?:x|xc|vm|roles|guide):Key=""([^""]+)""")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>XAML 里 StaticResource / DynamicResource 引用的资源名。</summary>
    private static HashSet<string> ParseUsedResources(string xaml)
        => Regex.Matches(StripComments(xaml), @"(?:Static|Dynamic)Resource\s+([A-Za-z0-9_]+)")
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

    /// <summary>
    /// 全部引用了资源的 .axaml:<b>Views 目录 + 应用外壳</b>(MainWindow/App 就在 Views 之外)。
    /// <para>
    /// 此前只扫 Views —— 而 <c>src/McKuro/MainWindow.axaml</c> 自身有约 25 处 {DynamicResource …},
    /// 且它是应用外壳(坏了整窗都起不来),恰好是当初 RolesView StringIsEmpty 那类
    /// "运行期才炸、编译期不报"缺陷最容易漏掉的盲区。
    /// </para>
    /// </summary>
    public static TheoryData<string> ViewFiles()
    {
        var data = new TheoryData<string>();
        var appRoot = Path.Combine(RepositoryRoot(), "src", "McKuro");
        var viewsDir = Path.Combine(appRoot, "Views");
        Assert.True(Directory.Exists(viewsDir), $"Views 目录不存在: {viewsDir}");

        var files = Directory.GetFiles(viewsDir, "*.axaml").ToList();
        // 应用外壳(与 Views 平级):MainWindow.axaml / App.axaml
        foreach (var shell in new[] { "MainWindow.axaml", "App.axaml" })
        {
            var path = Path.Combine(appRoot, shell);
            Assert.True(File.Exists(path), $"{shell} 不存在: {path}");
            files.Add(path);
        }

        foreach (var file in files.OrderBy(f => f, StringComparer.Ordinal))
        {
            data.Add(file);
        }
        Assert.True(data.Count > 0, "没有任何 .axaml 被纳入扫描");
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

    /// <summary>
    /// 「压在视频上的文字」前景令牌必须<b>只定义一次、且定义在 ThemeDictionaries 之外</b>:
    /// 它表达的是物理场景(视频明暗随镜头变化),与用户在设置里选的明暗主题无关。
    /// 一旦被挪进某个主题字典,另一主题下就退化成"深色字压黑视频"完全看不见
    /// (2026-10 用户反馈:资源分级切换按键与轮播图关闭按键在亮色主题下消失)。
    /// </summary>
    [Fact]
    public void VideoTextToken_Is_ThemeIndependent_And_DefinedOnce()
    {
        var path = Path.Combine(RepositoryRoot(), "src", "McKuro", "App.axaml");
        var xaml = File.ReadAllText(path);
        const string key = @"x:Key=""McKuroTextOnVideo""";

        Assert.Equal(1, Regex.Matches(xaml, Regex.Escape(key)).Count);

        var themeEnd = xaml.IndexOf("</ResourceDictionary.ThemeDictionaries>", StringComparison.Ordinal);
        Assert.True(themeEnd >= 0, "App.axaml 结构变化:找不到 ThemeDictionaries 的结束标记");
        Assert.True(xaml.IndexOf(key, StringComparison.Ordinal) > themeEnd,
            "McKuroTextOnVideo 必须定义在 ThemeDictionaries 之外,否则它会随主题翻转而失去意义");
    }

    /// <summary>
    /// 浮层面板材质必须走共享的 <c>Border.mcPanel</c>,不许再内联重述一遍面板底色。
    /// <para>
    /// 根因:启动页功能菜单曾是「两层不透明底叠加」、资源等级面板是「单层 DialogSurface」、
    /// 两个弹窗又各写一份 —— 同一层级的东西长出四种材质(2026-10 用户反馈"展开后的材质不统一")。
    /// 收敛成一处后,这条测试保证以后新增面板不会又悄悄自己写一份。
    /// </para>
    /// </summary>
    [Fact]
    public void Overlay_Panels_Must_Use_Shared_mcPanel_Class()
    {
        var dir = Path.Combine(RepositoryRoot(), "src", "McKuro", "Views");
        const string inlineSurface = @"Background=""{DynamicResource McKuroDialogSurface}""";

        var offenders = new List<string>();
        foreach (var file in Directory.GetFiles(dir, "*.axaml"))
        {
            if (File.ReadAllText(file).Contains(inlineSurface, StringComparison.Ordinal))
            {
                offenders.Add(Path.GetFileName(file));
            }
        }

        Assert.True(offenders.Count == 0,
            $"这些视图内联重述了面板底色,应改用 Classes=\"mcPanel\": {string.Join(", ", offenders)}");

        // 共享样式必须存在:否则 Classes="mcPanel" 会静默不生效,面板变成无底透明块
        var app = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "McKuro", "App.axaml"));
        Assert.Contains(@"Selector=""Border.mcPanel""", app, StringComparison.Ordinal);
    }
}
