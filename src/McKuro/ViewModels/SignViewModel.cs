using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using McKuro.Core.Models.Kuro;
using McKuro.Core.Services.Kuro;
using McKuro.Services;
using Microsoft.Extensions.Logging;

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
/// 签到页:库街区账号登录 + 游戏签到 + 库街区每日任务。
/// 参考 Haiyu 的 GamerSignPage / LoginDialog / AutoKuroClientSignService。
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

    // 登录表单
    [ObservableProperty]
    private string _mobileInput = "";

    [ObservableProperty]
    private string _verifyCodeInput = "";

    [ObservableProperty]
    private string _smsStatusText = "";

    // 验证码重发倒计时(60s,对齐 WutheringWavesTool smsCooldown)
    [ObservableProperty]
    private int _smsCountdown;

    [ObservableProperty]
    private bool _smsSending;

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

    /// <summary>发送验证码按钮文案(倒计时中显示剩余秒数)。</summary>
    public string SmsButtonText => SmsCountdown > 0 ? $"重新发送 ({SmsCountdown}s)" : "发送验证码";

    /// <summary>发送验证码按钮可用(极验/发送中或倒计时中禁用)。</summary>
    public bool CanSendSms => !SmsSending && !IsBusy && SmsCountdown <= 0;

    // 设备 ID:会话内固定,贯穿发码与登录(对齐 Haiyu LoginGameViewModel 的 IdV2)
    private readonly string _smsDeviceId = KuroClient.NewDeviceId();

    // 短信/极验流程日志(写本地文件,方便排查)
    private readonly ILogger? _smsLog = AppServices.LoggerFactory?.CreateLogger("McKuro.SmsLogin");

    private readonly DispatcherTimer _smsTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    partial void OnSmsCountdownChanged(int value)
    {
        OnPropertyChanged(nameof(SmsButtonText));
        OnPropertyChanged(nameof(CanSendSms));
    }

    partial void OnSmsSendingChanged(bool value) => OnPropertyChanged(nameof(CanSendSms));

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanSendSms));

    /// <summary>启动验证码重发倒计时(默认 60 秒)。</summary>
    private void StartSmsCountdown(int seconds = 60)
    {
        SmsCountdown = seconds;
        _smsTimer.Tick -= OnSmsTick;
        _smsTimer.Tick += OnSmsTick;
        _smsTimer.Start();
    }

    private void OnSmsTick(object? sender, EventArgs e)
    {
        if (SmsCountdown <= 1)
        {
            _smsTimer.Stop();
            SmsCountdown = 0;
        }
        else
        {
            SmsCountdown--;
        }
    }

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

    /// <summary>
    /// 发送手机号验证码:先极验(GeeTest)人机验证,再调用发送接口;成功后启动 60s 重发倒计时。
    /// 流程对齐 Haiyu:极验 → /user/getSmsCode(mobile + geeTestData)。
    /// </summary>
    [RelayCommand]
    private async Task SendSmsAsync()
    {
        var mobile = MobileInput.Trim();
        if (!Regex.IsMatch(mobile, @"^1[3-9]\d{9}$"))
        {
            SmsStatusText = "请输入正确的 11 位手机号";
            return;
        }
        if (SmsCountdown > 0)
        {
            SmsStatusText = $"请 {SmsCountdown} 秒后再试";
            return;
        }
        if (SmsSending || IsBusy)
        {
            return;
        }

        SmsSending = true;
        SmsStatusText = "正在打开极验验证,请在浏览器完成滑块…";
        try
        {
            // 极验人机验证(系统浏览器打开本地滑块页,完成后本地回调)
            var geeTestJson = await AppServices.GeetVerify.VerifyAsync();
            if (string.IsNullOrEmpty(geeTestJson))
            {
                SmsStatusText = "极验验证未完成或超时,请重试";
                _smsLog?.LogWarning("极验验证未完成或超时(返回 null),手机号: {Mobile}", mobile);
                return;
            }

            _smsLog?.LogInformation("极验验证成功,极验 JSON 长度={Len} 摘要={Summary}",
                geeTestJson.Length, geeTestJson.Length <= 80 ? geeTestJson : geeTestJson[..80] + "…");
            SmsStatusText = "正在发送验证码…";
            var result = await AppServices.Kuro.SendSMSAsync(mobile, geeTestJson, _smsDeviceId);
            if (result is null)
            {
                _smsLog?.LogWarning("SendSMSAsync 返回 null(服务无响应),手机号: {Mobile}", mobile);
                SmsStatusText = "发送失败: 服务无响应";
                return;
            }
            _smsLog?.LogInformation("SendSMSAsync 响应: Code={Code} Success={Success} Msg={Msg} GeeTest={GeeTest}",
                result.Code, result.Success, result.Msg, result.Data?.GeeTest);
            if (result.Code == 242)
            {
                SmsStatusText = "短信发送频繁,请稍后再试";
                return;
            }
            // Data.GeeTest == false 表示服务端确认发送成功(对齐 Haiyu 判断)
            if (result is { Data.GeeTest: false } || result.Success || result.Code is 0 or 200)
            {
                SmsStatusText = "验证码已发送,请查收";
                StartSmsCountdown(60);
            }
            else
            {
                SmsStatusText = $"发送失败: {result.Msg ?? $"code={result.Code}"}";
            }
        }
        catch (Exception ex)
        {
            SmsStatusText = $"发送失败: {ex.Message}";
        }
        finally
        {
            SmsSending = false;
        }
    }

    /// <summary>手机号 + 验证码登录。</summary>
    [RelayCommand]
    private async Task LoginWithSmsAsync()
    {
        var mobile = MobileInput.Trim();
        var code = VerifyCodeInput.Trim();
        if (string.IsNullOrWhiteSpace(mobile) || string.IsNullOrWhiteSpace(code))
        {
            StatusText = "请填写手机号与验证码";
            return;
        }
        if (string.IsNullOrEmpty(_smsDeviceId))
        {
            StatusText = "请先点击「发送验证码」";
            return;
        }

        IsBusy = true;
        StatusText = "正在登录…";
        try
        {
            var result = await AppServices.Kuro.LoginAsync(mobile, code, _smsDeviceId);
            if (result is not { Success: true } || result.Data is null || string.IsNullOrEmpty(result.Data.Token))
            {
                StatusText = $"登录失败: {result?.Msg ?? "响应无效"}";
                return;
            }

            var account = new KuroAccount
            {
                UserId = result.Data.UserId ?? "",
                Token = result.Data.Token!,
                DeviceId = _smsDeviceId,
                Mobile = mobile,
                Nickname = result.Data.UserName ?? "",
            };
            AppServices.KuroAccounts.AddOrUpdate(account);
            _smsTimer.Stop();
            SmsCountdown = 0;
            // 同步 Token 到设置:角色数据页自动加载依赖 KujiequToken
            AppServices.Settings.Current.KujiequToken = account.Token;
            AppServices.Settings.Save();
            RefreshAccount();
            StatusText = "登录成功";
        }
        catch (Exception ex)
        {
            StatusText = $"登录失败: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Logout()
    {
        var current = AppServices.KuroAccounts.Current;
        if (current is not null)
        {
            AppServices.KuroAccounts.Remove(current.UserId);
        }
        IsLoggedIn = false;
        AccountText = "未登录";
        Roles.Clear();
        StatusText = "已退出登录";
    }

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
                            TodaySignText = signed ? "✅ 今日已签到" : "今日尚未签到";
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