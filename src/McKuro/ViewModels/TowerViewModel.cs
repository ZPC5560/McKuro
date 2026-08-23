using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using McKuro.Core.Models.Tower;
using McKuro.Services;

namespace McKuro.ViewModels;

/// <summary>逆境深塔-难度展示项(选择器)。</summary>
public sealed class TowerDifficultyItem
{
    public required string DifficultyName { get; init; }
    public required int Difficulty { get; init; }
    /// <summary>最高难度(difficulty==3)显示赛季刷新倒计时(对齐 WutheringWavesTool TowerView)。</summary>
    public bool ShowSeasonEnd => Difficulty == 3;
    public required List<TowerAreaItem> Areas { get; init; }
}

/// <summary>逆境深塔-区域展示项。</summary>
public sealed class TowerAreaItem
{
    public required string AreaName { get; init; }
    /// <summary>已得星/总星,如 "12 / 15"。</summary>
    public required string StarText { get; init; }
    public required List<TowerFloorItem> Floors { get; init; }
}

/// <summary>逆境深塔-楼层展示项。</summary>
public sealed class TowerFloorItem
{
    public required string FloorName { get; init; }
    /// <summary>已得星(0-3)。</summary>
    public required int Star { get; init; }
    /// <summary>已得星字符串,如 "★★"。</summary>
    public string FilledStars => new('★', Star);
    /// <summary>未得星字符串,如 "☆"。</summary>
    public string EmptyStars => new('☆', 3 - Star);
    public required List<TowerSeasonRole> Roles { get; init; }
}

/// <summary>海墟/终焉矩阵 buff 展示项。</summary>
public sealed class TowerBuffItem
{
    public required string BuffName { get; init; }
    public string? BuffIcon { get; init; }
    public string? BuffDescription { get; init; }
}

/// <summary>终焉矩阵模式展示项。</summary>
public sealed class TowerModeItem
{
    public required string ModeName { get; init; }
    public required string ScoreText { get; init; }
    /// <summary>通关进度,如 "2/5"。</summary>
    public required string ProgressText { get; init; }
    public required string RankText { get; init; }
    public required string RankColor { get; init; }
    public required List<NewTowerRole> Roles { get; init; }
    public required List<TowerBuffItem> Buffs { get; init; }
}

/// <summary>海墟-单个队伍(半分)展示项。</summary>
public sealed class SlashTeamItem
{
    public required string TeamName { get; init; }
    public required string ScoreText { get; init; }
    public required string BuffName { get; init; }
    public string? BuffIcon { get; init; }
    public string? BuffDescription { get; init; }
    public required List<NewTowerRole> Roles { get; init; }
}

/// <summary>海墟关卡展示项。</summary>
public sealed class SlashChallengeItem
{
    /// <summary>关卡编号(challengeId,如 7..12)。</summary>
    public required int ChallengeId { get; init; }
    /// <summary>编号文本,如 "第7关"。</summary>
    public required string ChallengeNoText { get; init; }
    public required string ChallengeName { get; init; }
    public required string ScoreText { get; init; }
    public required string RankText { get; init; }
    public required string RankColor { get; init; }
    /// <summary>逐队(上半/下半)展示。</summary>
    public required List<SlashTeamItem> Teams { get; init; }
}

/// <summary>
/// 深塔/海墟页:三个页签展示逆境深塔(towerDataDetail)、终焉矩阵(newTowerDetail)与再生海域(slashDetail),
/// 解析对齐 Java 版 WutheringWavesTool(TowerViewModel / NewTowerViewModel / SlashViewModel)。
/// </summary>
public sealed partial class TowerViewModel : ViewModelBase
{
    /// <summary>页签:0=逆境深塔,1=终焉矩阵,2=海墟。</summary>
    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private string _statusText = "未加载";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _hasData;

    /// <summary>逆境深塔-已解锁(未解锁时页签内显示提示)。</summary>
    [ObservableProperty]
    private bool _towerUnlocked;

    /// <summary>逆境深塔-赛季刷新倒计时("X天Y小时后刷新",仅最高难度下显示)。</summary>
    [ObservableProperty]
    private string _towerSeasonEndText = "";

    /// <summary>终焉矩阵-已解锁(未解锁时页签内显示提示)。</summary>
    [ObservableProperty]
    private bool _newTowerUnlocked;

    public ObservableCollection<TowerDifficultyItem> TowerDifficulties { get; } = [];

    /// <summary>当前选中难度的区域列表。</summary>
    public ObservableCollection<TowerAreaItem> TowerAreas { get; } = [];

    /// <summary>终焉矩阵模式列表。</summary>
    public ObservableCollection<TowerModeItem> TowerModes { get; } = [];

    /// <summary>海墟关卡列表。</summary>
    public ObservableCollection<SlashChallengeItem> SlashChallenges { get; } = [];

    [ObservableProperty]
    private TowerDifficultyItem? _selectedTowerDifficulty;

    partial void OnSelectedTowerDifficultyChanged(TowerDifficultyItem? value)
    {
        TowerAreas.Clear();
        if (value is null)
        {
            TowerSeasonEndText = "";
            return;
        }
        foreach (var area in value.Areas)
        {
            TowerAreas.Add(area);
        }
        // 对齐 WutheringWavesTool TowerView:仅最高难度(difficulty==3)显示赛季刷新倒计时
        TowerSeasonEndText = value.ShowSeasonEnd ? TowerSeasonParser.RefreshText(
            _towerSeasonEndMillis) : "";
    }

    private long? _towerSeasonEndMillis;

    /// <summary>进入页面自动刷新深塔/海墟数据(替代原手动"刷新"按钮)。</summary>
    public TowerViewModel()
    {
        _ = LoadAsync();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsBusy)
        {
            return;
        }
        IsBusy = true;
        StatusText = "正在加载…";
        try
        {
            var (tower, newTower, slash, error) = await AppServices.Tower.GetTowerDataAsync();
            TowerDifficulties.Clear();
            TowerAreas.Clear();
            TowerModes.Clear();
            SlashChallenges.Clear();
            if (!string.IsNullOrEmpty(error))
            {
                StatusText = error;
                HasData = false;
                return;
            }

            // ---- 逆境深塔(towerDataDetail,对齐 TowerDataDetailTask/TowerViewModel) ----
            if (tower is not null)
            {
                TowerUnlocked = tower.IsUnlock;
                _towerSeasonEndMillis = tower.SeasonEndTime;
                var sorted = TowerSeasonParser.SortDifficulties(tower.DifficultyList);
                if (sorted.Count > 0)
                {
                    // 每难度的区域/楼层映射为展示项
                    var items = sorted
                        .Where(d => (d.TowerAreaList?.Count ?? 0) > 0)
                        .Select(d => new TowerDifficultyItem
                        {
                            DifficultyName = d.DifficultyName ?? $"难度 {d.Difficulty}",
                            Difficulty = d.Difficulty,
                            Areas = d.TowerAreaList!.Select(a => new TowerAreaItem
                            {
                                AreaName = a.AreaName ?? $"区域 {a.AreaId}",
                                StarText = $"{a.Star} / {a.MaxStar}",
                                Floors = (a.FloorList ?? [])
                                    .OrderBy(f => f.Floor)
                                    .Select(f => new TowerFloorItem
                                    {
                                        FloorName = $"第{f.Floor}层",
                                        Star = Math.Max(0, Math.Min(f.Star, 3)),
                                        Roles = f.RoleList ?? [],
                                    })
                                    .ToList(),
                            }).ToList(),
                        })
                        .ToList();
                    foreach (var d in items)
                    {
                        TowerDifficulties.Add(d);
                    }
                    SelectedTowerDifficulty = TowerDifficulties.FirstOrDefault();
                }
            }

            // ---- 终焉矩阵(newTowerDetail,对齐 NewTowerViewModel:modeId 0=稳态协议,其余=奇点扩张) ----
            if (newTower is not null)
            {
                NewTowerUnlocked = newTower.IsUnlock;
                foreach (var m in newTower.ModeDetails?.Where(x => x.HasRecord && x.Score > 0) ?? [])
                {
                    TowerModes.Add(new TowerModeItem
                    {
                        ModeName = m.ModeId == 0 ? "稳态协议" : "奇点扩张",
                        ScoreText = $"{m.Score}",
                        ProgressText = $"{m.PassBoss}/{m.BossCount}",
                        RankText = RankToText(m.Rank),
                        RankColor = RankToColor(m.Rank),
                        Roles = m.Teams?.SelectMany(t => t.RoleList ?? []).ToList() ?? [],
                        Buffs = m.Teams?.SelectMany(t => t.Buffs ?? [])
                            .Select(b => new TowerBuffItem
                            {
                                BuffName = b.BuffName ?? "特殊增益",
                                BuffIcon = b.BuffIcon,
                                BuffDescription = b.Desc,
                            }).ToList() ?? [],
                    });
                }
            }

            // ---- 海墟(slashDetail,对齐 SlashViewModel.updateDate) ----
            if (slash is { DifficultyList: not null })
            {
                // 1. 过滤 difficulty==0(禁忌海域)与 allScore==0
                // 2. 无尽湍渊(difficulty=2)的关卡插到「再生海域」(difficulty=1)列表头
                var validDiffs = slash.DifficultyList
                    .Where(d => d.Difficulty is 1 or 2 && d.AllScore > 0)
                    .OrderByDescending(d => d.Difficulty)
                    .ToList();
                foreach (var diff in validDiffs)
                {
                    bool isTurbid = diff.Difficulty == 2;
                    foreach (var c in diff.ChallengeList ?? [])
                    {
                        var halves = c.HalfList ?? [];
                        SlashChallenges.Add(new SlashChallengeItem
                        {
                            ChallengeId = c.ChallengeId,
                            ChallengeNoText = $"第{c.ChallengeId}关",
                            ChallengeName = isTurbid
                                ? (c.ChallengeName ?? $"无尽湍渊 {c.ChallengeId}")
                                : (c.ChallengeName ?? $"关卡 {c.ChallengeId}"),
                            ScoreText = $"{c.Score}",
                            RankText = SlashRankText(c.Rank),
                            RankColor = SlashRankColor(c.Rank),
                            Teams = halves.Select((h, i) => new SlashTeamItem
                            {
                                TeamName = i == 0 ? "上半" : "下半",
                                ScoreText = $"{h.Score}",
                                BuffName = string.IsNullOrWhiteSpace(h.BuffName) ? "无增益" : h.BuffName!,
                                BuffIcon = h.BuffIcon,
                                BuffDescription = h.BuffDescription,
                                Roles = h.RoleList ?? [],
                            }).ToList(),
                        });
                    }
                }
            }

            HasData = TowerDifficulties.Count > 0 || TowerModes.Count > 0 || SlashChallenges.Count > 0;
            StatusText = $"加载完成(逆境深塔 {TowerDifficulties.Count} 难度 / 终焉矩阵 {TowerModes.Count} 模式 / 海墟 {SlashChallenges.Count} 关)";
        }
        catch (Exception ex)
        {
            StatusText = $"加载失败: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string RankToText(int rank) => rank switch
    {
        0 => "C",
        1 => "B",
        2 => "A",
        3 => "S",
        _ => "?",
    };

    private static string RankToColor(int rank) => rank switch
    {
        0 => "#9e9e9e",
        1 => "#4caf50",
        2 => "#2196f3",
        3 => "#f8f05c",
        _ => "#9e9e9e",
    };

    /// <summary>海墟 rank 是字符串(S/A/B/C),直接展示(参照 WutheringWavesTool SlashChallenge.rank)。</summary>
    private static string SlashRankText(string? rank)
        => string.IsNullOrWhiteSpace(rank) ? "?" : rank;

    private static string SlashRankColor(string? rank) => (rank ?? "").ToUpperInvariant() switch
    {
        "S" => "#f8f05c",
        "A" => "#2196f3",
        "B" => "#4caf50",
        "C" => "#9e9e9e",
        _ => "#9e9e9e",
    };
}
