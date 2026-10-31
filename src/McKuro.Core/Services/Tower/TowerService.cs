using McKuro.Core.Infrastructure;
using McKuro.Core.Models.Kuro;
using McKuro.Core.Models.Tower;
using McKuro.Core.Services.Game;
using McKuro.Core.Services.Kuro;
using McKuro.Core.Services.Notification;
using McKuro.Core.Services.Roles;
using McKuro.Core.Services.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

namespace McKuro.Core.Services.Tower;

/// <summary>
/// 深塔/海墟页一次拉取的全部数据。
/// <para>
/// 原先是 5 元组返回;加入全息战略后到 7 项,元组已经读不出含义(多个 <c>?Data</c> 类型相邻),
/// 故改为具名记录:<see cref="Error"/> 非空即表示本次整体失败(未登录/无令牌),各数据成员可能为 null。
/// </para>
/// </summary>
/// <param name="Tower">逆境深塔(towerDataDetail)。</param>
/// <param name="NewTower">终焉矩阵(newTowerDetail)。</param>
/// <param name="Slash">再生海域(slashDetail)。</param>
/// <param name="HologramIndex">全息战略总览(challengeIndex)。</param>
/// <param name="HologramDetails">全息战略各难度记录(challengeDetails)。</param>
/// <param name="Explore">地图探索(exploreIndex;页签是附加的,失败不影响其余页签)。</param>
/// <param name="Error">整体失败原因;空串表示成功。</param>
/// <param name="RoleId">本次解析出的库街区角色条目 ID(矩阵历史按它落库)。</param>
public sealed record TowerDataResult(
    TowerSeasonData? Tower,
    NewTowerData? NewTower,
    SlashData? Slash,
    HologramIndexData? HologramIndex,
    HologramDetailData? HologramDetails,
    ExploreIndexData? Explore,
    string Error,
    string RoleId);

/// <summary>
/// 赛季临期提醒用的轻量快照:只含三个**赛季制**玩法(逆境深塔/冥歌海墟/终焉矩阵)。
/// <para>
/// 提醒链路<see cref="TowerService.GetSeasonSnapshotAsync"/> 刻意不复用 <see cref="TowerDataResult"/>:
/// 全息战略/地图探索那三个接口对本功能毫无用处,却要额外 1.2 秒间隔并放大库街区风控概率
/// (同一账号短时间串行打 6 个数据中心接口)。提醒每几小时跑一次,只取必要数据。
/// </para>
/// </summary>
/// <param name="Tower">逆境深塔(towerDataDetail)。</param>
/// <param name="Slash">冥歌海墟(slashDetail)。</param>
/// <param name="Matrix">终焉矩阵(newTowerDetail)。</param>
/// <param name="Error">整体失败原因;空串表示成功(单项失败只让该成员为 null)。</param>
/// <param name="RoleId">本次解析出的库街区角色条目 ID。</param>
public sealed record TowerSeasonSnapshot(
    TowerSeasonData? Tower,
    SlashData? Slash,
    NewTowerData? Matrix,
    string Error,
    string RoleId);

/// <summary>
/// 深塔/海墟数据服务:用库街区 accessToken 拉取逆境深塔、终焉矩阵、再生海域与全息战略数据。
/// (对齐 WutheringWavesTool TowerDataDetailTask / NewTowerDataDetailTask / SlashDataDetailTask,
/// 全息战略对齐官方 H5 的 challengeIndex / challengeDetails)
/// </summary>
public sealed class TowerService
{
    private readonly KujiequApiClient _api;
    private readonly IKuroClient _kuro;
    private readonly KuroAccountService _accounts;
    private readonly AppDatabase? _database;
    private readonly ISettingsService? _settings;
    private readonly ILogger<TowerService> _logger;

    /// <param name="settings">
    /// 应用设置(可选):用其中配置的 <see cref="AppSettings.RoleId"/> 在账号绑定的多个角色中选取
    /// 与角色页一致的那一个;未注入或未配置时回退到第一个角色(旧行为)。
    /// </param>
    public TowerService(
        KujiequApiClient api,
        IKuroClient kuro,
        KuroAccountService accounts,
        ILogger<TowerService>? logger = null,
        AppDatabase? database = null,
        ISettingsService? settings = null)
    {
        _api = api;
        _kuro = kuro;
        _accounts = accounts;
        _logger = logger ?? NullLogger<TowerService>.Instance;
        _database = database;
        _settings = settings;
    }

    /// <summary>
    /// 从账号绑定的角色列表中选取本次使用的角色:优先匹配配置的角色 ID(与 RoleDataService 同口径),
    /// 未配置或列表中没有该 ID 时回退到第一个。
    /// <para>旧实现直接取 <c>Data[0]</c>:多角色账号下深塔页与角色页显示的是不同角色(评审反馈)。</para>
    /// </summary>
    internal static GameRoilDataItem PickRole(IReadOnlyList<GameRoilDataItem> roles, string? configuredRoleId)
    {
        if (!string.IsNullOrWhiteSpace(configuredRoleId))
        {
            var wanted = configuredRoleId.Trim();
            foreach (var r in roles)
            {
                if (string.Equals(r.RoleId, wanted, StringComparison.Ordinal))
                {
                    return r;
                }
            }
        }
        return roles[0];
    }

    /// <summary>当前配置的角色 ID(未注入设置服务时为 null)。</summary>
    private string? ConfiguredRoleId => _settings?.Current.RoleId;

    /// <summary>
    /// 拉取深塔、海墟与全息战略数据;<see cref="TowerDataResult.Error"/> 非空表示失败
    /// (未登录/无 accessToken/接口异常)。
    /// RoleId 为本次解析的库街区角色条目 ID(矩阵历史按它落库)。
    /// </summary>
    public async Task<TowerDataResult> GetTowerDataAsync(CancellationToken ct = default)
    {
        // 诊断"深塔/海墟数据不刷新"这类问题:每次真正发起拉取都留一行(含调用时刻),
        // 若进入页面后日志没有新行,说明是该页面根本没重新拉(reload 触发问题),不是接口/缓存问题。
        _logger.LogInformation("Tower: 开始拉取深塔/海墟数据 {Time:HH:mm:ss.fff}", DateTime.Now);
        var account = _accounts.Current;
        if (account is null)
        {
            return Failed(CoreStrings.T("Core.Tower.NoLogin", "请先登录库街区账号"));
        }

        // 复用角色数据服务的 accessToken 换取流程
        var deviceId = account.DeviceId ?? Guid.NewGuid().ToString("N");
        var gamer = await _kuro.GetGamerAsync(account, (int)KuroGameType.Waves, ct).ConfigureAwait(false);
        if (gamer is not { Code: 200, Data: not null } || gamer.Data.Count == 0)
        {
            return Failed(CoreStrings.T("Core.Tower.RoleListFailed", "获取角色列表失败"));
        }
        var role = PickRole(gamer.Data, ConfiguredRoleId);
        var roleId = role.RoleId ?? "";
        _logger.LogInformation("Tower: 角色列表 {Count} 个,本次使用 roleId={RoleId} roleName={RoleName} serverId={ServerId}",
            gamer.Data.Count, roleId, role.RoleName, role.ServerId);
        if (string.IsNullOrEmpty(roleId))
        {
            return Failed(CoreStrings.T("Core.Tower.NoRoleId", "角色 ID 为空"));
        }

        _api.PublicIp = _kuro.Ip;
        var accessToken = await _api.GetAccessTokenAsync(
            account.Token, deviceId, roleId, account.UserId ?? "", "android", ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(accessToken))
        {
            return Failed(CoreStrings.T("Core.Tower.TokenFailed", "获取访问令牌失败(Token 可能已失效)"), roleId);
        }

        TowerSeasonData? tower = null;
        NewTowerData? newTower = null;
        SlashData? slash = null;
        HologramIndexData? hologramIndex = null;
        HologramDetailData? hologramDetails = null;
        ExploreIndexData? explore = null;
        try
        {
            tower = await _api.GetTowerAsync(accessToken, deviceId, roleId, "android", ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // 调用方取消必须向上传播(评审反馈):吞掉会让已取消的加载继续串行打后面 5 个接口。
            // 令牌未取消的 OCE(HttpClient 超时)仍按单项失败处理,不连累其余页签。
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "拉取逆境深塔数据失败");
        }
        try
        {
            newTower = await _api.GetNewTowerAsync(accessToken, deviceId, roleId, "android", ct).ConfigureAwait(false);
            if (newTower is not null)
            {
                SaveNewTowerHistory(roleId, newTower);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "拉取终焉矩阵数据失败");
        }
        try
        {
            slash = await _api.GetSlashAsync(accessToken, deviceId, roleId, "android", ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "拉取海墟数据失败");
        }
        try
        {
            hologramIndex = await _api.GetChallengeIndexAsync(accessToken, deviceId, roleId, "android", ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 取消语义必须保留:吞掉 OCE 会把"用户已离开/取消"变成一条误导性的"拉取失败"警告,
            // 并让已取消的加载继续往下跑。(相邻的 details 块同理,两块保持同一写法。)
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "拉取全息战略总览失败");
        }
        // 两个接口之间留间隔:数据中心接口有风控,连续密集请求易触发极验(官方 H5 自己也是分次请求)
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(600), ct).ConfigureAwait(false);
            hologramDetails = await _api.GetChallengeDetailsAsync(accessToken, deviceId, roleId, "android", ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "拉取全息战略挑战记录失败");
        }
        // 地图探索(exploreIndex):与上面四个接口同一个 accessToken。
        // 中间再留一次间隔——同一账号在短时间内串行请求 6 个数据中心接口,密集时容易触发极验;
        // 探索度只是本页的附加页签,宁可慢半秒,也不要因为它把整页拖进风控。
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(600), ct).ConfigureAwait(false);
            explore = await _api.GetExploreIndexAsync(accessToken, deviceId, roleId, "android", ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "拉取地图探索数据失败");
        }

        if (tower is null && newTower is null && slash is null && hologramIndex is null && hologramDetails is null
            && explore is null)
        {
            return Failed(CoreStrings.T("Core.Tower.EmptyData", "接口返回空数据(可能受风控)"), roleId);
        }
        // 各接口的关键量(诊断"某页签数据不刷新":能区分"没重新拉"还是"接口返回的就是旧值")
        // 注意 seasonEndTime/endTime 是"剩余毫秒"而非绝对时间,这里折算成剩余时长便于阅读
        static string Remain(long? ms) => ms is { } v
            ? TimeSpan.FromMilliseconds(v) is { TotalDays: > 0 } t ? $"{t.Days}天{t.Hours}小时" : $"{v / 3600000.0:F1}小时"
            : "无";
        _logger.LogInformation(
            "Tower: 拉取完成 | 深塔:难度{TD}个 剩余{TSE} | 矩阵:模式{NTM}个/共{NTAll}个 剩余{NTSE} | 海墟:难度{SDR}个 关卡{SC}关 剩余{SSE}",
            tower?.DifficultyList?.Count ?? -1, Remain(tower?.SeasonEndTime),
            newTower?.ModeDetails?.Count(m => m.HasRecord && m.Score > 0) ?? -1, newTower?.ModeDetails?.Count ?? -1, Remain(newTower?.EndTime),
            slash?.DifficultyList?.Count ?? -1,
            slash?.DifficultyList?.Sum(d => d.ChallengeList?.Count ?? 0) ?? -1, Remain(slash?.SeasonEndTime));
        _logger.LogInformation(
            "Tower: 海墟难度明细(难度:总积分/满积分) {Detail}",
            string.Join(" | ", slash?.DifficultyList?.Select(d => $"{d.Difficulty}:{d.AllScore}/{d.MaxScore}") ?? []));
        // 全息战略:地区数/boss 数/满档数 + 有记录的难度条数(便于核对"卡片角标与详情是否一致")
        _logger.LogInformation(
            "Tower: 全息战略 地区{HC}个 boss{HB}个 满档{HF}个 | 详情 boss{HDB}个 有记录难度{HDR}档",
            hologramIndex?.ChallengeList?.Count ?? -1,
            hologramIndex?.ChallengeList?.Sum(c => c.IndexList?.Count ?? 0) ?? -1,
            hologramIndex?.ChallengeList?.Sum(c => HologramParser.CountFullyCleared(c.IndexList)) ?? -1,
            hologramDetails?.ChallengeInfo?.Count ?? -1,
            hologramDetails?.ChallengeInfo?.Values.Sum(v => v.Count(HologramParser.IsCleared)) ?? -1);
        // 原始值:用于判定 seasonEndTime/endTime 究竟是"剩余毫秒"还是绝对时间戳
        // (剩余毫秒按 now+值 折算成结束时刻;若原值本身就是 epoch 毫秒,这里的绝对值会接近 1.7e12)
        static string RawMs(long? ms) => ms is { } v
            ? $"{v} (≈now{(v >= 0 ? "+" : "-")}{TimeSpan.FromMilliseconds(Math.Abs(v)).TotalDays:F1}天)"
            : "null";
        _logger.LogInformation(
            "Tower: 原始时间字段 | 深塔.seasonEndTime={TSE} | 矩阵.endTime={NTE} | 海墟.seasonEndTime={SSE}",
            RawMs(tower?.SeasonEndTime), RawMs(newTower?.EndTime), RawMs(slash?.SeasonEndTime));
        _logger.LogInformation(
            "Tower: 地图探索 国家{EC}个 区域{EA}个(接口无数据时该页签显示「暂无探索数据」)",
            explore?.ExploreList?.Count ?? -1,
            explore?.ExploreList?.Sum(c => c.AreaInfoList?.Count ?? 0) ?? -1);
        return new TowerDataResult(tower, newTower, slash, hologramIndex, hologramDetails, explore, "", roleId);
    }

    /// <summary>整体失败结果(各数据成员为 null,只带原因)。</summary>
    private static TowerDataResult Failed(string error, string roleId = "")
        => new(null, null, null, null, null, null, error, roleId);

    /// <summary>
    /// 赛季临期提醒专用:只拉三个赛季制玩法(深塔/海墟/矩阵)的结束时间与完成度。
    /// <para>
    /// 与 <see cref="GetTowerDataAsync"/> 共用同一套 accessToken 换取流程,但不请求全息战略/地图探索,
    /// 也不落库矩阵历史 —— 提醒链路的副作用必须为零,否则"后台每几小时跑一次"会变成
    /// "后台每几小时往历史表里塞一条记录"。
    /// </para>
    /// <para>整体失败(未登录/令牌失效)时 <see cref="TowerSeasonSnapshot.Error"/> 非空;单项失败静默为 null。</para>
    /// </summary>
    public async Task<TowerSeasonSnapshot> GetSeasonSnapshotAsync(CancellationToken ct = default)
    {
        var account = _accounts.Current;
        if (account is null)
        {
            return new TowerSeasonSnapshot(null, null, null, CoreStrings.T("Core.Tower.NoLogin", "请先登录库街区账号"), "");
        }

        var deviceId = account.DeviceId ?? Guid.NewGuid().ToString("N");
        var gamer = await _kuro.GetGamerAsync(account, (int)KuroGameType.Waves, ct).ConfigureAwait(false);
        if (gamer is not { Code: 200, Data: not null } || gamer.Data.Count == 0)
        {
            return new TowerSeasonSnapshot(null, null, null, CoreStrings.T("Core.Tower.RoleListFailed", "获取角色列表失败"), "");
        }
        var roleId = PickRole(gamer.Data, ConfiguredRoleId).RoleId ?? "";
        if (string.IsNullOrEmpty(roleId))
        {
            return new TowerSeasonSnapshot(null, null, null, CoreStrings.T("Core.Tower.NoRoleId", "角色 ID 为空"), "");
        }

        _api.PublicIp = _kuro.Ip;
        var accessToken = await _api.GetAccessTokenAsync(
            account.Token, deviceId, roleId, account.UserId ?? "", "android", ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(accessToken))
        {
            return new TowerSeasonSnapshot(null, null, null,
                CoreStrings.T("Core.Tower.TokenFailed", "获取访问令牌失败(Token 可能已失效)"), roleId);
        }

        TowerSeasonData? tower = null;
        SlashData? slash = null;
        NewTowerData? matrix = null;
        // 三个接口各自吞异常:任一失败只让该项缺席,不影响另外两项的提醒判定
        try
        {
            tower = await _api.GetTowerAsync(accessToken, deviceId, roleId, "android", ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "提醒: 拉取逆境深塔赛季数据失败");
        }
        try
        {
            matrix = await _api.GetNewTowerAsync(accessToken, deviceId, roleId, "android", ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "提醒: 拉取终焉矩阵赛季数据失败");
        }
        // 与页面链路一致:两个数据中心接口之间留间隔,降低极验概率
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(600), ct).ConfigureAwait(false);
            slash = await _api.GetSlashAsync(accessToken, deviceId, roleId, "android", ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "提醒: 拉取冥歌海墟赛季数据失败");
        }

        if (tower is null && slash is null && matrix is null)
        {
            return new TowerSeasonSnapshot(null, null, null,
                CoreStrings.T("Core.Tower.EmptyData", "接口返回空数据(可能受风控)"), roleId);
        }
        static string Remain(long? ms) => ms is { } v
            ? TimeSpan.FromMilliseconds(v) is { TotalDays: > 0 } t2 ? $"{t2.Days}天{t2.Hours}小时" : $"{v / 3600000.0:F1}小时"
            : "无";
        _logger.LogInformation(
            "提醒: 赛季剩余 | 深塔{TSE} | 矩阵{NTE} | 海墟{SSE}",
            Remain(tower?.SeasonEndTime), Remain(matrix?.EndTime), Remain(slash?.SeasonEndTime));
        return new TowerSeasonSnapshot(tower, slash, matrix, "", roleId);
    }

    /// <summary>
    /// 终焉矩阵本期有记录时落库一期历史(key=roleId+赛季结束时间,对齐 WutheringWavesTool saveToDB)。
    /// 赛季结束绝对时间 = 当前时间 + 剩余毫秒,归整到当天 04:00(convertToHourlyTimestamp 同款)。
    /// </summary>
    internal void SaveNewTowerHistory(string roleId, NewTowerData newTower)
    {
        if (_database is null)
        {
            return;
        }
        try
        {
            var modes = newTower.ModeDetails?
                .Where(d => d.HasRecord && d.Score > 0)
                .ToList();
            if (modes is not { Count: > 0 })
            {
                return;
            }
            // 剩余毫秒缺失时退化为"当前时间归整",保证仍能落一条历史
            var remaining = newTower.EndTime ?? 0;
            var end = NormalizeToServerHour4(DateTime.UtcNow.AddMilliseconds(remaining));
            var json = JsonSerializer.Serialize(modes, TowerJsonContext.Default.ListNewTowerModeDetail);
            _database.UpsertNewTowerHistory(roleId, end, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "终焉矩阵历史落库失败: {RoleId}", roleId);
        }
    }

    /// <summary>归整到当天 04:00(对齐 WutheringWavesTool convertToHourlyTimestamp)。</summary>
    internal static long NormalizeToHour4(DateTime local)
    {
        var atFour = new DateTime(local.Year, local.Month, local.Day, 4, 0, 0, local.Kind);
        return new DateTimeOffset(atFour).ToUnixTimeMilliseconds();
    }

    /// <summary>
    /// 按<b>服务器时间</b>归整到当天 04:00(赛季在服务器 04:00 结束),返回 Unix 毫秒。
    /// <para>旧实现按本机时间归整:非 UTC+8 时区里服务器 04:00 可能落在本机午夜附近,
    /// 剩余毫秒的秒级抖动会让归整日期在两天之间翻转,同一期落出两行历史(评审反馈)。
    /// 国服(UTC+8)用户本机即服务器时间,结果与旧实现一致,已有历史行不受影响。</para>
    /// </summary>
    internal static long NormalizeToServerHour4(DateTime time)
    {
        var server = ReminderWindows.ToServerTime(time);
        var atFour = new DateTimeOffset(
            server.Year, server.Month, server.Day, ReminderWindows.ResetHour, 0, 0, ReminderWindows.ServerUtcOffset);
        return atFour.ToUnixTimeMilliseconds();
    }
}
