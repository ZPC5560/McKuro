using System.Text.Json;
using McKuro.Core.Models.Tower;

namespace McKuro.Tests;

/// <summary>深塔/海墟模型反序列化测试。</summary>
public class TowerModelTests
{
    [Fact]
    public void Deserialize_NewTowerData()
    {
        string json = """
            {"endTime":"2026-09-01","isUnlock":true,
             "modeDetails":[{"modeId":0,"score":6600,"passBoss":3,"bossCount":4,"round":1,"rank":3,
               "teams":[{"score":6600,"round":1,
                 "buffs":[{"buffIcon":"u1","buffName":"强攻","desc":"攻击+"}],
                 "roleList":[{"roleId":1209,"iconUrl":"http://x/1.png"}]}]}]}
            """;
        var data = JsonSerializer.Deserialize(json, TowerJsonContext.Default.NewTowerData)!;
        Assert.True(data.IsUnlock);
        Assert.NotNull(data.ModeDetails);
        var mode = Assert.Single(data.ModeDetails);
        Assert.Equal(0, mode.ModeId);
        Assert.Equal(6600, mode.Score);
        Assert.Equal(3, mode.Rank);
        var team = Assert.Single(mode.Teams!);
        var role = Assert.Single(team.RoleList!);
        Assert.Equal(1209, role.RoleId);
    }

    [Fact]
    public void Deserialize_SlashData()
    {
        string json = """
            {"seasonEndTime":"2026-09-15",
             "difficultyList":[{"difficulty":1,"allScore":1200,"maxScore":2000,
               "challengeList":[{"challengeId":1,"challengeName":"再生之域","rank":"A","score":1200,
                 "halfList":[{"score":600,"buffName":"增伤","buffIcon":"b1",
                   "roleList":[{"roleId":1108,"iconUrl":"http://x/2.png"}]}]}]}]}
            """;
        var data = JsonSerializer.Deserialize(json, TowerJsonContext.Default.SlashData)!;
        Assert.NotNull(data.DifficultyList);
        var diff = Assert.Single(data.DifficultyList);
        Assert.Equal(1, diff.Difficulty);
        var challenge = Assert.Single(diff.ChallengeList!);
        Assert.Equal(1200, challenge.Score);
        Assert.Equal("A", challenge.Rank);   // 海墟 rank 是字符串 S/A/B/C
        var half = Assert.Single(challenge.HalfList!);
        Assert.Equal(600, half.Score);
    }

    [Fact]
    public void Deserialize_NewTowerData_NotUnlocked_ZeroReward()
    {
        // 真实接口形态:未解锁时 reward/totalReward 是数字 0(曾因期望 List 而抛 JsonException)
        string json = """{"isUnlock":false,"reward":0,"totalReward":0}""";
        var data = JsonSerializer.Deserialize(json, TowerJsonContext.Default.NewTowerData)!;
        Assert.False(data.IsUnlock);
        Assert.Null(data.Reward);
        Assert.Null(data.TotalReward);
    }

    [Fact]
    public void Deserialize_SlashData_NumericSeasonEndTime()
    {
        // 真实接口形态:seasonEndTime 是数字时间戳(曾因期望 String 而抛 JsonException)
        string json = """
            {"seasonEndTime":665363164,
             "difficultyList":[{"difficulty":2,"allScore":5060,"maxScore":4500,
               "challengeList":[{"challengeId":9,"challengeName":"无尽湍渊","rank":"S","score":5060,
                 "halfList":[{"score":2530,"buffName":"那倒映彼方的明镜","buffIcon":"b2",
                   "buffDescription":"角色附加虚湮效应时,造成伤害最终提升60%,持续15秒。",
                   "roleList":[{"roleId":1508,"iconUrl":"http://x/3.png"}]}]}]}]}
            """;
        var data = JsonSerializer.Deserialize(json, TowerJsonContext.Default.SlashData)!;
        Assert.Equal(665363164, data.SeasonEndTime);
        var diff = Assert.Single(data.DifficultyList!);
        Assert.Equal(2, diff.Difficulty);
        var challenge = Assert.Single(diff.ChallengeList!);
        var half = Assert.Single(challenge.HalfList!);
        Assert.Equal("那倒映彼方的明镜", half.BuffName);
        Assert.Equal(1508, Assert.Single(half.RoleList!).RoleId);
    }

    [Fact]
    public void Deserialize_TowerSeasonData_RealPayload()
    {
        // towerDataDetail 真实响应子集(字段名/值域按实机抓包):
        // difficulty 值域 1稳定区/2实验区/3深境区/4超载区,seasonEndTime 为剩余毫秒数字
        string json = """
            {"difficultyList":[
              {"difficulty":1,"difficultyName":"稳定区","towerAreaList":[
                {"areaId":1,"areaName":"残响之塔",
                 "floorList":[{"floor":1,"picUrl":"https://x/66.png",
                   "roleList":[{"iconUrl":"https://x/r1.png","roleId":1406}],"star":3},
                   {"floor":2,"picUrl":"https://x/67.png",
                   "roleList":[{"iconUrl":"https://x/r2.png","roleId":1203},
                                {"iconUrl":"https://x/r3.png","roleId":1601}],"star":3}],
                 "maxStar":12,"star":12}]},
              {"difficulty":3,"difficultyName":"深境区","towerAreaList":[
                {"areaId":2,"areaName":"深境之塔",
                 "floorList":[{"floor":1,"picUrl":"https://x/68.png",
                   "roleList":[{"iconUrl":"https://x/r4.png","roleId":1409}],"star":3}],
                 "maxStar":12,"star":11}]}],
             "isUnlock":true,"seasonEndTime":1874134021}
            """;
        var data = JsonSerializer.Deserialize(json, TowerJsonContext.Default.TowerSeasonData)!;
        Assert.True(data.IsUnlock);
        Assert.Equal(1874134021, data.SeasonEndTime);
        var list = Assert.IsAssignableFrom<List<TowerSeasonDifficulty>>(data.DifficultyList);
        Assert.Equal(2, list.Count);
        var diff1 = list[0];
        Assert.Equal(1, diff1.Difficulty);
        Assert.Equal("稳定区", diff1.DifficultyName);
        var area = Assert.Single(diff1.TowerAreaList!);
        Assert.Equal(1, area.AreaId);
        Assert.Equal("残响之塔", area.AreaName);
        Assert.Equal(12, area.MaxStar);
        Assert.Equal(12, area.Star);
        var floors = area.FloorList!;
        Assert.Equal(2, floors.Count);
        Assert.Equal(2, floors[1].Floor);
        Assert.Equal(3, floors[1].Star);
        Assert.Equal("https://x/67.png", floors[1].PicUrl);
        Assert.Equal(1601, floors[1].RoleList![1].RoleId);
        Assert.Equal("https://x/r2.png", floors[1].RoleList![0].IconUrl);
        // 深境区(难度3):未满星区域保留原值(11/12)
        var diff3 = list[1];
        Assert.Equal("深境区", diff3.DifficultyName);
        Assert.Equal(11, Assert.Single(diff3.TowerAreaList!).Star);
    }

    [Fact]
    public void SortDifficulties_MatchesJava_TowerDataDetailTask()
    {
        // Java 比较器:o1==3 → -1(深境区置顶),其余 o2-o1 降序 → 输入 [1,2,4,3] 排序为 [3,4,2,1]
        var input = new List<TowerSeasonDifficulty>
        {
            new() { Difficulty = 1, DifficultyName = "稳定区" },
            new() { Difficulty = 2, DifficultyName = "实验区" },
            new() { Difficulty = 4, DifficultyName = "超载区" },
            new() { Difficulty = 3, DifficultyName = "深境区" },
        };
        var sorted = TowerSeasonParser.SortDifficulties(input);
        Assert.Equal([3, 4, 2, 1], sorted.Select(d => d.Difficulty).ToArray());
        Assert.Equal("深境区", sorted[0].DifficultyName);
        Assert.Empty(TowerSeasonParser.SortDifficulties(null));
        Assert.Empty(TowerSeasonParser.SortDifficulties([]));
    }

    [Fact]
    public void RefreshText_RemainingMillis()
    {
        // 实机 seasonEndTime=1874134021 ms ≈ 21天16小时(对齐 WutheringWavesTool updateSeasonEndTime)
        Assert.Equal("21天16小时后刷新", TowerSeasonParser.RefreshText(1_874_134_021));
        Assert.Equal("", TowerSeasonParser.RefreshText(null));
        Assert.Equal("", TowerSeasonParser.RefreshText(0));
    }
}
