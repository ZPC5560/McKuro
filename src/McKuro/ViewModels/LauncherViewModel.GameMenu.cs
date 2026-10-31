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

    /// <summary>
    /// 检查重复文件(手动体积自检)。
    /// <para>
    /// 与其它菜单项不同:这里<b>先不关菜单</b>,等扫描真正开始(状态栏已出现"正在检查…")再关。
    /// 原先所有菜单项都"点了立刻关面板",而检查重复文件需要枚举整个游戏目录(数万文件,数秒到数十秒),
    /// 面板一关、状态栏又没绑定,用户看到的就是"点了一下什么都没发生"(2026-10 反馈)。
    /// </para>
    /// </summary>
    [RelayCommand]
    private async Task CheckResidueFromMenuAsync()
    {
        var task = CheckResidueCoreAsync(promptAlways: true, skipBusyGuard: false);
        // 给状态栏一帧时间把"正在检查…"画出来,再收起菜单
        await Task.Yield();
        CloseGameMenu();
        await task;
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

        // 成功 = 一次性结论(用户已经拿到快捷方式,不需要再做什么)→ 自动消失;
        // 失败要常驻,让用户看见"为什么没建成"。
        if (created is not null)
        {
            SetTransientStatus(LanguageService.Format("Launcher.ShortcutCreated"));
        }
        else
        {
            StatusText = LanguageService.Format("Launcher.ShortcutFailed");
        }
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

    /// <summary>
    /// 强制退出允许匹配的进程名:只认游戏自己的 exe(根 exe + 客户端 exe,外加用户在设置里
    /// 指定的、位于游戏目录内的启动 exe)。
    /// <para>
    /// <b>不复用 <see cref="BuildGameProcessNames"/>(L27)</b>:进程监控为了识别「游戏中」会把自定义启动程序
    /// 也加进名单,而它可能是 python / powershell / cmd 这类通用宿主 —— 按名字 Kill 整棵进程树会误杀
    /// 系统里所有同名的无关进程。Core 的 LaunchGame 目前不回传 PID,没法只杀"本次启动的那棵树",
    /// 所以退而求其次:只按已知的游戏 exe 名匹配。
    /// </para>
    /// </summary>
    private static IReadOnlyList<string> BuildKillableProcessNames()
    {
        var names = new List<string>
        {
            Path.GetFileNameWithoutExtension(GamePathResolver.ExeRootName),
            Path.GetFileNameWithoutExtension(GamePathResolver.ExeClientRelative),
        };

        // 自定义启动程序不在此列;普通启动的 exe 只有位于游戏目录内才视为游戏进程
        if (!AppServices.Settings.Current.CustomLauncherEnabled
            && AppServices.GameUpdater.ResolveLaunchExePath() is { Length: > 0 } launched
            && AppServices.Paths.GameRootDir is { Length: > 0 } root
            && IsPathUnderRoot(root, launched))
        {
            var name = Path.GetFileNameWithoutExtension(launched);
            if (!names.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                names.Add(name);
            }
        }
        return names;
    }

    /// <summary>路径是否位于 root 之内(路径格式非法时按"不在"处理,不抛异常)。</summary>
    private static bool IsPathUnderRoot(string root, string path)
    {
        try
        {
            return McKuro.Core.Infrastructure.ArchivePathGuard.IsUnderRoot(
                Path.GetFullPath(root), Path.GetFullPath(path));
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>结束命中的游戏进程(只按游戏自身 exe 名匹配,见 <see cref="BuildKillableProcessNames"/>)。</summary>
    private int KillGameProcesses()
    {
        int killed = 0;
        foreach (var name in BuildKillableProcessNames())
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
