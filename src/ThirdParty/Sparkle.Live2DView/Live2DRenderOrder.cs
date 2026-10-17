namespace Sparkle.Live2DView;

/// <summary>
/// drawable 绘制顺序的合法性判定。
///
/// 官方 Cubism 渲染器用<b>槽位赋值</b>建立绘制顺序:
/// <code>sortedDrawableIndexList[renderOrder[i]] = i</code>
/// 因此 renderOrder 必须是 0..N-1 的一个排列。第三方"分层 PSD → Cubism"导出工具
/// 有可能把整张数组写成全 0;此时槽位表退化为「下标 0 被最后一个 drawable 覆盖、
/// 其余槽位仍是 0」,渲染器只会画出 drawable[0] 与 drawable[N-1]。
/// 实机症状是模型只显示极少数图层(例如只剩一条尾巴和一件挂件)。
///
/// 这里把判定单独抽成纯函数,便于测试(见 tests/McKuro.Tests)。
/// </summary>
public static class Live2DRenderOrder
{
    /// <summary>
    /// 判定 <paramref name="orders"/> 是否为 0..count-1 的排列(既检查取值范围也检查重复)。
    /// 空数组视为合法(无可修复内容);单元素时只有 0 合法。
    /// </summary>
    public static bool IsValidPermutation(ReadOnlySpan<int> orders)
    {
        int count = orders.Length;
        if (count == 0)
        {
            return true;
        }

        var seen = new bool[count];
        for (int i = 0; i < count; i++)
        {
            int order = orders[i];
            if (order < 0 || order >= count || seen[order])
            {
                return false;
            }
            seen[order] = true;
        }
        return true;
    }
}
