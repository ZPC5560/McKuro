using System.Collections.ObjectModel;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using McKuro.Core.Models.Guide;
using McKuro.Core.Models.Roles;
using McKuro.Core.Services.Roles;
using McKuro.Services;

namespace McKuro.ViewModels;

/// <summary>角色养成页:原生显示当前账号的角色养成数据。</summary>
public sealed partial class RolesViewModel : ViewModelBase
{
    private readonly IMessenger _messenger;

    /// <summary>属性筛选中的"全部"选项。</summary>
    public const string AllAttributeFilter = "全部属性";

    public const string SortByStar = "星级 ↓";
    public const string SortByName = "名称 ↑";

    [ObservableProperty]
    private string _statusText = "就绪";

    [ObservableProperty]
    private string _sourceText = "数据源: -";

    [ObservableProperty]
    private string _tokenText = "";

    [ObservableProperty]
    private string _roleIdText = "";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _hasRoles;

    [ObservableProperty]
    private RoleDetail? _selectedRole;

    /// <summary>角色详情头部卡片背景(从角色立绘提取主色生成渐变;参照 WutheringWavesTool ImgColorBgTask)。</summary>
    [ObservableProperty]
    private Avalonia.Media.IBrush? _roleHeaderBackground;

    /// <summary>角色头部文字色(按取色主色亮度自适应:背景亮→深字,背景暗→浅字;参照 WutheringWavesTool GetForegroundColor)。</summary>
    [ObservableProperty]
    private Avalonia.Media.IBrush? _roleNameBrush;

    /// <summary>mcguide 登录状态文案。</summary>
    [ObservableProperty]
    private string _guideStatusText = "";

    /// <summary>是否已登录 mcguide 攻略站(控制登录表单/达成度区可见性)。</summary>
    [ObservableProperty]
    private bool _guideLoggedIn;

    /// <summary>mcguide 达成度加载中。</summary>
    [ObservableProperty]
    private bool _guideLoading;

    /// <summary>当前选中角色的 mcguide 官方达成度(未加载/未支持时为 null)。</summary>
    [ObservableProperty]
    private GuideIntroductionInfo? _guideAchievement;

    /// <summary>是否有 mcguide 达成度可展示。</summary>
    [ObservableProperty]
    private bool _hasGuideAchievement;

    [ObservableProperty]
    private string _selectedAttributeFilter = AllAttributeFilter;

    [ObservableProperty]
    private string _selectedSort = SortByStar;

    /// <summary>全部角色(数据源)。</summary>
    public ObservableCollection<RoleDetail> Roles { get; } = [];

    /// <summary>过滤+排序后的显示集合(View 绑定此集合)。</summary>
    public ObservableCollection<RoleDetail> FilteredRoles { get; } = [];

    /// <summary>属性筛选选项(含"全部属性")。</summary>
    public ObservableCollection<string> AttributeFilters { get; } = [];

    public IReadOnlyList<string> SortOptions { get; } = [SortByStar, SortByName];

    public RolesViewModel(IMessenger? messenger = null)
    {
        _messenger = messenger ?? WeakReferenceMessenger.Default;
        TokenText = AppServices.Settings.Current.KujiequToken;
        RoleIdText = AppServices.Settings.Current.RoleId;
        GuideLoggedIn = AppServices.Guide.HasToken;
        GuideStatusText = AppServices.Guide.HasToken
            ? $"已登录攻略站 ({AppServices.Settings.Current.GuideCName})"
            : "未登录攻略站";
        // 加载页面自动同步库街区(同步失败/未登录时自动兜底读本地缓存)
        _ = LoadFromKujiequAsync();

        // 登录/切号后自动同步:从设置重读 Token/RoleId 并拉取库街区角色数据
        _messenger.Register<RolesViewModel, RolesRefreshRequestedMessage>(this, static (recipient, message) =>
        {
            recipient.TokenText = AppServices.Settings.Current.KujiequToken;
            recipient.RoleIdText = AppServices.Settings.Current.RoleId;
            _ = recipient.LoadFromKujiequAsync();
        });
    }

    /// <summary>当前库街区账号 ID(用于校验缓存归属;未登录为空)。</summary>
    private static string CurrentAccountId => AppServices.KuroAccounts.Current?.UserId ?? "";

    partial void OnTokenTextChanged(string value) => AppServices.Settings.Current.KujiequToken = value;

    partial void OnRoleIdTextChanged(string value) => AppServices.Settings.Current.RoleId = value;

    /// <summary>选中角色变化时:拉取官方达成度 + 角色头部背景取色。</summary>
    partial void OnSelectedRoleChanged(RoleDetail? value)
    {
        GuideAchievement = null;
        HasGuideAchievement = false;
        if (value is not null)
        {
            _ = LoadGuideAchievementAsync();
            _ = LoadRoleHeaderBackgroundAsync();
        }
    }

    /// <summary>从角色立绘提取主色,生成头部卡片渐变背景(参照 WutheringWavesTool ImgColorBgTask 的 ColorThief 取色)。</summary>
    private async Task LoadRoleHeaderBackgroundAsync()
    {
        var role = SelectedRole;
        var url = role?.Role?.RolePicUrl;
        if (role is null || string.IsNullOrWhiteSpace(url))
        {
            RoleHeaderBackground = null;
            RoleNameBrush = null;
            return;
        }
        try
        {
            // 复用一个轻量 HttpClient 下载图片并解码(AOT 安全)
            var bitmap = await AppServices.Http.GetByteArrayAsync(url);
            if (bitmap.Length == 0)
            {
                return;
            }
            using var ms = new System.IO.MemoryStream(bitmap, writable: false);
            var bmp = new Avalonia.Media.Imaging.Bitmap(ms);
            var colors = ColorThiefHelper.GetDominantColors(bmp, 2);
            if (role != SelectedRole)
            {
                return; // 已切换角色,丢弃过期结果
            }
            if (colors.Count >= 1)
            {
                // 右下角放射渐变:主色 → 两个过渡色 → 白色(主色只集中在右下,过渡柔和)
                var main = colors[0];
                var white = Avalonia.Media.Colors.White;
                RoleHeaderBackground = new Avalonia.Media.RadialGradientBrush
                {
                    Center = new Avalonia.RelativePoint(1, 1, Avalonia.RelativeUnit.Relative),
                    GradientOrigin = new Avalonia.RelativePoint(1, 1, Avalonia.RelativeUnit.Relative),
                    RadiusX = new Avalonia.RelativeScalar(1.2, Avalonia.RelativeUnit.Relative),
                    RadiusY = new Avalonia.RelativeScalar(1.2, Avalonia.RelativeUnit.Relative),
                    GradientStops =
                    {
                        new Avalonia.Media.GradientStop(main, 0),
                        new Avalonia.Media.GradientStop(Mix(main, white, 1.0 / 3.0), 0.33),
                        new Avalonia.Media.GradientStop(Mix(main, white, 2.0 / 3.0), 0.66),
                        new Avalonia.Media.GradientStop(white, 1),
                    },
                };
                // 前景文字按渐变主色(右下)亮度决定,保证右下主色区文字可读
                RoleNameBrush = ForegroundFor(main);
            }
        }
        catch (Exception)
        {
            // 取色失败:保留默认背景,不影响主流程
        }
    }

    /// <summary>线性插值两个颜色(t=0→a,t=1→b)。</summary>
    private static Avalonia.Media.Color Mix(Avalonia.Media.Color a, Avalonia.Media.Color b, double t)
    {
        byte L(byte x, byte y) => (byte)Math.Clamp((int)Math.Round(x + (y - x) * t), 0, 255);
        return Avalonia.Media.Color.FromRgb(L(a.R, b.R), L(a.G, b.G), L(a.B, b.B));
    }

    /// <summary>按主色亮度决定前景文字色(参照 WutheringWavesTool GetForegroundColor:亮度→深/浅字)。</summary>
    private static Avalonia.Media.IBrush ForegroundFor(Avalonia.Media.Color bg)
    {
        double luminance = (bg.R * 0.299 + bg.G * 0.587 + bg.B * 0.114) / 255.0;
        return luminance > 0.5
            ? new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#1a1a1a"))   // 背景亮:深字
            : new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#f5f5f5"));  // 背景暗:浅字
    }

    /// <summary>拉取当前选中角色的 mcguide 官方达成度。</summary>
    private async Task LoadGuideAchievementAsync()
    {
        var role = SelectedRole;
        if (role is null || GuideLoading)
        {
            return;
        }
        if (!AppServices.Guide.HasToken)
        {
            GuideStatusText = "未登录攻略站(可输入手机号+验证码登录)";
            return;
        }

        var cardRoleId = role.Role?.RoleId ?? 0;
        if (cardRoleId <= 0)
        {
            GuideStatusText = $"该角色无 cardRoleId,无法查询攻略站({role.RoleName})";
            return;
        }

        GuideLoading = true;
        GuideStatusText = $"正在拉取 {role.RoleName} 达成度…";
        try
        {
            var info = await AppServices.Guide.GetAchievementAsync(role.RoleName, cardRoleId);
            if (role != SelectedRole)
            {
                return; // 已切到其他角色,丢弃过期结果
            }
            GuideAchievement = info;
            HasGuideAchievement = info is not null;
            GuideStatusText = info is null ? "未获取到该角色达成度" : $"官方达成度: {info.Grade ?? "-"}";
        }
        catch (Exception ex)
        {
            GuideStatusText = $"拉取达成度失败: {ex.Message}";
        }
        finally
        {
            GuideLoading = false;
        }
    }

    [RelayCommand]
    private async Task LoadFromKujiequAsync()
    {
        if (IsBusy)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(TokenText))
        {
            LoadCachedOrHint("未登录库街区");
            return;
        }
        if (string.IsNullOrWhiteSpace(RoleIdText))
        {
            LoadCachedOrHint("未配置角色 ID");
            return;
        }

        IsBusy = true;
        StatusText = "正在从库街区获取角色数据…";
        try
        {
            var result = await AppServices.Roles.LoadFromKujiequAsync(TokenText, RoleIdText);
            if (result.IsSuccess)
            {
                ApplyRoles(result);
                StatusText = $"库街区同步成功: {result.Roles.Count} 个角色";
            }
            else
            {
                // 同步失败(网络/token 失效等):兜底读本地缓存
                LoadCachedOrHint(result.Message ?? "同步失败");
            }
        }
        catch (Exception ex)
        {
            LoadCachedOrHint($"获取失败: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>兜底:校验账号后读取本地缓存;无可用缓存时给出提示。</summary>
    private void LoadCachedOrHint(string reason)
    {
        var cached = AppServices.Roles.LoadFromCache(CurrentAccountId, RoleIdText);
        if (cached.IsSuccess && cached.Roles.Count > 0)
        {
            ApplyRoles(cached);
            StatusText = $"{reason} → 已加载本地缓存 (角色数: {cached.Roles.Count})";
        }
        else
        {
            StatusText = $"{reason} → 本地缓存不可用({cached.Message})";
        }
    }

    /// <summary>读取本地缓存(校验当前账号归属;库街区未登录或同步异常时的备用入口)。</summary>
    [RelayCommand]
    private void LoadFromLocal()
    {
        var accountId = CurrentAccountId;
        var result = AppServices.Roles.LoadFromCache(accountId, RoleIdText);
        if (result.IsSuccess)
        {
            ApplyRoles(result);
            StatusText = $"已从本地缓存读取 {result.Roles.Count} 个角色";
        }
        else
        {
            StatusText = result.Message ?? "本地缓存不可用";
        }
    }

    private void ApplyRoles(RoleDataLoadResult result)
    {
        Roles.Clear();
        foreach (var role in result.Roles)
        {
            Roles.Add(role);
        }

        HasRoles = Roles.Count > 0;
        SourceText = result.Source switch
        {
            RoleDataSource.Kujiequ => "数据源: 库街区 (在线)",
            RoleDataSource.Local => "数据源: 本地缓存/文件",
            _ => "数据源: -",
        };

        // 重建属性筛选选项(从角色数据中提取去重属性)
        AttributeFilters.Clear();
        AttributeFilters.Add(AllAttributeFilter);
        foreach (var attr in Roles
                     .Select(r => r.AttributeName)
                     .Where(a => !string.IsNullOrWhiteSpace(a))
                     .Distinct()
                     .OrderBy(a => a, StringComparer.Ordinal))
        {
            AttributeFilters.Add(attr);
        }
        SelectedAttributeFilter = AllAttributeFilter;

        RebuildFilteredRoles();
        SelectedRole = FilteredRoles.FirstOrDefault();
    }

    partial void OnSelectedAttributeFilterChanged(string value) => RebuildFilteredRoles();

    partial void OnSelectedSortChanged(string value) => RebuildFilteredRoles();

    /// <summary>按当前属性筛选 + 排序重建 <see cref="FilteredRoles"/>。</summary>
    private void RebuildFilteredRoles()
    {
        var all = Roles;
        var source = SelectedAttributeFilter == AllAttributeFilter || string.IsNullOrWhiteSpace(SelectedAttributeFilter)
            ? all
            : all.Where(r => string.Equals(r.AttributeName, SelectedAttributeFilter, StringComparison.Ordinal));

        IEnumerable<RoleDetail> ordered = SelectedSort switch
        {
            SortByName => source.OrderBy(r => r.RoleName, StringComparer.Ordinal),
            _ => source
                .OrderByDescending(r => r.StarLevel)
                .ThenBy(r => r.RoleName, StringComparer.Ordinal),
        };

        FilteredRoles.Clear();
        foreach (var role in ordered)
        {
            FilteredRoles.Add(role);
        }

        // 若当前选中项被过滤掉,回退到第一个
        if (SelectedRole is not null && !FilteredRoles.Contains(SelectedRole))
        {
            SelectedRole = FilteredRoles.FirstOrDefault();
        }
    }
}

/// <summary>布尔 → 画刷转换(用于共鸣链解锁状态显示)。</summary>
public sealed class BoolToBrushConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly BoolToBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        var app = Avalonia.Application.Current;
        if (app is null)
        {
            return null;
        }

        bool ok = value is true;
        if (ok)
        {
            return app.TryFindResource("SemiColorPrimary", out var brush) ? brush : null;
        }
        return app.TryFindResource("SemiColorText3", out var gray) ? gray : null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>布尔 → 透明度(未解锁共鸣链置灰;参照 WutheringWavesTool chainImgVisible)。</summary>
public sealed class BoolToOpacityConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly BoolToOpacityConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is true ? 1.0 : 0.35;

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>数值 &gt; 0 判断(角色卡片仅在确有链信息时显示链数)。</summary>
public sealed class GreaterThanZeroConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly GreaterThanZeroConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is int n && n > 0;

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>星级 → 色条画刷(5★金 / 4★紫 / 其他灰;参照 WutheringWavesTool thumb)。</summary>
public sealed class StarThumbBrushConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly StarThumbBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value switch
        {
            5 => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#f8f05c")),
            4 => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#bc60f2")),
            _ => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#4a4a4a")),
        };

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>有效词条装饰条色(参照 WutheringWavesTool thumb:level3=黄 / 2,1=青 / 0=深灰;主题自适应)。</summary>
public sealed class PropLevelBrushConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly PropLevelBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        bool dark = ThemeHelper.IsDarkTheme();
        return value switch
        {
            3 => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(dark ? "#ffec16" : "#a88400")),
            2 => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(dark ? "#00dde8" : "#007a85")),
            1 => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(dark ? "#00dde8" : "#007a85")),
            _ => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(dark ? "#7a7a7a" : "#9e9e9e")),
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>有效词条文字色(level&gt;=1 高亮;无效词条灰色;主题自适应)。</summary>
public sealed class PropTextBrushConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly PropTextBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        bool dark = ThemeHelper.IsDarkTheme();
        return value is int l and > 0
            ? new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(dark ? "#ffec16" : "#a88400"))
            : new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(dark ? "#9a9a9a" : "#8a8a8a"));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>声骸评级等级 → 文字色(参照 WutheringWavesTool status:ACE红 / SSS,SS黄 / S紫 / N灰;主题自适应)。</summary>
public sealed class EchoRatingLevelBrushConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly EchoRatingLevelBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        bool dark = ThemeHelper.IsDarkTheme();
        return (value as EchoRatingLevel?) switch
        {
            EchoRatingLevel.Ace => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#e33737")),
            EchoRatingLevel.SSS or EchoRatingLevel.SS =>
                new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(dark ? "#ffec16" : "#a88400")),
            EchoRatingLevel.S => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(dark ? "#9300e8" : "#6a00a8")),
            _ => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(dark ? "#9a9a9a" : "#8a8a8a")),
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>主题明暗辅助(转换器共用)。</summary>
internal static class ThemeHelper
{
    public static bool IsDarkTheme()
    {
        var app = Avalonia.Application.Current;
        if (app?.RequestedThemeVariant == Avalonia.Styling.ThemeVariant.Dark)
        {
            return true;
        }
        if (app?.RequestedThemeVariant == Avalonia.Styling.ThemeVariant.Light)
        {
            return false;
        }
        try
        {
            return app?.PlatformSettings?.GetColorValues().ThemeVariant == Avalonia.Platform.PlatformThemeVariant.Dark;
        }
        catch (Exception)
        {
            return false;
        }
    }
}

/// <summary>声骸品质底色(参照 WutheringWavesTool icon ssr/sr/r:5=金 / 4=紫 / 其他=灰)。</summary>
public sealed class PhantomQualityBrushConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly PhantomQualityBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value switch
        {
            5 => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#8a6d1f")),
            4 => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#5b4a8a")),
            _ => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#4a4a4a")),
        };

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>布尔 → 达成/未达成文字色(达标绿色,未达标灰/红)。</summary>
public sealed class BoolToOkBrushConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly BoolToOkBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is true
            ? new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#4caf50"))
            : new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#ff7043"));

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>可空布尔 → 达成文本(true=已达标 / false=未达标 / null=未知)。</summary>
public sealed class NullableBoolToTextConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly NullableBoolToTextConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value switch
        {
            true => "已达标",
            false => "未达标",
            _ => "未知",
        };

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// 主题自适应强调色转换器(参照 WutheringWavesTool GetForegroundColor 的亮度→前景色逻辑)。
/// <para>深色主题(背景暗)返回亮色;浅色主题(背景亮)返回深色,保证对比度。
/// ConverterParameter 可选:缺省=黄调("emphasis"),"cyan"=青调。</para>
/// </summary>
public sealed class ThemeAdaptiveEmphasisBrushConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly ThemeAdaptiveEmphasisBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        bool isDark = ThemeHelper.IsDarkTheme();
        bool cyan = parameter is string p && p.Equals("cyan", StringComparison.OrdinalIgnoreCase);
        return new Avalonia.Media.SolidColorBrush(cyan
            ? (isDark
                ? Avalonia.Media.Color.Parse("#00dde8")  // 深色主题:亮青
                : Avalonia.Media.Color.Parse("#007a85")) // 浅色主题:深青
            : (isDark
                ? Avalonia.Media.Color.Parse("#f8f05c")  // 深色主题:亮黄
                : Avalonia.Media.Color.Parse("#8a6d1f")) // 浅色主题:深琥珀(保黄调+可读)
        );
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>武器星级 → 背景色(5★金 / 4★紫 / 其他灰;参照 WutheringWavesTool weaponBg)。</summary>
public sealed class WeaponStarBrushConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly WeaponStarBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value switch
        {
            5 => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#8a6d1f")),
            4 => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#5b4a8a")),
            _ => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#4a4a4a")),
        };

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>字符串非空判断(用于图标等有值才显示)。</summary>
public sealed class StringNotEmptyConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly StringNotEmptyConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is string s && !string.IsNullOrWhiteSpace(s);

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}