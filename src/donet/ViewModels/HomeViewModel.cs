using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using donet.Core.Services.Game;
using donet.Services;

namespace donet.ViewModels;

/// <summary>主页:欢迎横幅 + 快捷操作(Haiyu HomePage 风格)。</summary>
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

    public HomeViewModel()
    {
        RefreshState();
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
    }

    [RelayCommand]
    private void Refresh() => RefreshState();
}
