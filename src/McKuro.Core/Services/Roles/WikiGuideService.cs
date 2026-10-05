using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace McKuro.Core.Services.Roles;

/// <summary>
/// 鸣潮 Wiki(wiki.kurobbs.com)角色攻略抓取:取「声骸词条」优先级文本,
/// 派生<b>每角色</b>的词条权重表,供声骸评级使用。
/// <para>
/// 链路(均免登录,实测 2026-10):① <c>catalogue/item/getPage</c>(catalogueId=1384
/// 「角色攻略」目录)列出各角色攻略卡(name + entryId);② <c>catalogue/item/getEntryDetail</c>
/// (id=entryId)返回攻略文档(内嵌 HTML 表格),其中「声骸词条」行形如
/// 「暴击=暴击伤害＞攻击&gt;共鸣技能&gt; 共鸣效率(推荐效率120%以上)」。
/// </para>
/// <para>
/// 解析:按 ＞/&gt; 分组、=/＝ 并列,组序赋予递减权重(2.0/1.0/0.75/0.5/…),
/// 词条别名(如「共鸣技能」→ 共鸣技能伤害加成、「攻击」→ 攻击%+固定攻击)归一到
/// 标准属性名。联名角色/无攻略/解析失败 → null,调用方回退通用权重表。
/// </para>
/// </summary>
public static class WikiGuideService
{
    private static readonly HttpClient Http = new();
    private const string PageUrl = "https://api.kurobbs.com/wiki/core/catalogue/item/getPage";
    private const string DetailUrl = "https://api.kurobbs.com/wiki/core/catalogue/item/getEntryDetail";
    /// <summary>攻略合集 → 角色攻略 目录(wiki.kurobbs.com/mc/catalogue/list?fid=1322&sid=1384)。</summary>
    private const string RoleGuideCatalogueId = "1384";

    /// <summary>组序 → 权重(首组 2.0;并列组共用同权重)。</summary>
    private static readonly double[] RankWeights = { 2.0, 1.0, 0.75, 0.5, 0.4, 0.35, 0.3 };

    /// <summary>按角色名缓存(含 null = 已确认无攻略),避免每次切换角色重复请求。</summary>
    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>?> Cache =
        new System.Collections.Concurrent.ConcurrentDictionary<string, IReadOnlyDictionary<string, double>?>(StringComparer.Ordinal);

    private static readonly object CacheLock = new();
    private static readonly Dictionary<string, Task<IReadOnlyDictionary<string, double>?>> InFlight = new(StringComparer.Ordinal);

    /// <summary>取角色词条权重(角色名 → 权重表);无攻略/失败返回 null。结果按角色名缓存。</summary>
    public static Task<IReadOnlyDictionary<string, double>?> GetPriorityWeightsAsync(string roleName)
    {
        lock (CacheLock)
        {
            if (Cache.TryGetValue(roleName, out var cached))
            {
                return Task.FromResult(cached);
            }
            if (InFlight.TryGetValue(roleName, out var inFlight))
            {
                return inFlight;
            }
            var task = FetchPriorityWeightsAsync(roleName);
            InFlight[roleName] = task;
            task.ContinueWith(t =>
            {
                lock (CacheLock)
                {
                    InFlight.Remove(roleName);
                    ((System.Collections.Concurrent.ConcurrentDictionary<string, IReadOnlyDictionary<string, double>?>)Cache)[roleName] =
                        t.IsCompletedSuccessfully ? t.Result : null;
                }
            });
            return task;
        }
    }

    private static async Task<IReadOnlyDictionary<string, double>?> FetchPriorityWeightsAsync(string roleName)
    {
        try
        {
            var entryId = await FindRoleEntryIdAsync(roleName).ConfigureAwait(false);
            if (entryId is null)
            {
                return null;
            }
            var html = await FetchEntryDetailAsync(entryId).ConfigureAwait(false);
            var section = ExtractAffixPrioritySection(html);
            return ParsePriorityWeights(section);
        }
        catch (Exception)
        {
            return null; // 网络异常/结构变化:静默回退通用权重
        }
    }

    /// <summary>在「角色攻略」目录中找角色的攻略条目 id(取第一个同名条目)。</summary>
    private static async Task<string?> FindRoleEntryIdAsync(string roleName)
    {
        var json = await PostFormAsync(PageUrl, $"catalogueId={RoleGuideCatalogueId}&page=1&limit=60").ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out var data))
        {
            return null;
        }
        var records = data.GetProperty("results").GetProperty("records");
        string? fallback = null;
        foreach (var rec in records.EnumerateArray())
        {
            var name = rec.TryGetProperty("name", out var n) ? n.GetString() : null;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }
            // 条目 id:优先 entryId 字段,链接型卡片从 content 里挖
            var entryId = rec.TryGetProperty("entryId", out var eid) ? eid.GetString() : null;
            if (string.IsNullOrWhiteSpace(entryId))
            {
                var raw = rec.GetRawText();
                var m = Regex.Match(raw, @"""entryId"":""(\d+)""");
                entryId = m.Success ? m.Groups[1].Value : null;
            }
            if (string.IsNullOrWhiteSpace(entryId))
            {
                continue;
            }
            if (string.Equals(name, roleName, StringComparison.Ordinal))
            {
                return entryId;
            }
            // 名称带形态后缀(漂泊者-导电):角色名前缀命中时先记下,精确命中优先
            if (fallback is null && name!.StartsWith(roleName, StringComparison.Ordinal))
            {
                fallback = entryId;
            }
        }
        return fallback;
    }

    private static async Task<string> FetchEntryDetailAsync(string entryId)
        => await PostFormAsync(DetailUrl, $"id={entryId}").ConfigureAwait(false);

    /// <summary>从攻略文档里截取「声骸词条」一节的原文(HTML,含转义)。</summary>
    private static string? ExtractAffixPrioritySection(string json)
    {
        // getEntryDetail 的 data 是对象,文档内容(HTML)嵌在字段里:整体反转义后按标题定位
        var text = json.Replace("\\\"", "\"").Replace("\\n", "\n");
        var idx = text.IndexOf("声骸词条", StringComparison.Ordinal);
        while (idx >= 0)
        {
            var segment = text[idx..];
            var candidate = sectionCandidate(segment);
            if (candidate is not null)
            {
                return candidate;
            }
            idx = text.IndexOf("声骸词条", idx + 4, StringComparison.Ordinal);
        }
        return null;

        static string? sectionCandidate(string segment)
        {
            if (segment.Length < 12)
            {
                return null;
            }
            var chunk = segment.Length > 1200 ? segment[..1200] : segment;
            // 该节必须真的含优先级分隔符(＞ 或 > 的转义),否则是别的引用位置
            return chunk.Contains('＞') || chunk.Contains("&gt;") || chunk.Contains(">") ? chunk : null;
        }
    }

    /// <summary>
    /// 解析「声骸词条」文本为权重表:去 HTML 标签 → &gt;/＞/&gt; 分组(=/＝ 并列)→ 组序递减赋权。
    /// 无法映射到已知词条的组(如括号里的推荐说明)终止解析。
    /// </summary>
    public static IReadOnlyDictionary<string, double>? ParsePriorityWeights(string? section)
    {
        if (string.IsNullOrWhiteSpace(section))
        {
            return null;
        }
        var s = Regex.Replace(section, "<[^>]+>", " ");
        s = s.Replace("&gt;", ">").Replace("&amp;", "&").Replace('＝', '>').Replace('＞', '>');
        var cut = s.IndexOfAny(new[] { '（', '(' });
        if (cut >= 0)
        {
            s = s[..cut];
        }
        var groups = s.Split('>', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var dict = new Dictionary<string, double>(StringComparer.Ordinal);
        for (var g = 0; g < groups.Length; g++)
        {
            var weight = g < RankWeights.Length ? RankWeights[g] : RankWeights[^1];
            var mapped = 0;
            foreach (var token in groups[g].Split(['=', '＝'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                foreach (var affix in MapTokenToAffixes(token))
                {
                    if (dict.TryAdd(affix, weight))
                    {
                        mapped++;
                    }
                }
            }
            if (mapped == 0 && g > 0)
            {
                break; // 解析到非词条文本(下一节标题/说明)即停
            }
        }
        return dict.Count > 0 ? dict : null;
    }

    /// <summary>优先级词条别名 → 标准属性名(一个别名可覆盖多个词条,如「攻击」含攻击%与固定攻击)。</summary>
    private static IEnumerable<string> MapTokenToAffixes(string token)
    {
        var t = token.Trim().Replace(" ", "");
        if (t.Length == 0)
        {
            yield break;
        }
        if (t.Contains("暴击伤害"))
        {
            yield return "暴击伤害";
            yield break;
        }
        if (t is "暴击" || t.Contains("暴击率"))
        {
            yield return "暴击";
            yield break;
        }
        if (t.Contains("重击"))
        {
            yield return "重击伤害加成";
            yield break;
        }
        if (t.Contains("普攻"))
        {
            yield return "普攻伤害加成";
            yield break;
        }
        if (t.Contains("共鸣技能") || t.Contains("技能伤害"))
        {
            yield return "共鸣技能伤害加成";
            yield break;
        }
        if (t.Contains("共鸣解放") || t.Contains("解放伤害"))
        {
            yield return "共鸣解放伤害加成";
            yield break;
        }
        if (t.Contains("谐度"))
        {
            yield return "谐度破坏伤害加成";
            yield break;
        }
        if (t.Contains("共鸣效率") || t.Contains("充能"))
        {
            yield return "共鸣效率";
            yield break;
        }
        if (t.Contains("攻击"))
        {
            yield return "攻击百分比";
            yield return "攻击";
            yield break;
        }
        if (t.Contains("生命"))
        {
            yield return "生命百分比";
            yield return "生命";
            yield break;
        }
        if (t.Contains("防御"))
        {
            yield return "防御百分比";
            yield return "防御";
            yield break;
        }
    }

    private static async Task<string> PostFormAsync(string url, string body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/x-www-form-urlencoded"),
        };
        request.Headers.TryAddWithoutValidation("Origin", "https://wiki.kurobbs.com");
        request.Headers.TryAddWithoutValidation("Referer", "https://wiki.kurobbs.com/");
        using var response = await Http.SendAsync(request).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    }
}
