using Avalonia.Controls;
using McKuro.ViewModels;

namespace McKuro.Views;

public partial class TowerView : UserControl
{
    /// <summary>卡片目标宽度:此时 150px 高的通栏主图接近源图 2.1:1,基本不裁。</summary>
    private const double TargetCardWidth = 340;

    /// <summary>切换列数的滞回带:宽度要明显越过边界才换列,否则拖窗口在边界附近会反复重建整张卡片列表。</summary>
    private const double HysteresisWidth = TargetCardWidth * 0.10;

    /// <summary>当前已应用的列数(0 = 还没算过)。</summary>
    private int _appliedColumns;

    public TowerView()
    {
        InitializeComponent();
        // 区域卡片列数跟随可用宽度(纯视图层职责:VM 不关心窗口有多宽)。
        AreaScroll.SizeChanged += OnAreaScrollSizeChanged;
    }

    private void OnAreaScrollSizeChanged(object? sender, SizeChangedEventArgs e)
        => ApplyAreaColumns(e.NewSize.Width);

    /// <summary>
    /// 按可用宽度决定区域卡片的列数。
    /// <para>
    /// 目标卡片宽约 340px:此时 150px 高的通栏主图接近源图 2.1:1,基本不裁。
    /// 窗口拉宽就多分几列(最多 6) —— 如果固定少量列把卡片拉到 1500px 宽,
    /// 主图会被 <c>UniformToFill</c> 裁成窄窄一条,看起来就是"主图显示不完整"(全屏时踩到过)。
    /// </para>
    /// </summary>
    private void ApplyAreaColumns(double width)
    {
        if (width <= 0 || DataContext is not TowerViewModel vm)
        {
            return;
        }
        var columns = TowerViewModel.ClampAreaColumns(
            (int)Math.Round(width / TargetCardWidth, MidpointRounding.AwayFromZero));
        if (_appliedColumns != 0 && columns != _appliedColumns)
        {
            // 边界 = 两档之间的中点(如 2→3 是 2.5×340);离边界不足一个滞回带就先不动,
            // 避免用户拖着窗口在边界上小幅来回时反复 Clear+Add 整张卡片列表(最多 51 张)。
            var boundary = Math.Min(columns, _appliedColumns) * TargetCardWidth + TargetCardWidth / 2;
            if (Math.Abs(width - boundary) < HysteresisWidth)
            {
                return;
            }
        }
        _appliedColumns = columns;
        vm.SetExploreAreaColumns(columns);
    }
}
