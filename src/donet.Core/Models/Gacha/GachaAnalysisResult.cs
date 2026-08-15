namespace donet.Core.Models.Gacha;

/// <summary>单条五星出货记录(含垫抽数)。</summary>
public sealed class FiveStarEntry
{
    public required GachaRecord Record { get; init; }

    /// <summary>抽到该五星时的垫抽数(距离上一个五星的抽数)。</summary>
    public int Pity { get; init; }

    /// <summary>是否歪了(false=UP,true=歪,null=无法判定)。</summary>
    public bool? IsOffBanner { get; init; }

    /// <summary>该五星在整个卡池中的序号(1 起)。</summary>
    public int Index { get; init; }
}

/// <summary>单个卡池的统计结果。</summary>
public sealed class PoolStats
{
    public required CardPoolType PoolType { get; init; }
    public string DisplayName => CardPoolTypeValues.GetDisplayName(PoolType);

    /// <summary>总抽数。</summary>
    public int TotalPulls { get; init; }

    /// <summary>五星数量。</summary>
    public int FiveStarCount { get; init; }

    /// <summary>五星出货列表(旧→新)。</summary>
    public IReadOnlyList<FiveStarEntry> FiveStarEntries { get; init; } = [];

    /// <summary>当前已垫抽数(最后一个五星之后)。</summary>
    public int CurrentPity { get; init; }

    /// <summary>距上次五星的平均抽数(若至少有 1 个五星)。</summary>
    public double? AveragePity => FiveStarEntries.Count > 0
        ? FiveStarEntries.Average(x => x.Pity)
        : null;

    /// <summary>小保底歪率(0~1,无法判定时为 null)。</summary>
    public double? OffBannerRate { get; init; }

    /// <summary>五星期望值(鸣潮硬保底 80,软保底机制按官方概率近似)。</summary>
    public const double ExpectedFiveStarPity = 80.0;
}

/// <summary>抽卡分析的整体结果。</summary>
public sealed class GachaAnalysisResult
{
    public required string PlayerId { get; init; }

    /// <summary>全部卡池统计(按卡池类型)。</summary>
    public IReadOnlyList<PoolStats> Pools { get; init; } = [];

    public PoolStats? this[CardPoolType type] => Pools.FirstOrDefault(x => x.PoolType == type);

    /// <summary>总抽数。</summary>
    public int TotalPulls => Pools.Sum(x => x.TotalPulls);

    /// <summary>总五星数。</summary>
    public int TotalFiveStars => Pools.Sum(x => x.FiveStarCount);

    /// <summary>综合评分(参考 Haiyu 的 Score 算法,0~100,越高越欧)。</summary>
    public double Score { get; init; }

    public DateTime AnalysisTime { get; init; } = DateTime.Now;
}
