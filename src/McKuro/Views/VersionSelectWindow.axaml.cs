using Avalonia.Controls;
using McKuro.Services;

namespace McKuro.Views;

/// <summary>
/// 手动指定本地游戏版本对话框(对齐 Haiyu「选择版本/跳过校验」)。
/// 场景:游戏被官方启动器等外部渠道更新后,McKuro 本地版本记录滞后,
/// 界面显示旧版本并误报「有更新」。通过 <see cref="ShowAsync"/> 返回用户选定的版本与模式:
/// verify=false 仅写版本记录(跳过校验);verify=true 写记录后继续走修复校验流程。取消返回 (null, false)。
/// </summary>
public partial class VersionSelectWindow : Window
{
    private string? _resultVersion;
    private bool _verify;

    /// <summary>XAML 加载器要求公共无参构造;业务入口请使用 <see cref="ShowAsync"/>。</summary>
    public VersionSelectWindow()
    {
        InitializeComponent();
        // 跟随主窗口的系统材质(Mica/Acrylic/毛玻璃):窗口透明,只有内容面板是半透明玻璃面。
        // 无材质可用时(MacVibrancy 之外的 Linux 透明染色)该提示为空集合,窗口仍用面板面色保证可读。
        TransparencyLevelHint = McKuro.Services.SystemMaterialService.TransparencyLevelHint;
    }

    /// <summary>
    /// 打开对话框。<paramref name="refresher"/> 用于初始加载与「重新加载」按钮拉取候选版本
    /// (服务端当前版本 → 预载版本 → 本地记录);网络异常时应自行降级返回本地候选,不抛出。
    /// </summary>
    public static async Task<(string? Version, bool Verify)> ShowAsync(
        Window? owner,
        Func<Task<IReadOnlyList<string>>> refresher,
        string? current)
    {
        IReadOnlyList<string> initial;
        try
        {
            initial = await refresher();
        }
        catch
        {
            initial = [];
        }

        var window = new VersionSelectWindow();
        // 默认选中当前本地记录:它不在候选里时会被补进列表(下拉不可编辑,不能再靠手输兜底)
        window.ApplyCandidates(initial, current);

        window.ReloadButton.Click += async (_, _) =>
        {
            window.ReloadButton.IsEnabled = false;
            try
            {
                // 保留用户当前选择(重新拉取后仍指向同一个版本,而不是跳回第一项)
                var keep = window.VersionBox.SelectedItem as string;
                window.ApplyCandidates(await refresher(), keep ?? current);
            }
            catch
            {
                // 刷新失败保留现有候选(接口层已记日志)
            }
            finally
            {
                window.ReloadButton.IsEnabled = true;
            }
        };
        window.SkipButton.Click += (_, _) =>
        {
            window._resultVersion = window.CurrentText();
            window.Close();
        };
        window.VerifyButton.Click += (_, _) =>
        {
            window._verify = true;
            window._resultVersion = window.CurrentText();
            window.Close();
        };
        window.CancelButton.Click += (_, _) => window.Close();

        if (owner is not null)
        {
            await window.ShowDialog(owner);
        }
        else
        {
            window.Show();
            await Task.CompletedTask;
        }
        return (window._resultVersion, window._verify);
    }

    /// <summary>
    /// 读取当前选中的版本。下拉不可编辑,因此只认 SelectedItem;
    /// 没有选中项(候选为空)时返回 null,由调用方按"未选择"处理。
    /// </summary>
    private string? CurrentText()
    {
        var text = VersionBox.SelectedItem as string;
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    /// <summary>
    /// 重填候选并选中 <paramref name="selected"/>。
    /// 目标值不在候选里时会被补进列表(下拉已不可编辑,不能靠手输兜底);
    /// 见 <see cref="VersionCandidateList.Build"/>。
    /// </summary>
    private void ApplyCandidates(IReadOnlyList<string> candidates, string? selected = null)
    {
        var (items, index) = VersionCandidateList.Build(candidates, selected);
        VersionBox.ItemsSource = items;
        VersionBox.SelectedIndex = index;
        // 无候选时禁用确认键:否则会带着 null 版本号关闭对话框
        var hasChoice = items.Count > 0;
        SkipButton.IsEnabled = hasChoice;
        VerifyButton.IsEnabled = hasChoice;
    }
}
