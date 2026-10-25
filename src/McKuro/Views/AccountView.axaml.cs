using Avalonia.Controls;
using Avalonia.Input;
using McKuro.ViewModels;

namespace McKuro.Views;

public partial class AccountView : UserControl
{
    public AccountView()
    {
        InitializeComponent();
    }

    /// <summary>点击库街区账号行:切换该接口的当前账号(选中高亮随 RefreshAccounts 重建)。</summary>
    private void OnKuroAccountPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: InterfaceAccountItem item }
            && DataContext is AccountViewModel vm
            && vm.KuroAccountItems.IndexOf(item) is { } index and >= 0)
        {
            vm.SelectKuroAccountAt(index);
        }
    }

    /// <summary>点击云鸣潮账号行:切换该接口的当前账号。</summary>
    private void OnCloudAccountPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: InterfaceAccountItem item }
            && DataContext is AccountViewModel vm
            && vm.CloudAccountItems.IndexOf(item) is { } index and >= 0)
        {
            vm.SelectCloudAccountAt(index);
        }
    }
}
