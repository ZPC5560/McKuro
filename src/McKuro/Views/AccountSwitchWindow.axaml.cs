using Avalonia.Controls;
using Avalonia.Interactivity;
using McKuro.Services;
using McKuro.ViewModels;

namespace McKuro.Views;

/// <summary>
/// 首页「今日数据」账号切换弹窗(参考 Haiyu 本地账号卡片弹窗):
/// 列出«自动»与本地已保存的库街区账号,选中项用主题强调色高亮;
/// 确定后返回所选 UserId(取消返回 null)。
/// </summary>
public partial class AccountSwitchWindow : Window
{
    private AccountSwitchSelection? _selection;
    private string? _result;

    /// <summary>XAML 加载器要求公共无参构造;业务入口请使用 <see cref="ShowAsync"/>。</summary>
    public AccountSwitchWindow()
    {
        InitializeComponent();
        // 跟随主窗口系统材质:窗口透明,只有内容面板是半透明玻璃面
        TransparencyLevelHint = SystemMaterialService.TransparencyLevelHint;
    }

    /// <summary>
    /// 打开账号选择弹窗。<paramref name="vm"/> 提供可选项(含«自动»,角色昵称/UID 异步补全)与当前绑定值。
    /// 返回用户确定的 UserId(«自动»为空串);取消/直接关闭返回 null。
    /// </summary>
    public static async Task<string?> ShowAsync(Window? owner, HomeViewModel vm)
    {
        var window = new AccountSwitchWindow();
        // 打开前先确保角色信息在补全中:弹窗与 VM 共享同一批选项实例,回填后列表实时刷新
        window.BindSelection(vm);
        window.ConfirmButton.Click += (_, _) =>
        {
            window._result = window._selection?.Selected?.Key;
            window.Close();
        };
        // 重新检测本地启动器账号:游戏里换号后无需重启应用即可看到新账号。
        // 关闭仍走窗口标题栏(与 VersionSelectWindow 一致),不再放重复的关闭键。
        window.RefreshButton.Click += async (_, _) => await window.RefreshAccountsAsync(vm);

        if (owner is not null)
        {
            await window.ShowDialog(owner);
        }
        else
        {
            window.Show();
            await Task.CompletedTask;
        }
        return window._result;
    }

    /// <summary>把 VM 的当前候选列表绑定为弹窗展示状态(初次打开与刷新后共用)。</summary>
    /// <param name="selectedKey">要标记为选中的绑定键;为空时回落到 VM 的持久化绑定值。</param>
    private void BindSelection(HomeViewModel vm, string? selectedKey = null)
    {
        var key = selectedKey ?? vm.HomeBoundAccountId;
        _selection = new AccountSwitchSelection(vm.HomeAccountOptions, key);
        DataContext = _selection;
        EmptyText.IsVisible = !_selection.HasChoice;
    }

    /// <summary>
    /// 刷新候选账号:<see cref="AccountSwitchSelection"/> 持有的是打开那一刻的列表<b>副本</b>,
    /// 因此必须在枚举完成后用 VM 的最新列表重建一个 selection 并重新绑定,
    /// 否则「刷新」点了列表也不变。用户未确定的当前选择会被保留,
    /// 并回填刷新结果(成功/无变化/未检测到)让用户看到明确反馈。
    /// </summary>
    private async Task RefreshAccountsAsync(HomeViewModel vm)
    {
        var pendingKey = _selection?.Selected?.Key;
        if (_selection is { IsRefreshing: true })
        {
            return;
        }
        if (_selection is not null)
        {
            _selection.IsRefreshing = true;
        }
        try
        {
            var outcome = await vm.ReloadLocalAccountsAsync();
            BindSelection(vm, pendingKey);
            // 重建 selection 会重置反馈状态,故在绑定之后回填
            _selection?.ApplyRefreshOutcome(outcome);
        }
        catch
        {
            // 枚举异常:保留原列表,但仍然给出失败反馈(静默失败是本次要修的缺陷)
            _selection?.ApplyRefreshOutcome(AccountRefreshOutcome.Failed);
        }
        finally
        {
            if (_selection is not null)
            {
                _selection.IsRefreshing = false;
            }
        }
    }

    /// <summary>点账号卡片:切换选中项(复制按钮单独处理,不冒泡到这里)。</summary>
    private void OnItemPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: HomeAccountOption option })
        {
            _selection?.Select(option);
        }
    }

    /// <summary>复制该账号的游戏角色 UID(没有 UID 时按钮自身不显示)。</summary>
    private async void OnCopyRoleId(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: HomeAccountOption { RoleIdText: { } roleId } })
        {
            return;
        }
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            return;
        }
        try
        {
            // Avalonia 12:IClipboard 自身无 SetTextAsync,走 ClipboardExtensions(与复制兑换码同款)
            await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(clipboard, roleId);
        }
        catch
        {
            // 剪贴板被占用等异常:静默忽略(不影响选号主流程)
        }
    }
}
