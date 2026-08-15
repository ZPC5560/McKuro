using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using donet.Core.Models.Gacha;
using donet.Core.Services.Gacha;
using donet.Services;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;

namespace donet.ViewModels;

/// <summary>抽卡分析页(Haiyu 风格):左侧五星列表,右侧统计条 + 图表。</summary>
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
    private string _designation = "-";

    [ObservableProperty]
    private int _doubleCount;

    [ObservableProperty]
    private int _crookedTotal;

    [ObservableProperty]
    private double _avgPulls;

    [ObservableProperty]
    private double _actualFiveStarRate;

    [ObservableProperty]
    private int _days;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private PoolStats? _selectedPool;

    [ObservableProperty]
    private double _starAvgValue;

    [ObservableProperty]
    private string _guaranteeHeader = "保底状态: -";

    public ObservableCollection<PoolStats> Pools { get; } = [];

    public ObservableCollection<FiveStarEntry> FiveStarEntries { get; } = [];

    public ObservableCollection<GachaRecord> AllRecords { get; } = [];

    // ---- 图表 ----
    public ObservableCollection<ISeries> GuaranteeChart { get; } = [];

    public ObservableCollection<ISeries> StarRatioChart { get; } = [];

    public ObservableCollection<ISeries> PoolChart { get; } = [];

    public ObservableCollection<ISeries> TimeLineChart { get; } = [];

    public ObservableCollection<string> TimeLineLabels { get; } = [];

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
        Designation = analysis.Designation;
        DoubleCount = analysis.DoubleCount;
        CrookedTotal = analysis.CrookedTotal;
        AvgPulls = analysis.AvgPulls;
        ActualFiveStarRate = analysis.ActualFiveStarRate;
        Days = analysis.Days;

        Pools.Clear();
        foreach (var pool in analysis.Pools.OrderByDescending(p => p.TotalPulls))
        {
            Pools.Add(pool);
        }

        SelectedPool = Pools.FirstOrDefault(p => p.FiveStarCount > 0) ?? Pools.FirstOrDefault();
        RefreshDetail();
        BuildCharts(analysis);
    }

    private void BuildCharts(GachaAnalysisResult analysis)
    {
        // 保底状态(小保底中/歪):默认第一个有小保底机制的池
        var pityPool = analysis.Pools.FirstOrDefault(p => p.HasPityMechanism && p.OffBannerRate.HasValue)
            ?? analysis.Pools.FirstOrDefault(p => p.HasPityMechanism);
        GuaranteeChart.Clear();
        if (pityPool is not null)
        {
            var rate = pityPool.OffBannerRate ?? 0;
            GuaranteeChart.Add(new PieSeries<double>
            {
                Name = "中",
                Values = [Math.Round((1 - rate) * 100, 1)],
                Fill = new LiveChartsCore.SkiaSharpView.Painting.SolidColorPaint(new SkiaSharp.SKColor(82, 196, 26)),
            });
            GuaranteeChart.Add(new PieSeries<double>
            {
                Name = "歪",
                Values = [Math.Round(rate * 100, 1)],
                Fill = new LiveChartsCore.SkiaSharpView.Painting.SolidColorPaint(new SkiaSharp.SKColor(245, 63, 63)),
            });
            GuaranteeHeader = pityPool.HasPityMechanism
                ? $"保底状态: {pityPool.DisplayName} · 歪率 {rate * 100:0.#}%"
                : "保底状态: -";
        }

        // 出货占比(4星/5星)
        var fourStar = analysis.TotalPulls - analysis.TotalFiveStars;
        StarRatioChart.Clear();
        StarRatioChart.Add(new PieSeries<double> { Name = "4星", Values = [fourStar] });
        StarRatioChart.Add(new PieSeries<double> { Name = "5星", Values = [analysis.TotalFiveStars] });

        // 各卡池抽数分布
        PoolChart.Clear();
        foreach (var pool in analysis.Pools.Where(p => p.TotalPulls > 0))
        {
            PoolChart.Add(new PieSeries<double> { Name = pool.DisplayName, Values = [pool.TotalPulls] });
        }

        // 每日抽数折线图
        TimeLineChart.Clear();
        TimeLineLabels.Clear();
        var points = new List<DateTimePoint>();
        foreach (var daily in analysis.DailyPulls)
        {
            points.Add(new DateTimePoint(daily.Date.ToDateTime(TimeOnly.MinValue), daily.Count));
            TimeLineLabels.Add(daily.Date.ToString("MM-dd"));
        }
        if (points.Count > 0)
        {
            TimeLineChart.Add(new LineSeries<DateTimePoint>
            {
                Name = "每日抽数",
                Values = points,
                GeometrySize = 6,
            });
        }
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

        var pool = SelectedPool;
        // 五星列表:从旧到新展示(最新的在最下,补一条"已垫 X 发")
        foreach (var entry in pool.FiveStarEntries.Reverse())
        {
            FiveStarEntries.Add(entry);
        }

        StarAvgValue = pool.FiveStarEntries.Count > 0
            ? Math.Round(pool.FiveStarEntries.Average(e => e.Pity), 1)
            : 0;

        var all = AppServices.GachaStore.GetRecords(_analysis.PlayerId, pool.PoolType);
        foreach (var record in all.AsEnumerable().Reverse())
        {
            AllRecords.Add(record);
        }
    }
}

/// <summary>五星 UP/歪 标记文字:true→歪,false→UP,null→-。</summary>
public sealed class FiveStarFlagTextConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly FiveStarFlagTextConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value switch
        {
            true => "歪",
            false => "UP",
            _ => "-",
        };

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>五星 UP/歪 标记背景:true→红,false→绿,null→灰。</summary>
public sealed class FiveStarFlagConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly FiveStarFlagConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value switch
        {
            true => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#F53F3F")),
            false => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#52C41A")),
            _ => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#8C8C8C")),
        };

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>垫抽进度条颜色:歪→红,UP→绿,null→主色。</summary>
public sealed class PityBarBrushConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly PityBarBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value switch
        {
            true => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#F53F3F")),
            false => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#52C41A")),
            _ => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#1677FF")),
        };

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}
