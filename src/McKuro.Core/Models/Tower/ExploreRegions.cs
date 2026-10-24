using System.Collections.Generic;

namespace McKuro.Core.Models.Tower;

/// <summary>
/// 库街区探索数据里「地区 → 子地区 → 区域」的二级分级目录。
/// <para>
/// <b>为什么需要本地表</b>:<c>exploreIndex</c> 的 <c>areaInfoList</c> 是**扁平**的,
/// 只有 <c>areaId/areaName</c>,没有"该区域属于哪个子地区"的字段 —— 但游戏里确实是三层的:
/// 瑝珑(国家/地区) → 今州 / 梦州(子地区) → 云陵谷 / 玄方城(区域)。
/// 因此只能按 <c>areaId</c> 归类(2026-10 用真实账号 51 个区域逐一核对:
/// 瑝珑的梦州区域是 44xxx 号段 + 46「梦枢天罗」,拉海洛的 9 个区域是 3610x 号段,
/// 与官方 3.0「拉海洛包含区域」名单一一对应)。
/// </para>
/// <para>
/// <b>新增区域怎么办</b>:接口新开区域后,这里查不到 id 的区域会落到「其他区域」分组
/// (而不是被丢掉)—— 补一行 id 即可归位。没有配置子地区的国家(黑海岸/黎那汐塔)
/// 保持扁平,左栏只有一层,不做无意义的嵌套。
/// </para>
/// </summary>
public static class ExploreRegionCatalog
{
    /// <summary>一个子地区:稳定的 key(不随语言变)、本地化 key 与它包含的区域 id。</summary>
    public sealed record SubRegion(string Key, string LabelKey, IReadOnlySet<int> AreaIds);

    /// <summary>子地区 key → 本地化文案 key(文案在 Assets/lang 里,zh/en 各一份)。</summary>
    public static string LabelKeyOf(string key) => "Tower.Explore.Sub." + key;

    /// <summary>未归类区域的兜底分组 key(文案 "其他区域")。</summary>
    public const string OtherKey = "Other";

    /// <summary>国家/地区 id → 子地区列表(顺序即左栏展示顺序)。</summary>
    private static readonly Dictionary<int, SubRegion[]> Map = new()
    {
        // 瑝珑:今州(1.x 主线所在) + 梦州(3.5 开放玄方地界,含 46「梦枢天罗」)
        [1] =
        [
            new SubRegion("Jinzhou", LabelKeyOf("Jinzhou"), new HashSet<int>
            {
                2, 3, 4, 5, 6, 7, 8, 9, 10, 12, 14,
            }),
            new SubRegion("Mengzhou", LabelKeyOf("Mengzhou"), new HashSet<int>
            {
                46, 44001, 44002, 44003, 44004,
            }),
        ],
        // 黎那汐塔:拉古那(2.0~2.3 多地 + 2.5 隐海试验场) / 七丘(2.4 七丘、2.6 桑古伊斯狩原)
        // 区域名与官方/社区地区页的辖地对齐:拉古那 13 个区域,七丘 2 个区域(七丘本体 + 桑古伊斯狩原)
        [3] =
        [
            new SubRegion("Ragunna", LabelKeyOf("Ragunna"), new HashSet<int>
            {
                16, 17, 18, 19, 20, 21, 22, 23, 25, 27, 28, 32, 34,
            }),
            new SubRegion("Septimont", LabelKeyOf("Septimont"), new HashSet<int>
            {
                33, 36,
            }),
        ],
        // 罗伊冰原:拉海洛(3.0) / 冰原地表(3.1) / 黯原(3.3) —— 三个子地区的区域名单与官方版本说明逐一核对过
        [4] =
        [
            new SubRegion("LahaiRoi", LabelKeyOf("LahaiRoi"), new HashSet<int>
            {
                36102, 36105, 36106, 36107, 36108, 36109, 36110, 36111, 36112,
            }),
            new SubRegion("IceSurface", LabelKeyOf("IceSurface"), new HashSet<int>
            {
                41001, 41002, 41004, 41005, 41006,
            }),
            new SubRegion("DarkPlain", LabelKeyOf("DarkPlain"), new HashSet<int>
            {
                42001, 42002, 42003, 42004,
            }),
        ],
    };

    /// <summary>该国家是否配置了子地区(没有配置的国家/地区保持扁平)。</summary>
    public static bool HasSubRegions(int countryId) => Map.ContainsKey(countryId);

    /// <summary>该国家配置的子地区(没有返回空表)。</summary>
    public static IReadOnlyList<SubRegion> SubRegionsOf(int countryId)
        => Map.TryGetValue(countryId, out var list) ? list : [];

    /// <summary>
    /// 区域 id → 子地区 key;配置了分级但没归类的区域返回 <see cref="OtherKey"/>;
    /// 国家本身没有分级时返回 null(左栏不显示二级)。
    /// </summary>
    public static string? SubRegionOf(int countryId, int areaId)
    {
        if (!Map.TryGetValue(countryId, out var list))
        {
            return null;
        }
        foreach (var sub in list)
        {
            if (sub.AreaIds.Contains(areaId))
            {
                return sub.Key;
            }
        }
        return OtherKey;
    }

    /// <summary>子地区 key → 它在配置里的顺序(兜底分组排最后),用于左栏排序。</summary>
    public static int OrderOf(int countryId, string key)
    {
        if (!Map.TryGetValue(countryId, out var list))
        {
            return 0;
        }
        for (var i = 0; i < list.Length; i++)
        {
            if (string.Equals(list[i].Key, key, StringComparison.Ordinal))
            {
                return i;
            }
        }
        return list.Length; // 「其他区域」排在已配置分组的后面
    }
}
