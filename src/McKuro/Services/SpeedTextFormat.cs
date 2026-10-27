using System;

namespace McKuro.Services;

/// <summary>
/// 网速数值的「大字 + 小单位」拆解结果。
/// </summary>
/// <param name="Value">数值部分,如 <c>37.78</c> / <c>0</c> / <c>--</c>。</param>
/// <param name="Unit">单位部分,如 <c>MB/s</c> / <c>KB/s</c>;无有效速度时为空串。</param>
public readonly record struct SpeedDisplay(string Value, string Unit)
{
    /// <summary>数值 + 单位(空格分隔),用于紧凑场景(如状态栏/提示)。</summary>
    public string Combined => string.IsNullOrEmpty(Unit) ? Value : $"{Value} {Unit}";

    /// <summary>是否为「无速度」占位(0 B/s 或未开始)。</summary>
    public bool IsEmpty => Unit.Length == 0;
}

/// <summary>
/// 下载速度的展示口径(纯函数,可单测)。用户反馈(2026-10):下载卡里网速与进度都不明显 ——
/// 根因之一是网速只作为 12px 的小字和时间/磁盘挤在同一行,且数值与单位同样大小。
/// 这里把数值与单位拆开,交给 XAML 用「大字号数值 + 小字号单位」排版(对齐 FlClash 仪表盘),
/// 同时统一"何时显示 --"的口径,避免 0 B/s 与空串在不同调用点各写一遍。
/// </summary>
public static class SpeedTextFormat
{
    /// <summary>低于该值视为"没有速度"(0 B/s 的占位与未开始下载同义)。</summary>
    public const double MinMeaningfulBps = 1;

    /// <summary>字节速率 → 大字数值 + 单位。0/负值/NaN 返回 <see cref="SpeedDisplay"/>("--", "")。</summary>
    public static SpeedDisplay FromBytesPerSecond(double bps)
    {
        if (!(bps >= MinMeaningfulBps) || double.IsInfinity(bps))
        {
            return new SpeedDisplay("--", "");
        }

        // 大字号数值最多两位小数:再多的位数在大字号下只是视觉噪声
        if (bps >= 1024 * 1024 * 1024)
        {
            return new SpeedDisplay(Trim(bps / (1024.0 * 1024 * 1024)), "GB/s");
        }
        if (bps >= 1024 * 1024)
        {
            return new SpeedDisplay(Trim(bps / (1024.0 * 1024)), "MB/s");
        }
        if (bps >= 1024)
        {
            return new SpeedDisplay(Trim(bps / 1024.0), "KB/s");
        }
        return new SpeedDisplay(Trim(bps), "B/s");
    }

    /// <summary>MB/s 数值 → 大字数值 + 单位(曲线峰值与当前速度共用同一口径)。</summary>
    public static SpeedDisplay FromMbps(double mbps)
        => FromBytesPerSecond(mbps * 1024.0 * 1024.0);

    /// <summary>
    /// 保留至多两位小数并去掉无意义的尾零:37.78 → "37.78",37.5 → "37.5",37 → "37"。
    /// 用 InvariantCulture 的小数点再按当前文化替换,避免某些文化下千位分隔符混进数值。
    /// </summary>
    private static string Trim(double v)
    {
        var s = v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        var sep = System.Globalization.CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
        return sep == "." ? s : s.Replace(".", sep);
    }
}
