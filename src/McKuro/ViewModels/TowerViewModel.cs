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
    /// <summary>按轮次分组的队伍明细(每队一条:轮次/该队积分/进度/角色/增益)。</summary>
    public List<TowerRoundItem> Rounds { get; init; } = [];
}

/// <summary>终焉矩阵-一轮(一轮内可有多支队伍)。</summary>
public sealed class TowerRoundItem
{
    /// <summary>轮次标题,如 "第1轮";无轮次信息时为空(不显示)。</summary>
    public required string RoundText { get; init; }
    /// <summary>本轮合计积分。</summary>
    public required string RoundScoreText { get; init; }
    /// <summary>本轮队伍列表。</summary>
    public required List<TowerTeamItem> Teams { get; init; }
}

/// <summary>终焉矩阵-单支队伍(矩阵按轮次分队,每队有独立积分)。</summary>
public sealed class TowerTeamItem
{
    /// <summary>队伍序号文本(按该轮内顺序,如 "第1队")。</summary>
    public required string TeamNoText { get; init; }
    public required string ScoreText { get; init; }
    /// <summary>该队进度,如 "3/5"。</summary>
    public required string PassText { get; init; }
    public required List<NewTowerRole> Roles { get; init; }
    public required List<TowerBuffItem> Buffs { get; init; }
}

/// <summary>终焉矩阵往期历史条目(一期,按赛季结束时间标识,对齐 WutheringWavesTool initHistory)。</summary>
public sealed class NewTowerHistoryItem
{
    public required long EndTimeMillis { get; init; }
    /// <summary>列表文案,如 "2026.08.01 前的记录"。</summary>
    public required string Label { get; init; }
    /// <summary>该期起始日期(按赛季周期由结束日倒推),如 "2026.09.02"。</summary>
    public required string StartDateText { get; init; }
    /// <summary>该期结束日期,如 "2026.09.30"。</summary>
    public required string EndDateText { get; init; }
    /// <summary>时间区间文案,如 "2026.09.02 → 2026.09.30"。</summary>
    public string PeriodText => $"{StartDateText} → {EndDateText}";
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
    private string _statusText = LanguageService.Format("Tower.NotLoaded");

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
            NewTowerDetailTitle = LanguageService.Format("Tower.HistoryMatrix");
            NewTowerHistoryDetail = first is null ? null : BuildModeItem(first, prefix: LanguageService.Format("Tower.HistoryPrefix"));
        }
        catch (Exception)
        {
            NewTowerHistoryDetail = null;
            NewTowerDetailTitle = "";
        }
        OnPropertyChanged(nameof(NewTowerShowWaiting));
    }

    /// <summary>
    /// 矩阵队伍按轮次分组(纯函数,供单测):轮次升序,每轮给出轮内合计积分与各队明细。
    /// 稳态协议(round=0/缺失)不显示轮次标题,归入同一组。
    /// </summary>
    public static List<TowerRoundItem> GroupTeamsByRound(IEnumerable<NewTowerTeam>? teams)
        => (teams ?? [])
            .GroupBy(t => t.Round)
            .OrderBy(g => g.Key)
            .Select(g => new TowerRoundItem
            {
                RoundText = g.Key > 0 ? LanguageService.Format("Tower.RoundNo", g.Key) : "",
                RoundScoreText = $"{g.Sum(t => t.Score)}",
                Teams = g.Select((t, i) => new TowerTeamItem
                {
                    TeamNoText = LanguageService.Format("Tower.TeamNo", i + 1),
                    ScoreText = $"{t.Score}",
                    PassText = $"{t.PassBoss}/{t.BossCount}",
                    Roles = t.RoleList ?? [],
                    Buffs = (t.Buffs ?? [])
                        .Select(b => new TowerBuffItem
                        {
                            BuffName = b.BuffName ?? LanguageService.Format("Tower.SpecialBuff"),
                            BuffIcon = b.BuffIcon,
                            BuffDescription = b.Desc,
                        }).ToList(),
                }).ToList(),
            })
            .ToList();

    /// <summary>把矩阵模式详情映射为展示项(rank 0-5 → C/B/A/S/SS/SSS,见 RankTextOf)。</summary>
    private static TowerModeItem BuildModeItem(NewTowerModeDetail m, string prefix = "")
    {
        var rank = m.Rank is >= 0 and <= 5 ? m.Rank : 0;
        var teams = m.Teams ?? [];
        // 按轮次分组(每轮给出轮内合计积分,便于对照"每轮队伍分数")
        var rounds = GroupTeamsByRound(teams);

        return new TowerModeItem
        {
            ModeName = prefix + (m.ModeId == 0 ? LanguageService.Format("Tower.ModeStable") : LanguageService.Format("Tower.ModeSingularity")),
            ScoreText = $"{m.Score}",
            ProgressText = m.ModeId == 0
                ? $"{m.PassBoss}/{m.BossCount}"
                : LanguageService.Format("Tower.RoundInfo", m.Round, m.PassBoss, m.BossCount),
            RankText = RankTextOf(rank),
            RankColor = RankToColor(rank),
            Roles = teams.SelectMany(t => t.RoleList ?? []).ToList(),
            Buffs = teams.SelectMany(t => t.Buffs ?? [])
                .Select(b => new TowerBuffItem
                {
                    BuffName = b.BuffName ?? LanguageService.Format("Tower.SpecialBuff"),
                    BuffIcon = b.BuffIcon,
                    BuffDescription = b.Desc,
                }).ToList(),
            Rounds = rounds,
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
        // 对齐 WutheringWavesTool TowerView:仅最高难度(difficulty==3)显示赛季刷新倒计时;
        // 若这一期已结束(库街区返回的是上一期数据),明确提示"本期已结束",而不是让倒计时凭空消失
        TowerSeasonEndText = value.ShowSeasonEnd ? SeasonEndText(_towerSeasonEndMillis) : "";
    }

    private long? _towerSeasonEndMillis;

    /// <summary>进入页面自动刷新深塔/海墟数据(替代原手动"刷新"按钮)。</summary>
    public TowerViewModel()
    {
        NewTowerHistory.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNewTowerHistory));
        _ = LoadAsync();
    }

    /// <summary>上次成功/尝试拉取的时间(进入页面时的"新鲜度"判断,避免反复切页狂刷接口)。</summary>
    private DateTime _lastLoadAt = DateTime.MinValue;

    /// <summary>进入页面的最小重新拉取间隔(秒):越快于它反复切页就不重复请求,规避风控。</summary>
    private const int ReloadMinIntervalSeconds = 60;

    /// <summary>
    /// 导航到深塔/海墟页时调用:重新拉取数据。
    /// 页面 VM 在 App 启动时就全部建好并只构造一次(见 MainWindowViewModel),构造函数里的
    /// 那次加载发生在启动瞬间;没有这个入口,页面上永远是启动那一刻的战绩快照
    /// —— 这正是"深塔海墟数据不刷新"的原因(游戏内打完再进来还是旧分,只能重启 App)。
    /// </summary>
    public void OnNavigatedTo()
    {
        if (IsBusy)
        {
            return;
        }
        // 刚拉过就不重复拉(切页很频繁;接口还有风控风险)
        if (DateTime.Now - _lastLoadAt < TimeSpan.FromSeconds(ReloadMinIntervalSeconds))
        {
            return;
        }
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
        _lastLoadAt = DateTime.Now;
        StatusText = LanguageService.Format("Common.Loading");
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
                            DifficultyName = d.DifficultyName ?? LanguageService.Format("Tower.Difficulty", d.Difficulty),
                            Difficulty = d.Difficulty,
                            Areas = d.TowerAreaList!.Select(a => new TowerAreaItem
                            {
                                AreaName = a.AreaName ?? LanguageService.Format("Tower.Area", a.AreaId),
                                StarText = $"{a.Star} / {a.MaxStar}",
                                Floors = (a.FloorList ?? [])
                                    .OrderBy(f => f.Floor)
                                    .Select(f => new TowerFloorItem
                                    {
                                        FloorName = LanguageService.Format("Tower.Floor", f.Floor),
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
                // 本期已结束时(endTime ≤ 0),接口返回的是**上一期**的战绩:这批数据已由
                // TowerService.SaveNewTowerHistory 落库为历史一期,不应再挂在「挑战模式」(本期)下,
                // 否则同一期数据同时出现在"本期"和"往期历史"两处,看起来像本期已出成绩。
                var newTowerEnded = TowerSeasonParser.IsSeasonEnded(newTower.EndTime);
                if (!newTowerEnded)
                {
                    foreach (var m in newTower.ModeDetails?.Where(x => x.HasRecord && x.Score > 0) ?? [])
                    {
                        TowerModes.Add(BuildModeItem(m));
                    }
                    SelectedTowerMode = TowerModes.FirstOrDefault();
                }
            }

            // 往期历史(本地库,按赛季结束时间降序;对齐 WutheringWavesTool initHistory)
            if (!string.IsNullOrEmpty(roleId))
            {
                // 必须记录本次 roleId:LoadNewTowerHistoryDetailAsync 用它读历史详情。
                // 此前该字段从未赋值(恒为空串)→ 读库必然查不到行 → 点「往期历史」右栏
                // 永远停在"当前版本等待开放中",表现为"上期记录没有显示"。
                _newTowerRoleId = roleId;
                foreach (var end in AppServices.Database.GetNewTowerHistoryEndTimes(roleId))
                {
                    var endLocal = DateTimeOffset.FromUnixTimeMilliseconds(end).LocalDateTime;
                    // 只显示一个孤立截止日时看不出"这一期覆盖哪段时间";按赛季周期倒推起始日,
                    // 与参考实现(起止日期两行 + 时间节点)一致。
                    var startLocal = TowerSeasonParser.SeasonStartDate(endLocal);
                    NewTowerHistory.Add(new NewTowerHistoryItem
                    {
                        EndTimeMillis = end,
                        Label = LanguageService.Format("Tower.RecordBefore", endLocal.ToString("yyyy.MM.dd")),
                        StartDateText = startLocal.ToString("yyyy.MM.dd"),
                        EndDateText = endLocal.ToString("yyyy.MM.dd"),
                    });
                }
                // 本期无成绩但有往期记录时,默认展开最近一期:否则右栏一直显示空态,
                // 用户会以为"往期历史是坏的"。本期有成绩时不抢占,保持"本期优先"。
                if (TowerModes.Count == 0 && NewTowerHistory.Count > 0)
                {
                    SelectedNewTowerHistory = NewTowerHistory[0];
                }
            }

            // ---- 海墟(slashDetail,对齐 SlashViewModel.updateDate/updateScore) ----
            if (slash is { DifficultyList: not null })
            {
                // difficulty:1=再生海域-海隙,2=无尽湍渊;0=禁忌海域不计
                var regen = slash.DifficultyList.FirstOrDefault(d => d.Difficulty == 1);
                var turbid = slash.DifficultyList.FirstOrDefault(d => d.Difficulty == 2);
                SlashSeasonEndText = SeasonEndText(slash.SeasonEndTime);

                // 1. 过滤 difficulty==0(禁忌海域)与 allScore==0
                // 2. 无尽湍渊(difficulty=2)的关卡插到「再生海域」(difficulty=1)列表头
                // 3. 赛季已结束时接口返回的是上一期残留:只保留跨赛季延续的第 7、8 关,
                //    丢弃每期清零的 9/10/11 与无尽湍渊(否则会拿上赛季满档成绩充当本期成绩)
                var seasonEnded = TowerSeasonParser.IsSeasonEnded(slash.SeasonEndTime);
                var selected = TowerSeasonParser.SelectSlashChallenges(slash.DifficultyList, seasonEnded);

                // 总积分:赛季已结束时不能沿用接口的整季合计(含已清零的 9/10/11),
                // 否则会把上赛季的 19220 当成"本期总积分";改按实际展示的关卡求和 ——
                // 7/8 跨赛季延续计为已有成绩,每期重打的关卡本期为 0。
                if (seasonEnded)
                {
                    var regenShown = selected.Where(s => !s.IsTurbid).Sum(s => s.Challenge.Score);
                    if (regen is not null && regenShown > 0)
                    {
                        SlashTotalScoreText = $"{regenShown} / {regen.MaxScore}";
                    }
                }
                else
                {
                    if (regen is not null && regen.AllScore > 0)
                    {
                        SlashTotalScoreText = $"{regen.AllScore} / {regen.MaxScore}";
                    }
                    if (turbid is not null && turbid.AllScore > 0)
                    {
                        SlashTurbidScoreText = LanguageService.Format("Tower.TurbidScore", turbid.AllScore, turbid.MaxScore);
                        SlashHasTurbidScore = true;
                    }
                }

                foreach (var (c, isTurbid) in selected)
                {
                    var halves = c.HalfList!;
                    SlashChallenges.Add(new SlashChallengeItem
                    {
                        ChallengeId = c.ChallengeId,
                        ChallengeNoText = LanguageService.Format("Tower.ChallengeNo", c.ChallengeId),
                        ChallengeName = isTurbid
                            ? (c.ChallengeName ?? LanguageService.Format("Tower.TurbidName", c.ChallengeId))
                            : (c.ChallengeName ?? LanguageService.Format("Tower.LevelName", c.ChallengeId)),
                        ScoreText = $"{c.Score}",
                        RankText = SlashRankText(c.Rank),
                        RankColor = SlashRankColor(c.Rank),
                        Teams = halves.Select((h, i) => new SlashTeamItem
                        {
                            TeamName = i == 0 ? LanguageService.Format("Tower.TeamFirst") : LanguageService.Format("Tower.TeamSecond"),
                            ScoreText = $"{h.Score}",
                            BuffName = string.IsNullOrWhiteSpace(h.BuffName) ? LanguageService.Format("Tower.NoBuff") : h.BuffName!,
                            BuffIcon = h.BuffIcon,
                            BuffDescription = h.BuffDescription,
                            Roles = h.RoleList ?? [],
                        }).ToList(),
                    });
                }
                SlashHasRecord = SlashChallenges.Count > 0;
            }

            HasData = TowerDifficulties.Count > 0 || TowerModes.Count > 0 || SlashChallenges.Count > 0;
            StatusText = LanguageService.Format("Tower.StatusLoaded", TowerDifficulties.Count, TowerModes.Count, SlashChallenges.Count);
        }
        catch (Exception ex)
        {
            StatusText = LanguageService.Format("Status.LoadFailedWith", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 赛季剩余时间文案:正常 → "X天Y小时后刷新";≤0(接口返回的这一期已结束)→ "本期已结束"。
    /// 后者是"看起来数据没刷新"的常见成因:账号没打新一期时接口会把上一期数据连着已过去的
    /// seasonEndTime 一起返回,旧实现此时返回空串,倒计时消失但分数照旧显示。
    /// </summary>
    private static string SeasonEndText(long? remainingMillis)
        => TowerSeasonParser.IsSeasonEnded(remainingMillis)
            ? LanguageService.Format("Tower.SeasonEnded")
            : TowerSeasonParser.RefreshText(remainingMillis);

    /// <summary>
    /// 矩阵评级 rank → 字母(公开供单测)。实机数据 rank 值域 0..5(稳态协议 3=S、奇点扩张 44065 分 rank=5),
    /// 参考实现只有 4 级 {"C","B","A","S"},rank≥4 会落到范围外被显示成 C(实测 44065 分被标成 C),
    /// 故按库街区 6 级补全:sss/ss/s/a/b/c。
    /// </summary>
    public static string RankTextOf(int rank) => rank switch
    {
        0 => "C",
        1 => "B",
        2 => "A",
        3 => "S",
        4 => "SS",
        5 => "SSS",
        _ => "?",
    };

    /// <summary>数值档评级色:先转文字档,再走与 <see cref="SlashRankColor"/> 同一份档位→色值表
    /// (评审反馈:此前 4/5 仍落 S 的黄色,同一页两套顶级档配色不一致)。</summary>
    private static string RankToColor(int rank) => SlashRankColor(RankTextOf(rank));

    /// <summary>
    /// 海墟 rank 是字符串(S/A/B/C/SS/SSS),直接展示(参照 WutheringWavesTool SlashChallenge.rank)。
    /// 第 12 关「无尽湍渊」满档为 SSS,SSS 红 / SS 橙突出顶级档;S/A/B/C 沿用原配色。
    /// </summary>
    private static string SlashRankText(string? rank)
        => string.IsNullOrWhiteSpace(rank) ? "?" : rank;

    /// <summary>
    /// 海墟评级徽章底色(公开供单测):SSS 红 / SS 橙 / S 黄 / A 蓝 / B 绿 / C 与未知灰。
    /// 第 12 关「无尽湍渊」满档为 SSS,红色突出顶级档。
    /// </summary>
    public static string SlashRankColor(string? rank) => (rank ?? "").ToUpperInvariant() switch
    {
        "SSS" => "#e33737",   // 满档 SSS:红(参照角色页声骸 ACE 红档)
        "SS" => "#ff9800",    // 次满档 SS:橙
        "S" => "#f8f05c",
        "A" => "#2196f3",
        "B" => "#4caf50",
        "C" => "#9e9e9e",
        _ => "#9e9e9e",
    };
}
