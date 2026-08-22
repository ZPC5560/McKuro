using Avalonia.Collections;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using McKuro.Controls;
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

    // ---- 云鸣潮登录(抽卡记录接口通道) ----
    [ObservableProperty]
    private bool _isCloudLoggedIn;

    [ObservableProperty]
    private string _cloudAccountText = "未登录";

    [ObservableProperty]
    private string _cloudMobile = "";

    [ObservableProperty]
    private string _cloudCode = "";

    [ObservableProperty]
    private string _cloudStatusText = "";

    [ObservableProperty]
    private int _cloudSmsCountdown;

    [ObservableProperty]
    private bool _cloudSmsSending;

    /// <summary>发送验证码按钮文案(倒计时中显示剩余秒数)。</summary>
    public string CloudSmsButtonText => CloudSmsCountdown > 0 ? $"重新发送 ({CloudSmsCountdown}s)" : "发送验证码";

    /// <summary>发送验证码按钮可用。</summary>
    public bool CanSendCloudSms => !CloudSmsSending && CloudSmsCountdown <= 0;

    partial void OnCloudSmsCountdownChanged(int value)
    {
        OnPropertyChanged(nameof(CloudSmsButtonText));
        OnPropertyChanged(nameof(CanSendCloudSms));
    }

    partial void OnCloudSmsSendingChanged(bool value) => OnPropertyChanged(nameof(CanSendCloudSms));

    /// <summary>玩家筛选选项(含"全部账号")。</summary>
    public AvaloniaList<string> PlayerIds { get; } = [];

    [ObservableProperty]
    private string _selectedPlayerId = "";

    // 使用 AvaloniaList(支持 AddRange 批量填充,只触发一次变更通知)
    public AvaloniaList<PoolStats> Pools { get; } = [];

    public AvaloniaList<FiveStarEntry> FiveStarEntries { get; } = [];

    /// <summary>最近一次五星之后已垫抽数大于 0 时,在五星列表顶部显示"已垫"行。</summary>
    public bool ShowCurrentPityRow => SelectedPool is { CurrentPity: > 0 };

    /// <summary>已垫行进度条值(封顶 80)。</summary>
    public int CurrentPityBarValue => Math.Min(SelectedPool?.CurrentPity ?? 0, 80);

    /// <summary>已垫行文本(出五星——无论是否歪——后刷新为角色行)。</summary>
    public string CurrentPityRowText => $"已垫 {SelectedPool?.CurrentPity ?? 0} 抽";

    public AvaloniaList<GachaRecord> AllRecords { get; } = [];

    // ---- 视图切换(0=综合分析[默认] 1=统计卡片 2=详细分析 3=表格) ----
    [ObservableProperty]
    private int _selectedViewIndex;

    // ---- 表格视图(分页) ----
    public AvaloniaList<string> TablePoolTypes { get; } = [];

    [ObservableProperty]
    private string _selectedTablePool = "";

    [ObservableProperty]
    private int _tableCurrentPage = 1;

    [ObservableProperty]
    private int _tablePageSize = 20;

    /// <summary>表格每页条数选项。</summary>
    public AvaloniaList<int> PageSizeOptions { get; } = [10, 20, 50, 100];

    [ObservableProperty]
    private int _tableTotalCount;

    [ObservableProperty]
    private int _tableTotalPages = 1;

    /// <summary>表格分析条目:与五星列表共用同一套 UP/垫抽解析结果。</summary>
    public AvaloniaList<GachaPullEntry> TableRecords { get; } = [];

    public bool CanTablePrev => TableCurrentPage > 1;
    public bool CanTableNext => TableCurrentPage < TableTotalPages;

    // ---- 自绘图表(AOT 安全,不用 LiveCharts) ----
    public AvaloniaList<PieSliceViewModel> GuaranteeSlices { get; } = [];

    public AvaloniaList<PieSliceViewModel> StarRatioSlices { get; } = [];

    public AvaloniaList<PieSliceViewModel> PoolSlices { get; } = [];

    /// <summary>每日抽数(旧→新,用于平滑面积图;由 TimeLineChart 自绘)。</summary>
    public AvaloniaList<int> TimeLineCounts { get; } = [];

    public AvaloniaList<string> TimeLineLabels { get; } = [];

    /// <summary>每日悬浮提示(日期/卡池/抽数,由 TimeLineChart 渲染)。</summary>
    public AvaloniaList<string> TimeLineTips { get; } = [];

    private GachaAnalysisResult? _analysis;

    /// <summary>UP/歪判定用(异步预取缓存)。</summary>
    private System.Collections.Generic.IReadOnlyDictionary<CardPoolType, System.Collections.Generic.HashSet<int>>? _upIds;

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
        RefreshCloudState();
        LoadExisting();
        _ = PreloadUpIdsAsync();
    }

    /// <summary>异步预取 UP/歪 判定配置(缓存,失败不影响主流程)。</summary>
    private async Task PreloadUpIdsAsync()
    {
        try
        {
            _upIds = await AppServices.UpPools.GetUpIdsAsync();
            // 预取完成后再分析一次,使 UP/歪 判定生效
            if (_analysis is not null)
            {
                AnalyzeCurrentPlayer();
            }
        }
        catch (Exception)
        {
            _upIds = null;
        }
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
        ApplyAnalysis(AppServices.GachaAnalysis.Analyze(playerId, records, _upIds));
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
        StatusText = "正在同步抽卡记录…";
        try
        {
            // 先刷新 UP/歪 判定配置:避免整段会话沿用构造时的旧缓存(如新卡池开启后旧数据把当期 UP 误判为歪)
            _upIds = await AppServices.UpPools.GetUpIdsAsync();

            // 双通道:优先云鸣潮(库街区)接口;失败或无登录则回退本地日志解密
            GachaSyncResult? result = null;
            if (AppServices.CloudGacha.HasSavedLogin)
            {
                StatusText = "正在通过云鸣潮接口同步…";
                var cloud = await AppServices.CloudGacha.SyncFromCloudAsync();
                if (cloud.IsSuccess)
                {
                    result = cloud.Sync;
                    StatusText = "云鸣潮接口同步成功";
                }
                else
                {
                    // 云鸣潮失败 → 回退本地日志
                    StatusText = $"{cloud.Message},回退本地日志…";
                    result = await AppServices.GachaSync.SyncFromLocalLogAsync(AppServices.UpPools);
                }
            }
            else
            {
                result = await AppServices.GachaSync.SyncFromLocalLogAsync(AppServices.UpPools);
            }

            if (result is null || !result.IsSuccess)
            {
                // 云鸣潮 + 本地都失败 → 从 sqlite 缓存兜底(校验账号,已有记录则展示)
                string? cachedTarget = result?.Request?.PlayerId;
                var cachedRecords = TryLoadCache(cachedTarget, out var cachePlayerId);
                if (cachedRecords is not null)
                {
                    PlayerIdText = cachePlayerId ?? "-";
                    ApplyAnalysis(AppServices.GachaAnalysis.Analyze(cachePlayerId ?? "", cachedRecords, _upIds));
                    var cachedIds = AppServices.GachaStore.GetAllPlayerIds();
                    PlayerIds.Clear();
                    PlayerIds.Add(AllPlayersLabel);
                    PlayerIds.AddRange(cachedIds);
                    SelectedPlayerId = cachePlayerId ?? (cachedIds.Count > 0 ? cachedIds[^1] : "");
                    StatusText = $"{result?.Message ?? "同步失败"},已显示本地缓存记录";
                }
                else
                {
                    StatusText = result?.Message ?? "同步失败";
                }
                return;
            }

            PlayerIdText = result.Request?.PlayerId ?? "-";
            if (result.Request?.PlayerId is { Length: > 0 } syncedPlayerId)
            {
                // 云端同步的底层流水线没有 UP 配置参数,这里统一用最新 UP 集合重算,
                // 让统计卡、五星列表和表格共享完全相同的判定结果。
                var syncedRecords = AppServices.GachaStore.GetRecords(syncedPlayerId);
                ApplyAnalysis(AppServices.GachaAnalysis.Analyze(syncedPlayerId, syncedRecords, _upIds));
            }
            else if (result.Analysis is not null)
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

    /// <summary>从 sqlite 缓存加载抽卡记录(校验账号与日期:优先匹配目标 playerId,其次最近同步的玩家;过滤无效日期记录)。</summary>
    private List<GachaRecord>? TryLoadCache(string? targetPlayerId, out string? matchedPlayerId)
    {
        matchedPlayerId = null;
        try
        {
            var now = DateTime.Now;
            // 优先目标账号缓存
            if (!string.IsNullOrWhiteSpace(targetPlayerId))
            {
                var target = FilterValidDates(AppServices.GachaStore.GetRecords(targetPlayerId), now);
                if (target.Count > 0)
                {
                    matchedPlayerId = targetPlayerId;
                    return target;
                }
            }
            // 回退到最近同步的玩家(校验其缓存存在)
            var all = AppServices.GachaStore.GetAllPlayerIds();
            if (all.Count == 0)
            {
                return null;
            }
            var last = all[^1];
            var cached = FilterValidDates(AppServices.GachaStore.GetRecords(last), now);
            if (cached.Count == 0)
            {
                return null;
            }
            matchedPlayerId = last;
            return cached;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>过滤无效日期的缓存记录(剔除无法解析/明显未来的时间)。</summary>
    private static List<GachaRecord> FilterValidDates(List<GachaRecord> records, DateTime now)
    {
        if (records.Count == 0)
        {
            return records;
        }
        var result = new List<GachaRecord>(records.Count);
        foreach (var r in records)
        {
            if (DateTime.TryParse(r.Time, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal, out var dt))
            {
                // 未来时间(时钟误差容忍 1 天)视为无效
                if (dt > now.AddDays(1))
                {
                    continue;
                }
            }
            else if (string.IsNullOrWhiteSpace(r.Time))
            {
                continue;
            }
            result.Add(r);
        }
        return result;
    }

    /// <summary>刷新云鸣潮登录状态。</summary>
    private void RefreshCloudState()
    {
        IsCloudLoggedIn = AppServices.CloudGacha.HasSavedLogin;
        CloudAccountText = IsCloudLoggedIn
            ? (string.IsNullOrWhiteSpace(AppServices.CloudGacha.SavedLoginName) ? "已登录" : AppServices.CloudGacha.SavedLoginName)
            : "未登录";
    }

    /// <summary>发送云鸣潮登录验证码。</summary>
    [RelayCommand]
    private async Task SendCloudSmsAsync()
    {
        if (CloudSmsSending || CloudSmsCountdown > 0)
        {
            return;
        }
        if (string.IsNullOrWhiteSpace(CloudMobile))
        {
            CloudStatusText = "请先填写手机号";
            return;
        }
        CloudSmsSending = true;
        CloudStatusText = "正在发送验证码…";
        try
        {
            var (ok, msg) = await AppServices.CloudGacha.SendSmsAsync(CloudMobile.Trim());
            CloudStatusText = msg ?? (ok ? "验证码已发送" : "发送失败");
            if (ok)
            {
                CloudSmsCountdown = 60;
                _ = RunCloudSmsCountdownAsync();
            }
        }
        catch (Exception ex)
        {
            CloudStatusText = $"发送失败: {ex.Message}";
        }
        finally
        {
            CloudSmsSending = false;
        }
    }

    private async Task RunCloudSmsCountdownAsync()
    {
        while (CloudSmsCountdown > 0)
        {
            await Task.Delay(1000);
            if (CloudSmsCountdown > 0)
            {
                CloudSmsCountdown--;
            }
        }
    }

    /// <summary>云鸣潮手机号登录(登录成功后自动同步)。</summary>
    [RelayCommand]
    private async Task CloudLoginAsync()
    {
        if (string.IsNullOrWhiteSpace(CloudMobile) || string.IsNullOrWhiteSpace(CloudCode))
        {
            CloudStatusText = "请填写手机号与验证码";
            return;
        }
        CloudStatusText = "正在登录…";
        try
        {
            var (ok, msg) = await AppServices.CloudGacha.LoginAsync(CloudMobile.Trim(), CloudCode.Trim());
            if (ok)
            {
                CloudCode = "";
                RefreshCloudState();
                CloudStatusText = "登录成功,点击「同步」拉取抽卡记录";
            }
            else
            {
                CloudStatusText = msg ?? "登录失败";
            }
        }
        catch (Exception ex)
        {
            CloudStatusText = $"登录失败: {ex.Message}";
        }
    }

    /// <summary>退出云鸣潮登录。</summary>
    [RelayCommand]
    private void CloudLogout()
    {
        AppServices.CloudGacha.Logout();
        RefreshCloudState();
        CloudStatusText = "已退出云鸣潮登录";
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
        // 全部 13 个卡池(有记录的 + 无记录的空池),保证下拉框完整
        var filled = analysis.Pools.ToDictionary(p => p.PoolType);
        foreach (var type in McKuro.Core.Models.Gacha.CardPoolTypeValues.All)
        {
            if (filled.ContainsKey(type))
            {
                continue;
            }
            filled[type] = new McKuro.Core.Models.Gacha.PoolStats { PoolType = type };
        }
        Pools.AddRange(filled.Values.OrderByDescending(p => p.TotalPulls));

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

        // 每日抽数平滑面积图(自绘,参照调用趋势图)
        TimeLineCounts.Clear();
        TimeLineCounts.AddRange(analysis.DailyPulls.Select(d => d.Count));
        TimeLineLabels.Clear();
        TimeLineLabels.AddRange(analysis.DailyPulls.Select(d => d.Date.ToString("MM-dd")));
        TimeLineTips.Clear();
        TimeLineTips.AddRange(analysis.DailyPulls.Select(TimeLineChart.BuildTip));
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

        OnPropertyChanged(nameof(ShowCurrentPityRow));
        OnPropertyChanged(nameof(CurrentPityRowText));
        OnPropertyChanged(nameof(CurrentPityBarValue));

        StarAvgValue = pool.FiveStarEntries.Count > 0
            ? Math.Round(pool.FiveStarEntries.Average(e => e.Pity), 1)
            : 0;

        var all = AppServices.GachaStore.GetRecords(_analysis.PlayerId, pool.PoolType);
        AllRecords.Clear();
        AllRecords.AddRange(all.AsEnumerable().Reverse());

        RefreshTable();
    }

    // ==================== 表格视图(参考 Java CardTableShowView) ====================

    /// <summary>刷新表格视图:按选中卡池筛选 + 分页。</summary>
    private void RefreshTable()
    {
        if (_analysis is null)
        {
            return;
        }
        // 收集所有池名(有记录的)
        TablePoolTypes.Clear();
        foreach (var p in Pools)
        {
            if (p.TotalPulls > 0)
            {
                TablePoolTypes.Add(p.DisplayName);
            }
        }
        if (TablePoolTypes.Count > 0 && !TablePoolTypes.Contains(SelectedTablePool))
        {
            SelectedTablePool = TablePoolTypes[0];
        }
        ApplyTablePoolFilter();
    }

    partial void OnSelectedTablePoolChanged(string value) => ApplyTablePoolFilter();

    partial void OnTablePageSizeChanged(int value)
    {
        if (value <= 0)
        {
            TablePageSize = 20;
            return;
        }
        TableCurrentPage = 1;
        ApplyTablePoolFilter();
    }

    private void ApplyTablePoolFilter()
    {
        if (_analysis is null || string.IsNullOrEmpty(SelectedTablePool))
        {
            TableRecords.Clear();
            TableTotalCount = 0;
            TableTotalPages = 1;
            return;
        }
        var poolType = TablePoolTypeOf(SelectedTablePool);
        IEnumerable<GachaPullEntry> all;
        if (poolType is null)
        {
            all = _analysis.Pools
                .SelectMany(p => p.PullEntries)
                .OrderByDescending(e => e.Record.Time)
                .ThenByDescending(e => e.Index);
        }
        else
        {
            var pool = _analysis.Pools.FirstOrDefault(p => p.PoolType == poolType.Value);
            all = pool?.PullEntries.AsEnumerable().Reverse() ?? [];
        }
        var allList = all.ToList();
        TableTotalCount = allList.Count;
        TableTotalPages = Math.Max(1, (int)Math.Ceiling(TableTotalCount / (double)TablePageSize));
        if (TableCurrentPage > TableTotalPages)
        {
            TableCurrentPage = TableTotalPages;
        }
        TableRecords.Clear();
        var page = allList
            .Skip((TableCurrentPage - 1) * TablePageSize)
            .Take(TablePageSize)
            .ToList();
        TableRecords.AddRange(page);
        OnPropertyChanged(nameof(CanTablePrev));
        OnPropertyChanged(nameof(CanTableNext));
    }

    private CardPoolType? TablePoolTypeOf(string displayName)
    {
        foreach (var type in McKuro.Core.Models.Gacha.CardPoolTypeValues.All)
        {
            if (McKuro.Core.Models.Gacha.CardPoolTypeValues.GetDisplayName(type) == displayName)
            {
                return type;
            }
        }
        return null;
    }

    [RelayCommand]
    private void TableFirst() { TableCurrentPage = 1; ApplyTablePoolFilter(); }

    [RelayCommand]
    private void TablePrev() { if (TableCurrentPage > 1) { TableCurrentPage--; ApplyTablePoolFilter(); } }

    [RelayCommand]
    private void TableNext() { if (TableCurrentPage < TableTotalPages) { TableCurrentPage++; ApplyTablePoolFilter(); } }

    [RelayCommand]
    private void TableLast() { TableCurrentPage = TableTotalPages; ApplyTablePoolFilter(); }

    partial void OnTableCurrentPageChanged(int value)
    {
        OnPropertyChanged(nameof(CanTablePrev));
        OnPropertyChanged(nameof(CanTableNext));
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

/// <summary>五星图标占位底色(无图标时区分 UP/歪):UP→浅绿,歪→浅红,null→蓝。</summary>
public sealed class FiveStarFlagBgConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly FiveStarFlagBgConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value switch
        {
            true => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#5A0A0A")),
            false => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#0A3D1A")),
            _ => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#123A5A")),
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

/// <summary>按垫抽数量(Pity,0-80)分级着色:越接近保底颜色越红,参考保底进度逻辑。</summary>
public sealed class PityColorConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly PityColorConverter Instance = new();

    // 抽数分级:0-40 蓝,40-55 青,55-65 橙,65-75 深橙,75-80 红(接近保底)
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        double pity = value is double d ? d : value is int i ? i : 0;
        string hex = pity switch
        {
            >= 75 => "#F53F3F",  // 红(接近保底)
            >= 65 => "#FA8C16",  // 深橙
            >= 55 => "#FAAD14",  // 橙
            >= 40 => "#13C2C2",  // 青
            _ => "#1677FF",      // 蓝
        };
        return new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(hex));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}


/// <summary>星级颜色:5→金,4→紫,其他→灰。</summary>
public sealed class QualityColorConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly QualityColorConverter Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value switch
        {
            int q when q >= 5 => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#D4A017")),
            int q when q == 4 => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#A855F7")),
            _ => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#8C8C8C")),
        };
    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>取字符串首字符(占位图标用)。</summary>
public sealed class FirstCharConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly FirstCharConverter Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is string { Length: > 0 } s ? s[..1] : "?";
    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}
