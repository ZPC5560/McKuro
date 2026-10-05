using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using McKuro.Core.Models.User;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace McKuro.Core.Services.User;

/// <summary>
/// 本地游戏启动器每日数据服务(参考 Haiyu WavesV2):
/// 读取 PC 启动器本地 OAuth 缓存(%AppData%\KR_G152\{PKGId}\KRSDKUserLauncherCache.json),
/// XOR 解密 oauthCode 后调官方 PC 启动器 SDK(查询玩家→查询角色)获取每日数据(体力/活跃度/等级等)。
/// <para>优点:不依赖库街区账号登录,读游戏本地缓存即可。</para>
/// </summary>
public sealed class LocalGameDailyDataService
{
    private const string SdkBase = "https://pc-launcher-sdk-api.kurogame.com";
    private const string GameId = "G152";
    private const string PkgId = "A1381";

    private readonly HttpClient _http;
    private readonly ILogger<LocalGameDailyDataService> _logger;

    public LocalGameDailyDataService(HttpClient http, ILogger<LocalGameDailyDataService>? logger = null)
    {
        _http = http;
        _logger = logger ?? NullLogger<LocalGameDailyDataService>.Instance;
    }

    /// <summary>读取本地游戏 OAuth 缓存(明文 JSON 列表)。</summary>
    public async Task<List<LauncherCacheAccount>?> ReadCacheAsync(CancellationToken ct = default)
    {
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var path = Path.Combine(appData, $"KR_{GameId}", PkgId, "KRSDKUserLauncherCache.json");
            if (!File.Exists(path))
            {
                return null;
            }
            var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
            return JsonSerializer.Deserialize(json, LocalDailyJsonContext.Default.ListLauncherCacheAccount);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "读取本地游戏缓存失败");
            return null;
        }
    }

    /// <summary>XOR 解密 oauthCode(每字符异或 key)。</summary>
    public static string XorDecrypt(string data, int key)
    {
        if (string.IsNullOrEmpty(data))
        {
            return "";
        }
        var sb = new StringBuilder(data.Length);
        foreach (var c in data)
        {
            sb.Append((char)(c ^ key));
        }
        return sb.ToString();
    }

    /// <summary>AOT 安全的 JSON 字符串转义(避免 JsonSerializer 反射警告)。</summary>
    private static string EscapeJson(string value)
        => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    /// <summary>
    /// 枚举本地启动器登录凭证对应的<b>全部</b>游戏角色(对齐 Haiyu 的账号卡片列表)。
    /// 每个凭证各一次 queryPlayerInfo;一个凭证下有多个服务器角色时全部列出。
    /// 单个凭证失败(风控/失效)跳过,不影响其余。
    /// </summary>
    public async Task<List<LocalLauncherPlayer>> GetLocalPlayersAsync(CancellationToken ct = default)
    {
        var result = new List<LocalLauncherPlayer>();
        var accounts = await ReadCacheAsync(ct).ConfigureAwait(false);
        if (accounts is null)
        {
            return result;
        }
        foreach (var account in accounts)
        {
            foreach (var (player, server) in await QueryPlayersAsync(account, ct).ConfigureAwait(false))
            {
                if (string.IsNullOrEmpty(player.RoleId))
                {
                    continue;
                }
                result.Add(new LocalLauncherPlayer
                {
                    KuroUid = ToUid(account),
                    Username = account.Username ?? "",
                    RoleId = player.RoleId!,
                    RoleName = player.RoleName ?? "",
                    ServerName = server,
                    Level = player.Level,
                    Phone = account.Phone ?? "",
                });
            }
        }
        return result;
    }

    /// <summary>
    /// 取<b>指定</b>本地凭证对应角色的每日数据(<paramref name="bindKey"/> = <see cref="LocalLauncherPlayer.BindKey"/>)。
    /// 这是首页账号切换能真正换数据的关键:自动模式只会返回"第一个成功的角色",
    /// 而绑定某个具体角色时必须按该账号的 oauthCode 重新走一遍 SDK(对齐 Haiyu 的 QueryRoleInfo)。
    /// </summary>
    public async Task<RoleDailyData?> GetDailyDataForAsync(string bindKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(bindKey))
        {
            return null;
        }
        var parts = bindKey.Split('|', 2);
        var kuroUid = parts[0];
        var wantedRoleId = parts.Length > 1 ? parts[1] : "";
        var accounts = await ReadCacheAsync(ct).ConfigureAwait(false);
        var account = accounts?.FirstOrDefault(a => ToUid(a) == kuroUid);
        if (account is null)
        {
            _logger.LogInformation("本地启动器缓存中找不到账号 {Uid}", kuroUid);
            return null;
        }
        foreach (var (player, server) in await QueryPlayersAsync(account, ct).ConfigureAwait(false))
        {
            // 指定了角色就只取那个角色(同账号多服务器时不会串号);没指定则取第一个成功的
            if (wantedRoleId.Length > 0 && player.RoleId != wantedRoleId)
            {
                continue;
            }
            var daily = await QueryRoleAsync(account, player, server, ct).ConfigureAwait(false);
            if (daily is not null)
            {
                return daily;
            }
        }
        return null;
    }

    /// <summary>缓存里的 id 是 JSON 数字(Haiyu 用 double 接收),统一转成不带小数的字符串 UID。</summary>
    internal static string ToUid(LauncherCacheAccount account)
        => ((long)account.Id).ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// 指定库街区凭证(账号 UID)是否<b>仍存在</b>于本地启动器缓存中。
    /// <para>
    /// 纯文件读取、<b>不走网络</b> —— 专门用于区分两件看起来一样的事:
    /// 「该账号已从启动器登出/移除」与「本次 queryPlayerInfo 失败(1005 限流/瞬时错误)导致它没被枚举出来」。
    /// 后者发生在 <see cref="QueryPlayersAsync"/> 里会被静默跳过而其余账号照常返回,
    /// 于是"枚举结果里没有这个账号"并不能证明它真的没了。
    /// </para>
    /// </summary>
    /// <returns>
    /// <c>true</c> 凭证仍在;<c>false</c> 凭证确实已不在缓存列表中;
    /// <c>null</c> = <b>无法判定</b>(启动器缓存文件不存在或读取失败)—— 调用方应保守处理,
    /// 不要据此清除用户已保存的绑定。
    /// </returns>
    public async Task<bool?> HasCredentialAsync(string kuroUid, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(kuroUid))
        {
            return null;
        }
        var accounts = await ReadCacheAsync(ct).ConfigureAwait(false);
        if (accounts is null)
        {
            return null; // 无文件/读取异常:不判定
        }
        return HasCredential(accounts, kuroUid);
    }

    /// <summary>
    /// <see cref="HasCredentialAsync"/> 的纯判定部分(便于单测:不碰文件系统)。
    /// <paramref name="accounts"/> 为 null 表示"无法判定",返回 null。
    /// </summary>
    public static bool? HasCredential(IReadOnlyList<LauncherCacheAccount>? accounts, string? kuroUid)
    {
        if (accounts is null || string.IsNullOrWhiteSpace(kuroUid))
        {
            return null;
        }
        return accounts.Any(a => ToUid(a) == kuroUid);
    }

    // ---- 短时缓存:账号页三张卡片 + 签到页 + 首页都要枚举同一批本地角色 ----
    // 每个页面各查一次会让 queryPlayerInfo 被重复调用(实测该接口有 1005 限流),
    // 且换页时明显卡顿;角色昵称/UID 在一次运行内不会变化,故做进程内短缓存。
    private static readonly TimeSpan LocalPlayersCacheTtl = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 上一次枚举"没拿到任何角色"后,多久内不再重试。
    /// <para>
    /// 没有这个退避时,<see cref="InvalidateLocalPlayersCache"/> 只把成功时间戳打到 MinValue、
    /// 而失败的枚举不写回时间戳,于是此后<b>每次</b>读缓存都会重新跑一轮全部凭证的
    /// queryPlayerInfo(每个还有重试与退避)—— 在已经被限流的情况下反而放大限流。
    /// </para>
    /// </summary>
    private static readonly TimeSpan LocalPlayersFailureRetryDelay = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _localPlayersGate = new(1, 1);
    private List<LocalLauncherPlayer>? _localPlayersCache;
    private DateTimeOffset _localPlayersCachedAt;

    /// <summary>上一次"枚举到空结果"的时刻(用于失败退避;成功时清回 MinValue)。</summary>
    private DateTimeOffset _localPlayersFailedAt = DateTimeOffset.MinValue;

    /// <summary>
    /// 带短缓存的 <see cref="GetLocalPlayersAsync"/>:并发调用只发一轮请求,
    /// 失败时沿用上一次成功结果(未装启动器/无凭证属正常状态,不抛异常)。
    /// </summary>
    public async Task<List<LocalLauncherPlayer>> GetLocalPlayersCachedAsync(CancellationToken ct = default)
        => (await GetLocalPlayersCoreAsync(ignoreTtl: false, ct).ConfigureAwait(false)).Players;

    /// <summary>
    /// <b>强制</b>重新枚举(忽略短缓存),并如实告知结果是否来自本次枚举。
    /// <para>
    /// 供首页账号弹窗的「刷新」使用:同手机号/账号集合都没变时需能区分
    /// 「检测完成且无变化」与「本次没检测成功(沿用旧数据)」——
    /// 只看列表内容无法区分这两者,会把失败误报成"无变化"。
    /// </para>
    /// </summary>
    /// <returns><c>Players</c> 可用列表;<c>Fresh</c> = 本次枚举确实成功(而非沿用旧缓存)。</returns>
    public async Task<(List<LocalLauncherPlayer> Players, bool Fresh)> RefreshLocalPlayersAsync(
        CancellationToken ct = default)
        => await GetLocalPlayersCoreAsync(ignoreTtl: true, ct).ConfigureAwait(false);

    private async Task<(List<LocalLauncherPlayer> Players, bool Fresh)> GetLocalPlayersCoreAsync(
        bool ignoreTtl, CancellationToken ct)
    {
        if (!ignoreTtl && _localPlayersCache is { } cached
            && DateTimeOffset.UtcNow - _localPlayersCachedAt < LocalPlayersCacheTtl)
        {
            return (cached, true);
        }
        // 上次枚举失败/为空:短时间内不再重试(否则限流期间每次读缓存都会再打一轮全部凭证),
        // 并按"不是本次成功结果"如实上报 —— 不能因为"没有旧数据可沿用"就把失败报成成功。
        if (!ignoreTtl && DateTimeOffset.UtcNow - _localPlayersFailedAt < LocalPlayersFailureRetryDelay)
        {
            return (_localPlayersCache ?? [], false);
        }
        await _localPlayersGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // 双检:等锁期间别的调用可能已经刷新过(强制刷新时跳过,它要求结果必须来自本次)
            if (!ignoreTtl && _localPlayersCache is { } fresh
                && DateTimeOffset.UtcNow - _localPlayersCachedAt < LocalPlayersCacheTtl)
            {
                return (fresh, true);
            }
            var players = await GetLocalPlayersAsync(ct).ConfigureAwait(false);
            if (players.Count > 0)
            {
                _localPlayersCache = players;
                _localPlayersCachedAt = DateTimeOffset.UtcNow;
                _localPlayersFailedAt = DateTimeOffset.MinValue;
                return (players, true);
            }
            // 本次没枚举到任何角色,有三种不可区分的情形:
            //   ① 确实没有本地凭证;② 全部 queryPlayerInfo 失败(1005 限流/瞬时网络错误);
            //   ③ 启动器缓存文件缺失/损坏(ReadCacheAsync 返回 null)。
            // 一律<b>如实按"本次没成功"上报</b>(Fresh=false):
            // 旧实现只在"有旧数据可沿用"时给 false、无旧数据时给 true(见下),于是首次使用遇上限流,
            // 首页账号弹窗会显示「已重新检测,账号列表无变化」—— 而真实情况是"什么都没检测到",
            // 恰好把本功能(区分"检测完成无变化" vs "没检测成功")要区分的两件事又混成一件。
            // 用户看到「未检测到本地登录账号,请先在游戏启动器登录」才是准确且可行动的。
            _localPlayersFailedAt = DateTimeOffset.UtcNow;
            return (_localPlayersCache ?? players, false);
        }
        finally
        {
            _localPlayersGate.Release();
        }
    }

    /// <summary>
    /// 把本地角色短缓存标记为过期(下次调用强制重新枚举)。
    /// <para>
    /// 有意<b>保留</b>上一次的成功结果作为兜底:queryPlayerInfo 有 1005 限流,
    /// 重新枚举瞬时失败(返回空)时若缓存已被丢弃,账号页/首页列表会当场清空;
    /// 保留数据则调用方会沿用旧列表并等下次再试。
    /// </para>
    /// <para>
    /// 同时清掉失败退避:本方法是"用户主动要求重新检测"的入口(账号弹窗的刷新键),
    /// 必须真的重试一次,不能被上一轮的失败退避挡回去。
    /// </para>
    /// </summary>
    public void InvalidateLocalPlayersCache()
    {
        _localPlayersCachedAt = DateTimeOffset.MinValue;
        _localPlayersFailedAt = DateTimeOffset.MinValue;
    }

    /// <summary>用一个凭证查它的游戏角色列表(服务器名 → 角色)。</summary>
    private async Task<List<(PcPlayerItem Player, string Server)>> QueryPlayersAsync(
        LauncherCacheAccount account, CancellationToken ct)
    {
        var players = new List<(PcPlayerItem, string)>();
        var oauth = XorDecrypt(account.OauthCode ?? "", 5);
        if (string.IsNullOrEmpty(oauth))
        {
            return players;
        }
        var playerJson = await PostSdkAsync(
            "game/queryPlayerInfo", $"{{\"oauthCode\":{EscapeJson(oauth)}}}", ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(playerJson))
        {
            return players;
        }
        var resp = JsonSerializer.Deserialize(playerJson, LocalDailyJsonContext.Default.PcPlayerInfoResponse);
        if (resp?.Code != 0 || resp.Data is null)
        {
            return players;
        }
        foreach (var (server, payload) in resp.Data)
        {
            var player = JsonSerializer.Deserialize(payload, LocalDailyJsonContext.Default.PcPlayerItem);
            if (player is not null)
            {
                players.Add((player, server));
            }
        }
        return players;
    }

    /// <summary>用「凭证 + 角色 + 服务器」查该角色的每日数据并映射。</summary>
    private async Task<RoleDailyData?> QueryRoleAsync(
        LauncherCacheAccount account, PcPlayerItem player, string server, CancellationToken ct)
    {
        var oauth = XorDecrypt(account.OauthCode ?? "", 5);
        var roleId = player.RoleId ?? "";
        if (string.IsNullOrEmpty(oauth) || string.IsNullOrEmpty(roleId))
        {
            return null;
        }
        var roleBody = $"{{\"oauthCode\":{EscapeJson(oauth)},\"playerId\":{EscapeJson(roleId)},\"region\":{EscapeJson(server)}}}";
        var roleJson = await PostSdkAsync("game/queryRole", roleBody, ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(roleJson))
        {
            return null;
        }
        var roleResp = JsonSerializer.Deserialize(roleJson, LocalDailyJsonContext.Default.PcRoleInfoResponse);
        var roleData = roleResp?.Data?.Values.FirstOrDefault();
        if (roleResp?.Code != 0 || string.IsNullOrEmpty(roleData))
        {
            return null;
        }
        var role = JsonSerializer.Deserialize(roleData, LocalDailyJsonContext.Default.PcRoleItem);
        if (role is null)
        {
            return null;
        }
        role.ServerName = server;
        var daily = MapToDaily(role, player.RoleName ?? "", roleId);
        // 等级优先取 queryPlayerInfo,缺失时回退 queryRole 的 Base
        daily.Level = player.Level > 0 ? player.Level : role.Base?.Level ?? 0;
        return daily;
    }

    /// <summary>获取每日数据(PC SDK);失败返回 null。</summary>
    public async Task<RoleDailyData?> GetDailyDataAsync(CancellationToken ct = default)
    {
        try
        {
            var accounts = await ReadCacheAsync(ct).ConfigureAwait(false);
            if (accounts is null || accounts.Count == 0)
            {
                return null;
            }

            // 遍历账号,跳过无效/未绑定账号(1005 等),用第一个成功且有玩家的
            foreach (var account in accounts)
            {
                foreach (var (player, server) in await QueryPlayersAsync(account, ct).ConfigureAwait(false))
                {
                    var daily = await QueryRoleAsync(account, player, server, ct).ConfigureAwait(false);
                    if (daily is not null)
                    {
                        return daily;
                    }
                }
            }
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PC 启动器 SDK 查询每日数据失败");
            return null;
        }
    }

    private async Task<string?> PostSdkAsync(string path, string body, CancellationToken ct)
    {
        // 1005 = 服务器限流/临时错误,重试最多 5 次(参考 Haiyu)
        for (int attempt = 0; attempt < 5; attempt++)
        {
            var url = $"{SdkBase}/{path}?_t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/139.0.0.0 Safari/537.36");
            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                return null;
            }
            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            // 1005 重试;成功或其他错误直接返回
            if (json.Contains("\"code\":1005", StringComparison.Ordinal) && attempt < 4)
            {
                await Task.Delay(200 * (attempt + 1), ct).ConfigureAwait(false);
                continue;
            }
            return json;
        }
        return null;
    }

    private static RoleDailyData MapToDaily(PcRoleItem? role, string roleName, string roleId)
    {
        var b = role?.Base;
        return new RoleDailyData
        {
            RoleId = roleId,
            RoleName = string.IsNullOrEmpty(roleName) ? b?.Name : roleName,
            ServerName = role?.ServerName,
            ActiveDays = b?.ActiveDays ?? 0,
            CreatTime = b?.CreatTime ?? 0,
            EnergyData = b is null ? null : new RoleDailyDetail
            {
                Name = "体力",
                Cur = b.Energy,
                Total = b.MaxEnergy,
                Value = $"{b.Energy}/{b.MaxEnergy}",
            },
            StoreEnergyData = b is null || b.StoreEnergy is null ? null : new RoleDailyDetail
            {
                Name = "结晶单质",
                Cur = (int)b.StoreEnergy.Value,
                Total = b.MaxStoreEnergy ?? 0,
                Value = $"{b.StoreEnergy}/{b.MaxStoreEnergy}",
            },
            LivenessData = b is null ? null : new RoleDailyDetail
            {
                Name = "活跃度",
                Cur = b.Liveness,
                Total = b.LivenessMaxCount,
                Value = $"{b.Liveness}/{b.LivenessMaxCount}",
            },
            WeeklyData = b is null ? null : new RoleDailyDetail
            {
                Name = "周本",
                Cur = b.WeeklyInstCount,
                Total = 3,
                Value = $"{b.WeeklyInstCount}/3",
            },
            BattlePassData = role?.BattlePass is null ? null :
            [
                new RoleDailyDetail { Name = "战令等级", Cur = role.BattlePass.Level, Total = 0, Value = $"LV.{role.BattlePass.Level}" },
                new RoleDailyDetail { Name = "战令进度", Cur = role.BattlePass.Exp, Total = role.BattlePass.ExpLimit, Value = $"{role.BattlePass.Exp}/{role.BattlePass.ExpLimit}" },
            ],
        };
    }
}

/// <summary>
/// 本地启动器登录凭证对应的一个游戏角色(首页账号列表条目,对齐 Haiyu 的卡片数据源)。
/// <see cref="KuroUid"/> 是缓存里的 id(库街区 UID,绑定值);<see cref="RoleId"/> 是游戏角色 UID。
/// </summary>
public sealed class LocalLauncherPlayer
{
    /// <summary>库街区 UID(缓存 id 数字形式,绑定键的一部分)。</summary>
    public required string KuroUid { get; init; }

    /// <summary>库街区显示名(缓存 username,形如 "U536781653A")。</summary>
    public required string Username { get; init; }

    public required string RoleId { get; init; }
    public required string RoleName { get; init; }
    public required string ServerName { get; init; }
    public int Level { get; init; }

    /// <summary>
    /// 凭证的登录手机号(启动器缓存里就有)。账号页三张接口卡片与签到页靠它把
    /// 「接口账号」映射到「游戏角色」,从而显示游戏昵称 + 游戏角色 UID。
    /// </summary>
    public string Phone { get; init; } = "";

    /// <summary>
    /// 账号+角色的唯一定位键(对齐 Haiyu 的 GetKey 思路:同一库街区账号可能有多个服务器角色,
    /// 只按 UID 绑定会选错角色)。持久化到设置,重启后仍能定位到同一个角色。
    /// </summary>
    public string BindKey => $"{KuroUid}|{RoleId}";
}

/// <summary>本地缓存账号项。</summary>
public sealed class LauncherCacheAccount{
    [JsonPropertyName("cuid")] public string? Cuid { get; set; }
    [JsonPropertyName("id")] public double Id { get; set; }
    [JsonPropertyName("oauthCode")] public string? OauthCode { get; set; }
    [JsonPropertyName("phone")] public string? Phone { get; set; }
    [JsonPropertyName("username")] public string? Username { get; set; }
}

/// <summary>PC SDK 查询玩家响应。</summary>
public sealed class PcPlayerInfoResponse
{
    [JsonPropertyName("code")] public int Code { get; set; }
    [JsonPropertyName("data")] public Dictionary<string, string>? Data { get; set; }
}

public sealed class PcPlayerItem
{
    [JsonPropertyName("roleId")] public string? RoleId { get; set; }
    [JsonPropertyName("roleName")] public string? RoleName { get; set; }
    [JsonPropertyName("level")] public int Level { get; set; }
}

/// <summary>PC SDK 查询角色响应。</summary>
public sealed class PcRoleInfoResponse
{
    [JsonPropertyName("code")] public int Code { get; set; }
    [JsonPropertyName("data")] public Dictionary<string, string>? Data { get; set; }
}

public sealed class PcRoleItem
{
    [JsonPropertyName("Base")] public PcRoleBase? Base { get; set; }
    [JsonPropertyName("BattlePass")] public PcBattlePass? BattlePass { get; set; }
    [JsonIgnore] public string? ServerName { get; set; }
}

public sealed class PcBattlePass
{
    [JsonPropertyName("Exp")] public int Exp { get; set; }
    [JsonPropertyName("ExpLimit")] public int ExpLimit { get; set; }
    [JsonPropertyName("Level")] public int Level { get; set; }
    [JsonPropertyName("IsUnlock")] public bool IsUnlock { get; set; }
}

public sealed class PcRoleBase
{
    [JsonPropertyName("Name")] public string? Name { get; set; }
    [JsonPropertyName("Energy")] public int Energy { get; set; }
    [JsonPropertyName("MaxEnergy")] public int MaxEnergy { get; set; }
    [JsonPropertyName("StoreEnergy")] public long? StoreEnergy { get; set; }
    [JsonPropertyName("MaxStoreEnergy")] public int? MaxStoreEnergy { get; set; }
    [JsonPropertyName("Liveness")] public int Liveness { get; set; }
    [JsonPropertyName("LivenessMaxCount")] public int LivenessMaxCount { get; set; }
    [JsonPropertyName("Level")] public int Level { get; set; }
    [JsonPropertyName("WorldLevel")] public int WorldLevel { get; set; }
    [JsonPropertyName("RoleNum")] public int RoleNum { get; set; }
    [JsonPropertyName("WeeklyInstCount")] public int WeeklyInstCount { get; set; }
    [JsonPropertyName("ActiveDays")] public int ActiveDays { get; set; }
    [JsonPropertyName("CreatTime")] public long CreatTime { get; set; }
}

[JsonSerializable(typeof(List<LauncherCacheAccount>))]
[JsonSerializable(typeof(LauncherCacheAccount))]
[JsonSerializable(typeof(PcPlayerInfoResponse))]
[JsonSerializable(typeof(PcPlayerItem))]
[JsonSerializable(typeof(PcRoleInfoResponse))]
[JsonSerializable(typeof(PcRoleItem))]
[JsonSerializable(typeof(PcRoleBase))]
[JsonSerializable(typeof(PcBattlePass))]
public sealed partial class LocalDailyJsonContext : JsonSerializerContext;
