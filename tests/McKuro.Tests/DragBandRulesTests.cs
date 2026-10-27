using McKuro.Services;

namespace McKuro.Tests;

/// <summary>
/// 「顶部拖动带」判定测试(用户需求:操作栏不显示时,顶部 1/4 区域也能拖动窗口)。
/// <para>
/// 回归背景:无边框沉浸模式下操作栏收起后,整个窗口没有任何可拖空白,必须先贴顶唤出操作栏
/// 才能挪窗。补上拖动带的同时有两个必须守住的红线,否则会重演历史事故:
/// ① 操作栏展开时不能接管(与它自己的拖动区抢手势);
/// ② 命中按钮/输入框/页签时必须放行(上一轮"设置页点分类没反应"就是吞点击的同类症状)。
/// </para>
/// </summary>
public class DragBandRulesTests
{
    /// <summary>默认窗口客户区高度:与本项目主窗口默认 656 高减去非客户区后的量级接近。</summary>
    private const double ClientHeight = 600;

    // ---------- 拖动带高度 ----------

    [Fact]
    public void BandHeight_IsQuarterOfClientHeight()
    {
        Assert.Equal(150, DragBandRules.BandHeight(ClientHeight));
        Assert.Equal(250, DragBandRules.BandHeight(1000));
    }

    [Fact]
    public void BandHeight_HasLowerBound_ForTinyWindows()
    {
        // 窗口很矮时 1/4 会小于操作栏高度(38),抓不住 → 用 44 DIP 兜底
        Assert.Equal(DragBandRules.MinHeight, DragBandRules.BandHeight(100));  // 25 → 44
        Assert.Equal(DragBandRules.MinHeight, DragBandRules.BandHeight(0));
    }

    [Fact]
    public void BandHeight_NegativeHeight_FallsBackToMin()
    {
        // 首帧/尚未布局时 Bounds 可能是 0 甚至异常值,不能算出负数把整条带抹掉
        Assert.Equal(DragBandRules.MinHeight, DragBandRules.BandHeight(-100));
    }

    // ---------- 位置判定 ----------

    [Theory]
    [InlineData(0, true)]      // 最顶部
    [InlineData(75, true)]     // 带内中部
    [InlineData(150, true)]    // 边界(含端点)
    [InlineData(151, false)]   // 刚出带
    [InlineData(400, false)]   // 页面中部
    [InlineData(-5, false)]    // 异常负值不接管
    public void IsInBand_Boundaries(double y, bool expected)
    {
        Assert.Equal(expected, DragBandRules.IsInBand(y, ClientHeight));
    }

    // ---------- 综合判定:四道闸 ----------

    [Fact]
    public void ShouldStartDrag_InBand_OnBlank_InImmersiveWithBarHidden()
    {
        Assert.True(DragBandRules.ShouldStartDrag(
            isImmersive: true,
            titleBarShown: false,
            titlePinned: false,
            isInteractive: false,
            y: 50,
            clientHeight: ClientHeight));
    }

    [Fact]
    public void ShouldStartDrag_NotImmersive_DoesNotHijack()
    {
        // 非沉浸模式有系统标题栏可拖,软件内再接管会与系统抢手势
        Assert.False(DragBandRules.ShouldStartDrag(
            isImmersive: false,
            titleBarShown: false,
            titlePinned: false,
            isInteractive: false,
            y: 50,
            clientHeight: ClientHeight));
    }

    [Fact]
    public void ShouldStartDrag_TitleBarShown_DoesNotHijack()
    {
        // 操作栏展开时那段是它自己的拖动区,两处接管会让拖动位置跳动/双击被吞
        Assert.False(DragBandRules.ShouldStartDrag(
            isImmersive: true,
            titleBarShown: true,
            titlePinned: false,
            isInteractive: false,
            y: 50,
            clientHeight: ClientHeight));
    }

    [Fact]
    public void ShouldStartDrag_TitlePinned_DoesNotHijack()
    {
        // 临时固定 ⇔ 常驻展开,同样属于"操作栏在场"
        Assert.False(DragBandRules.ShouldStartDrag(
            isImmersive: true,
            titleBarShown: false,
            titlePinned: true,
            isInteractive: false,
            y: 50,
            clientHeight: ClientHeight));
    }

    [Fact]
    public void ShouldStartDrag_InteractiveSource_AlwaysYields()
    {
        // 红线:点在按钮/输入框/页签上必须交给控件本身。
        // 若这里返回 true,点顶部菜单会变成拖窗口(与"设置页点分类没反应"同症状)。
        Assert.False(DragBandRules.ShouldStartDrag(
            isImmersive: true,
            titleBarShown: false,
            titlePinned: false,
            isInteractive: true,
            y: 10,
            clientHeight: ClientHeight));
    }

    [Fact]
    public void ShouldStartDrag_OutsideBand_DoesNotStart()
    {
        Assert.False(DragBandRules.ShouldStartDrag(
            isImmersive: true,
            titleBarShown: false,
            titlePinned: false,
            isInteractive: false,
            y: 200,
            clientHeight: ClientHeight));
    }
}
