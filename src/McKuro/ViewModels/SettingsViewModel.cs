using System.Collections.ObjectModel;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using McKuro.Core.Services.Game;
using McKuro.Core.Services.Update;
using McKuro.Core.Services.Wallpaper;
using McKuro.Services;

namespace McKuro.ViewModels;

/// <summary>设置页。</summary>
public sealed partial class SettingsViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _gameRootDir;

    /// <summary>游戏目录即时保存(路径解析器通过 Func 自动读取最新值,无需重启)。</summary>
    partial void OnGameRootDirChanged(string value)
    {
        AppServices.Settings.Current.GameRootDir = value;
        AppServices.Settings.Save();
    }

    [ObservableProperty]
    private string _serverTypeText = "";

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private int _downloadConcurrency;

    /// <summary>并发下载数即时保存(改动即生效,无需点保存)。</summary>
    partial void OnDownloadConcurrencyChanged(int value)
    {
        var v = Math.Clamp(value, 1, 32);
        var s = AppServices.Settings.Current;
        s.DownloadConcurrency = v;
        AppServices.Settings.Save();
        AppServices.Downloader.SetConcurrency(v);
    }

    /// <summary>下载速度限制(MB/s,0 = 不限;对齐 Haiyu LimitSpeed)。</summary>
    [ObservableProperty]
    private int _limitSpeedMbps;

    /// <summary>下载限速即时保存(改动即生效,无需点保存)。</summary>
    partial void OnLimitSpeedMbpsChanged(int value)
    {
        var v = Math.Max(0, value);
        var s = AppServices.Settings.Current;
        s.LimitSpeedMbps = v;
        AppServices.Settings.Save();
        AppServices.Downloader.SetSpeedLimit((long)v * 1024 * 1024);
    }

    // 游戏启动参数(对齐 Haiyu StartGameOption)
    [ObservableProperty]
    private bool _useDx11;

    partial void OnUseDx11Changed(bool value)
    {
        AppServices.Settings.Current.UseDx11 = value;
        AppServices.Settings.Save();
    }

    [ObservableProperty]
    private bool _disableDlss;

    partial void OnDisableDlssChanged(bool value)
    {
        AppServices.Settings.Current.DisableDlss = value;
        AppServices.Settings.Save();
    }

    [ObservableProperty]
    private string _startGameArguments = "";

    partial void OnStartGameArgumentsChanged(string value)
    {
        AppServices.Settings.Current.StartGameArguments = value;
        AppServices.Settings.Save();
    }

    [ObservableProperty]
    private string _startGameExeName = "";

    partial void OnStartGameExeNameChanged(string value)
    {
        AppServices.Settings.Current.StartGameExeName = value;
        AppServices.Settings.Save();
    }

    /// <summary>启动游戏后最小化主窗口。</summary>
    [ObservableProperty]
    private bool _minimizeOnLaunch;

    partial void OnMinimizeOnLaunchChanged(bool value)
    {
        AppServices.Settings.Current.MinimizeOnLaunch = value;
        AppServices.Settings.Save();
        OnPropertyChanged(nameof(ShowMinimizeLocationSetting));
    }

    // ---------- 启动后最小化位置 / 游戏结束后窗口状态 ----------

    /// <summary>启动后最小化位置选项(任务栏 / 系统托盘;macOS 对应 Dock / 菜单栏)。</summary>
    public ObservableCollection<string> MinimizeLocations { get; } = ["任务栏", "系统托盘"];

    /// <summary>游戏结束后软件窗口状态选项(保持原样 / 显示主窗口 / 自动退出软件)。</summary>
    public ObservableCollection<string> AfterGameExitActions { get; } =
        ["保持原样", "显示主窗口", "自动退出软件"];

    /// <summary>最小化位置行是否显示:仅启用「启动游戏后最小化主窗口」时显示。</summary>
    public bool ShowMinimizeLocationSetting => MinimizeOnLaunch;

    /// <summary>最小化位置:0=任务栏,1=系统托盘。</summary>
    [ObservableProperty]
    private int _minimizeLocationIndex;

    /// <summary>最小化位置即时保存(无需点保存)。</summary>
    partial void OnMinimizeLocationIndexChanged(int value)
    {
        AppServices.Settings.Current.MinimizeLocationOnLaunch = value == 1 ? "Tray" : "Taskbar";
        AppServices.Settings.Save();
    }

    /// <summary>游戏结束后窗口状态:0=保持原样,1=显示主窗口,2=自动退出软件。</summary>
    [ObservableProperty]
    private int _afterGameExitIndex;

    /// <summary>游戏结束后窗口状态即时保存(无需点保存)。</summary>
    partial void OnAfterGameExitIndexChanged(int value)
    {
        AppServices.Settings.Current.AfterGameExitAction = value switch
        {
            1 => "ShowMainWindow",
            2 => "ExitApp",
            _ => "KeepCurrent",
        };
        AppServices.Settings.Save();
    }

    [ObservableProperty]
    private bool _backgroundVideoEnabled;

    /// <summary>背景视频开关即时保存(切换立即生效,无需点保存)。</summary>
    partial void OnBackgroundVideoEnabledChanged(bool value)
    {
        AppServices.Settings.Current.BackgroundVideoEnabled = value;
        AppServices.Settings.Save();
    }

    // ---------- 启动页视频自定义(本地视频 / Wallpaper Engine 动态壁纸) ----------

    /// <summary>视频来源:0=官方宣传视频,1=自定义动态壁纸(即时保存)。</summary>
    [ObservableProperty]
    private int _backgroundVideoMode;

    partial void OnBackgroundVideoModeChanged(int value)
    {
        AppServices.Settings.Current.BackgroundVideoMode = value;
        AppServices.Settings.Save();
        OnPropertyChanged(nameof(IsCustomVideoMode));
    }

    /// <summary>是否自定义模式(控制自定义区块显隐)。</summary>
    public bool IsCustomVideoMode => BackgroundVideoMode == 1;

    /// <summary>视频来源下拉项(索引即 BackgroundVideoMode)。</summary>
    public ObservableCollection<string> VideoSources { get; } = ["官方宣传视频", "自定义动态壁纸(本地 / Wallpaper Engine)"];

    /// <summary>当前自定义视频绝对路径(即时保存)。</summary>
    [ObservableProperty]
    private string _customBackgroundVideoPath = "";

    partial void OnCustomBackgroundVideoPathChanged(string value)
    {
        AppServices.Settings.Current.CustomBackgroundVideoPath = value;
        AppServices.Settings.Save();
        OnPropertyChanged(nameof(HasCustomVideoPath));
    }

    /// <summary>已选择自定义视频(控制预览区块显隐)。</summary>
    public bool HasCustomVideoPath => !string.IsNullOrWhiteSpace(CustomBackgroundVideoPath);

    /// <summary>自定义视频封面路径(WE 包的 preview.jpg;本地文件选择时为空,设置页直接实时预览)。</summary>
    [ObservableProperty]
    private string _customVideoCoverPath = "";

    /// <summary>扫描到的 Wallpaper Engine 视频壁纸条目(选择面板数据源)。</summary>
    public ObservableCollection<WallpaperVideoEntry> WallpaperEntries { get; } = [];

    [ObservableProperty]
    private bool _hasWallpaperEntries;

    /// <summary>扫描/选择状态提示。</summary>
    [ObservableProperty]
    private string _wallpaperScanStatus = "";

    /// <summary>上次扫描的 WE 内容目录(即时保存,记忆用)。</summary>
    [ObservableProperty]
    private string _wallpaperEngineDir = "";

    partial void OnWallpaperEngineDirChanged(string value)
    {
        AppServices.Settings.Current.WallpaperEngineDir = value;
        AppServices.Settings.Save();
    }

    /// <summary>直接选择本地视频文件作为动态壁纸。</summary>
    [RelayCommand]
    private async Task BrowseCustomVideoAsync()
    {
        var topLevel = (Avalonia.Application.Current?.ApplicationLifetime
            as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (topLevel is null)
        {
            return;
        }
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "选择动态壁纸视频",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new Avalonia.Platform.Storage.FilePickerFileType("视频文件")
                {
                    Patterns = ["*.mp4", "*.webm", "*.mkv", "*.avi", "*.mov", "*.wmv"],
                },
            ],
        });
        if (files.Count == 0)
        {
            return;
        }
        CustomVideoCoverPath = ""; // 本地文件无封面,设置页用实时预览
        CustomBackgroundVideoPath = files[0].Path.LocalPath;
        BackgroundVideoMode = 1;
        WallpaperScanStatus = "已选择本地视频";
    }

    /// <summary>选择 Wallpaper Engine 内容目录并扫描视频壁纸(工坊 431960 目录或单个壁纸包目录)。</summary>
    [RelayCommand]
    private async Task BrowseWallpaperEngineAsync()
    {
        var topLevel = (Avalonia.Application.Current?.ApplicationLifetime
            as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (topLevel is null)
        {
            return;
        }
        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
        {
            Title = "选择 Wallpaper Engine 内容目录(如 steamapps\\workshop\\content\\431960)",
            AllowMultiple = false,
        });
        if (folders.Count == 0)
        {
            return;
        }
        var dir = folders[0].Path.LocalPath;
        WallpaperEngineDir = dir;
        WallpaperScanStatus = "正在扫描…";
        // 大目录(几十个包 + IO)不卡 UI 线程
        var entries = await Task.Run(() => WallpaperEngineScanner.Scan(dir));
        WallpaperEntries.Clear();
        foreach (var e in entries)
        {
            WallpaperEntries.Add(e);
        }
        HasWallpaperEntries = WallpaperEntries.Count > 0;
        WallpaperScanStatus = entries.Count > 0
            ? $"扫描到 {entries.Count} 个视频壁纸,点击封面选用"
            : "未找到视频类壁纸(scene/web 类型由 WE 私有引擎渲染,无视频文件可复用)";
    }

    /// <summary>点选扫描结果条目 → 设为自定义壁纸(带封面)。</summary>
    [RelayCommand]
    private void SelectWallpaper(WallpaperVideoEntry? entry)
    {
        if (entry is null)
        {
            return;
        }
        CustomVideoCoverPath = entry.CoverPath ?? "";
        CustomBackgroundVideoPath = entry.VideoPath;
        BackgroundVideoMode = 1;
        WallpaperScanStatus = $"已选择「{entry.Title}」";
    }

    /// <summary>为已保存的自定义视频找回 WE 封面(视频同目录 preview.*;本地任意文件则无)。</summary>
    private static string FindCoverForVideo(string videoPath)
    {
        if (string.IsNullOrWhiteSpace(videoPath))
        {
            return "";
        }
        try
        {
            var dir = Path.GetDirectoryName(videoPath);
            return string.IsNullOrEmpty(dir) ? "" : WallpaperEngineScanner.FindCoverIn(dir) ?? "";
        }
        catch (Exception)
        {
            return "";
        }
    }

    // 游戏修复:跳过校验文件(对齐 Haiyu SkipVerifyFiles)
    [ObservableProperty]
    private string _skipVerifyInput = "";

    [ObservableProperty]
    private bool _autoSkipVerifyDelete = true;

    /// <summary>修复游戏时是否删除被跳过文件,即时保存(无需点保存)。</summary>
    partial void OnAutoSkipVerifyDeleteChanged(bool value)
    {
        AppServices.Settings.Current.AutoSkipVerifyDelete = value;
        AppServices.Settings.Save();
    }

    public ObservableCollection<string> SkipVerifyFiles { get; } = [];

    // 界面语言
    [ObservableProperty]
    private int _languageIndex;

    /// <summary>界面语言即时保存(切换后写回配置;界面文案重启后完整应用)。</summary>
    partial void OnLanguageIndexChanged(int value)
    {
        AppServices.Settings.Current.Language = value == 1 ? "en-US" : "zh-Hans";
        AppServices.Settings.Save();
    }

    // 启动时打开的页面(主页 / 鸣潮启动页)
    /// <summary>启动页选项:0=主页,1=启动页(鸣潮)。</summary>
    public ObservableCollection<string> StartupPages { get; } = ["主页", "启动页"];

    [ObservableProperty]
    private int _startupPageIndex;

    /// <summary>启动页选择即时保存(下次启动生效)。</summary>
    partial void OnStartupPageIndexChanged(int value)
    {
        AppServices.Settings.Current.StartupPage = value == 1 ? "Launcher" : "Home";
        AppServices.Settings.Save();
    }

    // 签到设置(AutoSignEnabled/AutoKuroClientTaskEnabled)已迁移至「签到」页管理,
    // 此处不再持有副本:避免保存其他设置时用启动时的旧值回滚签到页的新值
    [ObservableProperty]
    private string _appUpdateStatusText = "";

    [ObservableProperty]
    private string _appUpdateVersionText = "";

    [ObservableProperty]
    private bool _appUpdateAvailable;

    [ObservableProperty]
    private bool _appUpdateChecking;

    [ObservableProperty]
    private bool _appUpdateDownloading;

    [ObservableProperty]
    private double _appUpdateProgress;

    private AppUpdateInfo? _pendingUpdate;

    /// <summary>当前应用版本(读程序集版本)。</summary>
    public string CurrentAppVersionText =>
        $"v{Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0"}";

    /// <summary>日志目录(为空表示文件日志不可用)。</summary>
    public string LogDirText => AppServices.LogDir;

    public string PlatformNameText => AppServices.Capabilities.PlatformName;

    /// <summary>平台徽标:是否为 Windows(四格窗格 Logo)。</summary>
    public bool IsPlatformWindows => AppServices.Capabilities.IsWindows;

    /// <summary>平台徽标:是否为 macOS(⌘ 命令符)。</summary>
    public bool IsPlatformMacOS => AppServices.Capabilities.IsMacOS;

    /// <summary>平台徽标:Linux 或其他平台(回退终端图标)。</summary>
    public bool IsPlatformLinux => AppServices.Capabilities.IsLinux;

    /// <summary>平台支持状态徽标文本。</summary>
    public string PlatformSupportBadgeText => AppServices.Capabilities.IsWindows ? "原生支持" : "部分支持";

    [RelayCommand]
    private void OpenLogDir()
    {
        var dir = AppServices.LogDir;
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
        {
            AppUpdateStatusText = "日志目录不可用";
            return;
        }
        try
        {
            if (!AppServices.OpenInFileManager(dir))
            {
                AppUpdateStatusText = "打开日志目录失败";
            }
        }
        catch (Exception ex)
        {
            AppUpdateStatusText = $"打开日志目录失败: {ex.Message}";
        }
    }

    // 主题(Default 跟随系统 / Light / Dark)
    [ObservableProperty]
    private int _themeIndex;

    public ObservableCollection<string> Languages { get; } = ["简体中文 (zh-Hans)", "English (en-US)"];

    public ObservableCollection<string> Themes { get; } = ["跟随系统", "浅色", "深色"];

    /// <summary>主题选择即时生效(对齐 Haiyu OnSelectThemeChanged):切换即应用并持久化,无需点保存。</summary>
    partial void OnThemeIndexChanged(int value)
    {
        var theme = ThemeIndex switch
        {
            1 => "Light",
            2 => "Dark",
            _ => "Default",
        };
        var s = AppServices.Settings.Current;
        s.Theme = theme;
        AppServices.Settings.Save();
        ApplyThemeVariant(theme);
    }

    /// <summary>把主题字符串应用到全局 RequestedThemeVariant(Semi 动态资源随之刷新:浅色/深色/跟随系统)。</summary>
    private static void ApplyThemeVariant(string theme)
    {
        if (Avalonia.Application.Current is { } app)
        {
            app.RequestedThemeVariant = theme switch
            {
                "Light" => Avalonia.Styling.ThemeVariant.Light,
                "Dark" => Avalonia.Styling.ThemeVariant.Dark,
                _ => Avalonia.Styling.ThemeVariant.Default,
            };
        }
    }

    public ObservableCollection<string> ServerTypes { get; } =
    [
        "自动检测", "官服", "B站", "WeGame", "国际服",
    ];

    [ObservableProperty]
    private int _selectedServerIndex;

    public SettingsViewModel()
    {
        var s = AppServices.Settings.Current;
        _gameRootDir = s.GameRootDir;
        _downloadConcurrency = s.DownloadConcurrency;
        _limitSpeedMbps = s.LimitSpeedMbps;
        _useDx11 = s.UseDx11;
        _disableDlss = s.DisableDlss;
        _startGameArguments = s.StartGameArguments;
        _startGameExeName = s.StartGameExeName;
        _minimizeOnLaunch = s.MinimizeOnLaunch;
        _minimizeLocationIndex = s.MinimizeLocationOnLaunch == "Tray" ? 1 : 0;
        _afterGameExitIndex = s.AfterGameExitAction switch
        {
            "ShowMainWindow" => 1,
            "ExitApp" => 2,
            _ => 0,
        };
        _backgroundVideoEnabled = s.BackgroundVideoEnabled;
        _backgroundVideoMode = s.BackgroundVideoMode;
        _customBackgroundVideoPath = s.CustomBackgroundVideoPath;
        _customVideoCoverPath = FindCoverForVideo(s.CustomBackgroundVideoPath);
        _wallpaperEngineDir = s.WallpaperEngineDir;
        _autoSkipVerifyDelete = s.AutoSkipVerifyDelete;
        foreach (var p in s.SkipVerifyFiles)
        {
            SkipVerifyFiles.Add(p);
        }
        _languageIndex = Math.Max(0, Languages.IndexOf(LanguageLabel(s.Language)));
        _startupPageIndex = s.StartupPage == "Launcher" ? 1 : 0;
        _appUpdateAutoCheck = s.AppUpdateAutoCheck;
        _appUpdateAutoInstall = s.AppUpdateAutoInstall;
        _themeIndex = s.Theme switch
        {
            "Light" => 1,
            "Dark" => 2,
            _ => 0,
        };
        _selectedServerIndex = s.ServerType switch
        {
            GameServerType.Official => 1,
            GameServerType.Bilibili => 2,
            GameServerType.WeGame => 3,
            GameServerType.Global => 4,
            _ => 0,
        };
        UpdateServerTypeText();
    }

    private static string LanguageLabel(string code) => code == "en-US" ? "English (en-US)" : "简体中文 (zh-Hans)";

    /// <summary>服务器渠道选择即时保存(无需点保存)。</summary>
    partial void OnSelectedServerIndexChanged(int value)
    {
        UpdateServerTypeText();
        AppServices.Settings.Current.ServerType = value switch
        {
            1 => GameServerType.Official,
            2 => GameServerType.Bilibili,
            3 => GameServerType.WeGame,
            4 => GameServerType.Global,
            _ => GameServerType.Unknown,
        };
        AppServices.Settings.Save();
    }

    private void UpdateServerTypeText()
    {
        ServerTypeText = ServerTypes[Math.Clamp(SelectedServerIndex, 0, ServerTypes.Count - 1)];
    }

    [RelayCommand]
    private async Task BrowseGameDirAsync()
    {
        var lifetime = Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
        var topLevel = lifetime?.MainWindow;
        if (topLevel is null)
        {
            return;
        }

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
        {
            Title = "选择鸣潮游戏安装目录",
            AllowMultiple = false,
        });
        if (folders.Count == 0)
        {
            return;
        }

        var dir = folders[0].Path.LocalPath;

        // 校验目录包含游戏主程序(对齐 Haiyu 选目录后自动识别:无效目录直接提示,不写入配置)
        if (!File.Exists(Path.Combine(dir, GamePathResolver.ExeRootName)))
        {
            StatusText = $"目录未包含 {GamePathResolver.ExeRootName},请选择正确的游戏安装目录";
            return;
        }

        // 即时保存并通知启动器页自动识别加载(检测版本/渠道,自动检查更新)
        var s = AppServices.Settings.Current;
        s.ServerType = GameServerType.Unknown; // 交给自动检测
        SelectedServerIndex = 0; // 触发 OnSelectedServerIndexChanged 写回并保存
        GameRootDir = dir; // 触发 OnGameRootDirChanged 写回并保存(含上面的 ServerType 变更)
        StatusText = "目录已设置,正在自动识别加载…";
        WeakReferenceMessenger.Default.Send(new GameDirectoryChangedMessage(dir));
    }

    [RelayCommand]
    private void AddSkipVerifyFile()
    {
        var path = SkipVerifyInput.Trim().Replace('\\', '/');
        if (string.IsNullOrEmpty(path))
        {
            StatusText = "请输入要跳过的文件相对路径";
            return;
        }
        if (SkipVerifyFiles.Any(f => string.Equals(f, path, StringComparison.OrdinalIgnoreCase)))
        {
            StatusText = "该文件已在跳过列表中";
            return;
        }
        SkipVerifyFiles.Add(path);
        SkipVerifyInput = "";
        Save(); // 即时写回配置
        StatusText = $"已添加跳过文件: {path}";
    }

    [RelayCommand]
    private void RemoveSkipVerifyFile(string path)
    {
        SkipVerifyFiles.Remove(path);
        Save();
        StatusText = $"已移除跳过文件: {path}";
    }

    /// <summary>把所有界面设置项写回配置并保存(各设置项改动时已即时保存,此方法为幂等兜底)。</summary>
    private void Save()
    {
        var s = AppServices.Settings.Current;
        s.GameRootDir = GameRootDir;
        s.ServerType = SelectedServerIndex switch
        {
            1 => GameServerType.Official,
            2 => GameServerType.Bilibili,
            3 => GameServerType.WeGame,
            4 => GameServerType.Global,
            _ => GameServerType.Unknown,
        };
        s.DownloadConcurrency = Math.Clamp(DownloadConcurrency, 1, 32);
        s.LimitSpeedMbps = Math.Max(0, LimitSpeedMbps);
        s.UseDx11 = UseDx11;
        s.DisableDlss = DisableDlss;
        s.StartGameArguments = StartGameArguments;
        s.StartGameExeName = StartGameExeName;
        s.MinimizeOnLaunch = MinimizeOnLaunch;
        s.MinimizeLocationOnLaunch = MinimizeLocationIndex == 1 ? "Tray" : "Taskbar";
        s.AfterGameExitAction = AfterGameExitIndex switch
        {
            1 => "ShowMainWindow",
            2 => "ExitApp",
            _ => "KeepCurrent",
        };
        s.BackgroundVideoEnabled = BackgroundVideoEnabled;
        s.SkipVerifyFiles = [.. SkipVerifyFiles];
        s.AutoSkipVerifyDelete = AutoSkipVerifyDelete;
        s.Language = LanguageIndex == 1 ? "en-US" : "zh-Hans";
        s.StartupPage = StartupPageIndex == 1 ? "Launcher" : "Home";
        s.Theme = ThemeIndex switch
        {
            1 => "Light",
            2 => "Dark",
            _ => "Default",
        };
        AppServices.Settings.Save();

        // 重新应用并发数与限速(无需重启);路径解析器已通过 Func<string?> 自动读取最新值
        AppServices.Downloader.SetConcurrency(s.DownloadConcurrency);
        AppServices.Downloader.SetSpeedLimit((long)s.LimitSpeedMbps * 1024 * 1024);

        // 主题即时生效(对齐 Haiyu 设置主题;选择时已即时应用,保存时再确保一致)
        ApplyThemeVariant(s.Theme);
    }

    // ---------- 应用自更新(对齐 Haiyu UpdateAppViewModel) ----------

    /// <summary>启动后自动检查应用更新(主窗口启动延迟触发,见 MainWindowViewModel)。</summary>
    [ObservableProperty]
    private bool _appUpdateAutoCheck;

    partial void OnAppUpdateAutoCheckChanged(bool value)
    {
        AppServices.Settings.Current.AppUpdateAutoCheck = value;
        AppServices.Settings.Save();
    }

    /// <summary>自动下载并安装更新(零点击升级;关闭则发现新版时弹窗询问)。</summary>
    [ObservableProperty]
    private bool _appUpdateAutoInstall;

    partial void OnAppUpdateAutoInstallChanged(bool value)
    {
        AppServices.Settings.Current.AppUpdateAutoInstall = value;
        AppServices.Settings.Save();
    }

    /// <summary>检查应用更新(GitHub Releases latest;跳过已跳过的版本)。public:供主窗口启动自动检查调用。</summary>
    [RelayCommand]
    public async Task CheckAppUpdateAsync()
    {
        // 仓库由配置提供(默认 ZPC5560/McKuro),设置页不再提供输入框
        var repo = AppServices.Settings.Current.AppUpdateRepo.Trim();
        if (string.IsNullOrWhiteSpace(repo))
        {
            AppUpdateStatusText = "未配置更新仓库,无法检查更新";
            return;
        }
        if (AppUpdateChecking || AppUpdateDownloading)
        {
            return;
        }

        AppUpdateChecking = true;
        AppUpdateStatusText = "正在检查应用更新…";
        AppUpdateAvailable = false;
        _pendingUpdate = null;
        try
        {
            var info = await AppServices.AppUpdate.CheckAsync(repo);
            if (info is null)
            {
                AppUpdateStatusText = "检查失败(仓库不存在或网络异常)";
                return;
            }

            var current = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
            var isNewer = AppUpdateService.IsNewer(current, info.Version);
            if (!isNewer)
            {
                AppUpdateStatusText = $"已是最新版本 ({info.Version})";
                return;
            }
            if (string.Equals(info.Version, AppServices.Settings.Current.SkipAppVersion, StringComparison.OrdinalIgnoreCase))
            {
                AppUpdateStatusText = $"已跳过版本 {info.Version}(发布更新版本时会再次提示)";
                return;
            }

            _pendingUpdate = info;
            AppUpdateAvailable = true;
            AppUpdateVersionText = $"发现新版本 {info.Version}";
            // HTML 回退通道拿不到文件大小(AssetSize=0),省略大小段
            AppUpdateStatusText = info.AssetSize > 0
                ? $"大小 {FormatAppUpdateSize(info.AssetSize)} · {info.AssetName}"
                : info.AssetName;
        }
        catch (Exception ex)
        {
            AppUpdateStatusText = $"检查失败: {ex.Message}";
        }
        finally
        {
            AppUpdateChecking = false;
        }
    }

    /// <summary>下载并安装应用更新(zip 绿色包解压替换;exe 安装包静默安装)。public:供主窗口自动升级调用。</summary>
    [RelayCommand]
    public async Task DownloadAppUpdateAsync()
    {
        if (_pendingUpdate is null || AppUpdateDownloading)
        {
            return;
        }

        AppUpdateDownloading = true;
        AppUpdateStatusText = "正在下载更新…";
        try
        {
            var progress = new Progress<double>(p => AppUpdateProgress = p * 100);
            var destDir = Path.Combine(AppServices.AppDataDir, "updates");
            var localPath = await AppServices.AppUpdate.DownloadAsync(
                _pendingUpdate.DownloadUrl, destDir, progress);
            if (localPath is null)
            {
                AppUpdateStatusText = "下载失败,请检查网络";
                return;
            }

            var fileName = Path.GetFileName(localPath);
            if (fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                // zip 绿色包:解压到 updates\&lt;版本&gt;\ 后延迟替换当前安装目录并重启(本进程退出后再执行)
                AppUpdateStatusText = "下载完成,正在准备替换…";
                if (!TryApplyZipUpdate(localPath, destDir))
                {
                    AppUpdateStatusText = "替换失败(请关闭程序后手动解压 zip 到安装目录覆盖)";
                }
                return;
            }

            AppUpdateStatusText = "下载完成,正在静默安装(自动替换并重启)…";
            // /DIR 显式锁定当前安装目录:自更新链路零选择、零歧义(zip 便携版无卸载注册表项,
            // 不能依赖 Inno 的 UsePreviousAppDir;手动双击 setup.exe 才走注册表自动定位)。
            var appDir = Path.GetDirectoryName(Environment.ProcessPath);
            var silentArgs = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART";
            if (!string.IsNullOrEmpty(appDir))
            {
                silentArgs += $" /DIR=\"{appDir}\"";
            }
            // 提权启发:安装目录可写(zip 便携版/自定义目录)→ 用户态静默更新,连 UAC 都不弹;
            // Program Files 等不可写 → runas 提权(安装器仍需管理员)。
            var writable = false;
            try
            {
                if (!string.IsNullOrEmpty(appDir))
                {
                    var probe = Path.Combine(appDir, $".writetest-{Environment.ProcessId}.tmp");
                    File.WriteAllText(probe, "");
                    File.Delete(probe);
                    writable = true;
                }
            }
            catch (Exception)
            {
                // 探测失败按不可写处理,走提权路径
            }
            var installer = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = localPath,
                Arguments = writable ? silentArgs + " /CURRENTUSER" : silentArgs,
                UseShellExecute = true,
                Verb = writable ? string.Empty : "runas",
            });
            if (installer is null)
            {
                AppUpdateStatusText = "安装程序启动失败";
                return;
            }

            // 重启兜底:Inno 的 RestartApplications 只重启"优雅退出"的应用,被 Restart Manager
            // 强杀的(本应用有关闭隐藏到托盘逻辑)不会拉起。用监视脚本等安装器进程退出后启动新版,
            // 本进程随即主动退出(安装器无文件锁,替换干净)。
            if (!string.IsNullOrEmpty(appDir))
            {
                var watcher = Path.Combine(destDir, "relaunch.cmd");
                File.WriteAllText(watcher, $$"""
                    @echo off
                    :wait
                    timeout /t 1 /nobreak >nul
                    tasklist /nh /fi "PID eq {{installer.Id}}" | find "{{installer.Id}}" >nul && goto wait
                    start "" "{{Path.Combine(appDir, "McKuro.exe")}}"
                    del "%~f0"
                    """, System.Text.Encoding.UTF8);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"\"{watcher}\"\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                });
            }
            if (Avalonia.Application.Current?.ApplicationLifetime
                is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
        }
        catch (Exception ex)
        {
            AppUpdateStatusText = $"下载失败: {ex.Message}";
        }
        finally
        {
            AppUpdateDownloading = false;
        }
    }

    /// <summary>解压 zip 绿色包并生成延迟替换脚本(等待本进程退出后 xcopy 替换安装目录并重启)。</summary>
    private bool TryApplyZipUpdate(string zipPath, string destDir)
    {
        try
        {
            var version = string.IsNullOrWhiteSpace(_pendingUpdate?.Version) ? "new" : _pendingUpdate.Version;
            var extractDir = Path.Combine(destDir, version);
            if (Directory.Exists(extractDir))
            {
                Directory.Delete(extractDir, true);
            }
            Directory.CreateDirectory(extractDir);
            System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, extractDir);

            var appDir = Path.GetDirectoryName(Environment.ProcessPath) ?? ".";
            var script = Path.Combine(destDir, "update.cmd");
            File.WriteAllText(script, $"""
                @echo off
                chcp 65001 >nul
                timeout /t 2 /nobreak >nul
                taskkill /im McKuro.exe /f >nul 2>&1
                timeout /t 1 /nobreak >nul
                xcopy /e /y /q "{extractDir}\*" "{appDir}\"
                start "" "{Path.Combine(appDir, "McKuro.exe")}"
                """, System.Text.Encoding.UTF8);

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = script,
                UseShellExecute = true,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
            });
            // 主程序退出,等待脚本完成替换后重新打开
            if (Avalonia.Application.Current?.ApplicationLifetime
                is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
            return true;
        }
        catch (Exception ex)
        {
            AppUpdateStatusText = $"替换失败: {ex.Message}";
            return false;
        }
    }

    /// <summary>跳过当前版本(对齐 Haiyu SkipAppUpdate)。</summary>
    [RelayCommand]
    private void SkipAppUpdate()
    {
        if (_pendingUpdate is null)
        {
            return;
        }
        AppServices.Settings.Current.SkipAppVersion = _pendingUpdate.Version;
        AppServices.Settings.Save();
        AppUpdateAvailable = false;
        AppUpdateVersionText = "";
        AppUpdateStatusText = $"已跳过版本 {_pendingUpdate.Version}";
    }

    private static string FormatAppUpdateSize(long bytes) =>
        bytes >= 1024 * 1024
            ? $"{bytes / 1024.0 / 1024.0:0.0} MB"
            : $"{bytes / 1024.0:0.0} KB";
}
