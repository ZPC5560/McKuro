using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using donet.Core.Services.Game;
using donet.Services;

namespace donet.ViewModels;

/// <summary>设置页。</summary>
public sealed partial class SettingsViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _gameRootDir;

    [ObservableProperty]
    private string _kujiequToken;

    [ObservableProperty]
    private string _roleId;

    [ObservableProperty]
    private string _serverTypeText = "";

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private int _downloadConcurrency;

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
        _kujiequToken = s.KujiequToken;
        _roleId = s.RoleId;
        _downloadConcurrency = s.DownloadConcurrency;
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
        if (folders.Count > 0)
        {
            GameRootDir = folders[0].Path.LocalPath;
        }
    }

    [RelayCommand]
    private void Save()
    {
        var s = AppServices.Settings.Current;
        s.GameRootDir = GameRootDir;
        s.KujiequToken = KujiequToken;
        s.RoleId = RoleId;
        s.ServerType = SelectedServerIndex switch
        {
            1 => GameServerType.Official,
            2 => GameServerType.Bilibili,
            3 => GameServerType.WeGame,
            4 => GameServerType.Global,
            _ => GameServerType.Unknown,
        };
        s.DownloadConcurrency = Math.Clamp(DownloadConcurrency, 1, 32);
        AppServices.Settings.Save();

        // 重新应用并发数与路径(无需重启)
        AppServices.Downloader.SetConcurrency(s.DownloadConcurrency);
        AppServices.Paths = new GamePathResolver(() => AppServices.Settings.Current.GameRootDir);
        StatusText = "设置已保存";
    }
}
