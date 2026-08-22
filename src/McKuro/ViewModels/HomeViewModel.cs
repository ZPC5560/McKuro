using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FluentIcons.Common;
using McKuro.Core.Models.User;
using McKuro.Core.Services.Game;
using McKuro.Services;

namespace McKuro.ViewModels;

/// <summary>首页每日数据项(体力/结晶单质/活跃度/周本/终焉矩阵/冥歌海墟/千道门扉/周度游历/战令)。</summary>
public sealed class DailyItem : System.ComponentModel.INotifyPropertyChanged
{
    public required Icon Icon { get; init; }
    /// <summary>图标来源:官方图标 URL(库街区 getData 每项 img)或本地游戏图标路径(Assets/waves/*.png);非空时显示图片。</summary>
    public string? ImageUrl { get; init; }
    public required string Name { get; init; }
    public required string ValueText { get; init; }   // 例如 "120/160" 或仅 "100"
    /// <summary>次要说明(可选,如电台 "经验: 3250/12000")。</summary>
    public string? SubText { get; init; }
    public required int Cur { get; init; }
    public required int Total { get; init; }
    /// <summary>是否有总量(有 total 才显示进度条)。</summary>
    public bool HasTotal => Total > 0;
    /// <summary>进度 0-100。</summary>
    public double Percent => Total > 0 ? Math.Clamp(Cur * 100.0 / Total, 0, 100) : 0;
    public string PercentText => $"{Percent:0}%";

    /// <summary>每点恢复秒数(体力/结晶单质均为 360 = 6 分钟/点;0 = 不恢复,不显示倒计时)。</summary>
    public int RecoverSecondsPerPoint { get; init; }

    /// <summary>倒计时门控项(结晶单质:体力(结晶波片)恢复满后才开始恢复,体力未满时不启动倒计时)。</summary>
    public DailyItem? Gate { get; init; }

    /// <summary>数据加载时间(倒计时以此为准)。</summary>
    public DateTime LoadedAt { get; init; } = DateTime.Now;

    private string? _countdownText;

    /// <summary>恢复满预计用时文本(如 "预计 3:24:10 后满");不显示时为空。</summary>
    public string? CountdownText => _countdownText;

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    /// <summary>距离恢复满的剩余秒数(null = 不显示倒计时:已满/无总量/不恢复/已算尽)。</summary>
    public static int? RemainingSeconds(int cur, int total, int secondsPerPoint, TimeSpan elapsed)
    {
        if (secondsPerPoint <= 0 || total <= 0 || cur >= total)
        {
            return null;
        }
        var raw = (long)(total - cur) * secondsPerPoint - Math.Max(0, (long)elapsed.TotalSeconds);
        return raw <= 0 ? null : (int)Math.Min(raw, int.MaxValue);
    }

    /// <summary>该数据项截至 elapsed 时刻(含已恢复点数)是否已满。</summary>
    public static bool IsFullAt(DailyItem item, TimeSpan elapsed)
    {
        if (item.Total <= 0)
        {
            return false;
        }
        var points = item.RecoverSecondsPerPoint > 0
            ? (int)(elapsed.TotalSeconds / item.RecoverSecondsPerPoint)
            : 0;
        return item.Cur + points >= item.Total;
    }

    /// <summary>刷新倒计时文本(计时器每秒调用;门控项未满时被门控项不启动倒计时)。</summary>
    public void TickClock(DateTime now)
    {
        var elapsed = now - LoadedAt;
        if (Gate is not null)
        {
            if (!IsFullAt(Gate, now - Gate.LoadedAt))
            {
                SetCountdownText(null);
                return;
            }
            // 门控项已满:被门控项从门控开启那一刻开始计时(而非数据加载时)。
            var gateOpenAt = Gate.LoadedAt.AddSeconds(Gate.RecoverSecondsPerPoint > 0
                ? Math.Max(0L, (long)(Gate.Total - Gate.Cur) * Gate.RecoverSecondsPerPoint)
                : 0);
            elapsed = now - gateOpenAt;
        }
        SetCountdownText(RemainingSeconds(Cur, Total, RecoverSecondsPerPoint, elapsed));
    }

    private void SetCountdownText(int? seconds)
    {
        var text = seconds is null ? null : $"预计 {FormatCountdown(seconds.Value)} 后满";
        if (text != _countdownText)
        {
            _countdownText = text;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(CountdownText)));
        }
    }

    /// <summary>秒数 → "h:mm:ss"(≥1 小时)或 "mm:ss"。</summary>
    public static string FormatCountdown(long seconds) =>
        seconds >= 3600
            ? $"{seconds / 3600}:{(seconds % 3600) / 60:00}:{seconds % 60:00}"
            : $"{seconds / 60:00}:{seconds % 60:00}";

    /// <summary>
    /// 计算展示/进度用总量:curOnly 时无总量(不显示进度条);
    /// 否则优先接口 total,接口缺失(0)时回退默认上限(如活跃度 100、周本 3)。
    /// </summary>
    public static int ResolveTotal(bool curOnly, int detailTotal, int totalFallback)
        => curOnly ? 0 : detailTotal > 0 ? detailTotal : totalFallback;
}

/// <summary>主页:InternalBeyond 风格欢迎页 + 角色每日数据(全量字段)。</summary>
public sealed partial class HomeViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private bool _isInstalled;

    [ObservableProperty]
    private string _installStateText = "未检测";

    [ObservableProperty]
    private string _serverTypeText = "-";

    [ObservableProperty]
    private bool _isLoggedIn;

    [ObservableProperty]
    private string _accountText = "未登录";

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>欢迎页淡入动画(0→1,配合 Avalonia Transitions)。</summary>
    [ObservableProperty]
    private double _revealOpacity;

    /// <summary>每日数据项(体力/结晶单质/活跃度/周本/终焉矩阵/冥歌海墟/千道门扉/周度游历/战令)。</summary>
    public ObservableCollection<DailyItem> DailyItems { get; } = [];

    /// <summary>默认头像(对齐 Haiyu GameRoilDataWrapper:库街区未绑定头像时的官方默认图)。</summary>
    private const string DefaultAvatarUrl = "https://mc.kurogames.com/cloud/assets/avatar-cb06ab22.png";

    /// <summary>角色名(游戏内昵称)。</summary>
    [ObservableProperty]
    private string _roleNameText = "";

    /// <summary>角色等级文本,如 "LV.80";未知时为空。</summary>
    [ObservableProperty]
    private string _levelText = "";

    /// <summary>已游玩文本,如 "已游玩 818 天";未知时为空。</summary>
    [ObservableProperty]
    private string _playDaysText = "";

    /// <summary>头像 URL(空时用默认头像资源)。</summary>
    [ObservableProperty]
    private string _avatarUrl = "";

    /// <summary>是否为开服玩家(2024-05-23 开服及后 10 天内注册)。</summary>
    [ObservableProperty]
    private bool _isLaunchPlayer;

    /// <summary>注册时间文本(如 "注册于 2024-05-23"),用作开服玩家徽章提示。</summary>
    [ObservableProperty]
    private string _registerText = "";

    /// <summary>角色 ID(资料卡右侧,如 "ID: 103242935");未知时为空。</summary>
    [ObservableProperty]
    private string _roleIdText = "";

    /// <summary>是否已有角色资料(控制首页资料卡显示)。</summary>
    [ObservableProperty]
    private bool _hasProfile;

    /// <summary>本地官方图标路径(参照 Haiyu Assets/GameAssets/Waves:波片/结晶单质/活跃度/战令)。</summary>
    private static string GameIcon(string fileName)
        => Path.Combine(AppContext.BaseDirectory, "Assets", "waves", fileName);

    /// <summary>每日数据倒计时(体力/结晶单质)每秒刷新。</summary>
    private readonly DispatcherTimer _countdownTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    public HomeViewModel()
    {
        _countdownTimer.Tick += (_, _) => TickCountdowns();
        _countdownTimer.Start();
        RefreshState();
        _ = RefreshDailyAsync();
        _ = RevealAsync();
    }

    private void TickCountdowns()
    {
        var now = DateTime.Now;
        foreach (var item in DailyItems)
        {
            item.TickClock(now);
        }
    }

    private async Task RevealAsync()
    {
        await Task.Delay(120);
        RevealOpacity = 1;
    }

    private void RefreshState()
    {
        IsInstalled = AppServices.Paths.IsGameInstalled;
        InstallStateText = IsInstalled ? "游戏已就绪" : "尚未安装游戏";
        ServerTypeText = AppServices.Paths.DetectServerType() switch
        {
            GameServerType.Official => "官服",
            GameServerType.Bilibili => "B站",
            GameServerType.WeGame => "WeGame",
            GameServerType.Global => "国际服",
            _ => "自动检测",
        };
        var account = AppServices.KuroAccounts.Current;
        IsLoggedIn = account is not null;
        // 显示账号名称(昵称);未登录显示"未登录",名称缺失时用友好占位而非账号 ID
        AccountText = account is null
            ? "未登录"
            : (!string.IsNullOrWhiteSpace(account.Nickname) ? account.Nickname : "已登录账号");
    }

    [RelayCommand]
    private void Refresh() => RefreshState();

    /// <summary>导航到启动器页。</summary>
    [RelayCommand]
    private void GoLauncher() => SendNav(NavigationKeys.Launcher);

    /// <summary>导航到抽卡分析页。</summary>
    [RelayCommand]
    private void GoGacha() => SendNav(NavigationKeys.Gacha);

    /// <summary>导航到角色数据页。</summary>
    [RelayCommand]
    private void GoRoles() => SendNav(NavigationKeys.Roles);

    private static void SendNav(string key)
        => WeakReferenceMessenger.Default.Send(new NavigationRequestedMessage(key));

    /// <summary>拉取角色每日数据(优先本地游戏缓存 + PC 启动器 SDK,失败回退库街区接口)。</summary>
    [RelayCommand]
    private async Task RefreshDailyAsync()
    {
        if (IsBusy)
        {
            return;
        }
        RefreshState();
        IsBusy = true;
        StatusText = "正在拉取每日数据…";
        try
        {
            // ① 优先本地游戏缓存 + PC 启动器 SDK(不依赖库街区登录)
            var local = await AppServices.LocalDaily.GetDailyDataAsync();
            if (local is not null)
            {
                ApplyDailyData(local, "本地启动器");
                return;
            }

            // ② 回退库街区接口(需登录)
            if (!IsLoggedIn)
            {
                ClearProfile();
                DailyItems.Clear();
                StatusText = "本地无游戏缓存,请登录库街区账号显示每日数据";
                return;
            }
            var data = await AppServices.DailyData.GetDailyDataAsync();
            if (data is null)
            {
                ClearProfile();
                DailyItems.Clear();
                StatusText = "拉取每日数据失败(未登录或接口异常)";
                return;
            }
            ApplyDailyData(data, "库街区");
        }
        catch (Exception ex)
        {
            StatusText = $"拉取失败: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>清空角色资料(刷新失败/无数据时,避免展示过期账号)。</summary>
    private void ClearProfile()
    {
        HasProfile = false;
        RoleNameText = "";
        RoleIdText = "";
        LevelText = "";
        PlayDaysText = "";
        AvatarUrl = "";
        IsLaunchPlayer = false;
        RegisterText = "";
    }

    /// <summary>应用全量每日数据(缺字段的项自动跳过)。</summary>
    private void ApplyDailyData(RoleDailyData data, string source)
    {
        DailyItems.Clear();
        ApplyProfile(data);
        // 体力(结晶波片):每 6 分钟恢复 1 点(上限 240);结晶单质:体力恢复满(240)后才开始恢复,
        // 同样每 6 分钟恢复 1 点(上限 480),体力未满时不启动倒计时。
        var energy = AddItem(data.EnergyData, Icon.Flash, "体力", iconFile: "waveplates.png", recoverMinutes: 6);
        AddItem(data.StoreEnergyData, Icon.Diamond, "结晶单质", iconFile: "wavesubstance.png",
            gate: energy, recoverMinutes: 6, totalFallback: 480);
        // 活跃度满 100:接口无总量时回退 100(数据中心 livenessMaxCount)
        AddItem(data.LivenessData, Icon.Fire, "活跃度", iconFile: "activity.png",
            totalFallback: data.LivenessLimit > 0 ? data.LivenessLimit : 100);
        // 周本每周 3 次:接口无总量时回退 3(数据中心 weeklyInstCountLimit)
        AddItem(data.WeeklyData, Icon.Trophy, "周本", iconFile: "weeklyInst.png", forcedUrl: data.WeeklyIconUrl,
            totalFallback: data.WeeklyLimit > 0 ? data.WeeklyLimit : 3);
        AddItem(data.NewTowerData, Icon.BuildingSkyscraper, "终焉矩阵");
        AddItem(data.SlashTowerData, Icon.Beach, "冥歌海墟");
        AddItem(data.RougeData, Icon.Door, "千道门扉", curOnly: true);
        AddItem(data.WeeklyFrameData, Icon.Map, "周度游历", curOnly: true);
        AddBattlePass(data.BattlePassData);
        StatusText = $"已更新({source}) · {data.RoleName ?? ""}({data.RoleId ?? ""})";
    }

    /// <summary>填充资料卡:昵称/等级/游玩天数/头像/开服玩家徽章(参照 Java WutheringWavesTool 角色卡)。</summary>
    private void ApplyProfile(RoleDailyData data)
    {
        RoleNameText = string.IsNullOrWhiteSpace(data.RoleName) ? "" : data.RoleName!;
        RoleIdText = string.IsNullOrWhiteSpace(data.RoleId) ? "" : $"ID: {data.RoleId}";
        LevelText = data.Level > 0 ? $"LV.{data.Level}" : "";
        PlayDaysText = data.ActiveDays > 0 ? $"已游玩 {data.ActiveDays} 天" : "";
        AvatarUrl = string.IsNullOrWhiteSpace(data.HeadUrl) ? DefaultAvatarUrl : data.HeadUrl!;
        RegisterText = data.CreatTime > 0
            ? $"注册于 {DateTimeOffset.FromUnixTimeMilliseconds(data.CreatTime).LocalDateTime:yyyy-MM-dd}"
            : "";
        IsLaunchPlayer = UserProfile.IsLaunchPlayer(data.CreatTime);
        HasProfile = !string.IsNullOrWhiteSpace(data.RoleName) || data.Level > 0;
    }

    /// <summary>电台(战令):第 1 个元素 cur=等级,第 2 个 cur/total=经验进度(参考截图 "电台 LV.03 经验: 3250/12000")。</summary>
    private void AddBattlePass(List<RoleDailyDetail>? battlePass)
    {
        if (battlePass is null || battlePass.Count == 0)
        {
            return;
        }
        var level = battlePass[0].Cur;
        var progress = battlePass.Count > 1 ? battlePass[1] : null;
        DailyItems.Add(new DailyItem
        {
            Icon = Icon.Medal,
            ImageUrl = GameIcon("podcast.png"),
            Name = "电台",
            ValueText = $"LV.{level:00}",
            SubText = progress is null ? null : $"经验: {progress.Cur}/{progress.Total}",
            Cur = progress?.Cur ?? 0,
            Total = progress?.Total ?? 0,
        });
    }

    private DailyItem? AddItem(RoleDailyDetail? detail, Icon icon, string fallbackName, bool curOnly = false,
        string? iconFile = null, string? forcedUrl = null, int totalFallback = 0,
        int recoverMinutes = 0, DailyItem? gate = null)
    {
        if (detail is null)
        {
            return null;
        }
        var total = DailyItem.ResolveTotal(curOnly, detail.Total, totalFallback);
        var item = new DailyItem
        {
            Icon = icon,
            // 优先级:强制图标(数据中心周本图标)→ 本地官方图标 → 库街区 img 字段
            ImageUrl = !string.IsNullOrWhiteSpace(forcedUrl)
                ? forcedUrl
                : iconFile is not null
                    ? GameIcon(iconFile)
                    : string.IsNullOrWhiteSpace(detail.Img) ? null : detail.Img,
            Name = string.IsNullOrWhiteSpace(detail.Name) ? fallbackName : detail.Name!,
            ValueText = total > 0 ? $"{detail.Cur}/{total}" : $"{detail.Cur}",
            Cur = detail.Cur,
            Total = total,
            RecoverSecondsPerPoint = recoverMinutes * 60,
            Gate = gate,
        };
        DailyItems.Add(item);
        return item;
    }
}
