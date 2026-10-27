using Avalonia.Controls;
using McKuro.Core.Services.Game;
using McKuro.Services;

namespace McKuro.Views;

/// <summary>
/// 安装完成后的「重复/残留文件」清理确认窗口。
/// <para>
/// 用户需求(2026-10):多次断连下载后游戏目录出现重复文件,体积明显大于服务端清单;
/// 希望安装完成后自动比对,发现明显过大时弹窗询问是否删除。
/// </para>
/// <para>
/// 只展示 <see cref="ResidueScanner"/> 认定的「启动器自身产生、且确认可删」的条目,
/// 并在窗口里明确写出「不会动其它画质资源包与存档」—— 这正是本功能最容易误伤的地方。
/// </para>
/// </summary>
public partial class ResidueCleanupWindow : Window
{
    public ResidueCleanupWindow()
    {
        InitializeComponent();
    }

    /// <summary>弹出确认窗口,返回用户是否要求删除。</summary>
    public static async Task<bool> ShowAsync(Window? owner, ResidueScanResult result)
    {
        if (result is null || !result.Report.HasResidue)
        {
            return false;
        }

        var window = new ResidueCleanupWindow();
        var report = result.Report;

        window.TitleText.Text = LanguageService.Format("Launcher.Residue.Title");
        window.BodyText.Text = LanguageService.Format("Launcher.Residue.Body", report.Items.Count);
        window.ActualText.Text = FormatSize(result.ActualBytes);
        window.ManifestText.Text = result.ManifestBytes > 0
            ? FormatSize(result.ManifestBytes)
            : LanguageService.Format("Launcher.Residue.ManifestUnknown");
        window.ReclaimText.Text = FormatSize(report.ReclaimBytes);

        // 分类明细:每类一行(备份 N 项 · 12.3 GB)
        var lines = new List<string>();
        foreach (var (kind, count, bytes) in ResiduePolicy.Summarize(report))
        {
            lines.Add(LanguageService.Format(
                "Launcher.Residue.KindLine",
                KindName(kind),
                count,
                FormatSize(bytes)));
        }
        window.KindList.ItemsSource = lines;

        var accepted = false;
        window.DeleteButton.Click += (_, _) =>
        {
            accepted = true;
            window.Close();
        };
        window.KeepButton.Click += (_, _) => window.Close();

        if (owner is not null)
        {
            await window.ShowDialog(owner);
        }
        else
        {
            window.Show();
            await Task.CompletedTask;
        }
        return accepted;
    }

    /// <summary>残留类别的用户可读名。</summary>
    private static string KindName(ResidueKind kind) => kind switch
    {
        ResidueKind.Backup => LanguageService.Format("Launcher.Residue.Kind.Backup"),
        ResidueKind.PartialDownload => LanguageService.Format("Launcher.Residue.Kind.Partial"),
        ResidueKind.PatchTemp => LanguageService.Format("Launcher.Residue.Kind.PatchTemp"),
        ResidueKind.DiffTemp => LanguageService.Format("Launcher.Residue.Kind.DiffTemp"),
        _ => kind.ToString(),
    };

    /// <summary>字节 → 人类可读体积(与启动器其它体积文案同一口径)。</summary>
    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return $"{size:0.##} {units[unit]}";
    }
}
