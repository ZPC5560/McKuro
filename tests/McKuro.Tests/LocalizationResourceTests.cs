using System.Text.Json;
using System.Text.RegularExpressions;

namespace McKuro.Tests;

/// <summary>
/// 双语文案资源的一致性守卫(zh-Hans 为源语言,en-US 必须同步)。
/// <para>
/// 回归背景:每次给界面加文案都要同时改两份 JSON,而<b>没有任何测试碰过它们</b> ——
/// 漏一个 key 不会编译报错,而是运行期回退成 key 本身(界面上直接显示 "Roles.Widget.Title"),
/// 占位符不一致更隐蔽:某语言少写 {0} 会显示半截句子或多出字面 "{0}"。
/// 本测试把这三条不变量(键集合相等 / 占位符集合相等 / 值非空)钉住。
/// </para>
/// <para>
/// 直接读仓库源码文件而不是内嵌资源:失败信息能直接指到文件,且不依赖程序集内嵌名的拼写。
/// </para>
/// </summary>
public class LocalizationResourceTests
{
    private static string RepositoryRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && dir is not null; i++)
        {
            if (Directory.Exists(Path.Combine(dir, "src", "McKuro", "Assets", "lang")))
            {
                return dir;
            }
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new DirectoryNotFoundException("未能定位仓库根(src/McKuro/Assets/lang 所在目录)");
    }

    private static Dictionary<string, string> LoadLang(string lang)
    {
        var path = Path.Combine(RepositoryRoot(), "src", "McKuro", "Assets", "lang", $"{lang}.json");
        Assert.True(File.Exists(path), $"语言资源不存在: {path}");
        var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
        Assert.NotNull(parsed);
        return parsed!;
    }

    private static HashSet<string> Placeholders(string value)
        => Regex.Matches(value, @"\{(\d+)\}")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

    [Fact]
    public void Both_Languages_Define_The_Same_Key_Set()
    {
        var zh = LoadLang("zh-Hans");
        var en = LoadLang("en-US");

        var missingInEn = zh.Keys.Except(en.Keys).OrderBy(k => k, StringComparer.Ordinal).ToList();
        var missingInZh = en.Keys.Except(zh.Keys).OrderBy(k => k, StringComparer.Ordinal).ToList();

        Assert.True(missingInEn.Count == 0,
            $"en-US.json 缺少这些 key(运行期会直接显示 key 本身): {string.Join(", ", missingInEn)}");
        Assert.True(missingInZh.Count == 0,
            $"zh-Hans.json 缺少这些 key(en-US 多出来的): {string.Join(", ", missingInZh)}");
    }

    [Fact]
    public void Placeholder_Sets_Match_Across_Languages()
    {
        var zh = LoadLang("zh-Hans");
        var en = LoadLang("en-US");

        var mismatched = new List<string>();
        foreach (var (key, zhValue) in zh)
        {
            if (!en.TryGetValue(key, out var enValue))
            {
                continue; // 键集合差异由另一个测试负责
            }
            var zhSlots = Placeholders(zhValue);
            var enSlots = Placeholders(enValue);
            if (!zhSlots.SetEquals(enSlots))
            {
                mismatched.Add(
                    $"{key} (zh=[{string.Join(",", zhSlots.OrderBy(x => x))}] " +
                    $"en=[{string.Join(",", enSlots.OrderBy(x => x))}])");
            }
        }

        Assert.True(mismatched.Count == 0,
            $"以下 key 的 {{n}} 占位符两种语言不一致(会显示半截句子或多出字面 {{0}}): {string.Join("; ", mismatched)}");
    }

    [Fact]
    public void No_Empty_Values()
    {
        foreach (var lang in new[] { "zh-Hans", "en-US" })
        {
            var strings = LoadLang(lang);
            var empty = strings
                .Where(kv => string.IsNullOrWhiteSpace(kv.Value))
                .Select(kv => kv.Key)
                .OrderBy(k => k, StringComparer.Ordinal)
                .ToList();
            Assert.True(empty.Count == 0, $"{lang}.json 存在空文案: {string.Join(", ", empty)}");
        }
    }

    /// <summary>
    /// 界面上真正引用到的 key 必须存在。
    /// <para>
    /// 前一组的「键集合相等」只能保证两种语言<b>互相</b>一致 —— 两处都漏掉同一个 key 时它是绿的,
    /// 而运行期会直接把 key 本身显示给用户(界面出现 "Tower.Holo.NotChallenged")。
    /// 本组扫描 XAML 的 <c>loc:Localize</c> 与 C# 的 <c>LanguageService.Format/Get</c> 字面量,
    /// 把它们与资源键做差集。
    /// </para>
    /// <para>
    /// <b>为什么用"宽松窗口"而不是紧贴调用的正则</b>:调用点常写成三元表达式
    /// (<c>Format(cond ? "A" : "B")</c>)或跨行(<c>Format(\n "Key", …)</c>),
    /// 紧贴的正则(<c>\(\s*"key"</c>)会整条漏扫 —— 实测就漏掉了
    /// <c>Tower.Holo.TierCleared</c>/<c>TierNotCleared</c> 两个 key。
    /// 故改为:以调用点为锚,取其后一段窗口内的<b>全部</b>字符串字面量。
    /// 代价是窗口内非 key 的字符串会进入差集 → 用"看着像本地化 key"的形状(含点、全 ASCII、
    /// 首段大写驼峰)过滤掉,漏扫优于误报的原则不变。
    /// </para>
    /// <para>
    /// 只扫字面量:插值/变量键(如按状态拼后缀)无法静态求值。
    /// <b>不扫 <c>CoreStrings</c></b> —— Core 层网关约定带中文兜底原文(见 README「CoreStrings 网关」),
    /// 未登记进语言文件是设计如此,不属于缺陷。
    /// </para>
    /// </summary>
    [Fact]
    public void Referenced_Keys_Exist_In_Resources()
    {
        var keys = LoadLang("zh-Hans").Keys.ToHashSet(StringComparer.Ordinal);
        var missing = new List<string>();
        var scanned = 0;

        var xamlDir = Path.Combine(RepositoryRoot(), "src", "McKuro");
        foreach (var file in Directory.EnumerateFiles(xamlDir, "*.axaml", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            foreach (Match m in Regex.Matches(text, @"loc:Localize\s+Key=([A-Za-z0-9_.]+)"))
            {
                scanned++;
                if (!keys.Contains(m.Groups[1].Value))
                {
                    missing.Add($"{m.Groups[1].Value} (XAML {Path.GetFileName(file)})");
                }
            }
        }

        foreach (var project in new[] { "McKuro", "McKuro.Core" })
        {
            var dir = Path.Combine(RepositoryRoot(), "src", project);
            if (!Directory.Exists(dir))
            {
                continue;
            }
            foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                {
                    continue;
                }
                var text = File.ReadAllText(file);
                // 与 Key_Scanner_Covers_Ternary_And_Multiline_Calls 共用同一套抽取逻辑,
                // 保证那条测试真的在守护这里的扫描行为
                foreach (var key in ExtractKeysFromCSharp(text))
                {
                    scanned++;
                    if (!keys.Contains(key))
                    {
                        missing.Add($"{key} (C# {Path.GetFileName(file)})");
                    }
                }
            }
        }

        // 防空转:扫描本身若因路径/正则失效而一条都没扫到,断言会"全绿"却毫无守护作用。
        Assert.True(scanned > 100,
            $"只扫描到 {scanned} 处 key 引用,远低于预期 —— 扫描路径或正则很可能已失效(本测试会假绿)");

        Assert.True(missing.Count == 0,
            $"以下 key 被界面/代码引用但资源里不存在(运行期会直接显示 key 本身): {string.Join(", ", missing.Distinct().OrderBy(x => x, StringComparer.Ordinal))}");
    }

    /// <summary>
    /// 扫描器必须能覆盖<b>三元表达式</b>与<b>跨行</b>两种写法。
    /// <para>
    /// 回归:初版正则 <c>\(\s*"key"</c> 紧贴调用点,实测漏掉
    /// <c>Format(cleared ? "Tower.Holo.TierCleared" : "Tower.Holo.TierNotCleared")</c>
    /// 与跨行的 <c>Format(\n "Tower.StatusLoaded", …)</c> —— 两个 key 都处于守卫盲区。
    /// 本测试直接喂典型片段给同一套抽取逻辑,钉住"不再漏扫"。
    /// </para>
    /// </summary>
    [Fact]
    public void Key_Scanner_Covers_Ternary_And_Multiline_Calls()
    {
        const string snippet = """
            var a = LanguageService.Format(cleared ? "Tower.Holo.TierCleared" : "Tower.Holo.TierNotCleared");
            var b = LanguageService.Format(
                "Tower.StatusLoaded", x, y);
            var c = LanguageService.Get("Nav.Home");
            """;

        var found = ExtractKeysFromCSharp(snippet);

        Assert.Contains("Tower.Holo.TierCleared", found);
        Assert.Contains("Tower.Holo.TierNotCleared", found);
        Assert.Contains("Tower.StatusLoaded", found);
        Assert.Contains("Nav.Home", found);
    }

    /// <summary>
    /// 扫描器不得把<b>文档注释</b>与<b>相邻语句的字面量</b>当成 key。
    /// <para>
    /// 回归:初版用"调用点后固定 240 字符窗口",实测溢出到隔壁参数与 XML 文档注释,误报出
    /// <c>DownloadProgress.CanPause</c>(来自 &lt;see cref="…"/&gt;)、<c>waveplates.png</c>(相邻命名参数)、
    /// <c>yyyy.MM.dd</c>(日期格式)等一堆假 key。现改为去注释 + 括号配对截取实参列表。
    /// </para>
    /// </summary>
    [Fact]
    public void Key_Scanner_Ignores_Comments_And_Sibling_Arguments()
    {
        const string snippet = """
            /// <summary>按 <see cref="DownloadProgress.CanPause"/> 决定按钮。</summary>
            var item = AddItem(data, LanguageService.Format("Home.ItemEnergy"),
                iconFile: "waveplates.png", recoverMinutes: 6);
            var stamp = LanguageService.Format("Nav.Home");
            var date = someDate.ToString("yyyy.MM.dd");
            """;

        var found = ExtractKeysFromCSharp(snippet);

        Assert.Contains("Home.ItemEnergy", found);
        Assert.Contains("Nav.Home", found);
        // 假 key 一个都不该进来
        Assert.DoesNotContain("DownloadProgress.CanPause", found);
        Assert.DoesNotContain("waveplates.png", found);
        Assert.DoesNotContain("yyyy.MM.dd", found);
    }

    /// <summary>
    /// 嵌套调用里的字符串是"参数值"而不是 key,不得被收进来。
    /// <para>回归:<c>Format("Tower.RecordBefore", d.ToString("yyyy.MM.dd"))</c> 里的日期格式串
    /// 曾被当成 key 报缺失。</para>
    /// </summary>
    [Fact]
    public void Key_Scanner_Ignores_Nested_Call_Arguments()
    {
        const string snippet = """
            var label = LanguageService.Format("Tower.RecordBefore", endLocal.ToString("yyyy.MM.dd"));
            var msg = LanguageService.Format("Account.KuroSessionExpired", gamer?.Msg ?? $"code={gamer?.Code}");
            """;

        var found = ExtractKeysFromCSharp(snippet);

        Assert.Contains("Tower.RecordBefore", found);
        Assert.Contains("Account.KuroSessionExpired", found);
        Assert.DoesNotContain("yyyy.MM.dd", found);
        // 插值串不是 key
        Assert.DoesNotContain("code={gamer?.Code}", found);
    }

    /// <summary>
    /// 从 C# 源码抽取本地化 key 字面量(与主守卫同一套逻辑)。
    /// <para>
    /// 三步走:① 去掉注释(否则 <c>/// &lt;see cref="A.B"/&gt;</c> 这类文档引用会被误当 key,
    /// 实测就是这样混进过 <c>DownloadProgress.CanPause</c>);
    /// ② 从调用点出发按<b>括号配对</b>截出实参列表(固定长度窗口会溢出到相邻语句);
    /// ③ 只取实参列表里<b>括号深度为 0</b> 的字符串字面量 —— key 永远是 Format/Get 的直接实参,
    /// 而嵌套调用里的字符串是"参数值"而非 key(实测会误收
    /// <c>Format("Tower.RecordBefore", d.ToString("yyyy.MM.dd"))</c> 里的日期格式串)。
    /// 三元表达式 <c>Format(cond ? "A" : "B")</c> 不带括号,仍为深度 0,故照常覆盖。
    /// </para>
    /// </summary>
    private static HashSet<string> ExtractKeysFromCSharp(string text)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        var code = StripCSharpComments(text);
        foreach (Match call in Regex.Matches(code, @"LanguageService\.(?:Format|Get)\("))
        {
            var args = BalancedArgs(code, call.Index + call.Length - 1);
            foreach (var literal in TopLevelStringLiterals(args))
            {
                if (LooksLikeResourceKey(literal))
                {
                    found.Add(literal);
                }
            }
        }
        return found;
    }

    /// <summary>取出文本里括号深度为 0 的字符串字面量(跳过嵌套调用、字符字面量与转义)。</summary>
    private static List<string> TopLevelStringLiterals(string text)
    {
        var result = new List<string>();
        var depth = 0;
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '(' || c == '[' || c == '{')
            {
                depth++;
                i++;
                continue;
            }
            if (c == ')' || c == ']' || c == '}')
            {
                depth--;
                i++;
                continue;
            }
            if (c == '\'')
            {
                // 字符字面量:跳过内容(可能含转义),不参与取值
                i++;
                while (i < text.Length && text[i] != '\'')
                {
                    i += text[i] == '\\' ? 2 : 1;
                }
                i++;
                continue;
            }
            if (c == '@' && i + 1 < text.Length && text[i + 1] == '"')
            {
                var (value, next) = ReadStringLiteral(text, i + 1, verbatim: true);
                if (depth == 0)
                {
                    result.Add(value);
                }
                i = next;
                continue;
            }
            // 插值字符串($"…")永远不是资源 key:它的内容是拼给用户看的消息体,
            // 实测会把 `$"code={gamer?.Code}"` 这类片段误当 key。整段跳过。
            if (c == '$')
            {
                var j = i + 1;
                if (j < text.Length && text[j] == '@')
                {
                    j++;
                }
                if (j < text.Length && text[j] == '"')
                {
                    // 插值串里可能含 {…} 表达式,直接扫到收尾引号
                    var end = j + 1;
                    while (end < text.Length && text[end] != '"')
                    {
                        end += text[end] == '\\' ? 2 : 1;
                    }
                    i = end + 1;
                    continue;
                }
                i++;
                continue;
            }
            if (c == '@' && i + 1 < text.Length && text[i + 1] == '$')
            {
                var j = i + 2;
                if (j < text.Length && text[j] == '"')
                {
                    var end = j + 1;
                    while (end < text.Length && text[end] != '"')
                    {
                        end += text[end] == '"' && end + 1 < text.Length && text[end + 1] == '"' ? 2 : 1;
                    }
                    i = end + 1;
                    continue;
                }
                i++;
                continue;
            }
            if (c == '"')
            {
                var (value, next) = ReadStringLiteral(text, i, verbatim: false);
                if (depth == 0)
                {
                    result.Add(value);
                }
                i = next;
                continue;
            }
            i++;
        }
        return result;
    }

    /// <summary>读一个字符串字面量(从开引号起),返回值与结束后的下标。</summary>
    private static (string Value, int Next) ReadStringLiteral(string text, int openQuote, bool verbatim)
    {
        var sb = new System.Text.StringBuilder();
        var i = openQuote + 1;
        while (i < text.Length)
        {
            var c = text[i];
            if (verbatim)
            {
                if (c == '"')
                {
                    // 逐字字符串里 "" 表示一个引号
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        sb.Append('"');
                        i += 2;
                        continue;
                    }
                    return (sb.ToString(), i + 1);
                }
                sb.Append(c);
                i++;
                continue;
            }
            if (c == '\\')
            {
                i += 2;
                continue;
            }
            if (c == '"')
            {
                return (sb.ToString(), i + 1);
            }
            sb.Append(c);
            i++;
        }
        return (sb.ToString(), text.Length);
    }

    /// <summary>去掉 // 行注释与 /* */ 块注释(字符串内的 // 不误判:只在引号外生效)。</summary>
    private static string StripCSharpComments(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        var inString = false;
        var inChar = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                sb.Append(c);
                if (c == '\\' && i + 1 < text.Length)
                {
                    sb.Append(text[++i]);
                }
                else if (c == '"')
                {
                    inString = false;
                }
                continue;
            }
            if (inChar)
            {
                sb.Append(c);
                if (c == '\\' && i + 1 < text.Length)
                {
                    sb.Append(text[++i]);
                }
                else if (c == '\'')
                {
                    inChar = false;
                }
                continue;
            }
            if (c == '"')
            {
                inString = true;
                sb.Append(c);
                continue;
            }
            if (c == '\'')
            {
                inChar = true;
                sb.Append(c);
                continue;
            }
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                while (i < text.Length && text[i] != '\n')
                {
                    i++;
                }
                sb.Append('\n');
                continue;
            }
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < text.Length && !(text[i] == '*' && text[i + 1] == '/'))
                {
                    i++;
                }
                i++; // 跳过结尾的 '/'
                sb.Append(' ');
                continue;
            }
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// 从 <paramref name="openParen"/> 处的 '(' 起,返回配对括号<b>内部</b>的文本
    /// (已跳过字符串/字符字面量与嵌套括号,故不会溢出到相邻语句)。
    /// </summary>
    private static string BalancedArgs(string code, int openParen)
    {
        if (openParen < 0 || openParen >= code.Length || code[openParen] != '(')
        {
            return "";
        }
        var depth = 0;
        var inString = false;
        var inChar = false;
        var start = openParen + 1;
        for (var i = openParen; i < code.Length; i++)
        {
            var c = code[i];
            if (inString)
            {
                if (c == '\\')
                {
                    i++;
                }
                else if (c == '"')
                {
                    inString = false;
                }
                continue;
            }
            if (inChar)
            {
                if (c == '\\')
                {
                    i++;
                }
                else if (c == '\'')
                {
                    inChar = false;
                }
                continue;
            }
            switch (c)
            {
                case '"':
                    inString = true;
                    break;
                case '\'':
                    inChar = true;
                    break;
                case '(':
                    depth++;
                    break;
                case ')':
                    depth--;
                    if (depth == 0)
                    {
                        return code[start..i];
                    }
                    break;
                default:
                    break;
            }
        }
        return "";
    }

    /// <summary>字面量是否"像本地化 key"(含点 + 每段以字母开头);用于过滤参数里的非 key 字符串。</summary>
    private static bool LooksLikeResourceKey(string value)
    {
        if (!value.Contains('.', StringComparison.Ordinal))
        {
            return false;
        }
        var segments = value.Split('.');
        return segments.All(s => s.Length > 0 && char.IsAsciiLetter(s[0]));
    }
}
