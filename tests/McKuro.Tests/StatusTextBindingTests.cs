using System.Text.RegularExpressions;

namespace McKuro.Tests;

/// <summary>
/// 「ViewModel 写了状态文案,但页面没绑定」的静态守卫。
/// <para>
/// 回归背景(用户反馈 2026-10):启动页手动点「检查重复文件」后"没有进度也没有反馈"。
/// 根因不是逻辑没跑,而是 <c>LauncherView.axaml</c> 里<b>根本没有绑定 StatusText</b> ——
/// ViewModel 里 20 多处 <c>StatusText = ...</c> 全写进了没人显示的地方。
/// 这类缺陷编译不报错、单测也测不到(VM 状态是对的,只是没人看),只能在 XAML 层守。
/// </para>
/// <para>
/// 规则:凡是 ViewModel 里<b>确实会赋值</b> <c>StatusText</c> 的页面,它的 .axaml 必须绑定 StatusText。
/// 反过来,完全不写 StatusText 的 VM 不要求绑定(避免给无状态页面加死绑定)。
/// </para>
/// </summary>
public class StatusTextBindingTests
{
    /// <summary>
    /// 定位仓库根。
    /// <para>
    /// <b>必须同时校验 ViewModels 与 Views 两个目录</b>:只校验其中一个时,若向上第 N 层
    /// 恰好有同名目录(bin 输出目录里的映射目录),会静默返回错误根 —— 后续 <c>GetFiles</c>
    /// 拿到空集合,守卫测试就"永远通过",变成假测试(本文件初版正是这样漏掉的)。
    /// </para>
    /// </summary>
    private static string RepositoryRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 10 && dir is not null; i++)
        {
            if (Directory.Exists(Path.Combine(dir, "src", "McKuro", "ViewModels"))
                && Directory.Exists(Path.Combine(dir, "src", "McKuro", "Views")))
            {
                return dir;
            }
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new DirectoryNotFoundException(
            $"未能定位仓库根(需要 src/McKuro/ViewModels 与 src/McKuro/Views)。起点: {AppContext.BaseDirectory}");
    }

    /// <summary>ViewModel 源码里是否有对 StatusText 的实际赋值。</summary>
    private static bool AssignsStatusText(string vmSource)
        => Regex.IsMatch(vmSource, @"\bStatusText\s*=");

    /// <summary>
    /// XAML 是否<b>把 StatusText 显示出来</b>。
    /// <para>
    /// 判据必须是 <c>Text="{Binding StatusText…}"</c>,不能只判"出现过 {Binding StatusText" ——
    /// <c>IsVisible="{Binding StatusText, Converter=…}"</c> 也含同样的片段,但它只是控制显隐,
    /// 文案本身仍然没人显示。初版就是这么被骗过的:把 Text 绑定删掉后守卫依旧全绿。
    /// </para>
    /// </summary>
    private static bool BindsStatusText(string xamlSource)
        => Regex.IsMatch(xamlSource, @"Text\s*=\s*""\{Binding\s+StatusText\b");

    [Fact]
    public void PagesWhoseViewModelWritesStatusText_MustBindIt()
    {
        var root = RepositoryRoot();
        var viewsDir = Path.Combine(root, "src", "McKuro", "Views");
        var vmDir = Path.Combine(root, "src", "McKuro", "ViewModels");

        Assert.True(Directory.Exists(viewsDir), $"未找到 Views 目录: {viewsDir}");
        Assert.True(Directory.Exists(vmDir), $"未找到 ViewModels 目录: {vmDir}");

        // 启动页的 ViewModel 由三个 partial 文件组成,合并读取后再判断"是否写 StatusText"
        string ReadVm(string viewName)
        {
            var parts = Directory.GetFiles(vmDir, viewName + "*.cs");
            Assert.True(parts.Length > 0, $"未找到 {viewName} 的 ViewModel 源文件");
            return string.Concat(parts.Select(File.ReadAllText));
        }

        var violations = new List<string>();
        foreach (var xaml in Directory.GetFiles(viewsDir, "*.axaml"))
        {
            var viewName = Path.GetFileNameWithoutExtension(xaml);
            // 只检查「有同名 ViewModel」的页面(View 与 VM 同前缀是本项目约定)
            var vmSources = Directory.GetFiles(vmDir, viewName + ".cs");
            if (vmSources.Length == 0)
            {
                continue;
            }

            var vmSource = ReadVm(viewName);
            if (!AssignsStatusText(vmSource))
            {
                continue;
            }

            if (!BindsStatusText(File.ReadAllText(xaml)))
            {
                violations.Add(
                    $"{Path.GetFileName(xaml)}: {viewName}ViewModel 会给 StatusText 赋值,但本页没有绑定 {{Binding StatusText}} —— 这些提示用户永远看不到");
            }
        }

        Assert.True(violations.Count == 0,
            "以下页面存在「状态文案写了但没人显示」的缺陷:\n  " + string.Join("\n  ", violations));
    }

    /// <summary>
    /// 反向守卫:LauncherView 必须真的绑定 StatusText(直接钉子,不依赖上面的通用扫描)。
    /// 启动页是用户反馈的原发地,单独钉死,避免将来有人"重构掉"这个绑定。
    /// </summary>
    [Fact]
    public void LauncherView_BindsStatusText()
    {
        var root = RepositoryRoot();
        var xaml = Path.Combine(root, "src", "McKuro", "Views", "LauncherView.axaml");
        Assert.True(File.Exists(xaml), $"未找到 {xaml}");
        Assert.True(BindsStatusText(File.ReadAllText(xaml)),
            "LauncherView.axaml 必须绑定 StatusText:启动页所有菜单操作的反馈都靠它(2026-10 用户反馈回归点)");
    }

    /// <summary>
    /// 状态文案的初始值不能是静态占位词(「就绪」)。
    /// <para>
    /// 回归背景(用户反馈 2026-10):"一直显示就绪,这种没必要显示"。启动页 / 角色页 / 抽卡页
    /// 的 <c>StatusText</c> 初始值都是 <c>LanguageService.Format("Roles.Ready")</c>,
    /// 于是没有任何操作发生时页面也挂着一个"就绪" —— 它不传达信息,还让人以为有事没做完。
    /// 这些页面的 StatusText 只在"操作开始/结束"时被赋值,初始值应当为空。
    /// </para>
    /// <para>
    /// 判据只盯<b>字段声明处的初始值</b>,不限制运行时赋任何值 —— 运行时要显示什么由各页面自己决定。
    /// </para>
    /// </summary>
    [Fact]
    public void StatusText_Initializers_Are_Not_Static_Placeholders()
    {
        var root = RepositoryRoot();
        var vmDir = Path.Combine(root, "src", "McKuro", "ViewModels");
        Assert.True(Directory.Exists(vmDir), $"未找到 {vmDir}");

        var violations = new List<string>();
        foreach (var vm in Directory.GetFiles(vmDir, "*.cs", SearchOption.AllDirectories))
        {
            var source = File.ReadAllText(vm);
            foreach (Match m in Regex.Matches(source, @"private\s+string\s+_statusText\s*=\s*([^;]+);"))
            {
                if (m.Groups[1].Value.Contains("Roles.Ready", StringComparison.Ordinal))
                {
                    violations.Add(
                        $"{Path.GetFileName(vm)}: _statusText 初始值是 Roles.Ready(「就绪」) —— 静态占位词不该常驻,初始应为空串");
                }
            }
        }

        Assert.True(violations.Count == 0,
            "以下 ViewModel 的状态文案初始值仍是占位词:\n  " + string.Join("\n  ", violations));
    }
}
