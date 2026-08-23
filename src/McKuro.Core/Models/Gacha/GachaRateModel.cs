namespace McKuro.Core.Models.Gacha;

/// <summary>
/// 抽卡概率模型(鸣潮官方口径近似,用于"预计下一抽"估算)。
/// <list type="bullet">
/// <item>5★ 基础概率 1.8%(第 1~65 抽固定);</item>
/// <item>第 66 抽起进入软保底,每抽较上抽 +4%;</item>
/// <item>第 80 抽硬保底必出 5★。</item>
/// </list>
/// 各卡池共享该曲线(角色/武器/常驻);软保底线性递增为社区公认区间的近似,
/// 仅供界面参考,不保证与官方综合概率完全一致。
/// </summary>
public static class GachaRateModel
{
    /// <summary>5★ 基础概率(官方公告 1.8%)。</summary>
    public const double BaseFiveStar = 0.018;

    /// <summary>软保底起始抽:已垫 65 抽后,下一抽(第 66 抽)开始提升。</summary>
    public const int SoftPityStart = 65;

    /// <summary>硬保底:第 80 抽必出 5★。</summary>
    public const int HardPity = 80;

    /// <summary>软保底阶段每抽提升幅度。</summary>
    public const double SoftPityStep = 0.04;

    /// <summary>
    /// 已垫抽数为 <paramref name="pity"/> 时,下一抽获得 5★ 的概率(0~1)。
    /// <paramref name="pity"/> 为 0 表示刚出过 5★;79 表示下一抽必为第 80 抽。
    /// </summary>
    public static double FiveStarRateAtPity(int pity)
    {
        if (pity >= HardPity - 1)
        {
            return 1.0;
        }
        if (pity < SoftPityStart)
        {
            return BaseFiveStar;
        }
        return BaseFiveStar + (pity - SoftPityStart + 1) * SoftPityStep;
    }
}
