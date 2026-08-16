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
                var oauth = XorDecrypt(account.OauthCode ?? "", 5);
                if (string.IsNullOrEmpty(oauth))
                {
                    continue;
                }

                // 1. 查询玩家列表(data: 服务器名 → 玩家JSON)
                var playerJson = await PostSdkAsync("game/queryPlayerInfo", $"{{\"oauthCode\":{EscapeJson(oauth)}}}", ct).ConfigureAwait(false);
                if (string.IsNullOrEmpty(playerJson))
                {
                    continue;
                }
                var playerResp = JsonSerializer.Deserialize(playerJson, LocalDailyJsonContext.Default.PcPlayerInfoResponse);
                var players = playerResp?.Data ?? [];
                if (playerResp?.Code != 0 || players.Count == 0)
                {
                    continue;
                }
                var first = players.First();
                var serverName = first.Key;
                var player = JsonSerializer.Deserialize(first.Value, LocalDailyJsonContext.Default.PcPlayerItem);
                if (player is null)
                {
                    continue;
                }

                // 2. 查询角色每日数据
                var roleBody = $"{{\"oauthCode\":{EscapeJson(oauth)},\"playerId\":{EscapeJson(player.RoleId ?? "")},\"region\":{EscapeJson(serverName)}}}";
                var roleJson = await PostSdkAsync("game/queryRole", roleBody, ct).ConfigureAwait(false);
                if (string.IsNullOrEmpty(roleJson))
                {
                    continue;
                }
                var roleResp = JsonSerializer.Deserialize(roleJson, LocalDailyJsonContext.Default.PcRoleInfoResponse);
                var roleData = roleResp?.Data?.Values.FirstOrDefault();
                if (roleResp?.Code != 0 || string.IsNullOrEmpty(roleData))
                {
                    continue;
                }
                var role = JsonSerializer.Deserialize(roleData, LocalDailyJsonContext.Default.PcRoleItem);
                if (role is null)
                {
                    continue;
                }
                role.ServerName = serverName;

                return MapToDaily(role, player.RoleName ?? "", player.RoleId ?? "");
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
            if (json.Contains("\"code\":1005") && attempt < 4)
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

/// <summary>本地缓存账号项。</summary>
public sealed class LauncherCacheAccount
{
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
