using Avalonia.Media;
using McKuro.Services;

namespace McKuro.Tests;

public sealed class ThemePaletteServiceTests
{
    [Fact]
    public void DarkPaletteLiftsAnAlmostBlackWallpaperAccent()
    {
        var palette = ThemePaletteService.CreatePalette(Color.FromRgb(5, 10, 15), isDark: true);

        Assert.True(palette.IsDark);
        Assert.True(palette.Accent.R > 5 || palette.Accent.G > 10 || palette.Accent.B > 15);
        Assert.Equal(255, palette.TextOnWallpaper.A);
    }

    [Fact]
    public void LightPaletteKeepsReadableTextAndTransparentGlass()
    {
        var palette = ThemePaletteService.CreatePalette(Color.FromRgb(245, 225, 210), isDark: false);

        Assert.False(palette.IsDark);
        Assert.True(palette.TextOnWallpaper.R < 80);
        Assert.True(palette.GlassFill.A < 255);
        Assert.True(palette.Accent.R < 245 || palette.Accent.G < 225 || palette.Accent.B < 210);
    }
}
