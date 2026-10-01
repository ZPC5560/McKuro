using System.Diagnostics;
using CommunityToolkit.Mvvm.Input;
using McKuro.Core.Services.Game;
using McKuro.Core.Services.Infrastructure;
using McKuro.Services;

namespace McKuro.ViewModels;

/// <summary>
/// 启动器页左侧「菜单」按钮展开的功能项(对齐官方启动器的左上角菜单):
/// 游戏设置 / 检查更新 / 修复游戏 / 创建快捷方式 / 强制退出游戏 / 查看截图。
/// </summary>
public sealed partial class LauncherViewModel
{
    /// <summary>菜单是否展开。</summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool _isGameMenuOpen;

    /// <summary>切换左侧功能菜单。</summary>
    [RelayCommand]
    private void ToggleGameMenu() => IsGameMenuOpen = !IsGameMenuOpen;

    /// <summary>关闭左侧功能菜单(选中任一项后调用)。</summary>
    private void CloseGameMenu() => IsGameMenuOpen = false;

    /// <summary>检查游戏更新。</summary>
    [RelayCommand]
    private async Task CheckGameUpdateAsync()
    {
        CloseGameMenu();
        await CheckUpdateAsync();
    }

    /// <summary>修复游戏(复用既有修复流程)。</summary>
    [RelayCommand]
    private async Task RepairGameFromMenuAsync()
    {
        CloseGameMenu();
        await RepairGameAsync();
    }

    /// <summary>创建桌面快捷方式(指向启动器自身)。</summary>
    [RelayCommand]
    private void CreateDesktopShortcut()
    {
        CloseGameMenu();
        if (!DesktopShortcutService.IsSupported)
        {
            StatusText = LanguageService.Format("Launcher.ShortcutUnsupported");
            return;
        }

        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
        {
            StatusText = LanguageService.Format("Launcher.ShortcutFailed");
            return;
        }

        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrEmpty(desktop))
        {
            StatusText = LanguageService.Format("Launcher.ShortcutFailed");
            return;
        }

        var name = LanguageService.Format("Launcher.ShortcutName");
        var path = Path.Combine(desktop, name + ".lnk");
        var created = DesktopShortcutService.CreateShortcut(
            path,
            exe,
            workingDir: Path.GetDirectoryName(exe),
            description: name);

        StatusText = created is not null
            ? LanguageService.Format("Launcher.ShortcutCreated")
            : LanguageService.Format("Launcher.ShortcutFailed");
    }

    /// <summary>游戏是否正在运行(决定「强制退出游戏」是否可用)。</summary>
    public bool CanForceExitGame => AppServices.GameMonitor.State != GameSessionState.Idle;

    /// <summary>强制退出游戏(结束游戏进程)。</summary>
    [RelayCommand]
    private void ForceExitGame()
    {
        CloseGameMenu();
        if (!CanForceExitGame)
        {
            StatusText = LanguageService.Format("Launcher.NoGameRunning");
            return;
        }

        var killed = KillGameProcesses();
        AppServices.GameMonitor.Reset();
        StatusText = killed > 0
            ? LanguageService.Format("Launcher.GameForceExited")
            : LanguageService.Format("Launcher.GameForceExitFailed");
    }

    /// <summary>结束所有命中的游戏进程(客户端 + 根 exe)。</summary>
    private int KillGameProcesses()
    {
        int killed = 0;
        foreach (var name in BuildGameProcessNames())
        {
            Process[] procs;
            try
            {
                procs = Process.GetProcessesByName(name);
            }
            catch (Exception)
            {
                continue;
            }

            foreach (var p in procs)
            {
                try
                {
                    p.Kill(entireProcessTree: true);
                    killed++;
                }
                catch (Exception)
                {
                    // 单个进程结束失败(已退出/权限不足)不阻断其余
                }
                finally
                {
                    p.Dispose();
                }
            }
        }
        return killed;
    }
}
