using System.Globalization;
using Avalonia.Media;
using McKuro.ViewModels;

namespace McKuro.Tests;

/// <summary>ThemeAdaptiveEmphasisBrushConverter 测试(参照 WutheringWavesTool 亮度→前景色逻辑)。</summary>
public class ThemeAdaptiveEmphasisBrushTests
{
    [Fact]
    public void Dark_Returns_BrightYellow()
    {
        var c = new ThemeAdaptiveEmphasisBrushConverter();
        // 无 Avalonia Application 时走默认:RequestedThemeVariant 为 null → 非 dark → 返回深琥珀
        var brush = Assert.IsType<SolidColorBrush>(c.Convert(null, typeof(IBrush), null, CultureInfo.InvariantCulture));
        Assert.Equal(Color.Parse("#8a6d1f"), brush.Color);
    }

    [Fact]
    public void Cyan_Parameter_Returns_DarkCyan_In_Light()
    {
        var c = new ThemeAdaptiveEmphasisBrushConverter();
        var brush = Assert.IsType<SolidColorBrush>(c.Convert(null, typeof(IBrush), "cyan", CultureInfo.InvariantCulture));
        Assert.Equal(Color.Parse("#007a85"), brush.Color);
    }

    [Fact]
    public void ConvertBack_Throws()
    {
        var c = new ThemeAdaptiveEmphasisBrushConverter();
        Assert.Throws<NotSupportedException>(() => c.ConvertBack(null, typeof(IBrush), null, CultureInfo.InvariantCulture));
    }
}
