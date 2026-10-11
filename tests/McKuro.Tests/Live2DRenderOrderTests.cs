using Sparkle.Live2DView;
using Xunit;

namespace McKuro.Tests;

/// <summary>
/// Live2D drawable 绘制顺序(renderOrder)合法性判定测试。
///
/// 回归背景:官方 Cubism 渲染器按<b>槽位赋值</b>排序
/// <c>sortedDrawableIndexList[renderOrder[i]] = i</c>,
/// renderOrder 必须是 0..N-1 的排列。有第三方"分层 PSD → Cubism"导出工具把它整张写成全 0,
/// 渲染器便只画出 drawable[0] 与 drawable[N-1] —— 实机表现为模型只剩极少图层
/// (本项目实测:主页只剩一条尾巴和一件挂件)。此判定用于识别并归一化这类模型。
/// </summary>
public class Live2DRenderOrderTests
{
    [Fact]
    public void Identity_Is_Valid()
    {
        Assert.True(Live2DRenderOrder.IsValidPermutation(new[] { 0, 1, 2, 3 }));
    }

    [Fact]
    public void Permutation_In_Any_Order_Is_Valid()
    {
        // 排列允许任意顺序,只要每个槽位恰好被占一次
        Assert.True(Live2DRenderOrder.IsValidPermutation(new[] { 3, 1, 0, 2 }));
        Assert.True(Live2DRenderOrder.IsValidPermutation(new[] { 2, 0, 3, 1 }));
    }

    [Fact]
    public void AllZeros_Is_Invalid()
    {
        // 本项目的实际故障形态:renderOrder 全 0
        Assert.False(Live2DRenderOrder.IsValidPermutation(new[] { 0, 0, 0, 0 }));
        Assert.False(Live2DRenderOrder.IsValidPermutation(new int[59]));
    }

    [Fact]
    public void Duplicate_Value_Is_Invalid()
    {
        // 双 0 占同一槽位,同时缺一个槽位
        Assert.False(Live2DRenderOrder.IsValidPermutation(new[] { 0, 0, 2, 3 }));
    }

    [Fact]
    public void OutOfRange_Value_Is_Invalid()
    {
        // 越界会写到槽位表之外(官方渲染器是 Buffer.set,越界即丢层/异常)
        Assert.False(Live2DRenderOrder.IsValidPermutation(new[] { 0, 1, 2, 4 }));
        Assert.False(Live2DRenderOrder.IsValidPermutation(new[] { 0, 1, 2, -1 }));
    }

    [Fact]
    public void Missing_High_Slot_Is_Invalid()
    {
        // 0..n-1 少一个(这里缺 3,多出 0)
        Assert.False(Live2DRenderOrder.IsValidPermutation(new[] { 0, 1, 2, 0 }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Trivial_Counts_Are_Valid(int count)
    {
        // 0/1 个 drawable 不存在排序歧义
        Assert.True(Live2DRenderOrder.IsValidPermutation(new int[count]));
    }

    [Fact]
    public void Single_Element_NonZero_Is_Valid()
    {
        // 单个 drawable 时,renderOrder 只能是 0;非 0 反而越界
        Assert.True(Live2DRenderOrder.IsValidPermutation(new[] { 0 }));
        Assert.False(Live2DRenderOrder.IsValidPermutation(new[] { 1 }));
    }

    [Fact]
    public void Real_World_Sample_Shapes()
    {
        // 心-红黑:59 个 drawable,故障时全 0
        Assert.False(Live2DRenderOrder.IsValidPermutation(new int[59]));

        // Hiyori:134 个 drawable,正常时是完整排列
        var hiyori = new int[134];
        for (int i = 0; i < hiyori.Length; i++)
        {
            hiyori[i] = 133 - i;   // 反向排列同样合法
        }
        Assert.True(Live2DRenderOrder.IsValidPermutation(hiyori));
    }

    [Fact]
    public void Large_AllZero_Array_Is_Detected()
    {
        // 大模型同样要能识别(避免只对小样本生效)
        Assert.False(Live2DRenderOrder.IsValidPermutation(new int[1000]));
    }
}
