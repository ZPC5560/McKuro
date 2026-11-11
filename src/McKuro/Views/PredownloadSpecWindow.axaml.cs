using Avalonia.Controls;
using McKuro.Core.Models.Game;
using McKuro.Services;

namespace McKuro.Views;

/// <summary>
/// 分级预下载的选择窗(对齐官方启动器 3.8 的「预下载新版本」:
/// 全量 / 精简两张卡片 + 下载大小 / 所需空间 / 可用空间 + 确定下载)。
/// <para>
/// <b>为什么要独立弹窗:</b>选错规格的代价是十几 GB 流量(全量 12.08 GB vs 精简 7.30 GB)。
/// 官方就是把它做成「点预下载 → 弹窗比对两档体积 → 确定下载」的;启动页只有一个合并按钮的位置。
/// </para>
/// <para>
/// 卡片做成固定的两个而不是列表:官方最多两档,列表会引入多余的模板与选中态同步,
/// 而选中态在这里必须和底部数字<b>同步变化</b>(否则会出现"卡片说 7.30、底部说 12.08")。
/// </para>
/// </summary>
public partial class PredownloadSpecWindow : Window
{
    private PredownloadInfo? _info;
    private long? _availableBytes;
    private PredownloadSpec _selected = PredownloadSpec.Full;

    public PredownloadSpecWindow()
    {
        InitializeComponent();
    }

    /// <summary>用户选中的规格;取消时为 null。</summary>
    public PredownloadSpec? Result { get; private set; }

    /// <summary>弹出选择窗。</summary>
    /// <param name="owner">父窗口。</param>
    /// <param name="info">分级预载信息(两档体积与磁盘需求)。</param>
    /// <param name="availableBytes">游戏盘可用空间;null 表示读不到(该行显示 "-")。</param>
    /// <param name="initial">默认选中的规格。</param>
    /// <returns>用户选择的规格;取消返回 null。</returns>
    public static async Task<PredownloadSpec?> ShowAsync(
        Window? owner,
        PredownloadInfo info,
        long? availableBytes,
        PredownloadSpec initial = PredownloadSpec.Full)
    {
        var window = new PredownloadSpecWindow();
        window.Init(info, availableBytes, initial);

        window.ConfirmButton.Click += (_, _) => window.Close();
        window.CancelButton.Click += (_, _) =>
        {
            window.Result = null;
            window.Close();
        };
        // 点卡片即选中(与官方一致:卡片本身就是单选)。
        window.FullCard.PointerPressed += (_, _) => window.Select(PredownloadSpec.Full);
        window.MinimalCard.PointerPressed += (_, _) => window.Select(PredownloadSpec.Minimal);

        if (owner is not null)
        {
            await window.ShowDialog(owner);
        }
        else
        {
            window.Show();
        }
        return window.Result;
    }

    private void Init(PredownloadInfo info, long? availableBytes, PredownloadSpec initial)
    {
        _info = info;
        _availableBytes = availableBytes;

        FullSizeText.Text = FormatBytes(info.FullBytes);
        RequiredSpaceText.Text = "";
        AvailableSpaceText.Text = availableBytes is { } free ? FormatBytes(free) : "-";

        // 服务端没有「精简」区分时(老清单没有 tagGroups),隐藏精简卡而不是显示一个体积相同的假选项。
        MinimalCard.IsVisible = info.SupportsMinimal;
        MinimalSizeText.Text = info.SupportsMinimal ? FormatBytes(info.MinimalBytes) : "";

        Select(info.SupportsMinimal ? initial : PredownloadSpec.Full);
    }

    /// <summary>选中某档:高亮该卡,并把底部「下载大小 / 所需空间」同步成该档的数字。</summary>
    private void Select(PredownloadSpec spec)
    {
        if (_info is not null && spec == PredownloadSpec.Minimal && !_info.SupportsMinimal)
        {
            spec = PredownloadSpec.Full;
        }

        _selected = spec;
        Result = spec;

        FullCard.Classes.Set("selected", spec == PredownloadSpec.Full);
        MinimalCard.Classes.Set("selected", spec == PredownloadSpec.Minimal);

        if (_info is not null)
        {
            // 底部数字必须与所选档一致:这正是官方弹窗让用户"看清差别再决定"的地方。
            DownloadSizeText.Text = FormatBytes(_info.BytesFor(spec));
            RequiredSpaceText.Text = FormatBytes(_info.DiskBytesFor(spec));
        }
    }

    /// <summary>把字节数格式化成与界面其它位置一致的单位(1024 进制)。</summary>
    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.##} {units[unit]}";
    }
}
