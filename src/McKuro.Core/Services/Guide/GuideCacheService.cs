using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using McKuro.Core.Infrastructure;
using McKuro.Core.Models.Guide;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace McKuro.Core.Services.Guide;

/// <summary>攻略缓存条目(按「攻略账号 + cardRoleId」存一篇攻略的列表 + 当前详情;时间戳按 payload 分列)。</summary>
public sealed class GuideCacheEntry
{
    /// <summary>该角色的攻略列表(切换攻略下拉数据源;点赞降序)。</summary>
    public IReadOnlyList<GuideIntroductionItem> List { get; init; } = [];

    /// <summary>当前选中攻略的详情(达成度/推荐/共鸣链图标/技能达标)。</summary>
    public GuideIntroductionInfo? Detail { get; init; }

    /// <summary>当前选中攻略 id(0=未选)。</summary>
    public long SelectedId { get; init; }

    /// <summary>列表写入时间(UTC)。</summary>
    public DateTime ListUpdateTime { get; init; }

    /// <summary>详情写入时间(UTC)。</summary>
    public DateTime DetailUpdateTime { get; init; }
}

/// <summary>
/// mcguide 攻略数据本地缓存(SQLite <c>guide_cache</c> 表,主键 =「攻略账号 account_id + 库街区 cardRoleId」)。
/// <para>
/// 目的:攻略内容变化很慢(角色养成推荐基本只随版本/新攻略更新),没必要每次点角色都重拉攻略站。
/// 命中缓存直接秒开(含共鸣链图标、推荐标签、技能达标),未命中或过期再拉网络并回写。
/// </para>
/// <para>
/// 账号维度(评审反馈):detail_json 里的 isAcquired/currentLevel 是 per-account 达成度,
/// 只按 cardRoleId 定位会让换号用户命中上一账号快照,故 account_id(攻略账号 CUid|PlayerId)进主键。
/// </para>
/// <para>
/// 新鲜度按 payload 分列判定(评审反馈):list/detail/role_info 各有独立更新时间列,
/// 共用一列时任一 payload 刷新会把整行"续期",掩盖另一 payload 的过期。
/// 时间统一 UTC + 不变文化 roundtrip("O")格式,避免当前文化解析失败导致 TTL 判定漂移。
/// </para>
/// </summary>
public sealed class GuideCacheService
{
    /// <summary>缓存有效期(24 小时):攻略更新不频繁,一天一次足够,又不会长期过期。</summary>
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(24);

    private readonly AppDatabase _db;
    private readonly ILogger<GuideCacheService> _logger;

    public GuideCacheService(AppDatabase db, ILogger<GuideCacheService>? logger = null)
    {
        _db = db;
        _logger = logger ?? NullLogger<GuideCacheService>.Instance;
    }

    /// <summary>统一时间戳格式:UTC + 不变文化 roundtrip(评审反馈:当前文化本地时间在非公历文化下解析失败)。</summary>
    private static string NowStamp() => DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);

    private static DateTime ParseStamp(string text)
        => DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t)
            ? t
            : DateTime.MinValue;

    private static bool IsFresh(DateTime updateTime) => DateTime.UtcNow - updateTime < DefaultTtl;

    /// <summary>读取某账号+角色的攻略缓存;无记录返回 null。</summary>
    public GuideCacheEntry? TryGet(string accountId, int cardRoleId)
    {
        if (cardRoleId <= 0)
        {
            return null;
        }
        try
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText =
                """
                SELECT list_json, detail_json, selected_id, list_update_time, detail_update_time
                FROM guide_cache WHERE account_id = $acct AND card_role_id = $id
                """;
            cmd.Parameters.AddWithValue("$acct", accountId ?? "");
            cmd.Parameters.AddWithValue("$id", cardRoleId);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
            {
                return null;
            }
            var listJson = reader.GetString(0);
            var detailJson = reader.GetString(1);
            var selectedId = reader.GetInt64(2);
            var listTime = reader.GetString(3);
            var detailTime = reader.GetString(4);

            var list = string.IsNullOrWhiteSpace(listJson)
                ? []
                : JsonSerializer.Deserialize(listJson, GuideCacheJsonContext.Default.ListGuideIntroductionItem) ?? [];
            var detail = string.IsNullOrWhiteSpace(detailJson)
                ? null
                : JsonSerializer.Deserialize(detailJson, GuideCacheJsonContext.Default.GuideIntroductionInfo);
            return new GuideCacheEntry
            {
                List = list,
                Detail = detail,
                SelectedId = selectedId,
                ListUpdateTime = ParseStamp(listTime),
                DetailUpdateTime = ParseStamp(detailTime),
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "读取攻略缓存失败: cardRoleId={Id}", cardRoleId);
            return null;
        }
    }

    /// <summary>列表是否仍新鲜(24h 内有列表数据)。</summary>
    public bool IsListFresh(GuideCacheEntry? entry)
        => entry is { List.Count: > 0 } && IsFresh(entry.ListUpdateTime);

    /// <summary>
    /// 列表结果是否"已知且仍新鲜"—— <b>含确认无攻略</b>(行存在但列表为空)。
    /// 负缓存(评审反馈):无攻略/未收录角色若不以空列表为命中,每次进页都会重发请求。
    /// </summary>
    public bool IsListKnownFresh(GuideCacheEntry? entry)
        => entry is not null && IsFresh(entry.ListUpdateTime);

    /// <summary>详情是否仍新鲜(24h 内有详情数据)。</summary>
    public bool IsDetailFresh(GuideCacheEntry? entry)
        => entry is { Detail: not null } && IsFresh(entry.DetailUpdateTime);

    /// <summary>
    /// 写入/更新攻略列表(只动 list_json/list_update_time/selected_id,不碰已存详情)。
    /// </summary>
    public void SaveList(string accountId, int cardRoleId, IReadOnlyList<GuideIntroductionItem> list, long selectedId)
    {
        if (cardRoleId <= 0)
        {
            return;
        }
        try
        {
            var listJson = JsonSerializer.Serialize(list, GuideCacheJsonContext.Default.IReadOnlyListGuideIntroductionItem);
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText =
                """
                INSERT INTO guide_cache(account_id, card_role_id, list_json, selected_id, list_update_time)
                VALUES ($acct, $id, $list, $selected, $time)
                ON CONFLICT(account_id, card_role_id) DO UPDATE SET
                    list_json = $list, selected_id = $selected, list_update_time = $time
                """;
            cmd.Parameters.AddWithValue("$acct", accountId ?? "");
            cmd.Parameters.AddWithValue("$id", cardRoleId);
            cmd.Parameters.AddWithValue("$list", listJson);
            cmd.Parameters.AddWithValue("$selected", selectedId);
            cmd.Parameters.AddWithValue("$time", NowStamp());
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "写入攻略列表缓存失败(不影响本次展示): cardRoleId={Id}", cardRoleId);
        }
    }

    /// <summary>
    /// 写入/更新攻略详情(只动 detail_json/detail_update_time/selected_id,不碰已存列表)。
    /// <para>detail 为 null 时直接跳过:旧实现无条件覆写会把"列表成功/详情失败"场景下
    /// 上一次的好详情清成空(评审反馈)。</para>
    /// </summary>
    public void SaveDetail(string accountId, int cardRoleId, GuideIntroductionInfo? detail, long selectedId)
    {
        if (cardRoleId <= 0 || detail is null)
        {
            return;
        }
        try
        {
            var detailJson = JsonSerializer.Serialize(detail, GuideCacheJsonContext.Default.GuideIntroductionInfo);
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText =
                """
                INSERT INTO guide_cache(account_id, card_role_id, detail_json, selected_id, detail_update_time)
                VALUES ($acct, $id, $detail, $selected, $time)
                ON CONFLICT(account_id, card_role_id) DO UPDATE SET
                    detail_json = $detail, selected_id = $selected, detail_update_time = $time
                """;
            cmd.Parameters.AddWithValue("$acct", accountId ?? "");
            cmd.Parameters.AddWithValue("$id", cardRoleId);
            cmd.Parameters.AddWithValue("$detail", detailJson);
            cmd.Parameters.AddWithValue("$selected", selectedId);
            cmd.Parameters.AddWithValue("$time", NowStamp());
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "写入攻略详情缓存失败(不影响本次展示): cardRoleId={Id}", cardRoleId);
        }
    }

    /// <summary>
    /// 清理孤儿缓存(换号/删角色后):删除其他账号的全部行 + 本账号不在当前角色列表中的行。
    /// 空集合表示"未知当前角色集合",此时只清其他账号、不动本账号(避免误删)。
    /// </summary>
    public void PruneExcept(string accountId, IReadOnlyCollection<int> keepCardRoleIds)
    {
        try
        {
            using var cmd = _db.Connection.CreateCommand();
            if (keepCardRoleIds.Count == 0)
            {
                cmd.CommandText = "DELETE FROM guide_cache WHERE account_id <> $acct";
                cmd.Parameters.AddWithValue("$acct", accountId ?? "");
            }
            else
            {
                // 逐 id 参数化(评审反馈:NOT IN 拼接虽为 int 无注入面,但违背全参数化约定、模式脆弱)
                var idParams = keepCardRoleIds.Select((_, i) => $"$k{i}").ToList();
                cmd.CommandText =
                    $"DELETE FROM guide_cache WHERE account_id <> $acct OR card_role_id NOT IN ({string.Join(",", idParams)})";
                cmd.Parameters.AddWithValue("$acct", accountId ?? "");
                var index = 0;
                foreach (var id in keepCardRoleIds)
                {
                    cmd.Parameters.AddWithValue($"$k{index}", id);
                    index++;
                }
            }
            var removed = cmd.ExecuteNonQuery();
            if (removed > 0)
            {
                _logger.LogInformation("清理失效攻略缓存 {Count} 条(换账号/角色已不在当前列表)", removed);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "清理攻略缓存失败(忽略)");
        }
    }

    /// <summary>角色资料缓存条目(role/info:技能演示视频 + 角色特点图标)。</summary>
    public sealed class RoleInfoCacheEntry
    {
        public GuideRoleInfoData? Data { get; init; }
        public DateTime UpdateTime { get; init; }
    }

    /// <summary>读取角色资料缓存(role/info);无记录返回 null。</summary>
    public RoleInfoCacheEntry? TryGetRoleInfo(string accountId, int cardRoleId)
    {
        if (cardRoleId <= 0)
        {
            return null;
        }
        try
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText =
                "SELECT role_info_json, role_info_update_time FROM guide_cache WHERE account_id = $acct AND card_role_id = $id";
            cmd.Parameters.AddWithValue("$acct", accountId ?? "");
            cmd.Parameters.AddWithValue("$id", cardRoleId);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
            {
                return null;
            }
            var json = reader.GetString(0);
            var time = reader.GetString(1);
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }
            return new RoleInfoCacheEntry
            {
                Data = JsonSerializer.Deserialize(json, GuideCacheJsonContext.Default.GuideRoleInfoData),
                UpdateTime = ParseStamp(time),
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "读取角色资料缓存失败: cardRoleId={Id}", cardRoleId);
            return null;
        }
    }

    /// <summary>角色资料缓存是否新鲜(24h 内且有数据;独立时间列,不再被攻略保存"续命")。</summary>
    public bool IsRoleInfoFresh(RoleInfoCacheEntry? entry)
        => entry is { Data: not null } && IsFresh(entry.UpdateTime);

    /// <summary>
    /// 写入角色资料(role/info)缓存。与攻略列表/详情共用同一行,
    /// 但只更新 role_info_json/role_info_update_time 两列(评审反馈:
    /// 旧实现刷新共用 update_time,把超期攻略判新鲜;分列后互不影响)。
    /// </summary>
    public void SaveRoleInfo(string accountId, int cardRoleId, GuideRoleInfoData data)
    {
        if (cardRoleId <= 0 || data is null)
        {
            return;
        }
        try
        {
            var json = JsonSerializer.Serialize(data, GuideCacheJsonContext.Default.GuideRoleInfoData);
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText =
                """
                INSERT INTO guide_cache(account_id, card_role_id, role_info_json, role_info_update_time)
                VALUES ($acct, $id, $info, $time)
                ON CONFLICT(account_id, card_role_id) DO UPDATE SET role_info_json = $info, role_info_update_time = $time
                """;
            cmd.Parameters.AddWithValue("$acct", accountId ?? "");
            cmd.Parameters.AddWithValue("$id", cardRoleId);
            cmd.Parameters.AddWithValue("$info", json);
            cmd.Parameters.AddWithValue("$time", NowStamp());
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "写入角色资料缓存失败(不影响展示): cardRoleId={Id}", cardRoleId);
        }
    }
}

[JsonSerializable(typeof(GuideIntroductionInfo))]
[JsonSerializable(typeof(GuideRoleInfoData))]
[JsonSerializable(typeof(List<GuideIntroductionItem>))]
[JsonSerializable(typeof(IReadOnlyList<GuideIntroductionItem>))]
public sealed partial class GuideCacheJsonContext : JsonSerializerContext;
