namespace McKuro.Core.Models.Gacha;

/// <summary>单条抽卡分析记录(包含五星间隔与 UP 判定)。</summary>
public sealed class GachaPullEntry
{
    public required GachaRecord Record { get; init; }

    /// <summary>该条记录在当前卡池中的抽卡序号(1 起)。</summary>
    public int Index { get; init; }

    /// <summary>若该条是五星,则为该五星的垫抽数(不含本次,即距离上一个五星的普通抽数)。</summary>
    public int? Pity { get; init; }

    /// <summary>该条之前是否已经出现过五星。</summary>
    public bool HasPreviousFiveStar { get; init; }

    /// <summary>若该条是五星且之前已有五星,则为两个五星之间的普通抽数。</summary>
    public int? FiveStarGap => HasPreviousFiveStar && Pity.HasValue ? Pity.Value : null;

    /// <summary>是否歪了(false=UP,true=歪,null=无法判定)。</summary>
    public bool? IsOffBanner { get; init; }

    public bool IsUp => IsOffBanner == false;

    public string BannerText => IsOffBanner switch
    {
        false => "UP",
        true => "歪",
        _ => "",
    };

    public string PityText => Pity.HasValue ? $"{Pity.Value} 抽" : "";

    public string FiveStarGapText => FiveStarGap.HasValue ? $"{FiveStarGap.Value} 抽" : "-";

    public string IconUrl => IconCatalog.GetIconUrl(Record);
}

/// <summary>单条五星出货记录(含垫抽数)。</summary>
public sealed class FiveStarEntry
{
    public required GachaRecord Record { get; init; }

    /// <summary>该五星的垫抽数(不含本次,即距离上一个五星的普通抽数;与 Haiyu FormatStartFive 一致)。</summary>
    public int Pity { get; init; }

    /// <summary>进度条显示值(封顶 80,避免超保底数据撑破进度条)。</summary>
    public int PityBarValue => Math.Min(Pity, 80);

    /// <summary>是否歪了(false=UP,true=歪,null=无法判定)。</summary>
    public bool? IsOffBanner { get; init; }

    /// <summary>两个五星之间的普通抽数(与 Pity 相同,不含本次)。</summary>
    public int FiveStarGap => Math.Max(0, Pity);

    public string FiveStarGapText => Index > 1 ? $"间隔 {FiveStarGap} 抽" : "首次五星";

    /// <summary>该五星在整个卡池中的序号(1 起)。</summary>
    public int Index { get; init; }

    /// <summary>角色/武器图标 URL(未收录时为空串)。</summary>
    public string IconUrl => IconCatalog.GetIconUrl(Record);
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

    /// <summary>所有抽卡记录的分析条目(旧→新)。</summary>
    public IReadOnlyList<GachaPullEntry> PullEntries { get; init; } = [];

    /// <summary>五星出货列表(旧→新)。</summary>
    public IReadOnlyList<FiveStarEntry> FiveStarEntries { get; init; } = [];

    /// <summary>当前已垫抽数(最后一个五星之后)。</summary>
    public int CurrentPity { get; init; }

    /// <summary>最近一次 UP 五星之后到现在的垫抽数;没有可判定 UP 时为 0。</summary>
    public int CurrentUpPity { get; init; }

    /// <summary>用于界面显示的最近一次 UP 垫抽文本。</summary>
    public string CurrentUpPityText => UpCount > 0 ? $"UP 后垫抽: {CurrentUpPity} 抽" : "UP 后垫抽: -";

    /// <summary>距上次五星的平均抽数(含本次,与 Haiyu FormatRecordFive 一致;若至少有 1 个五星)。</summary>
    public double? AveragePity => FiveStarEntries.Count > 0
        ? FiveStarEntries.Average(x => x.Pity + 1)
        : null;

    /// <summary>小保底歪率(0~1,无法判定时为 null)。</summary>
    public double? OffBannerRate { get; init; }

    /// <summary>四星数量。</summary>
    public int FourStarCount { get; init; }

    /// <summary>三星数量。</summary>
    public int ThreeStarCount { get; init; }

    /// <summary>UP 五星数量(可判定时;无法判定为 0)。</summary>
    public int UpCount { get; init; }

    /// <summary>UP 率(UP 五星占全部五星比例,0~1)。</summary>
    public double? UpRate => FiveStarCount > 0 && FiveStarEntries.Any(e => e.IsOffBanner.HasValue)
        ? (double)UpCount / FiveStarCount
        : null;

    /// <summary>是否有 UP/歪 判定可用(常驻/新手等无 UP 池为 false,界面据此隐藏"不歪率"等行)。</summary>
    public bool CanJudgeUp => FiveStarEntries.Any(e => e.IsOffBanner.HasValue);

    /// <summary>记录起始日期(YYYY-MM-dd)。</summary>
    public string StartDate { get; init; } = "";

    /// <summary>记录结束日期(YYYY-MM-dd)。</summary>
    public string EndDate { get; init; } = "";

    /// <summary>是否有小保底机制(角色活动/角色联动池)。</summary>
    public bool HasPityMechanism => PoolType is CardPoolType.RoleActivity or CardPoolType.CharacterCollaboration;

    // ---- "预计下一抽"模型(界面统计卡片用) ----

    /// <summary>是否有 UP 目标;常驻/新手唤取/新手自选/感恩定向无 UP 概念,目标是任意 5★。</summary>
    public bool HasUpTarget => PoolType is not
        (CardPoolType.RoleResident or
         CardPoolType.WeaponsResident or
         CardPoolType.Beginner or
         CardPoolType.BeginnerChoice or
         CardPoolType.GratitudeOrientation);

    /// <summary>是否有 50/50 小保底机制(角色 UP 池:活动/联动/忆旅/新旅;武器 UP 池不歪)。</summary>
    public bool IsFiftyFifty => PoolType is
        CardPoolType.RoleActivity or
        CardPoolType.CharacterCollaboration or
        CardPoolType.CharacterMemoryJourney or
        CardPoolType.CharacterNovice;

    /// <summary>是否处于大保底(上一个五星歪了,下一金必 UP;仅 50/50 池有意义)。</summary>
    public bool IsGuaranteedUp => IsFiftyFifty && FiveStarEntries.LastOrDefault()?.IsOffBanner == true;

    /// <summary>下一抽获得 5★ 的概率(0~1,按当前垫抽数估算)。</summary>
    public double NextFiveStarRate => GachaRateModel.FiveStarRateAtPity(CurrentPity);

    /// <summary>
    /// 预计下一抽获得目标(UP 或常驻任意 5★)的概率(%)。
    /// <para>50/50 角色池:小保底 50%、大保底 100%;武器池与常驻池:出金即目标(不歪);无 UP 池目标为任意 5★。</para>
    /// </summary>
    public double NextPullUpPercent
    {
        get
        {
            var rate = NextFiveStarRate * 100;
            if (IsFiftyFifty && !IsGuaranteedUp)
            {
                return rate * 0.5;
            }
            return rate;
        }
    }

    /// <summary>HERO 数字的标签:有 UP 池显示 UP 概率,无 UP 池显示 5★ 概率。</summary>
    public string UpChanceCaption => HasUpTarget ? "预计下一抽 UP" : "预计下一抽 5★";

    /// <summary>保底状态徽标文本(大保底/小保底/必UP/无UP)。</summary>
    public string GuaranteeBadgeText => HasUpTarget
        ? (IsFiftyFifty ? (IsGuaranteedUp ? "大保底" : "小保底") : "必UP")
        : "无UP";

    /// <summary>保底状态徽标语义色键(guaranteed/fifty/always/none),供配色转换器。</summary>
    public string GuaranteeBadgeKind => HasUpTarget
        ? (IsFiftyFifty ? (IsGuaranteedUp ? "guaranteed" : "fifty") : "always")
        : "none";

    /// <summary>保底进度条值(当前垫抽,封顶 80;进度条 Maximum 绑 <see cref="GachaRateModel.HardPity"/>)。</summary>
    public double PityProgressValue => Math.Min(CurrentPity, GachaRateModel.HardPity);

    /// <summary>五星期望值(鸣潮硬保底 80,软保底机制按官方概率近似)。</summary>
    public const double ExpectedFiveStarPity = 80.0;
}

/// <summary>某日某卡池的抽数(时间线悬浮提示用)。</summary>
public sealed class DailyPoolPull
{
    public required CardPoolType PoolType { get; init; }

    /// <summary>卡池显示名(如"角色活动")。</summary>
    public required string PoolName { get; init; }

    public required int Count { get; init; }
}

/// <summary>每日抽数(用于时间线图)。</summary>
public sealed class DailyPull
{
    public required DateOnly Date { get; init; }
    public required int Count { get; init; }

    /// <summary>当日各卡池抽数明细(按抽数降序,供悬浮提示)。</summary>
    public IReadOnlyList<DailyPoolPull> Pools { get; init; } = [];
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

    /// <summary>称号(大非酋/非酋/平民/小欧皇/至尊无敌欧皇)。</summary>
    public string Designation { get; init; } = "平民";

    /// <summary>双金次数(10 抽内两个五星)。</summary>
    public int DoubleCount { get; init; }

    /// <summary>歪的次数(可判定 UP 的池子)。</summary>
    public int CrookedTotal { get; init; }

    /// <summary>平均出金抽数(整体)。</summary>
    public double AvgPulls { get; init; }

    /// <summary>实际出金率(%)。五星数/总抽数*100。</summary>
    public double ActualFiveStarRate { get; init; }

    /// <summary>抽卡跨度天数(首尾记录间隔)。</summary>
    public int Days { get; init; }

    /// <summary>每日抽数(旧→新,用于时间线图)。</summary>
    public IReadOnlyList<DailyPull> DailyPulls { get; init; } = [];

    public DateTime AnalysisTime { get; init; } = DateTime.Now;
}
