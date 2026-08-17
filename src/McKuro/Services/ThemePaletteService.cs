using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using McKuro.Core.Services.Settings;

namespace McKuro.Services;

/// <summary>壁纸驱动的语义色板，供应用壳层和玻璃表面使用。</summary>
public sealed record ThemePalette(
    Color Accent,
    Color AccentHover,
    Color BackdropBase,
    Color BackdropTint,
    Color GlassFill,
    Color GlassFillStrong,
    Color GlassStroke,
    Color TextOnWallpaper,
    Color TextMuted,
    Color AccentGlow,
    bool IsDark);

/// <summary>
/// 从壁纸提取主色并一次性更新应用资源。取色在后台执行，资源写入回到 Avalonia UI 线程。
/// </summary>
public sealed class ThemePaletteService
{
    private readonly ISettingsService _settings;

    public ThemePaletteService(ISettingsService settings)
    {
        _settings = settings;
    }

    public ThemePalette Current { get; private set; } = CreateDefault(isDark: true);

    public async Task<ThemePalette> ApplyCurrentAsync(CancellationToken cancellationToken = default)
    {
        var settings = _settings.Current;
        var path = settings.DynamicPaletteEnabled ? settings.WallpaperPath : "";
        var colors = await LoadDominantColorsAsync(path, cancellationToken).ConfigureAwait(false);
        var palette = CreatePalette(colors.FirstOrDefault(), IsDarkTheme(settings));

        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => ApplyResources(palette));
        return palette;
    }

    public async Task<ThemePalette> ApplyWallpaperAsync(string path, CancellationToken cancellationToken = default)
    {
        var colors = await LoadDominantColorsAsync(path, cancellationToken).ConfigureAwait(false);
        var palette = CreatePalette(colors.FirstOrDefault(), IsDarkTheme(_settings.Current));
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => ApplyResources(palette));
        return palette;
    }

    public static ThemePalette CreatePalette(Color? dominant, bool isDark)
    {
        var accent = dominant is { } color && color.A > 0
            ? EnsureAccentContrast(color, isDark)
            : Color.FromRgb(90, 141, 238);
        var accentHover = Blend(accent, isDark ? Colors.White : Colors.Black, isDark ? 0.18 : 0.12);
        var text = isDark ? Color.FromRgb(245, 248, 255) : Color.FromRgb(25, 32, 46);
        var muted = isDark ? Color.FromRgb(191, 204, 226) : Color.FromRgb(78, 91, 112);

        return isDark
            ? new ThemePalette(
                accent,
                accentHover,
                Color.FromRgb(7, 12, 24),
                Color.FromArgb(158, 5, 10, 24),
                Color.FromArgb(148, 20, 29, 50),
                Color.FromArgb(198, 17, 25, 45),
                Color.FromArgb(112, 205, 224, 250),
                text,
                muted,
                Color.FromArgb(86, accent.R, accent.G, accent.B),
                true)
            : new ThemePalette(
                accent,
                accentHover,
                Color.FromRgb(238, 243, 250),
                Color.FromArgb(148, 246, 249, 253),
                Color.FromArgb(172, 255, 255, 255),
                Color.FromArgb(218, 255, 255, 255),
                Color.FromArgb(100, 80, 98, 125),
                text,
                muted,
                Color.FromArgb(52, accent.R, accent.G, accent.B),
                false);
    }

    private static ThemePalette CreateDefault(bool isDark)
        => CreatePalette(Color.FromRgb(90, 141, 238), isDark);

    private async Task<List<Color>> LoadDominantColorsAsync(string? path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return [];
        }

        try
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var bitmap = new Bitmap(stream);
                return ColorThiefHelper.GetDominantColors(bitmap, 3);
            }, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return [];
        }
    }

    private void ApplyResources(ThemePalette palette)
    {
        Current = palette;
        var app = Application.Current;
        if (app is null)
        {
            return;
        }

        SetBrush(app, "McKuroAccent", palette.Accent);
        SetBrush(app, "McKuroAccentHover", palette.AccentHover);
        SetBrush(app, "McKuroBackdropBase", palette.BackdropBase);
        SetBrush(app, "McKuroBackdropTint", palette.BackdropTint);
        SetBrush(app, "McKuroGlassFill", palette.GlassFill);
        SetBrush(app, "McKuroGlassFillStrong", palette.GlassFillStrong);
        SetBrush(app, "McKuroGlassStroke", palette.GlassStroke);
        SetBrush(app, "McKuroTextOnWallpaper", palette.TextOnWallpaper);
        SetBrush(app, "McKuroTextMuted", palette.TextMuted);
        SetBrush(app, "McKuroAccentGlow", palette.AccentGlow);
        SetBrush(app, "McKuroNavFill", palette.GlassFillStrong);
        SetBrush(app, "McKuroContentTint", palette.BackdropTint);
        SetBrush(app, "McKuroHomeScrim", Color.FromArgb(palette.IsDark ? (byte)72 : (byte)38, palette.BackdropBase.R, palette.BackdropBase.G, palette.BackdropBase.B));
        app.Resources["McKuroWallpaperBlurEffect"] = new BlurEffect
        {
            Radius = _settings.Current.GlassQuality switch
            {
                "High" => 26,
                "Low" => 0,
                _ => 18,
            },
        };

        // 桥接 Semi 的基础表面与主色资源，让尚未迁移的业务页也能表现为半透明玻璃。
        SetBrush(app, "SemiColorBg0", palette.BackdropBase);
        SetBrush(app, "SemiColorBg1", palette.GlassFill);
        SetBrush(app, "SemiColorBg2", palette.GlassFill);
        SetBrush(app, "SemiColorBg3", palette.GlassFillStrong);
        SetBrush(app, "SemiColorPrimary", palette.Accent);
        SetBrush(app, "SemiColorPrimaryHover", palette.AccentHover);
    }

    private static void SetBrush(Application app, string key, Color color)
        => app.Resources[key] = new SolidColorBrush(color);

    private static bool IsDarkTheme(AppSettings settings)
    {
        if (settings.Theme == "Dark")
        {
            return true;
        }
        if (settings.Theme == "Light")
        {
            return false;
        }

        return Application.Current?.PlatformSettings?.GetColorValues().ThemeVariant
            == Avalonia.Platform.PlatformThemeVariant.Dark;
    }

    private static Color EnsureAccentContrast(Color color, bool isDark)
    {
        var luminance = RelativeLuminance(color);
        if (isDark && luminance < 0.18)
        {
            return Blend(color, Colors.White, 0.45);
        }
        if (!isDark && luminance > 0.72)
        {
            return Blend(color, Colors.Black, 0.35);
        }
        return Color.FromRgb(color.R, color.G, color.B);
    }

    private static Color Blend(Color source, Color target, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromRgb(
            (byte)Math.Round(source.R + (target.R - source.R) * amount),
            (byte)Math.Round(source.G + (target.G - source.G) * amount),
            (byte)Math.Round(source.B + (target.B - source.B) * amount));
    }

    private static double RelativeLuminance(Color color)
    {
        static double Channel(byte value)
        {
            var normalized = value / 255d;
            return normalized <= 0.03928
                ? normalized / 12.92
                : Math.Pow((normalized + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
    }
}
