using Avalonia.Collections;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using McKuro.Core.Models.Gacha;
using McKuro.Core.Services.Gacha;
using McKuro.Services;

namespace McKuro.ViewModels;

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

    /// <summary>"全部账号"聚合选项的显示文本。</summary>
    public const string AllPlayersLabel = "全部账号";

    /// <summary>玩家筛选选项(含"全部账号")。</summary>
    public AvaloniaList<string> PlayerIds { get; } = [];

    [ObservableProperty]
    private string _selectedPlayerId = "";

    // 使用 AvaloniaList(支持 AddRange 批量填充,只触发一次变更通知)
    public AvaloniaList<PoolStats> Pools { get; } = [];

    public AvaloniaList<FiveStarEntry> FiveStarEntries { get; } = [];

    public AvaloniaList<GachaRecord> AllRecords { get; } = [];

    // ---- 自绘图表(AOT 安全,不用 LiveCharts) ----
    public AvaloniaList<PieSliceViewModel> GuaranteeSlices { get; } = [];

    public AvaloniaList<PieSliceViewModel> StarRatioSlices { get; } = [];

    public AvaloniaList<PieSliceViewModel> PoolSlices { get; } = [];

    /// <summary>每日抽数折线图 Path 数据(面积图)。</summary>
    [ObservableProperty]
    private string _timeLinePathData = "";

    public AvaloniaList<string> TimeLineLabels { get; } = [];

    private GachaAnalysisResult? _analysis;

    private static readonly Color[] Palette =
    [
        Color.Parse("#1677FF"), // 蓝
        Color.Parse("#52C41A"), // 绿
        Color.Parse("#F53F3F"), // 红
        Color.Parse("#FAAD14"), // 橙
        Color.Parse("#722ED1"), // 紫
        Color.Parse("#13C2C2"), // 青
        Color.Parse("#EB2F96"), // 粉
        Color.Parse("#8C8C8C"), // 灰
    ];

    public GachaViewModel()
    {
        LoadExisting();
    }

    /// <summary>玩家下拉切换时重新分析(空串 = 全部账号聚合)。</summary>
    partial void OnSelectedPlayerIdChanged(string value) => AnalyzeCurrentPlayer();

    private void AnalyzeCurrentPlayer()
    {
        var selected = SelectedPlayerId;
        List<GachaRecord> records;
        string display;

        if (string.IsNullOrEmpty(selected) || selected == AllPlayersLabel)
        {
            records = AppServices.GachaStore.GetAllRecords();
            display = AllPlayersLabel;
        }
        else
        {
            records = AppServices.GachaStore.GetRecords(selected);
            display = selected;
        }

        if (records.Count == 0)
        {
            return;
        }

        var playerId = selected == AllPlayersLabel ? "" : selected;
        ApplyAnalysis(AppServices.GachaAnalysis.Analyze(playerId, records));
        StatusText = $"已加载本地记录 ({display})";
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

            PlayerIds.Clear();
            PlayerIds.Add(AllPlayersLabel);
            PlayerIds.AddRange(playerIds);

            // 默认选中最后同步的玩家
            SelectedPlayerId = playerIds[^1];
            AnalyzeCurrentPlayer();
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

            // 刷新玩家下拉(可能新增玩家)
            var all = AppServices.GachaStore.GetAllPlayerIds();
            PlayerIds.Clear();
            PlayerIds.Add(AllPlayersLabel);
            PlayerIds.AddRange(all);
            SelectedPlayerId = result.Request?.PlayerId ?? (all.Count > 0 ? all[^1] : "");

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
        Pools.AddRange(analysis.Pools.OrderByDescending(p => p.TotalPulls));

        SelectedPool = Pools.FirstOrDefault(p => p.FiveStarCount > 0) ?? Pools.FirstOrDefault();
        RefreshDetail();
        BuildCharts(analysis);
    }

    private void BuildCharts(GachaAnalysisResult analysis)
    {
        // 保底状态(小保底中/歪)
        var pityPool = analysis.Pools.FirstOrDefault(p => p.HasPityMechanism && p.OffBannerRate.HasValue)
            ?? analysis.Pools.FirstOrDefault(p => p.HasPityMechanism);
        GuaranteeSlices.Clear();
        if (pityPool is not null)
        {
            var rate = pityPool.OffBannerRate ?? 0;
            GuaranteeSlices.AddRange(PieSliceViewModel.BuildPie(
                [("中", Math.Round((1 - rate) * 100, 1)), ("歪", Math.Round(rate * 100, 1))],
                [Color.Parse("#52C41A"), Color.Parse("#F53F3F")]));

            GuaranteeHeader = $"保底状态: {pityPool.DisplayName} · 歪率 {rate * 100:0.#}%";
        }
        else
        {
            GuaranteeHeader = "保底状态: -";
        }

        // 出货占比(4星/5星)
        var fourStar = Math.Max(0, analysis.TotalPulls - analysis.TotalFiveStars);
        StarRatioSlices.Clear();
        StarRatioSlices.AddRange(PieSliceViewModel.BuildPie(
            [("4星", fourStar), ("5星", analysis.TotalFiveStars)],
            [Color.Parse("#1677FF"), Color.Parse("#FAAD14")]));

        // 各卡池抽数分布
        PoolSlices.Clear();
        var poolsWithPulls = analysis.Pools.Where(p => p.TotalPulls > 0)
            .Select(p => (p.DisplayName, (double)p.TotalPulls)).ToList();
        PoolSlices.AddRange(PieSliceViewModel.BuildPie(poolsWithPulls, Palette));

        // 每日抽数折线图
        var counts = analysis.DailyPulls.Select(d => (double)d.Count).ToList();
        TimeLinePathData = counts.Count > 0 ? PieSliceViewModel.BuildAreaPath(counts) : "";
        TimeLineLabels.Clear();
        TimeLineLabels.AddRange(analysis.DailyPulls.Select(d => d.Date.ToString("MM-dd")));
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
        // 五星列表:从旧到新展示(最新的在最下)
        FiveStarEntries.Clear();
        FiveStarEntries.AddRange(pool.FiveStarEntries.Reverse());

        StarAvgValue = pool.FiveStarEntries.Count > 0
            ? Math.Round(pool.FiveStarEntries.Average(e => e.Pity), 1)
            : 0;

        var all = AppServices.GachaStore.GetRecords(_analysis.PlayerId, pool.PoolType);
        AllRecords.Clear();
        AllRecords.AddRange(all.AsEnumerable().Reverse());
    }
}

/// <summary>自绘饼图扇形(ViewBox 0-100 坐标系,圆心 50,50)。</summary>
public sealed class PieSliceViewModel
{
    public required string Data { get; init; }
    public required SolidColorBrush Brush { get; init; }
    public required string Name { get; init; }
    public required double Value { get; init; }

    /// <summary>生成从 startAngle 到 endAngle(度,顺时针,12 点钟为 0)的扇形 Path。</summary>
    public static string BuildSector(double startAngle, double endAngle)
    {
        const double cx = 50, cy = 50, r = 46;
        var a1 = (startAngle - 90) * Math.PI / 180;
        var a2 = (endAngle - 90) * Math.PI / 180;
        double x1 = cx + r * Math.Cos(a1);
        double y1 = cy + r * Math.Sin(a1);
        double x2 = cx + r * Math.Cos(a2);
        double y2 = cy + r * Math.Sin(a2);
        var large = endAngle - startAngle > 180 ? 1 : 0;
        return $"M {cx:0.###},{cy:0.###} L {x1:0.###},{y1:0.###} A {r:0.###},{r:0.###} 0 {large} 1 {x2:0.###},{y2:0.###} Z";
    }

    /// <summary>按值数组生成各片扇形路径(角度占比),返回 (Name, Data, Brush) 列表。</summary>
    public static List<PieSliceViewModel> BuildPie(
        IReadOnlyList<(string Name, double Value)> items,
        IReadOnlyList<Color> colors)
    {
        var total = items.Sum(i => Math.Max(0, i.Value));
        var result = new List<PieSliceViewModel>();
        if (total <= 0)
        {
            return result;
        }

        double angle = 0;
        for (var i = 0; i < items.Count; i++)
        {
            var value = Math.Max(0, items[i].Value);
            if (value <= 0)
            {
                continue;
            }

            var sweep = value / total * 360;
            var data = BuildSector(angle, angle + sweep);
            var color = colors[i % colors.Count];
            result.Add(new PieSliceViewModel
            {
                Name = items[i].Name,
                Value = value,
                Brush = new SolidColorBrush(color),
                Data = data,
            });
            angle += sweep;
        }

        return result;
    }

    /// <summary>生成折线/面积图 Path(0-100 x 0-40 坐标)。</summary>
    public static string BuildAreaPath(IReadOnlyList<double> values, double width = 100, double height = 40)
    {
        if (values.Count == 0)
        {
            return "";
        }

        var max = values.Max();
        if (max <= 0)
        {
            max = 1;
        }

        var step = width / (values.Count - 1);
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < values.Count; i++)
        {
            double x = i * step;
            double y = height - (values[i] / max) * (height * 0.85) - height * 0.05;
            sb.Append(i == 0 ? "M " : " L ").Append(x.ToString("0.###")).Append(',').Append(y.ToString("0.###"));
        }

        sb.Append(" L ").Append(width.ToString("0.###")).Append(',').Append(height.ToString("0.###"));
        sb.Append(" L 0,").Append(height.ToString("0.###")).Append(" Z");
        return sb.ToString();
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
