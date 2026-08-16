using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using McKuro.Core.Models.Redeem;
using McKuro.Services;

namespace McKuro.ViewModels;

/// <summary>兑换码页:远程拉取鸣潮兑换码清单,一键复制(参照 WutheringWavesTool RedemptionCodeView)。</summary>
public sealed partial class RedemptionCodeViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _hasData;

    /// <summary>国服兑换码(有效在前)。</summary>
    public ObservableCollection<RedemptionCodeItem> MainlandCodes { get; } = [];

    /// <summary>国际服兑换码(有效在前)。</summary>
    public ObservableCollection<RedemptionCodeItem> GlobalCodes { get; } = [];

    [ObservableProperty]
    private RedemptionCodeItem? _selectedMainland;

    [ObservableProperty]
    private RedemptionCodeItem? _selectedGlobal;

    public RedemptionCodeViewModel()
    {
        _ = LoadAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (IsLoading)
        {
            return;
        }
        IsLoading = true;
        StatusText = "正在拉取兑换码…";
        try
        {
            var data = await AppServices.RedeemCodes.FetchAsync();
            MainlandCodes.Clear();
            GlobalCodes.Clear();
            if (data is not null)
            {
                AddSorted(MainlandCodes, data.Mainland);
                AddSorted(GlobalCodes, data.Global);
                HasData = MainlandCodes.Count > 0 || GlobalCodes.Count > 0;
                StatusText = $"共 {MainlandCodes.Count + GlobalCodes.Count} 条(国服 {MainlandCodes.Count} / 国际服 {GlobalCodes.Count})";
            }
            else
            {
                StatusText = "拉取失败(网络异常或服务不可用)";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"拉取失败: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static void AddSorted(ObservableCollection<RedemptionCodeItem> target, List<RedemptionCodeItem>? items)
    {
        if (items is null)
        {
            return;
        }
        // 有效在前,再按到期时间倒序(最新兑换码在前;无法解析的时间排最后)
        foreach (var item in items
            .OrderByDescending(i => i.Valid)
            .ThenByDescending(i => ParseTime(i.EndTime))
            .ThenBy(i => i.Key, StringComparer.Ordinal))
        {
            target.Add(item);
        }
    }

    /// <summary>解析时间字符串(yyyy-MM-dd HH:mm:ss)用于排序;失败返回最小值。</summary>
    private static DateTime ParseTime(string? s)
        => DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var dt) ? dt : DateTime.MinValue;

    /// <summary>复制兑换码到剪贴板(国服)。</summary>
    [RelayCommand]
    private void CopyMainland()
    {
        if (SelectedMainland?.Key is { Length: > 0 } key)
        {
            CopyToClipboard(key);
        }
    }

    /// <summary>复制兑换码到剪贴板(国际服)。</summary>
    [RelayCommand]
    private void CopyGlobal()
    {
        if (SelectedGlobal?.Key is { Length: > 0 } key)
        {
            CopyToClipboard(key);
        }
    }

    private static void CopyToClipboard(string text)
    {
        try
        {
            var top = Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
            var clip = top?.MainWindow?.Clipboard;
            Dispatcher.UIThread.Post(() =>
            {
                if (clip is not null)
                {
                    _ = Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(clip, text);
                }
            });
        }
        catch (Exception)
        {
            // 剪贴板失败静默
        }
    }
}

/// <summary>兑换码有效性 → 行透明度(有效=1,无效=0.45)。</summary>
public sealed class ValidOpacityConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly ValidOpacityConverter Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is true ? 1.0 : 0.45;
    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>兑换码有效性 → 标签背景色(有效=绿,无效=灰)。</summary>
public sealed class ValidBrushConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly ValidBrushConverter Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is true
            ? new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#4caf50"))
            : new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#757575"));
    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>兑换码有效性 → 标签文本(有效/已过期)。</summary>
public sealed class ValidTextConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly ValidTextConverter Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is true ? "有效" : "已过期";
    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}
