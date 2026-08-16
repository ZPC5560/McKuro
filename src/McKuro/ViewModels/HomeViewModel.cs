using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using McKuro.Core.Models.User;
using McKuro.Core.Services.Game;
using McKuro.Services;

namespace McKuro.ViewModels;

/// <summary>首页每日数据项(体力/结晶单质/活跃度/周本/终焉矩阵/冥歌海墟/千道门扉/周度游历/战令)。</summary>
public sealed class DailyItem
{
    public required string Icon { get; init; }
    /// <summary>官方图标 URL(库街区 getData 每项 img;非空时优先显示)。</summary>
    public string? ImageUrl { get; init; }
    public required string Name { get; init; }
    public required string ValueText { get; init; }   // 例如 "120/160" 或仅 "100"
    public required int Cur { get; init; }
    public required int Total { get; init; }
    /// <summary>是否有总量(有 total 才显示进度条)。</summary>
    public bool HasTotal => Total > 0;
    /// <summary>进度 0-100。</summary>
    public double Percent => Total > 0 ? Math.Clamp(Cur * 100.0 / Total, 0, 100) : 0;
    public string PercentText => $"{Percent:0}%";
}

/// <summary>主页:InternalBeyond 风格欢迎页 + 角色每日数据(全量字段)。</summary>
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

    /// <summary>欢迎页淡入动画(0→1,配合 Avalonia Transitions)。</summary>
    [ObservableProperty]
    private double _revealOpacity;

    /// <summary>每日数据项(体力/结晶单质/活跃度/周本/终焉矩阵/冥歌海墟/千道门扉/周度游历/战令)。</summary>
    public ObservableCollection<DailyItem> DailyItems { get; } = [];

    public HomeViewModel()
    {
        RefreshState();
        _ = RefreshDailyAsync();
        _ = RevealAsync();
    }

    private async Task RevealAsync()
    {
        await Task.Delay(120);
        RevealOpacity = 1;
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

    /// <summary>导航到启动器页。</summary>
    [RelayCommand]
    private void GoLauncher() => SendNav(NavigationKeys.Launcher);

    /// <summary>导航到抽卡分析页。</summary>
    [RelayCommand]
    private void GoGacha() => SendNav(NavigationKeys.Gacha);

    /// <summary>导航到角色数据页。</summary>
    [RelayCommand]
    private void GoRoles() => SendNav(NavigationKeys.Roles);

    private static void SendNav(string key)
        => WeakReferenceMessenger.Default.Send(new NavigationRequestedMessage(key));

    /// <summary>拉取角色每日数据(优先本地游戏缓存 + PC 启动器 SDK,失败回退库街区接口)。</summary>
    [RelayCommand]
    private async Task RefreshDailyAsync()
    {
        if (IsBusy)
        {
            return;
        }
        RefreshState();
        IsBusy = true;
        StatusText = "正在拉取每日数据…";
        try
        {
            // ① 优先本地游戏缓存 + PC 启动器 SDK(不依赖库街区登录)
            var local = await AppServices.LocalDaily.GetDailyDataAsync();
            if (local is not null)
            {
                ApplyDailyData(local, "本地启动器");
                return;
            }

            // ② 回退库街区接口(需登录)
            if (!IsLoggedIn)
            {
                DailyItems.Clear();
                StatusText = "本地无游戏缓存,请登录库街区账号显示每日数据";
                return;
            }
            var data = await AppServices.DailyData.GetDailyDataAsync();
            if (data is null)
            {
                DailyItems.Clear();
                StatusText = "拉取每日数据失败(未登录或接口异常)";
                return;
            }
            ApplyDailyData(data, "库街区");
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

    /// <summary>应用全量每日数据(缺字段的项自动跳过)。</summary>
    private void ApplyDailyData(RoleDailyData data, string source)
    {
        DailyItems.Clear();
        AddItem(data.EnergyData, "⚡", "体力");
        AddItem(data.StoreEnergyData, "💎", "结晶单质");
        AddItem(data.LivenessData, "🔥", "活跃度", curOnly: true);
        AddItem(data.WeeklyData, "🗡", "周本", curOnly: true);
        AddItem(data.NewTowerData, "🏯", "终焉矩阵");
        AddItem(data.SlashTowerData, "🌊", "冥歌海墟");
        AddItem(data.RougeData, "📻", "千道门扉", curOnly: true);
        AddItem(data.WeeklyFrameData, "🗺", "周度游历", curOnly: true);
        AddBattlePass(data.BattlePassData);
        StatusText = $"已更新({source}) · {data.RoleName ?? ""}({data.RoleId ?? ""})";
    }

    /// <summary>战令:第 1 个元素 cur=等级,第 2 个 cur/total=进度(进度条)。</summary>
    private void AddBattlePass(List<RoleDailyDetail>? battlePass)
    {
        if (battlePass is null || battlePass.Count == 0)
        {
            return;
        }
        var level = battlePass[0].Cur;
        var progress = battlePass.Count > 1 ? battlePass[1] : null;
        DailyItems.Add(new DailyItem
        {
            Icon = "🎖",
            Name = "战令",
            ValueText = $"LV.{level}",
            Cur = progress?.Cur ?? 0,
            Total = progress?.Total ?? 0,
        });
    }

    private void AddItem(RoleDailyDetail? detail, string icon, string fallbackName, bool curOnly = false)
    {
        if (detail is null)
        {
            return;
        }
        DailyItems.Add(new DailyItem
        {
            Icon = icon,
            ImageUrl = string.IsNullOrWhiteSpace(detail.Img) ? null : detail.Img,
            Name = string.IsNullOrWhiteSpace(detail.Name) ? fallbackName : detail.Name!,
            ValueText = curOnly ? $"{detail.Cur}" : $"{detail.Cur}/{detail.Total}",
            Cur = detail.Cur,
            Total = curOnly ? 0 : detail.Total,
        });
    }
}
