using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using McKuro.Core.Models.User;
using McKuro.Core.Services.Game;
using McKuro.Services;

namespace McKuro.ViewModels;

/// <summary>首页每日数据项(体力/活跃度/周本/电台/周度游历)。</summary>
public sealed class DailyItem
{
    public required string Icon { get; init; }
    public required string Name { get; init; }
    public required string ValueText { get; init; }   // 例如 "120/160"
    public required int Cur { get; init; }
    public required int Total { get; init; }
    /// <summary>进度 0-100。</summary>
    public double Percent => Total > 0 ? Math.Clamp(Cur * 100.0 / Total, 0, 100) : 0;
    public string PercentText => $"{Percent:0}%";
}

/// <summary>主页:欢迎横幅 + 角色每日数据(体力/活跃度/周本/电台/周度游历)。</summary>
public sealed partial class HomeViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private bool _isInstalled;

    [ObservableProperty]
    private string _installStateText = "未检测";

    [ObservableProperty]
    private string _serverTypeText = "-";

    [ObservableProperty]
    private bool _isLoggedIn;

    [ObservableProperty]
    private string _accountText = "未登录";

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>每日数据项(体力/活跃度/周本/电台/周度游历)。</summary>
    public ObservableCollection<DailyItem> DailyItems { get; } = [];

    public HomeViewModel()
    {
        RefreshState();
        _ = RefreshDailyAsync();
    }

    private void RefreshState()
    {
        IsInstalled = AppServices.Paths.IsGameInstalled;
        InstallStateText = IsInstalled ? "游戏已就绪" : "尚未安装游戏";
        ServerTypeText = AppServices.Paths.DetectServerType() switch
        {
            GameServerType.Official => "官服",
            GameServerType.Bilibili => "B站",
            GameServerType.WeGame => "WeGame",
            GameServerType.Global => "国际服",
            _ => "自动检测",
        };
        var account = AppServices.KuroAccounts.Current;
        IsLoggedIn = account is not null;
        // 显示账号名称(昵称);未登录显示"未登录",名称缺失时用友好占位而非账号 ID
        AccountText = account is null
            ? "未登录"
            : (!string.IsNullOrWhiteSpace(account.Nickname) ? account.Nickname : "已登录账号");
    }

    [RelayCommand]
    private void Refresh() => RefreshState();

    /// <summary>拉取角色每日数据(需登录)。</summary>
    [RelayCommand]
    private async Task RefreshDailyAsync()
    {
        if (IsBusy)
        {
            return;
        }
        RefreshState();
        if (!IsLoggedIn)
        {
            DailyItems.Clear();
            StatusText = "登录库街区账号后显示每日数据";
            return;
        }
        IsBusy = true;
        StatusText = "正在拉取每日数据…";
        try
        {
            var data = await AppServices.DailyData.GetDailyDataAsync();
            DailyItems.Clear();
            if (data is null)
            {
                StatusText = "拉取每日数据失败(未登录或接口异常)";
                return;
            }
            AddItem(data.EnergyData, "⚡", "体力");
            AddItem(data.LivenessData, "🔥", "活跃度");
            AddItem(data.WeeklyData, "🗡", "周本");
            AddItem(data.RougeData, "📻", "电台");
            AddItem(data.WeeklyFrameData, "🗺", "周度游历");
            StatusText = $"已更新 · {data.RoleName ?? ""}({data.RoleId ?? ""})";
        }
        catch (Exception ex)
        {
            StatusText = $"拉取失败: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void AddItem(RoleDailyDetail? detail, string icon, string fallbackName)
    {
        if (detail is null)
        {
            return;
        }
        DailyItems.Add(new DailyItem
        {
            Icon = icon,
            Name = string.IsNullOrWhiteSpace(detail.Name) ? fallbackName : detail.Name!,
            ValueText = $"{detail.Cur}/{detail.Total}",
            Cur = detail.Cur,
            Total = detail.Total,
        });
    }
}
