using System.Globalization;
using System.Text.Json.Serialization;

namespace McKuro.Core.Models.Tower;

/// <summary>
/// 全息战略总览(challengeIndex 接口)。
/// <para>
/// 与深塔/海墟同属库街区数据中心接口,但<b>字段语义自成一套</b>,有 4 个容易踩的反直觉点
/// (2026-10 实测 23 个 boss / 138 条记录逐一核对):
/// </para>
/// <list type="number">
/// <item>响应信封的 <c>data</c> 是<b>JSON 字符串</b>,要二次 parse(不是 AES)。</item>
/// <item>索引里的 <c>difficulty</c> 与记录里的 <c>difficulty</c> <b>同名不同义</b>:
/// 索引里是「已通关最高难度」(0=未挑战),记录里是「难度档位」(1..6)。</item>
/// <item>索引里国家字段拼作 <c>contryId</c>(服务端拼写),不是 countryId。</item>
/// <item>记录里 <c>roles</c> 是<b>可选键</b>:未通关时整个键缺失,而非空数组。</item>
/// </list>
/// <para>
/// 国家图标字段中只有 <c>homePageImage</c> 是完整 URL;
/// <c>homePageIcon/detailPageImage/detailPagePic/detailPageAreaPic</c> 实测是「目录前缀」
/// (以 / 结尾、无文件名),颜色类字段 4 个地区实测全为空串 —— 故不建模,避免出现永远为空的死字段。
/// </para>
/// </summary>
public sealed class HologramIndexData
{
    /// <summary>是否已解锁。空数据(roleId 不存在)时该键整体缺失,故为可空。</summary>
    [JsonPropertyName("isUnlock")] public bool? IsUnlock { get; set; }

    /// <summary>活动是否开放(与 <see cref="IsUnlock"/> 同为空数据时缺失)。</summary>
    [JsonPropertyName("open")] public bool? Open { get; set; }

    /// <summary>
    /// 攻略站地址。接口有下发,但界面上的「查看详情」入口已按用户要求移除,
    /// 故当前无消费方 —— 保留字段是为了让响应形状与接口一致(反序列化时能看出来),
    /// 若后续恢复入口可直接接上。
    /// </summary>
    [JsonPropertyName("wikiUrl")] public string? WikiUrl { get; set; }

    /// <summary>地区分组(演武/同步/幻痛/强袭),接口按 sort 降序下发。</summary>
    [JsonPropertyName("challengeList")] public List<HologramCountryGroup>? ChallengeList { get; set; }
}

/// <summary>全息战略-地区分组(一个地区 + 其下的 boss 索引)。</summary>
public sealed class HologramCountryGroup
{
    /// <summary>地区排序值(实测 演武 130 / 同步 105 / 幻痛 100 / 强袭 90,降序即展示顺序)。</summary>
    [JsonPropertyName("sort")] public int Sort { get; set; }

    [JsonPropertyName("country")] public HologramCountryInfo? Country { get; set; }

    /// <summary>该地区的 boss 索引列表(按 sort 降序)。</summary>
    [JsonPropertyName("indexList")] public List<HologramBossEntry>? IndexList { get; set; }
}

/// <summary>全息战略-地区信息。</summary>
public sealed class HologramCountryInfo
{
    /// <summary>地区 ID(实测 100004 演武 / 100001 同步 / 100002 幻痛 / 100003 强袭)。</summary>
    [JsonPropertyName("countryId")] public int CountryId { get; set; }

    /// <summary>地区名(即界面上的四个 logo 菜单文案)。</summary>
    [JsonPropertyName("countryName")] public string? CountryName { get; set; }

    /// <summary>地区 logo(唯一以完整 URL 下发的图标字段,浮雕风格的剪影图)。</summary>
    [JsonPropertyName("homePageImage")] public string? HomePageImage { get; set; }
}

/// <summary>
/// 全息战略-boss 索引项(地区卡片)。
/// <para><b>注意</b>:本类型的 <see cref="ClearedDifficulty"/> 是「已通关最高难度」,
/// 与 <see cref="HologramChallengeRecord.Difficulty"/>(难度档位)不是同一个量。</para>
/// </summary>
public sealed class HologramBossEntry
{
    /// <summary>boss ID(全服唯一,同时是 challengeDetails 里字典的键)。</summary>
    [JsonPropertyName("bossId")] public int BossId { get; set; }

    [JsonPropertyName("bossName")] public string? BossName { get; set; }

    /// <summary>难度 1 时的 boss 等级(各 boss 不同:常规 60,强袭老 boss 45/50;满档 90-100)。</summary>
    [JsonPropertyName("bossLevel")] public int BossLevel { get; set; }

    /// <summary>已通关最高难度,0..6;<b>0 = 未挑战</b>。等于该 boss 各难度记录里 passTime&gt;0 的最大难度。</summary>
    [JsonPropertyName("difficulty")] public int ClearedDifficulty { get; set; }

    /// <summary>方形头像(地区卡片用)。</summary>
    [JsonPropertyName("bossHeadIcon")] public string? BossHeadIcon { get; set; }

    /// <summary>大图立绘(详情 hero 用)。</summary>
    [JsonPropertyName("bossIconUrl")] public string? BossIconUrl { get; set; }

    /// <summary>所属地区 ID(<b>服务端拼作 contryId</b>,此处按拼写映射,不要"顺手改正")。</summary>
    [JsonPropertyName("contryId")] public int CountryId { get; set; }

    [JsonPropertyName("sort")] public int Sort { get; set; }
}

/// <summary>
/// 全息战略-boss 全难度记录(challengeDetails 接口)。
/// <para>
/// <c>challengeInfo</c> 是<b>字典</b>(键 = 字符串形式的 bossId),不是数组 ——
/// 前端压缩代码里的 <c>detailList[bossId]</c> 是字典取值,曾把它误读成「数组下标 = bossId」。
/// 每个值的数组长度 6,下标严格 = 难度-1(138/138 实测成立),但代码按
/// <see cref="HologramChallengeRecord.Difficulty"/> 排序消费,不依赖下标。
/// </para>
/// </summary>
public sealed class HologramDetailData
{
    [JsonPropertyName("isUnlock")] public bool? IsUnlock { get; set; }
    [JsonPropertyName("open")] public bool? Open { get; set; }

    /// <summary>bossId(字符串) → 该 boss 各难度记录。roleId 不存在时整个 data 为字符串 "null"。</summary>
    [JsonPropertyName("challengeInfo")]
    public Dictionary<string, List<HologramChallengeRecord>>? ChallengeInfo { get; set; }
}

/// <summary>
/// 全息战略-单个难度的挑战记录。
/// <para>
/// <see cref="ChallengeId"/> 与 <see cref="BossName"/> 目前<b>无消费方</b>
/// (界面用「难度 N」而不是关卡号;boss 名从索引取),保留是为了让响应形状完整、
/// 反序列化时字段不丢,便于排查接口变化。其余字段都有界面消费。
/// </para>
/// </summary>
public sealed class HologramChallengeRecord
{
    /// <summary>关卡 ID(实测 = bossId/10 + difficulty - 1)。当前界面未展示。</summary>
    [JsonPropertyName("challengeId")] public int ChallengeId { get; set; }

    /// <summary>难度档位 1..6(<b>不是</b>「已通关最高难度」)。</summary>
    [JsonPropertyName("difficulty")] public int Difficulty { get; set; }

    /// <summary>boss 名(与索引里的同名,界面上从索引取,此处仅保留响应形状)。</summary>
    [JsonPropertyName("bossName")] public string? BossName { get; set; }

    /// <summary>该难度的 boss 等级(随难度递增,如 60/65/70/80/90/100)。</summary>
    [JsonPropertyName("bossLevel")] public int BossLevel { get; set; }

    /// <summary>
    /// 通关用时<b>秒</b>;<b>0 = 未通关</b>(此时 <see cref="Roles"/> 整个键缺失,不会下发空数组)。
    /// 前端把它格式化成 MM:SS 展示,故这里同样按秒消费。
    /// </summary>
    [JsonPropertyName("passTime")] public int PassTime { get; set; }

    [JsonPropertyName("bossHeadIcon")] public string? BossHeadIcon { get; set; }

    /// <summary>该难度专属的立绘(每个难度一张,换难度时 hero 图会变)。</summary>
    [JsonPropertyName("bossIconUrl")] public string? BossIconUrl { get; set; }

    /// <summary>通关队伍(可选键:未通关时缺失)。实测非空时恒为 3 人。</summary>
    [JsonPropertyName("roles")] public List<HologramRole>? Roles { get; set; }
}

/// <summary>全息战略-通关队伍里的角色。</summary>
public sealed class HologramRole
{
    /// <summary>属性 ID 1..6 = 冷凝/热熔/导电/气动/衍射/湮灭(与 roleData 的 attributeId 同口径,
    /// 可直接复用本地 Assets/attr/{id}.png)。</summary>
    [JsonPropertyName("natureId")] public int NatureId { get; set; }

    [JsonPropertyName("roleName")] public string? RoleName { get; set; }

    [JsonPropertyName("roleLevel")] public int RoleLevel { get; set; }

    [JsonPropertyName("roleHeadIcon")] public string? RoleHeadIcon { get; set; }
}

/// <summary>
/// 全息战略解析辅助(纯函数,供单测)。
/// </summary>
public static class HologramParser
{
    /// <summary>全息战略的难度上限(界面「N/6」的分母,与关卡选择器的上界)。</summary>
    public const int MaxDifficulty = 6;

    /// <summary>
    /// 通关用时(秒) → <c>MM:SS</c>(≥1 小时时 <c>H:MM:SS</c>)。
    /// <para>
    /// 对齐官方 H5 的 <c>handlePassTime</c>/<c>formatSeconds</c>:补零到两位。
    /// <b>刻意不沿用</b> H5 另一处的「X小时X分钟」大粒度格式 ——
    /// 全息战略实测用时全部 &lt; 300 秒(中位 37s),按分钟取整会把 10 秒和 59 秒都显示成「&lt;1分钟」,
    /// 丢掉这份数据最有价值的部分(精确到秒的通关用时)。
    /// </para>
    /// <para>未通关(<paramref name="seconds"/> ≤ 0)返回空串,由调用方决定占位文案。</para>
    /// </summary>
    public static string FormatPassTime(int seconds)
    {
        if (seconds <= 0)
        {
            return "";
        }
        var hours = seconds / 3600;
        var minutes = seconds % 3600 / 60;
        var secs = seconds % 60;
        return hours > 0 ? $"{hours}:{minutes:00}:{secs:00}" : $"{minutes:00}:{secs:00}";
    }

    /// <summary>是否已通关该难度(实测 <c>PassTime &gt; 0</c> 与「roles 键存在」严格等价)。</summary>
    public static bool IsCleared(HologramChallengeRecord? record) => record is { PassTime: > 0 };

    /// <summary>难度是否已达上限(= 满档)。用于地区徽章的满档统计(悬停提示),不用于描边。</summary>
    public static bool IsFullyCleared(int clearedDifficulty) => clearedDifficulty >= MaxDifficulty;

    /// <summary>
    /// 地区排序(纯函数):按接口 <c>sort</c> 降序 —— 实测降序即 演武→同步→幻痛→强袭,
    /// 与界面四个 logo 菜单的顺序一致。缺失 country 的项丢弃(无 name/icon 展示不出来)。
    /// </summary>
    public static List<HologramCountryGroup> SortCountries(IEnumerable<HologramCountryGroup>? groups)
        => [.. (groups ?? [])
            .Where(g => g.Country is not null)
            .OrderByDescending(g => g.Sort)];

    /// <summary>boss 排序(纯函数):按 <c>sort</c> 降序(实测即新版 boss 在前)。</summary>
    public static List<HologramBossEntry> SortBosses(IEnumerable<HologramBossEntry>? bosses)
        => [.. (bosses ?? []).OrderByDescending(b => b.Sort)];

    /// <summary>
    /// 取某 boss 的 6 档记录(纯函数):从 <c>challengeInfo</c> 字典按 bossId 取值,按难度升序排列。
    /// <para>
    /// 字典键是字符串;缺失/为 null 时返回空列表(调用方据此展示「暂无挑战记录」)。
    /// 键拼装用 <see cref="CultureInfo.InvariantCulture"/>:接口的键恒为 ASCII 数字,
    /// 不能依赖宿主机的区域设置(避免任何区域相关的数字格式化把键拼错、导致整页"无记录")。
    /// 数组中的 JSON null 元素同样被丢弃(而不是让排序抛 NRE)。
    /// </para>
    /// <para>
    /// 按 <see cref="HologramChallengeRecord.Difficulty"/> 排序而非依赖数组下标 ——
    /// 下标=difficulty-1 只是当前实测成立,排序后对服务端调整顺序免疫。
    /// </para>
    /// </summary>
    public static List<HologramChallengeRecord> TiersOf(HologramDetailData? details, int bossId)
    {
        if (details?.ChallengeInfo is not { } info)
        {
            return [];
        }
        var key = bossId.ToString(CultureInfo.InvariantCulture);
        if (!info.TryGetValue(key, out var records) || records is null)
        {
            return [];
        }
        // 数组元素也可能是 JSON null(服务端空洞):直接 OrderBy 会在键选择器上 NRE,
        // 且异常会被 TowerViewModel.LoadAsync 的页面级 catch 吞掉,把"一个坏元素"放大成"整页签加载失败"。
        return [.. records.Where(r => r is not null).OrderBy(r => r.Difficulty)];
    }

    /// <summary>某地区里已满档(难度 6 通关)的 boss 数 —— 地区徽章的悬停提示用(菜单本身显示的是"已通关进度")。</summary>
    public static int CountFullyCleared(IEnumerable<HologramBossEntry>? bosses)
        => (bosses ?? []).Count(b => IsFullyCleared(b.ClearedDifficulty));
}

[JsonSerializable(typeof(HologramIndexData))]
[JsonSerializable(typeof(HologramCountryGroup))]
[JsonSerializable(typeof(List<HologramCountryGroup>))]
[JsonSerializable(typeof(HologramCountryInfo))]
[JsonSerializable(typeof(HologramBossEntry))]
[JsonSerializable(typeof(List<HologramBossEntry>))]
[JsonSerializable(typeof(HologramDetailData))]
[JsonSerializable(typeof(HologramChallengeRecord))]
[JsonSerializable(typeof(List<HologramChallengeRecord>))]
[JsonSerializable(typeof(Dictionary<string, List<HologramChallengeRecord>>))]
[JsonSerializable(typeof(HologramRole))]
public sealed partial class HologramJsonContext : JsonSerializerContext;
