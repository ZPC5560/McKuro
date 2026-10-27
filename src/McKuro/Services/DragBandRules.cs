namespace McKuro.Services;

/// <summary>
/// 「顶部拖动带」的判定逻辑(纯函数,供主窗口与单测共用)。
/// <para>
/// 需求背景:无边框沉浸模式下操作栏会自动收起,收起后整个窗口没有任何可拖动的空白,
/// 用户必须先贴窗口顶边唤出操作栏才能挪窗。因此把客户区顶部一块区域常驻为拖动带 ——
/// 即使操作栏不在,在那儿按住也能拖窗、双击最大化/还原。
/// </para>
/// <para>
/// 两条边界必须同时成立(任一写错都会重演历史事故):
/// <list type="number">
/// <item><b>只在操作栏收起时接管</b> —— 操作栏展开时那段本身就是它自己的拖动区,
///   两处都接管会让同一个手势被处理两次(拖动位置跳动 / 双击被吞一次)。</item>
/// <item><b>只接空白</b> —— 命中按钮、输入框、页签、列表项时必须放行。
///   上一轮"设置页点分类没反应"就是内容被意外位移导致点击落空;若这里再吞掉点击,
///   症状会一模一样。</item>
/// </list>
/// </para>
/// </summary>
public static class DragBandRules
{
    /// <summary>拖动带占客户区高度的比例(顶部 1/4)。</summary>
    public const double Ratio = 0.25;

    /// <summary>拖动带最小高度(DIP):窗口很矮时 1/4 可能盖不住操作栏,用该值兜底。</summary>
    public const double MinHeight = 44;

    /// <summary>
    /// 当前窗口高度下的拖动带高度(DIP)。
    /// </summary>
    /// <param name="clientHeight">客户区高度(DIP);非正数视为未知,退回下限。</param>
    public static double BandHeight(double clientHeight)
        => clientHeight > 0 ? Math.Max(MinHeight, clientHeight * Ratio) : MinHeight;

    /// <summary>该纵向位置(DIP,相对客户区顶部)是否落在拖动带内。</summary>
    /// <param name="y">指针的客户区 Y 坐标(DIP)。</param>
    /// <param name="clientHeight">客户区高度(DIP)。</param>
    public static bool IsInBand(double y, double clientHeight)
        => y >= 0 && y <= BandHeight(clientHeight);

    /// <summary>
    /// 本次按下是否应由拖动带接管。
    /// </summary>
    /// <param name="isImmersive">是否处于无边框沉浸模式(否则窗口有系统标题栏可拖,不该接管)。</param>
    /// <param name="titleBarShown">操作栏当前是否展开(展开时交给它自己处理)。</param>
    /// <param name="titlePinned">操作栏是否被临时固定(等同展开)。</param>
    /// <param name="isInteractive">按下的元素是否属于会消费指针事件的交互控件。</param>
    /// <param name="y">指针的客户区 Y 坐标(DIP)。</param>
    /// <param name="clientHeight">客户区高度(DIP)。</param>
    public static bool ShouldStartDrag(
        bool isImmersive,
        bool titleBarShown,
        bool titlePinned,
        bool isInteractive,
        double y,
        double clientHeight)
        => isImmersive
           && !titleBarShown
           && !titlePinned
           && !isInteractive
           && IsInBand(y, clientHeight);
}
