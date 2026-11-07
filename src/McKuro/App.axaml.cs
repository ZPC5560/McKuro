using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using McKuro.Core.Models.Kuro;
using McKuro.Services;
using McKuro.ViewModels;
using McKuro.Views;

namespace McKuro;

public partial class App : Application
{
    private DailyTaskScheduler? _scheduler;
    private ReminderScheduler? _reminderScheduler;


    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// 已有库街区账号时的按需预热:后台校验当前账号登录态,确认失效才预热 WebView2。
    /// <para>
    /// 判据与账号页/提醒调度器完全一致(<see cref="AccountLoginStateLogic.ProbeKuroSession"/>):
    /// 只有服务端<b>明确拒绝</b>才算失效 —— 网络异常/超时(返回 null)不改结论、也不预热,
    /// 避免一次网络抖动就把浏览器进程常驻起来。
    /// </para>
    /// <para>
    /// 判据与账号页/提醒调度器完全一致:复用同一份 <c>GetGamerAsync</c> 响应交给
    /// <see cref="AccountLoginStateLogic.ProbeKuroSession"/> 三态判定,不另造一套逻辑。
    /// </para>
    /// </summary>
    private static async Task PrewarmWhenKuroSessionExpiredAsync()
    {
        try
        {
            var account = AppServices.KuroAccounts.Current;
            if (account is null)
            {
                // 存过账号但没有「当前账号」:等同于未登录,按要求预热以便直接登录
                await Dispatcher.UIThread.InvokeAsync(Controls.WebView2Control.PrewarmEnvironment);
                return;
            }

            var gamer = await AppServices.Kuro
                .GetGamerAsync(account, (int)KuroGameType.Waves)
                .ConfigureAwait(false);

            var probe = AccountLoginStateLogic.ProbeKuroSession(gamer);
            System.Console.Error.WriteLine($"MCKURO-WV2 启动登录态校验 probe={probe}");
            if (probe != AccountLoginStateLogic.KuroSessionProbe.Rejected)
            {
                // 有效或无法判定:不预热,省下浏览器进程常驻内存
                return;
            }

            // 必须在 UI(平台)线程预热:WebView2 环境绑定创建它的线程套间
            await Dispatcher.UIThread.InvokeAsync(Controls.WebView2Control.PrewarmEnvironment);
        }
        catch (Exception ex)
        {
            // 校验失败不预热(与原"有账号就跳过"的最坏情况一致),不影响其余启动流程
            System.Console.Error.WriteLine($"MCKURO-WV2 启动登录态校验失败,跳过预热: {ex.Message}");
        }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // UI 线程未处理异常记录(进程级处理器已在 Program.Main 挂好);容器未初始化前回退直写文件日志
        GlobalExceptionLogger.RegisterDispatcherHandler();

        // 诊断:确认启动线程模型(App 初始化线程 vs 平台线程是否同一条)
        System.Console.Error.WriteLine(
            $"MCKURO-THREAD app-init tid={Environment.CurrentManagedThreadId} apt={Thread.CurrentThread.GetApartmentState()}");

        // UI 线程心跳诊断(仅 McKuro_UI_HEARTBEAT=1):量化"界面卡死/通知迟滞"是否真实存在
        if (Environment.GetEnvironmentVariable("McKuro_UI_HEARTBEAT") == "1")
        {
            Dispatcher.UIThread.Post(UiHeartbeat.Start, DispatcherPriority.Background);
        }

        // 数据目录可经 McKuro_DATA_DIR 重定向(自更新流程模拟测试/多实例隔离用;默认 %AppData%/McKuro)
        AppServices.Initialize(Environment.GetEnvironmentVariable("McKuro_DATA_DIR"));

        // 图鉴图标目录:先同步应用磁盘缓存(必须在任何页面构造前,否则抽卡页首帧只有静态兜底),
        // 再由抽卡分析页按需触发网络刷新。失败静默,不影响启动。
        AppServices.GachaIcons.ApplyCachedCatalog();

        // WebView2 环境预热:只在「确实需要重新登录」时才做。
        //
        // 旧实现只判断「是否存过账号」,而账号存在 ≠ 登录仍然有效:token 可能已在别处失效、
        // 或用户主动退出后又没重新登录。于是"有账号但已失效"的用户永远等不到预热,
        // 真要登录时极验窗口还要冷启动数秒。现在改成:
        //   ① 无账号 → 必然要登录,直接预热(与原行为一致,不额外发请求);
        //   ② 有账号 → 先不预热(省掉浏览器进程常驻的内存,实测 6 个 msedgewebview2 子进程
        //      合计约 320 MB 工作集),后台跑一次轻量登录态校验;只有确认失效才回来预热。
        // 当前线程即平台线程(STA),环境对象绑定此线程,后续验证窗口的 Controller 创建与之同套间,
        // 因此后台校验完成后必须经 Dispatcher 回到 UI 线程再预热。
        if (AppServices.KuroAccounts.GetAccounts().Count == 0)
        {
            Controls.WebView2Control.PrewarmEnvironment();
        }
        else
        {
            _ = PrewarmWhenKuroSessionExpiredAsync();
        }

        // 异步探测出口 IP(数据中心 Devcode 头需要;避免库街区风控)
        // 提醒首轮会等它完成(带上限),确保库街区接口的 ip 头不为空
        var networkInit = AppServices.Kuro.InitAsync();

        // 界面语言(重启后生效;首次启动默认 zh-Hans)
        LanguageService.Load(AppServices.Settings.Current.Language);
        // Core 层展示文案网关:Core 项目不引用应用层,统一委托给 LanguageService 解析
        McKuro.Core.Services.CoreStrings.Resolver = key => LanguageService.Get(key);
        McKuro.Core.Services.CoreStrings.CurrentLanguage = LanguageService.Current;

        // 应用已保存的下载并发数与限速(对齐 Haiyu 的下载设置持久化)
        AppServices.Downloader.SetConcurrency(AppServices.Settings.Current.DownloadConcurrency);
        AppServices.Downloader.SetSpeedLimit((long)AppServices.Settings.Current.LimitSpeedMbps * 1024 * 1024);

        // 主题(Default 跟随系统 / Light / Dark;即时生效)
        RequestedThemeVariant = AppServices.Settings.Current.Theme switch
        {
            "Light" => Avalonia.Styling.ThemeVariant.Light,
            "Dark" => Avalonia.Styling.ThemeVariant.Dark,
            _ => Avalonia.Styling.ThemeVariant.Default,
        };

        // 每日自动签到调度(启动 15 秒后检查一次,之后每小时检查;按天去重,当天只签一次)
        _scheduler = new DailyTaskScheduler();
        _scheduler.Start();

        // 提醒调度(活动临期/登录状态/战歌同奏(周本)/每日活跃度/深塔赛季临期;
        // 冒烟模式不启动,保持无网络副作用)
        if (Environment.GetEnvironmentVariable("McKuro_SMOKE") != "1")
        {
            // 启动时重置当天提醒台账(设置项,默认关):提醒卡片 8 秒自动消失,用户可能没看清就没了,
            // 而台账保证"当天不再重复"→ 当天永远看不到了。开启后首轮检查会把当天该弹的重新弹一遍。
            // 必须在新建 ReminderScheduler 之前清:调度器首轮就在窗口 Opened 后跑,晚了会读到旧台账。
            if (AppServices.Settings.Current.NotifResetOnLaunch)
            {
                var cleared = AppServices.Reminders.ResetToday();
                System.Console.Error.WriteLine($"MCKURO-NOTIF 启动重置当天台账:清掉 {cleared} 条");
            }
            // 首轮不再盲等 15 秒:窗口就绪后再 Start(见下方窗口 Opened),只等出口 IP 探测完成
            _reminderScheduler = new ReminderScheduler(networkInit);
        }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = new MainWindowViewModel();
            var window = new MainWindow
            {
                DataContext = vm,
            };
            desktop.MainWindow = window;

            // 主窗口显示后才跑首轮提醒:此时通知卡片层已可渲染,首条消息无需再等布局/首帧;
            // Start 幂等,重复触发无副作用。
            window.Opened += (_, _) => _reminderScheduler?.Start();

            // 单实例:主窗口就绪后开始监听「唤起」信号(次实例启动→本实例显示并聚焦)。
            SingleInstanceGuard.StartActivationListener();

            // 诊断:稳态提醒延迟测试(McKuro_NOTIF_DELAY_TEST=<秒>)。
            // 与 McKuro_SMOKE_NOTIF 的构造期播种不同,这里等窗口完全渲染后再由后台线程触发提醒,
            // 才能测到"真实跑起来之后,一条通知从触发到画上屏幕"的耗时(派发/布局/渲染各段)。
            if (double.TryParse(Environment.GetEnvironmentVariable("McKuro_NOTIF_DELAY_TEST"), out var notifDelay)
                && notifDelay > 0)
            {
                DispatcherTimer.RunOnce(() =>
                {
                    UiHeartbeat.Mark("notif-test-timer-fired");
                    // 后台线程触发:模拟真实调度器(ReminderScheduler 在 Task.Run 里调用 Raise)
                    Task.Run(() =>
                    {
                        UiHeartbeat.Mark("notif-raise-begin(background)");
                        AppServices.Reminders.Raise(
                            $"delaytest:{DateTime.Now:HHmmss}",
                            NotificationKind.ActivityEnding,
                            "延迟测试通知",
                            "用于测量通知从触发到绘制到屏幕的耗时",
                            NavigationKeys.Activity);
                        UiHeartbeat.Mark("notif-raise-returned(background)");
                    });
                }, TimeSpan.FromSeconds(notifDelay));
            }

            // 自测模式:自动导航(默认抽卡分析页,验证 AOT 下图表页渲染),4 秒后退出
            // McKuro_SMOKE_NAV 可指到其他页(如 Settings/Launcher)用于页面级验证
            if (Environment.GetEnvironmentVariable("McKuro_SMOKE") == "1")
            {
                Dispatcher.UIThread.Post(() =>
                {
                    var target = Environment.GetEnvironmentVariable("McKuro_SMOKE_NAV") is { Length: > 0 } navKey
                        ? vm.NavigationItems.FirstOrDefault(n => n.Key == navKey) ?? vm.NavigationItems[2]
                        : vm.NavigationItems[2];
                    vm.NavigateTo(target);

                    // 下载进度卡目视验证(McKuro_SMOKE_DL=normal|weak|stable):不触网,按固定节奏
                    // 灌假波形,用于验证速度曲线自适应放大、大字网速与环形进度排版。
                    if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("McKuro_SMOKE_DL")))
                    {
                        var launcherItem = vm.NavigationItems.FirstOrDefault(n => n.ViewModel is LauncherViewModel);
                        if (launcherItem?.ViewModel is LauncherViewModel launcherVm)
                        {
                            vm.NavigateTo(launcherItem);
                            launcherVm.StartSmokeDownloadFeed();
                        }
                    }

                    // 重复/残留文件弹窗目视验证(McKuro_SMOKE_RESIDUE=1):
                    // 直接触发一次手动体积自检(会弹窗),用于验证清理确认窗口的排版与文案。
                    if (Environment.GetEnvironmentVariable("McKuro_SMOKE_RESIDUE") == "1")
                    {
                        var launcherItem = vm.NavigationItems.FirstOrDefault(n => n.ViewModel is LauncherViewModel);
                        if (launcherItem?.ViewModel is LauncherViewModel launcherVm)
                        {
                            vm.NavigateTo(launcherItem);
                            // 等启动页绑定完成再触发,否则弹窗可能抢在窗口就绪前
                            DispatcherTimer.RunOnce(
                                () => launcherVm.CheckResidueCommand.Execute(null),
                                TimeSpan.FromSeconds(2));
                        }
                    }

                    // 「游戏已退出」状态文案自动消失的目视验证(McKuro_SMOKE_SESSION_END=1):
                    // 伪造一次正常结束(不是 Failed,失败文案要常驻),用于确认 8 秒后状态栏清空。
                    if (Environment.GetEnvironmentVariable("McKuro_SMOKE_SESSION_END") == "1")
                    {
                        var launcherItem = vm.NavigationItems.FirstOrDefault(n => n.ViewModel is LauncherViewModel);
                        if (launcherItem?.ViewModel is LauncherViewModel launcherVm)
                        {
                            vm.NavigateTo(launcherItem);
                            DispatcherTimer.RunOnce(
                                () => AppServices.GameMonitor.SimulateSessionEndedForSmoke(GameSessionEndReason.Finished),
                                TimeSpan.FromSeconds(3));
                        }
                    }

                    // 游戏会话资源释放自检(McKuro_SMOKE_GAME_SESSION=1):走与 Launch()
                    // 相同的 BeginLaunch/StateChanged 出口，不需要真实游戏与版本服务端。
                    // McKuro_SMOKE_GAME_EXE 指向一个真实可执行的替身程序(测试用 calc 副本)：
                    // 必须让进程真的活着，否则 20 秒稳定窗口到点会被判「启动失败」回到空闲，
                    // 资源随之恢复 —— 那测的是失败路径，不是「游戏运行中」。
                    // McKuro_SMOKE_GAME_SECONDS 为会话时长(默认 10 秒;<=0 表示 kill 后不自动结束)。
                    if (Environment.GetEnvironmentVariable("McKuro_SMOKE_GAME_SESSION") == "1")
                    {
                        System.Console.Error.WriteLine("MCKURO-SMOKE 游戏会话钩子已装载");
                        var gameSeconds = double.TryParse(
                            Environment.GetEnvironmentVariable("McKuro_SMOKE_GAME_SECONDS"), out var gs)
                            ? Math.Max(0, gs)   // 显式给 0 = 一直保持在游戏中
                            : 10;
                        var gameStart = double.TryParse(
                            Environment.GetEnvironmentVariable("McKuro_SMOKE_GAME_START"), out var gst) && gst > 0
                            ? gst
                            : 10;
                        DispatcherTimer.RunOnce(() =>
                        {
                            System.Console.Error.WriteLine($"MCKURO-SMOKE 到达游戏会话起点 t={gameStart}s");
                            System.Diagnostics.Process? fake = null;
                            var fakeExe = Environment.GetEnvironmentVariable("McKuro_SMOKE_GAME_EXE");
                            var names = new[] { "McKuroFakeGame" };
                            if (!string.IsNullOrWhiteSpace(fakeExe) && File.Exists(fakeExe))
                            {
                                try
                                {
                                    var psi = new System.Diagnostics.ProcessStartInfo(fakeExe)
                                    {
                                        UseShellExecute = true,
                                        // timeout.exe 需要一个时长参数才会驻留;其它替身程序忽略多余参数。
                                        Arguments = "/t 600 /nobreak",
                                    };
                                    fake = System.Diagnostics.Process.Start(psi);
                                }
                                catch (Exception ex)
                                {
                                    System.Console.Error.WriteLine($"MCKURO-SMOKE 启动替身进程失败: {ex.Message}");
                                }
                                names = [Path.GetFileNameWithoutExtension(fakeExe)];
                            }
                            AppServices.GameMonitor.BeginLaunch(names);
                            if (gameSeconds > 0)
                            {
                                DispatcherTimer.RunOnce(() =>
                                {
                                    // 结束进程让监控按「正常退出」走，而不是伪造 Reset：
                                    // 这样状态切换与真实退出完全同路。
                                    try
                                    {
                                        if (fake is { HasExited: false })
                                        {
                                            fake.Kill(entireProcessTree: true);
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        System.Console.Error.WriteLine($"MCKURO-SMOKE 结束替身进程失败: {ex.Message}");
                                    }
                                }, TimeSpan.FromSeconds(gameSeconds));
                            }
                        }, TimeSpan.FromSeconds(gameStart));
                    }

                    // WebView2 常驻资源释放自检(McKuro_SMOKE_WV2_RELEASE=<秒>):到点调用
                    // ReleasePrewarmResources,验证锚点 Controller/隐藏窗口/环境缓存被拆掉、
                    // msedgewebview2 子进程随后退出。仅开发诊断用,不触碰任何登录接口。
                    if (double.TryParse(Environment.GetEnvironmentVariable("McKuro_SMOKE_WV2_RELEASE"), out var wv2Release)
                        && wv2Release > 0)
                    {
                        DispatcherTimer.RunOnce(
                            () => Controls.WebView2Control.ReleasePrewarmResources(),
                            TimeSpan.FromSeconds(wv2Release));
                    }

                    // 释放后重建自检(McKuro_SMOKE_WV2_REPREWARM=<秒>):验证 ReleasePrewarmResources
                    // 清空状态后 PrewarmEnvironment 仍能重新冷启动成功(用于确认"退出登录后再登录"可用)。
                    if (double.TryParse(Environment.GetEnvironmentVariable("McKuro_SMOKE_WV2_REPREWARM"), out var wv2Reprewarm)
                        && wv2Reprewarm > 0)
                    {
                        DispatcherTimer.RunOnce(
                            () => Controls.WebView2Control.PrewarmEnvironment(),
                            TimeSpan.FromSeconds(wv2Reprewarm));
                    }

                    // 冒烟时长默认 4s,可用 McKuro_SMOKE_SECONDS 覆盖(验证视频背景等慢加载 UI 用)
                    var seconds = double.TryParse(Environment.GetEnvironmentVariable("McKuro_SMOKE_SECONDS"), out var s) && s > 0
                        ? s
                        : 4;
                    DispatcherTimer.RunOnce(() => desktop.Shutdown(), TimeSpan.FromSeconds(seconds));
                });
            }

            // 极验窗口冒烟:应用内验证窗口自检(创建平台 WebView 控件并加载页面),4s 后自动退出。
            // McKuro_GEETEST_SMOKE=1 加载 example.com;设为具体 URL 则加载该页面(如本地极验页,用于布局验证)。
            // 仅开发诊断用:不触碰任何登录接口。
            var geetestSmoke = Environment.GetEnvironmentVariable("McKuro_GEETEST_SMOKE");
            if (!string.IsNullOrEmpty(geetestSmoke))
            {
                var url = geetestSmoke == "1" ? "https://example.com/" : geetestSmoke;
                var win = new GeetestWindow(url);
                win.Show();
                System.Console.Error.WriteLine("MCKURO-GEETEST-SMOKE window shown, supported="
                    + GeetestWindow.IsPlatformSupported);
                // "1" 快速自检 4s;URL 模式给 15s(极验脚本初始化+布局检查需要时间)
                DispatcherTimer.RunOnce(() => desktop.Shutdown(),
                    TimeSpan.FromSeconds(geetestSmoke == "1" ? 4 : 15));
            }
        }
        else
        {
            // 非桌面生命周期(理论路径):没有窗口可等,直接开始首轮提醒
            _reminderScheduler?.Start();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
