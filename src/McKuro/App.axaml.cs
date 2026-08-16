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
        AppServices.Initialize();

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

            // 自测模式:自动导航到抽卡分析页,4 秒后退出(验证 AOT 下图表页渲染)
            if (Environment.GetEnvironmentVariable("McKuro_SMOKE") == "1")
            {
                Dispatcher.UIThread.Post(() =>
                {
                    vm.NavigateTo(vm.NavigationItems[2]);
                    DispatcherTimer.RunOnce(() => desktop.Shutdown(), TimeSpan.FromSeconds(4));
                });
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
