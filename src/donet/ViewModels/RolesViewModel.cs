using System.Collections.ObjectModel;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using donet.Core.Models.Roles;
using donet.Core.Services.Roles;
using donet.Services;

namespace donet.ViewModels;

/// <summary>角色养成页:原生显示当前账号的角色养成数据。</summary>
public sealed partial class RolesViewModel : ViewModelBase
{
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

    public ObservableCollection<RoleDetail> Roles { get; } = [];

    public RolesViewModel()
    {
        TokenText = AppServices.Settings.Current.KujiequToken;
        RoleIdText = AppServices.Settings.Current.RoleId;
        TryLoadCache();
    }

    private void TryLoadCache()
    {
        if (string.IsNullOrEmpty(RoleIdText))
        {
            return;
        }

        var cached = AppServices.Roles.LoadFromCache(RoleIdText);
        if (cached.IsSuccess && cached.Roles.Count > 0)
        {
            ApplyRoles(cached);
            StatusText = $"已加载本地缓存 (角色数: {cached.Roles.Count})";
        }
    }

    partial void OnTokenTextChanged(string value) => AppServices.Settings.Current.KujiequToken = value;

    partial void OnRoleIdTextChanged(string value) => AppServices.Settings.Current.RoleId = value;

    [RelayCommand]
    private async Task LoadFromKujiequAsync()
    {
        if (IsBusy)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(TokenText))
        {
            StatusText = "请先在设置中填入库街区 Token";
            return;
        }
        if (string.IsNullOrWhiteSpace(RoleIdText))
        {
            StatusText = "请填写角色 ID";
            return;
        }

        IsBusy = true;
        StatusText = "正在从库街区获取角色数据…";
        try
        {
            var result = await AppServices.Roles.LoadFromKujiequAsync(TokenText, RoleIdText);
            ApplyRoles(result);
            StatusText = result.IsSuccess
                ? $"库街区同步成功: {result.Roles.Count} 个角色"
                : result.Message ?? "同步失败";
        }
        catch (Exception ex)
        {
            StatusText = $"获取失败: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void LoadFromLocal()
    {
        var result = AppServices.Roles.LoadFromLocal();
        ApplyRoles(result);
        StatusText = result.IsSuccess
            ? $"已从本地数据读取 {result.Roles.Count} 个角色"
            : result.Message ?? "本地未找到角色数据";
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
        SelectedRole = Roles.FirstOrDefault();
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
