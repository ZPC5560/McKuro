using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using donet.Core.Services.Game;
using donet.Services;

namespace donet.ViewModels;

/// <summary>工具箱页:常用工具入口(Haiyu ToolkitPage 风格)。</summary>
public sealed partial class ToolkitViewModel : ViewModelBase
{
    private readonly LauncherViewModel _launcher;
    private readonly GachaViewModel _gacha;
    private readonly RolesViewModel _roles;

    public ToolkitViewModel(LauncherViewModel launcher, GachaViewModel gacha, RolesViewModel roles)
    {
        _launcher = launcher;
        _gacha = gacha;
        _roles = roles;
    }

    public ObservableCollection<object> ToolItems { get; } = [];

    /// <summary>由主窗口调用的导航回调。</summary>
    public Action<NavigationItem>? NavigateRequested { get; set; }

    [RelayCommand]
    private void OpenGachaAnalysis() => NavigateRequested?.Invoke(new NavigationItem
    {
        Title = "抽卡分析",
        Icon = "🎴",
        ViewModel = _gacha,
    });

    [RelayCommand]
    private void OpenRoles() => NavigateRequested?.Invoke(new NavigationItem
    {
        Title = "角色数据",
        Icon = "👤",
        ViewModel = _roles,
    });

    [RelayCommand]
    private void OpenGamePage() => NavigateRequested?.Invoke(new NavigationItem
    {
        Title = "鸣潮",
        Icon = "🌊",
        ViewModel = _launcher,
    });

    [RelayCommand]
    private void OpenGameFolder()
    {
        var path = AppServices.Paths.GameRootDir;
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
        {
            return;
        }
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
        }
        catch (Exception)
        {
            // 忽略
        }
    }
}
