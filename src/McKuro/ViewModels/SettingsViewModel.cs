using System.Collections.ObjectModel;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using McKuro.Core.Services.Game;
using McKuro.Core.Services.Update;
using McKuro.Services;

namespace McKuro.ViewModels;

/// <summary>设置页。</summary>
public sealed partial class SettingsViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _gameRootDir;

    [ObservableProperty]
    private string _serverTypeText = "";

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private int _downloadConcurrency;

    /// <summary>下载速度限制(MB/s,0 = 不限;对齐 Haiyu LimitSpeed)。</summary>
    [ObservableProperty]
    private int _limitSpeedMbps;

    // 游戏启动参数(对齐 Haiyu StartGameOption)
    [ObservableProperty]
    private bool _useDx11;

    [ObservableProperty]
    private bool _disableDlss;

    [ObservableProperty]
    private string _startGameArguments = "";

    [ObservableProperty]
    private string _startGameExeName = "";

    /// <summary>启动游戏后最小化主窗口。</summary>
    [ObservableProperty]
    private bool _minimizeOnLaunch;

    [ObservableProperty]
    private bool _backgroundVideoEnabled;

    /// <summary>背景视频开关即时保存(切换立即生效,无需点保存)。</summary>
    partial void OnBackgroundVideoEnabledChanged(bool value)
    {
        AppServices.Settings.Current.BackgroundVideoEnabled = value;
        AppServices.Settings.Save();
    }

    // 动态壁纸与玻璃主题
    [ObservableProperty]
    private string _wallpaperPath = "";

    [ObservableProperty]
    private string _wallpaperStatusText = "";

    [ObservableProperty]
    private bool _hasWallpaper;

    [ObservableProperty]
    private bool _dynamicPaletteEnabled;

    [ObservableProperty]
    private int _glassQualityIndex;

    // 游戏修复:跳过校验文件(对齐 Haiyu SkipVerifyFiles)
    [ObservableProperty]
    private string _skipVerifyInput = "";

    [ObservableProperty]
    private bool _autoSkipVerifyDelete = true;

    public ObservableCollection<string> SkipVerifyFiles { get; } = [];

    // 界面语言
    [ObservableProperty]
    private int _languageIndex;

    // 自动游戏签到(打开软件后自动签到;与签到页开关同步同一配置)
    [ObservableProperty]
    private bool _autoSignEnabled;

    /// <summary>自动签到开关即时保存(与签到页一致),无需点保存。</summary>
    partial void OnAutoSignEnabledChanged(bool value)
    {
        AppServices.Settings.Current.AutoSignEnabled = value;
        AppServices.Settings.Save();
    }

    // 应用自更新(对齐 Haiyu UpdateAppViewModel)
    [ObservableProperty]
    private string _appUpdateRepo = "";

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

    public string PlatformCapabilityText => AppServices.Capabilities.GameSupportText;

    public string PlatformVideoText => AppServices.Capabilities.VideoSupportText;

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

    public ObservableCollection<string> GlassQualities { get; } = ["自动", "高质量", "低性能模式"];

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
        _ = AppServices.ThemePalette.ApplyCurrentAsync();
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
        _backgroundVideoEnabled = s.BackgroundVideoEnabled;
        _wallpaperPath = AppServices.Wallpaper.CurrentWallpaperPath;
        _hasWallpaper = AppServices.Wallpaper.HasWallpaper;
        _dynamicPaletteEnabled = s.DynamicPaletteEnabled;
        _glassQualityIndex = s.GlassQuality switch
        {
            "High" => 1,
            "Low" => 2,
            _ => 0,
        };
        _wallpaperStatusText = AppServices.Wallpaper.HasWallpaper ? "正在使用自定义壁纸" : "使用默认背景";
        _autoSkipVerifyDelete = s.AutoSkipVerifyDelete;
        foreach (var p in s.SkipVerifyFiles)
        {
            SkipVerifyFiles.Add(p);
        }
        _languageIndex = Math.Max(0, Languages.IndexOf(LanguageLabel(s.Language)));
        _autoSignEnabled = s.AutoSignEnabled;
        _appUpdateRepo = s.AppUpdateRepo;
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

    partial void OnSelectedServerIndexChanged(int value) => UpdateServerTypeText();

    private void UpdateServerTypeText()
    {
        ServerTypeText = ServerTypes[Math.Clamp(SelectedServerIndex, 0, ServerTypes.Count - 1)];
    }

    partial void OnDynamicPaletteEnabledChanged(bool value)
    {
        AppServices.Settings.Current.DynamicPaletteEnabled = value;
        AppServices.Settings.Save();
        _ = AppServices.ThemePalette.ApplyCurrentAsync();
    }

    partial void OnGlassQualityIndexChanged(int value)
    {
        AppServices.Settings.Current.GlassQuality = value switch
        {
            1 => "High",
            2 => "Low",
            _ => "Auto",
        };
        AppServices.Settings.Save();
        _ = AppServices.ThemePalette.ApplyCurrentAsync();
    }

    [RelayCommand]
    private async Task SelectWallpaperAsync()
    {
        var lifetime = Avalonia.Application.Current?.ApplicationLifetime
            as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
        var topLevel = lifetime?.MainWindow;
        if (topLevel is null)
        {
            return;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "选择主页壁纸",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new Avalonia.Platform.Storage.FilePickerFileType("图片")
                {
                    Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp"],
                    AppleUniformTypeIdentifiers = ["public.png", "public.jpeg", "org.webmproject.webp"],
                    MimeTypes = ["image/png", "image/jpeg", "image/webp"],
                },
            ],
        });
        if (files.Count == 0)
        {
            return;
        }

        try
        {
            WallpaperStatusText = "正在处理壁纸…";
            WallpaperPath = await AppServices.Wallpaper.SetWallpaperAsync(files[0].Path.LocalPath);
            HasWallpaper = true;
            await AppServices.ThemePalette.ApplyCurrentAsync();
            WallpaperStatusText = "壁纸已应用，应用强调色已同步";
        }
        catch (Exception ex)
        {
            WallpaperStatusText = $"壁纸应用失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ResetWallpaperAsync()
    {
        AppServices.Wallpaper.ClearWallpaper();
        WallpaperPath = "";
        HasWallpaper = false;
        await AppServices.ThemePalette.ApplyCurrentAsync();
        WallpaperStatusText = "已恢复默认背景";
    }

    /// <summary>打开壁纸存储目录(跨平台;macOS 用 open)。</summary>
    [RelayCommand]
    private void OpenWallpaperDir()
    {
        var dir = Path.Combine(AppServices.AppDataDir, "wallpapers");
        if (!Directory.Exists(dir))
        {
            WallpaperStatusText = "壁纸目录不存在";
            return;
        }
        if (!AppServices.OpenInFileManager(dir))
        {
            WallpaperStatusText = "打开壁纸目录失败";
        }
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
        GameRootDir = dir;

        // 校验目录包含游戏主程序(对齐 Haiyu 选目录后自动识别:无效目录直接提示)
        if (!File.Exists(Path.Combine(dir, GamePathResolver.ExeRootName)))
        {
            StatusText = $"目录未包含 {GamePathResolver.ExeRootName},请选择正确的游戏安装目录";
            return;
        }

        // 保存并通知启动器页自动识别加载(检测版本/渠道,自动检查更新)
        var s = AppServices.Settings.Current;
        s.GameRootDir = dir;
        s.ServerType = GameServerType.Unknown; // 交给自动检测
        SelectedServerIndex = 0;
        AppServices.Settings.Save();
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
        StatusText = $"已添加跳过文件: {path}(保存后生效)";
    }

    [RelayCommand]
    private void RemoveSkipVerifyFile(string path)
    {
        SkipVerifyFiles.Remove(path);
        StatusText = $"已移除跳过文件: {path}(保存后生效)";
    }

    [RelayCommand]
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
        s.BackgroundVideoEnabled = BackgroundVideoEnabled;
        s.WallpaperPath = WallpaperPath;
        s.DynamicPaletteEnabled = DynamicPaletteEnabled;
        s.GlassQuality = GlassQualityIndex switch
        {
            1 => "High",
            2 => "Low",
            _ => "Auto",
        };
        s.SkipVerifyFiles = [.. SkipVerifyFiles];
        s.AutoSkipVerifyDelete = AutoSkipVerifyDelete;
        s.Language = LanguageIndex == 1 ? "en-US" : "zh-Hans";
        s.AutoSignEnabled = AutoSignEnabled;
        s.Theme = ThemeIndex switch
        {
            1 => "Light",
            2 => "Dark",
            _ => "Default",
        };
        s.AppUpdateRepo = AppUpdateRepo.Trim();
        AppServices.Settings.Save();

        // 重新应用并发数与限速(无需重启);路径解析器已通过 Func<string?> 自动读取最新值
        AppServices.Downloader.SetConcurrency(s.DownloadConcurrency);
        AppServices.Downloader.SetSpeedLimit((long)s.LimitSpeedMbps * 1024 * 1024);

        // 主题即时生效(对齐 Haiyu 设置主题;选择时已即时应用,保存时再确保一致)
        ApplyThemeVariant(s.Theme);

        StatusText = "设置已保存";
    }

    // ---------- 应用自更新(对齐 Haiyu UpdateAppViewModel) ----------

    /// <summary>检查应用更新(GitHub Releases latest;跳过已跳过的版本)。</summary>
    [RelayCommand]
    private async Task CheckAppUpdateAsync()
    {
        var repo = AppUpdateRepo.Trim();
        if (string.IsNullOrWhiteSpace(repo))
        {
            AppUpdateStatusText = "请先填写 GitHub 仓库(owner/repo)";
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
                AppUpdateStatusText = $"已跳过版本 {info.Version}(可在仓库地址清空后保存重置)";
                return;
            }

            _pendingUpdate = info;
            AppUpdateAvailable = true;
            AppUpdateVersionText = $"发现新版本 {info.Version}";
            AppUpdateStatusText = $"大小 {FormatAppUpdateSize(info.AssetSize)} · {info.AssetName}";
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

    /// <summary>下载并安装应用更新(下载到临时目录后以管理员启动安装包)。</summary>
    [RelayCommand]
    private async Task DownloadAppUpdateAsync()
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

            AppUpdateStatusText = "下载完成,正在启动安装程序…";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = localPath,
                UseShellExecute = true,
                Verb = "runas", // 管理员安装(对齐 Haiyu)
            });
            // 主程序退出,等待安装完成后重新打开
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
