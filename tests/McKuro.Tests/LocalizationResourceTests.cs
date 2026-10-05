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
}
