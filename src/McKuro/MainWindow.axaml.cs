using Avalonia.Controls;
using Avalonia.Threading;
using McKuro.Services;

namespace McKuro;

public partial class MainWindow : Window
{
    private ScreenCaptureService? _capture;

    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        var handle = TryGetPlatformHandle()?.Handle ?? nint.Zero;
        if (handle == nint.Zero)
        {
            return;
        }

        var settings = AppServices.Settings.Current;
        _capture = new ScreenCaptureService();
        _capture.Attach(handle);
        _capture.Register(settings.CaptureModifierKey, settings.CaptureKey);
        _capture.CaptureCompleted += path => Dispatcher.UIThread.Post(() =>
        {
            if (DataContext is ViewModels.SettingsViewModel settingsVm)
            {
                settingsVm.NotifyCaptureSaved(path);
            }
        });
    }

    protected override void OnClosed(EventArgs e)
    {
        _capture?.Dispose();
        _capture = null;
        base.OnClosed(e);
    }
}
