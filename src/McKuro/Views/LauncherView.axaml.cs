using Avalonia.Controls;
using Avalonia.Threading;
using McKuro.ViewModels;

namespace McKuro.Views;

public partial class LauncherView : UserControl
{
    private readonly DispatcherTimer _slideTimer;

    public LauncherView()
    {
        InitializeComponent();

        // 封面轮播自动切换(每 6 秒)
        _slideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        _slideTimer.Tick += (_, _) =>
        {
            if (DataContext is LauncherViewModel vm && vm.Slideshows.Count > 1)
            {
                SlideCarousel.SelectedIndex = (SlideCarousel.SelectedIndex + 1) % vm.Slideshows.Count;
            }
        };
        _slideTimer.Start();

        Unloaded += (_, _) => _slideTimer.Stop();
    }
}
