using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FluentIcons.Common;
using McKuro.Core.Models.User;
using McKuro.Core.Services.Game;
using McKuro.Services;

namespace McKuro.ViewModels;

/// <summary>首页每日数据项(体力/结晶单质/活跃度/周本/终焉矩阵/冥歌海墟/千道门扉/周度游历/战令)。</summary>
public sealed class DailyItem
{
    public required Icon Icon { get; init; }
    /// <summary>图标来源:官方图标 URL(库街区 getData 每项 img)或本地游戏图标路径(Assets/waves/*.png);非空时显示图片。</summary>
    public string? ImageUrl { get; init; }
    public required string Name { get; init; }
    public required string ValueText { get; init; }   // 例如 "120/160" 或仅 "100"
    /// <summary>次要说明(可选,如电台 "经验: 3250/12000")。</summary>
    public string? SubText { get; init; }
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

    /// <summary>默认头像(对齐 Haiyu GameRoilDataWrapper:库街区未绑定头像时的官方默认图)。</summary>
    private const string DefaultAvatarUrl = "https://mc.kurogames.com/cloud/assets/avatar-cb06ab22.png";

    /// <summary>角色名(游戏内昵称)。</summary>
    [ObservableProperty]
    private string _roleNameText = "";

    /// <summary>角色等级文本,如 "LV.80";未知时为空。</summary>
    [ObservableProperty]
    private string _levelText = "";

    /// <summary>已游玩文本,如 "已游玩 818 天";未知时为空。</summary>
    [ObservableProperty]
    private string _playDaysText = "";

    /// <summary>头像 URL(空时用默认头像资源)。</summary>
    [ObservableProperty]
    private string _avatarUrl = "";

    /// <summary>是否为开服玩家(2024-05-23 开服及后 10 天内注册)。</summary>
    [ObservableProperty]
    private bool _isLaunchPlayer;

    /// <summary>注册时间文本(如 "注册于 2024-05-23"),用作开服玩家徽章提示。</summary>
    [ObservableProperty]
    private string _registerText = "";

    /// <summary>是否已有角色资料(控制首页资料卡显示)。</summary>
    [ObservableProperty]
    private bool _hasProfile;

    /// <summary>本地官方图标路径(参照 Haiyu Assets/GameAssets/Waves:波片/结晶单质/活跃度/战令)。</summary>
    private static string GameIcon(string fileName)
        => Path.Combine(AppContext.BaseDirectory, "Assets", "waves", fileName);

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
                ClearProfile();
                DailyItems.Clear();
                StatusText = "本地无游戏缓存,请登录库街区账号显示每日数据";
                return;
            }
            var data = await AppServices.DailyData.GetDailyDataAsync();
            if (data is null)
            {
                ClearProfile();
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

    /// <summary>清空角色资料(刷新失败/无数据时,避免展示过期账号)。</summary>
    private void ClearProfile()
    {
        HasProfile = false;
        RoleNameText = "";
        LevelText = "";
        PlayDaysText = "";
        AvatarUrl = "";
        IsLaunchPlayer = false;
        RegisterText = "";
    }

    /// <summary>应用全量每日数据(缺字段的项自动跳过)。</summary>
    private void ApplyDailyData(RoleDailyData data, string source)
    {
        DailyItems.Clear();
        ApplyProfile(data);
        AddItem(data.EnergyData, Icon.Flash, "体力", iconFile: "waveplates.png");
        AddItem(data.StoreEnergyData, Icon.Diamond, "结晶单质", iconFile: "wavesubstance.png");
        AddItem(data.LivenessData, Icon.Fire, "活跃度", curOnly: true, iconFile: "activity.png");
        AddItem(data.WeeklyData, Icon.Trophy, "周本", curOnly: true, iconFile: "weeklyInst.png", forcedUrl: data.WeeklyIconUrl);
        AddItem(data.NewTowerData, Icon.BuildingSkyscraper, "终焉矩阵");
        AddItem(data.SlashTowerData, Icon.Beach, "冥歌海墟");
        AddItem(data.RougeData, Icon.Door, "千道门扉", curOnly: true);
        AddItem(data.WeeklyFrameData, Icon.Map, "周度游历", curOnly: true);
        AddBattlePass(data.BattlePassData);
        StatusText = $"已更新({source}) · {data.RoleName ?? ""}({data.RoleId ?? ""})";
    }

    /// <summary>填充资料卡:昵称/等级/游玩天数/头像/开服玩家徽章(参照 Java WutheringWavesTool 角色卡)。</summary>
    private void ApplyProfile(RoleDailyData data)
    {
        RoleNameText = string.IsNullOrWhiteSpace(data.RoleName) ? "" : data.RoleName!;
        LevelText = data.Level > 0 ? $"LV.{data.Level}" : "";
        PlayDaysText = data.ActiveDays > 0 ? $"已游玩 {data.ActiveDays} 天" : "";
        AvatarUrl = string.IsNullOrWhiteSpace(data.HeadUrl) ? DefaultAvatarUrl : data.HeadUrl!;
        RegisterText = data.CreatTime > 0
            ? $"注册于 {DateTimeOffset.FromUnixTimeMilliseconds(data.CreatTime).LocalDateTime:yyyy-MM-dd}"
            : "";
        IsLaunchPlayer = UserProfile.IsLaunchPlayer(data.CreatTime);
        HasProfile = !string.IsNullOrWhiteSpace(data.RoleName) || data.Level > 0;
    }

    /// <summary>电台(战令):第 1 个元素 cur=等级,第 2 个 cur/total=经验进度(参考截图 "电台 LV.03 经验: 3250/12000")。</summary>
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
            Icon = Icon.Medal,
            ImageUrl = GameIcon("podcast.png"),
            Name = "电台",
            ValueText = $"LV.{level:00}",
            SubText = progress is null ? null : $"经验: {progress.Cur}/{progress.Total}",
            Cur = progress?.Cur ?? 0,
            Total = progress?.Total ?? 0,
        });
    }

    private void AddItem(RoleDailyDetail? detail, Icon icon, string fallbackName, bool curOnly = false,
        string? iconFile = null, string? forcedUrl = null)
    {
        if (detail is null)
        {
            return;
        }
        DailyItems.Add(new DailyItem
        {
            Icon = icon,
            // 优先级:强制图标(数据中心周本图标)→ 本地官方图标 → 库街区 img 字段
            ImageUrl = !string.IsNullOrWhiteSpace(forcedUrl)
                ? forcedUrl
                : iconFile is not null
                    ? GameIcon(iconFile)
                    : string.IsNullOrWhiteSpace(detail.Img) ? null : detail.Img,
            Name = string.IsNullOrWhiteSpace(detail.Name) ? fallbackName : detail.Name!,
            ValueText = curOnly ? $"{detail.Cur}" : $"{detail.Cur}/{detail.Total}",
            Cur = detail.Cur,
            Total = curOnly ? 0 : detail.Total,
        });
    }
}
