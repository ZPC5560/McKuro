using System.Collections.Concurrent;
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
    /// <summary>默认客户端:未注入共享客户端时使用(单测/早期调用)。短超时,避免默认 100 秒挂住回填。</summary>
    private static readonly HttpClient DefaultHttp = new() { Timeout = TimeSpan.FromSeconds(20) };

    /// <summary>应用层注入的共享客户端(走系统代理/连接池,见 <see cref="UseHttpClient"/>);null 时用默认客户端。</summary>
    private static HttpClient? _sharedHttp;

    private static HttpClient Http => Volatile.Read(ref _sharedHttp) ?? DefaultHttp;

    /// <summary>单次请求超时(不依赖客户端 Timeout:共享客户端为 60 秒,对后台回填偏长)。</summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    /// <summary>临时失败(网络/超时/拦截)后的冷却期:期内不重试,期满再访问会重新拉取。</summary>
    private static readonly TimeSpan FailureRetryDelay = TimeSpan.FromMinutes(2);

    /// <summary>目录分页大小与页数上限(上限防止服务端分页字段异常时无限翻页)。</summary>
    private const int CataloguePageSize = 60;
    private const int CatalogueMaxPages = 20;

    /// <summary>「声骸词条」标签文本。</summary>
    private const string AffixLabel = "声骸词条";

    private const string PageUrl = "https://api.kurobbs.com/wiki/core/catalogue/item/getPage";
    private const string DetailUrl = "https://api.kurobbs.com/wiki/core/catalogue/item/getEntryDetail";
    /// <summary>攻略合集 → 角色攻略 目录(wiki.kurobbs.com/mc/catalogue/list?fid=1322&sid=1384)。</summary>
    private const string RoleGuideCatalogueId = "1384";

    /// <summary>组序 → 权重(首组 2.0;并列组共用同权重)。</summary>
    private static readonly double[] RankWeights = { 2.0, 1.0, 0.75, 0.5, 0.4, 0.35, 0.3 };

    /// <summary>
    /// 权重缓存(键见 <see cref="CacheKey"/>;值 null = <b>已确认</b>无攻略/无可解析词条)。
    /// <para>只缓存确定结果:网络异常/超时/被拦截不进缓存,只记冷却时刻(<see cref="FailedAt"/>),
    /// 否则启动时一次网络抖动就会让该角色整个会话都用兜底权重。</para>
    /// </summary>
    private static readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, double>?> Cache = new(StringComparer.Ordinal);

    /// <summary>临时失败时刻(冷却期内直接返回 null,不重复打接口)。仅在 <see cref="CacheLock"/> 内读写。</summary>
    private static readonly Dictionary<string, DateTime> FailedAt = new(StringComparer.Ordinal);

    private static readonly object CacheLock = new();
    private static readonly Dictionary<string, Task<IReadOnlyDictionary<string, double>?>> InFlight = new(StringComparer.Ordinal);

    /// <summary>「角色攻略」目录(整个会话只成功拉取一次;失败不缓存)。仅在 <see cref="CacheLock"/> 内读写。</summary>
    private static Task<IReadOnlyList<CatalogueEntry>>? _catalogue;

    /// <summary>目录中的一张攻略卡。</summary>
    internal sealed record CatalogueEntry(string Name, string EntryId);

    /// <summary>
    /// 注入应用共享的 <see cref="HttpClient"/>(走系统代理与连接池)。应用启动时调用一次;
    /// 未调用时使用内置的短超时默认客户端。
    /// </summary>
    public static void UseHttpClient(HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(http);
        Volatile.Write(ref _sharedHttp, http);
    }

    /// <summary>
    /// 取角色词条权重(角色名 → 权重表);无攻略/失败返回 null。按角色名缓存(兼容旧调用)。
    /// <para>漂泊者各属性同名:调用方有 cardRoleId/属性名时应改用带参数的重载,否则各形态共用一套权重。</para>
    /// </summary>
    public static Task<IReadOnlyDictionary<string, double>?> GetPriorityWeightsAsync(string roleName)
        => GetPriorityWeightsAsync(roleName, 0, null, CancellationToken.None);

    /// <summary>
    /// 取角色词条权重;无攻略/失败返回 null。
    /// </summary>
    /// <param name="roleName">角色名(库街区 roleName)。</param>
    /// <param name="cardRoleId">库街区 cardRoleId(&gt;0 时作缓存键:漂泊者各属性同名但 id 不同,不能共用一套权重)。</param>
    /// <param name="attributeName">属性名(如「衍射」;用于在同名多形态的攻略卡中挑对应属性的一张)。</param>
    /// <param name="ct">只取消本次等待,不取消共享的在途请求(其他调用方可能还在等它)。</param>
    public static async Task<IReadOnlyDictionary<string, double>?> GetPriorityWeightsAsync(
        string roleName,
        int cardRoleId,
        string? attributeName = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(roleName))
        {
            return null;
        }
        var key = CacheKey(roleName, cardRoleId, attributeName);
        Task<IReadOnlyDictionary<string, double>?> task;
        lock (CacheLock)
        {
            if (Cache.TryGetValue(key, out var cached))
            {
                return cached;
            }
            if (FailedAt.TryGetValue(key, out var failedAt) && DateTime.UtcNow - failedAt < FailureRetryDelay)
            {
                return null;
            }
            if (!InFlight.TryGetValue(key, out var inFlight))
            {
                inFlight = FetchAndCacheAsync(key, roleName, attributeName);
                InFlight[key] = inFlight;
            }
            task = inFlight;
        }
        return await task.WaitAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 拉取并按结果分类缓存:成功(含确认无攻略)写 <see cref="Cache"/>;临时失败只记冷却时刻。
    /// <para>先强制让出到线程池:保证调用方在锁内登记 <see cref="InFlight"/> 之后,本方法才会走到 finally 移除。</para>
    /// </summary>
    private static async Task<IReadOnlyDictionary<string, double>?> FetchAndCacheAsync(
        string key, string roleName, string? attributeName)
    {
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        try
        {
            var weights = await FetchPriorityWeightsAsync(roleName, attributeName).ConfigureAwait(false);
            lock (CacheLock)
            {
                Cache[key] = weights;
                FailedAt.Remove(key);
            }
            return weights;
        }
        catch (Exception)
        {
            // 网络异常/超时/WAF 返回 HTML 等:本次回退通用权重,但不缓存,冷却期后重试
            lock (CacheLock)
            {
                FailedAt[key] = DateTime.UtcNow;
            }
            return null;
        }
        finally
        {
            lock (CacheLock)
            {
                InFlight.Remove(key);
            }
        }
    }

    /// <summary>
    /// 实际拉取:目录查条目 → 条目详情 → 截取「声骸词条」→ 解析。
    /// 目录里没有该角色(联名角色等)或文档里解析不出词条 → 返回 null(确定结果,可缓存);
    /// 网络/结构异常 → 抛出(由 <see cref="FetchAndCacheAsync"/> 当作临时失败)。
    /// </summary>
    private static async Task<IReadOnlyDictionary<string, double>?> FetchPriorityWeightsAsync(
        string roleName, string? attributeName)
    {
        var catalogue = await GetCatalogueAsync().ConfigureAwait(false);
        var entryId = FindRoleEntryId(catalogue, roleName, attributeName);
        if (entryId is null)
        {
            return null;
        }
        var json = await PostFormAsync(DetailUrl, "id=" + Uri.EscapeDataString(entryId)).ConfigureAwait(false);
        // 非 JSON(WAF/网关错误页):临时失败,不能当成"该角色无词条"缓存
        if (!json.TrimStart().StartsWith('{'))
        {
            throw new InvalidDataException("wiki 条目详情响应不是 JSON");
        }
        var section = ExtractAffixPrioritySection(json);
        return ParsePriorityWeights(section);
    }

    /// <summary>缓存键:有 cardRoleId 用 id(区分漂泊者各形态),否则用 角色名+属性名。</summary>
    private static string CacheKey(string roleName, int cardRoleId, string? attributeName)
        => cardRoleId > 0
            ? "id:" + cardRoleId.ToString(CultureInfo.InvariantCulture)
            : "name:" + roleName + "|" + (attributeName ?? "");

    /// <summary>取「角色攻略」目录:会话内成功一次即复用;失败(含部分失败)不缓存,下次重新翻页。</summary>
    private static Task<IReadOnlyList<CatalogueEntry>> GetCatalogueAsync()
    {
        lock (CacheLock)
        {
            if (_catalogue is null || _catalogue.IsFaulted || _catalogue.IsCanceled)
            {
                _catalogue = Task.Run(() => LoadCatalogueAsync());
            }
            return _catalogue;
        }
    }

    /// <summary>
    /// 翻页读取整个目录(原实现只读 page=1&amp;limit=60,目录超过 60 条后新角色永远找不到)。
    /// 以服务端 pages 字段为准;缺失时按"本页不满一页即末页"判断,并设页数上限防止异常无限翻页。
    /// </summary>
    private static async Task<IReadOnlyList<CatalogueEntry>> LoadCatalogueAsync()
    {
        var entries = new List<CatalogueEntry>();
        for (var page = 1; page <= CatalogueMaxPages; page++)
        {
            var body = string.Create(CultureInfo.InvariantCulture,
                $"catalogueId={RoleGuideCatalogueId}&page={page}&limit={CataloguePageSize}");
            var json = await PostFormAsync(PageUrl, body).ConfigureAwait(false);
            var (records, pages) = ParseCataloguePage(json);
            if (records is null)
            {
                // 首页结构不对(接口变更/被拦截返回错误信封):当作临时失败,不能缓存成"没有攻略"
                if (page == 1)
                {
                    throw new InvalidDataException("wiki 角色攻略目录响应结构异常");
                }
                break;
            }
            entries.AddRange(records.Entries);
            var lastPage = pages > 0 ? page >= pages : records.Count < CataloguePageSize;
            if (lastPage || records.Count == 0)
            {
                break;
            }
        }
        if (entries.Count == 0)
        {
            throw new InvalidDataException("wiki 角色攻略目录为空");
        }
        return entries;
    }

    /// <summary>单页解析结果:Entries = 有名字且有条目 id 的卡片;Count = 本页原始记录数(含无效卡片,用于判断末页)。</summary>
    internal sealed record CataloguePage(List<CatalogueEntry> Entries, int Count);

    /// <summary>
    /// 解析目录单页(<c>data.results.records[]</c>,分页总数 <c>data.results.pages</c>)。
    /// 结构不符返回 (null, 0);pages 缺失时为 0。
    /// </summary>
    internal static (CataloguePage? Records, int Pages) ParseCataloguePage(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object
            || !doc.RootElement.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty("results", out var results)
            || results.ValueKind != JsonValueKind.Object
            || !results.TryGetProperty("records", out var records)
            || records.ValueKind != JsonValueKind.Array)
        {
            return (null, 0);
        }
        var pages = results.TryGetProperty("pages", out var p) && p.ValueKind == JsonValueKind.Number
            && p.TryGetInt32(out var pv) ? pv : 0;
        var list = new List<CatalogueEntry>();
        var count = 0;
        foreach (var rec in records.EnumerateArray())
        {
            count++;
            if (rec.ValueKind != JsonValueKind.Object)
            {
                continue;
            }
            var name = rec.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }
            // 条目 id:优先 entryId 字段(字符串或数字),链接型卡片从 content 里挖(content 可能是转义后的 JSON 串)
            var entryId = rec.TryGetProperty("entryId", out var eid) ? ScalarToString(eid) : null;
            if (string.IsNullOrWhiteSpace(entryId))
            {
                var m = Regex.Match(rec.GetRawText(), @"\\?""entryId\\?""\s*:\s*\\?""?(\d+)");
                entryId = m.Success ? m.Groups[1].Value : null;
            }
            if (string.IsNullOrWhiteSpace(entryId))
            {
                continue;
            }
            list.Add(new CatalogueEntry(name.Trim(), entryId));
        }
        return (new CataloguePage(list, count), pages);
    }

    private static string? ScalarToString(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString(),
        JsonValueKind.Number => e.GetRawText(),
        _ => null,
    };

    /// <summary>
    /// 在目录中挑角色的攻略条目 id。优先级:
    /// ① 名称以角色名开头且含属性名(漂泊者-衍射:同名多形态按属性区分);
    /// ② 名称与角色名完全一致;③ 名称以角色名开头(形态后缀,取第一张)。
    /// </summary>
    internal static string? FindRoleEntryId(IReadOnlyList<CatalogueEntry> catalogue, string roleName, string? attributeName)
    {
        string? exact = null;
        string? prefix = null;
        string? byAttribute = null;
        foreach (var entry in catalogue)
        {
            if (!entry.Name.StartsWith(roleName, StringComparison.Ordinal))
            {
                continue;
            }
            if (byAttribute is null && !string.IsNullOrWhiteSpace(attributeName)
                && entry.Name.Length > roleName.Length
                && entry.Name.IndexOf(attributeName, roleName.Length, StringComparison.Ordinal) >= 0)
            {
                byAttribute = entry.EntryId;
            }
            if (exact is null && entry.Name.Length == roleName.Length)
            {
                exact = entry.EntryId;
            }
            prefix ??= entry.EntryId;
        }
        return byAttribute ?? exact ?? prefix;
    }

    /// <summary>
    /// 从 getEntryDetail 响应里截取「声骸词条」<b>对应的那一格/那一段</b>(HTML 片段,不含标签本身)。
    /// <para>
    /// 旧实现整体反转义后取标签后 1200 字符,判定条件里的 <c>chunk.Contains("&gt;")</c> 对 HTML 恒为真,
    /// 结果混入后续表格行(其他词条/说明),且标题留在首组里。现改为结构化定位:
    /// 解析 JSON → 遍历全部字符串值(HTML 可能再嵌一层 JSON 串)→ 在 HTML 中找到标签,
    /// 跳过紧随其后的标签边界(<c>&lt;/td&gt;&lt;td&gt;</c>、<c>&lt;/p&gt;&lt;p&gt;</c> 等),只取到该格/段结束为止;
    /// 字符串数组形态的表格取标签后一项。截出的片段须能解析出词条,否则视为别处引用,继续找下一处。
    /// </para>
    /// </summary>
    internal static string? ExtractAffixPrioritySection(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return FindSectionInElement(doc.RootElement, depth: 0);
        }
        catch (JsonException)
        {
            // 非 JSON(或被截断):按纯文本兜底(先还原最常见的 JSON 转义)
            return FindSectionInHtml(json.Replace("\\\"", "\"").Replace("\\n", "\n").Replace("\\/", "/"));
        }
    }

    /// <summary>递归遍历 JSON 元素中的字符串值查找「声骸词条」一格(depth 防止病态嵌套)。</summary>
    private static string? FindSectionInElement(JsonElement element, int depth)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
            {
                foreach (var prop in element.EnumerateObject())
                {
                    if (FindSectionInElement(prop.Value, depth) is { } found)
                    {
                        return found;
                    }
                }
                return null;
            }
            case JsonValueKind.Array:
            {
                var items = element.EnumerateArray().ToList();
                for (var i = 0; i < items.Count; i++)
                {
                    // 表格以字符串数组表示时(["声骸词条", "暴击＝暴击伤害＞…"]):取标签后一项
                    if (i + 1 < items.Count
                        && items[i].ValueKind == JsonValueKind.String
                        && items[i + 1].ValueKind == JsonValueKind.String
                        && IsBareAffixLabel(items[i].GetString())
                        && items[i + 1].GetString() is { } cell
                        && ParsePriorityWeights(cell) is not null)
                    {
                        return cell;
                    }
                    if (FindSectionInElement(items[i], depth) is { } found)
                    {
                        return found;
                    }
                }
                return null;
            }
            case JsonValueKind.String:
            {
                var s = element.GetString();
                if (string.IsNullOrEmpty(s) || !s.Contains(AffixLabel, StringComparison.Ordinal))
                {
                    return null;
                }
                // 文档内容本身可能是一层 JSON 串(富文本模块):再解析一层
                var trimmed = s.TrimStart();
                if (depth < 3 && trimmed.Length > 0 && (trimmed[0] == '{' || trimmed[0] == '['))
                {
                    try
                    {
                        using var inner = JsonDocument.Parse(s);
                        if (FindSectionInElement(inner.RootElement, depth + 1) is { } nested)
                        {
                            return nested;
                        }
                    }
                    catch (JsonException)
                    {
                        // 不是 JSON:按 HTML 文本处理
                    }
                }
                return FindSectionInHtml(s);
            }
            default:
                return null;
        }
    }

    /// <summary>单元格是否只有「声骸词条」标签本身(去标签/空白/冒号后)。</summary>
    private static bool IsBareAffixLabel(string? cell)
    {
        if (string.IsNullOrEmpty(cell))
        {
            return false;
        }
        var text = Regex.Replace(cell, "<[^>]+>", "").Trim().TrimEnd(':', '：').Trim();
        return string.Equals(text, AffixLabel, StringComparison.Ordinal);
    }

    /// <summary>标签与值之间允许跳过的"包裹/单元格边界"标签(&lt;/td&gt;&lt;td&gt;、&lt;/p&gt;&lt;p&gt;、&lt;strong&gt; 等)。</summary>
    private static readonly Regex LeadingWrapperTag = new(
        @"\G</?(?:td|th|p|span|strong|b|em|font|div|h[1-6])\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>值所在格/段的结束位置(取最近的一个);不含 &lt;/strong&gt; 等行内标签。</summary>
    private static readonly string[] SectionStops =
        ["</td", "</th", "</tr", "</p", "</li", "</div", "</h1", "</h2", "</h3", "</h4", "</h5", "</h6", "<br", "\n"];

    /// <summary>截取片段长度上限(防止结构异常时把整篇文档当成一格)。</summary>
    private const int MaxSectionLength = 400;

    /// <summary>在 HTML/文本中逐个定位「声骸词条」,取其后紧邻的一格/一段;解析不出词条的位置跳过。</summary>
    internal static string? FindSectionInHtml(string html)
    {
        var idx = html.IndexOf(AffixLabel, StringComparison.Ordinal);
        while (idx >= 0)
        {
            var candidate = SliceValueAfterLabel(html, idx + AffixLabel.Length);
            if (candidate is not null && ParsePriorityWeights(candidate) is not null)
            {
                return candidate;
            }
            idx = html.IndexOf(AffixLabel, idx + AffixLabel.Length, StringComparison.Ordinal);
        }
        return null;
    }

    /// <summary>从标签末尾开始:跳过空白/冒号/包裹标签,截到所在格/段结束。</summary>
    private static string? SliceValueAfterLabel(string html, int start)
    {
        var pos = start;
        while (pos < html.Length)
        {
            var c = html[pos];
            if (char.IsWhiteSpace(c) || c is ':' or '：')
            {
                pos++;
                continue;
            }
            if (c == '<')
            {
                var m = LeadingWrapperTag.Match(html, pos, html.Length - pos);
                if (m.Success && m.Index == pos)
                {
                    pos += m.Length;
                    continue;
                }
            }
            break;
        }
        if (pos >= html.Length)
        {
            return null;
        }
        var end = Math.Min(html.Length, pos + MaxSectionLength);
        foreach (var stop in SectionStops)
        {
            var s = html.IndexOf(stop, pos, end - pos, StringComparison.OrdinalIgnoreCase);
            if (s >= 0 && s < end)
            {
                end = s;
            }
        }
        var slice = html[pos..end].Trim();
        return slice.Length == 0 ? null : slice;
    }

    /// <summary>
    /// 解析「声骸词条」文本为权重表:去 HTML 标签 → &gt;/＞ 分组 → 同级分隔符 → 组序递减赋权。
    /// <para>
    /// <b>同级分隔符</b>:Wiki 用 <c>=</c>,而 mcguide 攻略正文常用 <c>/</c>(如「暴击/暴击伤害」)
    /// 与 <c>、</c>。早期只按 <c>=</c> 切分,导致 <c>暴击/暴击伤害</c> 被当成单个 token 而只映射出
    /// 「暴击伤害」—— <c>暴击</c> 静默丢失(实测千咲「暴击/暴击伤害/攻击%/共鸣解放/共鸣效率」
    /// 只解析出 1 条,丢 4 条)。
    /// </para>
    /// <para>
    /// <b>括号处理</b>:早期遇到第一个 <c>（</c> 就截断其后全部内容,但 mcguide 的补充说明
    /// 出现在句中(如「共鸣效率（230%）&gt;暴击伤害&gt;…」),截断会把后面真正有效的词条全丢掉。
    /// 现改为<b>只删除括号及其内部内容</b>,保留其余文本。
    /// </para>
    /// </summary>
    public static IReadOnlyDictionary<string, double>? ParsePriorityWeights(string? section)
    {
        if (string.IsNullOrWhiteSpace(section))
        {
            return null;
        }
        var s = Regex.Replace(section, "<[^>]+>", " ");
        // 标题前缀(「声骸词条：暴击＞…」/「声骸词条推荐 暴击＞…」):只解析标签之后的部分,
        // 否则首个 token 变成「声骸词条暴击」之类,暴击排第一时会被丢掉
        var labelAt = s.IndexOf(AffixLabel, StringComparison.Ordinal);
        if (labelAt >= 0)
        {
            s = s[(labelAt + AffixLabel.Length)..];
        }
        // 全角「＝」是同级分隔符(见 TierSeparators),不能在这里替换成分级符号 >
        s = s.Replace("&gt;", ">").Replace("&amp;", "&").Replace('＞', '>');
        // 删除括号内容(含全角/半角),而不是截断其后文本 —— 说明常出现在句中
        s = Regex.Replace(s, "[（(][^）)]*[）)]", " ");
        // 句号/分号/顿号等拆成独立段落,避免与前缀说明(如「共鸣效率120%」)黏连
        s = s.Replace('；', '>').Replace(';', '>').Replace('。', '>').Replace('，', '>').Replace(',', '>');
        var groups = s.Split('>', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var dict = new Dictionary<string, double>(StringComparer.Ordinal);
        for (var g = 0; g < groups.Length; g++)
        {
            var weight = g < RankWeights.Length ? RankWeights[g] : RankWeights[^1];
            foreach (var token in groups[g].Split(TierSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                foreach (var affix in MapTokenToAffixes(token))
                {
                    dict.TryAdd(affix, weight);
                }
            }
            // 该组一个词条都没映射出来(纯数值/说明文字)时不再中断解析:
            // mcguide 正文里「共鸣效率120%」这类带数值的说明很常见,中断会丢掉其后全部词条。
        }
        return dict.Count > 0 ? dict : null;
    }

    /// <summary>同级词条分隔符:Wiki 的 <c>=</c>/<c>＝</c>,以及 mcguide 常用的 <c>/</c>、<c>、</c>。</summary>
    private static readonly char[] TierSeparators = ['=', '＝', '/', '／', '、'];

    /// <summary>token 两端要去掉的标点(标签后的冒号、破折号等)。</summary>
    private static readonly char[] TokenTrimChars = [':', '：', '-', '—', '·', '|'];

    /// <summary>优先级词条别名 → 标准属性名(一个别名可覆盖多个词条,如「攻击」含攻击%与固定攻击)。</summary>
    private static IEnumerable<string> MapTokenToAffixes(string token)
    {
        var t = token.Trim().Replace(" ", "").Trim(TokenTrimChars);
        if (t.Length == 0)
        {
            yield break;
        }
        if (t.Contains("暴击伤害"))
        {
            yield return "暴击伤害";
            yield break;
        }
        // EndsWith 而非精确相等:残留的前缀说明(「推荐暴击」「：暴击」)不应让排第一的暴击被丢掉
        if (t.EndsWith("暴击", StringComparison.Ordinal) || t.Contains("暴击率"))
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
        // 单请求超时:请求是多个调用方共享的在途任务,不跟任何单个调用方的取消令牌走
        using var timeout = new CancellationTokenSource(RequestTimeout);
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/x-www-form-urlencoded"),
        };
        request.Headers.TryAddWithoutValidation("Origin", "https://wiki.kurobbs.com");
        request.Headers.TryAddWithoutValidation("Referer", "https://wiki.kurobbs.com/");
        using var response = await Http.SendAsync(request, timeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
    }
}
