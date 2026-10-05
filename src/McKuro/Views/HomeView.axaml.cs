using Avalonia.Controls;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.Messaging;
using McKuro.Services;
using McKuro.ViewModels;

namespace McKuro.Views;

public partial class HomeView : UserControl
{
    public HomeView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 打开账号切换弹窗(昵称旁的小切换钮):选择今日数据显示哪个本地登录的库街区账号。
    /// 确定后由 VM 持久化绑定并重新拉取数据。
    /// <para>
    /// <b>必须整体 try/catch</b>:<c>async void</c> 的异常无法被调用方捕获(按钮点击没有等待者),
    /// 会直接抛到 UI 线程;而本仓没有任何全局异常兜底(无 UnhandledException/
    /// DispatcherUnhandledException/UnobservedTaskException 处理器),逃逸即终止进程 ——
    /// 对一颗昵称旁的按钮来说代价过大。弹窗显示失败(owner 不可见等)与构造失败都会走到这里。
    /// </para>
    /// </summary>
    private async void OnSwitchAccountClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not HomeViewModel vm)
        {
            return;
        }
        try
        {
            var owner = TopLevel.GetTopLevel(this) as Window;
            var selected = await AccountSwitchWindow.ShowAsync(owner, vm);
            if (selected is not null)
            {
                vm.ApplyAccountSelection(selected);
            }
            else
            {
                // 取消:把卡片上的临时选中态还原成持久化的绑定值
                vm.SyncAccountSelection();
            }
        }
        catch (Exception ex)
        {
            // 如实告知而不是静默:否则用户点了没反应会以为按钮坏了
            WeakReferenceMessenger.Default.Send(
                new ShowToastMessage(LanguageService.Format("Common.OperationFailedWith", ex.Message)));
        }
    }
}
