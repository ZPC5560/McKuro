using System.Text.Json.Serialization;

namespace McKuro.Core.Models.Tower;

/// <summary>深塔(终焉矩阵)数据(对齐 WutheringWavesTool NewTowerData)。</summary>
public sealed class NewTowerData
{
    [JsonPropertyName("endTime")] public string? EndTime { get; set; }
    [JsonPropertyName("isUnlock")] public bool IsUnlock { get; set; }
    [JsonPropertyName("reward")] public List<NewTowerRewardItem>? Reward { get; set; }
    [JsonPropertyName("totalReward")] public List<NewTowerRewardItem>? TotalReward { get; set; }
    [JsonPropertyName("modeDetails")] public List<NewTowerModeDetail>? ModeDetails { get; set; }
}

public sealed class NewTowerRewardItem
{
    [JsonPropertyName("goodsName")] public string? GoodsName { get; set; }
    [JsonPropertyName("goodsNum")] public int GoodsNum { get; set; }
    [JsonPropertyName("isGain")] public bool IsGain { get; set; }
}

/// <summary>深塔模式详情(modeId:0=稳态,1=奇点)。</summary>
public sealed class NewTowerModeDetail
{
    [JsonPropertyName("modeId")] public int ModeId { get; set; }
    [JsonPropertyName("score")] public int Score { get; set; }
    [JsonPropertyName("passBoss")] public int PassBoss { get; set; }
    [JsonPropertyName("bossCount")] public int BossCount { get; set; }
    [JsonPropertyName("round")] public int Round { get; set; }
    [JsonPropertyName("rank")] public int Rank { get; set; }
    [JsonPropertyName("hasRecord")] public bool HasRecord { get; set; }
    [JsonPropertyName("teams")] public List<NewTowerTeam>? Teams { get; set; }
}

/// <summary>深塔配队。</summary>
public sealed class NewTowerTeam
{
    [JsonPropertyName("score")] public int Score { get; set; }
    [JsonPropertyName("round")] public int Round { get; set; }
    [JsonPropertyName("buffs")] public List<NewTowerBuff>? Buffs { get; set; }
    [JsonPropertyName("roleList")] public List<NewTowerRole>? RoleList { get; set; }
}

public sealed class NewTowerBuff
{
    [JsonPropertyName("buffIcon")] public string? BuffIcon { get; set; }
    [JsonPropertyName("buffName")] public string? BuffName { get; set; }
    [JsonPropertyName("desc")] public string? Desc { get; set; }
}

public sealed class NewTowerRole
{
    [JsonPropertyName("roleId")] public int RoleId { get; set; }
    [JsonPropertyName("iconUrl")] public string? IconUrl { get; set; }
}

/// <summary>海墟(再生海域)数据(对齐 WutheringWavesTool SlashData)。</summary>
public sealed class SlashData
{
    [JsonPropertyName("difficultyList")] public List<SlashDifficulty>? DifficultyList { get; set; }
    [JsonPropertyName("seasonEndTime")] public string? SeasonEndTime { get; set; }
}

/// <summary>海墟难度(difficulty:0=禁忌,1=再生,2=湍渊)。</summary>
public sealed class SlashDifficulty
{
    [JsonPropertyName("difficulty")] public int Difficulty { get; set; }
    [JsonPropertyName("allScore")] public int AllScore { get; set; }
    [JsonPropertyName("maxScore")] public int MaxScore { get; set; }
    [JsonPropertyName("challengeList")] public List<SlashChallenge>? ChallengeList { get; set; }
}

public sealed class SlashChallenge
{
    [JsonPropertyName("challengeId")] public int ChallengeId { get; set; }
    [JsonPropertyName("challengeName")] public string? ChallengeName { get; set; }
    [JsonPropertyName("rank")] public string? Rank { get; set; }
    [JsonPropertyName("score")] public int Score { get; set; }
    [JsonPropertyName("halfList")] public List<SlashHalf>? HalfList { get; set; }
}

public sealed class SlashHalf
{
    [JsonPropertyName("score")] public int Score { get; set; }
    [JsonPropertyName("buffName")] public string? BuffName { get; set; }
    [JsonPropertyName("buffIcon")] public string? BuffIcon { get; set; }
    [JsonPropertyName("buffDescription")] public string? BuffDescription { get; set; }
    [JsonPropertyName("roleList")] public List<NewTowerRole>? RoleList { get; set; }
}

[JsonSerializable(typeof(NewTowerData))]
[JsonSerializable(typeof(NewTowerModeDetail))]
[JsonSerializable(typeof(NewTowerTeam))]
[JsonSerializable(typeof(NewTowerBuff))]
[JsonSerializable(typeof(NewTowerRole))]
[JsonSerializable(typeof(SlashData))]
[JsonSerializable(typeof(SlashDifficulty))]
[JsonSerializable(typeof(SlashChallenge))]
[JsonSerializable(typeof(SlashHalf))]
public sealed partial class TowerJsonContext : JsonSerializerContext;
