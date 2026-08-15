using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using donet.Core.Models.Gacha;
using donet.Core.Services.Gacha;
using donet.Services;

namespace donet.ViewModels;

/// <summary>抽卡分析页:从本地日志同步记录、展示卡池统计与五星记录。</summary>
public sealed partial class GachaViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _statusText = "就绪";

    [ObservableProperty]
    private string _playerIdText = "-";

    [ObservableProperty]
    private int _totalPulls;

    [ObservableProperty]
    private int _totalFiveStars;

    [ObservableProperty]
    private double _score;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private PoolStats? _selectedPool;

    public ObservableCollection<PoolStats> Pools { get; } = [];

    public ObservableCollection<FiveStarEntry> FiveStarEntries { get; } = [];

    public ObservableCollection<GachaRecord> AllRecords { get; } = [];

    private GachaAnalysisResult? _analysis;

    public GachaViewModel()
    {
        LoadExisting();
    }

    private void LoadExisting()
    {
        try
        {
            var playerIds = AppServices.GachaStore.GetAllPlayerIds();
            if (playerIds.Count == 0)
            {
                return;
            }

            var playerId = playerIds[^1];
            var records = AppServices.GachaStore.GetRecords(playerId);
            if (records.Count == 0)
            {
                return;
            }

            ApplyAnalysis(new GachaAnalysisService().Analyze(playerId, records));
            StatusText = $"已加载本地记录 (玩家 {playerId})";
        }
        catch (Exception ex)
        {
            StatusText = $"加载本地记录失败: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusText = "正在从游戏日志同步抽卡记录…";
        try
        {
            var result = await AppServices.GachaSync.SyncFromLocalLogAsync(AppServices.UpPools);
            if (!result.IsSuccess)
            {
                StatusText = result.Message ?? "同步失败";
                return;
            }

            PlayerIdText = result.Request?.PlayerId ?? "-";
            if (result.Analysis is not null)
            {
                ApplyAnalysis(result.Analysis);
            }

            StatusText = $"同步完成:新增 {result.NewRecords} 条,共 {result.TotalRecords} 条";
        }
        catch (Exception ex)
        {
            StatusText = $"同步失败: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyAnalysis(GachaAnalysisResult analysis)
    {
        _analysis = analysis;
        TotalPulls = analysis.TotalPulls;
        TotalFiveStars = analysis.TotalFiveStars;
        Score = Math.Round(analysis.Score, 1);
        PlayerIdText = analysis.PlayerId;

        Pools.Clear();
        foreach (var pool in analysis.Pools.OrderByDescending(p => p.TotalPulls))
        {
            Pools.Add(pool);
        }

        SelectedPool = Pools.FirstOrDefault(p => p.FiveStarCount > 0) ?? Pools.FirstOrDefault();
        RefreshDetail();
    }

    partial void OnSelectedPoolChanged(PoolStats? value) => RefreshDetail();

    private void RefreshDetail()
    {
        FiveStarEntries.Clear();
        AllRecords.Clear();

        if (_analysis is null || SelectedPool is null)
        {
            return;
        }

        foreach (var entry in SelectedPool.FiveStarEntries)
        {
            FiveStarEntries.Add(entry);
        }

        var all = AppServices.GachaStore.GetRecords(_analysis.PlayerId, SelectedPool.PoolType);
        foreach (var record in all.AsEnumerable().Reverse())
        {
            AllRecords.Add(record);
        }
    }
}
