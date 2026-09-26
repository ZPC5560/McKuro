using Avalonia.Controls;
using Avalonia.Threading;
using McKuro.ViewModels;

namespace McKuro.Views;

public partial class ActivityView : UserControl
{
    private ActivityViewModel? _vm;

    public ActivityView()
    {
        InitializeComponent();
        // 甘特数据就绪/视口尺寸变化/挂树时把视口滚动到"今天"居中:左侧完成段默认被视口
        // 部分隐藏,拖动横向滚动条可回看完整历史。
        // 注意:不要挂 LayoutUpdated 做这件事 —— 处理器内改 Offset 会再触发布局,与通知卡片
        // 等常驻动画叠加会形成布局风暴(UI 卡死);这里只用离散事件(加载完成/尺寸变化/挂树)。
        GanttScroll.SizeChanged += (_, _) => CenterOnToday();
        DataContextChanged += (_, _) => HookViewModel();
        AttachedToVisualTree += (_, _) =>
        {
            // 挂树时布局还没跑,视口宽度/内容宽都还是 0,直接居中会被钳成 0;排到布局提交后再做
            PostCenterOnToday();
            // 跨天挂载:数据是旧日期时自动刷新(今日线/进度分界随 now 前移,过期即错位)
            if (_vm?.GanttLoadedOnDate is { } loadedOn && loadedOn != DateTime.Today)
            {
                _ = _vm.RefreshCommand.ExecuteAsync(null);
            }
        };
        DetachedFromVisualTree += (_, _) => UnhookViewModel();
        HookViewModel();
    }

    private void HookViewModel()
    {
        UnhookViewModel();
        if (DataContext is ActivityViewModel vm)
        {
            _vm = vm;
            vm.GanttLoaded += OnGanttLoaded;
            // 页面 VM 在启动时就全部建好,eager 加载完成后 GanttLoaded 早已错过(视图此刻才建):
            // 必须自己补一次居中,且同样要等布局提交(否则内容宽为 0 → 目标被钳成 0 → 停在最左)
            PostCenterOnToday();
        }
    }

    private void UnhookViewModel()
    {
        if (_vm is not null)
        {
            _vm.GanttLoaded -= OnGanttLoaded;
            _vm = null;
        }
    }

    private void OnGanttLoaded() => PostCenterOnToday();

    /// <summary>把居中排到布局提交之后(Loaded 优先级):此时视口宽度与时间轴内容宽才都是真值。</summary>
    private void PostCenterOnToday() =>
        Dispatcher.UIThread.Post(CenterOnToday, DispatcherPriority.Loaded);

    private void CenterOnToday()
    {
        var vm = _vm;
        if (vm is null || !vm.GanttDataReady || GanttScroll.Viewport.Width <= 0 || vm.GanttTimelineWidth <= 0)
        {
            return;
        }
        // 今天芯片真正跨线居中:芯片宽度取决于语言与字体(如「今天 09-26」≈56px,写死半宽会左右偏),
        // 布局提交后按实际 Bounds 宽度取左缘,与今日线共用 GanttTodayOffsetPx 同一锚点
        var chipWidth = TodayChip.Bounds.Width;
        if (chipWidth > 0)
        {
            Avalonia.Controls.Canvas.SetLeft(TodayChip, vm.GanttTodayOffsetPx - chipWidth / 2);
        }
        // 今日线在内容坐标系 = 时间轴内偏移,减去半个视口即居中(时间轴右侧已对称延伸,
        // 正常不会再被 maxOffset 钳到最右)。
        // maxOffset 用 VM 的时间轴宽算,而不是 GanttScroll.Extent:Extent 是上一次布局的快照,
        // 数据刚就绪时它还是 0,会把目标钳成 0(实测表现为"打开活动页停在最左侧")。
        var maxOffset = Math.Max(0, vm.GanttTimelineWidth - GanttScroll.Viewport.Width);
        var target = Math.Clamp(vm.GanttTodayOffsetPx - GanttScroll.Viewport.Width / 2, 0, maxOffset);
        GanttScroll.Offset = GanttScroll.Offset.WithX(target);
    }
}
