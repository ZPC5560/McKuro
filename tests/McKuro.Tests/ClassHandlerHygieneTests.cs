using System.Text.RegularExpressions;

namespace McKuro.Tests;

/// <summary>
/// 订阅卫生守卫:<c>AvaloniaProperty.Changed.AddClassHandler</c> 是**类型级**静态事件,
/// 必须在静态构造函数里挂且只挂一次。
/// <para>
/// <b>回归背景</b>:<c>WebView2Control</c> / <c>WkWebViewControl</c> / <c>VideoBackgroundControl</c>
/// 曾把 <c>AddClassHandler</c> 写在实例构造函数里,而返回的 IDisposable 从不保存 ——
/// 每 new 一个控件就永久多一个处理器(闭包捕获 this,旧控件也收不掉)。
/// 这与 Haiyu 1.4.6 修的 WebView2 匿名事件泄漏是同一类问题,但更隐蔽:它不报错、不泄漏句柄,
/// 只是让每次页面重建都多一点开销与更长的对象寿命。
/// </para>
/// <para>
/// 这个测试是**源码级**断言(不依赖运行时反射):它把「实例构造里出现 AddClassHandler」判为失败。
/// 之所以用源码而不是 IL:失败信息直接给出文件与行号,修的人一眼能改。
/// </para>
/// </summary>
public class ClassHandlerHygieneTests
{
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

    /// <summary>按顶层大括号切出各个成员块(返回 成员头 + 成员体),用于区分静态构造与实例构造。</summary>
    internal static IEnumerable<(string Header, string Body, int Line)> TypeMemberBlocks(string source)
    {
        var lines = source.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();

            // 只关心构造函数:含 "类名(" 且以 { 结尾的成员头;static 版本单独识别
            var isCtor = Regex.IsMatch(trimmed, @"^(static\s+)?[A-Z]\w*\s*\(\s*\)\s*$")
                || Regex.IsMatch(trimmed, @"^(static\s+)?[A-Z]\w*\s*\([^)]*\)\s*$");
            if (!isCtor)
            {
                continue;
            }

            // 收集体:从当前行的 { 开始数括号,直到配平
            var builder = new System.Text.StringBuilder();
            var depth = 0;
            var started = false;
            var j = i;
            for (; j < lines.Length; j++)
            {
                builder.AppendLine(lines[j]);
                foreach (var ch in lines[j])
                {
                    if (ch == '{') { depth++; started = true; }
                    else if (ch == '}') { depth--; }
                }
                if (started && depth == 0)
                {
                    break;
                }
            }

            yield return (trimmed, builder.ToString(), i + 1);
            i = j;
        }
    }

    [Fact]
    public void AddClassHandler_Only_Appears_In_Static_Constructors()
    {
        var root = Path.Combine(RepositoryRoot(), "src", "McKuro");
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            {
                continue;
            }

            var source = File.ReadAllText(file);
            if (!source.Contains("AddClassHandler", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var (header, body, line) in TypeMemberBlocks(source))
            {
                if (!body.Contains("AddClassHandler", StringComparison.Ordinal))
                {
                    continue;
                }
                // 静态构造函数(statically 一次)是唯一允许的位置
                if (!header.StartsWith("static", StringComparison.Ordinal))
                {
                    offenders.Add($"{Path.GetRelativePath(root, file)}:{line}  {header}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "AddClassHandler 只能写在静态构造函数里(类型级事件,实例构造里挂会随实例数累积不解除的处理器):\n  "
            + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// 控件订阅外部集合(常驻 ViewModel 持有)的 CollectionChanged 不得用匿名 lambda:
    /// lambda 无法退订,集合会把每一代控件实例都钉住(TimeLineChart 曾因此泄漏,见 SpeedTrendChart 的具名写法)。
    /// </summary>
    [Fact]
    public void Controls_Do_Not_Subscribe_CollectionChanged_With_Lambdas()
    {
        var root = Path.Combine(RepositoryRoot(), "src", "McKuro", "Controls");
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (Regex.IsMatch(lines[i], @"CollectionChanged\s*\+=\s*(\(|delegate|\w+\s*=>)"))
                {
                    offenders.Add($"{Path.GetFileName(file)}:{i + 1}  {lines[i].Trim()}");
                }
            }
        }
        Assert.True(offenders.Count == 0,
            "控件订阅 CollectionChanged 必须用具名处理器并在换集合/脱离视觉树时退订:\n  " + string.Join("\n  ", offenders));
    }
}
