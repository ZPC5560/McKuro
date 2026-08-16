using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using McKuro.Core.Models.Tower;
using McKuro.Services;

namespace McKuro.ViewModels;

/// <summary>深塔模式展示项。</summary>
public sealed class TowerModeItem
{
    public required string ModeName { get; init; }   // 稳态/奇点
    public required string ScoreText { get; init; }
    public required string RankText { get; init; }
    public required string RankColor { get; init; }
    public required List<NewTowerRole> Roles { get; init; }
}

/// <summary>海墟关卡展示项。</summary>
public sealed class SlashChallengeItem
{
    public required string ChallengeName { get; init; }
    public required string ScoreText { get; init; }
    public required string RankText { get; init; }
    public required string RankColor { get; init; }
    public required List<NewTowerRole> Roles { get; init; }
}

/// <summary>
/// 深塔/海墟页:拉取并展示终焉矩阵与再生海域数据。
/// </summary>
public sealed partial class TowerViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _statusText = "未加载";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _hasData;

    public ObservableCollection<TowerModeItem> TowerModes { get; } = [];

    public ObservableCollection<SlashChallengeItem> SlashChallenges { get; } = [];

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
            var (tower, slash, error) = await AppServices.Tower.GetTowerDataAsync();
            TowerModes.Clear();
            SlashChallenges.Clear();
            if (!string.IsNullOrEmpty(error))
            {
                StatusText = error;
                HasData = false;
                return;
            }

            if (tower is { ModeDetails: not null })
            {
                // 只展示有记录且积分为正的模式(对齐 WutheringWavesTool 落库过滤 hasRecord && score>0)
                foreach (var m in tower.ModeDetails.Where(x => x.HasRecord && x.Score > 0))
                {
                    TowerModes.Add(new TowerModeItem
                    {
                        ModeName = m.ModeId == 0 ? "稳态" : "奇点",
                        ScoreText = $"{m.Score}",
                        RankText = RankToText(m.Rank),
                        RankColor = RankToColor(m.Rank),
                        Roles = m.Teams?.SelectMany(t => t.RoleList ?? []).ToList() ?? [],
                    });
                }
            }

            if (slash is { DifficultyList: not null })
            {
                // 海墟(对齐 WutheringWavesTool SlashViewModel.updateDate):
                // 1. 过滤 difficulty==0 与 allScore==0
                // 2. 无尽湍渊(difficulty=2) 的关卡插到「再生海域」(difficulty=1) 列表头
                var validDiffs = slash.DifficultyList
                    .Where(d => d.Difficulty is 1 or 2 && d.AllScore > 0)
                    .OrderByDescending(d => d.Difficulty)
                    .ToList();
                foreach (var diff in validDiffs)
                {
                    bool isTurbid = diff.Difficulty == 2;
                    foreach (var c in diff.ChallengeList ?? [])
                    {
                        SlashChallenges.Add(new SlashChallengeItem
                        {
                            ChallengeName = isTurbid
                                ? $"🌪 {c.ChallengeName ?? $"湍渊 {c.ChallengeId}"}"
                                : (c.ChallengeName ?? $"关卡 {c.ChallengeId}"),
                            ScoreText = $"{c.Score}",
                            RankText = SlashRankText(c.Rank),
                            RankColor = SlashRankColor(c.Rank),
                            Roles = c.HalfList?.SelectMany(h => h.RoleList ?? []).ToList() ?? [],
                        });
                    }
                }
            }

            HasData = TowerModes.Count > 0 || SlashChallenges.Count > 0;
            StatusText = $"加载完成(深塔 {TowerModes.Count} 模式 / 海墟 {SlashChallenges.Count} 关)";
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
