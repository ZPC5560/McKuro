using System.Collections.ObjectModel;
using System.Text.Json;
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

/// <summary>终焉矩阵往期历史条目(一期,按赛季结束时间标识,对齐 WutheringWavesTool initHistory)。</summary>
public sealed class NewTowerHistoryItem
{
    public required long EndTimeMillis { get; init; }
    /// <summary>列表文案,如 "2026.08.01 前的记录"。</summary>
    public required string Label { get; init; }
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

    /// <summary>海墟-刷新倒计时("X天Y小时后刷新",对齐 WutheringWavesTool updateSeasonEndTime)。</summary>
    [ObservableProperty]
    private string _slashSeasonEndText = "";

    /// <summary>海墟-再生海域总积分(allScore/maxScore,对齐 WutheringWavesTool score01)。</summary>
    [ObservableProperty]
    private string _slashTotalScoreText = "";

    /// <summary>海墟-无尽湍渊总积分(有记录才显示,对齐 WutheringWavesTool score02)。</summary>
    [ObservableProperty]
    private string _slashTurbidScoreText = "";

    /// <summary>是否显示无尽湍渊总积分。</summary>
    [ObservableProperty]
    private bool _slashHasTurbidScore;

    /// <summary>海墟-本期是否有挑战记录(无记录时页签内显示提示)。</summary>
    [ObservableProperty]
    private bool _slashHasRecord;

    public ObservableCollection<TowerDifficultyItem> TowerDifficulties { get; } = [];

    /// <summary>当前选中难度的区域列表。</summary>
    public ObservableCollection<TowerAreaItem> TowerAreas { get; } = [];

    /// <summary>终焉矩阵模式列表。</summary>
    public ObservableCollection<TowerModeItem> TowerModes { get; } = [];

    /// <summary>终焉矩阵往期历史列表(按赛季结束时间降序)。</summary>
    public ObservableCollection<NewTowerHistoryItem> NewTowerHistory { get; } = [];

    /// <summary>是否有往期历史(控制「暂无往期记录」占位显隐)。</summary>
    public bool HasNewTowerHistory => NewTowerHistory.Count > 0;

    /// <summary>当前选中的矩阵模式(右栏详情)。</summary>
    [ObservableProperty]
    private TowerModeItem? _selectedTowerMode;

    /// <summary>当前选中的往期历史(与模式选择互斥)。</summary>
    [ObservableProperty]
    private NewTowerHistoryItem? _selectedNewTowerHistory;

    /// <summary>历史详情标题(如 "历史-终焉矩阵")。</summary>
    [ObservableProperty]
    private string _newTowerDetailTitle = "";

    /// <summary>历史详情(从本地库加载的一期记录)。</summary>
    [ObservableProperty]
    private TowerModeItem? _newTowerHistoryDetail;

    partial void OnSelectedTowerModeChanged(TowerModeItem? value)
    {
        if (value is not null)
        {
            _selectedNewTowerHistory = null;
            OnPropertyChanged(nameof(SelectedNewTowerHistory));
            NewTowerHistoryDetail = null;
            NewTowerDetailTitle = "";
        }
        OnPropertyChanged(nameof(NewTowerDetail));
        OnPropertyChanged(nameof(NewTowerShowWaiting));
    }

    partial void OnSelectedNewTowerHistoryChanged(NewTowerHistoryItem? value)
    {
        if (value is not null)
        {
            _selectedTowerMode = null;
            OnPropertyChanged(nameof(SelectedTowerMode));
            _ = LoadNewTowerHistoryDetailAsync(value);
        }
        else
        {
            NewTowerHistoryDetail = null;
            NewTowerDetailTitle = "";
        }
        OnPropertyChanged(nameof(NewTowerDetail));
        OnPropertyChanged(nameof(NewTowerShowWaiting));
    }

    partial void OnNewTowerHistoryDetailChanged(TowerModeItem? value)
    {
        OnPropertyChanged(nameof(NewTowerDetail));
        OnPropertyChanged(nameof(NewTowerShowWaiting));
    }

    /// <summary>右栏详情:历史查看优先,否则当前选中模式。</summary>
    public TowerModeItem? NewTowerDetail => NewTowerHistoryDetail ?? SelectedTowerMode;

    /// <summary>右栏是否显示「当前版本等待开放中」:本期无模式记录且未查看历史。</summary>
    public bool NewTowerShowWaiting => TowerModes.Count == 0 && NewTowerHistoryDetail is null;

    /// <summary>本次加载解析出的库街区角色条目 ID(矩阵历史按它落库/读取)。</summary>
    private string _newTowerRoleId = "";

    /// <summary>加载某期历史详情(对齐 WutheringWavesTool changHistory/updateHistoryData:取第一条模式记录)。</summary>
    private async Task LoadNewTowerHistoryDetailAsync(NewTowerHistoryItem item)
    {
        try
        {
            var json = await Task.Run(() => AppServices.Database.GetNewTowerHistory(_newTowerRoleId, item.EndTimeMillis));
            if (json is null)
            {
                NewTowerHistoryDetail = null;
                NewTowerDetailTitle = "";
                return;
            }
            var modes = JsonSerializer.Deserialize(json, TowerJsonContext.Default.ListNewTowerModeDetail);
            var first = modes?.FirstOrDefault();
            NewTowerDetailTitle = "历史-终焉矩阵";
            NewTowerHistoryDetail = first is null ? null : BuildModeItem(first, prefix: "历史-");
        }
        catch (Exception)
        {
            NewTowerHistoryDetail = null;
            NewTowerDetailTitle = "";
        }
        OnPropertyChanged(nameof(NewTowerShowWaiting));
    }

    /// <summary>把矩阵模式详情映射为展示项(rank 0-3 → C/B/A/S,对齐 RANK_MAP)。</summary>
    private static TowerModeItem BuildModeItem(NewTowerModeDetail m, string prefix = "")
    {
        var rank = m.Rank is >= 0 and <= 3 ? m.Rank : 0;
        return new TowerModeItem
        {
            ModeName = prefix + (m.ModeId == 0 ? "稳态协议" : "奇点扩张"),
            ScoreText = $"{m.Score}",
            ProgressText = m.ModeId == 0
                ? $"{m.PassBoss}/{m.BossCount}"
                : $"第{m.Round}轮 {m.PassBoss}/{m.BossCount}",
            RankText = RankToText(rank),
            RankColor = RankToColor(rank),
            Roles = m.Teams?.SelectMany(t => t.RoleList ?? []).ToList() ?? [],
            Buffs = m.Teams?.SelectMany(t => t.Buffs ?? [])
                .Select(b => new TowerBuffItem
                {
                    BuffName = b.BuffName ?? "特殊增益",
                    BuffIcon = b.BuffIcon,
                    BuffDescription = b.Desc,
                }).ToList() ?? [],
        };
    }

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
        NewTowerHistory.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNewTowerHistory));
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
            var (tower, newTower, slash, error, roleId) = await AppServices.Tower.GetTowerDataAsync();
            TowerDifficulties.Clear();
            TowerAreas.Clear();
            TowerModes.Clear();
            SlashChallenges.Clear();
            SlashSeasonEndText = "";
            SlashTotalScoreText = "";
            SlashTurbidScoreText = "";
            SlashHasTurbidScore = false;
            SlashHasRecord = false;
            SelectedTowerMode = null;
            SelectedNewTowerHistory = null;
            NewTowerHistoryDetail = null;
            NewTowerDetailTitle = "";
            NewTowerHistory.Clear();
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
                    TowerModes.Add(BuildModeItem(m));
                }
                SelectedTowerMode = TowerModes.FirstOrDefault();
            }

            // 往期历史(本地库,按赛季结束时间降序;对齐 WutheringWavesTool initHistory)
            if (!string.IsNullOrEmpty(roleId))
            {
                foreach (var end in AppServices.Database.GetNewTowerHistoryEndTimes(roleId))
                {
                    NewTowerHistory.Add(new NewTowerHistoryItem
                    {
                        EndTimeMillis = end,
                        Label = $"{DateTimeOffset.FromUnixTimeMilliseconds(end).LocalDateTime:yyyy.MM.dd} 前的记录",
                    });
                }
            }

            // ---- 海墟(slashDetail,对齐 SlashViewModel.updateDate/updateScore) ----
            if (slash is { DifficultyList: not null })
            {
                // 总积分与刷新倒计时(difficulty:1=再生海域,2=无尽湍渊;0=禁忌海域不计)
                var regen = slash.DifficultyList.FirstOrDefault(d => d.Difficulty == 1);
                var turbid = slash.DifficultyList.FirstOrDefault(d => d.Difficulty == 2);
                SlashSeasonEndText = TowerSeasonParser.RefreshText(slash.SeasonEndTime);
                if (regen is not null && regen.AllScore > 0)
                {
                    SlashTotalScoreText = $"{regen.AllScore} / {regen.MaxScore}";
                }
                if (turbid is not null && turbid.AllScore > 0)
                {
                    SlashTurbidScoreText = $"无尽湍渊 {turbid.AllScore} / {turbid.MaxScore}";
                    SlashHasTurbidScore = true;
                }

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
                        if (halves.Count == 0)
                        {
                            continue;
                        }
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
                SlashHasRecord = SlashChallenges.Count > 0;
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
