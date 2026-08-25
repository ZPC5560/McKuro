using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using McKuro.Core.Models.Kuro;
using McKuro.Core.Services.Kuro;
using McKuro.Services;

namespace McKuro.ViewModels;

/// <summary>签到页角色条目。</summary>
public sealed partial class RoleSignItem : ObservableObject
{
    public required string GameName { get; init; }
    public required string RoleName { get; init; }
    public required string ServerName { get; init; }
    public string? Level { get; init; }

    [ObservableProperty]
    private string _signStatus = "待签到";

    public GameRoilDataItem Source { get; init; } = null!;
}

/// <summary>
/// 签到页:游戏签到 + 库街区每日任务(登录已统一迁移到「账号」页)。
/// 参考 Haiyu 的 GamerSignPage / AutoKuroClientSignService。
/// </summary>
public sealed partial class SignViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isLoggedIn;

    [ObservableProperty]
    private string _accountText = "未登录";

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _autoSignEnabled;

    [ObservableProperty]
    private bool _autoKuroClientTaskEnabled;

    public ObservableCollection<RoleSignItem> Roles { get; } = [];

    /// <summary>签到奖励格子(主角色的签到奖励配置,参照 WutheringWavesTool goodsView)。</summary>
    public ObservableCollection<McKuro.Core.Models.Kuro.SignInGoodsItem> SignGoods { get; } = [];

    /// <summary>今日签到状态文案(已签到/未签到)。</summary>
    [ObservableProperty]
    private string _todaySignText = "";

    /// <summary>累计签到天数文案。</summary>
    [ObservableProperty]
    private string _signCountText = "";

    /// <summary>签到历史统计(按物品聚合,星声置顶,参照 WutheringWavesTool signHistoryListView)。</summary>
    public ObservableCollection<McKuro.Core.Models.Kuro.SignRecordItem> SignHistory { get; } = [];

    public SignViewModel()
    {
        _autoSignEnabled = AppServices.Settings.Current.AutoSignEnabled;
        _autoKuroClientTaskEnabled = AppServices.Settings.Current.AutoKuroClientTaskEnabled;
        RefreshAccount();
    }

    partial void OnAutoSignEnabledChanged(bool value)
    {
        AppServices.Settings.Current.AutoSignEnabled = value;
        AppServices.Settings.Save();
    }

    partial void OnAutoKuroClientTaskEnabledChanged(bool value)
    {
        AppServices.Settings.Current.AutoKuroClientTaskEnabled = value;
        AppServices.Settings.Save();
    }

    private void RefreshAccount()
    {
        var account = AppServices.KuroAccounts.Current;
        IsLoggedIn = account is not null;
        AccountText = account is null
            ? "未登录"
            : $"{(string.IsNullOrEmpty(account.Nickname) ? "库街区用户" : account.Nickname)} (ID: {account.UserId})";
        if (account is not null)
        {
            // 异步校验 token 有效性:若在其他设备登录导致失效,自动登出并提示
            _ = ValidateAndRefreshAsync(account);
        }
    }

    /// <summary>校验当前账号 token 是否仍有效;失效则自动登出(账号在其他设备登录),有效则刷新角色列表。</summary>
    private async Task ValidateAndRefreshAsync(KuroAccount account)
    {
        try
        {
            var ok = await AppServices.Kuro.IsLoginAsync(account);
            if (!ok)
            {
                // token 已失效(可能在别处登录被顶掉):清除本地登录态
                AppServices.KuroAccounts.Remove(account.UserId);
                IsLoggedIn = false;
                AccountText = "未登录";
                StatusText = "登录已失效(账号可能已在其他设备登录),请重新登录";
                WeakReferenceMessenger.Default.Send(new RolesRefreshRequestedMessage(account.UserId));
                return;
            }
            await RefreshRolesAsync();
        }
        catch (Exception)
        {
            // 网络异常:不登出,尝试刷新(失败由 RefreshRolesAsync 提示)
            await RefreshRolesAsync();
        }
    }

    /// <summary>跳转「账号」页登录(登录入口已统一迁移到账号页)。</summary>
    [RelayCommand]
    private void GoAccount()
        => WeakReferenceMessenger.Default.Send(new NavigationRequestedMessage(NavigationKeys.Account));

    /// <summary>刷新角色列表(鸣潮)。</summary>
    [RelayCommand]
    private async Task RefreshRolesAsync()
    {
        var account = AppServices.KuroAccounts.Current;
        if (account is null)
        {
            return;
        }

        IsBusy = true;
        StatusText = "正在获取角色列表…";
        try
        {
            Roles.Clear();
            var roles = await AppServices.Kuro.GetGamerAsync(account, (int)KuroGameType.Waves);
            if (roles is not { Code: 200 })
            {
                // token 失效(如账号在其他设备登录)或接口异常:提示重新登录并登出
                StatusText = roles is null
                    ? "获取角色列表失败(网络异常),请重试"
                    : $"登录已失效或获取失败: {roles.Msg ?? $"code={roles.Code}"}";
                if (roles is not null && roles.Code != 200)
                {
                    AppServices.KuroAccounts.Remove(account.UserId);
                    IsLoggedIn = false;
                    AccountText = "未登录";
                    StatusText += " → 请重新登录";
                    WeakReferenceMessenger.Default.Send(new RolesRefreshRequestedMessage(account.UserId));
                }
                return;
            }
            if (roles.Data is not null)
            {
                foreach (var role in roles.Data)
                {
                    Roles.Add(new RoleSignItem
                    {
                        GameName = "鸣潮",
                        RoleName = role.RoleName ?? "未知角色",
                        ServerName = role.ServerName ?? "",
                        Level = role.GameLevel,
                        Source = role,
                    });
                }

                // 异步查询各角色当日签到状态(已签到→更新状态;失败保持"待签到")
                _ = RefreshSignStatusAsync(account);

                // 自动同步角色 ID 到设置:角色数据页自动加载依赖 RoleId(取第一个有效角色)
                var firstRoleId = roles.Data.FirstOrDefault(r => !string.IsNullOrEmpty(r.RoleId))?.RoleId;
                if (!string.IsNullOrEmpty(firstRoleId))
                {
                    var settings = AppServices.Settings.Current;
                    if (settings.RoleId != firstRoleId)
                    {
                        settings.RoleId = firstRoleId!;
                        AppServices.Settings.Save();
                    }
                }
                // 通知角色数据页自动同步(登录态下;手动刷新同样触发)
                WeakReferenceMessenger.Default.Send(new RolesRefreshRequestedMessage(account.UserId));
            }
            StatusText = Roles.Count > 0
                ? $"共 {Roles.Count} 个角色"
                : "未找到游戏角色(可在设置页确认已绑定游戏)";
        }
        catch (Exception ex)
        {
            StatusText = $"获取角色失败: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>并行查询各角色当日签到状态,更新列表中的 SignStatus(已签到/待签到)。</summary>
    private async Task RefreshSignStatusAsync(KuroAccount account)
    {
        var items = Roles.ToList();
        if (items.Count == 0)
        {
            return;
        }
        try
        {
            await Task.WhenAll(items.Select(async item =>
            {
                try
                {
                    var info = await AppServices.Kuro.GetSignInDataAsync(account, item.Source);
                    bool signed = info is { Code: 200, Data.IsSigIn: true };
                    bool isFirst = ReferenceEquals(item, items[0]);
                    // 回到 UI 线程更新(角色可能已被重新加载,按 Source 定位)
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        var target = Roles.FirstOrDefault(r => ReferenceEquals(r.Source, item.Source));
                        if (target is not null)
                        {
                            target.SignStatus = signed ? "已签到" : "待签到";
                        }
                        // 主角色(列表第一项)同时填充签到奖励格子与今日状态
                        if (isFirst && info?.Data is { } data)
                        {
                            SignGoods.Clear();
                            var sigInNum = data.SigInNum;
                            if (data.SignInGoodsConfigs is not null)
                            {
                                // 已签判断:列表索引 < 累计天数(对齐 WutheringWavesTool signGoods[i].setSign(i < sigInNum))
                                var ordered = data.SignInGoodsConfigs.OrderBy(x => x.SerialNum).ToList();
                                for (int i = 0; i < ordered.Count; i++)
                                {
                                    ordered[i].IsSigned = i < sigInNum;
                                    SignGoods.Add(ordered[i]);
                                }
                            }
                            TodaySignText = signed ? "今日已签到" : "今日尚未签到";
                            SignCountText = $"累计签到 {sigInNum} 天";
                        }
                    });
                    // 主角色额外拉取签到历史(后台网络调用,聚合后回 UI 线程更新)
                    if (isFirst)
                    {
                        try
                        {
                            var record = await AppServices.Kuro.GetSignRecordAsync(account, item.Source);
                            var aggregated = record is { Code: 200, Data: not null }
                                ? AggregateHistory(record.Data)
                                : null;
                            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                SignHistory.Clear();
                                if (aggregated is not null)
                                {
                                    foreach (var r in aggregated)
                                    {
                                        SignHistory.Add(r);
                                    }
                                }
                            });
                        }
                        catch (Exception)
                        {
                            // 历史拉取失败不影响主流程
                        }
                    }
                }
                catch (Exception)
                {
                    // 单角色查询失败:保持"待签到"
                }
            })).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // 整体失败不影响
        }
    }

    /// <summary>聚合签到历史:按物品名累加数量,「星声」恒第一,其余按 type 降序(对齐 WutheringWavesTool)。</summary>
    private static List<McKuro.Core.Models.Kuro.SignRecordItem> AggregateHistory(
        IList<McKuro.Core.Models.Kuro.SignRecordItem> items)
    {
        var grouped = items
            .GroupBy(x => x.GoodsName ?? "")
            .Select(g => new McKuro.Core.Models.Kuro.SignRecordItem
            {
                GoodsName = g.Key,
                GoodsNum = g.Sum(x => x.GoodsNum),
                GoodsUrl = g.FirstOrDefault()?.GoodsUrl,
                Type = g.FirstOrDefault()?.Type ?? 0,
            })
            .OrderByDescending(x => x.GoodsName == "星声")
            .ThenByDescending(x => x.Type)
            .ToList();
        return grouped;
    }

    /// <summary>对所有角色执行游戏签到。</summary>
    [RelayCommand]
    private async Task SignAllAsync()
    {
        var account = AppServices.KuroAccounts.Current;
        if (account is null)
        {
            StatusText = "请先登录库街区账号";
            return;
        }

        IsBusy = true;
        StatusText = "正在执行游戏签到…";
        try
        {
            var summary = await AppServices.KuroSign.SignAllGamesAsync(account);
            StatusText = summary.Message;
            await RefreshRolesAsync();
            StatusText = summary.Message;
        }
        catch (Exception ex)
        {
            StatusText = $"签到失败: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>执行库街区每日任务(签到+浏览+点赞+分享)。</summary>
    [RelayCommand]
    private async Task ExecuteDailyAsync()
    {
        var account = AppServices.KuroAccounts.Current;
        if (account is null)
        {
            StatusText = "请先登录库街区账号";
            return;
        }

        IsBusy = true;
        StatusText = "正在执行库街区每日任务…";
        try
        {
            var ok = await AppServices.KuroSign.ExecuteDailyTasksAsync(account);
            StatusText = ok ? "库街区每日任务完成" : "每日任务执行失败(请查看网络或稍后重试)";
        }
        catch (Exception ex)
        {
            StatusText = $"每日任务失败: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}

/// <summary>已签到 → 透明度(已签=0.75,未签=1)。</summary>
public sealed class SignedOpacityConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly SignedOpacityConverter Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is true ? 0.75 : 1.0;
    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>签到格子序号:serialNum(0 起) + 1 补零显示(对齐 WutheringWavesTool serialNum+1 %02d)。</summary>
public sealed class SerialNumPlusOneConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly SerialNumPlusOneConverter Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is int n ? (n + 1).ToString("D2") : "00";
    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}