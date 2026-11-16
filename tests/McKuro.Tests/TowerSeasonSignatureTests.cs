using McKuro.Core.Models.Tower;

namespace McKuro.Tests;

/// <summary>
/// 赛季数据指纹(<see cref="TowerSeasonSignature"/>)测试。
/// <para>
/// 这张指纹决定"后台静默刷新要不要重建界面":算错一侧都有明确后果 ——
/// 该变时不变 = 用户看到的还是旧战绩;不该变时变 = 列表每 20 秒重建一次,
/// 滚动位置与难度选中被打回原位。故这里同时盯住"变化要能检出"与"噪声不能检出"两侧。
/// </para>
/// </summary>
public class TowerSeasonSignatureTests
{
    private static TowerSeasonData Tower(int star = 3, int floorStar = 3, long? seasonEndTime = 86_400_000) => new()
    {
        IsUnlock = true,
        SeasonEndTime = seasonEndTime,
        DifficultyList =
        [
            new TowerSeasonDifficulty
            {
                Difficulty = 3,
                DifficultyName = "深境区",
                TowerAreaList =
                [
                    new TowerSeasonArea
                    {
                        AreaId = 11,
                        AreaName = "残响之塔",
                        Star = star,
                        MaxStar = 12,
                        FloorList =
                        [
                            new TowerSeasonFloor
                            {
                                Floor = 1,
                                Star = floorStar,
                                RoleList = [new TowerSeasonRole { RoleId = 1108 }],
                            },
                        ],
                    },
                ],
            },
        ],
    };

    private static NewTowerData NewTower(int score = 6600, long? endTime = 1_000_000) => new()
    {
        IsUnlock = true,
        EndTime = endTime,
        ModeDetails =
        [
            new NewTowerModeDetail
            {
                ModeId = 0,
                Score = score,
                PassBoss = 3,
                BossCount = 4,
                Round = 1,
                Rank = 3,
                HasRecord = true,
                Teams =
                [
                    new NewTowerTeam { Score = score, Round = 1, PassBoss = 3, BossCount = 4 },
                ],
            },
        ],
    };

    private static SlashData Slash(int challengeScore = 4440, long? seasonEndTime = 2_000_000) => new()
    {
        SeasonEndTime = seasonEndTime,
        DifficultyList =
        [
            new SlashDifficulty
            {
                Difficulty = 1,
                AllScore = 9000,
                MaxScore = 12000,
                ChallengeList =
                [
                    new SlashChallenge
                    {
                        ChallengeId = 9,
                        Score = challengeScore,
                        Rank = "S",
                        HalfList = [new SlashHalf { Score = challengeScore }],
                    },
                ],
            },
        ],
    };

    [Fact]
    public void IdenticalData_ProducesSameSignature()
    {
        var a = TowerSeasonSignature.Compute(Tower(), NewTower(), Slash());
        var b = TowerSeasonSignature.Compute(Tower(), NewTower(), Slash());

        Assert.Equal(a, b);
    }

    [Fact]
    public void RemainingTime_DoesNotAffectSignature()
    {
        // seasonEndTime / endTime 是"剩余毫秒",每次请求都在变小:若参与指纹,
        // 每一次后台轮询都会被判成"数据变了",界面会被无谓地重建。
        var before = TowerSeasonSignature.Compute(
            Tower(seasonEndTime: 86_400_000), NewTower(endTime: 1_000_000), Slash(seasonEndTime: 2_000_000));
        var after = TowerSeasonSignature.Compute(
            Tower(seasonEndTime: 86_390_000), NewTower(endTime: 990_000), Slash(seasonEndTime: 1_990_000));

        Assert.Equal(before, after);
    }

    [Fact]
    public void ChangedTowerStar_ChangesSignature()
    {
        var before = TowerSeasonSignature.Compute(Tower(star: 3), NewTower(), Slash());
        var after = TowerSeasonSignature.Compute(Tower(star: 6), NewTower(), Slash());

        Assert.NotEqual(before, after);
    }

    [Fact]
    public void ChangedFloorStar_ChangesSignature()
    {
        var before = TowerSeasonSignature.Compute(Tower(floorStar: 1), NewTower(), Slash());
        var after = TowerSeasonSignature.Compute(Tower(floorStar: 3), NewTower(), Slash());

        Assert.NotEqual(before, after);
    }

    [Fact]
    public void ChangedMatrixScore_ChangesSignature()
    {
        var before = TowerSeasonSignature.Compute(Tower(), NewTower(score: 6600), Slash());
        var after = TowerSeasonSignature.Compute(Tower(), NewTower(score: 10823), Slash());

        Assert.NotEqual(before, after);
    }

    [Fact]
    public void ChangedSlashScore_ChangesSignature()
    {
        var before = TowerSeasonSignature.Compute(Tower(), NewTower(), Slash(challengeScore: 4440));
        var after = TowerSeasonSignature.Compute(Tower(), NewTower(), Slash(challengeScore: 0));

        Assert.NotEqual(before, after);
    }

    [Fact]
    public void ChangedTeamComposition_ChangesSignature()
    {
        // 同一层换了配队:楼层卡的队伍图标会变,指纹必须跟着变
        var withA = Tower();
        var withB = Tower();
        withB.DifficultyList![0].TowerAreaList![0].FloorList![0].RoleList = [new TowerSeasonRole { RoleId = 1209 }];

        Assert.NotEqual(
            TowerSeasonSignature.Compute(withA, NewTower(), Slash()),
            TowerSeasonSignature.Compute(withB, NewTower(), Slash()));
    }

    [Fact]
    public void EmptyInputs_AreStableAndDistinct()
    {
        var empty = TowerSeasonSignature.Compute(null, null, null);

        Assert.Equal(empty, TowerSeasonSignature.Compute(null, null, null));
        Assert.NotEqual(empty, TowerSeasonSignature.Compute(Tower(), null, null));
    }

    [Fact]
    public void DifficultyOrder_DoesNotMatter()
    {
        // 接口偶发乱序:同一份数据只要内容一致,指纹就必须一致,否则会被误判成"变了"
        var first = Tower();
        var second = Tower();
        first.DifficultyList!.Add(new TowerSeasonDifficulty { Difficulty = 1, DifficultyName = "稳定区" });
        second.DifficultyList!.Add(new TowerSeasonDifficulty { Difficulty = 1, DifficultyName = "稳定区" });
        second.DifficultyList.Reverse();

        Assert.Equal(
            TowerSeasonSignature.Compute(first, NewTower(), Slash()),
            TowerSeasonSignature.Compute(second, NewTower(), Slash()));
    }
}
