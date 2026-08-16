using System.Text.Json.Serialization;

namespace McKuro.Core.Models.User;

/// <summary>
/// 角色每日数据(gamer/widget/game3/getData,对齐 WutheringWavesTool RoleDailyData)。
/// 含体力/活跃度/周本(战歌重奏)/电台(千道门扉的异想)/周度游历等。
/// </summary>
public sealed class RoleDailyData
{
    [JsonPropertyName("gameId")] public int GameId { get; set; }
    [JsonPropertyName("userId")] public int UserId { get; set; }
    [JsonPropertyName("serverTime")] public long ServerTime { get; set; }
    [JsonPropertyName("serverName")] public string? ServerName { get; set; }
    [JsonPropertyName("roleId")] public string? RoleId { get; set; }
    [JsonPropertyName("roleName")] public string? RoleName { get; set; }
    [JsonPropertyName("hasSignIn")] public bool HasSignIn { get; set; }

    /// <summary>体力。</summary>
    [JsonPropertyName("energyData")] public RoleDailyDetail? EnergyData { get; set; }

    /// <summary>活跃度。</summary>
    [JsonPropertyName("livenessData")] public RoleDailyDetail? LivenessData { get; set; }

    /// <summary>周本(战歌重奏)。</summary>
    [JsonPropertyName("weeklyData")] public RoleDailyDetail? WeeklyData { get; set; }

    /// <summary>电台(千道门扉的异想,rouge)。</summary>
    [JsonPropertyName("weeklyRougeData")] public RoleDailyDetail? RougeData { get; set; }

    /// <summary>周度游历。</summary>
    [JsonPropertyName("weeklyFrameData")] public RoleDailyDetail? WeeklyFrameData { get; set; }

    /// <summary>战令(第 1 个元素 cur=战令等级,第 2 个 cur/total=进度)。</summary>
    [JsonPropertyName("battlePassData")] public List<RoleDailyDetail>? BattlePassData { get; set; }

    /// <summary>结晶单质。</summary>
    [JsonPropertyName("storeEnergyData")] public RoleDailyDetail? StoreEnergyData { get; set; }

    /// <summary>终焉矩阵。</summary>
    [JsonPropertyName("newTowerData")] public RoleDailyDetail? NewTowerData { get; set; }

    /// <summary>冥歌海墟。</summary>
    [JsonPropertyName("slashTowerData")] public RoleDailyDetail? SlashTowerData { get; set; }
}

/// <summary>每日数据单项(cur/total 进度)。</summary>
public sealed class RoleDailyDetail
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("img")] public string? Img { get; set; }
    [JsonPropertyName("key")] public string? Key { get; set; }
    [JsonPropertyName("value")] public string? Value { get; set; }
    [JsonPropertyName("status")] public int Status { get; set; }
    [JsonPropertyName("cur")] public int Cur { get; set; }
    [JsonPropertyName("total")] public int Total { get; set; }
}

[JsonSerializable(typeof(RoleDailyData))]
[JsonSerializable(typeof(RoleDailyDetail))]
[JsonSerializable(typeof(List<RoleDailyDetail>))]
public sealed partial class UserJsonContext : JsonSerializerContext;
