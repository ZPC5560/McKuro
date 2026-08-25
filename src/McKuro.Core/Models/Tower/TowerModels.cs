using System.Text.Json;
using System.Text.Json.Serialization;

namespace McKuro.Core.Models.Tower;

/// <summary>深塔(终焉矩阵)数据(对齐 WutheringWavesTool NewTowerData)。</summary>
public sealed class NewTowerData
{
    /// <summary>本期刷新剩余毫秒(对齐 WutheringWavesTool getEndTime:long 参与倒计时运算)。</summary>
    [JsonPropertyName("endTime")]
    [JsonConverter(typeof(FlexibleLongConverter))]
    public long? EndTime { get; set; }
    [JsonPropertyName("isUnlock")] public bool IsUnlock { get; set; }
    /// <summary>未解锁时接口以数字 0 占位(而非列表),经 RewardListConverter 归一化为 null。</summary>
    [JsonPropertyName("reward")]
    [JsonConverter(typeof(RewardListConverter))]
    public List<NewTowerRewardItem>? Reward { get; set; }
    /// <summary>同 <see cref="Reward"/>:未解锁时为数字 0。</summary>
    [JsonPropertyName("totalReward")]
    [JsonConverter(typeof(RewardListConverter))]
    public List<NewTowerRewardItem>? TotalReward { get; set; }
    [JsonPropertyName("modeDetails")] public List<NewTowerModeDetail>? ModeDetails { get; set; }
}

public sealed class NewTowerRewardItem
{
    [JsonPropertyName("goodsName")] public string? GoodsName { get; set; }
    [JsonPropertyName("goodsNum")] public int GoodsNum { get; set; }
    [JsonPropertyName("isGain")] public bool IsGain { get; set; }
}

/// <summary>
/// 逆境深塔数据(对齐 WutheringWavesTool DifficultyTotal,towerDataDetail 接口)。
/// </summary>
public sealed class TowerSeasonData
{
    [JsonPropertyName("difficultyList")] public List<TowerSeasonDifficulty>? DifficultyList { get; set; }
    [JsonPropertyName("isUnlock")] public bool IsUnlock { get; set; }
    /// <summary>本期剩余毫秒数(now + seasonEndTime 即为结束时刻);接口以数字返回。</summary>
    [JsonPropertyName("seasonEndTime")]
    [JsonConverter(typeof(FlexibleLongConverter))]
    public long? SeasonEndTime { get; set; }
}

/// <summary>逆境深塔难度(difficulty:后端下发,难度名由 difficultyName 提供)。</summary>
public sealed class TowerSeasonDifficulty
{
    [JsonPropertyName("difficulty")] public int Difficulty { get; set; }
    [JsonPropertyName("difficultyName")] public string? DifficultyName { get; set; }
    [JsonPropertyName("towerAreaList")] public List<TowerSeasonArea>? TowerAreaList { get; set; }
}

/// <summary>逆境深塔-分区(区域名 + 已得/总星数 + 楼层列表)。</summary>
public sealed class TowerSeasonArea
{
    [JsonPropertyName("areaId")] public int AreaId { get; set; }
    [JsonPropertyName("areaName")] public string? AreaName { get; set; }
    [JsonPropertyName("floorList")] public List<TowerSeasonFloor>? FloorList { get; set; }
    [JsonPropertyName("maxStar")] public int MaxStar { get; set; }
    [JsonPropertyName("star")] public int Star { get; set; }
}

/// <summary>逆境深塔-楼层(层数 + 星级(0-3) + 通关配队角色)。</summary>
public sealed class TowerSeasonFloor
{
    [JsonPropertyName("floor")] public int Floor { get; set; }
    [JsonPropertyName("picUrl")] public string? PicUrl { get; set; }
    [JsonPropertyName("roleList")] public List<TowerSeasonRole>? RoleList { get; set; }
    [JsonPropertyName("star")] public int Star { get; set; }
}

/// <summary>逆境深塔-角色(对齐 WutheringWavesTool SimpleRole)。</summary>
public sealed class TowerSeasonRole
{
    [JsonPropertyName("roleId")] public int RoleId { get; set; }
    [JsonPropertyName("iconUrl")] public string? IconUrl { get; set; }
}

/// <summary>逆境深塔响应解析辅助(排序/文案,对齐 WutheringWavesTool TowerDataDetailTask / TowerViewModel)。</summary>
public static class TowerSeasonParser
{
    /// <summary>
    /// 难度排序:difficulty==3(深境区,通常限时主力区)置顶,其余按难度降序。
    /// 对齐 WutheringWavesTool TowerDataDetailTask.difficultyList.sort
    /// (o1==3 → -1,否则 o2-o1)。实机 difficulty 值域:1稳定区/2实验区/3深境区/4超载区。
    /// </summary>
    public static List<TowerSeasonDifficulty> SortDifficulties(List<TowerSeasonDifficulty>? difficultyList)
    {
        if (difficultyList is null)
        {
            return [];
        }
        return
        [
            .. difficultyList
                .OrderBy(d => d.Difficulty == 3 ? 0 : 1)
                .ThenByDescending(d => d.Difficulty),
        ];
    }

    /// <summary>剩余毫秒 → "X天Y小时后刷新"(对齐 WutheringWavesTool updateSeasonEndTime)。</summary>
    public static string RefreshText(long? remainingMillis)
    {
        if (remainingMillis is not { } ms || ms <= 0)
        {
            return "";
        }
        var days = ms / 86_400_000;
        var hours = ms % 86_400_000 / 3_600_000;
        return $"{days}天{hours}小时后刷新";
    }
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
    /// <summary>接口以数字时间戳返回(原值为 Long),仅保存原始值;当前 UI 未展示。</summary>
    [JsonPropertyName("seasonEndTime")]
    [JsonConverter(typeof(FlexibleLongConverter))]
    public long? SeasonEndTime { get; set; }
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

/// <summary>
/// 兼容接口两种形态:未解锁时 reward/totalReward 为数字 0,解锁后为奖励列表。
/// </summary>
public sealed class RewardListConverter : JsonConverter<List<NewTowerRewardItem>?>
{
    public override List<NewTowerRewardItem>? Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;
            case JsonTokenType.Number:
                reader.Skip();
                return null;
            case JsonTokenType.StartArray:
                return JsonSerializer.Deserialize(ref reader, TowerJsonContext.Default.ListNewTowerRewardItem);
            default:
                throw new JsonException($"unexpected token {reader.TokenType} for reward list");
        }
    }

    public override void Write(Utf8JsonWriter writer, List<NewTowerRewardItem>? value, JsonSerializerOptions options)
        => throw new NotSupportedException();
}

/// <summary>
/// 兼容 long? 字段的两种形态:数字(接口现状)与数字字符串(部分响应),其余返回 null。
/// </summary>
public sealed class FlexibleLongConverter : JsonConverter<long?>
{
    public override long? Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;
            case JsonTokenType.Number:
                return reader.TryGetInt64(out var n) ? n : null;
            case JsonTokenType.String:
                return long.TryParse(reader.GetString(), out var v) ? v : null;
            default:
                reader.Skip();
                return null;
        }
    }

    public override void Write(Utf8JsonWriter writer, long? value, JsonSerializerOptions options)
        => writer.WriteNullValue();
}

[JsonSerializable(typeof(NewTowerData))]
[JsonSerializable(typeof(List<NewTowerRewardItem>))]
[JsonSerializable(typeof(NewTowerModeDetail))]
[JsonSerializable(typeof(List<NewTowerModeDetail>))]
[JsonSerializable(typeof(NewTowerTeam))]
[JsonSerializable(typeof(NewTowerBuff))]
[JsonSerializable(typeof(NewTowerRole))]
[JsonSerializable(typeof(TowerSeasonData))]
[JsonSerializable(typeof(TowerSeasonDifficulty))]
[JsonSerializable(typeof(TowerSeasonArea))]
[JsonSerializable(typeof(TowerSeasonFloor))]
[JsonSerializable(typeof(TowerSeasonRole))]
[JsonSerializable(typeof(SlashData))]
[JsonSerializable(typeof(SlashDifficulty))]
[JsonSerializable(typeof(SlashChallenge))]
[JsonSerializable(typeof(SlashHalf))]
public sealed partial class TowerJsonContext : JsonSerializerContext;
