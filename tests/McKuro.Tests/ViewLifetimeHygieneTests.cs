using System.Text.RegularExpressions;

namespace McKuro.Tests;

/// <summary>
/// View 生命周期守卫:防止「长生命周期对象持有短生命周期 View」的泄漏复现。
/// <para>
/// <b>回归背景</b>:<c>MainWindow</c> 用 <c>&lt;ContentControl Content="{Binding CurrentPage}" /&gt;</c>
/// 切页,每个页面 = 单例 ViewModel + 每次切页重建的 View。若 View 把**捕获 <c>this</c> 的委托**
/// 赋给单例 ViewModel 的公开属性(例如 <c>ConfirmLevelSwitchAsync</c>),单例 VM 就把该 View
/// 及其整棵视觉树钉住,永远等不到 GC —— 表现为「切页内存不断累加」。
/// <para/>
/// 曾真实泄漏:<c>LauncherView</c> 把两个弹窗确认委托注入单例 <c>LauncherViewModel</c>,
/// 且从不解绑;每进一次启动页就泄漏一个 <c>LauncherView</c>。
/// </summary>
/// <remarks>
/// 这是**源码级**断言(与 <see cref="ClassHandlerHygieneTests"/> 同风格):失败信息直接给出
/// 文件与行号,修的人一眼能改;不需要起 Avalonia 运行时。
/// </remarks>
public class ViewLifetimeHygieneTests
{
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

    /// <summary>取 <c>src/McKuro/Views</c> 下所有 .axaml.cs 源码。</summary>
    private static IEnumerable<(string File, string Source)> ViewSources()
    {
        var viewsDir = Path.Combine(RepositoryRoot(), "src", "McKuro", "Views");
        foreach (var file in Directory.EnumerateFiles(viewsDir, "*.axaml.cs", SearchOption.AllDirectories))
        {
            yield return (file, File.ReadAllText(file));
        }
    }

    /// <summary>
    /// 每个 View 都必须有一处「解除注入到单例 VM 的委托」的路径。
    /// <para>
    /// 判定方式:文件里出现 <c>vm.ConfirmXxxAsync = ...</c> 这类**写入单例 VM 委托属性**时,
    /// 必须能在**离开视觉树的处理块**里找到对应的解绑(把该属性置 null,或调用带
    /// Detach/Unhook/Clear/Release 语义的方法)。
    /// </para>
    /// <para>
    /// <b>为什么要求解绑在 detach 块内</b>:解绑只写在某个别的方法里是不够的 ——
    /// 那个方法得在切页时被调用才生效。挂在 <c>DetachedFromVisualTree</c> 上才是
    /// 「切走页面就解绑」的正确语义。
    /// </para>
    /// </summary>
    [Fact]
    public void View_InjectedVmDelegates_MustBeReleasedOnDetach()
    {
        // 匹配 "vm.<Name>Async = " / "vm.<Name> = " 形式的、对 VM 公开委托属性的赋值
        var assignment = new Regex(@"\bvm\.(?<prop>\w+)\s*=", RegexOptions.Compiled);

        var failures = new List<string>();

        foreach (var (file, source) in ViewSources())
        {
            // 去掉注释再判断:注释里出现 "vm.ConfirmXxxAsync" 只是说明文字,不是真的赋值
            var code = StripComments(source);

            var props = assignment.Matches(code)
                .Select(m => m.Groups["prop"].Value)
                .Where(p => p.EndsWith("Async", StringComparison.Ordinal) || p.EndsWith("Callback", StringComparison.Ordinal))
                .Distinct()
                .ToList();

            if (props.Count == 0)
            {
                continue;
            }

            // 取离开视觉树的处理块 —— 解绑必须挂在这里
            var detachBlock = DetachHandlerBody(code);
            if (detachBlock is null)
            {
                failures.Add(
                    $"{Path.GetFileName(file)}: 向单例 VM 注入委托({string.Join(", ", props)}) " +
                    "却没有 DetachedFromVisualTree 处理块 —— 切页时不会解绑,View 被 VM 钉住。");
                continue;
            }

            foreach (var prop in props)
            {
                // 该属性在 detach 块里被置 null,或调用了带解绑语义的方法
                var nulled = new Regex($@"\.{Regex.Escape(prop)}\s*=\s*null", RegexOptions.Compiled)
                    .IsMatch(detachBlock);
                var viaHelper = Regex.IsMatch(detachBlock, @"\b(Detach|Unhook|Clear|Release|Reset)\w*\s*\(", RegexOptions.Compiled);

                if (!nulled && !viaHelper)
                {
                    failures.Add(
                        $"{Path.GetFileName(file)}: 向 vm.{prop} 赋值,但离开视觉树的处理块里没有对应的解绑 —— " +
                        "单例 VM 会钉住捕获 this 的委托,View 及其视觉树无法回收。");
                }
            }
        }

        Assert.True(failures.Count == 0,
            "发现 View→单例 VM 的委托泄漏风险:\n" + string.Join("\n", failures));
    }

    /// <summary>
    /// View 订阅 ViewModel 的 event 时,必须存在成对的解绑。
    /// <para>
    /// 单例 ViewModel(如 <c>ActivityViewModel</c>)的 event 是强引用事件;View 只订阅不解绑,
    /// 单例 VM 会一直持有 View —— 与委托注入是同一类泄漏,只是通道不同。
    /// </para>
    /// </summary>
    [Fact]
    public void View_SubscribedVmEvents_MustBeUnsubscribed()
    {
        // "vm.Event += handler" 形式的订阅
        var subscribe = new Regex(@"\bvm\.(?<evt>\w+)\s*\+=\s*(?<handler>[\w.]+)", RegexOptions.Compiled);

        var failures = new List<string>();

        foreach (var (file, source) in ViewSources())
        {
            // 去掉注释再判断
            var code = StripComments(source);
            foreach (Match m in subscribe.Matches(code))
            {
                var evt = m.Groups["evt"].Value;
                var handler = m.Groups["handler"].Value;
                if (handler.Contains("=>", StringComparison.Ordinal))
                {
                    // 匿名 lambda 订阅:无法用 -= 解绑,除非另有机制(如整体换 VM 时重建)
                    continue;
                }

                var unsub = new Regex($@"\.\s*{Regex.Escape(evt)}\s*-=\s*{Regex.Escape(handler)}\b", RegexOptions.Compiled);
                if (!unsub.IsMatch(code))
                {
                    failures.Add(
                        $"{Path.GetFileName(file)}: 订阅了 vm.{evt} += {handler} 但没有对应的 -= —— " +
                        "单例 ViewModel 会一直持有该 View。");
                }
            }
        }

        Assert.True(failures.Count == 0,
            "发现 View 订阅单例 VM 事件却未解绑:\n" + string.Join("\n", failures));
    }

    /// <summary>
    /// 持有原生资源的控件必须在 View 离开视觉树时被显式释放。
    /// <para>
    /// <c>WebView2Control</c> 用 <c>GCHandle.Alloc(this)</c> 强引用保活自身
    /// (原生 WndProc 与回调链路要引用控件),那个句柄只在 <c>Teardown()</c> 里释放。
    /// Avalonia 的标准控件销毁路径(DetachedFromVisualTree → DestroyNativeControlCore)
    /// 覆盖不到它:若 View 不显式 <c>CloseWebView()</c>,每进一次页面新建的实例都被
    /// GCHandle 钉住,原生子窗口与浏览器进程资源一起泄漏。
    /// </para>
    /// </summary>
    [Fact]
    public void View_CreatingWebView2_MustCloseItOnDetach()
    {
        var failures = new List<string>();

        foreach (var (file, source) in ViewSources())
        {
            // 只针对会创建 WebView2Control 的 View
            if (!source.Contains("new WebView2Control", StringComparison.Ordinal))
            {
                continue;
            }

            // 只管页面级 View(继承 UserControl):顶级弹窗(Window)不是切页重建的,
            // 生命周期由 Closing/Closed 负责,不该按 detach 路径要求。
            if (!source.Contains(": UserControl", StringComparison.Ordinal))
            {
                continue;
            }

            // 必须在 detach 路径上调用 CloseWebView
            var detachBlock = DetachHandlerBody(source);
            if (detachBlock is null)
            {
                failures.Add(
                    $"{Path.GetFileName(file)}: 创建了 WebView2Control 但找不到 DetachedFromVisualTree 处理块 —— " +
                    "离开页面时不会释放,实例被 GCHandle 钉住。");
                continue;
            }

            // 去掉注释再判断:注释里出现 "CloseWebView" 只是说明文字,不是真的调用
            var code = StripComments(detachBlock);
            if (!code.Contains("CloseWebView", StringComparison.Ordinal)
                && !code.Contains("ReleaseEmbedPlayer", StringComparison.Ordinal))
            {
                failures.Add(
                    $"{Path.GetFileName(file)}: 创建了 WebView2Control,但 detach 处理块里没有 CloseWebView —— " +
                    "离开页面时原生窗口与浏览器进程资源泄漏。");
            }
        }

        Assert.True(failures.Count == 0,
            "发现 WebView2 资源未在离开页面时释放:\n" + string.Join("\n", failures));
    }

    /// <summary>去掉 C# 注释(行注释与块注释),只留代码,避免注释里的关键字造成误判。</summary>
    private static string StripComments(string source)
    {
        // 块注释
        var withoutBlock = Regex.Replace(source, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        // 行注释(注意不要误伤字符串里的 //,这里做粗略处理即可)
        return Regex.Replace(withoutBlock, @"(?m)//.*$", " ");
    }

    /// <summary>
    /// 取出 <c>DetachedFromVisualTree += ...</c> 那个 lambda 的方法体(按大括号配平截取)。
    /// 返回 null 表示没找到这样的订阅。
    /// </summary>
    private static string? DetachHandlerBody(string source)
    {
        int idx = source.IndexOf("DetachedFromVisualTree", StringComparison.Ordinal);
        if (idx < 0)
        {
            return null;
        }

        int open = source.IndexOf('{', idx);
        if (open < 0)
        {
            return null;
        }

        int depth = 0;
        for (int i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source[open..(i + 1)];
                }
            }
        }
        return null;
    }
}
