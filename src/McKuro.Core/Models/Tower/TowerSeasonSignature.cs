using System.Text;

namespace McKuro.Core.Models.Tower;

/// <summary>
/// 深塔页「赛季数据」指纹:把深塔/终焉矩阵/海墟三块**会被展示出来的**数值压成一个字符串,
/// 用来判断「这次重拉回来的数据跟界面上已有的一致吗」。
/// <para>
/// 为什么需要它:页面在可见状态下会每 20 秒静默重拉一次赛季数据(见 <c>TowerViewModel</c> 的自动刷新)。
/// 若每次都清空并重建 <c>TowerDifficulties</c>/<c>TowerAreas</c>/<c>SlashChallenges</c>,即使用户什么都没打,
/// 列表也会每 20 秒重建一次 —— 滚动位置回到顶部、下拉难度被弹回第一项,
/// 看起来像页面在"自己抽搐"。有了指纹,只有数值真的变了才动界面。
/// </para>
/// <para>
/// <b>刻意排除时间字段</b>(<see cref="TowerSeasonData.SeasonEndTime"/>、
/// <see cref="NewTowerData.EndTime"/>、<see cref="SlashData.SeasonEndTime"/>):
/// 接口下发的是"本期剩余毫秒",每秒都在变小。把它算进指纹,任何一次重拉都会被判定为"有变化",
/// 指纹就完全失去意义(而且倒计时文案本来也不随这一路刷新走)。
/// </para>
/// <para>
/// 也不看全息战略/地图探索:这两个是"打过就长期不变"的记录类数据,由进页重拉、手动刷新与
/// 游戏结束重拉负责;后台轮询不请求它们(少 3 个数据中心请求 = 少的金额外风控面),自然也不必比对。
/// 角色配队按 roleId 参与指纹:同一层换了配队,楼层卡的图标确实会变。
/// </para>
/// </summary>
public static class TowerSeasonSignature
{
    /// <summary>计算赛季三件套(深塔/矩阵/海墟)的指纹;入参为 null 时按"空数据"参与。</summary>
    public static string Compute(TowerSeasonData? tower, NewTowerData? newTower, SlashData? slash)
    {
        var sb = new StringBuilder(384);
        AppendTower(sb, tower);
        AppendNewTower(sb, newTower);
        AppendSlash(sb, slash);
        return sb.ToString();
    }

    /// <summary>逆境深塔:解锁态 + 每难度/区域/楼层的星数与配队。</summary>
    private static void AppendTower(StringBuilder sb, TowerSeasonData? tower)
    {
        sb.Append("T:").Append(tower is null ? "-" : tower.IsUnlock ? "1" : "0");
        if (tower?.DifficultyList is not { } difficulties)
        {
            return;
        }
        // 顺序无关:接口偶尔乱序,排序后再拼,避免同一份数据因为排列不同被判成"变了"
        foreach (var difficulty in difficulties.OrderBy(d => d.Difficulty))
        {
            sb.Append("|D").Append(difficulty.Difficulty).Append('[');
            foreach (var area in (difficulty.TowerAreaList ?? []).OrderBy(a => a.AreaId))
            {
                sb.Append('A').Append(area.AreaId).Append('=').Append(area.Star).Append('/').Append(area.MaxStar).Append('{');
                foreach (var floor in (area.FloorList ?? []).OrderBy(f => f.Floor))
                {
                    sb.Append('F').Append(floor.Floor).Append('=').Append(floor.Star).Append('<');
                    foreach (var role in floor.RoleList ?? [])
                    {
                        sb.Append(role.RoleId).Append(',');
                    }
                    sb.Append('>');
                }
                sb.Append('}');
            }
            sb.Append(']');
        }
    }

    /// <summary>终焉矩阵:解锁态 + 每模式分数/进度/评级 + 每队分数。</summary>
    private static void AppendNewTower(StringBuilder sb, NewTowerData? newTower)
    {
        sb.Append("|N:").Append(newTower is null ? "-" : newTower.IsUnlock ? "1" : "0");
        if (newTower?.ModeDetails is not { } modes)
        {
            return;
        }
        foreach (var mode in modes.OrderBy(m => m.ModeId))
        {
            sb.Append("|M").Append(mode.ModeId)
                .Append('=').Append(mode.Score)
                .Append('/').Append(mode.PassBoss).Append('-').Append(mode.BossCount)
                .Append('@').Append(mode.Rank)
                .Append('#').Append(mode.Round)
                .Append(mode.HasRecord ? '!' : '?');
            foreach (var team in mode.Teams ?? [])
            {
                sb.Append("+t").Append(team.Score).Append(':').Append(team.Round);
            }
        }
    }

    /// <summary>海墟:每难度总分 + 每关分数/评级/上下一半分数。</summary>
    private static void AppendSlash(StringBuilder sb, SlashData? slash)
    {
        sb.Append("|S:").Append(slash is null ? "-" : "1");
        if (slash?.DifficultyList is not { } difficulties)
        {
            return;
        }
        foreach (var difficulty in difficulties.OrderBy(d => d.Difficulty))
        {
            sb.Append("|d").Append(difficulty.Difficulty)
                .Append('=').Append(difficulty.AllScore).Append('/').Append(difficulty.MaxScore);
            foreach (var challenge in (difficulty.ChallengeList ?? []).OrderBy(c => c.ChallengeId))
            {
                sb.Append("|c").Append(challenge.ChallengeId)
                    .Append('=').Append(challenge.Score)
                    .Append(' ').Append(challenge.Rank ?? "");
                foreach (var half in challenge.HalfList ?? [])
                {
                    sb.Append('/').Append(half.Score);
                }
            }
        }
    }
}
