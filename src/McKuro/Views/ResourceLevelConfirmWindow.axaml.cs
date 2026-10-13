using Avalonia.Controls;
using McKuro.Services;

namespace McKuro.Views;

/// <summary>
/// 资源等级切换的二次确认窗口(防误点:切换可能需要下载几十 GB 画质包)。
/// 通过 <see cref="ShowAsync"/> 返回用户是否确认。
/// </summary>
public partial class ResourceLevelConfirmWindow : Window
{
    public ResourceLevelConfirmWindow()
    {
        InitializeComponent();
    }

    /// <summary>按等级名与体积文案构造并等待用户选择。</summary>
    /// <param name="levelName">目标等级显示名(如「极致」)。</param>
    /// <param name="sizeText">体积提示(如「游戏大小: 99.45 GB」);为空时用无需体积的文案。</param>
    /// <param name="owner">父窗口。</param>
    /// <returns>用户是否确认切换。</returns>
    public static async Task<bool> ShowAsync(Window? owner, string levelName, string sizeText)
    {
        return await ShowAsync(
            owner,
            LanguageService.Format("Launcher.LevelConfirmTitle"),
            LanguageService.Format("Launcher.LevelConfirmBody", levelName, sizeText),
            LanguageService.Format("Launcher.LevelConfirmOk"),
            LanguageService.Format("Launcher.LevelCancel"));
    }

    /// <summary>
    /// 通用二次确认(标题/正文/确认键文案自定)。
    /// 删除画质包复用同一个窗口,避免为一句文案再造一个 Window 与 XAML。
    /// </summary>
    public static async Task<bool> ShowAsync(Window? owner, string title, string body, string confirmText, string cancelText)
    {
        var window = new ResourceLevelConfirmWindow();
        window.TitleText.Text = title;
        window.BodyText.Text = body;
        window.ConfirmButton.Content = confirmText;
        window.CancelButton.Content = cancelText;

        var result = false;
        window.ConfirmButton.Click += (_, _) =>
        {
            result = true;
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
        return result;
    }
}
