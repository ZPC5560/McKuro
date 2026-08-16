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
}
