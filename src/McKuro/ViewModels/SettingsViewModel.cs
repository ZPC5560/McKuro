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

    // 游戏修复:跳过校验文件(对齐 Haiyu SkipVerifyFiles)
    [ObservableProperty]
    private string _skipVerifyInput = "";

    [ObservableProperty]
    private bool _autoSkipVerifyDelete = true;

    public ObservableCollection<string> SkipVerifyFiles { get; } = [];

    // 快捷键截图
    [ObservableProperty]
    private bool _captureEnabled;

    [ObservableProperty]
    private int _captureModifierIndex;

    [ObservableProperty]
    private int _captureKeyIndex;

    [ObservableProperty]
    private string _captureDir = "";

    [ObservableProperty]
    private string _captureStatusText = "";

    public ObservableCollection<string> ModifierKeys { get; } = ["Win", "Ctrl", "Alt", "Shift"];
    public ObservableCollection<string> CaptureKeys { get; } = ["F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12"];

    // 界面语言
    [ObservableProperty]
    private int _languageIndex;

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
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true,
            });
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
        _autoSkipVerifyDelete = s.AutoSkipVerifyDelete;
        foreach (var p in s.SkipVerifyFiles)
        {
            SkipVerifyFiles.Add(p);
        }
        _captureEnabled = s.CaptureEnabled;
        _captureModifierIndex = Math.Max(0, ModifierKeys.IndexOf(s.CaptureModifierKey));
        _captureKeyIndex = Math.Max(0, CaptureKeys.IndexOf(s.CaptureKey));
        _captureDir = s.ScreenCapturesDir;
        _languageIndex = Math.Max(0, Languages.IndexOf(LanguageLabel(s.Language)));
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
        s.SkipVerifyFiles = [.. SkipVerifyFiles];
        s.AutoSkipVerifyDelete = AutoSkipVerifyDelete;
        s.CaptureEnabled = CaptureEnabled;
        s.CaptureModifierKey = ModifierKeys[Math.Clamp(CaptureModifierIndex, 0, ModifierKeys.Count - 1)];
        s.CaptureKey = CaptureKeys[Math.Clamp(CaptureKeyIndex, 0, CaptureKeys.Count - 1)];
        s.ScreenCapturesDir = CaptureDir;
        s.Language = LanguageIndex == 1 ? "en-US" : "zh-Hans";
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

        // 主题即时生效(对齐 Haiyu 设置主题)
        if (Avalonia.Application.Current is { } app)
        {
            app.RequestedThemeVariant = s.Theme switch
            {
                "Light" => Avalonia.Styling.ThemeVariant.Light,
                "Dark" => Avalonia.Styling.ThemeVariant.Dark,
                _ => Avalonia.Styling.ThemeVariant.Default,
            };
        }

        StatusText = "设置已保存";
    }

    [RelayCommand]
    private async Task BrowseCaptureDirAsync()
    {
        var lifetime = Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
        var topLevel = lifetime?.MainWindow;
        if (topLevel is null)
        {
            return;
        }

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
        {
            Title = "选择截图保存目录",
            AllowMultiple = false,
        });
        if (folders.Count > 0)
        {
            CaptureDir = folders[0].Path.LocalPath;
        }
    }

    /// <summary>由主窗口热键回调调用,提示截图已保存。</summary>
    public void NotifyCaptureSaved(string path)
    {
        CaptureStatusText = $"截图已保存: {path}";
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
