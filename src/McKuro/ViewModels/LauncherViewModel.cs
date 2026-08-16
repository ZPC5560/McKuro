using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using McKuro.Core.Models.Game;
using McKuro.Core.Services.Game;
using McKuro.Services;

namespace McKuro.ViewModels;

/// <summary>启动器页:游戏状态、检查更新、预下载、安装、启动、封面轮播。</summary>
public sealed partial class LauncherViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _statusText = "就绪";

    [ObservableProperty]
    private string _gameVersionText = "未检测";

    [ObservableProperty]
    private string _serverVersionText = "-";

    [ObservableProperty]
    private string _installStateText = "未安装";

    [ObservableProperty]
    private string _predownloadStateText = "";

    [ObservableProperty]
    private bool _isInstalled;

    [ObservableProperty]
    private bool _hasUpdate;

    [ObservableProperty]
    private bool _hasPredownload;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isDownloading;

    /// <summary>下载是否处于暂停(对齐 Haiyu DownloadState.IsPaused)。</summary>
    public bool DownloadPaused => AppServices.Downloader.IsPaused;

    /// <summary>暂停/继续按钮文案。</summary>
    public string PauseResumeText => DownloadPaused ? "继续下载" : "暂停下载";

    /// <summary>是否显示暂停/继续按钮(下载进行中)。</summary>
    public bool ShowPauseResume => IsDownloading;

    /// <summary>已安装且有更新:显示「安装更新」按钮(未安装时只显示「下载安装」)。</summary>
    public bool ShowInstallUpdate => IsInstalled && HasUpdate;

    partial void OnIsInstalledChanged(bool value) => OnPropertyChanged(nameof(ShowInstallUpdate));

    partial void OnHasUpdateChanged(bool value) => OnPropertyChanged(nameof(ShowInstallUpdate));

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private string _progressText = "";

    [ObservableProperty]
    private long _updateBytes;

    [ObservableProperty]
    private string _currentFileText = "";

    [ObservableProperty]
    private string _speedText = "";

    [ObservableProperty]
    private string _bytesText = "";

    /// <summary>封面轮播图(官方启动器信息)。</summary>
    public ObservableCollection<SlideshowItem> Slideshows { get; } = [];

    /// <summary>轮播图是否显示(可隐藏,参考 Haiyu 左下角卡片)。</summary>
    [ObservableProperty]
    private bool _isSlideShowVisible = true;

    /// <summary>切换轮播图显示/隐藏。</summary>
    [RelayCommand]
    private void ToggleSlideShow() => IsSlideShowVisible = !IsSlideShowVisible;

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

    /// <summary>服务器渠道列表(与设置页一致)。</summary>
    public IReadOnlyList<string> Servers { get; } =
    [
        "自动检测", "官服", "B站", "WeGame", "国际服",
    ];

    [ObservableProperty]
    private int _selectedServerIndex;

    private UpdateCheckResult? _lastCheck;

    public LauncherViewModel()
    {
        SelectedServerIndex = AppServices.Settings.Current.ServerType switch
        {
            GameServerType.Official => 1,
            GameServerType.Bilibili => 2,
            GameServerType.WeGame => 3,
            GameServerType.Global => 4,
            _ => 0,
        };
        RefreshState();
        VideoEnabled = AppServices.Settings.Current.BackgroundVideoEnabled;
        _ = LoadLauncherInfoAsync();

        // 订阅游戏目录变更(设置页选择目录后) → 自动识别加载
        WeakReferenceMessenger.Default.Register<LauncherViewModel, GameDirectoryChangedMessage>(this, static (r, m) =>
        {
            Dispatcher.UIThread.Post(() => r.OnGameDirectoryChanged(m.Value));
        });

        // 已设置目录则自动检查更新(对齐 Haiyu 加载后自动检查)
        if (IsInstalled)
        {
            _ = CheckUpdateAsync();
        }
    }

    /// <summary>游戏目录变更处理:重新识别并自动加载(对齐 Haiyu 选目录后的自动识别)。</summary>
    private void OnGameDirectoryChanged(string gameRoot)
    {
        RefreshState();
        _ = LoadLauncherInfoAsync();
        if (IsInstalled)
        {
            _ = CheckUpdateAsync();
        }
        else
        {
            StatusText = string.IsNullOrEmpty(gameRoot) ? "未设置游戏目录" : $"目录未包含 {GamePathResolver.ExeRootName},请确认选择正确";
        }
    }

    private GameServerType SelectedServerType => SelectedServerIndex switch
    {
        1 => GameServerType.Official,
        2 => GameServerType.Bilibili,
        3 => GameServerType.WeGame,
        4 => GameServerType.Global,
        _ => GameServerType.Unknown,
    };

    private GameServerType ServerType =>
        SelectedServerType != GameServerType.Unknown
            ? SelectedServerType
            : AppServices.Paths.DetectServerType();

    /// <summary>拉取官方封面轮播图、公告与背景封面(失败静默,不影响其余功能)。</summary>
    private async Task LoadLauncherInfoAsync()
    {
        try
        {
            var server = ServerType;
            var info = await AppServices.LauncherInfo.GetLauncherInfoAsync(server);
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

            // 背景封面(首帧图 + 视频 URL + 版本 Logo)
            var background = await AppServices.LauncherInfo.GetLauncherBackgroundAsync(server);
            if (background is not null)
            {
                if (!string.IsNullOrWhiteSpace(background.FirstFrameImage))
                {
                    BackgroundImageUrl = background.FirstFrameImage;
                }

                if (!string.IsNullOrWhiteSpace(background.BackgroundFile))
                {
                    BackgroundVideoUrl = background.BackgroundFile;
                }

                if (!string.IsNullOrWhiteSpace(background.Slogan))
                {
                    VersionLogoUrl = background.Slogan;
                }
            }
        }
        catch (Exception)
        {
            // 静默失败
        }
    }

    private void RefreshState()
    {
        var paths = AppServices.Paths;
        IsInstalled = paths.IsGameInstalled;
        GameVersionText = IsInstalled ? "已安装" : "未安装";
        InstallStateText = IsInstalled ? "游戏已就绪" : "尚未安装游戏";
        RefreshGraphicsComponents();

        if (!IsInstalled)
        {
            HasUpdate = false;
            HasPredownload = false;
            ServerVersionText = "-";
            PredownloadStateText = "";
        }
    }

    /// <summary>DLSS/XeSS 组件版本显示(对齐 Haiyu GetLocalDLSSAsync)。</summary>
    public string GraphicsComponentsText { get; private set; } = "";

    private void RefreshGraphicsComponents()
    {
        var versions = AppServices.GameUpdater.GetLocalGraphicsComponentVersions();
        GraphicsComponentsText = versions.Count == 0
            ? ""
            : string.Join(" · ", versions.Select(v => $"{v.DisplayName} v{v.Version}"));
        OnPropertyChanged(nameof(GraphicsComponentsText));
    }

    [RelayCommand]
    private async Task CheckUpdateAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusText = "正在检查更新…";
        try
        {
            var result = await AppServices.GameUpdater.CheckUpdateAsync(ServerType);
            _lastCheck = result;
            if (!result.Success)
            {
                // 检查失败:清除旧的有更新横幅,避免残留误导
                HasUpdate = false;
                StatusText = result.Message ?? "检查失败";
                return;
            }

            ServerVersionText = result.ServerVersion ?? "-";
            // 本地版本(对齐 Haiyu 的 DisplayVersion)
            GameVersionText = result.InstalledVersion is { Length: > 0 }
                ? $"v{result.InstalledVersion}"
                : (result.NotInstalled ? "未安装" : "已安装");
            HasUpdate = result.HasUpdate;
            HasPredownload = result.HasPredownload;
            PredownloadStateText = result.HasPredownload
                ? $"可预下载:版本 {result.PredownloadVersion}"
                : "";

            if (result.NotInstalled)
            {
                InstallStateText = "未安装游戏,点击「下载安装」安装";
            }
            else if (result.HasUpdate)
            {
                InstallStateText = $"发现新版本 {result.ServerVersion}" +
                    (result.TotalBytes > 0 ? $" (需下载 {FormatSize(result.TotalBytes)})" : "");
            }
            else
            {
                InstallStateText = $"游戏已是最新版本 {result.ServerVersion}";
            }

            StatusText = result.HasUpdate ? "有可用更新" : "游戏已是最新";
        }
        catch (Exception ex)
        {
            StatusText = $"检查更新失败: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task PreDownloadAsync()
    {
        if (IsBusy || IsDownloading)
        {
            return;
        }

        IsDownloading = true;
        ProgressPercent = 0;
        ProgressText = "正在预下载…";
        StatusText = "预下载中(不会影响当前游戏文件)";

        var progress = new Progress<DownloadProgress>(p =>
        {
            ProgressPercent = p.Percent * 100;
            ProgressText = $"{p.FileIndex}/{p.FileTotal} 文件 · {FormatSize(p.BytesDownloaded)}/{FormatSize(p.BytesTotal)} · {FormatSpeed(p.SpeedBps)}";
            CurrentFileText = p.CurrentFile;
            SpeedText = FormatSpeed(p.SpeedBps);
            BytesText = $"{FormatSize(p.BytesDownloaded)} / {FormatSize(p.BytesTotal)}";
        });

        try
        {
            var (success, _, message) = await AppServices.GameUpdater.PreDownloadAsync(ServerType, progress);
            if (success)
            {
                PredownloadStateText = "预下载完成,可点击「安装更新」";
                StatusText = "预下载完成";
            }
            else
            {
                StatusText = message ?? "预下载失败";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"预下载失败: {ex.Message}";
        }
        finally
        {
            IsDownloading = false;
            ProgressText = "";
        }
    }

    [RelayCommand]
    private async Task InstallAsync()
    {
        if (IsBusy || IsDownloading)
        {
            return;
        }

        IsDownloading = true;
        ProgressPercent = 0;
        StatusText = "正在安装/更新…";

        var progress = new Progress<DownloadProgress>(p =>
        {
            ProgressPercent = p.Percent * 100;
            ProgressText = $"{p.FileIndex}/{p.FileTotal} 文件 · {FormatSize(p.BytesDownloaded)}/{FormatSize(p.BytesTotal)}";
            CurrentFileText = p.CurrentFile;
            SpeedText = FormatSpeed(p.SpeedBps);
            BytesText = $"{FormatSize(p.BytesDownloaded)} / {FormatSize(p.BytesTotal)}";
        });

        try
        {
            var (success, message) = await AppServices.GameUpdater.InstallAsync(ServerType, progress);
            StatusText = success ? (message ?? "安装完成") : (message ?? "安装失败");
            if (success)
            {
                RefreshState();
                await CheckUpdateAsync();
            }
        }
        catch (Exception ex)
        {
            StatusText = $"安装失败: {ex.Message}";
        }
        finally
        {
            IsDownloading = false;
            ProgressText = "";
        }
    }

    [RelayCommand]
    private void TogglePauseDownload()
    {
        if (AppServices.Downloader.IsPaused)
        {
            AppServices.Downloader.Resume();
        }
        else
        {
            AppServices.Downloader.Pause();
        }
        OnPropertyChanged(nameof(DownloadPaused));
        OnPropertyChanged(nameof(PauseResumeText));
    }

    [RelayCommand]
    private async Task RepairGameAsync()
    {
        if (IsBusy || IsDownloading)
        {
            return;
        }

        IsDownloading = true;
        ProgressPercent = 0;
        ProgressText = "正在修复游戏…";
        StatusText = "修复中(重新下载缺失/损坏文件)";

        var progress = new Progress<DownloadProgress>(p =>
        {
            ProgressPercent = p.Percent * 100;
            ProgressText = $"{p.FileIndex}/{p.FileTotal} 文件 · {FormatSize(p.BytesDownloaded)}/{FormatSize(p.BytesTotal)}";
            CurrentFileText = p.CurrentFile;
            SpeedText = FormatSpeed(p.SpeedBps);
            BytesText = $"{FormatSize(p.BytesDownloaded)} / {FormatSize(p.BytesTotal)}";
        });

        try
        {
            // 对齐 Haiyu:修复游戏跳过用户配置的校验文件,并按设置决定是否删除被跳过的文件
            var s = AppServices.Settings.Current;
            var skip = new HashSet<string>(s.SkipVerifyFiles, StringComparer.OrdinalIgnoreCase);
            var (success, message) = await AppServices.GameUpdater.RepairGameAsync(
                ServerType, skip, s.AutoSkipVerifyDelete, progress);
            StatusText = message ?? (success ? "修复完成" : "修复失败");
            if (success)
            {
                RefreshState();
                await CheckUpdateAsync();
            }
        }
        catch (Exception ex)
        {
            StatusText = $"修复失败: {ex.Message}";
        }
        finally
        {
            IsDownloading = false;
            ProgressText = "";
        }
    }

    [RelayCommand]
    private void Launch()
    {
        var ok = AppServices.GameUpdater.LaunchGame(out var error);
        StatusText = ok ? "游戏已启动" : $"启动失败: {error}";

        // 对齐 Haiyu 的"启动后可关闭主界面":可选最小化主窗口
        if (ok && AppServices.Settings.Current.MinimizeOnLaunch)
        {
            var lifetime = Avalonia.Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
            if (lifetime?.MainWindow is { WindowState: not Avalonia.Controls.WindowState.Minimized } w)
            {
                w.WindowState = Avalonia.Controls.WindowState.Minimized;
            }
        }
    }

    [RelayCommand]
    private void OpenGameFolder()
    {
        var root = AppServices.Paths.GameRootDir;
        if (!string.IsNullOrEmpty(root) && Directory.Exists(root))
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = root,
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                StatusText = $"打开目录失败: {ex.Message}";
            }
        }
        else
        {
            StatusText = "未设置游戏目录";
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

    private static string FormatSpeed(double bps)
    {
        if (bps <= 0)
        {
            return "--";
        }
        string[] units = ["B/s", "KB/s", "MB/s", "GB/s"];
        double v = bps;
        int unit = 0;
        while (v >= 1024 && unit < units.Length - 1)
        {
            v /= 1024;
            unit++;
        }
        return $"{v:0.#} {units[unit]}";
    }
}
