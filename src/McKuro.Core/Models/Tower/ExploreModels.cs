using System.Globalization;
using System.Text.Json.Serialization;

namespace McKuro.Core.Models.Tower;

/// <summary>
/// 地图探索数据(库街区 <c>exploreIndex</c> 接口,即官方「数据终端-探索数据」)。
/// <para>
/// 与深塔/海墟同属数据中心接口,响应信封的 <c>data</c> 同样是<b>JSON 字符串</b>,需二次 parse。
/// 有 3 个容易踩的反直觉点(结构见 Kuro-API-Collection 的 exploreIndex 文档):
/// </para>
/// <list type="number">
/// <item><c>countryProgress</c>(国家总探索度)是<b>字符串包裹的数字</b>(如 "45.2"),
/// 而 <c>areaProgress</c>/<c>item.progress</c> 是数字 —— 三种百分比的读法各不相同。</item>
/// <item>国家图标字段同全息战略:<c>homePageIcon/detailPageImage/detailPageAreaPic</c>
/// 实测偶有「目录前缀」(以 / 结尾、无文件名),只有 <c>homePageImage</c>/<c>detailPageImage</c>
/// 通常是完整 URL —— 故取图走 <see cref="ExploreParser.ResolveImageUrl"/> 挑第一个可用值。</item>
/// <item><c>exploreList</c> 一次返回<b>全部国家</b>(官方 H5 的国家页签就是由它渲染的),
/// 不需要按 countryCode 逐个国家再请求一遍。</item>
/// </list>
/// </summary>
public sealed class ExploreIndexData
{
    /// <summary>各国探索数据(顺序即官方 H5 的页签顺序,不做二次排序)。</summary>
    [JsonPropertyName("exploreList")] public List<ExploreCountryEntry>? ExploreList { get; set; }

    /// <summary>声骸探寻信息(与探索度无关,暂不展示;保留字段便于将来扩展)。</summary>
    [JsonPropertyName("detectionInfoList")] public List<ExploreDetectionInfo>? DetectionInfoList { get; set; }

    /// <summary>是否对外展示(空数据时该键整体缺失)。</summary>
    [JsonPropertyName("open")] public bool? Open { get; set; }
}

/// <summary>单个国家的探索数据。</summary>
public sealed class ExploreCountryEntry
{
    [JsonPropertyName("country")] public ExploreCountryInfo? Country { get; set; }

    /// <summary>国家总探索度:<b>字符串</b>包裹的数字(如 "45.2"),见 <see cref="ExploreParser.ParsePercent"/>。</summary>
    [JsonPropertyName("countryProgress")] public string? CountryProgress { get; set; }

    /// <summary>该国家下的区域(地图)列表。</summary>
    [JsonPropertyName("areaInfoList")] public List<ExploreArea>? AreaInfoList { get; set; }
}

/// <summary>国家信息(名称与配色/图标)。</summary>
public sealed class ExploreCountryInfo
{
    [JsonPropertyName("countryId")] public int CountryId { get; set; }
    [JsonPropertyName("countryName")] public string? CountryName { get; set; }
    [JsonPropertyName("bgColor")] public string? BgColor { get; set; }
    [JsonPropertyName("homePageIcon")] public string? HomePageIcon { get; set; }
    [JsonPropertyName("homePageImage")] public string? HomePageImage { get; set; }
    [JsonPropertyName("detailPageImage")] public string? DetailPageImage { get; set; }
    [JsonPropertyName("detailPagePic")] public string? DetailPagePic { get; set; }
    [JsonPropertyName("detailPageAreaPic")] public string? DetailPageAreaPic { get; set; }
    [JsonPropertyName("detailPageProgressColor")] public string? DetailPageProgressColor { get; set; }
}

/// <summary>单个区域(地图)的探索数据。</summary>
public sealed class ExploreArea
{
    [JsonPropertyName("areaId")] public int AreaId { get; set; }
    [JsonPropertyName("areaName")] public string? AreaName { get; set; }

    /// <summary>区域预览图 URL(可能是目录前缀,取图见 <see cref="ExploreParser.ResolveImageUrl"/>)。</summary>
    [JsonPropertyName("areaPic")] public string? AreaPic { get; set; }

    /// <summary>区域探索度(0-100 的百分数,非 0-1 比例)。</summary>
    [JsonPropertyName("areaProgress")] public double AreaProgress { get; set; }

    /// <summary>该区域内的探索物进度(奇藏/信标/声匣/观景点…)。</summary>
    [JsonPropertyName("itemList")] public List<ExploreItem>? ItemList { get; set; }
}

/// <summary>探索物进度(如「奇藏 12/20」)。</summary>
public sealed class ExploreItem
{
    [JsonPropertyName("icon")] public string? Icon { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }

    /// <summary>探索进度(百分数,与 <see cref="ExploreArea.AreaProgress"/> 同一口径)。</summary>
    [JsonPropertyName("progress")] public double Progress { get; set; }

    /// <summary>探索物类型(1=奇藏,2=信标,3=声匣…)。名称已由 <see cref="Name"/> 给出,这里只留作诊断。</summary>
    [JsonPropertyName("type")] public int Type { get; set; }
}

/// <summary>声骸探寻信息(暂不展示)。</summary>
public sealed class ExploreDetectionInfo
{
    [JsonPropertyName("acronym")] public string? Acronym { get; set; }
    [JsonPropertyName("detectionIcon")] public string? DetectionIcon { get; set; }
    [JsonPropertyName("detectionId")] public long DetectionId { get; set; }
    [JsonPropertyName("detectionName")] public string? DetectionName { get; set; }
    [JsonPropertyName("level")] public int Level { get; set; }
    [JsonPropertyName("levelName")] public string? LevelName { get; set; }
}

/// <summary>
/// 探索数据的纯函数解析工具(公开供单测)。
/// <para>三个口径差异集中在这里,避免 VM 里散落 "45.2" 字符串与 45.2 数字两套解析:</para>
/// </summary>
public static class ExploreParser
{
    /// <summary>
    /// 解析国家总探索度(<c>countryProgress</c> 是字符串,可能带百分号或千分位,也可能是空串)。
    /// 解析失败返回 null(界面显示「--」而不是假的 0%)。
    /// </summary>
    public static double? ParsePercent(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }
        var text = raw.Trim().TrimEnd('%', '％').Trim();
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? v
            : null;
    }

    /// <summary>
    /// 百分比文案统一保留 1 位小数并去掉多余的 ".0"(45.20 → "45.2%",100.00 → "100%")。
    /// 探索度在游戏里就是 1 位小数的口径,多给位数会让两处显示互相矛盾。
    /// </summary>
    public static string FormatPercent(double value)
        => value.ToString("0.#", CultureInfo.InvariantCulture) + "%";

    /// <summary>
    /// 库街区静态资源前缀。<b>实测(2026-10 真实账号数据)</b>:探索物图标下发的是相对路径
    /// (<c>Config/33/project_icon/1766625030728.png</c>),国标/区域图则是完整 URL ——
    /// 相对路径必须补上这段前缀才能取到图(该前缀下 adminConfig/33/... 返回 200 image/png)。
    /// </summary>
    public const string WebStaticAdminBase = "https://web-static.kurobbs.com/admin";

    /// <summary>
    /// 从若干候选字段里挑第一个**可用的图片地址**,并保证返回的是绝对 URL。
    /// <para>
    /// 两道过滤都是实测踩出来的:
    /// </para>
    /// <list type="number">
    /// <item>有些字段是「目录前缀」(以 / 结尾、没有文件名,如 <c>https://…/detail_page_area_pic/</c>),
    /// 直接当图片地址交给 <c>AsyncImage</c> 会请求一个目录、拿到 404 后永远停在占位图。</item>
    /// <item>探索物图标是相对路径(<c>Config/33/…png</c>),不补 <see cref="WebStaticAdminBase"/>
    /// 就是相对当前界面的无效地址。</item>
    /// </list>
    /// </summary>
    public static string? ResolveImageUrl(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }
            var value = candidate.Trim();
            if (value.EndsWith('/'))
            {
                continue; // 目录前缀,不是图片地址
            }
            if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
            // 协议相对地址("//host/x.png")、data:/file: 这类带 scheme 的值一律丢弃 ——
            // 补上静态资源前缀只会拼出一个必然 404 的地址,不如整块不显示。
            if (value.StartsWith("//", StringComparison.Ordinal) || value.Contains(':'))
            {
                continue;
            }
            // 相对路径:补静态资源前缀(不带前导 / 时 base + "/" + path)
            var path = value.TrimStart('/');
            if (path.Length == 0)
            {
                continue;
            }
            if (path.StartsWith("adminConfig/", StringComparison.OrdinalIgnoreCase))
            {
                return "https://web-static.kurobbs.com/" + path;
            }
            return WebStaticAdminBase + "/" + path;
        }
        return null;
    }

    /// <summary>区域进度(0-100)夹到合法区间:接口偶发给出 &gt;100 或负数,进度条会画反。</summary>
    public static double ClampPercent(double value) => double.IsNaN(value) ? 0 : Math.Clamp(value, 0, 100);
}

[JsonSerializable(typeof(ExploreIndexData))]
[JsonSerializable(typeof(ExploreCountryEntry))]
[JsonSerializable(typeof(List<ExploreCountryEntry>))]
[JsonSerializable(typeof(ExploreCountryInfo))]
[JsonSerializable(typeof(ExploreArea))]
[JsonSerializable(typeof(List<ExploreArea>))]
[JsonSerializable(typeof(ExploreItem))]
[JsonSerializable(typeof(List<ExploreItem>))]
[JsonSerializable(typeof(ExploreDetectionInfo))]
[JsonSerializable(typeof(List<ExploreDetectionInfo>))]
public sealed partial class ExploreJsonContext : JsonSerializerContext;
