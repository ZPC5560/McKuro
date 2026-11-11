using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using McKuro.Core.Services.Game;
using McKuro.Services;

namespace McKuro.ViewModels;

/// <summary>
/// 「游戏资源」弹窗的一项分类(环形图的一环 + 图例的一行)。
/// <para>
/// 环序与图例顺序必须<b>一致且稳定</b>:用户是靠颜色对应图例的,
/// 重新扫描后若某项因为体积变成 0 而消失,后面的环就会整体上移一格,
/// 颜色含义全部错位。因此<b>五环恒定存在</b>,体积 0 的那环照样占位(画出来只是一圈轨道)。
/// </para>
/// </summary>
public sealed partial class GameResourceLegendItem : ObservableObject
{
    /// <summary>分类(内部用,决定能否点「打开目录」)。</summary>
    public required GameResourceCategory Category { get; init; }

    /// <summary>显示名(正式资源 / 预下载 / 日志 / 截图 / 启动器临时文件)。</summary>
    public required string Title { get; init; }

    /// <summary>一行说明(这一项是什么、能不能删)。</summary>
    public required string Description { get; init; }

    /// <summary>体积文案(如「98.31 GB」)。</summary>
    public required string SizeText { get; init; }

    /// <summary>体积字节数(排序、占比计算用)。</summary>
    public long Bytes { get; init; }

    /// <summary>占游戏总量的文案(如「94.2%」);总量为 0 时为空。</summary>
    public string ShareText { get; init; } = "";

    /// <summary>比例 0-1(喂给环形图)。</summary>
    public double Ratio { get; init; }

    /// <summary>该环的颜色资源 key(与 <see cref="Views.GameResourceWindow"/> 里的画刷一致)。</summary>
    public required string BrushKey { get; init; }

    /// <summary>该项的目录路径(能定位到才算「可打开」)。</summary>
    public string? Path { get; init; }

    /// <summary>是否提供「打开目录」(路径存在时)。</summary>
    public bool CanOpen => !string.IsNullOrEmpty(Path) && System.IO.Directory.Exists(Path);

    /// <summary>体积为 0 时整行淡显(不隐藏,理由见类型注释)。</summary>
    public bool IsEmpty => Bytes <= 0;
}

/// <summary>设备信息的一行(label + value)。</summary>
public sealed class DeviceInfoRow
{
    public required string Label { get; init; }
    public required string Value { get; init; }

    /// <summary>值为空的行整行隐藏(例如非 AMD 显卡读不到 "Adrenalin 版本")。</summary>
    public bool HasValue => !string.IsNullOrWhiteSpace(Value);
}

/// <summary>
/// 「游戏资源」弹窗的 ViewModel。
/// <para>
/// 两个页签:<b>资源占用</b>(同心圆 + 分类图例)与<b>设备信息</b>(对齐官方启动器新版「游戏设置 → 设备信息」)。
/// 数据来自 Core 的两个纯函数服务:<see cref="GameResourceScanner"/> 与
/// <c>McKuro.Core.Services.Hardware.DeviceInfoService</c>。
/// </para>
/// <para>
/// 扫描放在线程池(游戏目录动辄十几万文件);<b>先出环、后补设备信息</b>——
/// 设备信息要读注册表,通常几十毫秒就好,但没必要让用户等它才能看到体积。
/// </para>
/// </summary>
public sealed partial class GameResourceViewModel : ViewModelBase
{
    private readonly string? _gameRootDir;

    /// <summary>扫描/采集的代际:连点「重新扫描」时旧任务的结果一律丢弃。</summary>
    private int _generation;

    public GameResourceViewModel(string? gameRootDir)
    {
        _gameRootDir = gameRootDir;
        BuildSkeletonLegend();
    }

    /// <summary>标题副文案:游戏目录路径(未设置时为提示语)。</summary>
    public string GameRootDisplay => string.IsNullOrEmpty(_gameRootDir)
        ? LanguageService.Format("Launcher.Resource.NoGameDir")
        : _gameRootDir;

    public bool HasGameDir => !string.IsNullOrEmpty(_gameRootDir);

    // ---------- 页签 ----------

    /// <summary>当前是否停在「资源占用」页(默认页:用户点进来的目的就是看体积)。</summary>
    [ObservableProperty]
    private bool _isStorageTabActive = true;

    /// <summary>当前是否停在「设备信息」页。</summary>
    [ObservableProperty]
    private bool _isDeviceTabActive;

    // ---------- 资源占用 ----------

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private string _scanStatusText = "";

    /// <summary>扫描是否真的跑过(没跑过时不画环,显示空态)。</summary>
    [ObservableProperty]
    private bool _isLoaded;

    /// <summary>游戏总量文案。</summary>
    [ObservableProperty]
    private string _totalSizeText = "-";

    /// <summary>总量下方的副文案(文件数 / 占整盘比例)。</summary>
    [ObservableProperty]
    private string _totalSizeSubText = "";

    /// <summary>磁盘占用行:「占整块硬盘 8.7% · 可用 123.4 GB / 500 GB」。</summary>
    [ObservableProperty]
    private string _diskUsageText = "";

    /// <summary>占整盘比例的 0-100(给进度条/文本用)。</summary>
    [ObservableProperty]
    private double _diskUsagePercent;

    /// <summary>是否读到了磁盘容量(读不到时隐藏整块磁盘信息)。</summary>
    [ObservableProperty]
    private bool _hasDiskInfo;

    /// <summary>预下载明细文案(多版本时逐行「3.8.0 · 12.08 GB」)。</summary>
    [ObservableProperty]
    private string _predownloadDetailText = "";

    [ObservableProperty]
    private bool _hasPredownloadDetail;

    /// <summary>图例(固定五项,见 <see cref="GameResourceLegendItem"/> 注释)。</summary>
    public ObservableCollection<GameResourceLegendItem> Legend { get; } = [];

    /// <summary>环形图比例(外→内,固定五项)。</summary>
    [ObservableProperty]
    private IReadOnlyList<double> _ringRatios = [0, 0, 0, 0, 0];

    /// <summary>环形图中心的大字(游戏总量)。</summary>
    [ObservableProperty]
    private string _ringCenterValue = "-";

    /// <summary>环形图中心的小字(「游戏总占用」)。</summary>
    [ObservableProperty]
    private string _ringCenterLabel = "";

    // ---------- 设备信息 ----------

    [ObservableProperty]
    private bool _isDeviceLoading = true;

    [ObservableProperty]
    private string _deviceUnsupportedText = "";

    /// <summary>设备信息行(采集完成后填充)。</summary>
    public ObservableCollection<DeviceInfoRow> DeviceRows { get; } = [];

    /// <summary>「一键复制设备信息」的按钮文案(按下后短暂变成「已复制」由窗口处理)。</summary>
    [ObservableProperty]
    private string _copyDeviceInfoText = "";

    // ---------- 加载 ----------

    /// <summary>
    /// 扫描资源占用 + 采集设备信息。
    /// </summary>
    /// <param name="ct">取消令牌(窗口关闭时取消)。</param>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        var generation = Interlocked.Increment(ref _generation);

        CopyDeviceInfoText = LanguageService.Format("Launcher.Resource.DeviceCopy");
        RingCenterLabel = LanguageService.Format("Launcher.Resource.TotalLabel");
        IsDeviceLoading = true;

        // 设备信息与体积扫描互不依赖,并行跑:体积要枚举几十万文件,设备只要读注册表。
        // 设备侧全程在线程池里"算完再给我一个结果对象",不在后台线程写 ObservableCollection ——
        // Avalonia 的集合绑定不保证跨线程安全,从后台线程 Add 会在随机时刻抛异常。
        var deviceTask = Task.Run(() => CollectDeviceRows(ct), ct);
        var usageTask = ScanUsageAsync(generation, ct);

        await usageTask.ConfigureAwait(true);

        try
        {
            var (rows, unsupported, failed) = await deviceTask.ConfigureAwait(true);
            if (Volatile.Read(ref _generation) == generation)
            {
                ApplyDeviceRows(rows, unsupported, failed);
            }
        }
        catch (OperationCanceledException)
        {
            // 窗口关了:什么都不用做
        }
        finally
        {
            if (Volatile.Read(ref _generation) == generation)
            {
                IsDeviceLoading = false;
            }
        }
    }

    private async Task ScanUsageAsync(int generation, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_gameRootDir))
        {
            IsScanning = false;
            ScanStatusText = LanguageService.Format("Launcher.Resource.NoGameDir");
            RingCenterValue = "-";
            // 没目录也别让图例停在骨架态:显示五项 0 体积,告诉用户"这里本来会有什么"
            ApplyUsage(GameResourceUsage.Empty, generation);
            return;
        }

        IsScanning = true;
        ScanStatusText = LanguageService.Format("Launcher.Resource.Scanning");
        try
        {
            // 进度回调要回到 UI 线程写属性(Progress<T> 会自动 marshal 回创建它的上下文)
            var progress = new Progress<GameResourceScanProgress>(p =>
            {
                if (Volatile.Read(ref _generation) != generation)
                {
                    return;
                }

                ScanStatusText = p.ScannedFiles > 0
                    ? LanguageService.Format("Launcher.Resource.ScanningCount", p.ScannedFiles)
                    : LanguageService.Format("Launcher.Resource.Scanning");
            });

            var usage = await Task.Run(
                () => GameResourceScanner.Scan(_gameRootDir, ct, progress),
                ct).ConfigureAwait(true);

            if (Volatile.Read(ref _generation) != generation || ct.IsCancellationRequested)
            {
                return;
            }

            ApplyUsage(usage, generation);
            IsLoaded = true;
            ScanStatusText = "";
        }
        catch (OperationCanceledException)
        {
            // 窗口关了:什么都不用做
        }
        catch (Exception ex)
        {
            if (Volatile.Read(ref _generation) == generation)
            {
                ScanStatusText = LanguageService.Format("Launcher.Resource.ScanFailed", ex.Message);
            }
        }
        finally
        {
            if (Volatile.Read(ref _generation) == generation)
            {
                IsScanning = false;
            }
        }
    }

    private void ApplyUsage(GameResourceUsage usage, int generation)
    {
        if (Volatile.Read(ref _generation) != generation)
        {
            return;
        }

        TotalSizeText = FormatBytes(usage.TotalBytes);
        RingCenterValue = TotalSizeText;

        TotalSizeSubText = usage.FileCount > 0
            ? LanguageService.Format("Launcher.Resource.FileCount", usage.FileCount)
            : "";

        HasDiskInfo = usage.HasDiskInfo;
        if (usage.HasDiskInfo)
        {
            var usedRatio = usage.DiskRatio;
            DiskUsagePercent = usedRatio * 100;
            DiskUsageText = LanguageService.Format(
                "Launcher.Resource.DiskLine",
                usedRatio * 100,
                FormatBytes(usage.DiskFreeBytes),
                FormatBytes(usage.DiskTotalBytes));
        }
        else
        {
            DiskUsagePercent = 0;
            DiskUsageText = "";
        }

        // 预下载明细:有多个版本暂存时逐行列出,让用户知道"到底占在哪一版"
        if (usage.PredownloadDirs.Count > 0)
        {
            HasPredownloadDetail = true;
            PredownloadDetailText = string.Join(
                " · ",
                usage.PredownloadDirs.Select(d =>
                    LanguageService.Format("Launcher.Resource.PredownloadItem", d.Name, FormatBytes(d.Bytes))));
        }
        else
        {
            HasPredownloadDetail = false;
            PredownloadDetailText = "";
        }

        RebuildLegend(usage);
        RingRatios = [.. Legend.Select(i => i.Ratio)];
    }

    /// <summary>
    /// 用扫描结果重建图例。**顺序恒定**(正式资源→预下载→日志→截图→启动器临时文件),
    /// 理由见 <see cref="GameResourceLegendItem"/> 的类型注释。
    /// </summary>
    private void RebuildLegend(GameResourceUsage usage)
    {
        var total = usage.TotalBytes;

        var seed = new (GameResourceCategory Category, string TitleKey, string DescKey, string BrushKey, long Bytes, string? Path)[]
        {
            (GameResourceCategory.Official, "Launcher.Resource.Official", "Launcher.Resource.OfficialDesc",
                "McKuroRingOfficial", usage.OfficialBytes, _gameRootDir),
            (GameResourceCategory.Predownload, "Launcher.Resource.Predownload", "Launcher.Resource.PredownloadDesc",
                "McKuroRingPredownload", usage.PredownloadBytes,
                string.IsNullOrEmpty(_gameRootDir) ? null : Path.Combine(_gameRootDir, GamePathResolver.DiffDataDirName)),
            (GameResourceCategory.Logs, "Launcher.Resource.Logs", "Launcher.Resource.LogsDesc",
                "McKuroRingLogs", usage.LogBytes,
                string.IsNullOrEmpty(_gameRootDir) ? null : Path.Combine(_gameRootDir, GameResourceScanner.LogRelativePath)),
            (GameResourceCategory.Screenshots, "Launcher.Resource.Screenshots", "Launcher.Resource.ScreenshotsDesc",
                "McKuroRingShots", usage.ScreenshotBytes,
                string.IsNullOrEmpty(_gameRootDir) ? null : Path.Combine(_gameRootDir, GameResourceScanner.ScreenshotRelativePath)),
            (GameResourceCategory.LauncherTemp, "Launcher.Resource.Temp", "Launcher.Resource.TempDesc",
                "McKuroRingTemp", usage.LauncherTempBytes, _gameRootDir),
        };

        Legend.Clear();
        foreach (var item in seed)
        {
            var ratio = GameResourceUsage.Ratio(item.Bytes, total);
            Legend.Add(new GameResourceLegendItem
            {
                Category = item.Category,
                Title = LanguageService.Format(item.TitleKey),
                Description = LanguageService.Format(item.DescKey),
                SizeText = FormatBytes(item.Bytes),
                Bytes = item.Bytes,
                Ratio = ratio,
                // 空分类不显示 "0%":体积行已经写了「0 B」+「暂无」标,再补一个 0% 只是噪声
                ShareText = item.Bytes > 0 && total > 0 ? $"{ratio * 100:0.#}%" : "",
                BrushKey = item.BrushKey,
                Path = item.Path,
            });
        }
    }

    /// <summary>构造占位的五项图例(扫描未开始/无目录时也要有稳定顺序)。</summary>
    private void BuildSkeletonLegend() => RebuildLegend(GameResourceUsage.Empty);

    // ---------- 设备信息 ----------

    /// <summary>
    /// 在线程池里采集设备信息,返回"数据 + 是否不支持 + 是否失败"三件套。
    /// <para>
    /// 不在后台线程直接写 <see cref="DeviceRows"/>:跨线程改绑定集合在 Avalonia 里是未定义行为,
    /// 而且这里本来就需要让 UI 线程一次性替换(避免逐行 Add 触发多次布局)。
    /// </para>
    /// </summary>
    private (List<(string LabelKey, string Value)> Rows, bool Unsupported, bool Failed) CollectDeviceRows(
        CancellationToken ct)
    {
        var rows = new List<(string LabelKey, string Value)>();
        try
        {
            var info = Core.Services.Hardware.DeviceInfoService.Collect(_gameRootDir);
            ct.ThrowIfCancellationRequested();

            if (!info.IsSupported)
            {
                return (rows, true, false);
            }

            // 后缀标"线程"而不是"核":Environment.ProcessorCount 给的是逻辑处理器数
            // (9700X 是 8 核 16 线程,写成"16 核"是错的,用户一眼就会觉得这软件不靠谱)
            Add("Launcher.Resource.DeviceCpu", info.CpuCores > 0
                ? $"{info.CpuName} · {info.CpuCores:0} {LanguageService.Format("Launcher.Resource.DeviceThreads")}"
                : info.CpuName);
            Add("Launcher.Resource.DeviceMemory",
                info.MemoryBytes > 0 ? FormatCapacity(info.MemoryBytes) : "");
            Add("Launcher.Resource.DeviceGpu", info.GpuName);
            Add("Launcher.Resource.DeviceVram",
                info.GpuMemoryBytes > 0 ? FormatCapacity(info.GpuMemoryBytes) : "");
            Add("Launcher.Resource.DeviceDriver", info.GpuDriverVersion);
            // 驱动套装名只在能拿到时才有(非 AMD 通常为空,不留一行空标签)
            Add("Launcher.Resource.DeviceDriverEdition", info.GpuDriverEdition);
            Add("Launcher.Resource.DeviceDiskKind", info.InstallDiskKind switch
            {
                Core.Services.Hardware.DiskKind.Ssd => LanguageService.Format("Launcher.Resource.DeviceDisk.Ssd"),
                Core.Services.Hardware.DiskKind.Hdd => LanguageService.Format("Launcher.Resource.DeviceDisk.Hdd"),
                _ => "",
            });
            Add("Launcher.Resource.DeviceOs", info.OsDisplayName.Length > 0
                ? $"{info.OsDisplayName} {info.OsVersion}".Trim()
                : info.OsVersion);

            return (rows, false, false);
        }
        catch (OperationCanceledException)
        {
            return (rows, false, false);
        }
        catch (Exception)
        {
            // 设备信息是"锦上添花",读不到不该让整个弹窗报错
            return (rows, false, true);
        }

        void Add(string labelKey, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                rows.Add((labelKey, value.Trim()));
            }
        }
    }

    private void ApplyDeviceRows(
        List<(string LabelKey, string Value)> rows,
        bool unsupported,
        bool failed)
    {
        DeviceRows.Clear();

        if (unsupported || failed)
        {
            DeviceUnsupportedText = LanguageService.Format("Launcher.Resource.DeviceUnsupported");
            return;
        }

        DeviceUnsupportedText = "";
        foreach (var (labelKey, value) in rows)
        {
            DeviceRows.Add(new DeviceInfoRow
            {
                Label = LanguageService.Format(labelKey),
                Value = value,
            });
        }
    }

    /// <summary>设备信息复制到剪贴板的纯文本(一行一项,便于贴进反馈贴)。</summary>
    public string BuildDeviceInfoPlainText()
    {
        var lines = new List<string>();
        foreach (var row in DeviceRows)
        {
            lines.Add($"{row.Label}: {row.Value}");
        }
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// 容量规格格式化:按 1024 进制取到 GB 后<b>四舍五入成整数</b>。
    /// <para>
    /// 专供内存/显存这类"硬件规格"读数,不用于体积统计。<b>为什么不用 <see cref="FormatBytes"/>
    /// 的两位小数</b>:GlobalMemoryStatusEx 报的是扣掉硬件保留后的可用物理内存(本机 47.61 GiB),
    /// 而用户认知里这台机器就是 48G;显存 15.98 GiB 同理。显示 47.61 会让人以为读错了,
    /// 反而要花时间去解释。体积统计(资源占用)必须保留精度,两者口径不同,所以分成两个函数。
    /// </para>
    /// </summary>
    private static string FormatCapacity(long bytes)
    {
        if (bytes <= 0)
        {
            return "";
        }

        const double gb = 1024d * 1024 * 1024;
        if (bytes >= gb)
        {
            return $"{Math.Round(bytes / gb):0} GB";
        }

        return FormatBytes(bytes);
    }

    /// <summary>字节格式化(B/GB 与全站口径一致:1024 进制、最多两位小数)。</summary>
    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return $"{size:0.##} {units[unit]}";
    }
}

/// <summary>
/// 把 App.axaml 里的资源 key(如 <c>McKuroRingOfficial</c>)解析成当前主题下的画刷。
/// <para>
/// 为什么不直接在 XAML 里写 <c>DynamicResource</c>:图例圆点的颜色是<b>数据</b>
/// (由 VM 按分类给出 key),不是写死的某个资源名 —— 用转换器就不用在 ItemTemplate 里
/// 给五个分类各写一遍圆点,加一类只要改 seed 表。
/// </para>
/// </summary>
public sealed class ThemeBrushConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly ThemeBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is not string key || key.Length == 0)
        {
            return null;
        }

        var app = Avalonia.Application.Current;
        if (app is null)
        {
            return null;
        }

        var variant = app.ActualThemeVariant is { } v && v != Avalonia.Styling.ThemeVariant.Default
            ? v
            : Avalonia.Styling.ThemeVariant.Dark;

        return app.TryGetResource(key, variant, out var brush) ? brush : null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}
