using Sparkle.Live2DView;
using Xunit;

namespace McKuro.Tests;

/// <summary>
/// Live2D「自带动画」并行层选择测试。
///
/// 回归背景:第三方「分层 PSD → Cubism」导出器(如 PuppetLoom)把互不相干的肢体动作
/// (左臂/右臂/左腿/右腿)全塞进同一个 <c>Idle</c> 组,并在其网页播放器里<b>同时</b>驱动它们
/// (puppetloom-web.js:<c>for (const r of behaviors) r.autoplay &amp;&amp; push(...)</c>)。
/// 但 Cubism 的 <c>Idle</c> 语义是<b>备选</b> —— 官方 <c>LAppModel.Update()</c> 用
/// <c>StartRandomMotion("Idle", PriorityIdle)</c> 每次只随机播一条,于是四个肢体里
/// 永远只有一个在动,实机表现为「模型基本静止」。
///
/// 这里按「驱动参数互不重叠」把动作分层,层内可并行;官方模型同一 Idle 组里各动作
/// 驱动同一批参数(互为备选),分层后仍是 1 条,行为不变。
/// </summary>
public class Live2DMotionPlanTests
{
    private static string[] Set(params string[] ids) => ids;

    [Fact]
    public void Disjoint_Tracks_All_Selected()
    {
        // 本项目实机形态:四条动作各驱动自己的参数,应全部入选(4 条肢体同时动)
        var result = Live2DMotionPlan.SelectConcurrentLayers(
        [
            Set("arm_L"),
            Set("arm_R"),
            Set("leg_L"),
            Set("leg_R"),
        ]);

        Assert.Equal([0, 1, 2, 3], result);
    }

    [Fact]
    public void Overlapping_Tracks_Keep_First_Only()
    {
        // 官方模型形态:同一 Idle 组里每条动作都驱动同一批参数 → 互为备选,只留第一条
        var shared = Set("ParamAngleX", "ParamAngleY", "ParamBreath");
        var result = Live2DMotionPlan.SelectConcurrentLayers([shared, shared, shared]);

        Assert.Equal([0], result);
    }

    [Fact]
    public void Partial_Overlap_Keeps_First_Claimant_And_Admits_Others()
    {
        // 第一条占住共享参数;第二条因冲突被跳过;第三、四条互不冲突应入选
        var result = Live2DMotionPlan.SelectConcurrentLayers(
        [
            Set("shared", "a"),
            Set("shared", "b"),
            Set("c"),
            Set("d"),
        ]);

        Assert.Equal([0, 2, 3], result);
    }

    [Fact]
    public void Track_With_No_Targets_Is_Skipped()
    {
        // 不驱动任何参数的动作不会产生可见效果,不应占用并行层
        var result = Live2DMotionPlan.SelectConcurrentLayers(
        [
            [],
            Set("arm_L"),
        ]);

        Assert.Equal([1], result);
    }

    [Fact]
    public void Null_Track_Is_Skipped()
    {
        var result = Live2DMotionPlan.SelectConcurrentLayers([null!, Set("a")]);

        Assert.Equal([1], result);
    }

    [Fact]
    public void Order_Is_Preserved()
    {
        // 选择顺序即播放顺序,必须保持输入次序(便于稳定复现)
        var result = Live2DMotionPlan.SelectConcurrentLayers(
        [
            Set("z"),
            Set("y"),
            Set("x"),
        ]);

        Assert.Equal([0, 1, 2], result);
    }

    [Fact]
    public void Empty_Input_Yields_Nothing()
    {
        Assert.Empty(Live2DMotionPlan.SelectConcurrentLayers([]));
    }

    [Fact]
    public void Single_Parameter_Groups_Are_Independent()
    {
        // 单参数动作:只要参数名不同就互不干扰(PuppetLoom 的实际布局)
        var result = Live2DMotionPlan.SelectConcurrentLayers(
        [
            Set("ParamPL_limb_idle_arm_L"),
            Set("ParamPL_limb_idle_arm_R"),
            Set("ParamPL_limb_idle_leg_L"),
            Set("ParamPL_limb_idle_leg_R"),
        ]);

        Assert.Equal(4, result.Count);
    }

    [Fact]
    public void Duplicate_Within_One_Track_Does_Not_Block_It()
    {
        // 同一条动作里重复列出同一参数(导出器可能重复)不应被自己判为冲突
        var result = Live2DMotionPlan.SelectConcurrentLayers(
        [
            Set("a", "a", "a"),
            Set("b"),
        ]);

        Assert.Equal([0, 1], result);
    }

    [Fact]
    public void Null_Argument_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => Live2DMotionPlan.SelectConcurrentLayers(null!));
    }

    [Fact]
    public void Real_World_Shape_Produces_Four_Layers()
    {
        // 心-红黑实测:moc 参数表里这 4 个 limb 参数与动作曲线一一对应
        var result = Live2DMotionPlan.SelectConcurrentLayers(
        [
            Set("ParamPL_limb_idle_arm_L_ece39ae0ef68"),
            Set("ParamPL_limb_idle_arm_R_85a096270b5f"),
            Set("ParamPL_limb_idle_leg_L_cf3b06ab1d7d"),
            Set("ParamPL_limb_idle_leg_R_c30012812403"),
        ]);

        Assert.Equal(4, result.Count);
    }
}
