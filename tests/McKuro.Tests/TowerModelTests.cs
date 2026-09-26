using System.Text.Json;
using McKuro.Core.Models.Tower;
using McKuro.ViewModels;

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

    [Fact]
    public void IsSeasonEnded_NegativeOrZeroRemaining()
    {
        // 实机:账号还没打新一期时,深塔 seasonEndTime=-1068656700(已结束 12.4 天)、
        // 海墟 seasonEndTime=-2278256901(已结束 26.4 天),接口仍返回上一期数据且 code=200;
        // 这两种情况必须能被识别成"本期已结束",否则页面会出现"分数照旧、倒计时消失"的假象
        Assert.True(TowerSeasonParser.IsSeasonEnded(-1_068_656_700));
        Assert.True(TowerSeasonParser.IsSeasonEnded(-2_278_256_901));
        Assert.True(TowerSeasonParser.IsSeasonEnded(0));
        Assert.False(TowerSeasonParser.IsSeasonEnded(null));      // 字段缺失:不判为已结束
        Assert.False(TowerSeasonParser.IsSeasonEnded(1_349_731_707)); // 本期剩余 15.6 天
        Assert.False(TowerSeasonParser.IsSeasonEnded(140_131_497));   // 本期剩余 1.6 天
    }

    /// <summary>实机 newTowerDetail:modeId=1(奇点扩张) 44065 分 rank=5,share 6 队分两轮。</summary>
    private static List<NewTowerTeam> RealTeams() =>
    [
        new() { Round = 1, Score = 12002, PassBoss = 3, BossCount = 5 },
        new() { Round = 1, Score = 8197, PassBoss = 5, BossCount = 5 },
        new() { Round = 2, Score = 8238, PassBoss = 1, BossCount = 5 },
        new() { Round = 2, Score = 5167, PassBoss = 1, BossCount = 5 },
        new() { Round = 2, Score = 6432, PassBoss = 2, BossCount = 5 },
        new() { Round = 2, Score = 4029, PassBoss = 3, BossCount = 5 },
    ];

    [Fact]
    public void GroupTeamsByRound_SplitsRoundsAndSumsPerRoundScore()
    {
        var rounds = TowerViewModel.GroupTeamsByRound(RealTeams());

        Assert.Equal(2, rounds.Count);
        // 第1轮:12002 + 8197 = 20199;第2轮:8238 + 5167 + 6432 + 4029 = 23866
        Assert.Equal("20199", rounds[0].RoundScoreText);
        Assert.Equal("23866", rounds[1].RoundScoreText);
        Assert.Equal(2, rounds[0].Teams.Count);
        Assert.Equal(4, rounds[1].Teams.Count);
        // 每队保留自己的分数(这正是"细化每轮队伍分数"要展示的东西)
        Assert.Equal(["12002", "8197"], rounds[0].Teams.Select(t => t.ScoreText));
        Assert.Equal(["8238", "5167", "6432", "4029"], rounds[1].Teams.Select(t => t.ScoreText));
        // 队伍进度按队保留(3/5、5/5 与模式级 3/5 不同,不能混成一锅)
        Assert.Equal("3/5", rounds[0].Teams[0].PassText);
        Assert.Equal("5/5", rounds[0].Teams[1].PassText);
        // 轮次升序
        Assert.Equal("第1轮", rounds[0].RoundText);
        Assert.Equal("第2轮", rounds[1].RoundText);
    }

    [Fact]
    public void GroupTeamsByRound_StableModeHasNoRoundTitleAndKeepsAllTeams()
    {
        // 稳态协议(round=0/缺失):不显示轮次标题,队伍仍要全部列出(实机 2 队:7215 + 3608 = 10823)
        var rounds = TowerViewModel.GroupTeamsByRound(
        [
            new() { Round = 0, Score = 7215, PassBoss = 4, BossCount = 4 },
            new() { Round = 0, Score = 3608, PassBoss = 4, BossCount = 4 },
        ]);

        Assert.Single(rounds);
        Assert.Equal("", rounds[0].RoundText);
        Assert.Equal("10823", rounds[0].RoundScoreText);
        Assert.Equal(2, rounds[0].Teams.Count);
    }

    [Fact]
    public void GroupTeamsByRound_HandlesNoTeams()
    {
        Assert.Empty(TowerViewModel.GroupTeamsByRound(null));
        Assert.Empty(TowerViewModel.GroupTeamsByRound([]));
    }

    [Fact]
    public void RankText_CoversSixGradesFromRealData()
    {
        // 实机 rank:稳态协议=3(S)、奇点扩张=5(SSS)。旧实现只映射 0..3,rank=5 会被显示成 C
        Assert.Equal("S", TowerViewModel.RankTextOf(3));
        Assert.Equal("SSS", TowerViewModel.RankTextOf(5));
        Assert.Equal(["C", "B", "A", "S", "SS", "SSS"], Enumerable.Range(0, 6).Select(TowerViewModel.RankTextOf));
    }
}
