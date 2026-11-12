using Avalonia.Controls;
using McKuro.Core.Services;
using McKuro.Services;

namespace McKuro.Views;

/// <summary>
/// 「清理日志」的范围选择窗口(用户需求 2026-11)。
///
/// <para>
/// <b>为什么把它做成弹窗而不是页面上两个按钮</b>:关于页原来有「打开日志目录」「清理历史日志」
/// 「清空全部日志」三个按钮,视觉上把这一页压满;"清历史还是全清"是一个<b>点下去之后才需要想</b>
/// 的决策,不是常驻信息,放在弹窗里更省地方,也能在同一个界面里把两个范围的后果说清楚。
/// </para>
/// <para>
/// <b>默认动作为「清理历史日志」</b>且排在危险按钮之前:今天的日志正被
/// <see cref="McKuro.Core.Infrastructure.FileLoggerProvider"/> 的常驻句柄追加,是用户
/// "清完再来反馈"的唯一现场,不该被一次随手点击带走。
/// </para>
/// </summary>
public partial class LogCleanupWindow : Window
{
    public LogCleanupWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 弹出范围选择窗口;用户取消返回 <c>null</c>。
    /// </summary>
    /// <param name="owner">父窗口(可为 null:此时改用非模态显示)。</param>
    /// <param name="footprint">当前日志占用,用于在弹窗里给出"能清多少、保留多少"的具体数字。</param>
    public static async Task<LogCleanScope?> ShowAsync(Window? owner, LogFootprint footprint)
    {
        var window = new LogCleanupWindow();

        window.TitleText.Text = LanguageService.Format("Log.Clean.Dialog.Title");
        window.BodyText.Text = LanguageService.Format("Log.Clean.Dialog.Body");
        var totalBytes = footprint.TotalBytes;
        var oldBytes = EstimateOldBytes(footprint);
        window.TotalText.Text = FormatSize(totalBytes);
        window.OldText.Text = footprint.OldFileCount > 0
            ? LanguageService.Format("Log.Clean.Dialog.OldValue", FormatSize(oldBytes), footprint.OldFileCount)
            : LanguageService.Format("Log.Clean.Dialog.NoOld");
        window.TodayText.Text = LanguageService.Format(
            "Log.Clean.Dialog.TodayValue", FormatSize(Math.Max(0, totalBytes - oldBytes)));

        LogCleanScope? selected = null;
        window.OldOnlyButton.Click += (_, _) =>
        {
            selected = LogCleanScope.OldOnly;
            window.Close();
        };
        window.AllButton.Click += (_, _) =>
        {
            selected = LogCleanScope.All;
            window.Close();
        };
        window.CancelButton.Click += (_, _) => window.Close();

        if (owner is not null)
        {
            await window.ShowDialog(owner);
        }
        else
        {
            // 无 owner(理论上只在非常规启动路径出现):非模态显示,
            // 直接返回 null —— 宁可让用户再点一次,也不要阻塞在一个可能没人关的窗口上
            window.Show();
            return null;
        }
        return selected;
    }

    /// <summary>
    /// 估算"历史日志"的字节数。
    /// <para>
    /// <see cref="LogFootprint"/> 只带总字节与两类<b>计数</b>(总数/历史数),不带历史字节。
    /// 逐文件精确统计要再枚举一遍磁盘,而弹窗是同步出现在按钮点击后的第一帧上,不值得为
    /// 一个明细数字多读一次目录。这里按"历史文件数 × 平均文件大小"估算 —— 日志文件按天
    /// 滚动、体量量级相近,估算误差对"值不值得点"这个判断没有影响;
    /// 真正执行清理后界面会用 <see cref="LogCleanResult.BytesFreed"/> 报出精确值。
    /// </para>
    /// </summary>
    private static long EstimateOldBytes(LogFootprint footprint)
    {
        if (footprint.FileCount <= 0 || footprint.OldFileCount <= 0)
        {
            return 0;
        }
        var average = footprint.TotalBytes / footprint.FileCount;
        return Math.Min(footprint.TotalBytes, average * footprint.OldFileCount);
    }

    /// <summary>字节 → 人类可读体积(与启动器其它体积文案同一口径)。</summary>
    private static string FormatSize(long bytes) => McKuro.Core.Infrastructure.StorageFootprint.FormatSize(bytes);
}
