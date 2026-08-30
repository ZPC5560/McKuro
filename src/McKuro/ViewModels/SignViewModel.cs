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
    /// <summary>所属库街区账号标识(多账号时区分同名角色;单账号时为空不显示)。</summary>
    public string AccountLabel { get; init; } = "";

    /// <summary>角色头像(优先本地磁盘缓存路径,后台落盘完成后切换)。</summary>
    [ObservableProperty]
    private string _headUrl = "";

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

        // 账号页登录/切号/移除后自动刷新本页(与 RolesViewModel 同一消息源)
        WeakReferenceMessenger.Default.Register<SignViewModel, RolesRefreshRequestedMessage>(this, static (recipient, _) =>
        {
            recipient.RefreshAccount();
        });
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
        var accounts = AppServices.KuroAccounts.GetAccounts();
        IsLoggedIn = accounts.Count > 0;
        AccountText = accounts.Count switch
        {
            0 => "未登录",
            1 => DescribeAccount(accounts[0]),
            _ => $"{accounts.Count} 个库街区账号",
        };
        if (accounts.Count > 0)
        {
            // 异步校验各账号 token 并拉取全部角色(失效账号自动移除并提示)
            _ = RefreshAllAccountsAsync(accounts);
        }
    }

    private static string DescribeAccount(KuroAccount account) =>
        $"{(string.IsNullOrEmpty(account.Nickname) ? "库街区用户" : account.Nickname)} (ID: {account.UserId})";

    /// <summary>遍历全部已保存账号:拉取角色、失效账号自动移除、汇总签到状态。</summary>
    private async Task RefreshAllAccountsAsync(IReadOnlyList<KuroAccount> accounts)
    {
        // 重入保护:消息与按钮可能同时触发,两次并发刷新会交错 Clear/Add 产生重复条目
        if (_refreshingRoles)
        {
            return;
        }
        _refreshingRoles = true;
        IsBusy = true;
        Roles.Clear();
        var removed = new List<string>();
        var seenRoleIds = new HashSet<string>();
        try
        {
            foreach (var account in accounts)
            {
                // 单账号标签:不显示(底部行已有账号语义);多账号:用昵称/ID 区分同名角色
                var label = accounts.Count > 1
                    ? (string.IsNullOrEmpty(account.Nickname) ? $"ID {account.UserId}" : account.Nickname)
                    : "";
                try
                {
                    var resp = await AppServices.Kuro.GetGamerAsync(account, (int)KuroGameType.Waves);
                    if (resp is { Code: 200 } && resp.Data is not null)
                    {
                        foreach (var role in resp.Data)
                        {
                            // 去重兜底:同 RoleId 只保留一份(接口重复返回或并发残留不再显示两行)
                            if (!seenRoleIds.Add(role.RoleId ?? ""))
                            {
                                continue;
                            }
                            var item = new RoleSignItem
                            {
                                GameName = "鸣潮",
                                RoleName = role.RoleName ?? "未知角色",
                                ServerName = role.ServerName ?? "",
                                Level = role.GameLevel,
                                Source = role,
                                AccountLabel = label,
                            };
                            Roles.Add(item);
                            ResolveRoleHeadAsync(item, role);
                        }
                        // 异步查询各角色当日签到状态(已签到→更新状态;失败保持"待签到")
                        _ = RefreshSignStatusAsync(account);
                    }
                    else if (resp is not null)
                    {
                        // token 失效(如账号在其他设备登录):自动移除该账号
                        AppServices.KuroAccounts.Remove(account.UserId);
                        removed.Add($"{DescribeAccount(account)}: {resp.Msg ?? $"code={resp.Code}"}");
                    }
                    else
                    {
                        removed.Add($"{DescribeAccount(account)}: 网络异常");
                    }
                }
                catch (Exception)
                {
                    removed.Add($"{DescribeAccount(account)}: 网络异常");
                }
            }

            // 同步角色 ID 到设置(取当前账号的第一个有效角色;角色数据页自动加载依赖)
            var current = AppServices.KuroAccounts.Current;
            if (current is not null)
            {
                var firstRoleId = Roles.FirstOrDefault(r => r.Source.RoleId is not null)?.Source.RoleId;
                if (!string.IsNullOrEmpty(firstRoleId) && AppServices.Settings.Current.RoleId != firstRoleId)
                {
                    AppServices.Settings.Current.RoleId = firstRoleId;
                    AppServices.Settings.Save();
                }
            }

            StatusText = removed.Count > 0
                ? $"共 {Roles.Count} 个角色(已移除失效账号:{string.Join("; ", removed)}) → 请重新登录"
                : $"共 {Roles.Count} 个角色";
        }
        finally
        {
            _refreshingRoles = false;
            IsBusy = false;
        }
    }

    private bool _refreshingRoles;

    /// <summary>
    /// 解析角色头像(对齐主页头像链路):磁盘缓存命中直接用本地路径;
    /// 未命中先用远程 URL 显示并后台落盘,完成后切换为本地路径(下次秒开)。
    /// </summary>
    private static void ResolveRoleHeadAsync(RoleSignItem item, GameRoilDataItem role)
    {
        var url = !string.IsNullOrWhiteSpace(role.HeadPhotoUrl) ? role.HeadPhotoUrl : role.GameHeadUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }
        var key = IconDiskCacheService.Safe(role.RoleId ?? role.RoleName ?? url);
        var cached = AppServices.IconCache.GetCachedIconPath("role_head", key);
        if (cached is not null)
        {
            item.HeadUrl = cached;
            return;
        }
        item.HeadUrl = url;
        _ = Task.Run(async () =>
        {
            await AppServices.IconCache.CacheUrlAsync("role_head", key, url);
            var local = AppServices.IconCache.GetCachedIconPath("role_head", key);
            if (!string.IsNullOrEmpty(local))
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() => item.HeadUrl = local);
            }
        });
    }

    /// <summary>跳转「账号」页登录(登录入口已统一迁移到账号页)。</summary>
    [RelayCommand]
    private void GoAccount()
        => WeakReferenceMessenger.Default.Send(new NavigationRequestedMessage(NavigationKeys.Account));

    /// <summary>刷新角色列表(鸣潮):统一走全账号刷新,避免与消息触发的新流程并发造成重复条目。</summary>
    [RelayCommand]
    private async Task RefreshRolesAsync()
    {
        var accounts = AppServices.KuroAccounts.GetAccounts();
        if (accounts.Count == 0)
        {
            return;
        }
        await RefreshAllAccountsAsync(accounts);
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