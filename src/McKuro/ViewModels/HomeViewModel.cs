using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FluentIcons.Common;
using McKuro.Core.Models.Kuro;
using McKuro.Core.Models.User;
using McKuro.Core.Services.Game;
using McKuro.Core.Services.User;
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
        var text = seconds is null ? null : LanguageService.Format("Home.RecoverFull", FormatCountdown(seconds.Value));
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

/// <summary>
/// 首页「今日数据」绑定账号选项。
/// <see cref="Key"/> 为空 = «自动»;否则是本地启动器凭证的「库街区UID|游戏角色UID」复合键
/// (数据源与 Haiyu 的账号卡片相同:KRSDKUserLauncherCache.json + PC 启动器 SDK)。
/// 列表项一次枚举即同时拿到游戏昵称与游戏 UID,无需二次补全。
/// </summary>
public sealed partial class HomeAccountOption : ObservableObject
{
    /// <summary>绑定键(空串 = 自动)。</summary>
    public required string Key { get; init; }

    /// <summary>主行文本:「China - U536781653A - 以椿为鸣」。</summary>
    [ObservableProperty]
    private string _display = "";

    /// <summary>第二行小字:游戏角色 UID(为空则该行不显示、不编造)。</summary>
    [ObservableProperty]
    private string? _roleIdText;

    /// <summary>是否为弹窗当前选中项(选中项用主题强调色高亮)。</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>是否已有游戏角色 UID 可展示/复制。</summary>
    public bool HasRoleId => !string.IsNullOrWhiteSpace(RoleIdText);

    partial void OnRoleIdTextChanged(string? value) => OnPropertyChanged(nameof(HasRoleId));

    public override string ToString() => Display;
}

/// <summary>账号切换弹窗「刷新」的结果(用于给出明确反馈,而不是静默执行)。</summary>
public enum AccountRefreshOutcome
{
    /// <summary>重新检测到有变化(新增/移除了本地账号)。</summary>
    Added,
    /// <summary>检测完成,结果与刷新前一致。</summary>
    Unchanged,
    /// <summary>未检测到任何本地账号(未装启动器/无凭证/接口瞬时失败)。</summary>
    Failed,
}

/// <summary>账号切换弹窗的展示状态:候选列表 + 当前选中项(点卡片切换)。</summary>
public sealed partial class AccountSwitchSelection : ObservableObject
{
    public AccountSwitchSelection(IEnumerable<HomeAccountOption> options, string selectedKey)
    {
        Options = [.. options];
        foreach (var option in Options)
        {
            option.IsSelected = option.Key == selectedKey;
        }
        _selected = Options.FirstOrDefault(o => o.IsSelected) ?? Options.FirstOrDefault();
        if (_selected is not null)
        {
            _selected.IsSelected = true;
        }
    }

    /// <summary>候选账号(含«自动»);元素与 HomeViewModel 共享同一实例。</summary>
    public IReadOnlyList<HomeAccountOption> Options { get; }

    /// <summary>是否有多账号可切换(仅一个选项时弹窗显示空态提示)。</summary>
    public bool HasChoice => Options.Count > 1;

    [ObservableProperty]
    private HomeAccountOption? _selected;

    /// <summary>刷新进行中(禁用刷新键,避免狂点并发枚举触发接口限流)。</summary>
    [ObservableProperty]
    private bool _isRefreshing;

    /// <summary>刷新结果反馈文案(空 = 尚未刷新或已清除,不占位)。</summary>
    [ObservableProperty]
    private string _refreshStatus = "";

    /// <summary>反馈配色:成功=正常文字,失败/未检测到=警示色。</summary>
    [ObservableProperty]
    private bool _refreshFailed;

    /// <summary>是否有刷新反馈可显示。</summary>
    public bool HasRefreshStatus => !string.IsNullOrWhiteSpace(RefreshStatus);

    /// <summary>成功类反馈(已刷新/无变化)可见。</summary>
    public bool ShowRefreshOk => HasRefreshStatus && !RefreshFailed;

    /// <summary>失败类反馈(未检测到本地账号)可见。</summary>
    public bool ShowRefreshFailed => HasRefreshStatus && RefreshFailed;

    partial void OnRefreshStatusChanged(string value)
    {
        OnPropertyChanged(nameof(HasRefreshStatus));
        OnPropertyChanged(nameof(ShowRefreshOk));
        OnPropertyChanged(nameof(ShowRefreshFailed));
    }

    partial void OnRefreshFailedChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowRefreshOk));
        OnPropertyChanged(nameof(ShowRefreshFailed));
    }

    /// <summary>记录一次刷新结果(由弹窗在枚举完成后回填)。</summary>
    public void ApplyRefreshOutcome(AccountRefreshOutcome outcome)
    {
        RefreshFailed = outcome == AccountRefreshOutcome.Failed;
        RefreshStatus = outcome switch
        {
            AccountRefreshOutcome.Added => LanguageService.Format("Home.AccountRefreshAdded"),
            AccountRefreshOutcome.Unchanged => LanguageService.Format("Home.AccountRefreshUnchanged"),
            _ => LanguageService.Format("Home.AccountRefreshNone"),
        };
    }

    /// <summary>选中某个候选(点卡片时调用)。</summary>
    public void Select(HomeAccountOption? option)
    {
        if (option is null)
        {
            return;
        }
        foreach (var item in Options)
        {
            item.IsSelected = ReferenceEquals(item, option);
        }
        Selected = option;
    }
}

/// <summary>主页:InternalBeyond 风格欢迎页 + 角色每日数据(全量字段)。</summary>
public sealed partial class HomeViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isInstalled;

    [ObservableProperty]
    private string _installStateText = LanguageService.Format("Launcher.NotDetected");

    [ObservableProperty]
    private string _serverTypeText = "-";

    [ObservableProperty]
    private bool _isLoggedIn;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>欢迎页淡入动画(0→1,配合 Avalonia Transitions)。</summary>
    [ObservableProperty]
    private double _revealOpacity;

    // ---------- 首页 Live2D 模型(仅 Windows x64;设置页导入/配置,此处只读应用) ----------

    /// <summary>平台支持 Live2D 渲染(当前仅 Windows x64)。</summary>
    public bool IsLive2DSupported => Live2DLocator.IsSupported;

    /// <summary>首页是否应显示 Live2D 模型(设置开启 + 平台支持 + Cubism Core 可用 + 模型有效)。</summary>
    [ObservableProperty]
    private bool _live2DShow;

    /// <summary>Live2D 模型文件夹。</summary>
    [ObservableProperty]
    private string _live2DModelDir = "";

    /// <summary>Live2D 模型名(不含扩展名)。</summary>
    [ObservableProperty]
    private string _live2DModelName = "";

    /// <summary>模型缩放(0.5~3)。</summary>
    [ObservableProperty]
    private float _live2DZoom = 1f;

    /// <summary>模型水平位置(-2~2)。</summary>
    [ObservableProperty]
    private float _live2DPositionX;

    /// <summary>模型垂直位置(-2~2)。</summary>
    [ObservableProperty]
    private float _live2DPositionY;

    /// <summary>模型不透明度(0~1)。</summary>
    [ObservableProperty]
    private float _live2DOpacity = 1f;

    /// <summary>模型视线跟随鼠标(开启时模型区域会接收指针事件以驱动跟随)。</summary>
    [ObservableProperty]
    private bool _live2DPointerFollow = true;

    /// <summary>从设置同步 Live2D 显示状态(构造时与收到 Live2DSettingsChangedMessage 时调用)。</summary>
    public void RefreshLive2D()
    {
        var s = AppServices.Settings.Current;
        // 未导入模型目录时回退到首选 live2d 目录(程序目录下 live2d\,无写权限才用用户数据目录)自动发现默认模型
        var modelDir = string.IsNullOrWhiteSpace(s.Live2DModelDir) ? Live2DLocator.EnsurePreferredDir() : s.Live2DModelDir;
        var modelName = Live2DLocator.NormalizeModelName(s.Live2DModelName);
        var valid = !string.IsNullOrWhiteSpace(modelName)
                    && File.Exists(Path.Combine(modelDir, modelName + ".model3.json"));
        if (!valid)
        {
            var fallback = Live2DLocator.FindDefaultModel(modelDir);
            if (fallback is not null)
            {
                modelDir = fallback.Value.Dir;
                modelName = fallback.Value.Name;
                valid = true;
            }
        }
        Live2DModelDir = valid ? modelDir : "";
        Live2DModelName = valid ? modelName : "";
        Live2DZoom = Math.Clamp(s.Live2DZoom, 0.5f, 3f);
        Live2DPositionX = Math.Clamp(s.Live2DPositionX, -2f, 2f);
        Live2DPositionY = Math.Clamp(s.Live2DPositionY, -2f, 2f);
        Live2DOpacity = Math.Clamp(s.Live2DOpacity, 0f, 1f);
        Live2DPointerFollow = s.Live2DPointerFollow;
        // Core 缺失时显示层置 false(设置页有指引);DLL 搜索路径仍要就位,模型加载才能解析 Core
        Live2DLocator.EnsureDllSearchPath();
        Live2DShow = IsLive2DSupported && s.Live2DEnabled && valid && Live2DLocator.IsCoreAvailable;
    }

    /// <summary>每日数据项(体力/结晶单质/活跃度/周本/终焉矩阵/冥歌海墟/千道门扉/周度游历/战令)。</summary>
    public ObservableCollection<DailyItem> DailyItems { get; } = [];

    /// <summary>默认头像(对齐 Haiyu GameRoilDataWrapper:库街区未绑定头像时的官方默认图)。</summary>
    private const string DefaultAvatarUrl = "https://mc.kurogames.com/cloud/assets/avatar-cb06ab22.png";

    /// <summary>头像磁盘缓存分类(icon_cache/avatar,按 userId 索引;对齐 Java WutheringWavesTool assets/header 本地化)。</summary>
    private const string AvatarCacheCategory = "avatar";

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
    [NotifyPropertyChangedFor(nameof(ShowProfileCard))]
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
        // 游戏进程正常退出后重拉今日数据(体力/活跃度等在游玩期间会变化)
        WeakReferenceMessenger.Default.Register<HomeViewModel, GameSessionEndedMessage>(this,
            static (recipient, message) =>
            {
                if (message.Value == GameSessionEndReason.Finished)
                {
                    _ = recipient.RefreshDailyAsync();
                }
            });
        // 设置页 Live2D 开关/导入/参数调整后即时同步(模型显示层由视图订阅属性变化重建)
        WeakReferenceMessenger.Default.Register<HomeViewModel, Live2DSettingsChangedMessage>(this,
            static (recipient, _) => recipient.RefreshLive2D());
        RefreshState();
        RefreshLive2D();
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
        InstallStateText = IsInstalled ? LanguageService.Format("Launcher.GameReady") : LanguageService.Format("Launcher.NotInstalled");
        ServerTypeText = AppServices.Paths.DetectServerType() switch
        {
            GameServerType.Official => LanguageService.Format("Server.Official"),
            GameServerType.Bilibili => LanguageService.Format("Server.Bilibili"),
            GameServerType.WeGame => "WeGame",
            GameServerType.Global => LanguageService.Format("Server.Global"),
            _ => LanguageService.Format("Server.Auto"),
        };
        var account = AppServices.KuroAccounts.Current;
        IsLoggedIn = account is not null;
        RebuildHomeAccountOptions();
        // 后台枚举本地启动器登录凭证对应的全部角色(对齐 Haiyu 的账号列表数据源)
        _ = LoadLocalAccountsAsync();
    }

    // ---------- 今日数据绑定账号(识别本地启动器登录凭证,与 Haiyu 账号列表同一数据源) ----------

    /// <summary>可选绑定账号:«自动» + 本地启动器凭证枚举出的全部游戏角色。</summary>
    public ObservableCollection<HomeAccountOption> HomeAccountOptions { get; } = [];

    /// <summary>是否有可切换的账号(«自动» + 至少一个本地角色)。</summary>
    public bool HasHomeAccountChoice => HomeAccountOptions.Count > 1;

    /// <summary>
    /// 资料卡是否可见:有角色数据时可见;数据拉取失败时<b>只要还有可切换账号也必须可见</b> ——
    /// 否则绑定了拉不到数据的角色后,切换按键随卡片一起消失,再也切不回来(用户反馈的实际问题)。
    /// </summary>
    public bool ShowProfileCard => HasProfile || HasHomeAccountChoice;

    /// <summary>当前绑定键("库街区UID|游戏UID");空 = 自动模式。</summary>
    public string HomeBoundAccountId => AppServices.Settings.Current.HomeBoundAccountId;

    /// <summary>本地启动器凭证枚举出的角色(含游戏昵称与游戏 UID);首次加载完成前为空。</summary>
    private List<LocalLauncherPlayer> _localPlayers = [];

    /// <summary>自动模式最近一次实际展示的角色,用于«自动»项的第二行小字。</summary>
    private (string? RoleName, string? RoleId) _autoShownRole;

    /// <summary>本地凭证枚举进行中(防止重复打接口)。</summary>
    private bool _localPlayersLoading;

    /// <summary>按绑定值与已枚举到的本地角色重建选项列表(同步,可反复调用)。</summary>
    private void RebuildHomeAccountOptions()
    {
        var settings = AppServices.Settings.Current;
        var bound = settings.HomeBoundAccountId;
        // 绑定的角色已不在本地凭证里(游戏里登出/换绑)→ 回退«自动»并落盘。
        // 两个前提缺一不可:① 列表非空(还没加载完时不能清,否则首次进入会把用户的绑定误删);
        // ② 该绑定值已被「不走网络」的凭证存在性检查确认真的不在缓存文件里。
        // 只看②不够:启动器缓存文件读不到时也"看不到"该凭证,那时必须保守不清。
        //
        // 注意必须核对 _boundCredentialGoneKey == bound:判定结果是针对某个具体键算出来的,
        // 而用户刚选定的新键(ApplyAccountSelection → RefreshDailyAsync → RefreshState)会走到这里,
        // 那时旧键的"已消失"结论绝不能套用到新键上,否则刚选好就被清掉。
        if (ShouldClearBinding(bound, _localPlayers.Count, IsBoundCredentialGone(bound)))
        {
            settings.HomeBoundAccountId = "";
            AppServices.Settings.Save();
            bound = "";
            _boundCredentialGone = false;
            _boundCredentialGoneKey = "";
        }

        HomeAccountOptions.Clear();
        HomeAccountOptions.Add(new HomeAccountOption
        {
            Key = "",
            Display = LanguageService.Format("Home.AccountAuto"),
            RoleIdText = _autoShownRole.RoleId,
            IsSelected = bound.Length == 0,
        });
        foreach (var player in _localPlayers)
        {
            HomeAccountOptions.Add(new HomeAccountOption
            {
                Key = player.BindKey,
                Display = BuildAccountDisplay(player),
                RoleIdText = player.RoleId,
                IsSelected = player.BindKey == bound,
            });
        }
        OnPropertyChanged(nameof(HasHomeAccountChoice));
        OnPropertyChanged(nameof(ShowProfileCard));
    }

    /// <summary>
    /// 列表项主行文本(对齐 Haiyu):「China - U536781653A - 以椿为鸣」。
    /// 用户名用缓存的 username(形如 U…A,缺失时退回数字 UID);拿不到角色昵称时省略尾段,不编造。
    /// </summary>
    public static string BuildAccountDisplay(LocalLauncherPlayer player)
    {
        var region = LanguageService.Format("Home.AccountRegion");
        var uid = string.IsNullOrWhiteSpace(player.Username) ? player.KuroUid : player.Username;
        return string.IsNullOrWhiteSpace(player.RoleName)
            ? $"{region} - {uid}"
            : $"{region} - {uid} - {player.RoleName}";
    }

    /// <summary>
    /// 枚举本地启动器登录凭证对应的全部角色(读 KRSDKUserLauncherCache.json + 逐个 queryPlayerInfo),
    /// 拿到后回主线程重建选项列表。这就是"列表里只有我自己登录的一个账号"的根因修复:
    /// 之前列的是 McKuro 自己的库街区登录,而不是游戏启动器的本地登录凭证。
    /// </summary>
    /// <param name="forceRefresh">
    /// 弹窗「刷新」用:重新枚举以反映"游戏里刚换的号",不必重启应用。
    /// </param>
    /// <returns>本次枚举是否成功拿到结果(false = 沿用旧数据或空列表,供刷新反馈如实报告)。</returns>
    private async Task<bool> LoadLocalAccountsAsync(bool forceRefresh = false)
    {
        if (_localPlayersLoading)
        {
            return false;
        }
        _localPlayersLoading = true;
        try
        {
            if (forceRefresh)
            {
                var (players, fresh) = await AppServices.LocalDaily.RefreshLocalPlayersAsync();
                _localPlayers = players;
                await RefreshBoundCredentialGoneAsync();
                await RebuildOnUiThreadAsync();
                return fresh;
            }
            // 走服务自身的短缓存:RefreshState 在构造/刷新/每次每日数据刷新(含游戏退出)都会调用,
            // 直接用未缓存的 GetLocalPlayersAsync 会每次打一轮 queryPlayerInfo(实测该接口有 1005 限流)。
            _localPlayers = await AppServices.LocalDaily.GetLocalPlayersCachedAsync();
            await RefreshBoundCredentialGoneAsync();
            await RebuildOnUiThreadAsync();
            return true;
        }
        catch
        {
            // 枚举失败(未装启动器/无本地缓存):列表退化为只有«自动»,不影响自动模式取数
            _boundCredentialGone = false;
            _boundCredentialGoneKey = "";
            return false;
        }
        finally
        {
            _localPlayersLoading = false;
        }
    }

    /// <summary>
    /// 持久化绑定的账号是否<b>确实已不在</b>本地启动器凭证里(唯一允许清空绑定值的条件)。
    /// <para>
    /// 不能只看"枚举结果里没有它":<see cref="McKuro.Core.Services.User.LocalGameDailyDataService.QueryPlayersAsync"/>
    /// 对 queryPlayerInfo 失败(1005 限流/瞬时网络错误)的凭证是<b>静默跳过</b>、其余账号照常返回,
    /// 因此「列表里没有」既可能是真登出,也可能只是这次没查到 —— 后者一旦清空就把用户选好的账号抹掉了。
    /// 故进一步用<b>不走网络</b>的凭证存在性检查确认:<c>false</c>(确实不在)才允许清;<c>null</c>(读不到缓存文件)保守不清。
    /// </para>
    /// </summary>
    private static async Task<bool> IsBoundCredentialGoneAsync(string bound)
    {
        if (bound.Length == 0)
        {
            return false;
        }
        var kuroUid = bound.Split('|')[0];
        var exists = await AppServices.LocalDaily.HasCredentialAsync(kuroUid);
        return exists == false;
    }

    /// <summary>
    /// <paramref name="bound"/> 这个绑定值是否已被确认"凭证消失"。
    /// <para>
    /// <b>不能只看 <paramref name="gone"/></b>:那个结论是针对 <paramref name="verdictKey"/> 这个具体键
    /// 算出来的。用户选定新账号会复用同一 VM(ApplyAccountSelection → RefreshDailyAsync → RefreshState
    /// → RebuildHomeAccountOptions),若把旧键的结论套到新键上,用户<b>刚选好的绑定就会被立刻清掉</b>。
    /// 故必须做键核对。
    /// </para>
    /// <para>
    /// 抽成纯静态函数(而非内联在重建方法里 / 依赖 VM 实例)是为了让"键不匹配 ⇒ 不算已消失"
    /// 这条能被单测真正钉住:早先该判断内联在调用处,回归测试只断言了
    /// <see cref="ShouldClearBinding"/>,把键核对整段删掉测试依然全绿 —— 等于没有保护。
    /// 纯函数也免去测试里构造 <see cref="HomeViewModel"/>(其构造函数会起计时器并拉数据)。
    /// </para>
    /// </summary>
    public static bool CredentialGoneFor(string? bound, bool gone, string? verdictKey)
        => gone
            && !string.IsNullOrEmpty(bound)
            && string.Equals(verdictKey, bound, StringComparison.Ordinal);

    /// <summary>当前绑定值是否已被确认"凭证消失"(把实例里记的判定结果与判定时的键交给上面的纯函数)。</summary>
    private bool IsBoundCredentialGone(string? bound)
        => CredentialGoneFor(bound, _boundCredentialGone, _boundCredentialGoneKey);

    /// <summary>
    /// 是否应当清除持久化的账号绑定(回退到«自动»)。纯函数,便于单测覆盖"数据丢失 vs 保守保留"的边界。
    /// <para>
    /// 三个前提缺一不可:
    /// ① <paramref name="bound"/> 非空(没绑定就无所谓清);
    /// ② <paramref name="localPlayerCount"/> &gt; 0 —— 列表还没加载完时不能清,否则首次进入会把用户的绑定误删;
    /// ③ <paramref name="credentialGone"/> —— 已用「不走网络」的凭证存在性检查确认它真的不在启动器缓存里了。
    ///    <b>只看"枚举结果里没有它"不够</b>:单凭证 queryPlayerInfo 失败(1005 限流/瞬时错误)会被静默跳过、
    ///    其余账号照常返回,于是"列表非空"并不代表该凭证真的消失了;缓存文件读不到时同理(无法判定 ⇒ 保守不清)。
    /// </para>
    /// </summary>
    public static bool ShouldClearBinding(string? bound, int localPlayerCount, bool credentialGone)
        => !string.IsNullOrEmpty(bound)
            && localPlayerCount > 0
            && credentialGone;

    /// <summary>
    /// 重算"当前绑定值的凭证是否已消失",并把结果与<b>被判定时的键</b>一起记下来
    /// (键必须一起记:见 <see cref="_boundCredentialGoneKey"/>)。
    /// <para>
    /// 键在 await 之前只读一次并同时用于判定和记录 —— 若在 await 之后才读,等待期间用户换绑会
    /// 让"判定用的键"与"记录的键"错位,前者的结论就会被安到后者头上。
    /// </para>
    /// </summary>
    private async Task RefreshBoundCredentialGoneAsync()
    {
        var bound = AppServices.Settings.Current.HomeBoundAccountId;
        _boundCredentialGone = await IsBoundCredentialGoneAsync(bound);
        _boundCredentialGoneKey = bound;
    }

    /// <summary>
    /// 持久化绑定值是否已确认失效(见 <see cref="IsBoundCredentialGoneAsync"/>)。
    /// 初值 false:未经验证前绝不清除用户已保存的绑定。
    /// </summary>
    private bool _boundCredentialGone;

    /// <summary>
    /// <see cref="_boundCredentialGone"/> 是针对<b>哪个绑定键</b>算出来的。
    /// <para>
    /// 判定必须与键绑定:用户选定新账号后(<c>ApplyAccountSelection</c> → <c>RefreshDailyAsync</c>
    /// → <c>RefreshState</c> → 本方法所在的重建路径)会复用同一个 VM 实例,
    /// 若不核对是同一个键,旧键"已消失"的结论就会把用户刚选好的新绑定清掉。
    /// </para>
    /// </summary>
    private string _boundCredentialGoneKey = "";

    /// <summary>回 UI 线程重建候选列表(已在 UI 线程时直接重建,避免刷新键返回时还没换完)。</summary>
    private async Task RebuildOnUiThreadAsync()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            RebuildHomeAccountOptions();
        }
        else
        {
            await Dispatcher.UIThread.InvokeAsync(RebuildHomeAccountOptions);
        }
    }

    /// <summary>
    /// 弹窗「刷新」入口:重新检测本地启动器账号并重建候选列表。
    /// 游戏内换号后无需重启应用即可看到新账号(顺带把结果回填给账号页共享缓存)。
    /// </summary>
    /// <returns>
    /// 刷新结果,供弹窗给出明确反馈(此前静默执行,用户点了不知道成没成):
    /// <see cref="AccountRefreshOutcome.Added"/> 有新账号、
    /// <see cref="AccountRefreshOutcome.Unchanged"/> 结果与刷新前一致、
    /// <see cref="AccountRefreshOutcome.Failed"/> 未检测到任何本地账号(未装启动器/无凭证/被限流)。
    /// </returns>
    public async Task<AccountRefreshOutcome> ReloadLocalAccountsAsync()
    {
        var before = _localPlayers.Select(p => p.BindKey).ToList();
        AppServices.LocalDaily.InvalidateLocalPlayersCache();
        var fresh = await LoadLocalAccountsAsync(forceRefresh: true).ConfigureAwait(true);
        return ClassifyRefresh(fresh, before, _localPlayers.Select(p => p.BindKey).ToList());
    }

    /// <summary>
    /// 把刷新结果归类为给用户看的反馈(纯函数,便于单测覆盖"失败 vs 无变化"的区分)。
    /// <para>
    /// 关键点:<paramref name="fresh"/> 为 false 表示本次枚举<b>没成功</b>(沿用旧数据),
    /// 此时即使列表内容与刷新前完全一致,也必须报"未检测到"而不是"无变化"——
    /// 否则接口被限流时用户会看到"无变化"这条假消息。
    /// </para>
    /// </summary>
    public static AccountRefreshOutcome ClassifyRefresh(
        bool fresh, IReadOnlyList<string> before, IReadOnlyList<string> after)
    {
        if (!fresh)
        {
            return AccountRefreshOutcome.Failed;
        }
        return before.Count == after.Count && before.SequenceEqual(after, StringComparer.Ordinal)
            ? AccountRefreshOutcome.Unchanged
            : AccountRefreshOutcome.Added;
    }

    /// <summary>依据持久化绑定值恢复各选项的选中态(弹窗被取消时回到真实状态)。</summary>
    public void SyncAccountSelection()
    {
        var bound = AppServices.Settings.Current.HomeBoundAccountId;
        foreach (var option in HomeAccountOptions)
        {
            option.IsSelected = option.Key == bound;
        }
    }

    /// <summary>应用用户选择的绑定角色(弹窗确定后调用):持久化并重新拉取今日数据。</summary>
    public void ApplyAccountSelection(string key)
    {
        var settings = AppServices.Settings.Current;
        settings.HomeBoundAccountId = key;
        AppServices.Settings.Save();
        foreach (var option in HomeAccountOptions)
        {
            option.IsSelected = option.Key == key;
        }
        OnPropertyChanged(nameof(HomeBoundAccountId));
        OnPropertyChanged(nameof(ShowProfileCard));
        _ = RefreshDailyAsync();
    }

    /// <summary>当前绑定的本地启动器角色;自动模式(未绑定/已失效)返回 null。</summary>
    private LocalLauncherPlayer? GetBoundLocalPlayer()
    {
        var bound = AppServices.Settings.Current.HomeBoundAccountId;
        return bound.Length == 0 ? null : _localPlayers.FirstOrDefault(p => p.BindKey == bound);
    }

    [RelayCommand]
    private void Refresh() => RefreshState();

    /// <summary>
    /// 主页已精简:不再提供「进入游戏/抽卡分析/角色数据」快捷按钮(左侧主导航已可直达),
    /// 故移除对应导航命令。
    /// </summary>
    private static void NotifyUser(string message)
        => WeakReferenceMessenger.Default.Send(new ShowToastMessage(message));

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
        try
        {
            var boundKey = AppServices.Settings.Current.HomeBoundAccountId;
            // 先用本地缓存头像占位(离线/慢网也立即显示,参照 Java 版 assets/header 本地文件优先)
            PrefillAvatarFromCache(boundKey);

            // ⓪ 绑定模式:用该角色所属凭证的 oauthCode 精确走 PC 启动器 SDK(对齐 Haiyu 绑定卡片)。
            //    自动模式只会返回「第一个拉得通的角色」,所以换绑后必须按 BindKey 取数,
            //    否则永远显示同一个号 —— 这正是"切了账号但数据不变"的根因。
            if (!string.IsNullOrEmpty(boundKey))
            {
                var boundData = await AppServices.LocalDaily.GetDailyDataForAsync(boundKey);
                if (boundData is null)
                {
                    ClearProfile();
                    DailyItems.Clear();
                    NotifyUser(LanguageService.Format("Home.FetchDailyFailed"));
                    return;
                }
                ApplyDailyData(boundData);
                await ResolveAvatarAsync(boundData.HeadUrl, boundData.RoleId);
                return;
            }

            // ① 优先本地游戏缓存 + PC 启动器 SDK(不依赖库街区登录)
            var local = await AppServices.LocalDaily.GetDailyDataAsync();
            if (local is not null)
            {
                ApplyDailyData(local);
                // 本地 SDK 数据不含头像 URL:从库街区 gamer 接口补齐并落盘缓存
                await ResolveAvatarAsync(local.HeadUrl, local.RoleId);
                return;
            }

            // ② 回退库街区接口(需登录)
            if (!IsLoggedIn)
            {
                // 未登录/未装游戏属正常状态(不是错误):静默留白,不弹提示打扰
                ClearProfile();
                DailyItems.Clear();
                return;
            }
            var data = await AppServices.DailyData.GetDailyDataAsync();
            if (data is null)
            {
                ClearProfile();
                DailyItems.Clear();
                NotifyUser(LanguageService.Format("Home.FetchDailyFailed"));
                return;
            }
            ApplyDailyData(data);
            // 库街区路径 HeadUrl 已含头像:只需确保落盘缓存并切换为本地路径
            await ResolveAvatarAsync(data.HeadUrl, data.RoleId);
        }
        catch (Exception ex)
        {
            // 页面不再有状态栏,真实错误改走悬浮提示,避免"静默失败"无迹可寻
            NotifyUser(LanguageService.Format("Status.LoadFailedWith", ex.Message));
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 清空角色资料(刷新失败/无数据时,避免展示过期账号)。
    /// <para>
    /// 昵称位<b>不是</b>清空成空串,而是回退显示「当前账号标识」:资料卡必须留在界面上,
    /// 用户才能点昵称旁的切换按键换回一个拉得到数据的账号 —— 之前失败即整卡隐藏,
    /// 切换按键跟着消失,绑定了坏账号就再也切不回来(用户反馈)。
    /// </para>
    /// </summary>
    private void ClearProfile()
    {
        HasProfile = false;
        RoleNameText = FallbackAccountLabel();
        RoleIdText = "";
        LevelText = "";
        PlayDaysText = "";
        AvatarUrl = "";
        IsLaunchPlayer = false;
        RegisterText = "";
    }

    /// <summary>无角色数据时资料卡昵称位的兜底文本(绑定角色昵称 → 当前库街区账号昵称 → 空)。</summary>
    private string FallbackAccountLabel()
    {
        var bound = GetBoundLocalPlayer();
        if (bound is not null)
        {
            return string.IsNullOrWhiteSpace(bound.RoleName) ? bound.RoleId : bound.RoleName;
        }
        if (!string.IsNullOrWhiteSpace(_autoShownRole.RoleName))
        {
            return _autoShownRole.RoleName!;
        }
        var account = AppServices.KuroAccounts.Current;
        if (account is null)
        {
            return "";
        }
        return string.IsNullOrWhiteSpace(account.Nickname) ? account.UserId : account.Nickname!;
    }

    /// <summary>应用全量每日数据(缺字段的项自动跳过)。</summary>
    private void ApplyDailyData(RoleDailyData data)
    {
        DailyItems.Clear();
        ApplyProfile(data);
        // 体力(结晶波片):每 6 分钟恢复 1 点(上限 240);结晶单质:体力恢复满(240)后才开始恢复,
        // 同样每 6 分钟恢复 1 点(上限 480),体力未满时不启动倒计时。
        var energy = AddItem(data.EnergyData, Icon.Flash, LanguageService.Format("Home.ItemEnergy"), iconFile: "waveplates.png", recoverMinutes: 6);
        AddItem(data.StoreEnergyData, Icon.Diamond, LanguageService.Format("Home.ItemCrystal"), iconFile: "wavesubstance.png",
            gate: energy, recoverMinutes: 6, totalFallback: 480);
        // 活跃度满 100:接口无总量时回退 100(数据中心 livenessMaxCount)
        AddItem(data.LivenessData, Icon.Fire, LanguageService.Format("Home.ItemLiveness"), iconFile: "activity.png",
            totalFallback: data.LivenessLimit > 0 ? data.LivenessLimit : 100);
        // 周本每周 3 次:接口无总量时回退 3(数据中心 weeklyInstCountLimit)
        AddItem(data.WeeklyData, Icon.Trophy, LanguageService.Format("Home.ItemWeekly"), iconFile: "weeklyInst.png", forcedUrl: data.WeeklyIconUrl,
            totalFallback: data.WeeklyLimit > 0 ? data.WeeklyLimit : 3);
        AddItem(data.NewTowerData, Icon.BuildingSkyscraper, LanguageService.Format("Tower.TabMatrix"));
        AddItem(data.SlashTowerData, Icon.Beach, LanguageService.Format("Home.ItemSlash"));
        AddItem(data.RougeData, Icon.Door, LanguageService.Format("Home.ItemRouge"), curOnly: true);
        AddItem(data.WeeklyFrameData, Icon.Map, LanguageService.Format("Home.ItemWeeklyFrame"), curOnly: true);
        AddBattlePass(data.BattlePassData);
        // 页面已精简:不再展示「已更新(来源)」这类成功提示(数据本身即反馈)。
    }

    /// <summary>填充资料卡:昵称/等级/游玩天数/头像/开服玩家徽章(参照 Java WutheringWavesTool 角色卡)。</summary>
    private void ApplyProfile(RoleDailyData data)
    {
        RoleNameText = string.IsNullOrWhiteSpace(data.RoleName) ? "" : data.RoleName!;
        RoleIdText = string.IsNullOrWhiteSpace(data.RoleId) ? "" : $"ID: {data.RoleId}";
        // 记住«自动»模式实际展示的角色(昵称+UID),回写到「自动」项的第二行小字
        if (AppServices.Settings.Current.HomeBoundAccountId.Length == 0)
        {
            _autoShownRole = (data.RoleName, data.RoleId);
            var autoOption = HomeAccountOptions.FirstOrDefault(o => o.Key.Length == 0);
            if (autoOption is not null && !string.IsNullOrWhiteSpace(data.RoleId))
            {
                autoOption.RoleIdText = data.RoleId;
            }
        }
        LevelText = data.Level > 0 ? $"LV.{data.Level}" : "";
        PlayDaysText = data.ActiveDays > 0 ? LanguageService.Format("Home.PlayedDays", data.ActiveDays) : "";
        AvatarUrl = string.IsNullOrWhiteSpace(data.HeadUrl) ? DefaultAvatarUrl : data.HeadUrl!;
        RegisterText = data.CreatTime > 0
            ? LanguageService.Format("Home.RegisteredAt", DateTimeOffset.FromUnixTimeMilliseconds(data.CreatTime).LocalDateTime.ToString("yyyy-MM-dd"))
            : "";
        IsLaunchPlayer = UserProfile.IsLaunchPlayer(data.CreatTime);
        HasProfile = !string.IsNullOrWhiteSpace(data.RoleName) || data.Level > 0;
    }

    /// <summary>
    /// 刷新前先用本地磁盘缓存头像占位。头像按<b>游戏角色 UID</b> 缓存 ——
    /// 一个库街区账号可以有多个角色,按账号缓存会在切换角色时串头像;
    /// 角色还未知(首次自动模式)时退回当前库街区账号的旧缓存键。
    /// </summary>
    private void PrefillAvatarFromCache(string boundKey)
    {
        var separator = boundKey.IndexOf('|');
        var roleId = separator > 0 ? boundKey[(separator + 1)..] : _autoShownRole.RoleId;
        var key = !string.IsNullOrWhiteSpace(roleId)
            ? roleId
            : AppServices.KuroAccounts.Current?.UserId;
        if (string.IsNullOrEmpty(key))
        {
            return;
        }
        // 与写入侧共用 IconDiskCacheService.AvatarCacheKey 的键构造规则
        var cached = AppServices.IconCache.GetCachedIconPath(
            AvatarCacheCategory, IconDiskCacheService.AvatarCacheKey(key, null));
        if (cached is not null)
        {
            AvatarUrl = cached;
        }
    }

    /// <summary>
    /// 解析玩家真实头像(对齐 Java WutheringWavesTool:接口取 URL → 本地磁盘缓存 → UI 只加载本地文件):
    /// 已知 URL 直接落盘缓存;URL 缺失(本地启动器 SDK 数据不带头像)时从库街区 gamer/role/list 的
    /// headPhotoUrl 补齐。成功后把 <see cref="AvatarUrl"/> 切换为本地文件路径(未命中缓存则用远程 URL 兜底)。
    /// 任何失败静默保留现有头像,不影响每日数据主流程。
    /// </summary>
    private async Task ResolveAvatarAsync(string? headUrl, string? roleId)
    {
        try
        {
            var url = headUrl;
            if (string.IsNullOrWhiteSpace(url))
            {
                // 本地启动器 SDK 的数据不带头像 URL:从库街区 gamer 角色列表补齐。
                // 必须按 roleId 命中当前展示的那个角色:直接取 [0] 会在多角色账号上拿错头像,
                // 而当前库街区账号名下没有这个角色时(绑定的是别的账号)宁可不换,避免串号。
                var account = AppServices.KuroAccounts.Current;
                if (account is null)
                {
                    return;
                }
                var gamer = await AppServices.Kuro.GetGamerAsync(account, (int)KuroGameType.Waves);
                var roles = gamer is { Code: 200, Data: not null } ? gamer.Data : null;
                if (roles is null || roles.Count == 0)
                {
                    return;
                }
                var role = string.IsNullOrEmpty(roleId)
                    ? roles[0]
                    : roles.FirstOrDefault(r => r.RoleId == roleId);
                if (role is null)
                {
                    return;
                }
                url = !string.IsNullOrWhiteSpace(role.HeadPhotoUrl) ? role.HeadPhotoUrl : role.GameHeadUrl;
            }
            if (string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            // 缓存 key 用游戏角色 UID(头像属于角色);拿不到角色时退化为 URL 文件名。
            // 键的构造收敛到 IconDiskCacheService.AvatarCacheKey,读/写两侧共用,避免再次漂移。
            var key = IconDiskCacheService.AvatarCacheKey(roleId, url);
            await AppServices.IconCache.CacheUrlAsync(AvatarCacheCategory, key, url);
            var local = AppServices.IconCache.GetCachedIconPath(AvatarCacheCategory, key);
            AvatarUrl = local ?? url;
            // 通知导航栏切换为真实账号头像(落盘后的本地路径;主页已用磁盘缓存,导航栏复用同一份)
            if (!string.IsNullOrEmpty(local))
            {
                WeakReferenceMessenger.Default.Send(new AvatarResolvedMessage(local));
            }
        }
        catch (Exception)
        {
            // 头像解析/下载失败:保留默认或已显示的头像
        }
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
            Name = LanguageService.Format("Home.ItemRadio"),
            ValueText = $"LV.{level:00}",
            SubText = progress is null ? null : LanguageService.Format("Home.RadioExp", progress.Cur, progress.Total),
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
