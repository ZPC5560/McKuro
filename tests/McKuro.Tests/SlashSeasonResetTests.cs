using McKuro.Core.Models.Tower;

namespace McKuro.Tests;

/// <summary>
/// 海墟「赛季翻新后不展示上期成绩」相关纯函数测试。
/// <para>
/// 实机背景:库街区在**赛季已结束**时仍返回上一期数据(seasonEndTime 为负剩余毫秒),
/// 且第 9/10/11 关不会被清零 —— 实测赛季翻了约 3 天后,接口仍返回
/// 9/10/11 各 4440 分,页面遂把上赛季成绩当作本期成绩展示。
/// </para>
/// <para>
/// 官方规则:上期第 7、8 关达 S 且海隙总分 ≥15000、湍渊 ≥4500 时,下期 7/8 关会以相同配队
/// **自动覆盖挑战记录**(前提是 7/8 的关卡内容不随赛季更换);9/10/11 每期更换内容、进度清零。
/// 接口没有任何字段标记「常驻关」,故用 challengeId ∈ {7,8} 判定,并以赛季是否结束作为门闸。
/// </para>
/// </summary>
public class SlashSeasonResetTests
{
    private static SlashDifficulty Diff(int difficulty, int allScore, params int[] challengeIds)
        => new()
        {
            Difficulty = difficulty,
            AllScore = allScore,
            MaxScore = 1000,
            ChallengeList = challengeIds.Select(id => new SlashChallenge
            {
                ChallengeId = id,
                ChallengeName = $"关卡{id}",
                Score = id * 10,
                Rank = "S",
                HalfList = [new SlashHalf { Score = id * 5 }],
            }).ToList(),
        };

    [Fact]
    public void IsCrossSeasonSlashChallenge_Only_7_And_8()
    {
        Assert.True(TowerSeasonParser.IsCrossSeasonSlashChallenge(7));
        Assert.True(TowerSeasonParser.IsCrossSeasonSlashChallenge(8));
        foreach (var id in new[] { 1, 6, 9, 10, 11, 12 })
        {
            Assert.False(TowerSeasonParser.IsCrossSeasonSlashChallenge(id));
        }
    }

    [Fact]
    public void IsSeasonEnded_Negative_Remaining_Means_Ended()
    {
        // 实机取值 -279303239(该期已过去约 3.2 天)
        Assert.True(TowerSeasonParser.IsSeasonEnded(-279_303_239));
        Assert.True(TowerSeasonParser.IsSeasonEnded(0));
        Assert.False(TowerSeasonParser.IsSeasonEnded(931_285_790));
        Assert.False(TowerSeasonParser.IsSeasonEnded(null));
    }

    [Fact]
    public void Season_In_Progress_Keeps_All_Challenges()
    {
        // 赛季进行中:behavior 不变,difficulty 1/2 全量展示(12 关含湍渊)
        var list = new List<SlashDifficulty>
        {
            Diff(0, 14870, 1, 2, 3, 4, 5, 6),
            Diff(1, 19220, 7, 8, 9, 10, 11),
            Diff(2, 6000, 12),
        };

        var selected = TowerSeasonParser.SelectSlashChallenges(list, seasonEnded: false);

        // 海隙 5 关(id 7..11)+ 湍渊 1 关(id 12)= 6;禁忌海域(id 1..6)不计
        Assert.Equal(6, selected.Count);
        Assert.Contains(selected, s => s.Challenge.ChallengeId == 9);
        Assert.Contains(selected, s => s.Challenge.ChallengeId == 11);
        Assert.Contains(selected, s => s.Challenge.ChallengeId == 12);
    }

    [Fact]
    public void Season_Ended_Keeps_Only_Cross_Season_Stages()
    {
        // 赛季已结束:接口返回的是上一期残留,只保留跨赛季延续的第 7、8 关
        var list = new List<SlashDifficulty>
        {
            Diff(0, 14870, 1, 2, 3, 4, 5, 6),   // 禁忌海域:本就不展示
            Diff(1, 19220, 7, 8, 9, 10, 11),    // 海隙:只留 7、8
            Diff(2, 6000, 12),                  // 湍渊:每期重置,不展示上期成绩
        };

        var selected = TowerSeasonParser.SelectSlashChallenges(list, seasonEnded: true);

        Assert.Equal(2, selected.Count);
        Assert.Equal([7, 8], selected.Select(s => s.Challenge.ChallengeId).ToArray());
        Assert.All(selected, s => Assert.False(s.IsTurbid));
    }

    [Fact]
    public void Season_Ended_Orders_Turbid_First_When_Kept()
    {
        // 赛季进行中时湍渊(difficulty=2)排在再生海域之前(原行为)
        var list = new List<SlashDifficulty>
        {
            Diff(1, 100, 9),
            Diff(2, 200, 12),
        };

        var selected = TowerSeasonParser.SelectSlashChallenges(list, seasonEnded: false);

        Assert.Equal([12, 9], selected.Select(s => s.Challenge.ChallengeId).ToArray());
        Assert.True(selected[0].IsTurbid);
    }

    [Fact]
    public void Challenges_Without_Halves_Are_Skipped()
    {
        var noHalves = new SlashDifficulty
        {
            Difficulty = 1,
            AllScore = 100,
            MaxScore = 1000,
            ChallengeList =
            [
                new SlashChallenge { ChallengeId = 7, Score = 10, HalfList = null },
                new SlashChallenge { ChallengeId = 8, Score = 10, HalfList = [] },
                new SlashChallenge
                {
                    ChallengeId = 9,
                    Score = 10,
                    HalfList = [new SlashHalf { Score = 5 }],
                },
            ],
        };

        var selected = TowerSeasonParser.SelectSlashChallenges([noHalves], seasonEnded: false);

        // 无上/下半队伍数据的关卡不展示(原行为保留)
        Assert.Equal([9], selected.Select(s => s.Challenge.ChallengeId).ToArray());
    }

    [Fact]
    public void Zero_Score_Difficulty_Is_Excluded()
    {
        // 未解锁/无成绩的难度(allScore==0)整块不展示
        var list = new List<SlashDifficulty>
        {
            Diff(1, 0, 7, 8),
            Diff(2, 0, 12),
        };

        Assert.Empty(TowerSeasonParser.SelectSlashChallenges(list, seasonEnded: false));
    }

    [Fact]
    public void Null_List_Returns_Empty()
        => Assert.Empty(TowerSeasonParser.SelectSlashChallenges(null, seasonEnded: true));

    [Fact]
    public void Ended_Season_Sums_Only_Displayed_Stages()
    {
        // 页头总积分在赛季已结束时应按实际展示的第 7、8 关求和,
        // 而不是沿用接口整季合计(19220 含已清零的 9/10/11)
        var list = new List<SlashDifficulty>
        {
            Diff(1, 19220, 7, 8, 9, 10, 11),
        };

        var selected = TowerSeasonParser.SelectSlashChallenges(list, seasonEnded: true);

        Assert.Equal(7 * 10 + 8 * 10, selected.Sum(s => s.Challenge.Score));
    }
}
