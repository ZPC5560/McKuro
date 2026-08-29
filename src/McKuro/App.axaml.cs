using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using McKuro.Services;
using McKuro.ViewModels;
using McKuro.Views;

namespace McKuro;

public partial class App : Application
{
    private DailyTaskScheduler? _scheduler;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // 诊断:确认启动线程模型(App 初始化线程 vs 平台线程是否同一条)
        System.Console.Error.WriteLine(
            $"MCKURO-THREAD app-init tid={Environment.CurrentManagedThreadId} apt={Thread.CurrentThread.GetApartmentState()}");

        AppServices.Initialize();

        // WebView2 环境预热:仅当库街区账号未登录时(登录验证需要极验;已有账号则无需每次启动加载)。
        // 当前线程即平台线程(STA),环境对象绑定此线程,后续验证窗口的 Controller 创建与之同套间。
        if (AppServices.KuroAccounts.GetAccounts().Count == 0)
        {
            Controls.WebView2Control.PrewarmEnvironment();
        }
        else
        {
            System.Console.Error.WriteLine("MCKURO-WV2 已有库街区账号,跳过 WebView2 预热");
        }

        // 异步探测出口 IP(数据中心 Devcode 头需要;避免库街区风控)
        _ = AppServices.Kuro.InitAsync();

        // 界面语言(重启后生效;首次启动默认 zh-Hans)
        LanguageService.Load(AppServices.Settings.Current.Language);

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

        // 每日自动签到调度(每天 8:00 + 启动后一次)
        _scheduler = new DailyTaskScheduler();
        _scheduler.Start();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = new MainWindowViewModel();
            desktop.MainWindow = new MainWindow
            {
                DataContext = vm,
            };

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

        base.OnFrameworkInitializationCompleted();
    }
}
