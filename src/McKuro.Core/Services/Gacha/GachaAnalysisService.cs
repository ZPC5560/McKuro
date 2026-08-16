using McKuro.Core.Models.Gacha;

namespace McKuro.Core.Services.Gacha;

/// <summary>
/// 抽卡记录分析:保底计算、小保底歪率、平均出货、综合评分。
/// 算法参考 Haiyu(WutheringWavesTool 的 C# 版)的分析实现。
/// </summary>
public sealed class GachaAnalysisService
{
    /// <summary>
    /// 分析某玩家(或全部玩家)的抽卡记录。
    /// </summary>
    /// <param name="playerId">玩家 ID;<see cref="string.Empty"/> 表示聚合全部玩家。</param>
    /// <param name="allRecords">全部记录(任意顺序,内部按时间排序)。</param>
    /// <param name="upIds">各卡池 UP 五星 ID 集合(用于判定歪/不歪);为 null 时不做判定。</param>
    public GachaAnalysisResult Analyze(
        string playerId,
        IEnumerable<GachaRecord> allRecords,
        IReadOnlyDictionary<CardPoolType, HashSet<int>>? upIds = null)
    {
        var sorted = allRecords
            .Where(r => string.IsNullOrEmpty(playerId) || r.PlayerId == playerId || string.IsNullOrEmpty(r.PlayerId))
            .OrderBy(r => r.Time)
            .ThenBy(r => r.ResourceId)
            .ToList();

        var grouped = sorted.GroupBy(r => (CardPoolType)r.CardPoolType);

        var pools = new List<PoolStats>();
        foreach (var group in grouped)
        {
            var poolType = group.Key;
            var records = group.ToList();
            pools.Add(AnalyzePool(poolType, records, upIds));
        }

        var roleActivity = pools.FirstOrDefault(p => p.PoolType == CardPoolType.RoleActivity);
        var weaponActivity = pools.FirstOrDefault(p => p.PoolType == CardPoolType.WeaponsActivity);
        var resident = pools.FirstOrDefault(p =>
            p.PoolType == CardPoolType.RoleResident || p.PoolType == CardPoolType.WeaponsResident);

        double guaranteedRange = roleActivity?.OffBannerRate ?? weaponActivity?.OffBannerRate ?? 0;
        double roleAvg = roleActivity?.AveragePity ?? 0;
        double weaponAvg = weaponActivity?.AveragePity ?? 0;
        double residentAvg = resident?.AveragePity ?? 0;

        double score = ComputeScore(guaranteedRange, roleAvg, weaponAvg, residentAvg);

        // ---- Haiyu 风格的整体统计 ----
        var allPulls = sorted;
        int allStarTotal = allPulls.Count(r => r.IsFiveStar);
        int totalPulls = allPulls.Count;

        // 双金次数:每个五星后面 9 次抽卡(位置)内再出金计一次
        int doubleCount = 0;
        for (int i = 0; i < allPulls.Count; i++)
        {
            if (!allPulls[i].IsFiveStar)
            {
                continue;
            }
            for (int j = i + 1; j < Math.Min(i + 10, allPulls.Count); j++)
            {
                if (allPulls[j].IsFiveStar)
                {
                    doubleCount++;
                    break;
                }
            }
        }

        // 平均出金抽数:每个五星的 Pity(含本次)的平均
        var allFivePities = pools
            .SelectMany(p => p.FiveStarEntries)
            .Select(e => e.Pity)
            .ToList();
        double avgPulls = allFivePities.Count > 0 ? Math.Round(allFivePities.Average(), 1) : 0;

        // 实际出金率(%)
        double actualRate = totalPulls > 0 ? Math.Round((double)allStarTotal / totalPulls * 100, 2) : 0;

        // 抽卡跨度天数
        var parsedTimes = allPulls
            .Select(r => TryParseTime(r.Time))
            .Where(t => t.HasValue)
            .Select(t => t!.Value)
            .ToList();
        int days = parsedTimes.Count > 1 ? (parsedTimes[^1] - parsedTimes[0]).Days : 0;

        // 歪的次数(仅可判定 UP 的池子)
        int crookedTotal = pools
            .Where(p => p.HasPityMechanism)
            .SelectMany(p => p.FiveStarEntries)
            .Count(e => e.IsOffBanner == true);

        // 每日抽数(时间线)
        var dailyPulls = allPulls
            .Select(r => new { Date = TryParseTime(r.Time), Rec = r })
            .Where(x => x.Date.HasValue)
            .GroupBy(x => DateOnly.FromDateTime(x.Date!.Value.Date))
            .OrderBy(g => g.Key)
            .Select(g => new DailyPull { Date = g.Key, Count = g.Count() })
            .ToList();

        return new GachaAnalysisResult
        {
            PlayerId = playerId,
            Pools = pools,
            Score = score,
            Designation = ComputeDesignation(score),
            DoubleCount = doubleCount,
            CrookedTotal = crookedTotal,
            AvgPulls = avgPulls,
            ActualFiveStarRate = actualRate,
            Days = days,
            DailyPulls = dailyPulls,
        };
    }

    private PoolStats AnalyzePool(
        CardPoolType poolType,
        List<GachaRecord> records,
        IReadOnlyDictionary<CardPoolType, HashSet<int>>? upIds)
    {
        var entries = new List<FiveStarEntry>();
        int pityCount = 0;
        int index = 0;
        int fiveStarIndex = 0;
        int currentPity = 0;

        HashSet<int>? upSet = upIds is not null && upIds.TryGetValue(poolType, out var set) ? set : null;

        foreach (var record in records)
        {
            index++;
            if (record.IsFiveStar)
            {
                fiveStarIndex++;
                bool? offBanner = null;
                if (upSet is not null)
                {
                    offBanner = !upSet.Contains(record.ResourceId);
                }

                entries.Add(new FiveStarEntry
                {
                    Record = record,
                    Pity = pityCount + 1, // 含本次出货的抽数
                    IsOffBanner = offBanner,
                    Index = fiveStarIndex,
                });
                currentPity = 0;
                pityCount = 0;
            }
            else
            {
                pityCount++;
                currentPity++;
            }
        }

        return new PoolStats
        {
            PoolType = poolType,
            TotalPulls = records.Count,
            FiveStarCount = entries.Count,
            FiveStarEntries = entries,
            CurrentPity = currentPity,
            OffBannerRate = ComputeOffBannerRate(entries),
        };
    }

    /// <summary>
    /// 小保底歪率:按照保底规则,每个"小保底"五星若歪了则下一个必为大保底。
    /// 统计所有小保底中歪掉的比例。
    /// </summary>
    private static double? ComputeOffBannerRate(IReadOnlyList<FiveStarEntry> entries)
    {
        int totalSmallGuarantees = 0;
        int fails = 0;
        bool isNextSmallGuarantee = true;

        foreach (var entry in entries)
        {
            if (!entry.IsOffBanner.HasValue)
            {
                continue;
            }

            if (isNextSmallGuarantee)
            {
                totalSmallGuarantees++;
                if (entry.IsOffBanner.Value)
                {
                    fails++;
                }
            }

            isNextSmallGuarantee = !entry.IsOffBanner.Value;
        }

        if (totalSmallGuarantees == 0)
        {
            return null;
        }
        return (double)fails / totalSmallGuarantees;
    }

    /// <summary>
    /// 综合欧气评分(0~100,越高越欧),参考 Haiyu 的 Score 公式:
    /// 权重:小保底歪率 40%、活动角色平均抽数 20%、活动武器平均抽数 20%、常驻平均抽数 20%。
    /// </summary>
    private static double ComputeScore(
        double guaranteedRange,
        double roleAvg,
        double weaponAvg,
        double residentAvg)
    {
        const double w1 = 0.40, w2 = 0.20, w3 = 0.20, w4 = 0.20;
        const double max1 = 100.0, max2 = 80.0, max3 = 80.0, max4 = 80.0;

        double s1 = (1 - Math.Clamp(guaranteedRange, 0, max1) / max1) * w1;
        double s2 = (1 - Math.Clamp(roleAvg, 0, max2) / max2) * w2;
        double s3 = (1 - Math.Clamp(weaponAvg, 0, max3) / max3) * w3;
        double s4 = (1 - Math.Clamp(residentAvg, 0, max4) / max4) * w4;

        return Math.Clamp((s1 + s2 + s3 + s4) * 100, 0, 100);
    }

    /// <summary>根据综合评分给出称号(参考 Haiyu 的 EvaluateLuck)。</summary>
    private static string ComputeDesignation(double score) => score switch
    {
        < 20 => "大非酋",
        < 40 => "非酋",
        < 60 => "平民",
        < 80 => "小欧皇",
        _ => "至尊无敌欧皇",
    };

    /// <summary>解析记录时间字符串(兼容 ISO 8601 与 "yyyy-MM-dd HH:mm:ss")。</summary>
    private static DateTime? TryParseTime(string time)
    {
        if (string.IsNullOrWhiteSpace(time))
        {
            return null;
        }
        return DateTime.TryParse(time, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var dt)
            ? dt
            : null;
    }
}
