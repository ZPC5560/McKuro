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

    /// <summary>角色等级(PC 启动器 SDK / 数据中心,0 表示未知)。</summary>
    public int Level { get; set; }

    /// <summary>头像 URL(库街区 gamer 接口 headPhotoUrl;无可用 URL 时为空,由 UI 回退默认头像)。</summary>
    public string? HeadUrl { get; set; }

    /// <summary>已游玩天数(0 表示未知)。</summary>
    public int ActiveDays { get; set; }

    /// <summary>角色注册时间(unix 毫秒,0 表示未知)。</summary>
    public long CreatTime { get; set; }

    /// <summary>周本(战歌重奏)官方图标 URL(数据中心 weeklyInstIconUrl)。</summary>
    public string? WeeklyIconUrl { get; set; }

    /// <summary>活跃度上限(数据中心 livenessMaxCount,如 100);0 表示未知。</summary>
    public int LivenessLimit { get; set; }

    /// <summary>周本(战歌重奏)每周次数上限(数据中心 weeklyInstCountLimit,如 3);0 表示未知。</summary>
    public int WeeklyLimit { get; set; }

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

/// <summary>
/// 数据中心玩家基础数据(aki/roleBox/akiBox/baseData,对齐 Haiyu GamerBassData)。
/// 提供游玩天数/注册时间/周本(战歌重奏)图标等资料字段。
/// </summary>
public sealed class GamerBaseData
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("level")] public int Level { get; set; }
    [JsonPropertyName("worldLevel")] public int WorldLevel { get; set; }
    [JsonPropertyName("roleNum")] public int RoleNum { get; set; }

    /// <summary>已游玩天数。</summary>
    [JsonPropertyName("activeDays")] public int ActiveDays { get; set; }

    /// <summary>角色注册时间(unix 毫秒)。</summary>
    [JsonPropertyName("creatTime")] public long CreatTime { get; set; }

    [JsonPropertyName("energy")] public int Energy { get; set; }
    [JsonPropertyName("maxEnergy")] public int MaxEnergy { get; set; }
    [JsonPropertyName("storeEnergy")] public int StoreEnergy { get; set; }
    [JsonPropertyName("storeEnergyLimit")] public int StoreEnergyLimit { get; set; }
    [JsonPropertyName("storeEnergyIconUrl")] public string? StoreEnergyIconUrl { get; set; }
    [JsonPropertyName("storeEnergyTitle")] public string? StoreEnergyTitle { get; set; }
    [JsonPropertyName("liveness")] public int Liveness { get; set; }
    [JsonPropertyName("livenessMaxCount")] public int LivenessMaxCount { get; set; }
    [JsonPropertyName("weeklyInstCount")] public int WeeklyInstCount { get; set; }
    [JsonPropertyName("weeklyInstCountLimit")] public int WeeklyInstCountLimit { get; set; }

    /// <summary>周本(战歌重奏)图标 URL。</summary>
    [JsonPropertyName("weeklyInstIconUrl")] public string? WeeklyInstIconUrl { get; set; }

    [JsonPropertyName("weeklyInstTitle")] public string? WeeklyInstTitle { get; set; }
    [JsonPropertyName("rougeScore")] public int RougeScore { get; set; }
    [JsonPropertyName("rougeScoreLimit")] public int RougeScoreLimit { get; set; }
    [JsonPropertyName("rougeIconUrl")] public string? RougeIconUrl { get; set; }
    [JsonPropertyName("rougeTitle")] public string? RougeTitle { get; set; }
    [JsonPropertyName("achievementCount")] public int AchievementCount { get; set; }
    [JsonPropertyName("achievementStar")] public int AchievementStar { get; set; }
    [JsonPropertyName("bigCount")] public int BigCount { get; set; }
    [JsonPropertyName("smallCount")] public int SmallCount { get; set; }
}

/// <summary>账号资料(游戏内)静态工具:开服注册判定等。</summary>
public static class UserProfile
{
    /// <summary>国服公测开服日期(2024-05-23)。</summary>
    public static readonly DateTime CnLaunchDate = new(2024, 5, 23);

    /// <summary>
    /// 是否为开服玩家:注册时间落在开服当日及之后 10 天内(对齐 Haiyu StaminaWrapper.IsShowTime)。
    /// </summary>
    public static bool IsLaunchPlayer(long creatTimeMs)
    {
        if (creatTimeMs <= 0)
        {
            return false;
        }
        var date = DateTimeOffset.FromUnixTimeMilliseconds(creatTimeMs).LocalDateTime.Date;
        return date >= CnLaunchDate && date <= CnLaunchDate.AddDays(10);
    }
}

[JsonSerializable(typeof(RoleDailyData))]
[JsonSerializable(typeof(RoleDailyDetail))]
[JsonSerializable(typeof(List<RoleDailyDetail>))]
[JsonSerializable(typeof(GamerBaseData))]
public sealed partial class UserJsonContext : JsonSerializerContext;
