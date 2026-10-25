namespace McKuro.Services;

/// <summary>
/// 贴边自动隐藏的显隐状态机(左侧导航栏、顶部窗口操作栏共用;纯逻辑便于单测)。
/// <para>
/// 外部按固定节奏喂"指针到窗口对应边缘的距离":
/// <list type="bullet">
/// <item>距离 ≤ <see cref="RevealZone"/> → 立即展开(贴边唤出);</item>
/// <item>已展开且在「<see cref="Extent"/> + <see cref="HoldZone"/>」内 → 保持展开;</item>
/// <item>否则累计离开拍数,达到 <see cref="HideDelayTicks"/> 后收起(迟滞,避免快速划过时闪动)。</item>
/// </list>
/// 指针不在窗口内时传 <c>null</c>(按"离开"处理)。
/// </para>
/// </summary>
public sealed class EdgeRevealState
{
    /// <summary>展开后的可视尺寸(导航栏宽 / 操作栏高,DIP)。</summary>
    public required double Extent { get; init; }

    /// <summary>贴边唤出带:指针距边缘 ≤ 该值即展开(DIP)。</summary>
    public required double RevealZone { get; init; }

    /// <summary>已展开时继续悬停的余量:超出「展开区域 + 该余量」才开始累计收起(DIP)。</summary>
    public required double HoldZone { get; init; }

    /// <summary>离开后连续多少次 <see cref="Update"/> 才收起(配合轮询间隔形成时间延迟)。</summary>
    public int HideDelayTicks { get; init; } = 8;

    /// <summary>当前应否展开。</summary>
    public bool Shown { get; private set; }

    private int _hideTicks;

    /// <summary>喂入一次指针状态,返回更新后的显隐。</summary>
    /// <param name="edgeDistance">指针到窗口左/上边缘的距离(DIP);指针不在窗口内传 null。</param>
    public bool Update(double? edgeDistance)
    {
        // 贴边:立即展开(同时清零收起计时,鼠标在边缘来回也不会抖动)
        if (edgeDistance is { } distance && distance <= RevealZone)
        {
            _hideTicks = 0;
            Shown = true;
            return Shown;
        }

        if (!Shown)
        {
            _hideTicks = 0;
            return false;
        }

        // 仍悬停在展开区域附近:保持展开
        if (edgeDistance is { } stay && stay <= Extent + HoldZone)
        {
            _hideTicks = 0;
            return true;
        }

        if (++_hideTicks >= Math.Max(1, HideDelayTicks))
        {
            _hideTicks = 0;
            Shown = false;
        }
        return Shown;
    }

    /// <summary>
    /// 高频指针事件用的轻量喂入:只处理「贴边展开 / 悬停保持」,<b>不推进收起计时</b>
    /// (指针事件每秒几十次,若参与收起计数,延迟会被压到几毫秒)。
    /// 返回是否由收起变为展开(调用方据此立即刷新视觉)。
    /// </summary>
    public bool Observe(double? edgeDistance)
    {
        if (edgeDistance is { } distance && distance <= RevealZone)
        {
            _hideTicks = 0;
            if (Shown)
            {
                return false;
            }
            Shown = true;
            return true;
        }

        if (Shown && edgeDistance is { } stay && stay <= Extent + HoldZone)
        {
            _hideTicks = 0;
        }
        return false;
    }

    /// <summary>强制展开(Alt 快捷键、设置刚改动、指针事件唤出后同步状态)。</summary>
    public void ForceShow()
    {
        Shown = true;
        _hideTicks = 0;
    }

    /// <summary>强制收起(窗口失焦、启动时启用自动隐藏、设置里刚开启自动隐藏)。</summary>
    public void ForceHide()
    {
        Shown = false;
        _hideTicks = 0;
    }
}
