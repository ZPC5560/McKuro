using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using McKuro.Core.Models.Kuro;
using McKuro.Core.Services.Gacha;
using McKuro.Core.Services.Kuro;
using McKuro.Services;
using McKuro.Views;
using Microsoft.Extensions.Logging;

namespace McKuro.ViewModels;

/// <summary>同一账号判定:各接口登录态是否属于同一账号。</summary>
public enum SameAccountVerdict
{
    /// <summary>信息不足(仅登录1个接口或缺少手机号)。</summary>
    Unknown,
    /// <summary>已登录接口使用同一手机号,判定为同一账号。</summary>
    Same,
    /// <summary>已登录接口使用不同手机号,可能不是同一账号。</summary>
    Different,
}

/// <summary>接口账号登录状态(登录卡片标签后的状态点)。</summary>
public enum InterfaceLoginState
{
    /// <summary>未登录(灰点)。</summary>
    NotLoggedIn,
    /// <summary>登录正常(绿点)。</summary>
    Ok,
    /// <summary>异常登录:已保存登录但会话校验已失效(橙点)。</summary>
    Error,
}

/// <summary>
/// 账号页:全部接口账号登录的统一入口。
/// 包含:库街区多账号(短信+极验登录/切换/移除)、云鸣潮登录、mcguide 官方评级登录,
/// 以及自动化的「同一账号」判定(任一接口登录成功/切换后自动复用手机号并给出判定,仅提醒不强制登出)。
/// </summary>
public sealed partial class AccountViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _statusText = "";

    // 库街区账号
    [ObservableProperty]
    private string _accountText = LanguageService.Format("Sign.NotLoggedIn");

    [ObservableProperty]
    private int _selectedAccountIndex = -1;

    public ObservableCollection<string> AccountOptions { get; } = [];

    /// <summary>是否存在当前库街区登录账号(控制「退出登录」按钮显示;未登录时隐藏避免空操作)。</summary>
    public bool HasKuroLogin => AppServices.KuroAccounts.Current is not null;

    /// <summary>是否已保存库街区账号(控制「当前账号」切换行显示;无账号时隐藏,避免出现空下拉)。</summary>
    public bool HasKuroAccounts => AccountOptions.Count > 0;

    // ---- 登录状态点(每个接口账号标签后:绿=登录正常,橙=异常登录,灰=未登录) ----
    /// <summary>库街区登录状态点。</summary>
    [ObservableProperty]
    private InterfaceLoginState _kuroLoginState = InterfaceLoginState.NotLoggedIn;

    /// <summary>云鸣潮登录状态点。</summary>
    [ObservableProperty]
    private InterfaceLoginState _cloudLoginState = InterfaceLoginState.NotLoggedIn;

    /// <summary>mcguide 官方评级登录状态点。</summary>
    [ObservableProperty]
    private InterfaceLoginState _guideLoginState = InterfaceLoginState.NotLoggedIn;

    // ---- 库街区短信登录(自签到页迁移;极验 GeeTest 流程对齐 Haiyu)----
    [ObservableProperty]
    private bool _isKuroLoginOpen;

    // ---- 登录卡片堆叠(三个登录账号以堆叠/标签方式展示,登录完自动切下一个) ----
    /// <summary>堆叠卡片当前激活索引:0=库街区,1=云鸣潮,2=官方养成评级。</summary>
    [ObservableProperty]
    private int _selectedLoginCard;

    /// <summary>
    /// 切换到下一个堆叠卡片(登录成功后自动调用):
    /// 按库街区 → 云鸣潮 → 官方评级的顺序推进,已登录的自动跳过,全部登录完则停在最后。
    /// </summary>
    /// <param name="completedIndex">刚完成登录的卡片索引。</param>
    private void AdvanceToNextLoginCard(int completedIndex)
    {
        for (var step = 1; step <= 3; step++)
        {
            var next = (completedIndex + step) % 3;
            if (next != completedIndex && !IsCardLoggedIn(next))
            {
                SelectedLoginCard = next;
                return;
            }
        }
        // 三个都登录完:停回第一个(库街区,通常展示账号管理)
        SelectedLoginCard = 0;
    }

    private bool IsCardLoggedIn(int index) => index switch
    {
        0 => AppServices.KuroAccounts.Current is not null,
        1 => AppServices.CloudGacha.HasSavedLogin,
        2 => AppServices.Guide.HasToken,
        _ => false,
    };

    /// <summary>构造时选第一个未登录的卡片(全部已登录则停在库街区管理账号)。</summary>
    private void SelectInitialLoginCard()
    {
        for (var i = 0; i < 3; i++)
        {
            if (!IsCardLoggedIn(i))
            {
                SelectedLoginCard = i;
                return;
            }
        }
        SelectedLoginCard = 0;
    }

    [ObservableProperty]
    private string _mobileInput = "";

    [ObservableProperty]
    private string _verifyCodeInput = "";

    [ObservableProperty]
    private string _smsStatusText = "";

    [ObservableProperty]
    private int _smsCountdown;

    [ObservableProperty]
    private bool _smsSending;

    /// <summary>发送验证码按钮文案(倒计时中显示剩余秒数)。</summary>
    public string SmsButtonText => SmsCountdown > 0 ? LanguageService.Format("Account.Resend", SmsCountdown) : LanguageService.Format("Account.SendCode");

    /// <summary>发送验证码按钮可用(极验/发送中或倒计时中禁用)。</summary>
    public bool CanSendSms => !SmsSending && SmsCountdown <= 0;

    private readonly DispatcherTimer _smsTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    partial void OnSmsCountdownChanged(int value)
    {
        OnPropertyChanged(nameof(SmsButtonText));
        OnPropertyChanged(nameof(CanSendSms));
    }

    partial void OnSmsSendingChanged(bool value) => OnPropertyChanged(nameof(CanSendSms));

    // 设备 ID:稳定持久化(库街区 did 需跨启动不变,否则触发极验风控)
    private readonly string _smsDeviceId = AppServices.StableDeviceId;

    private readonly ILogger? _loginLog = AppServices.LoggerFactory?.CreateLogger("McKuro.SmsLogin");

    // ---- 云鸣潮登录(自抽卡分析页迁移)----
    [ObservableProperty]
    private bool _isCloudLoggedIn;

    /// <summary>云鸣潮登录表单展开/收起(与库街区卡片同款交互)。</summary>
    [ObservableProperty]
    private bool _isCloudLoginOpen;

    [ObservableProperty]
    private string _cloudAccountText = LanguageService.Format("Sign.NotLoggedIn");

    [ObservableProperty]
    private string _cloudMobile = "";

    [ObservableProperty]
    private string _cloudCode = "";

    [ObservableProperty]
    private string _cloudStatusText = "";

    [ObservableProperty]
    private int _cloudSmsCountdown;

    [ObservableProperty]
    private bool _cloudSmsSending;

    /// <summary>云鸣潮发送验证码按钮文案。</summary>
    public string CloudSmsButtonText => CloudSmsCountdown > 0 ? LanguageService.Format("Account.Resend", CloudSmsCountdown) : LanguageService.Format("Account.SendCode");

    /// <summary>云鸣潮发送验证码按钮可用。</summary>
    public bool CanSendCloudSms => !CloudSmsSending && CloudSmsCountdown <= 0;

    private readonly DispatcherTimer _cloudSmsTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    partial void OnCloudSmsCountdownChanged(int value)
    {
        OnPropertyChanged(nameof(CloudSmsButtonText));
        OnPropertyChanged(nameof(CanSendCloudSms));
    }

    partial void OnCloudSmsSendingChanged(bool value) => OnPropertyChanged(nameof(CanSendCloudSms));

    // ---- mcguide 官方评级登录 ----
    /// <summary>mcguide 登录表单展开/收起(与库街区卡片同款交互)。</summary>
    [ObservableProperty]
    private bool _isGuideLoginOpen;

    [ObservableProperty]
    private string _guideMobile = "";

    [ObservableProperty]
    private string _guideCode = "";

    [ObservableProperty]
    private string _guideSmsText = "";

    [ObservableProperty]
    private bool _guideSmsSending;

    [ObservableProperty]
    private int _guideSmsCountdown;

    [ObservableProperty]
    private string _guideStatusText = "";

    private readonly DispatcherTimer _guideSmsTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    /// <summary>是否已登录 mcguide 攻略站。</summary>
    public bool GuideLoggedIn => AppServices.Guide.HasToken;

    /// <summary>mcguide 发送验证码按钮文案。</summary>
    public string GuideSmsButtonText => GuideSmsCountdown > 0 ? LanguageService.Format("Account.Resend", GuideSmsCountdown) : LanguageService.Format("Account.SendCode");

    /// <summary>mcguide 发送验证码按钮可用。</summary>
    public bool CanSendGuideSms => !GuideSmsSending && GuideSmsCountdown <= 0;

    partial void OnGuideSmsCountdownChanged(int value)
    {
        OnPropertyChanged(nameof(GuideSmsButtonText));
        OnPropertyChanged(nameof(CanSendGuideSms));
    }

    partial void OnGuideSmsSendingChanged(bool value) => OnPropertyChanged(nameof(CanSendGuideSms));

    // ---- 自动化「同一账号」判定 ----
    /// <summary>当前判定结果(自动更新,只提醒不强制登出/删除)。</summary>
    [ObservableProperty]
    private SameAccountVerdict _sameAccountVerdict = SameAccountVerdict.Unknown;

    /// <summary>判定结果文案(顶部状态条/徽标显示)。</summary>
    [ObservableProperty]
    private string _sameAccountStatus = "";

    /// <summary>判定为不同账号(用警告色提示)。</summary>
    public bool SameAccountIsDifferent => SameAccountVerdict == SameAccountVerdict.Different;

    /// <summary>判定为同一账号(用强调色提示)。</summary>
    public bool SameAccountIsSame => SameAccountVerdict == SameAccountVerdict.Same;

    /// <summary>信息不足(中性提示)。</summary>
    public bool SameAccountIsUnknown => SameAccountVerdict == SameAccountVerdict.Unknown;

    partial void OnSameAccountVerdictChanged(SameAccountVerdict value)
    {
        OnPropertyChanged(nameof(SameAccountIsSame));
        OnPropertyChanged(nameof(SameAccountIsDifferent));
        OnPropertyChanged(nameof(SameAccountIsUnknown));
    }

    private readonly object _checkLock = new();
    private bool _checking;

    public AccountViewModel()
    {
        var s = AppServices.Settings.Current;
        RefreshAccounts();
        RefreshCloudState();

        // 手机号复用:打开页面即用各接口已保存的手机号预填登录表单
        var lastKuroMobile = AppServices.KuroAccounts.GetAccounts()
            .LastOrDefault(a => !string.IsNullOrEmpty(a.Mobile))?.Mobile ?? "";
        _mobileInput = lastKuroMobile;
        _cloudMobile = string.IsNullOrWhiteSpace(s.CloudLoginPhone) ? lastKuroMobile : s.CloudLoginPhone;
        _guideMobile = string.IsNullOrWhiteSpace(s.GuidePhone) ? lastKuroMobile : s.GuidePhone;

        _guideStatusText = AppServices.Guide.HasToken
            ? LanguageService.Format("Account.GuideLoggedIn", AppServices.Settings.Current.GuideCName)
            : LanguageService.Format("Account.GuideNotLoggedIn");

        // 尚无任何库街区账号时自动展开登录表单;云鸣潮/mcguide 未登录时同样展开
        _isKuroLoginOpen = AppServices.KuroAccounts.GetAccounts().Count == 0;
        _isCloudLoginOpen = !AppServices.CloudGacha.HasSavedLogin;
        _isGuideLoginOpen = !AppServices.Guide.HasToken;
        // mcguide 已保存登录先按绿点显示,会话校验失败再转橙点
        _guideLoginState = AppServices.Guide.HasToken ? InterfaceLoginState.Ok : InterfaceLoginState.NotLoggedIn;

        // 自动进行同一账号判定
        RefreshSameAccountAuto();
        SelectInitialLoginCard();
    }

    /// <summary>导航到账号页时调用:并行校验三个接口的登录态是否过期。</summary>
    public void OnNavigatedTo()
    {
        if (_sessionValidating)
        {
            return;
        }
        _sessionValidating = true;
        _ = ValidateSessionsAsync();
    }

    private bool _sessionValidating;

    private async Task ValidateSessionsAsync()
    {
        try
        {
            await Task.WhenAll(
                ValidateKuroSessionAsync(),
                ValidateCloudSessionAsync(),
                ValidateGuideSessionAsync()).ConfigureAwait(true);
        }
        finally
        {
            _sessionValidating = false;
        }
    }

    /// <summary>库街区:用当前账号 token 拉角色列表判定登录态(不自动删除账号,非破坏性)。</summary>
    private async Task ValidateKuroSessionAsync()
    {
        var account = AppServices.KuroAccounts.Current;
        if (account is null)
        {
            return;
        }
        try
        {
            var gamer = await AppServices.Kuro.GetGamerAsync(account, (int)KuroGameType.Waves).ConfigureAwait(true);
            if (gamer is { Code: 200, Data: not null })
            {
                KuroLoginState = InterfaceLoginState.Ok;
                return; // 有效,不打扰
            }
            // 服务端明确拒绝 → 橙点(异常登录);网络异常不改状态,避免误报
            KuroLoginState = InterfaceLoginState.Error;
            StatusText = LanguageService.Format("Account.KuroSessionExpired", gamer?.Msg ?? $"code={gamer?.Code}");
        }
        catch (Exception ex)
        {
            StatusText = LanguageService.Format("Account.KuroCheckFailed", ex.Message);
        }
    }

    /// <summary>云鸣潮:静默续期一次判定会话有效性;失效自动退出登录并展开登录表单。</summary>
    private async Task ValidateCloudSessionAsync()
    {
        if (!AppServices.CloudGacha.HasSavedLogin)
        {
            return;
        }
        var (status, msg) = await AppServices.CloudGacha.ValidateSessionAsync().ConfigureAwait(true);
        if (status == CloudGachaStatus.LoginFailed)
        {
            // 会话已失效:自动退出登录(清除保存的会话),避免同时显示登录表单与「退出登录」按钮;
            // 状态点转灰(未登录),状态文案保留失效原因
            AppServices.CloudGacha.Logout();
            RefreshCloudState();
            CloudStatusText = msg ?? LanguageService.Format("Account.CloudSessionExpired");
            IsCloudLoginOpen = true;
        }
        else if (status == CloudGachaStatus.Success)
        {
            CloudLoginState = InterfaceLoginState.Ok;
        }
        // FetchFailed(网络等临时失败)不改状态,避免误报异常登录
    }

    /// <summary>mcguide:轻量鉴权请求判定 x-token;过期时服务层已清会话,这里展开登录表单。</summary>
    private async Task ValidateGuideSessionAsync()
    {
        var (valid, msg) = await AppServices.Guide.ValidateSessionAsync().ConfigureAwait(true);
        GuideStatusText = msg;
        if (valid == false)
        {
            // 会话已被服务层清除(自动退出登录):状态点转灰(未登录),展开登录表单
            GuideLoginState = InterfaceLoginState.NotLoggedIn;
            IsGuideLoginOpen = true;
            OnPropertyChanged(nameof(GuideLoggedIn));
            RefreshSameAccountAuto();
        }
        else if (valid == true)
        {
            GuideLoginState = InterfaceLoginState.Ok;
        }
        // valid == null(未登录/校验失败)不改状态,避免误报
    }

    private void RefreshAccounts()
    {
        AccountOptions.Clear();
        foreach (var account in AppServices.KuroAccounts.GetAccounts())
        {
            var name = string.IsNullOrEmpty(account.Nickname) ? account.UserId : account.Nickname;
            var mobileSuffix = string.IsNullOrEmpty(account.Mobile) ? "" : $" · {MaskMobile(account.Mobile)}";
            AccountOptions.Add($"{name} (ID: {account.UserId}{mobileSuffix})");
        }
        var current = AppServices.KuroAccounts.Current;
        AccountText = current is null ? LanguageService.Format("Sign.NotLoggedIn") : AccountOptions.FirstOrDefault(o => o.Contains(current.UserId)) ?? LanguageService.Format("Sign.NotLoggedIn");
        SelectedAccountIndex = current is null ? -1 : Math.Max(0, AccountOptions.ToList().FindIndex(o => o.Contains(current.UserId)));
        // 已保存登录先按绿点显示,会话校验失败再转橙点
        KuroLoginState = current is null ? InterfaceLoginState.NotLoggedIn : InterfaceLoginState.Ok;
        OnPropertyChanged(nameof(HasKuroLogin));
        OnPropertyChanged(nameof(HasKuroAccounts));
        RefreshSameAccountAuto();
    }

    private static string MaskMobile(string mobile)
        => mobile.Length == 11 ? $"{mobile[..3]}****{mobile[^4..]}" : mobile;

    private void RefreshCloudState()
    {
        IsCloudLoggedIn = AppServices.CloudGacha.HasSavedLogin;
        CloudAccountText = IsCloudLoggedIn
            ? (string.IsNullOrWhiteSpace(AppServices.CloudGacha.SavedLoginName) ? LanguageService.Format("Account.LoggedIn") : AppServices.CloudGacha.SavedLoginName)
            : LanguageService.Format("Sign.NotLoggedIn");
        // 已保存登录先按绿点显示,会话校验失败再转橙点
        CloudLoginState = IsCloudLoggedIn ? InterfaceLoginState.Ok : InterfaceLoginState.NotLoggedIn;
        RefreshSameAccountAuto();
    }

    partial void OnSelectedAccountIndexChanged(int value)
    {
        var accounts = AppServices.KuroAccounts.GetAccounts();
        if (value < 0 || value >= accounts.Count)
        {
            return;
        }
        var account = accounts[value];
        AppServices.KuroAccounts.Current = account;
        // 同步 token 到设置:角色数据页自动加载依赖 KujiequToken
        AppServices.Settings.Current.KujiequToken = account.Token;
        AppServices.Settings.Save();
        RefreshAccounts();
        StatusText = LanguageService.Format("Account.Switched", string.IsNullOrEmpty(account.Nickname) ? account.UserId : account.Nickname);
        // 通知角色数据页按新账号刷新
        WeakReferenceMessenger.Default.Send(new RolesRefreshRequestedMessage(account.UserId));
    }

    /// <summary>展开/收起库街区登录表单(添加新账号)。</summary>
    [RelayCommand]
    private void ToggleKuroLogin()
    {
        IsKuroLoginOpen = !IsKuroLoginOpen;
        if (IsKuroLoginOpen && string.IsNullOrEmpty(MobileInput))
        {
            // 多账户优化:复用已保存账号的手机号,只需再输验证码
            var reused = AppServices.KuroAccounts.GetAccounts()
                .LastOrDefault(a => !string.IsNullOrEmpty(a.Mobile))?.Mobile;
            if (!string.IsNullOrEmpty(reused))
            {
                MobileInput = reused;
                SmsStatusText = LanguageService.Format("Account.PhoneReused");
            }
        }
    }

    /// <summary>展开/收起云鸣潮登录表单(与库街区同款交互;打开时复用已保存手机号)。</summary>
    [RelayCommand]
    private void ToggleCloudLogin()
    {
        IsCloudLoginOpen = !IsCloudLoginOpen;
        if (IsCloudLoginOpen && string.IsNullOrEmpty(CloudMobile))
        {
            var reused = AppServices.Settings.Current.CloudLoginPhone;
            if (string.IsNullOrWhiteSpace(reused))
            {
                reused = AppServices.KuroAccounts.GetAccounts()
                    .LastOrDefault(a => !string.IsNullOrEmpty(a.Mobile))?.Mobile;
            }
            if (!string.IsNullOrEmpty(reused))
            {
                CloudMobile = reused;
                CloudStatusText = LanguageService.Format("Account.PhoneReused");
            }
        }
    }

    /// <summary>展开/收起 mcguide 登录表单(与库街区同款交互;打开时复用已保存手机号)。</summary>
    [RelayCommand]
    private void ToggleGuideLogin()
    {
        IsGuideLoginOpen = !IsGuideLoginOpen;
        if (IsGuideLoginOpen && string.IsNullOrEmpty(GuideMobile))
        {
            var reused = AppServices.Settings.Current.GuidePhone;
            if (string.IsNullOrWhiteSpace(reused))
            {
                reused = AppServices.KuroAccounts.GetAccounts()
                    .LastOrDefault(a => !string.IsNullOrEmpty(a.Mobile))?.Mobile;
            }
            if (!string.IsNullOrEmpty(reused))
            {
                GuideMobile = reused;
                GuideSmsText = LanguageService.Format("Account.PhoneReused");
            }
        }
    }

    /// <summary>
    /// 退出当前库街区账号登录(与原「移除」合并为同一动作:移除当前账号并切回未登录态,
    /// 因两者功能相同,仅保留这一个入口)。
    /// </summary>
    [RelayCommand]
    private void Logout()
    {
        var current = AppServices.KuroAccounts.Current;
        if (current is not null)
        {
            AppServices.KuroAccounts.Remove(current.UserId);
            // 通知签到/角色等页同步登出态(与登录消息同源)
            WeakReferenceMessenger.Default.Send(new RolesRefreshRequestedMessage(current.UserId));
        }
        RefreshAccounts();
        StatusText = LanguageService.Format("Account.LoggedOut");
    }

    // ==================== 库街区短信登录(自签到页迁移) ====================

    /// <summary>
    /// 发送手机号验证码:先极验(GeeTest)人机验证,再调用发送接口;成功后启动 60s 重发倒计时。
    /// 流程对齐 Haiyu:极验 → /user/getSmsCode(mobile + geeTestData)。
    /// </summary>
    [RelayCommand]
    private async Task SendSmsAsync()
    {
        var mobile = MobileInput.Trim();
        if (!MobileRegex().IsMatch(mobile))
        {
            SmsStatusText = LanguageService.Format("Account.BadPhone");
            return;
        }
        if (SmsCountdown > 0)
        {
            SmsStatusText = LanguageService.Format("Account.WaitRetry", SmsCountdown);
            return;
        }
        if (SmsSending)
        {
            return;
        }

        SmsSending = true;
        SmsStatusText = LanguageService.Format("Account.OpeningGeetest");
        var geetCts = new CancellationTokenSource(); // 用户关闭内置验证窗口时取消等待
        GeetestWindow? geetestWindow = null;
        var fallbackToBrowser = false; // 内置窗口不可用自动改走系统浏览器:此时关窗不算取消
        try
        {
            // 极验人机验证:macOS 用应用内 WKWebView 窗口完成(对齐 Java 版鸣潮助手),
            // 其余平台回退系统浏览器打开本地滑块页。结果统一走本地 HTTP 回调。
            var geeTestJson = await AppServices.GeetVerify.VerifyAsync(
                geetCts.Token,
                openBrowser: url =>
                {
                    if (GeetestWindow.IsPlatformSupported)
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            var owner = (Application.Current?.ApplicationLifetime
                                as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
                            geetestWindow = new GeetestWindow(url);
                            geetestWindow.Closed += (_, _) =>
                            {
                                geetestWindow = null;
                                // 用户主动关闭 → 立即取消等待;验证流程收尾先关闭窗口时
                                // Closed 回调晚于 finally 的 Dispose,需吞掉 ObjectDisposedException;
                                // 内置窗口不可用自动回退系统浏览器时的关窗不算取消
                                if (!fallbackToBrowser)
                                {
                                    try
                                    {
                                        geetCts.Cancel();
                                    }
                                    catch (ObjectDisposedException)
                                    {
                                    }
                                }
                            };
                            geetestWindow.CreationFailed += () => Dispatcher.UIThread.Post(() =>
                            {
                                // 内置窗口不可用(WebView2 被安全软件拦截等):自动改用系统浏览器,
                                // 验证结果仍走本地 HTTP 回调,流程继续
                                fallbackToBrowser = true;
                                geetestWindow?.Close();
                                geetestWindow = null;
                                SmsStatusText = LanguageService.Format("Account.GeetestFallback");
                                _loginLog?.LogWarning("内置极验窗口不可用,自动回退系统浏览器");
                                try
                                {
                                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                                }
                                catch (Exception ex)
                                {
                                    SmsStatusText = LanguageService.Format("Account.OpenBrowserFailed", ex.Message);
                                }
                            });
                            if (owner is not null)
                            {
                                _ = geetestWindow.ShowDialog(owner);
                            }
                            else
                            {
                                geetestWindow.Show();
                            }
                        });
                    }
                    else
                    {
                        // 平台无内置 WebView 或 WebView2 运行时缺失:维持原有系统浏览器流程
                        SmsStatusText = LanguageService.Format("Account.OpeningBrowser");
                        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                    }
                });
            if (string.IsNullOrEmpty(geeTestJson))
            {
                SmsStatusText = geetestWindow is null
                    ? LanguageService.Format("Account.GeetestCancelled")
                    : LanguageService.Format("Account.GeetestTimeout");
                _loginLog?.LogWarning("极验验证未完成或超时(返回 null),手机号: {Mobile}", mobile);
                return;
            }

            _loginLog?.LogInformation("极验验证成功,极验 JSON 长度={Len}", geeTestJson.Length);
            SmsStatusText = LanguageService.Format("Account.Sending");
            var result = await AppServices.Kuro.SendSMSAsync(mobile, geeTestJson, _smsDeviceId);
            if (result is null)
            {
                SmsStatusText = LanguageService.Format("Account.SendNoResponse");
                return;
            }
            _loginLog?.LogInformation("SendSMSAsync 响应: Code={Code} Success={Success} Msg={Msg}",
                result.Code, result.Success, result.Msg);
            if (result.Code == 242)
            {
                SmsStatusText = LanguageService.Format("Account.SendThrottled");
                return;
            }
            // Data.GeeTest == false 表示服务端确认发送成功(对齐 Haiyu 判断)
            if (result is { Data.GeeTest: false } || result.Success || result.Code is 0 or 200)
            {
                SmsStatusText = LanguageService.Format("Account.CodeSent");
                StartKuroSmsCountdown(60);
            }
            else
            {
                SmsStatusText = LanguageService.Format("Account.SendFailed", result.Msg ?? $"code={result.Code}");
            }
        }
        catch (Exception ex)
        {
            SmsStatusText = LanguageService.Format("Account.SendFailed", ex.Message);
        }
        finally
        {
            SmsSending = false;
            // 关闭可能仍开着的内置验证窗口(验证完成/失败/异常后统一收尾)
            if (geetestWindow is not null)
            {
                var win = geetestWindow;
                Dispatcher.UIThread.Post(win.Close);
            }
            geetCts.Dispose();
        }
    }

    /// <summary>库街区手机号 + 验证码登录(支持多账号:同手机号自动合并旧记录)。</summary>
    [RelayCommand]
    private async Task LoginWithSmsAsync()
    {
        var mobile = MobileInput.Trim();
        var code = VerifyCodeInput.Trim();
        if (string.IsNullOrWhiteSpace(mobile) || string.IsNullOrWhiteSpace(code))
        {
            SmsStatusText = LanguageService.Format("Account.FillBoth");
            return;
        }
        if (string.IsNullOrEmpty(_smsDeviceId))
        {
            SmsStatusText = LanguageService.Format("Account.SendFirst");
            return;
        }

        SmsStatusText = LanguageService.Format("Account.LoggingIn");
        try
        {
            var result = await AppServices.Kuro.LoginAsync(mobile, code, _smsDeviceId);
            if (result is not { Success: true } || result.Data is null || string.IsNullOrEmpty(result.Data.Token))
            {
                SmsStatusText = LanguageService.Format("Account.LoginFailed", result?.Msg ?? LanguageService.Format("Account.InvalidResponse"));
                return;
            }

            var existed = AppServices.KuroAccounts.GetAccounts().FirstOrDefault(a => a.UserId == result.Data!.UserId);
            var sameMobileOld = AppServices.KuroAccounts.GetAccounts()
                .FirstOrDefault(a => a.UserId != result.Data!.UserId && a.Mobile == mobile);

            var account = new KuroAccount
            {
                UserId = result.Data.UserId ?? "",
                Token = result.Data.Token!,
                DeviceId = _smsDeviceId,
                Mobile = mobile,
                Nickname = result.Data.UserName ?? "",
            };
            AppServices.KuroAccounts.AddOrUpdate(account);

            string note;
            if (existed is not null)
            {
                // 同一账号重新登录:仅更新登录态,不产生重复条目
                note = LanguageService.Format("Account.NoteExists");
            }
            else if (sameMobileOld is not null)
            {
                // 同手机号但 UserId 不同:判定为同一账号的旧记录,合并移除
                AppServices.KuroAccounts.Remove(sameMobileOld.UserId);
                note = LanguageService.Format("Account.NoteMerged", sameMobileOld.UserId);
            }
            else
            {
                note = LanguageService.Format("Account.NoteAdded", account.Nickname, account.UserId);
            }

            _smsTimer.Stop();
            SmsCountdown = 0;
            VerifyCodeInput = "";
            // 同步 Token 到设置:角色数据页自动加载依赖 KujiequToken
            AppServices.Settings.Current.KujiequToken = account.Token;
            AppServices.Settings.Save();
            RefreshAccounts();
            SmsStatusText = LanguageService.Format("Account.LoginSuccessWith", note);
            StatusText = LanguageService.Format("Account.KuroLoginSuccess", note);

            // 一个接口账号登录成功 → 其他接口账号复用该手机号
            ReusePhoneAcrossLogins(mobile, source: "kuro");

            // 同步首个角色 ID 并通知角色数据页
            await SyncFirstRoleAsync(account);

            IsKuroLoginOpen = false;
            // 堆叠卡片:库街区登录完成 → 自动切到下一个未登录的卡片
            AdvanceToNextLoginCard(0);
        }
        catch (Exception ex)
        {
            SmsStatusText = LanguageService.Format("Account.LoginFailed", ex.Message);
        }
    }

    /// <summary>同步首个角色 ID 到设置并发送角色刷新消息(原签到页登录后行为)。</summary>
    private async Task SyncFirstRoleAsync(KuroAccount account)
    {
        try
        {
            var roles = await AppServices.Kuro.GetGamerAsync(account, (int)KuroGameType.Waves);
            if (roles is { Code: 200, Data: not null })
            {
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
            }
        }
        catch (Exception)
        {
            // 角色 ID 同步失败不阻塞登录流程(签到页刷新时会再次同步)
        }
        WeakReferenceMessenger.Default.Send(new RolesRefreshRequestedMessage(account.UserId));
    }

    private void StartKuroSmsCountdown(int seconds = 60)
    {
        SmsCountdown = seconds;
        _smsTimer.Tick -= OnKuroSmsTick;
        _smsTimer.Tick += OnKuroSmsTick;
        _smsTimer.Start();
    }

    private void OnKuroSmsTick(object? sender, EventArgs e)
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

    // ==================== 云鸣潮登录(自抽卡分析页迁移) ====================

    /// <summary>发送云鸣潮登录验证码。</summary>
    [RelayCommand]
    private async Task SendCloudSmsAsync()
    {
        if (CloudSmsSending || CloudSmsCountdown > 0)
        {
            return;
        }
        if (string.IsNullOrWhiteSpace(CloudMobile))
        {
            CloudStatusText = LanguageService.Format("Account.FillPhone");
            return;
        }
        CloudSmsSending = true;
        CloudStatusText = LanguageService.Format("Account.Sending");
        try
        {
            var (ok, msg) = await AppServices.CloudGacha.SendSmsAsync(CloudMobile.Trim());
            CloudStatusText = msg ?? (ok ? LanguageService.Format("Account.CodeSent") : LanguageService.Format("Account.SendFailedShort"));
            if (ok)
            {
                StartCloudSmsCountdown(60);
            }
        }
        catch (Exception ex)
        {
            CloudStatusText = LanguageService.Format("Account.SendFailed", ex.Message);
        }
        finally
        {
            CloudSmsSending = false;
        }
    }

    /// <summary>云鸣潮手机号 + 验证码登录(成功后抽卡分析页可直接同步)。</summary>
    [RelayCommand]
    private async Task CloudLoginAsync()
    {
        if (string.IsNullOrWhiteSpace(CloudMobile) || string.IsNullOrWhiteSpace(CloudCode))
        {
            CloudStatusText = LanguageService.Format("Account.FillBoth");
            return;
        }
        CloudStatusText = LanguageService.Format("Account.LoggingIn");
        try
        {
            var (ok, msg) = await AppServices.CloudGacha.LoginAsync(CloudMobile.Trim(), CloudCode.Trim());
            if (ok)
            {
                CloudCode = "";
                IsCloudLoginOpen = false;
                // 停掉验证码倒计时(表单已收起,避免后台空转)
                _cloudSmsTimer.Tick -= OnCloudSmsTick;
                _cloudSmsTimer.Stop();
                CloudSmsCountdown = 0;
                CloudSmsSending = false;
                RefreshCloudState();
                CloudStatusText = LanguageService.Format("Account.CloudLoginDone");
                StatusText = LanguageService.Format("Account.CloudLoginSuccess");

                // 堆叠卡片:云鸣潮登录完成 → 自动切到下一个未登录的卡片
                AdvanceToNextLoginCard(1);

                // 一个接口账号登录成功 → 其他接口账号复用该手机号
                ReusePhoneAcrossLogins(CloudMobile.Trim(), source: "cloud");
            }
            else
            {
                CloudStatusText = msg ?? LanguageService.Format("Account.LoginFailedShort");
            }
        }
        catch (Exception ex)
        {
            CloudStatusText = LanguageService.Format("Account.LoginFailed", ex.Message);
        }
    }

    /// <summary>退出云鸣潮登录。</summary>
    [RelayCommand]
    private void CloudLogout()
    {
        AppServices.CloudGacha.Logout();
        RefreshCloudState();
        CloudStatusText = LanguageService.Format("Account.CloudLoggedOut");
        // 退出后重新展开登录表单,便于再次登录
        IsCloudLoginOpen = true;
    }

    private void StartCloudSmsCountdown(int seconds = 60)
    {
        CloudSmsCountdown = seconds;
        _cloudSmsTimer.Tick -= OnCloudSmsTick;
        _cloudSmsTimer.Tick += OnCloudSmsTick;
        _cloudSmsTimer.Start();
    }

    private void OnCloudSmsTick(object? sender, EventArgs e)
    {
        if (CloudSmsCountdown <= 1)
        {
            _cloudSmsTimer.Stop();
            CloudSmsCountdown = 0;
        }
        else
        {
            CloudSmsCountdown--;
        }
    }

    // ==================== mcguide 官方评级登录 ====================

    /// <summary>发送 mcguide 官方评级登录验证码。</summary>
    [RelayCommand]
    private async Task SendGuideSmsAsync()
    {
        var mobile = GuideMobile.Trim();
        if (string.IsNullOrWhiteSpace(mobile))
        {
            GuideSmsText = LanguageService.Format("Account.FillPhone");
            return;
        }
        if (GuideSmsSending || GuideSmsCountdown > 0)
        {
            return;
        }

        GuideSmsSending = true;
        GuideSmsText = LanguageService.Format("Account.Sending");
        try
        {
            var (ok, msg) = await AppServices.Guide.SendSmsAsync(mobile);
            GuideSmsText = msg ?? (ok ? LanguageService.Format("Account.CodeSent") : LanguageService.Format("Account.SendFailedShort"));
            if (ok)
            {
                StartGuideSmsCountdown(60);
            }
        }
        catch (Exception ex)
        {
            GuideSmsText = LanguageService.Format("Account.SendFailed", ex.Message);
        }
        finally
        {
            GuideSmsSending = false;
        }
    }

    /// <summary>mcguide 手机号 + 验证码登录(登录后角色页显示官方评级)。</summary>
    [RelayCommand]
    private async Task GuideLoginAsync()
    {
        var mobile = GuideMobile.Trim();
        var code = GuideCode.Trim();
        if (string.IsNullOrWhiteSpace(mobile) || string.IsNullOrWhiteSpace(code))
        {
            GuideStatusText = LanguageService.Format("Account.FillBoth");
            return;
        }

        GuideStatusText = LanguageService.Format("Account.GuideLoggingIn");
        try
        {
            var (ok, msg) = await AppServices.Guide.LoginAsync(mobile, code);
            GuideStatusText = msg ?? (ok ? LanguageService.Format("Account.LoginSuccess") : LanguageService.Format("Account.LoginFailedShort"));
            if (ok)
            {
                GuideCode = "";
                IsGuideLoginOpen = false;
                // 停掉验证码倒计时(表单已收起,避免后台空转)
                _guideSmsTimer.Tick -= OnGuideSmsTick;
                _guideSmsTimer.Stop();
                GuideSmsCountdown = 0;
                GuideSmsSending = false;
                OnPropertyChanged(nameof(GuideLoggedIn));
                GuideLoginState = InterfaceLoginState.Ok;
                StatusText = LanguageService.Format("Account.GuideLoginSuccess");

                // 堆叠卡片:官方评级登录完成 → 自动切到下一个未登录的卡片(全登录完则回库街区)
                AdvanceToNextLoginCard(2);

                // 一个接口账号登录成功 → 其他接口账号复用该手机号
                ReusePhoneAcrossLogins(mobile, source: "guide");
                RefreshSameAccountAuto();
            }
        }
        catch (Exception ex)
        {
            GuideStatusText = LanguageService.Format("Account.LoginFailed", ex.Message);
        }
    }

    private void StartGuideSmsCountdown(int seconds = 60)
    {
        GuideSmsCountdown = seconds;
        _guideSmsTimer.Tick -= OnGuideSmsTick;
        _guideSmsTimer.Tick += OnGuideSmsTick;
        _guideSmsTimer.Start();
    }

    /// <summary>退出 mcguide 登录(清除攻略站会话;角色页随之隐藏官方评级)。</summary>
    [RelayCommand]
    private void GuideLogout()
    {
        var s = AppServices.Settings.Current;
        s.GuideToken = "";
        s.GuideCUid = "";
        s.GuideCName = "";
        s.GuidePlayerId = 0;
        s.GuideServerId = "";
        AppServices.Settings.Save();
        OnPropertyChanged(nameof(GuideLoggedIn));
        GuideLoginState = InterfaceLoginState.NotLoggedIn;
        GuideStatusText = LanguageService.Format("Account.GuideNotLoggedIn");
        StatusText = LanguageService.Format("Account.GuideLoggedOut");
        RefreshSameAccountAuto();
        // 退出后重新展开登录表单,便于再次登录
        IsGuideLoginOpen = true;
    }

    private void OnGuideSmsTick(object? sender, EventArgs e)
    {
        if (GuideSmsCountdown <= 1)
        {
            _guideSmsTimer.Stop();
            GuideSmsCountdown = 0;
        }
        else
        {
            GuideSmsCountdown--;
        }
    }

    // ==================== 手机号复用 + 自动同一账号判定 ====================

    /// <summary>
    /// 多账户登录优化:任一接口账号登录成功后,把该手机号复用到其他接口的登录表单,
    /// 用户在其他接口登录时无需重复输入手机号,只需输入验证码。
    /// </summary>
    private void ReusePhoneAcrossLogins(string phone, string source)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return;
        }
        if (source != "kuro")
        {
            MobileInput = phone;
        }
        if (source != "cloud")
        {
            CloudMobile = phone;
        }
        if (source != "guide")
        {
            GuideMobile = phone;
        }
    }

    /// <summary>
    /// 自动判定多个接口账号是否属于同一个账号(按登录手机号)。
    /// 纯提醒:绝不自动登出/删除任何账号。已登录接口数 ≥2 且手机号一致 → 同一账号;
    /// 手机号不一致 → 判定为不同账号(仅提醒,不强制登出);信息缺失 → 返回 Unknown 并说明。
    /// 判定结果仅在标题行右侧徽标展示(逐接口明细文字已移除,登录状态看标签后的状态点)。
    /// </summary>
    private void RefreshSameAccountAuto()
    {
        // 防重入(登录/切换时多次触发):只保留最近一次的判定落盘
        lock (_checkLock)
        {
            if (_checking)
            {
                return;
            }
            _checking = true;
        }

        try
        {
            // 各接口登录态与手机号
            var kuro = AppServices.KuroAccounts.Current;
            var kuroPhone = kuro?.Mobile ?? "";
            var cloudPhone = AppServices.Settings.Current.CloudLoginPhone;
            var guidePhone = AppServices.Settings.Current.GuidePhone;

            // 已登录的接口
            var active = new List<(string Name, string Phone)>();
            if (kuro is not null)
            {
                active.Add((LanguageService.Format("Account.IfKuro"), kuroPhone));
            }
            if (AppServices.CloudGacha.HasSavedLogin)
            {
                active.Add((LanguageService.Format("Account.IfCloud"), cloudPhone));
            }
            if (AppServices.Guide.HasToken)
            {
                active.Add(("mcguide", guidePhone));
            }

            if (active.Count <= 1)
            {
                SameAccountVerdict = SameAccountVerdict.Unknown;
                SameAccountStatus = active.Count == 0
                    ? LanguageService.Format("Account.NoAccounts")
                    : LanguageService.Format("Account.OnlyOne", active[0].Name);
                return;
            }

            var missing = active.Where(a => string.IsNullOrWhiteSpace(a.Phone)).ToList();
            if (missing.Count > 0)
            {
                // 有接口没记录手机号,无法完整判定
                SameAccountVerdict = SameAccountVerdict.Unknown;
                SameAccountStatus = LanguageService.Format("Account.PhonesMissing", string.Join(" / ", missing.Select(m => m.Name)));
                return;
            }

            var distinctPhones = active.Select(a => a.Phone).Distinct().Count();
            if (distinctPhones == 1)
            {
                SameAccountVerdict = SameAccountVerdict.Same;
                SameAccountStatus = LanguageService.Format("Account.AllSame", active.Count);
            }
            else
            {
                // 不同手机号 → 完全不同或部分不同,仅提醒不强制登出
                SameAccountVerdict = SameAccountVerdict.Different;
                SameAccountStatus = LanguageService.Format("Account.DifferentPhones");
            }
        }
        finally
        {
            lock (_checkLock)
            {
                _checking = false;
            }
        }
    }

    [GeneratedRegex(@"^1[3-9]\d{9}$")]
    private static partial Regex MobileRegex();
}

/// <summary>接口登录状态 → 状态点颜色(绿=登录正常,橙=异常登录,灰=未登录)。</summary>
public sealed class LoginDotBrushConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly LoginDotBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        var color = value is InterfaceLoginState state
            ? state switch
            {
                InterfaceLoginState.Ok => Avalonia.Media.Color.Parse("#22C55E"),   // 登录正常
                InterfaceLoginState.Error => Avalonia.Media.Color.Parse("#F59E0B"), // 异常登录
                _ => Avalonia.Media.Color.Parse("#9CA3AF"),                        // 未登录
            }
            : Avalonia.Media.Color.Parse("#9CA3AF");
        return new Avalonia.Media.SolidColorBrush(color);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}
