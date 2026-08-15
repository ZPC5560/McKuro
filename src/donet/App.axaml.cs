using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using donet.Services;
using donet.ViewModels;
using donet.Views;

namespace donet;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        AppServices.Initialize();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = new MainWindowViewModel();
            desktop.MainWindow = new MainWindow
            {
                DataContext = vm,
            };

            // 自测模式:自动导航到抽卡分析页,4 秒后退出(验证 AOT 下图表页渲染)
            if (Environment.GetEnvironmentVariable("DONET_SMOKE") == "1")
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
