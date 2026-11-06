namespace McKuro.Services;

/// <summary>
/// 导航选中胶囊的直接操控几何：与 UI/指针事件解耦，便于验证边界和吸附规则。
/// 所有坐标均使用相对导航面板同一坐标系的 DIP。
/// </summary>
public static class NavPillGesture
{
    /// <summary>越过导航首尾时保留抓取感，但用逐渐增加的阻力限制位移。</summary>
    public static double RubberBand(double value, double minimum, double maximum, double resistance = 36)
    {
        if (minimum > maximum)
        {
            (minimum, maximum) = (maximum, minimum);
        }
        if (value < minimum)
        {
            return minimum - Band(minimum - value, resistance);
        }
        if (value > maximum)
        {
            return maximum + Band(value - maximum, resistance);
        }
        return value;
    }

    /// <summary>
    /// 松手时把短距离速度投影到落点，再取最近导航项；慢速拖拽仍严格按就近吸附。
    /// 速度有上限，避免一次异常长的指针事件让胶囊跨越整栏。
    /// </summary>
    public static int FindSnapIndex(double value, IReadOnlyList<double> targets, double velocity = 0)
    {
        if (targets.Count == 0)
        {
            return -1;
        }

        var projected = value + Math.Clamp(velocity, -1200, 1200) * 0.1;
        var bestIndex = 0;
        var bestDistance = double.MaxValue;
        for (var i = 0; i < targets.Count; i++)
        {
            var distance = Math.Abs(targets[i] - projected);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = i;
            }
        }
        return bestIndex;
    }

    private static double Band(double overshoot, double resistance)
        => resistance * (1 - Math.Exp(-overshoot / resistance));
}
