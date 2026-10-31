using Avalonia.Collections;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using McKuro.Controls;
using McKuro.Core.Models.Gacha;
using McKuro.Core.Services.Gacha;
using McKuro.Services;

namespace McKuro.ViewModels;

/// <summary>抽卡分析页(Haiyu 风格):左侧五星列表,右侧统计条 + 图表。</summary>
public sealed partial class GachaViewModel : ViewModelBase
{
    /// <summary>副标题状态文案。初始为空:静态占位词「就绪」没有信息量(同启动页的处理)。</summary>
    [ObservableProperty]
    private string _statusText = "";

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
    private string _guaranteeHeader = LanguageService.Format("Gacha.PityHeaderNone");

    /// <summary>"全部账号"聚合选项的显示文本。</summary>
    public static string AllPlayersLabel => LanguageService.Format("Gacha.AllPlayers");

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
    public string CurrentPityRowText => LanguageService.Format("Gacha.CurrentPity", SelectedPool?.CurrentPity ?? 0);

    // ---- 视图切换(0=综合分析[默认] 1=统计卡片 2=表格) ----
    [ObservableProperty]
    private int _selectedViewIndex;

    // ---- 表格视图(分页) ----
    public AvaloniaList<string> TablePoolTypes { get; } = [];

    [ObservableProperty]
    private string _selectedTablePool = "";

    [ObservableProperty]
    private int _tableCurrentPage = 1;

    [ObservableProperty]
    private int _tablePageSize = 10;

    /// <summary>表格每页条数选项。</summary>
    public AvaloniaList<int> PageSizeOptions { get; } = [10, 20, 50, 100];

    [ObservableProperty]
    private int _tableTotalCount;

    /// <summary>表格总条数(本地化文本,替代 XAML StringFormat 中文)。</summary>
    public string TableTotalText => LanguageService.Format("Gacha.TableTotal", TableTotalCount);

    partial void OnTableTotalCountChanged(int value) => OnPropertyChanged(nameof(TableTotalText));

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

    /// <summary>统计图下拉选项(三图合一的卡片内切换)。</summary>
    public AvaloniaList<string> ChartOptions { get; } = [LanguageService.Format("Gacha.ChartPity"), LanguageService.Format("Gacha.ChartOffRate"), LanguageService.Format("Gacha.ChartPoolShare")];

    [ObservableProperty]
    private int _selectedChartIndex;

    /// <summary>当前统计图标题(随下拉切换)。</summary>
    public string ChartTitle => ChartOptions.Count > SelectedChartIndex ? ChartOptions[SelectedChartIndex] : "";

    partial void OnSelectedChartIndexChanged(int value) => OnPropertyChanged(nameof(ChartTitle));

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
        LoadExisting();
        _ = PreloadUpIdsAsync();
        _ = PreloadIconCatalogAsync();
    }

    /// <summary>
    /// 异步刷新图鉴图标目录(缓存过期时才真正联网)。完成后重算分析结果,
    /// 让本次新增的角色/武器图标立即出现在五星列表与表格里(无需重启应用)。
    /// </summary>
    private async Task PreloadIconCatalogAsync()
    {
        try
        {
            await AppServices.GachaIcons.RefreshAsync();
            // 目录更新后重建分析:FiveStarEntry/TabelRecords 的 IconUrl 是派生属性,
            // 需重新生成条目才会用上新目录(分析已发起但尚未完成时同样重跑,旧结果按代次丢弃)
            if (_analysis is not null || _analysisRequested)
            {
                AnalyzeCurrentPlayer();
            }
        }
        catch (Exception)
        {
            // 目录刷新失败不影响抽卡数据展示(静态字典兜底)
        }
    }

    /// <summary>异步预取 UP/歪 判定配置(缓存,失败不影响主流程)。</summary>
    private async Task PreloadUpIdsAsync()
    {
        try
        {
            _upIds = await AppServices.UpPools.GetUpIdsAsync();
            // 预取完成后再分析一次,使 UP/歪 判定生效(分析已发起但尚未完成时同样重跑)
            if (_analysis is not null || _analysisRequested)
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
    partial void OnSelectedPlayerIdChanged(string value)
    {
        if (!_suppressPlayerReanalysis)
        {
            AnalyzeCurrentPlayer();
        }
    }

    /// <summary>分析请求代次:每发起一次分析 / 直接应用一次结果 +1,较早发起的后台分析完成时据此丢弃。</summary>
    private int _analysisGeneration;

    /// <summary>是否已发起过本地分析(UP 配置 / 图鉴目录预取完成时据此决定是否重跑)。</summary>
    private bool _analysisRequested;

    /// <summary>按当前下拉选择重新分析(异步;调用方不等待)。</summary>
    private void AnalyzeCurrentPlayer() => _ = AnalyzeCurrentPlayerAsync();

    /// <summary>
    /// 查库 + 全量分析放到线程池,结果回 UI 线程应用。
    /// <para>此前在 UI 线程同步查 SQLite 并全量分析(1 万+ 记录),切换玩家 / 启动时会卡界面;
    /// 连续触发时只应用最后一次的结果(代次比对)。</para>
    /// </summary>
    private async Task AnalyzeCurrentPlayerAsync()
    {
        var generation = ++_analysisGeneration;
        _analysisRequested = true;
        var selected = SelectedPlayerId;
        var isAll = string.IsNullOrEmpty(selected) || selected == AllPlayersLabel;
        var playerId = isAll ? "" : selected;
        var upIds = _upIds;
        try
        {
            var analysis = await Task.Run(() =>
            {
                var records = isAll
                    ? AppServices.GachaStore.GetAllRecords()
                    : AppServices.GachaStore.GetRecords(playerId);
                return records.Count == 0
                    ? null
                    : AppServices.GachaAnalysis.Analyze(playerId, records, upIds);
            });
            // 回到 UI 线程;期间又发起了新的分析(换玩家 / 同步完成)则丢弃本次结果
            if (analysis is null || generation != _analysisGeneration)
            {
                return;
            }
            ApplyAnalysis(analysis);
            StatusText = LanguageService.Format("Gacha.LocalLoaded");
        }
        catch (Exception ex)
        {
            if (generation == _analysisGeneration)
            {
                StatusText = LanguageService.Format("Gacha.LocalLoadFailed", ex.Message);
            }
        }
    }

    /// <summary>
    /// 设置下拉选中项但<b>不</b>触发重新分析(结果已由调用方应用)。
    /// <para>清空 / 重填 <see cref="PlayerIds"/> 时绑定可能先把选中项写成 null 并触发一次分析,
    /// 这里推进代次,让那次中间态分析的结果被丢弃。</para>
    /// </summary>
    private void SelectPlayerWithoutReanalysis(string playerId)
    {
        _suppressPlayerReanalysis = true;
        try
        {
            SelectedPlayerId = playerId;
        }
        finally
        {
            _suppressPlayerReanalysis = false;
        }
        _analysisGeneration++;
    }

    private bool _suppressPlayerReanalysis;

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

            // 默认选中最近抽过卡的玩家(GetAllPlayerIds 按最近一条记录时间升序,[^1] 即最近)。
            // 选中项变化会经 OnSelectedPlayerIdChanged 触发一次分析,不再额外调用(此前启动时分析两遍);
            // 只有选中项未变(不会触发回调)时才手动分析。
            var target = playerIds[^1];
            if (SelectedPlayerId == target)
            {
                AnalyzeCurrentPlayer();
            }
            else
            {
                SelectedPlayerId = target;
            }
        }
        catch (Exception ex)
        {
            StatusText = LanguageService.Format("Gacha.LocalLoadFailed", ex.Message);
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
        StatusText = LanguageService.Format("Gacha.Syncing");
        try
        {
            // 先刷新 UP/歪 判定配置:避免整段会话沿用构造时的旧缓存(如新卡池开启后旧数据把当期 UP 误判为歪)
            _upIds = await AppServices.UpPools.GetUpIdsAsync();
            // 顺带刷新图鉴目录:版本更新后点同步即可补上新角色的头像(缓存 24h 内不会重复联网)
            await AppServices.GachaIcons.RefreshAsync();

            // 双通道:优先云鸣潮(库街区)接口;失败或无登录则回退本地日志解密
            GachaSyncResult? result = null;
            if (AppServices.CloudGacha.HasSavedLogin)
            {
                StatusText = LanguageService.Format("Gacha.SyncingCloud");
                var cloud = await AppServices.CloudGacha.SyncFromCloudAsync();
                if (cloud.IsSuccess)
                {
                    result = cloud.Sync;
                    StatusText = LanguageService.Format("Gacha.CloudSynced");
                }
                else
                {
                    // 云鸣潮失败 → 回退本地日志
                    StatusText = LanguageService.Format("Gacha.CloudFallback", cloud.Message);
                    result = await AppServices.GachaSync.SyncFromLocalLogAsync(AppServices.UpPools);
                }
            }
            else
            {
                result = await AppServices.GachaSync.SyncFromLocalLogAsync(AppServices.UpPools);
            }

            var upIds = _upIds;
            if (result is null || !result.IsSuccess)
            {
                // 云鸣潮 + 本地都失败 → 从 sqlite 缓存兜底(校验账号,已有记录则展示)。
                // 失败原因统一由 Core 填进 Message(含 ClientLogReadFailed 等各状态),这里不按状态分支。
                // 查库 + 分析放到线程池,结果回 UI 线程应用
                string? cachedTarget = result?.Request?.PlayerId;
                var (cachedAnalysis, cachePlayerId, cachedIds) = await Task.Run(() =>
                {
                    var records = TryLoadCache(cachedTarget, out var matched);
                    return records is null
                        ? ((GachaAnalysisResult?)null, matched, new List<string>())
                        : (AppServices.GachaAnalysis.Analyze(matched ?? "", records, upIds), matched, AppServices.GachaStore.GetAllPlayerIds());
                });
                if (cachedAnalysis is not null)
                {
                    ApplyAnalysis(cachedAnalysis);
                    PlayerIds.Clear();
                    PlayerIds.Add(AllPlayersLabel);
                    PlayerIds.AddRange(cachedIds);
                    var cacheSelect = cachePlayerId ?? (cachedIds.Count > 0 ? cachedIds[^1] : "");
                    if (cacheSelect == cachePlayerId)
                    {
                        // 结果已应用:只同步下拉,不再重复查库分析(也不让后台分析覆盖下面的状态文案)
                        SelectPlayerWithoutReanalysis(cacheSelect);
                    }
                    else
                    {
                        SelectedPlayerId = cacheSelect;
                    }
                    StatusText = LanguageService.Format("Gacha.SyncFailedCached", result?.Message ?? LanguageService.Format("Gacha.SyncFailed"));
                }
                else
                {
                    StatusText = result?.Message ?? LanguageService.Format("Gacha.SyncFailed");
                }
                return;
            }

            // 云端同步的底层流水线没有 UP 配置参数,这里统一用最新 UP 集合重算,
            // 让统计卡、五星列表和表格共享完全相同的判定结果。查库 + 分析放到线程池。
            var syncedPlayerId = result.Request?.PlayerId is { Length: > 0 } pid ? pid : null;
            var pipelineAnalysis = result.Analysis;
            var (syncedAnalysis, all) = await Task.Run(() =>
            {
                var analysis = syncedPlayerId is not null
                    ? AppServices.GachaAnalysis.Analyze(syncedPlayerId, AppServices.GachaStore.GetRecords(syncedPlayerId), upIds)
                    : pipelineAnalysis;
                return (analysis, AppServices.GachaStore.GetAllPlayerIds());
            });
            if (syncedAnalysis is not null)
            {
                ApplyAnalysis(syncedAnalysis);
            }

            // 刷新玩家下拉(可能新增玩家)
            PlayerIds.Clear();
            PlayerIds.Add(AllPlayersLabel);
            PlayerIds.AddRange(all);
            var select = result.Request?.PlayerId ?? (all.Count > 0 ? all[^1] : "");
            if (syncedPlayerId is not null && select == syncedPlayerId)
            {
                // 同步玩家的结果刚应用过:只同步下拉选中项,不重复查库分析
                SelectPlayerWithoutReanalysis(select);
            }
            else
            {
                SelectedPlayerId = select;
            }

            StatusText = LanguageService.Format("Gacha.Synced", result.NewRecords, result.TotalRecords);
        }
        catch (Exception ex)
        {
            StatusText = LanguageService.Format("Gacha.SyncFailedWith", ex.Message);
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
            // 记录时间是不带时区的服务器本地时间:按墙上时间解析(与 Core GachaAnalysisService.TryParseTime 同口径),
            // 不做 UTC → 本地换算
            if (TryParseRecordTime(r.Time, out var dt))
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

    /// <summary>
    /// 解析记录时间(优先 "yyyy-MM-dd HH:mm:ss",兼容 ISO 8601),按墙上时间、不做时区换算。
    /// 与 Core <c>GachaAnalysisService.TryParseTime</c>(私有,无法复用)保持一致。
    /// </summary>
    private static bool TryParseRecordTime(string? time, out DateTime value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(time))
        {
            return false;
        }
        return DateTime.TryParseExact(time, "yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture,
                   System.Globalization.DateTimeStyles.None, out value)
               || DateTime.TryParse(time, System.Globalization.CultureInfo.InvariantCulture,
                   System.Globalization.DateTimeStyles.None, out value);
    }

    private void ApplyAnalysis(GachaAnalysisResult analysis)
    {
        // 直接应用的结果(同步 / 缓存兜底 / 后台分析)也推进代次:更早发起的后台分析完成时不得覆盖它
        _analysisGeneration++;
        _analysis = analysis;
        TotalPulls = analysis.TotalPulls;
        TotalFiveStars = analysis.TotalFiveStars;
        Score = Math.Round(analysis.Score, 1);
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
                [(LanguageService.Format("Gacha.OnBanner"), Math.Round((1 - rate) * 100, 1)), (LanguageService.Format("Gacha.OffBanner"), Math.Round(rate * 100, 1))],
                [Color.Parse("#52C41A"), Color.Parse("#F53F3F")]));

            // 歪率保留两位小数:取整在模型层完成(见 PoolStats.OffBannerPercent),
            // 直接传 rate * 100 会把 double 全精度拼进文案(如 33.33333333333333%)。
            GuaranteeHeader = pityPool.GuaranteeHeaderText;
        }
        else
        {
            GuaranteeHeader = LanguageService.Format("Gacha.PityHeaderNone");
        }

        // 出货占比(4星/5星)
        var fourStar = Math.Max(0, analysis.TotalPulls - analysis.TotalFiveStars);
        StarRatioSlices.Clear();
        StarRatioSlices.AddRange(PieSliceViewModel.BuildPie(
            [(LanguageService.Format("Gacha.FourStar"), fourStar), (LanguageService.Format("Gacha.FiveStar"), analysis.TotalFiveStars)],
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

        // 此前每次切换卡池都在 UI 线程全量查一次该池记录填充 AllRecords,而没有任何视图绑定它,已移除
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
            TablePageSize = 10;
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

/// <summary>自绘环形图扇区(140 像素坐标系,圆心 70,70;外径 64.4,内孔 39.2;与画布 1:1,不使用 Stretch 缩放)。</summary>
public sealed class PieSliceViewModel
{
    public required string Data { get; init; }
    public required SolidColorBrush Brush { get; init; }
    public required string Name { get; init; }
    public required double Value { get; init; }

    private const double CenterX = 70, CenterY = 70, OuterR = 64.4, InnerR = 39.2;

    /// <summary>生成从 startAngle 到 endAngle(度,顺时针,12 点钟为 0)的环形扇区 Path。</summary>
    public static string BuildSector(double startAngle, double endAngle)
    {
        var sweep = endAngle - startAngle;
        if (sweep >= 360 - 1e-6)
        {
            return BuildFullRing();
        }

        var a1 = (startAngle - 90) * Math.PI / 180;
        var a2 = (endAngle - 90) * Math.PI / 180;
        double x1o = CenterX + OuterR * Math.Cos(a1);
        double y1o = CenterY + OuterR * Math.Sin(a1);
        double x2o = CenterX + OuterR * Math.Cos(a2);
        double y2o = CenterY + OuterR * Math.Sin(a2);
        double x1i = CenterX + InnerR * Math.Cos(a1);
        double y1i = CenterY + InnerR * Math.Sin(a1);
        double x2i = CenterX + InnerR * Math.Cos(a2);
        double y2i = CenterY + InnerR * Math.Sin(a2);
        var large = sweep > 180 ? 1 : 0;
        // 外弧顺时针 → 内弧逆时针回绕,形成中空圆环扇区
        return $"M {x1o:0.###},{y1o:0.###} A {OuterR:0.###},{OuterR:0.###} 0 {large} 1 {x2o:0.###},{y2o:0.###} " +
               $"L {x2i:0.###},{y2i:0.###} A {InnerR:0.###},{InnerR:0.###} 0 {large} 0 {x1i:0.###},{y1i:0.###} Z";
    }

    /// <summary>整圈(唯一扇区占比 100%)时,两个半圆弧拼成完整圆环。</summary>
    private static string BuildFullRing()
    {
        var top = CenterY - OuterR;
        var bottom = CenterY + OuterR;
        var topIn = CenterY - InnerR;
        var bottomIn = CenterY + InnerR;
        return $"M {CenterX},{top:0.###} A {OuterR:0.###},{OuterR:0.###} 0 1 1 {CenterX},{bottom:0.###} " +
               $"A {OuterR:0.###},{OuterR:0.###} 0 1 1 {CenterX},{top:0.###} Z " +
               $"M {CenterX},{topIn:0.###} A {InnerR:0.###},{InnerR:0.###} 0 1 0 {CenterX},{bottomIn:0.###} " +
               $"A {InnerR:0.###},{InnerR:0.###} 0 1 0 {CenterX},{topIn:0.###} Z";
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
            true => LanguageService.Format("Gacha.OffBanner"),
            false => "UP",
            _ => "-",
        };

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>UP/歪 徽章可见性:仅当 IsOffBanner 非空(可判定 UP/歪)时显示;不可判定(常驻等)时不显示占位徽章。</summary>
public sealed class UpFlagVisibleConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly UpFlagVisibleConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is bool;

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>徽章语义色工具:按当前主题变体(亮/暗)查应用资源画刷。</summary>
internal static class BadgeThemeBrush
{
    public static Avalonia.Styling.ThemeVariant CurrentVariant
        => Avalonia.Application.Current?.ActualThemeVariant ?? Avalonia.Styling.ThemeVariant.Default;

    public static object? Get(string key)
        => Avalonia.Application.Current?.TryGetResource(key, CurrentVariant, out var brush) == true ? brush : null;
}

/// <summary>五星 UP/歪 徽章:tint 胶囊底(true→危险红 tint,false→成功绿 tint)。
/// 语义淡底替代旧实色块,判定色不再喧宾夺主。</summary>
public sealed class FiveStarFlagConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly FiveStarFlagConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is true ? BadgeThemeBrush.Get("McKuroDangerTint") : BadgeThemeBrush.Get("McKuroSuccessTint");

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>五星 UP/歪 徽章文字色(随主题取可读深浅:亮色主题深字,暗色主题亮字)。</summary>
public sealed class FiveStarFlagTextBrushConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly FiveStarFlagTextBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is true ? BadgeThemeBrush.Get("McKuroDanger") : BadgeThemeBrush.Get("McKuroSuccess");

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


/// <summary>保底状态徽标 tint 胶囊底:guaranteed→info 蓝,fifty→warning 琥珀,always→success 绿,none→中性灰。</summary>
public sealed class GuaranteeBadgeTintConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly GuaranteeBadgeTintConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value switch
        {
            "guaranteed" => BadgeThemeBrush.Get("McKuroInfoTint"),
            "fifty" => BadgeThemeBrush.Get("McKuroWarningTint"),
            "always" => BadgeThemeBrush.Get("McKuroSuccessTint"),
            _ => BadgeThemeBrush.Get("McKuroCardStroke"),
        };

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>保底状态徽标文字色(随主题取可读深浅)。</summary>
public sealed class GuaranteeBadgeTextBrushConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly GuaranteeBadgeTextBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value switch
        {
            "guaranteed" => BadgeThemeBrush.Get("McKuroInfo"),
            "fifty" => BadgeThemeBrush.Get("McKuroWarning"),
            "always" => BadgeThemeBrush.Get("McKuroSuccess"),
            _ => BadgeThemeBrush.Get("SemiColorText2"),
        };

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>保底进度条实色(guaranteed→蓝,fifty→琥珀,always→绿,none→灰);徽章胶囊请用 Tint/Text 成对转换器。</summary>
public sealed class GuaranteeBadgeBrushConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly GuaranteeBadgeBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value switch
        {
            "guaranteed" => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#4096FF")),
            "fifty" => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#FAAD14")),
            "always" => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#52C41A")),
            _ => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#8C8C8C")),
        };

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

/// <summary>索引等于参数(如 SelectedChartIndex 与 0/1/2 比较,控制面板显隐)。</summary>
public sealed class IndexEqualsConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly IndexEqualsConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is int index && int.TryParse(parameter?.ToString(), out var expected) && index == expected;

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}
