using System.Collections.ObjectModel;
using System.IO;
using System;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using McKuro.Core.Models.Game;
using McKuro.Core.Services.Game;
using McKuro.Core.Services.Settings;
using McKuro.Services;

namespace McKuro.ViewModels;

/// <summary>启动器页:游戏状态、检查更新、预下载、安装、启动、封面轮播。</summary>
public sealed partial class LauncherViewModel : ViewModelBase
{
    /// <summary>
    /// 底部状态栏文案。<b>初始为空</b>:此前初始值是「就绪」,而它是一个静态占位词 ——
    /// 没有任何操作发生时也一直挂在启动页左下角,既不传达信息、又让人以为还有事没做完
    /// (2026-10 用户反馈「一直显示就绪,这种没必要显示」)。状态栏只承载"操作结果"。
    /// </summary>
    [ObservableProperty]
    private string _statusText = "";

    /// <summary>
    /// 「游戏已是最新」这类**一次性结果提示**在状态栏停留多久后自动消失。
    /// <para>
    /// 为什么需要:状态栏是启动页所有操作的反馈出口,而「已是最新 / 已是最新版本 3.7.1」
    /// 这类"没有后续动作"的结论会一直挂在那儿 —— 用户切到别的页面再回来、或下次打开软件
    /// 都还看到同一句话,像一条永远清不掉的残留。它又不像"发现新版本"那样需要一直提醒
    /// (那个在启动器标题卡里另有常驻文案 <c>InstallStateText</c>)。
    /// </para>
    /// <para>
    /// 8 秒与全局悬浮提醒(活动临期/登录失效等)的停留时长一致,保持同一套节奏。
    /// </para>
    /// </summary>
    private static readonly TimeSpan TransientStatusLifetime = TimeSpan.FromSeconds(8);

    /// <summary>当前挂着的自动清除计时器;新提示到达或页面离开时取消。</summary>
    private Avalonia.Threading.DispatcherTimer? _transientStatusTimer;

    /// <summary>当前状态栏内容是否属于"会自动消失"的一次性提示。</summary>
    private bool _isStatusTransient;

    /// <summary>
    /// 仅在 <see cref="SetTransientStatus"/> 内部赋值期间为 true。
    /// <para>
    /// <see cref="OnStatusTextChanged"/> 靠它区分"这次赋值来自一次性提示"还是"普通赋值"。
    /// 旧实现拿 <see cref="_isStatusTransient"/> 判断,而它在一次性提示挂着的 8 秒里一直为 true,
    /// 于是这 8 秒内的普通赋值(如「正在修复…」)不会取消计时器,到点被一起清掉(M33)。
    /// </para>
    /// </summary>
    private bool _assigningTransient;

    /// <summary>
    /// 设一条"会自动消失"的状态提示,并把它标记为 transient。
    /// <para>
    /// <b>只用于没有后续动作的结论</b>(如「游戏已是最新」)。失败/进行中/需要用户处理的
    /// 提示仍走普通 <see cref="StatusText"/> 赋值,必须留在屏幕上 —— 把"检查失败"也自动清了,
    /// 用户就永远看不到错在哪。
    /// </para>
    /// </summary>
    private void SetTransientStatus(string text)
    {
        _assigningTransient = true;
        try
        {
            StatusText = text;
        }
        finally
        {
            _assigningTransient = false;
        }
        _isStatusTransient = true;
        StartTransientStatusTimer();
    }

    /// <summary>启动(或重置)自动清除计时器:到期后清空状态栏。</summary>
    private void StartTransientStatusTimer()
    {
        _transientStatusTimer?.Stop();
        _transientStatusTimer = new Avalonia.Threading.DispatcherTimer
        {
            Interval = TransientStatusLifetime,
        };
        _transientStatusTimer.Tick += (_, _) =>
        {
            _transientStatusTimer?.Stop();
            _transientStatusTimer = null;
            _isStatusTransient = false;
            StatusText = "";
        };
        _transientStatusTimer.Start();
    }

    /// <summary>取消自动清除(普通提示接管状态栏时调用,避免它被上一条的计时器误清)。</summary>
    private void CancelTransientStatusTimer()
    {
        _transientStatusTimer?.Stop();
        _transientStatusTimer = null;
        _isStatusTransient = false;
    }

    /// <summary>
    /// 任何对 <see cref="StatusText"/> 的直接赋值都会取消上一条的自动清除计时器。
    /// <para>
    /// 不取消的后果:先弹了「游戏已是最新」(8 秒后清),用户立刻点「修复游戏」,
    /// 状态栏换成「正在修复…」—— 8 秒一到,那条**正在修复**被清空,看起来像卡住了。
    /// </para>
    /// <para>
    /// 判据是 <see cref="_assigningTransient"/>(只在 SetTransientStatus 赋值那一刻为 true),
    /// 而不是 <see cref="_isStatusTransient"/>(整段 8 秒都为 true)。计时器到点清空时
    /// 已先把计时器置空,这里再取消一次是无副作用操作。
    /// </para>
    /// </summary>
    partial void OnStatusTextChanged(string value)
    {
        if (!_assigningTransient)
        {
            CancelTransientStatusTimer();
        }
    }

    [ObservableProperty]
    private string _gameVersionText = LanguageService.Format("Launcher.NotDetected");

    [ObservableProperty]
    private string _serverVersionText = "-";

    [ObservableProperty]
    private string _installStateText = LanguageService.Format("Launcher.NotInstalledShort");

    [ObservableProperty]
    private string _predownloadStateText = "";

    [ObservableProperty]
    private bool _isInstalled;

    [ObservableProperty]
    private bool _hasUpdate;

    [ObservableProperty]
    private bool _hasPredownload;

    /// <summary>预下载已完成(本地暂存完整,等待官方开放更新):按钮禁用并显示「预下载完成」。</summary>
    [ObservableProperty]
    private bool _predownloadCompleted;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isDownloading;

    /// <summary>
    /// 任务已启动但还在「获取资源列表」阶段(清单/索引尚未回来,总量未知)。
    /// <para>
    /// <b>为什么需要它:</b>预下载/安装/修复一进来就 <see cref="IsDownloading"/>=true、
    /// 进度置 0,而真正的进度要等 index.json + 清单加载完才有;弱网下这段可能十几秒到几分钟,
    /// 期间界面是一个恒定的 0% 死环,用户会以为卡死(对齐 Haiyu 1.4.5 的 IsTransferPreparing)。
    /// </para>
    /// </summary>
    [ObservableProperty]
    private bool _isPreparing;

    partial void OnIsPreparingChanged(bool value) => OnPropertyChanged(nameof(ShowIndeterminateProgress));

    /// <summary>进度环是否显示为不确定态(准备阶段 + 还没有任何真实进度)。</summary>
    public bool ShowIndeterminateProgress => IsPreparing;

    /// <summary>下载是否处于暂停(对齐 Haiyu DownloadState.IsPaused)。</summary>
    public bool DownloadPaused => AppServices.Downloader.IsPaused;

    /// <summary>暂停/继续按钮文案。</summary>
    public string PauseResumeText => DownloadPaused ? LanguageService.Format("Launcher.Resume") : LanguageService.Format("Launcher.Pause");

    /// <summary>
    /// 当前进度阶段是否可暂停,由进度回调按 <see cref="DownloadProgress.CanPause"/> 驱动。
    /// 校验本地文件 / 合成分组差分(hpatchz 外部进程)/ 解压 / 资源安装等阶段不产生可暂停的
    /// 字节读取,暂停是空操作,此时必须隐藏暂停按钮(对齐上游 IProgressSetup.CanPause)。
    /// </summary>
    [ObservableProperty]
    private bool _canPauseCurrentPhase;

    partial void OnCanPauseCurrentPhaseChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowPauseResume));
        OnPropertyChanged(nameof(PreDownloadButtonVisible));
    }

    /// <summary>是否在真正可暂停的下载阶段(下载进行中且当前阶段允许暂停)。</summary>
    public bool ShowPauseResume => IsDownloading && CanPauseCurrentPhase;

    /// <summary>
    /// 合并下载按钮是否可见:未下载时显示(预下载/预下载完成),
    /// 下载中只在可暂停的下载阶段显示暂停/继续,非下载阶段整体隐藏。
    /// </summary>
    public bool PreDownloadButtonVisible => !IsDownloading || CanPauseCurrentPhase;

    /// <summary>合并按钮文案:未下载时"预下载"(已完成显示"预下载完成"),下载中"暂停下载"/"继续下载"。</summary>
    public string PreDownloadButtonText => IsDownloading
        ? (DownloadPaused ? LanguageService.Format("Launcher.Resume") : LanguageService.Format("Launcher.Pause"))
        : PredownloadCompleted ? LanguageService.Format("Launcher.PredownloadDone") : LanguageService.Format("Launcher.Predownload");

    /// <summary>合并按钮是否可用:未下载时有预下载可用(已完成则禁用);下载中始终可用。</summary>
    public bool PreDownloadButtonEnabled => IsDownloading || (HasPredownload && !PredownloadCompleted);

    partial void OnIsDownloadingChanged(bool value)
    {
        OnPropertyChanged(nameof(PreDownloadButtonText));
        OnPropertyChanged(nameof(PreDownloadButtonEnabled));
        OnPropertyChanged(nameof(ShowPauseResume));
        OnPropertyChanged(nameof(PreDownloadButtonVisible));
        OnPropertyChanged(nameof(IsPreDownloadActive));
        OnPropertyChanged(nameof(ShowDownloadCard));
        // 下载/安装期间隐藏资源等级小按钮/面板/画质包面板(与进度卡同列,避免重叠)
        OnPropertyChanged(nameof(ShowResourceLevelPill));
        OnPropertyChanged(nameof(ShowResourceLevelPanel));
        OnPropertyChanged(nameof(ShowPackManager));
        NotifyOperationStateChanged();
    }

    partial void OnIsBusyChanged(bool value) => NotifyOperationStateChanged();

    /// <summary>
    /// 是否有长操作在进行:检查更新(IsBusy)/ 安装·预下载·修复(IsDownloading)/
    /// 画质切换 / 删除画质包。
    /// <para>
    /// <b>所有长操作共用这一道门</b>(M36):旧实现里检查更新只看 IsBusy、下载类只看 IsDownloading、
    /// 选择版本完全不看,于是下载中回到本页会触发静默检查改写 HasUpdate/PredownloadCompleted,
    /// 选择版本也可能在安装中途写版本记录。新操作入口统一判 <c>IsOperationRunning</c>。
    /// </para>
    /// </summary>
    public bool IsOperationRunning =>
        IsBusy || IsDownloading || IsSwitchingResourceLevel || IsDeletingResourcePack;

    /// <summary>能否发起新的长操作(菜单项「检查更新」与安装按钮的 IsEnabled)。</summary>
    public bool CanStartOperation => !IsOperationRunning;

    /// <summary>已安装且没有长操作在进行(菜单项「选择版本/修复/检查重复文件」的 IsEnabled)。</summary>
    public bool CanStartInstalledOperation => IsInstalled && !IsOperationRunning;

    /// <summary>任一忙碌标志变化后统一刷新依赖它们的计算属性(否则绑定不会更新,L26)。</summary>
    private void NotifyOperationStateChanged()
    {
        OnPropertyChanged(nameof(IsOperationRunning));
        OnPropertyChanged(nameof(CanStartOperation));
        OnPropertyChanged(nameof(CanStartInstalledOperation));
        OnPropertyChanged(nameof(ResourceLevelEnabled));
        UpdateLaunchButtonEnabled();
    }

    partial void OnHasPredownloadChanged(bool value)
    {
        OnPropertyChanged(nameof(PreDownloadButtonEnabled));
        OnPropertyChanged(nameof(ShowPreDownloadRing));
        OnPropertyChanged(nameof(ShowDownloadCard));
    }

    partial void OnPredownloadCompletedChanged(bool value)
    {
        OnPropertyChanged(nameof(PreDownloadButtonText));
        OnPropertyChanged(nameof(PreDownloadButtonEnabled));
    }

    /// <summary>是否显示下载/安装进度卡片:有下载/安装活动或有预下载可领取时才显示。</summary>
    public bool ShowDownloadCard => IsDownloading || HasPredownload;

    /// <summary>是否正在预下载/下载(显示进度环)。</summary>
    public bool IsPreDownloadActive => IsDownloading || HasPredownload;

    /// <summary>是否显示预下载进度环(有预下载或正在下载)。</summary>
    public bool ShowPreDownloadRing => IsDownloading || HasPredownload;

    /// <summary>合并下载按钮命令:未下载时触发预下载,下载中切换暂停/继续(已完成预下载时不响应)。</summary>
    [RelayCommand]
    private void TogglePreDownload()
    {
        if (IsDownloading)
        {
            // 非下载阶段(校验/差分合成/解压/资源安装)没有可暂停的字节读取,
            // 切换暂停只会留下一个污染后续下载批次的暂停门 → 直接忽略。
            if (!CanPauseCurrentPhase)
            {
                return;
            }
            if (AppServices.Downloader.IsPaused)
            {
                AppServices.Downloader.Resume();
            }
            else
            {
                AppServices.Downloader.Pause();
            }
            OnPropertyChanged(nameof(PreDownloadButtonText));
            OnPropertyChanged(nameof(PauseResumeText));
            OnPropertyChanged(nameof(DownloadPaused));

            // 暂停/恢复都要清掉剩余时间与速度读数:
            // 暂停期间速度窗口里的字节增量趋零,ETA 会被算成越来越大(甚至"剩余 8 小时"),
            // 而实际只是没在下。恢复后由下一次 HandleProgress 自然重算。
            // (原先这段只在无人绑定的 TogglePauseDownload 里,已合并到这里并删除那份副本,L28。)
            RemainingTimeText = "";
            ClearSpeedDisplay();
        }
        else if (!PredownloadCompleted)
        {
            PreDownloadCommand.Execute(null);
        }
    }

    /// <summary>已安装且有更新:显示「安装更新」按钮(未安装时只显示「下载安装」)。</summary>
    public bool ShowInstallUpdate => IsInstalled && HasUpdate;

    /// <summary>无更新时显示「启动游戏」按钮;有更新时由「安装更新」按钮占据同一位置。</summary>
    public bool ShowLaunchButton => NativeGameManagementSupported && IsInstalled && !HasUpdate;

    /// <summary>启动按钮文案:启动游戏 / 启动中 / 游戏中(由 GameProcessMonitor 状态驱动,对齐 Haiyu 启动按钮状态)。</summary>
    [ObservableProperty]
    private string _launchButtonText = LanguageService.Format("Launcher.Launch");

    /// <summary>启动按钮可用:游戏中/启动中禁用,防止重复启动。</summary>
    [ObservableProperty]
    private bool _launchButtonEnabled = true;

    /// <summary>操作区是否显示修复按钮。</summary>
    public bool ShowRepairButton => NativeGameManagementSupported;

    public bool ShowInstallButton => NativeGameManagementSupported && !IsInstalled;

    partial void OnIsInstalledChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowInstallUpdate));
        OnPropertyChanged(nameof(ShowLaunchButton));
        OnPropertyChanged(nameof(ShowInstallButton));
        OnPropertyChanged(nameof(CanStartInstalledOperation));
        UpdateLaunchButtonEnabled();
    }

    /// <summary>
    /// 是否有操作正在改写游戏文件(安装/预下载/修复/画质切换都置 IsDownloading,外加删除画质包)。
    /// 此时启动游戏会锁住待替换文件或运行写了一半的客户端(M35)。
    /// 只读的检查更新(IsBusy)不算:它不碰游戏文件,不该让启动按钮在进页时闪一下灰。
    /// </summary>
    private bool IsModifyingGameFiles => IsDownloading || IsDeletingResourcePack;

    /// <summary>启动按钮可用性:已安装、无正在进行的游戏会话、且没有操作在改写游戏文件。</summary>
    private void UpdateLaunchButtonEnabled()
    {
        LaunchButtonEnabled = IsInstalled
            && !IsModifyingGameFiles
            && AppServices.GameMonitor.State == GameSessionState.Idle;
    }

    partial void OnHasUpdateChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowInstallUpdate));
        OnPropertyChanged(nameof(ShowLaunchButton));
    }

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private string _progressText = "";

    [ObservableProperty]
    private long _updateBytes;

    [ObservableProperty]
    private string _currentFileText = "";

    /// <summary>
    /// 当前下载速度的「大字数值」(如 <c>37.78</c>)。单位单独放 <see cref="SpeedUnitText"/>,
    /// 由 XAML 排成「大号数值 + 小号单位」(对齐 FlClash 仪表盘;用户反馈网速不明显)。
    /// </summary>
    [ObservableProperty]
    private string _speedValueText = "--";

    /// <summary>当前下载速度的单位(如 <c>MB/s</c>);无速度时为空串。</summary>
    [ObservableProperty]
    private string _speedUnitText = "";

    /// <summary>
    /// 本次任务的速度峰值(如 <c>峰值 42.1 MB/s</c>)。有峰值才能判断"现在是不是掉速了",
    /// 只给瞬时值用户看不出网络波动。
    /// </summary>
    [ObservableProperty]
    private string _peakSpeedText = "";

    [ObservableProperty]
    private string _bytesText = "";

    /// <summary>下载总大小预估(如 "12.5 GB",参考 Haiyu Config.Size)。</summary>
    [ObservableProperty]
    private string _downloadSizeText = "";

    /// <summary>磁盘空间预估(如 "需要 15 GB,可用 80 GB",参考 Haiyu UnCompressSize + DriveInfo)。</summary>
    [ObservableProperty]
    private string _diskSpaceText = "";

    /// <summary>磁盘空间是否不足(空间不足时置 true,显示警告色)。</summary>
    [ObservableProperty]
    private bool _diskSpaceWarning;

    /// <summary>预估剩余时间(如 "剩余约 15 分钟",按当前速度估算)。</summary>
    [ObservableProperty]
    private string _remainingTimeText = "";

    /// <summary>实时下载速度历史(每秒一点,单位 MB/s;供 SpeedTrendChart 画波动曲线,参考 Haiyu DownloadSpeedPoints)。</summary>
    public ObservableCollection<double> DownloadSpeedHistory { get; } = [];

    // 速度曲线采集:距上次采集时间,用于 1s 采样节流
    private DateTime _lastSpeedSample = DateTime.MinValue;

    // 安装进度 UI 节流:下载线程可以高频上报,界面最多每 500ms 刷新一次
    private DateTime _lastInstallUiUpdate = DateTime.MinValue;

    /// <summary>封面轮播图(官方启动器信息)。</summary>
    public ObservableCollection<SlideshowItem> Slideshows { get; } = [];

    /// <summary>轮播图是否显示(可隐藏,参考 Haiyu 左下角卡片)。</summary>
    [ObservableProperty]
    private bool _isSlideShowVisible = true;

    /// <summary>切换轮播图显示/隐藏。</summary>
    [RelayCommand]
    private void ToggleSlideShow() => IsSlideShowVisible = !IsSlideShowVisible;

    /// <summary>
    /// 双击轮播图在默认浏览器中打开该图的跳转链接(官方 slideshow 的 jumpUrl)。
    /// 无链接时不做任何事,并给出提示,避免用户以为"双击没反应"。
    /// </summary>
    [RelayCommand]
    private void OpenSlideshowLink(SlideshowItem? item)
    {
        if (item is null || string.IsNullOrWhiteSpace(item.JumpUrl))
        {
            StatusText = LanguageService.Format("Launcher.SlideNoLink");
            return;
        }

        // JumpUrl 来自服务端:只放行 http/https(file:/UNC/ms-msdt: 等协议处理器一律拒绝,M32)
        if (!SafeUrl.TryOpen(item.JumpUrl))
        {
            StatusText = LanguageService.Format("Status.OpenWebFailed");
        }
    }

    /// <summary>公告列表。</summary>
    public ObservableCollection<AnnouncementItem> Notices { get; } = [];

    /// <summary>活动列表。</summary>
    public ObservableCollection<AnnouncementItem> Activities { get; } = [];

    /// <summary>新闻列表。</summary>
    public ObservableCollection<AnnouncementItem> News { get; } = [];

    /// <summary>背景首帧图 URL(视频加载前的静态封面,空则无背景)。</summary>
    [ObservableProperty]
    private string _backgroundImageUrl = "";

    /// <summary>背景视频 URL(空则无视频,当前静态显示首帧图)。</summary>
    [ObservableProperty]
    private string _backgroundVideoUrl = "";

    /// <summary>版本 Logo 图 URL(启动按钮旁,空则不显示)。</summary>
    [ObservableProperty]
    private string _versionLogoUrl = "";

    /// <summary>背景视频开关(用户设置)。</summary>
    [ObservableProperty]
    private bool _videoEnabled = true;

    /// <summary>
    /// 自定义动态壁纸是否生效(视频开启 + mode=1 + 文件存在)。
    /// 官方版本标语(诗句文案图)是为官方宣传视频定制的美术叠加,与自定义壁纸不匹配 → 生效时隐藏。
    /// 由 <see cref="ApplyCustomVideoOverride"/> 在页面刷新/官方背景加载后同步。
    /// </summary>
    [ObservableProperty]
    private bool _isCustomVideoActive;

    public bool NativeGameManagementSupported => AppServices.Capabilities.SupportsNativeGameManagement;

    public string PlatformGameNotice => AppServices.Capabilities.GameSupportText;

    /// <summary>上一次导航到本页时生效的渠道(用于检测设置页改过渠道,决定要不要重载官方内容)。</summary>
    private GameServerType _lastResolvedServer = GameServerType.Unknown;

    private UpdateCheckResult? _lastCheck;

    /// <summary>最近一次加载官方背景时使用的背景视频开关(导航时据此判断是否需要重新加载背景)。</summary>
    private bool _lastLoadedVideoSetting;

    /// <summary>
    /// 官方侧最近一次成功取到的背景视频 URL(空 = 官方无视频或尚未加载成功)。
    /// 「自定义 → 官方」的撤回路径靠它就地恢复:没有这份缓存就只能等下一次网络加载,
    /// 而用户切换来源时并不会触发加载(见 <see cref="OnNavigatedTo"/>)。
    /// </summary>
    private string _officialBackgroundVideoUrl = "";

    /// <summary>官方背景是否为视频(backgroundFileType == 2 且 URL 非空)。</summary>
    private bool _officialBackgroundHasVideo;

    /// <summary>官方首帧占位图 URL(撤回官方时就地恢复)。</summary>
    private string _officialBackgroundImageUrl = "";

    /// <summary>官方背景是否已成功加载过一次(未加载过时不覆盖当前静态图,避免白屏)。</summary>
    private bool _officialBackgroundKnown;

    /// <summary>
    /// 最近一次应用视频来源时的设置指纹(开关 + 来源 + 自定义路径),见 <see cref="VideoSourceFingerprint"/>。
    /// 只比「开关」会漏掉「开关不变、只把来源从自定义改回官方」这一整类变化。
    /// </summary>
    private string _lastAppliedVideoSource = "";

    public LauncherViewModel()
    {
        // 渠道唯一来源是设置(启动页不再单独维护一份下拉框 —— 已删掉那份从未被任何
        // 控件绑定的 Servers/SelectedServerIndex 死代码)。切换入口只在设置页。
        _lastResolvedServer = ServerType;
        RefreshState();
        VideoEnabled = AppServices.Settings.Current.BackgroundVideoEnabled;
        _lastLoadedVideoSetting = AppServices.Settings.Current.BackgroundVideoEnabled;
        _lastAppliedVideoSource = VideoSourceFingerprint(AppServices.Settings.Current);
        _ = LoadLauncherInfoAsync();

        // 游戏进程监控:状态驱动启动按钮文案(启动中→游戏中→启动游戏)与游戏结束动作
        AppServices.GameMonitor.StateChanged += OnGameSessionStateChanged;
        AppServices.GameMonitor.SessionEnded += OnGameSessionEnded;
        UpdateLaunchButtonEnabled();

        // 订阅游戏目录变更(设置页选择目录后) → 自动识别加载
        WeakReferenceMessenger.Default.Register<LauncherViewModel, GameDirectoryChangedMessage>(this, static (r, m) =>
        {
            Dispatcher.UIThread.Post(() => r.OnGameDirectoryChanged(m.Value));
        });

        // 已设置目录则自动检查更新(对齐 Haiyu 加载后自动检查)。
        // 静默:结论落在标题卡/按钮/版本号,不刷状态栏(用户反馈"每次进页都弹一句已是最新")。
        if (IsInstalled)
        {
            _ = CheckUpdateSilentlyAsync();
        }
    }

    /// <summary>游戏目录变更处理:重新识别并自动加载(对齐 Haiyu 选目录后的自动识别)。</summary>
    private void OnGameDirectoryChanged(string gameRoot)
    {
        RefreshState();
        _ = LoadLauncherInfoAsync();
        if (IsInstalled)
        {
            _ = CheckUpdateSilentlyAsync();
        }
        else
        {
            StatusText = string.IsNullOrEmpty(gameRoot) ? LanguageService.Format("Launcher.NoGameDir") : LanguageService.Format("Launcher.DirInvalid", GamePathResolver.ExeRootName);
        }
    }

    /// <summary>
    /// 导航到启动页时调用:同步渠道/背景视频来源,配置变化时重新加载官方背景(无需重启)。
    /// </summary>
    public void OnNavigatedTo()
    {
        var s = AppServices.Settings.Current;

        // 离开再回来时,上一页留下的"已是最新"之类一次性提示不该跟着回来
        // (它本来就是"刚才那次检查"的结论,不是当前状态)。
        // 但**只清一次性提示**:「请先设置游戏目录」「当前平台不支持」这类
        // 需要用户处理的提示必须留着,清了就等于什么反馈都没给。
        if (_isStatusTransient && !IsBusy && !IsDownloading)
        {
            CancelTransientStatusTimer();
            StatusText = "";
        }

        // 服务器渠道可能在设置页变更:同步到启动页(渠道影响官方背景/轮播/更新检查/画质面板)。
        // 这里比较「解析后的渠道」而不是界面索引 —— 渠道的唯一定义在 GameServerResolver,
        // 启动页不再保留自己的那份下拉选择。
        var resolved = ServerType;
        var serverChanged = _lastResolvedServer != resolved;
        _lastResolvedServer = resolved;

        // 视频来源指纹(开关 + 官方/自定义 + 自定义路径)可能在设置页变更
        var wantSource = VideoSourceFingerprint(s);
        var sourceChanged = wantSource != _lastAppliedVideoSource;
        _lastAppliedVideoSource = wantSource;

        // 视频开关开/关变化 → 重新加载官方背景(视频 URL/首帧图/轮播),无需重启。
        // 注意:仅「来源」变化(自定义 ⇄ 官方,开关不变)不足以重新加载 ——
        // 官方 URL 往往与上次一致(LocalLauncherInfo 有缓存),重载不产生可观察变化,
        // 撤回官方要靠 ApplyCustomVideoOverride 就地恢复缓存的官方 URL 与首帧图。
        var wantVideo = s.BackgroundVideoEnabled;
        // 官方背景若尚未成功加载过(没有可恢复的缓存),来源变化时也要补一次加载(见下方注释)。
        // 两个条件合并成一次调用:旧实现分两处各发一次,同一次导航可能并发两次加载(L25)。
        if (wantVideo != _lastLoadedVideoSetting || serverChanged || (sourceChanged && !_officialBackgroundKnown))
        {
            _lastLoadedVideoSetting = wantVideo;
            _ = LoadLauncherInfoAsync();
        }

        // 即时同步开关(VideoBackgroundControl 的 IsVideoEnabled 绑定会自动响应)
        if (VideoEnabled != wantVideo)
        {
            VideoEnabled = wantVideo;
        }

        // 来源变化(尤其自定义 → 官方)必须在这里收敛到最终状态:
        // 旧实现只在 customActive 时改动视频,撤回官方时不碰 BackgroundVideoUrl,
        // 于是自定义路径一直留着 —— 只有「关掉再打开视频」触发 LoadLauncherInfoAsync
        // (其间把 BackgroundVideoUrl 清空重设)才会恢复官方视频(用户报告的现象)。
        // 官方背景若尚未成功加载过(没有可恢复的缓存),则补一次加载(已并入上面的那次调用)。
        ApplyCustomVideoOverride();

        // 下载/修复/画质切换期间不做自动检查:检查结果会改写 HasUpdate/PredownloadCompleted,
        // 可能让预下载按钮在下载途中重新出现(M36)。
        if (IsInstalled && !IsOperationRunning)
        {
            _ = CheckUpdateSilentlyAsync();
        }

        // 资源等级面板随渠道/安装状态刷新(渠道决定端点是否可用)
        _ = RefreshResourceLevelsAsync();
    }

    /// <summary>
    /// 背景视频最终状态(由 <see cref="ResolveBackgroundState"/> 计算,<see cref="ApplyCustomVideoOverride"/> 落地)。
    /// </summary>
    /// <param name="VideoUrl">应播放的视频 URL(空串 = 无视频,显示首帧图);<c>null</c> = 保持当前值不动。</param>
    /// <param name="ImageUrl">首帧占位图 URL;<c>null</c> = 保持当前值不动。</param>
    /// <param name="VideoEnabled">视频控件开关。</param>
    /// <param name="IsCustomActive">自定义壁纸是否生效(官方版本标语据此隐藏)。</param>
    public readonly record struct BackgroundState(
        string? VideoUrl, string? ImageUrl, bool VideoEnabled, bool IsCustomActive);

    /// <summary>
    /// 依据设置与官方背景缓存算出背景最终状态(<b>生产判定点</b>,供启动页与回归测试共用)。
    /// <para>
    /// 关键契约:<b>自定义失效时必须回落到官方</b>。旧实现在 mode≠1 时什么都不做,
    /// 于是「自定义 → 官方」切换后 <c>BackgroundVideoUrl</c> 仍是自定义路径,
    /// 只有「关掉再打开视频」触发一次完整重载才会恢复 —— 这就是用户报障的根因。
    /// </para>
    /// </summary>
    /// <param name="s">当前设置。</param>
    /// <param name="customFileExists">自定义视频文件是否存在(生产传 File.Exists)。</param>
    /// <param name="officialKnown">官方背景是否已成功加载过一次;false 时不动当前静态图(避免白屏)。</param>
    /// <param name="officialVideoUrl">官方视频 URL(空 = 官方无视频)。</param>
    /// <param name="officialImageUrl">官方首帧图 URL。</param>
    /// <param name="officialHasVideo">官方背景是否为视频。</param>
    public static BackgroundState ResolveBackgroundState(
        AppSettings s,
        bool customFileExists,
        bool officialKnown,
        string officialVideoUrl,
        string officialImageUrl,
        bool officialHasVideo)
    {
        var customActive = s.BackgroundVideoEnabled && s.BackgroundVideoMode == 1 && customFileExists;
        if (customActive)
        {
            return new BackgroundState(
                VideoUrl: s.CustomBackgroundVideoPath ?? "",
                ImageUrl: null, // 自定义视频没有官方首帧图:保持当前值不动
                VideoEnabled: true,
                IsCustomActive: true);
        }

        if (!officialKnown)
        {
            // 官方背景尚未加载过:只保证开关反映设置,不动 URL(等异步加载落地)
            return new BackgroundState(
                VideoUrl: null,
                ImageUrl: null,
                VideoEnabled: s.BackgroundVideoEnabled && officialHasVideo,
                IsCustomActive: false);
        }

        return new BackgroundState(
            VideoUrl: officialVideoUrl,
            ImageUrl: officialImageUrl,
            VideoEnabled: s.BackgroundVideoEnabled && officialHasVideo,
            IsCustomActive: false);
    }

    /// <summary>
    /// 自定义动态壁纸覆盖(双向收敛):mode=1 且文件存在 → 用本地/Wallpaper Engine 视频替代官方 URL;
    /// 否则 → 就地恢复官方背景(视频 URL / 首帧图 / 视频开关)。
    /// <para>
    /// <b>必须在「撤回官方」方向上也生效</b>:旧实现只在 customActive 时改动视频属性,
    /// 用户把来源从「自定义」改回「官方」时 BackgroundVideoUrl 仍是自定义路径,
    /// 而 OnNavigatedTo 只按「视频开关」判断是否重载官方背景 —— 开关没变就不重载,
    /// 于是官方视频迟迟不出现,必须「关掉视频再打开」触发一次 LoadLauncherInfoAsync
    /// (其间清空并重设 BackgroundVideoUrl)才恢复。版本标语(VersionLogoUrl 的 IsVisible)
    /// 走 IsCustomVideoActive,同一路径里立即翻转,所以文本立刻生效而视频不生效。
    /// </para>
    /// 官方背景异步加载完成后与页面刷新时各应用一次。
    /// </summary>
    private void ApplyCustomVideoOverride()
    {
        var s = AppServices.Settings.Current;
        var state = ResolveBackgroundState(
            s,
            customFileExists: !string.IsNullOrWhiteSpace(s.CustomBackgroundVideoPath)
                              && File.Exists(s.CustomBackgroundVideoPath),
            officialKnown: _officialBackgroundKnown,
            officialVideoUrl: _officialBackgroundVideoUrl,
            officialImageUrl: _officialBackgroundImageUrl,
            officialHasVideo: _officialBackgroundHasVideo);

        // 顺序:先静态图,再视频 URL,最后开关 —— 保证最后一次属性变更即最终状态
        if (state.ImageUrl is { Length: > 0 } img && BackgroundImageUrl != img)
        {
            BackgroundImageUrl = img;
        }
        if (state.VideoUrl is { } url && BackgroundVideoUrl != url)
        {
            BackgroundVideoUrl = url;
        }
        if (VideoEnabled != state.VideoEnabled)
        {
            VideoEnabled = state.VideoEnabled;
        }
        IsCustomVideoActive = state.IsCustomActive;
    }

    /// <summary>
    /// 视频来源指纹(<b>生产判定点</b>,供启动页与回归测试共用):
    /// 开关 + 来源模式 + 自定义路径。只比较开关会漏掉「开关不变、只把来源从自定义改回官方」
    /// 这一类切换 —— 正是"官方视频要关掉再打开才生效"的漏判入口。
    /// </summary>
    public static string VideoSourceFingerprint(AppSettings s) =>
        $"{(s.BackgroundVideoEnabled ? 1 : 0)}|{s.BackgroundVideoMode}|{s.CustomBackgroundVideoPath ?? ""}";

    /// <summary>
    /// 当前生效渠道:<b>设置优先、自动检测兜底</b>(判定收敛在
    /// <see cref="GameServerResolver.Resolve"/> 一处,避免各页面各写一份而漂移)。
    /// </summary>
    private GameServerType ServerType => GameServerResolver.Resolve(AppServices.Settings, AppServices.Paths);

    /// <summary>官方内容加载请求序号:只有最新一次加载能落地结果(L25)。</summary>
    private int _launcherInfoRequestId;

    /// <summary>拉取官方封面轮播图、公告与背景封面(失败静默,不影响其余功能)。</summary>
    private async Task LoadLauncherInfoAsync()
    {
        // 切换区服/开关视频会连续触发加载:旧区服的请求可能最后才回来,把新区服的轮播/背景覆盖掉。
        // 每次 await 之后比对序号,过期就直接丢弃。
        var requestId = ++_launcherInfoRequestId;
        try
        {
            var server = ServerType;
            var info = await AppServices.LauncherInfo.GetLauncherInfoAsync(server);
            if (requestId != _launcherInfoRequestId)
            {
                return;
            }
            if (info is not null)
            {
                Slideshows.Clear();
                if (info.Slideshow is not null)
                {
                    foreach (var slide in info.Slideshow)
                    {
                        if (!string.IsNullOrWhiteSpace(slide.Url))
                        {
                            Slideshows.Add(slide);
                        }
                    }
                }

                Notices.Clear();
                Activities.Clear();
                News.Clear();
                var guidance = info.Guidance;
                if (guidance is not null)
                {
                    if (guidance.Notice?.Contents is { } notices)
                    {
                        foreach (var n in notices)
                        {
                            Notices.Add(n);
                        }
                    }

                    if (guidance.Activity?.Contents is { } acts)
                    {
                        foreach (var a in acts)
                        {
                            Activities.Add(a);
                        }
                    }

                    if (guidance.News?.Contents is { } news)
                    {
                        foreach (var n in news)
                        {
                            News.Add(n);
                        }
                    }
                }
            }

            // 自定义壁纸生效需在设置官方视频前判定:生效则直接跳过官方视频 URL,
            // 避免"官方 URL → 自定义路径"同栈连续两次 TryStartVideo(旧版跨会话竞态的固定触发点)
            var sNow = AppServices.Settings.Current;
            var customActive = sNow.BackgroundVideoEnabled
                               && sNow.BackgroundVideoMode == 1
                               && File.Exists(sNow.CustomBackgroundVideoPath);

            // 背景封面(首帧图 + 视频 URL + 版本 Logo)
            var background = await AppServices.LauncherInfo.GetLauncherBackgroundAsync(server);
            if (requestId != _launcherInfoRequestId)
            {
                return;
            }
            if (background is not null)
            {
                // 先记录官方侧真相(与是否 customActive 无关):撤回官方时 ApplyCustomVideoOverride
                // 要就地恢复这份数据,不能因为当前自定义生效就把官方 URL 丢掉
                _officialBackgroundVideoUrl = "";
                _officialBackgroundImageUrl = string.IsNullOrWhiteSpace(background.FirstFrameImage)
                    ? ""
                    : background.FirstFrameImage;
                _officialBackgroundHasVideo = false;

                BackgroundVideoUrl = "";
                if (!string.IsNullOrWhiteSpace(background.FirstFrameImage))
                {
                    BackgroundImageUrl = background.FirstFrameImage;
                }

                if (!customActive)
                {
                    if (background.BackgroundFileType == 2 && !string.IsNullOrWhiteSpace(background.BackgroundFile))
                    {
                        BackgroundVideoUrl = background.BackgroundFile;
                        _officialBackgroundVideoUrl = background.BackgroundFile;
                        _officialBackgroundHasVideo = true;
                    }
                    else if (background.BackgroundFileType == 1 && !string.IsNullOrWhiteSpace(background.BackgroundFile))
                    {
                        // 官方接口偶尔只返回静态背景文件，仍然作为封面显示。
                        BackgroundImageUrl = background.BackgroundFile;
                        _officialBackgroundImageUrl = background.BackgroundFile;
                    }
                }
                else
                {
                    // 自定义生效时也要把官方视频 URL 记下来(供之后撤回官方用)
                    if (background.BackgroundFileType == 2 && !string.IsNullOrWhiteSpace(background.BackgroundFile))
                    {
                        _officialBackgroundVideoUrl = background.BackgroundFile;
                        _officialBackgroundHasVideo = true;
                    }
                }

                _officialBackgroundKnown = true;

                VideoEnabled = !customActive && _officialBackgroundHasVideo
                    && AppServices.Settings.Current.BackgroundVideoEnabled;

                if (!string.IsNullOrWhiteSpace(background.Slogan))
                {
                    VersionLogoUrl = background.Slogan;
                }
            }

            // 官方背景就绪后再叠加自定义覆盖(异步完成时机可能晚于用户在设置页的选择)
            ApplyCustomVideoOverride();
        }
        catch (Exception)
        {
            // 静默失败
        }
        finally
        {
            // 记录本次加载使用的背景视频开关,供下次导航判断是否需要重新加载背景
            _lastLoadedVideoSetting = AppServices.Settings.Current.BackgroundVideoEnabled;
        }
    }

    private void RefreshState()
    {
        if (!NativeGameManagementSupported)
        {
            IsInstalled = false;
            HasUpdate = false;
            HasPredownload = false;
            PredownloadCompleted = false;
            ServerVersionText = "-";
            PredownloadStateText = "";
            GameVersionText = LanguageService.Format("Launcher.PlatformUnsupported");
            InstallStateText = PlatformGameNotice;
            // 作废仍在后台跑的组件探测,免得它回来后把文案写回去
            _graphicsRequestId++;
            GraphicsComponentsText = "";
            OnPropertyChanged(nameof(GraphicsComponentsText));
            return;
        }

        var paths = AppServices.Paths;
        // 「安装未完成」优先于 exe 存在性:下载/安装被打断时根 exe 可能已落盘,
        // 若据此判为已安装,按钮会变成「启动游戏」—— 既没有续传入口、点启动也必然失败。
        // 判为未安装后,按钮回到「下载安装」,InstallAsync 会复用已下文件继续(下载器按 .part 续传)。
        var installIncomplete = AppServices.GameUpdater.HasIncompleteInstall();
        IsInstalled = paths.IsGameInstalled && !installIncomplete;
        GameVersionText = IsInstalled ? LanguageService.Format("Launcher.Installed") : LanguageService.Format("Launcher.NotInstalledShort");
        // 三种状态的文案:已装 / 上次没装完(说明会续传,避免用户以为要从头下几十 GB) / 从未安装
        InstallStateText = IsInstalled
            ? LanguageService.Format("Launcher.GameReady")
            : installIncomplete
                ? LanguageService.Format("Launcher.InstallIncomplete")
                : LanguageService.Format("Launcher.NotInstalled");
        // 组件探测要读游戏目录:后台执行,RefreshState 不等它(H10)
        _ = RefreshGraphicsComponentsAsync();

        if (!IsInstalled)
        {
            HasUpdate = false;
            HasPredownload = false;
            PredownloadCompleted = false;
            ServerVersionText = "-";
            PredownloadStateText = "";
        }
    }

    /// <summary>DLSS/XeSS 组件版本显示(对齐 Haiyu GetLocalDLSSAsync)。</summary>
    public string GraphicsComponentsText { get; private set; } = "";

    /// <summary>组件探测请求序号:连续 RefreshState 时只落地最后一次结果。</summary>
    private int _graphicsRequestId;

    /// <summary>
    /// 刷新 DLSS/XeSS 组件版本。
    /// <para>
    /// 旧实现在 UI 线程同步调用 <c>GetLocalGraphicsComponentVersions()</c>,
    /// RefreshState 每次都会卡在读盘上(构造、换目录、安装/修复后都会调,H10)。
    /// 现在走 Core 的后台版本,结果回到 UI 线程再写绑定属性。
    /// </para>
    /// </summary>
    private async Task RefreshGraphicsComponentsAsync()
    {
        var requestId = ++_graphicsRequestId;
        string text;
        try
        {
            var versions = await AppServices.GameUpdater.GetLocalGraphicsComponentVersionsAsync();
            text = versions.Count == 0
                ? ""
                : string.Join(" · ", versions.Select(v => $"{v.DisplayName} v{v.Version}"));
        }
        catch (Exception)
        {
            // 组件版本只是展示信息,读不到就不显示
            text = "";
        }

        void Apply()
        {
            if (requestId != _graphicsRequestId)
            {
                return;
            }
            GraphicsComponentsText = text;
            OnPropertyChanged(nameof(GraphicsComponentsText));
        }

        // 调用方通常在 UI 线程(await 会回到这里),但构造期可能没有同步上下文:统一保证在 UI 线程落地
        if (Dispatcher.UIThread.CheckAccess())
        {
            Apply();
        }
        else
        {
            Dispatcher.UIThread.Post(Apply);
        }
    }

    [RelayCommand]
    private Task CheckUpdateAsync() => CheckUpdateCoreAsync(interactive: true);

    /// <summary>
    /// 安装/修复/选择版本收尾时的复查:调用方自己就是那个"正在进行的长操作"(IsDownloading 仍为 true),
    /// 必须绕过操作门,否则这一步会被自己挡掉。
    /// </summary>
    private Task CheckUpdateAfterOperationAsync() => CheckUpdateCoreAsync(interactive: true, insideOperation: true);

    /// <summary>
    /// 自动触发的检查更新(进页/选完目录/安装完):<b>不写状态栏</b>。
    /// <para>
    /// <b>为什么区分 interactive:</b>「切换到启动页就弹一句"游戏已是最新"再消失」是用户明确
    /// 反馈的扰民行为 —— 每次进页都刷一次同样的结论,既没有信息量、又让状态栏看起来在抖。
    /// 自动检查的结论该落在<b>常驻</b>位置(启动器标题卡 / 按钮 / 版本号),那里本来就有;
    /// 状态栏只保留给"用户主动做了某件事之后的反馈"。
    /// </para>
    /// <para>
    /// 失败也不说话:自动检查失败时页面其余部分仍显示上次的版本信息,不值得弹一句
    /// 「检查更新失败」吓人 —— 用户想看结果可以点菜单里的「检查更新」。
    /// </para>
    /// </summary>
    private Task CheckUpdateSilentlyAsync() => CheckUpdateCoreAsync(interactive: false);

    /// <param name="interactive">是否写状态栏(用户主动检查)。</param>
    /// <param name="insideOperation">由正在进行的长操作在收尾时调用(只防重入 IsBusy,不判下载门)。</param>
    private async Task CheckUpdateCoreAsync(bool interactive, bool insideOperation = false)
    {
        if (!NativeGameManagementSupported)
        {
            if (interactive)
            {
                StatusText = PlatformGameNotice;
            }
            return;
        }
        // 统一操作门(M36):下载/修复/画质切换期间不检查 —— 结果会改写 HasUpdate/PredownloadCompleted
        if (insideOperation ? IsBusy : IsOperationRunning)
        {
            return;
        }

        IsBusy = true;
        if (interactive)
        {
            StatusText = LanguageService.Format("Launcher.Checking");
        }
        try
        {
            var result = await AppServices.GameUpdater.CheckUpdateAsync(ServerType);
            _lastCheck = result;
            if (!result.Success)
            {
                // 检查失败:清除旧的有更新横幅,避免残留误导
                HasUpdate = false;
                if (interactive)
                {
                    StatusText = result.Message ?? LanguageService.Format("Launcher.CheckFailed");
                }
                return;
            }

            ServerVersionText = result.ServerVersion ?? "-";
            // 本地版本(对齐 Haiyu 的 DisplayVersion)
            GameVersionText = result.InstalledVersion is { Length: > 0 }
                ? $"v{result.InstalledVersion}"
                : (result.NotInstalled ? LanguageService.Format("Launcher.NotInstalledShort") : LanguageService.Format("Launcher.Installed"));
            HasUpdate = result.HasUpdate;
            // 预载版本本地已完整下载:HasPredownload 已被 Core 短路为 false,
            // 这里置完成态让按钮禁用并显示「预下载完成」(对齐上游 1.6 修复)
            PredownloadCompleted = result.PredownloadCompleted;
            HasPredownload = result.HasPredownload;
            PredownloadStateText = result.PredownloadCompleted
                ? LanguageService.Format("Launcher.PredownloadDoneHint")
                : result.HasPredownload
                    ? LanguageService.Format("Launcher.CanPredownload", result.PredownloadVersion)
                    : "";

            // 下载/磁盘预估(参考 Haiyu Config.Size + UnCompressSize;空间不足时置警告)
            UpdateDownloadEstimate(result.TotalBytes);

            // 预下载场景:FilesToDownload 为空(CheckUpdate 不计算文件差异),
            // 额外加载预下载清单获取下载体积与磁盘需求,用于预下载环的下载/磁盘预估
            if (result.HasPredownload)
            {
                var (pdDownload, pdDisk) = await AppServices.GameUpdater.GetPredownloadEstimateAsync(ServerType);
                if (pdDownload > 0 || pdDisk > 0)
                {
                    UpdateDownloadEstimate(pdDownload, pdDisk);
                }
            }

            if (result.NotInstalled)
            {
                // NotInstalled 现在涵盖两种情形(见 GameUpdater.CheckUpdateAsync):
                // 从未安装 / 上次安装被打断。后者要明确告诉用户「会续传」,
                // 否则看到几十 GB 的预估会以为得从头再来一遍。
                InstallStateText = AppServices.GameUpdater.HasIncompleteInstall()
                    ? LanguageService.Format("Launcher.InstallIncomplete")
                    : LanguageService.Format("Launcher.InstallHint");
            }
            else if (result.HasUpdate)
            {
                InstallStateText = LanguageService.Format("Launcher.NewVersion", result.ServerVersion) +
                    (result.TotalBytes > 0 ? LanguageService.Format("Launcher.NeedDownload", FormatSize(result.TotalBytes)) : "");
            }
            else
            {
                InstallStateText = LanguageService.Format("Launcher.UpToDate", result.ServerVersion);
            }

            // 「已是最新」/「发现新版本」都只在**用户主动检查**时才刷状态栏:
            // 自动检查(进页/切目录/装完)不打扰 —— 进页就弹一句"游戏已是最新"再消失,
            // 是用户明确反馈的扰民行为。自动检查的结论已经有常驻出口(标题卡/按钮/版本号)。
            // 主动检查时:「发现新版本」留着提醒(需要用户处理),「已是最新」自动消失
            // (没有后续动作;无更新时标题卡整张隐藏,消失后页面回到干净状态)。
            if (interactive)
            {
                if (result.HasUpdate)
                {
                    StatusText = LanguageService.Format("Launcher.UpdateAvailable");
                }
                else
                {
                    SetTransientStatus(LanguageService.Format("Launcher.Latest"));
                }
            }
        }
        catch (Exception ex)
        {
            if (interactive)
            {
                StatusText = LanguageService.Format("Launcher.CheckFailedWith", ex.Message);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task PreDownloadAsync()
    {
        if (!NativeGameManagementSupported)
        {
            StatusText = PlatformGameNotice;
            return;
        }
        if (IsOperationRunning)
        {
            return;
        }

        IsDownloading = true;
        // 新任务开始:先按「不可暂停」起步,直到进度回调确认进入真正的下载阶段。
        // 避免沿用上一轮残留的 CanPause=true,让暂停按钮在无字节可暂停的阶段短暂闪现。
        CanPauseCurrentPhase = false;
        ProgressPercent = 0;
        ProgressText = LanguageService.Format("Launcher.Predownloading");
        StatusText = LanguageService.Format("Launcher.PredownloadStatus");
        // 清单回来之前总量未知:进度环转圈而不是死画 0%(弱网下这段可能很久)。
        IsPreparing = true;
        // 新任务重置峰值与波动曲线:上一轮的峰值/波形会把新曲线压平或误导"网速已恢复"。
        ResetSpeedStatistics();

        var progress = new Progress<DownloadProgress>(p => HandleProgress(p));

        try
        {
            var (success, _, message) = await AppServices.GameUpdater.PreDownloadAsync(ServerType, progress);
            if (success)
            {
                // 下载完整落盘即置完成态:按钮禁用并显示「预下载完成」(对齐上游 1.6)
                PredownloadCompleted = true;
                HasPredownload = false;
                PredownloadStateText = LanguageService.Format("Launcher.PredownloadDoneHint");
                StatusText = LanguageService.Format("Launcher.PredownloadAutoInstall");
                // 下载完成后自动进入安装阶段,进度卡片继续显示安装进度。
                await InstallCoreAsync();
            }
            else
            {
                StatusText = message ?? LanguageService.Format("Launcher.PredownloadFailed");
            }
        }
        catch (Exception ex)
        {
            StatusText = LanguageService.Format("Launcher.PredownloadFailedWith", ex.Message);
        }
        finally
        {
            // 无论成功/失败/异常都必须退出准备态,否则环会一直转下去。
            IsPreparing = false;
            IsDownloading = false;
            ProgressText = "";
        }
    }

    [RelayCommand]
    private async Task InstallAsync()
    {
        if (!NativeGameManagementSupported)
        {
            StatusText = PlatformGameNotice;
            return;
        }
        if (IsOperationRunning)
        {
            return;
        }
        // 还没选安装目录:直接给可操作的指引,而不是让 Core 返回一句"未设置游戏目录"就算完。
        // 全新安装场景下用户还没机会配目录(以前必须先装官方客户端才能选目录)。
        if (string.IsNullOrWhiteSpace(AppServices.Settings.Current.GameRootDir))
        {
            StatusText = LanguageService.Format("Launcher.PickDirFirst");
            return;
        }

        IsDownloading = true;
        try
        {
            // 安装/更新是新任务:上一轮的峰值与波形不该延续到新曲线(否则"看起来一直在满速")
            ResetSpeedStatistics();
            await InstallCoreAsync();
        }
        finally
        {
            IsDownloading = false;
            ProgressText = "";
        }
    }

    /// <summary>执行安装/更新并刷新状态。由「安装更新」和预下载完成后的自动安装共用。</summary>
    private async Task InstallCoreAsync()
    {
        ProgressPercent = 0;
        StatusText = LanguageService.Format("Launcher.Installing");
        // 安装阶段以补丁合成/校验/落位为主,先按不可暂停起步,进下载阶段后由回调打开。
        CanPauseCurrentPhase = false;
        // 与预下载同理:清单/索引回来之前总量未知,进度环转圈。
        IsPreparing = true;

        var progress = new Progress<DownloadProgress>(p => HandleProgress(p));

        try
        {
            var (success, message) = await AppServices.GameUpdater.InstallAsync(ServerType, progress);
            StatusText = success ? (message ?? LanguageService.Format("Launcher.InstallDone")) : (message ?? LanguageService.Format("Launcher.InstallFailed"));
            if (success)
            {
                RefreshState();
                // 此刻 IsDownloading 仍为 true(外层 finally 未执行):走绕过操作门的收尾复查
                await CheckUpdateAfterOperationAsync();
                // 安装成功后做一次体积自检(后台,不阻塞状态栏):多次断连留下的备份/半成品
                // 会让目录比清单大出几十 GB,发现"明显有过大残留"时弹窗问用户是否删除。
                await CheckResidueAfterInstallAsync();
            }
        }
        catch (Exception ex)
        {
            StatusText = LanguageService.Format("Launcher.InstallFailedWith", ex.Message);
        }
        finally
        {
            IsPreparing = false;
        }
    }

    /// <summary>
    /// 统一的进度回调处理(预下载/安装/修复共用,避免三份实现漂移):
    /// ① 非下载阶段(差分合成/解压/资源安装/迁移)只切文案,不动进度条;
    /// ② 校验阶段(BytesTotal==0)按文件数显示「正在校验本地文件 x/y…」,不再退化成 "0 B/0 B";
    /// ③ 下载阶段显示百分比/速度/剩余时间,并按 <see cref="DownloadProgress.CanPause"/> 决定暂停按钮可见。
    /// </summary>
    private void HandleProgress(DownloadProgress p)
    {
        // 一收到任何真实进度就退出准备态:此后进度条有实际含义,继续转圈会误导。
        // (准备态只在"清单还没回来、什么都还不知道"的窗口里有效。)
        if (IsPreparing)
        {
            IsPreparing = false;
        }

        // 可暂停性以 Core 的显式契约为准:非下载阶段暂停是空操作,必须隐藏按钮。
        CanPauseCurrentPhase = p.CanPause;

        // 非下载安装阶段(差分合成/解压/资源安装/迁移):只切文案,不动进度条
        if (p.Phase == DownloadPhase.Stage)
        {
            ProgressText = p.StageText!;
            CurrentFileText = p.CurrentFile;
            ClearSpeedDisplay();
            // 剩余时间/已下载量也一起清掉:这两项只在"有字节在流动"时才有意义。
            // 不清的话,差分合成一个 18 GB 分组期间界面会一直挂着上一阶段算出的
            // "剩余约 30 分钟",而进度条是静止的 —— 用户看到的是一个永远不动的承诺。
            RemainingTimeText = "";
            BytesText = "";
            return;
        }

        // 校验阶段(BytesTotal==0):进度按文件数显示,告知用户"正在校验本地文件"
        if (p.Phase == DownloadPhase.Verify)
        {
            ProgressPercent = Math.Clamp(p.FileIndex * 100.0 / p.FileTotal, 0, 100);
            ProgressText = LanguageService.Format("Launcher.Verifying", p.FileIndex, p.FileTotal);
            CurrentFileText = p.CurrentFile;
            ClearSpeedDisplay();
            BytesText = "";
            RemainingTimeText = "";
            return;
        }

        // 下载阶段:界面最多每 500ms 刷新一次(下载线程可高频上报),最后一次强制刷新
        var now = DateTime.UtcNow;
        var isFinal = p.FileTotal > 0 && p.FileIndex >= p.FileTotal;
        if (!isFinal && (now - _lastInstallUiUpdate).TotalMilliseconds < 500)
        {
            return;
        }

        _lastInstallUiUpdate = now;
        ProgressPercent = p.Percent * 100;
        ProgressText = LanguageService.Format("Launcher.FileProgress", p.FileIndex, p.FileTotal, FormatSize(p.BytesDownloaded), FormatSize(p.BytesTotal));
        CurrentFileText = p.CurrentFile;
        ApplySpeedDisplay(p.SpeedBps);
        BytesText = $"{FormatSize(p.BytesDownloaded)} / {FormatSize(p.BytesTotal)}";
        UpdateRemainingTime(p.BytesTotal - p.BytesDownloaded, p.SpeedBps);
        SampleSpeedHistory(p.SpeedBps);
    }

    [RelayCommand]
    private async Task RepairGameAsync()
    {
        if (!NativeGameManagementSupported)
        {
            StatusText = PlatformGameNotice;
            return;
        }
        if (IsOperationRunning)
        {
            return;
        }

        IsDownloading = true;
        CanPauseCurrentPhase = false;
        ProgressPercent = 0;
        ProgressText = LanguageService.Format("Launcher.Repairing");
        StatusText = LanguageService.Format("Launcher.RepairStatus");
        // 修复同样要先拉清单再逐文件校验,准备阶段总量未知。
        IsPreparing = true;

        var progress = new Progress<DownloadProgress>(p => HandleProgress(p));

        try
        {
            // 对齐 Haiyu:修复游戏跳过用户配置的校验文件,并按设置决定是否删除被跳过的文件
            var s = AppServices.Settings.Current;
            var skip = new HashSet<string>(s.SkipVerifyFiles, StringComparer.OrdinalIgnoreCase);
            var (success, message) = await AppServices.GameUpdater.RepairGameAsync(
                ServerType, skip, s.AutoSkipVerifyDelete, progress);
            StatusText = message ?? (success ? LanguageService.Format("Launcher.RepairDone") : LanguageService.Format("Launcher.RepairFailed"));
            if (success)
            {
                RefreshState();
                // 同安装:IsDownloading 仍为 true,收尾复查绕过操作门
                await CheckUpdateAfterOperationAsync();
                // 修复同样是"反复下载"的高发场景(每次修复都会给被替换文件再留一份备份)
                await CheckResidueAfterInstallAsync();
            }
        }
        catch (Exception ex)
        {
            StatusText = LanguageService.Format("Launcher.RepairFailedWith", ex.Message);
        }
        finally
        {
            IsPreparing = false;
            IsDownloading = false;
            ProgressText = "";
        }
    }

    /// <summary>
    /// 安装/修复完成后的体积自检(用户需求 2026-10):比对游戏目录实际占用与服务端清单总量,
    /// 同时扫描启动器自身留下的残留(替换前备份 / 孤儿 .part / 补丁与差分临时目录)。
    /// 仅在"明显过大"或"残留体积可观"时弹窗询问是否删除,不打扰常态安装。
    /// <para>
    /// <b>不阻断、不报错</b>:自检失败只写日志;用户选择保留时什么都不做。
    /// 扫描与删除都在后台线程,UI 线程只负责弹窗。
    /// </para>
    /// </summary>
    private async Task CheckResidueAfterInstallAsync()
    {
        // 用户可在设置里关掉(默认开);关闭后连扫描都不做。
        if (!AppServices.Settings.Current.InstallResidueCheckEnabled)
        {
            return;
        }

        // 走统一的扫描/弹窗/删除路径,但只在"明显过大"时才打扰(用户主动点菜单则如实呈现)。
        await CheckResidueCoreAsync(promptAlways: false, skipBusyGuard: true);
    }

    /// <summary>
    /// 手动触发体积自检(启动页功能菜单「检查重复文件」)。
    /// </summary>
    [RelayCommand]
    private async Task CheckResidueAsync() => await CheckResidueCoreAsync(promptAlways: true);

    /// <summary>
    /// 体积自检主体(手动入口与安装后自动检查共用,避免两份漂移)。
    /// </summary>
    /// <param name="promptAlways">
    /// true = 用户主动触发:只要有可回收残留就弹窗并如实反馈;
    /// false = 安装后自动:只在 <see cref="ResiduePolicy.ShouldPrompt"/> 判定"明显过大"时才打扰。
    /// </param>
    /// <param name="skipBusyGuard">
    /// 安装刚结束时 <see cref="IsDownloading"/> 可能仍为 true(外层 finally 尚未执行),
    /// 安装后自检必须跳过 bussy 守卫,否则这一步会被自己挡掉。
    /// </param>
    private async Task CheckResidueCoreAsync(bool promptAlways, bool skipBusyGuard = false)
    {
        if (!NativeGameManagementSupported)
        {
            StatusText = PlatformGameNotice;
            return;
        }
        if (!skipBusyGuard && IsOperationRunning)
        {
            return;
        }

        // 只在"自己确实拿到了忙碌位"时才占 IsBusy / 释放它。
        // 安装后自检会跳过守卫,而那时外层业务可能仍持有 IsBusy —— 无条件置 true/false
        // 会抢走别人的忙碌位、再把它清掉(后续命令的 IsBusy 自守卫就失效了)。
        var ownsBusy = !IsBusy;
        if (ownsBusy)
        {
            IsBusy = true;
        }
        StatusText = LanguageService.Format("Launcher.Residue.Scanning");
        try
        {
            // 扫描要遍历整个游戏目录(数万文件),必须把进度回吐到状态栏,
            // 否则用户点完只感到"卡住"而不是"正在检查"。
            var scanProgress = new Progress<ResidueScanProgress>(p =>
            {
                StatusText = p.ScannedFiles > 0
                    ? LanguageService.Format("Launcher.Residue.ScanningCount", p.ScannedFiles)
                    : LanguageService.Format("Launcher.Residue.Scanning");
            });
            var result = await AppServices.GameUpdater.ScanResidueAsync(progress: scanProgress);
            if (!result.Report.HasResidue)
            {
                // 「未发现可清理的重复文件」和「游戏已是最新」同类:一次性结论、没有后续动作。
                // 一直挂着会让人以为还有事没处理(用户反馈);给 8 秒后自动消失。
                SetTransientStatus(LanguageService.Format("Launcher.Residue.NoneFound"));
                return;
            }
            // 自动检查走策略门槛;手动检查如实呈现
            if (!promptAlways && !ResiduePolicy.ShouldPrompt(result))
            {
                return;
            }

            StatusText = LanguageService.Format("Launcher.Residue.Prompting");
            var owner = (Avalonia.Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            var confirmed = await McKuro.Views.ResidueCleanupWindow.ShowAsync(owner, result);
            if (!confirmed)
            {
                // 「已保留」同样是一次性结论(用户已经做了选择,不需要这句话常驻)
                SetTransientStatus(LanguageService.Format("Launcher.Residue.Kept"));
                return;
            }

            StatusText = LanguageService.Format("Launcher.Residue.Deleting");
            var (freed, failures) = await Task.Run(() => AppServices.GameUpdater.DeleteResidue(result));
            // 删除完成也是一次性结论(具体清理了多少、有无失败),同样自动消失;
            // 失败信息会在消失前停留 8 秒,足够看清,且真有问题用户可再点一次。
            SetTransientStatus(failures > 0
                ? LanguageService.Format("Launcher.Residue.Partial", FormatSize(freed), failures)
                : LanguageService.Format("Launcher.Residue.Cleaned", FormatSize(freed)));
            RefreshState();
        }
        catch (Exception ex)
        {
            StatusText = LanguageService.Format("Launcher.Residue.FailedWith", ex.Message);
        }
        finally
        {
            if (ownsBusy)
            {
                IsBusy = false;
            }
        }
    }

    /// <summary>
    /// 手动指定本地游戏版本(对齐 Haiyu「选择版本/跳过校验」):游戏被官方启动器等外部渠道
    /// 更新后,本地版本记录滞后 → 界面显示旧版本且误报「有更新」。跳过校验=只写版本记录;
    /// 进入校验=写记录后按服务端清单核对补齐(复用修复流程,其收尾会自动重跑检查更新)。
    /// 注意:不设 IsBusy —— 内部复用的 CheckUpdate/Repair 命令都有 IsBusy 自守卫,会互相吞掉。
    /// </summary>
    [RelayCommand]
    private async Task SelectVersionAsync()
    {
        if (!NativeGameManagementSupported)
        {
            StatusText = PlatformGameNotice;
            return;
        }
        if (!AppServices.Paths.IsGameInstalled)
        {
            return;
        }
        // 统一操作门(M36):安装/修复中途写版本记录会让安装器与记录对不上
        if (IsOperationRunning)
        {
            return;
        }

        try
        {
            var owner = (Avalonia.Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            // 预选"当前本地记录"。_lastCheck 可能为 null(首次检查还没跑完/上次检查失败),
            // 这时直接问 Core 要权威的 InstalledVersion,而不是放任下拉落到列表第一项
            // (= 服务端最新版):用户随手点「选定版本」就会把磁盘上根本没有的版本写成已装版本。
            // 注意不能从 GameVersionText 反推 —— 未装/未知时那里放的是本地化文案(如「已安装」),不是版本号。
            var installed = _lastCheck?.InstalledVersion
                ?? (await AppServices.GameUpdater.CheckUpdateAsync(ServerType)).InstalledVersion;
            var (version, verify) = await McKuro.Views.VersionSelectWindow.ShowAsync(
                owner,
                () => AppServices.GameUpdater.GetKnownVersionsAsync(ServerType),
                installed);
            if (version is null)
            {
                return;
            }
            // 弹窗前的 CheckUpdateAsync 不是模态的:这期间用户可能已开始安装/修复,写记录前再判一次门
            if (IsOperationRunning)
            {
                return;
            }
            if (!AppServices.GameUpdater.TrySetInstalledVersion(version))
            {
                StatusText = LanguageService.Format("Launcher.VersionSetInvalid");
                return;
            }
            GameVersionText = $"v{version}";
            if (verify)
            {
                await RepairGameAsync();
            }
            else
            {
                StatusText = LanguageService.Format("Launcher.VersionSetOk", version);
                await CheckUpdateAsync();
            }
        }
        catch (Exception ex)
        {
            StatusText = LanguageService.Format("Launcher.CheckFailedWith", ex.Message);
        }
    }

    [RelayCommand]
    private void Launch()
    {
        if (!NativeGameManagementSupported)
        {
            StatusText = PlatformGameNotice;
            return;
        }
        // 按钮已按 LaunchButtonEnabled 禁用;命令入口再挡一次(M35):
        // 替换文件途中启动会锁住待替换文件,或运行写了一半的客户端
        if (IsModifyingGameFiles)
        {
            return;
        }
        var ok = AppServices.GameUpdater.LaunchGame(out var error);
        if (!ok)
        {
            StatusText = LanguageService.Format("Launcher.LaunchFailed", error);
            return;
        }

        // 进程监控:点击启动 → 「启动中」;进程持续存活满 20 秒 → 「游戏中」;进程消失回「启动游戏」
        AppServices.GameMonitor.BeginLaunch(BuildGameProcessNames());
        StatusText = LanguageService.Format("Launcher.Launched");

        // 对齐 Haiyu 的"启动后可关闭主界面":可选最小化主窗口(任务栏 / 系统托盘)
        if (AppServices.Settings.Current.MinimizeOnLaunch
            && Avalonia.Application.Current?.ApplicationLifetime
                is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime
                { MainWindow: MainWindow w })
        {
            if (AppServices.Settings.Current.MinimizeLocationOnLaunch == "Tray")
            {
                w.HideToTray();
            }
            else if (w.WindowState != Avalonia.Controls.WindowState.Minimized)
            {
                w.WindowState = Avalonia.Controls.WindowState.Minimized;
            }
        }
    }

    /// <summary>进程监控候选名:实际启动 exe + 根 exe + 客户端 exe(兼容启动器 exe 转交客户端进程的情况)。</summary>
    private static IReadOnlyList<string> BuildGameProcessNames()
    {
        var names = new List<string>();
        var launched = AppServices.GameUpdater.ResolveLaunchExePath();
        if (!string.IsNullOrWhiteSpace(launched))
        {
            names.Add(Path.GetFileNameWithoutExtension(launched));
        }
        names.Add(Path.GetFileNameWithoutExtension(GamePathResolver.ExeRootName));
        names.Add(Path.GetFileNameWithoutExtension(GamePathResolver.ExeClientRelative));
        return names;
    }

    /// <summary>游戏会话状态变化:驱动启动按钮文案与状态文本。</summary>
    private void OnGameSessionStateChanged(GameSessionState state)
    {
        LaunchButtonText = state switch
        {
            GameSessionState.Launching => LanguageService.Format("Launcher.StateLaunching"),
            GameSessionState.InGame => LanguageService.Format("Launcher.StateInGame"),
            _ => LanguageService.Format("Launcher.Launch"),
        };
        UpdateLaunchButtonEnabled();
        // 这两个是读 GameMonitor.State 的计算属性,不通知的话「强制退出游戏」菜单项与
        // 画质包管理入口永远停在构造时的可用性(M34)
        OnPropertyChanged(nameof(CanForceExitGame));
        OnPropertyChanged(nameof(HasDeletablePacks));
        StatusText = state switch
        {
            GameSessionState.Launching => LanguageService.Format("Launcher.StateLaunchingStatus"),
            GameSessionState.InGame => LanguageService.Format("Launcher.StateInGameStatus", GameProcessMonitor.DefaultInGameWindow.TotalSeconds),
            _ => StatusText, // 空闲文案由 SessionEnded 设置
        };
    }

    /// <summary>
    /// 游戏会话结束:提前退出视为启动失败(恢复主窗口便于查看原因);
    /// 正常结束按「游戏结束后软件窗口状态」设置处理(保持原样/显示主窗口/自动退出)。
    /// </summary>
    private void OnGameSessionEnded(GameSessionEndReason reason)
    {
        if (reason == GameSessionEndReason.Failed)
        {
            // 启动失败要**常驻**:用户需要知道"刚才没启动起来"以及原因,
            // 自动消失就等于什么都没说(与「游戏已退出」性质不同 —— 后者是正常结束、无后续动作)。
            StatusText = LanguageService.Format("Launcher.LaunchProcessDied");
            ShowMainWindowFromHidden();
            return;
        }

        // 正常退出是一次性结论(用户刚玩完、不需要再做什么),8 秒后自动消失。
        // 原先一直挂在状态栏,回到启动页总看到一句过期的"游戏已退出"(2026-10 用户反馈)。
        SetTransientStatus(LanguageService.Format("Launcher.GameExited"));
        // 广播会话结束(主页重拉今日数据、游玩统计页重新解析日志刷新今日游玩时间)
        WeakReferenceMessenger.Default.Send(new GameSessionEndedMessage(reason));
        switch (AppServices.Settings.Current.AfterGameExitAction)
        {
            case "ExitApp":
                AppServices.Settings.Save();
                if (Avalonia.Application.Current?.ApplicationLifetime
                    is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                {
                    desktop.Shutdown();
                }
                break;
            case "ShowMainWindow":
                ShowMainWindowFromHidden();
                break;
        }
    }

    /// <summary>从托盘/最小化状态恢复主窗口(窗口未隐藏/未最小化时为无副作用操作)。</summary>
    private static void ShowMainWindowFromHidden()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime
            { MainWindow: MainWindow w })
        {
            w.RestoreFromHidden();
        }
    }

    [RelayCommand]
    private void OpenGameFolder()
    {
        if (!NativeGameManagementSupported)
        {
            StatusText = PlatformGameNotice;
            return;
        }
        var root = AppServices.Paths.GameRootDir;
        if (!string.IsNullOrEmpty(root) && Directory.Exists(root))
        {
            if (!AppServices.OpenInFileManager(root))
            {
                StatusText = LanguageService.Format("Launcher.OpenDirFailed");
            }
        }
        else
        {
            StatusText = LanguageService.Format("Launcher.NoGameDir");
        }
    }

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return $"{size:0.##} {units[unit]}";
    }

    /// <summary>
    /// 把一次速度上报落到界面:大字数值 + 单位 + 峰值,并采样进曲线。
    /// 速度只在进度卡的大字处显示一处(对齐上游 1.6 修复:避免进度行与速度行重复显示)。
    /// </summary>
    private void ApplySpeedDisplay(double speedBps)
    {
        var display = SpeedTextFormat.FromBytesPerSecond(speedBps);
        SpeedValueText = display.Value;
        SpeedUnitText = display.Unit;

        var mbps = speedBps / 1024.0 / 1024.0;
        if (mbps > _peakSpeedMbps)
        {
            _peakSpeedMbps = mbps;
            PeakSpeedText = LanguageService.Format("Launcher.PeakSpeed", SpeedTextFormat.FromMbps(_peakSpeedMbps).Combined);
        }
        SampleSpeedHistory(speedBps);
    }

    /// <summary>清空速度相关展示(非下载阶段)。峰值保留:曲线上仍显示本次任务的波动参照。</summary>
    private void ClearSpeedDisplay()
    {
        SpeedValueText = "--";
        SpeedUnitText = "";
    }

    /// <summary>本次任务的速度峰值(MB/s)。</summary>
    private double _peakSpeedMbps;

    /// <summary>
    /// 冒烟/目视验证用的假数据驱动(<c>McKuro_SMOKE=1</c> + <c>McKuro_SMOKE_DL=1</c>):
    /// 不触网,只按固定节奏往进度卡灌一组"会抖动的网速",用于验证曲线与大字排版。
    /// <para>
    /// <c>McKuro_SMOKE_DL</c> 的取值决定波形形态,便于分别验收三条纵轴规则:
    /// <c>1</c>/<c>normal</c> = 常规抖动(13.5MB/s 基准,含一次掉速);
    /// <c>weak</c> = 弱网小流量(0.6~1.2MB/s,验证小数值也能放大出波动);
    /// <c>stable</c> = 稳定高速(30MB/s ± 0.3,验证噪声不被放大成锯齿)。
    /// </para>
    /// 返回的计时器由调用方持有(随窗口关闭结束)。
    /// </summary>
    internal Avalonia.Threading.DispatcherTimer? StartSmokeDownloadFeed()
    {
        if (Environment.GetEnvironmentVariable("McKuro_SMOKE") != "1")
        {
            return null;
        }
        var profile = Environment.GetEnvironmentVariable("McKuro_SMOKE_DL");
        if (string.IsNullOrEmpty(profile) || profile == "0")
        {
            return null;
        }

        IsDownloading = true;
        CanPauseCurrentPhase = true;
        IsPreparing = false;
        ResetSpeedStatistics();
        ProgressPercent = 18.6;
        ProgressText = LanguageService.Format("Launcher.FileProgress", 180, 734, FormatSize(1_660_000_000), FormatSize(8_920_000_000));
        DownloadSizeText = LanguageService.Format("Launcher.NeedDownload", FormatSize(7_260_000_000));
        RemainingTimeText = LanguageService.Format("Launcher.RemainMinutes", 34);
        StatusText = LanguageService.Format("Launcher.PredownloadStatus");

        var phase = 0;
        var rng = new Random(20261026);
        var timer = new Avalonia.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250),
        };
        timer.Tick += (_, _) =>
        {
            phase++;
            double mbps;
            switch (profile)
            {
                case "weak":
                    // 弱网:0.6~1.2MB/s,抖动绝对值只有 ±0.3 —— 正是旧实现里"看着像直线"的场景
                    mbps = 0.9 + Math.Sin(phase * 0.33) * 0.22 + Math.Sin(phase * 0.11) * 0.08
                        + (rng.NextDouble() - 0.5) * 0.06;
                    break;
                case "stable":
                    // 稳定高速:30MB/s ± 0.3 —— 应呈现轻微起伏,而不是满屏锯齿
                    mbps = 30.0 + Math.Sin(phase * 0.5) * 0.3 + (rng.NextDouble() - 0.5) * 0.1;
                    break;
                default:
                    // 常规:13.5MB/s 基准 + 慢正弦 + 偶发下探,验证自适应放大与掉速形态
                    var wave = Math.Sin(phase * 0.28) * 4.2 + Math.Sin(phase * 0.09) * 2.0;
                    var dip = phase % 37 is > 30 and < 36 ? -6.5 : 0;
                    var jitter = (rng.NextDouble() - 0.5) * 1.2;
                    mbps = Math.Max(0.4, 13.5 + wave + dip + jitter);
                    break;
            }
            ApplySpeedDisplay(Math.Max(1, mbps * 1024 * 1024));
            ProgressPercent = Math.Min(99.9, ProgressPercent + 0.15);
        };
        timer.Start();
        return timer;
    }

    /// <summary>重置峰值与曲线(新任务启动时调用,避免上一轮的大峰值把新曲线压平)。</summary>
    private void ResetSpeedStatistics()
    {
        _peakSpeedMbps = 0;
        PeakSpeedText = "";
        _lastSpeedSample = DateTime.MinValue;
        DownloadSpeedHistory.Clear();
    }

    /// <summary>采样网速到历史集合(250ms 一点,保留最近 30 秒约 120 点,供波动曲线显示)。</summary>
    /// <remarks>
    /// 口径参考 FlClash 仪表盘:窗口太短(旧实现只有 5 秒 10 点)看不出"网速在波动",
    /// 曲线几乎是一条小抖动;30 秒窗口足够呈现一次掉速/回升的完整形态。
    /// </remarks>
    private void SampleSpeedHistory(double speedBps)
    {
        var now = DateTime.UtcNow;
        if ((now - _lastSpeedSample).TotalMilliseconds < SpeedSampleIntervalMs)
        {
            return;
        }
        _lastSpeedSample = now;
        var mbps = speedBps / 1024.0 / 1024.0;
        DownloadSpeedHistory.Add(mbps);
        // 保留最近 30 秒
        while (DownloadSpeedHistory.Count > SpeedSampleMaxPoints)
        {
            DownloadSpeedHistory.RemoveAt(0);
        }
    }

    /// <summary>曲线采样间隔(毫秒)。250ms × 120 点 = 30 秒窗口。</summary>
    public const int SpeedSampleIntervalMs = 250;

    /// <summary>曲线保留点数。</summary>
    public const int SpeedSampleMaxPoints = 120;

    /// <summary>更新下载/磁盘预估(参考 Haiyu Config.Size + UnCompressSize,磁盘空间用 DriveInfo 实测)。</summary>
    private void UpdateDownloadEstimate(long downloadBytes, long diskBytes = 0)
    {
        DownloadSizeText = downloadBytes > 0 ? LanguageService.Format("Launcher.NeedDownload", FormatSize(downloadBytes)) : "";
        DiskSpaceText = BuildDiskSpaceText(downloadBytes, diskBytes);
        OnPropertyChanged(nameof(DiskSpaceWarning));
    }

    /// <summary>预估剩余时间(按当前速度,参考 Haiyu 下载器 ETA 显示)。</summary>
    private void UpdateRemainingTime(long remainingBytes, double speedBps)
    {
        if (remainingBytes <= 0 || speedBps <= 0)
        {
            RemainingTimeText = "";
            return;
        }
        var seconds = (long)(remainingBytes / speedBps);
        if (seconds < 60)
        {
            RemainingTimeText = LanguageService.Format("Launcher.Less1Min");
        }
        else if (seconds < 3600)
        {
            RemainingTimeText = LanguageService.Format("Launcher.RemainMinutes", seconds / 60);
        }
        else
        {
            RemainingTimeText = LanguageService.Format("Launcher.RemainHoursMinutes", seconds / 3600, seconds % 3600 / 60);
        }
    }

    /// <summary>磁盘空间预估:所需磁盘空间 vs 游戏目录所在盘可用空间(不足时置警告)。</summary>
    private string BuildDiskSpaceText(long downloadBytes, long diskBytes)
    {
        try
        {
            var gameDir = AppServices.Settings.Current.GameRootDir;
            if (string.IsNullOrWhiteSpace(gameDir) || !Directory.Exists(gameDir))
            {
                return "";
            }
            var drive = new DriveInfo(Path.GetPathRoot(gameDir) ?? gameDir);
            if (!drive.IsReady)
            {
                return "";
            }
            // 所需磁盘空间:优先用服务端 unCompressSize/requiredDiskSpace,回退下载体积估算
            var needed = diskBytes > 0
                ? diskBytes
                : (long)(downloadBytes * 1.1);
            var free = drive.AvailableFreeSpace;
            DiskSpaceWarning = free < needed;
            return LanguageService.Format("Launcher.DiskNeed", FormatSize(needed), FormatSize(free))
                + (DiskSpaceWarning ? LanguageService.Format("Launcher.DiskInsufficient") : "");
        }
        catch
        {
            return "";
        }
    }
}
