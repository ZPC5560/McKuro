using System.Collections.ObjectModel;
using System.Globalization;
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
using McKuro.Core.Services.User;
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
    /// <summary>
    /// 有接口的登录态已失效(会话过期)。
    /// <para>优先于「同一账号」结论:即便手机号一致,失效的接口也需要重新登录,
    /// 此时不该只显示"均为同一账号"而掩盖问题。</para>
    /// </summary>
    SessionExpired,
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
/// 包含:库街区多账号(短信+极验登录/切换/移除)、云鸣潮多账号(短信登录/切换/移除)、mcguide 官方评级登录,
/// 以及自动化的「同一账号」判定(任一接口登录成功/切换后自动复用手机号并给出判定,仅提醒不强制登出)。
/// </summary>
public sealed partial class AccountViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _statusText = "";

    // 库街区账号
    [ObservableProperty]
    private string _accountText = LanguageService.Format("Sign.NotLoggedIn");

    /// <summary>已保存的库街区账号列表(列表样式展示,点行切换当前;替代原下拉)。</summary>
    public ObservableCollection<InterfaceAccountItem> KuroAccountItems { get; } = [];

    /// <summary>每行对应的库街区 UserId(顺序与 <see cref="KuroAccountItems"/> 一一对应)。</summary>
    private readonly List<string> _accountOptionKeys = [];

    /// <summary>已保存的云鸣潮账号列表(与库街区同款列表样式)。</summary>
    public ObservableCollection<InterfaceAccountItem> CloudAccountItems { get; } = [];

    /// <summary>云鸣潮列表每行的稳定 Id(顺序与 <see cref="CloudAccountItems"/> 一一对应)。</summary>
    private readonly List<string> _cloudAccountKeys = [];

    // ---- 接口账号 → 游戏角色显示口径(昵称 · 游戏角色 UID) ----
    // 本地官方启动器凭证枚举出的角色;账号页三张卡片都按手机号/账号名/库街区 UID 在其中匹配,
    // 显示游戏昵称 + 游戏角色 UID(与首页「今日数据」账号卡片、抽卡页 player_id 同一口径)。
    private List<LocalLauncherPlayer> _localPlayers = [];

    /// <summary>是否已经拿到本地角色列表(未拿到前不重写显示文案,避免闪烁成接口 ID)。</summary>
    private bool _localPlayersLoaded;

    /// <summary>
    /// 库街区卡片显示的「当前账号」文案:能匹配到本地游戏角色时为「游戏昵称 · 游戏角色 UID」。
    /// <para>与首页「今日数据」账号卡片、抽卡页显示的 player_id 同一口径;
    /// 匹配不到时退回接口账号文案,不编造游戏角色 ID。</para>
    /// </summary>
    public string KuroAccountDisplay => CurrentKuroAccountDisplay();

    /// <summary>云鸣潮卡片显示的「当前账号」文案(同上口径)。</summary>
    public string CloudAccountDisplay => CurrentCloudAccountDisplay();

    /// <summary>mcguide 卡片显示的「当前账号」文案(同上口径)。</summary>
    public string GuideAccountDisplay => CurrentGuideAccountDisplay();

    /// <summary>
    /// 库街区「当前账号」显示文案 = 列表当前项(已是游戏角色身份;未登录返回未登录文案)。
    /// </summary>
    private string CurrentKuroAccountDisplay()
        => AppServices.KuroAccounts.Current is null ? LanguageService.Format("Sign.NotLoggedIn") : AccountText;

    /// <summary>云鸣潮「当前账号」显示文案 = 云鸣潮列表当前项(已是游戏角色身份)。</summary>
    private string CurrentCloudAccountDisplay()
        => IsCloudLoggedIn
            ? CloudAccountItems.ElementAtOrDefault(CloudSelectedIndex())?.Display
                ?? (string.IsNullOrWhiteSpace(CloudAccountText) ? LanguageService.Format("Account.LoggedIn") : CloudAccountText)
            : LanguageService.Format("Sign.NotLoggedIn");

    /// <summary>
    /// mcguide「当前账号」显示文案:已登录且能匹配到本地游戏角色时为「游戏昵称 · 游戏角色 UID」,
    /// 否则退回接口账号名(不编造游戏角色 ID)。
    /// </summary>
    private string CurrentGuideAccountDisplay()
    {
        var s = AppServices.Settings.Current;
        if (!AppServices.Guide.HasToken)
        {
            return LanguageService.Format("Account.GuideNotLoggedIn");
        }
        return ResolveGameRole(phone: s.GuidePhone, userName: s.GuideCName, kuroUid: s.GuideCUid,
                // mcguide 明确记录了选定玩家(GuidePlayerId):同一手机号下有多个本地角色时按它选中正确的那个
                preferredRoleId: s.GuidePlayerId > 0 ? s.GuidePlayerId.ToString(CultureInfo.InvariantCulture) : null)
            is { } role
            ? LocalGameAccountLabel.Format(role.RoleName, role.RoleId)
            : LanguageService.Format("Account.GuideLoggedIn", s.GuideCName);
    }

    /// <summary>云鸣潮列表当前选中账号索引(仅内部定位用;UI 直接点列表行切换)。</summary>
    public int CloudSelectedIndex()
    {
        var currentId = AppServices.CloudGacha.SavedLoginId;
        return currentId.Length == 0 ? -1 : _cloudAccountKeys.IndexOf(currentId);
    }

    /// <summary>是否已保存云鸣潮账号(控制账号列表与「退出登录」显示;无账号时隐藏退出避免空操作)。</summary>
    public bool HasCloudAccounts => CloudAccountItems.Count > 0;

    /// <summary>是否存在当前库街区登录账号(当前账号身份文案显示用)。</summary>
    public bool HasKuroLogin => AppServices.KuroAccounts.Current is not null;

    /// <summary>是否已保存库街区账号(控制账号列表与「退出登录」显示;无账号时隐藏退出避免空操作)。</summary>
    public bool HasKuroAccounts => KuroAccountItems.Count > 0;

    // ---- 「退出登录」的多账号门禁:多账号必须先在列表中点选(退出点选的那个),单账号直接可退 ----
    // 点选目标单独记录(独立于"当前账号"):退出的是用户明确点中的账号,防止误退默认账号。

    /// <summary>库街区列表中被点选的 UserId(退出目标;null=未点选)。</summary>
    private string? _kuroPickedUserId;

    /// <summary>云鸣潮列表中被点选的稳定 Id(退出目标;null=未点选)。</summary>
    private string? _cloudPickedId;

    /// <summary>库街区「退出登录」是否可用:仅 1 个账号直接可退;多账号需先点选列表行。</summary>
    public bool CanLogoutKuro
        => AccountLogoutGate.CanLogout(KuroAccountItems.Count, _kuroPickedUserId is not null);

    /// <summary>库街区「退出登录」的悬停提示(多账号未点选时说明原因)。</summary>
    public string LogoutKuroTip
        => CanLogoutKuro ? "" : LanguageService.Format("Account.LogoutPickFirst");

    /// <summary>云鸣潮「退出登录」是否可用(与库街区同款门禁)。</summary>
    public bool CanLogoutCloud
        => AccountLogoutGate.CanLogout(CloudAccountItems.Count, _cloudPickedId is not null);

    /// <summary>云鸣潮「退出登录」的悬停提示。</summary>
    public string LogoutCloudTip
        => CanLogoutCloud ? "" : LanguageService.Format("Account.LogoutPickFirst");

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

    /// <summary>
    /// 有接口登录态已失效(橙色提示,优先于"同一账号")。
    /// <para>会话过期后右上角必须显示需要重新登录,而不是继续说"均为同一账号"。</para>
    /// </summary>
    public bool SameAccountIsSessionExpired => SameAccountVerdict == SameAccountVerdict.SessionExpired;

    partial void OnSameAccountVerdictChanged(SameAccountVerdict value)
    {
        OnPropertyChanged(nameof(SameAccountIsSame));
        OnPropertyChanged(nameof(SameAccountIsDifferent));
        OnPropertyChanged(nameof(SameAccountIsUnknown));
        OnPropertyChanged(nameof(SameAccountIsSessionExpired));
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
        _cloudMobile = string.IsNullOrWhiteSpace(AppServices.CloudGacha.SavedLoginPhone) ? lastKuroMobile : AppServices.CloudGacha.SavedLoginPhone;
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

        // 各接口账号统一显示「游戏角色昵称 · 游戏角色 UID」(与首页账号卡片、抽卡页 player_id 同口径):
        // 构造时先用接口账号文案兜底,本地角色枚举完成后异步改写(见 RefreshGameRoleLabelsAsync)

        // 自动进行同一账号判定
        RefreshSameAccountAuto();
        SelectInitialLoginCard();
        _ = RefreshGameRoleLabelsAsync();
    }

    /// <summary>
    /// 导航到账号页时调用:并行校验三个接口的登录态是否过期,并补一次本地角色枚举(首次失败可重试)。
    /// <para>
    /// 每次进入页面都应重跑校验:否则"在别处退出登录/会话过期"后回到账号页仍显示绿点。
    /// 若上一次校验仍在进行,则只做"当前账号已变"时的重跑(见 <see cref="ValidateSessionsAsync"/>)。
    /// </para>
    /// </summary>
    public void OnNavigatedTo()
    {
        if (!_localPlayersLoaded)
        {
            _ = RefreshGameRoleLabelsAsync();
        }
        _ = ValidateSessionsAsync();
    }

    private bool _sessionValidating;
    /// <summary>正在进行的校验所针对的库街区账号 UserId(用于识别"校验期间换了账号")。</summary>
    private string? _validatingKuroUserId;
    /// <summary>校验期间账号发生变化时置位,使校验结束后对新账号再跑一轮。</summary>
    private bool _rescheduleValidation;

    /// <summary>
    /// 并行校验三个接口的登录态。不重入(并发 getGamer 易触发风控);
    /// 若校验期间用户切换了库街区账号,则结束后对新账号再校验一轮,避免拿旧账号结论显示新账号状态。
    /// </summary>
    private async Task ValidateSessionsAsync()
    {
        if (_sessionValidating)
        {
            if (AppServices.KuroAccounts.Current?.UserId != _validatingKuroUserId)
            {
                _rescheduleValidation = true;
            }
            return;
        }
        _sessionValidating = true;
        try
        {
            do
            {
                _rescheduleValidation = false;
                _validatingKuroUserId = AppServices.KuroAccounts.Current?.UserId;
                await Task.WhenAll(
                    ValidateKuroSessionAsync(),
                    ValidateCloudSessionAsync(),
                    ValidateGuideSessionAsync()).ConfigureAwait(true);
            }
            while (_rescheduleValidation);
        }
        finally
        {
            _sessionValidating = false;
            _validatingKuroUserId = null;
        }
    }

    /// <summary>
    /// 库街区:用当前账号 token 拉角色列表判定登录态(不自动删除账号,非破坏性)。
    /// <para>
    /// 账号页每次导航进入都会执行,使"登录已失效"在账号页就能看到,而不必等到角色页点同步。
    /// 判定结果记入 <see cref="_expiredKuroUserIds"/>,避免随后的一次 <see cref="RefreshAccounts"/>
    /// (页面重绘)把橙点刷回绿点。
    /// </para>
    /// </summary>
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
            // 校验期间可能已切到别的账号:失效集合按 UserId 记录仍然成立,
            // 但状态点 / 状态文案属于"当前账号",旧账号的结论不得覆盖新账号(否则 A 的过期提示挂在 B 上)
            var stillCurrent = AppServices.KuroAccounts.Current?.UserId == account.UserId;
            switch (AccountLoginStateLogic.ProbeKuroSession(gamer))
            {
                case AccountLoginStateLogic.KuroSessionProbe.Valid:
                    // 服务端确认有效:清除失效标记,橙点恢复绿点
                    ClearKuroSessionExpired(account.UserId);
                    if (stillCurrent)
                    {
                        // 状态文案若仍是本页写下的"登录已失效"提示,一并清掉(否则绿点配过期提示)
                        if (_kuroExpiredStatusText is not null && StatusText == _kuroExpiredStatusText)
                        {
                            StatusText = "";
                        }
                        _kuroExpiredStatusText = null;
                        // 重建列表:行内状态点(InterfaceAccountItem.State 只能初始化时赋值)与「同一账号」徽标随之更新
                        RefreshAccounts();
                        KuroLoginState = InterfaceLoginState.Ok;
                    }
                    return; // 有效,不打扰
                case AccountLoginStateLogic.KuroSessionProbe.Rejected:
                    // 服务端明确拒绝 → 橙点(异常登录);记入失效集合,页面重绘后仍是橙点。
                    _expiredKuroUserIds.Add(account.UserId);
                    if (stillCurrent)
                    {
                        // 重建列表:行内状态点转橙,「同一账号」徽标转为"需要重新登录"
                        RefreshAccounts();
                        KuroLoginState = InterfaceLoginState.Error;
                        StatusText = _kuroExpiredStatusText = LanguageService.Format(
                            "Account.KuroSessionExpired", gamer?.Msg ?? $"code={gamer?.Code}");
                    }
                    return;
                case AccountLoginStateLogic.KuroSessionProbe.Unreachable:
                default:
                    // 未拿到响应(网络失败/超时/响应体不可解析):无法判定 → 不改任何状态,
                    // 也不清失效标记,避免一次网络抖动把刚判定的「会话失效(橙)」刷回绿点。
                    // (等价于旧的「网络异常不改状态」行为。)
                    return;
            }
        }
        catch (Exception ex)
        {
            // 同上:已切到别的账号时不拿旧账号的失败覆盖状态文案
            if (AppServices.KuroAccounts.Current?.UserId == account.UserId)
            {
                StatusText = LanguageService.Format("Account.KuroCheckFailed", ex.Message);
            }
        }
    }

    /// <summary>最近一次写入状态栏的"库街区登录已失效"文案(校验转为有效时据此清除,不误清其他提示)。</summary>
    private string? _kuroExpiredStatusText;

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
            OnPropertyChanged(nameof(GuideAccountDisplay));
            RefreshSameAccountAuto();
        }
        else if (valid == true)
        {
            GuideLoginState = InterfaceLoginState.Ok;
            OnPropertyChanged(nameof(GuideAccountDisplay));
        }
        // valid == null(未登录/校验失败)不改状态,避免误报
    }

    private void RefreshAccounts()
    {
        var currentUserId = AppServices.KuroAccounts.Current?.UserId;
        KuroAccountItems.Clear();
        _accountOptionKeys.Clear();
        foreach (var account in AppServices.KuroAccounts.GetAccounts())
        {
            // 显示口径与首页「今日数据」账号卡片、抽卡页 player_id 一致:游戏角色昵称 · 游戏角色 UID。
            // 匹配不到本地角色时退回「库街区账号名 · 掩码手机号」(不编造游戏角色 ID)。
            // 同一库街区账号下有多个角色时,优先当前应用正在使用的角色(设置里的 RoleId)。
            var gameRole = ResolveGameRole(account.Mobile, userName: null, kuroUid: account.UserId,
                preferredRoleId: PreferredKuroRoleId(account));
            string display;
            string masked = "";
            if (gameRole is not null)
            {
                display = LocalGameAccountLabel.Format(gameRole.RoleName, gameRole.RoleId);
            }
            else
            {
                display = FallbackAccountOptionText(account, out masked);
            }
            // 列表逐账号显示状态点:当前账号看会话校验结论,其余账号一律灰点(未校验,不编造结论)
            var state = account.UserId == currentUserId
                ? AccountLoginStateLogic.ResolveKuroDot(currentUserId, IsKuroSessionKnownExpired(currentUserId))
                : InterfaceLoginState.NotLoggedIn;
            KuroAccountItems.Add(new InterfaceAccountItem
            {
                Display = display,
                SubText = masked,
                State = state,
                IsCurrent = account.UserId == currentUserId,
                // 点选态跨重绘保留(重绘不丢用户已点选的退出目标);目标被移除后自动失效
                IsSelected = _kuroPickedUserId == account.UserId,
            });
            _accountOptionKeys.Add(account.UserId);
        }
        if (_kuroPickedUserId is not null && !_accountOptionKeys.Contains(_kuroPickedUserId))
        {
            _kuroPickedUserId = null;
        }
        var current = AppServices.KuroAccounts.Current;
        AccountText = current is null
            ? LanguageService.Format("Sign.NotLoggedIn")
            : KuroAccountItems.ElementAtOrDefault(IndexByKey(_accountOptionKeys, current.UserId))?.Display ?? LanguageService.Format("Sign.NotLoggedIn");
        // 登录状态点:未登录 → 灰;已保存登录 → 默认绿(会话校验失败后再转橙)。
        // ⚠️ 不能无条件写 Ok:本方法会被 RefreshGameRoleLabelsAsync 等在页面加载时反复调用,
        // 若每次都重置为 Ok,就会把 ValidateKuroSessionAsync 刚判定的「会话失效(橙)」冲掉,
        // 表现为「账号页看不到失效提示,直到去角色页点同步才发现」。
        // 判定规则抽到 AccountLoginStateLogic.ResolveKuroDot(纯函数,有单测锁定)。
        KuroLoginState = AccountLoginStateLogic.ResolveKuroDot(
            current?.UserId, IsKuroSessionKnownExpired(current?.UserId));
        OnPropertyChanged(nameof(HasKuroLogin));
        OnPropertyChanged(nameof(HasKuroAccounts));
        OnPropertyChanged(nameof(KuroAccountDisplay));
        OnPropertyChanged(nameof(CanLogoutKuro));
        OnPropertyChanged(nameof(LogoutKuroTip));
        RefreshSameAccountAuto();
    }

    /// <summary>
    /// 已判定失效的库街区账号 UserId 集合。
    /// <para>校验失败后记入,使页面重绘(<see cref="RefreshAccounts"/>)不会把橙点刷回绿点;
    /// 该账号成功登录/切换为其他账号时清除。仅按 UserId 记,不含任何凭据。</para>
    /// </summary>
    private readonly HashSet<string> _expiredKuroUserIds = new(StringComparer.Ordinal);

    /// <summary>该库街区账号是否已被判定会话失效(橙点)。</summary>
    private bool IsKuroSessionKnownExpired(string? userId)
        => !string.IsNullOrEmpty(userId) && _expiredKuroUserIds.Contains(userId);

    /// <summary>清除某账号的失效标记(重新登录成功 / 主动切换后调用)。</summary>
    private void ClearKuroSessionExpired(string? userId)
    {
        if (!string.IsNullOrEmpty(userId))
        {
            _expiredKuroUserIds.Remove(userId);
        }
    }

    /// <summary>按键列表定位下标(找不到返回 -1)。</summary>
    private static int IndexByKey(List<string> keys, string key)
    {
        var index = keys.FindIndex(k => string.Equals(k, key, StringComparison.Ordinal));
        return index;
    }

    /// <summary>
    /// 库街区卡片的角色偏好:当前库街区账号<b>就是</b>应用正在使用的账号时,
    /// 用设置里的 RoleId(角色数据页/首页正在展示的那个角色)选中正确角色;
    /// 其他账号没有已知的角色偏好,返回 null 由匹配逻辑取第一个。
    /// </summary>
    private static string? PreferredKuroRoleId(KuroAccount account)
        => AppServices.KuroAccounts.Current?.UserId == account.UserId && !string.IsNullOrWhiteSpace(AppServices.Settings.Current.RoleId)
            ? AppServices.Settings.Current.RoleId
            : null;

    /// <summary>
    /// 匹配不到本地游戏角色时库街区账号的回退显示文案:
    /// 「昵称(或库街区 UID)」;掩码手机号作为次行小字输出(<paramref name="maskedMobile"/>)。
    /// </summary>
    private static string FallbackAccountOptionText(KuroAccount account, out string maskedMobile)
    {
        var name = string.IsNullOrEmpty(account.Nickname) ? account.UserId : account.Nickname;
        maskedMobile = string.IsNullOrEmpty(account.Mobile) ? "" : MaskMobile(account.Mobile);
        return name;
    }

    /// <summary>
    /// 把接口账号映射到本地启动器凭证里的游戏角色(按手机号 → 账号名 → 库街区 UID)。
    /// 本地列表尚未枚举完时返回 null,调用方退回接口自己的文案。
    /// </summary>
    private LocalLauncherPlayer? ResolveGameRole(string? phone, string? userName, string? kuroUid, string? preferredRoleId = null)
        => _localPlayersLoaded
            ? LocalGameAccountLabel.Match(_localPlayers, phone, userName, kuroUid, preferredRoleId)
            : null;

    /// <summary>
    /// 枚举本地启动器凭证对应的游戏角色并重建三张卡片的显示文案。
    /// 网络/未装启动器失败时静默保持接口文案(不阻塞页面,也不显示空行)。
    /// </summary>
    private async Task RefreshGameRoleLabelsAsync()
    {
        try
        {
            _localPlayers = await AppServices.LocalDaily.GetLocalPlayersCachedAsync().ConfigureAwait(true);
            _localPlayersLoaded = _localPlayers.Count > 0;
        }
        catch (Exception ex)
        {
            _loginLog?.LogWarning(ex, "枚举本地启动器角色失败,账号页保留接口账号文案");
            _localPlayersLoaded = false;
        }
        RefreshAccounts();
        RefreshCloudState();
        NotifyAccountDisplaysChanged();
    }

    /// <summary>
    /// 登录/切换账号后强制重新枚举本地角色再改写文案:
    /// 新登录的账号可能对应本地启动器里另一个游戏角色,沿用上一次的缓存会显示成上一个号的昵称。
    /// </summary>
    private void InvalidateLocalPlayersAndReload()
    {
        AppServices.LocalDaily.InvalidateLocalPlayersCache();
        _localPlayersLoaded = false;
        _ = RefreshGameRoleLabelsAsync();
    }

    private static string MaskMobile(string mobile)
        => mobile.Length == 11 ? $"{mobile[..3]}****{mobile[^4..]}" : mobile;

    private void RefreshCloudState()
    {
        // 重建云鸣潮账号列表:显示游戏角色身份(昵称 · 游戏角色 UID),与库街区卡片同一口径
        CloudAccountItems.Clear();
        _cloudAccountKeys.Clear();
        var accounts = AppServices.CloudGacha.GetAccounts();
        var currentId = AppServices.CloudGacha.SavedLoginId;
        for (var i = 0; i < accounts.Count; i++)
        {
            var account = accounts[i];
            var gameRole = ResolveGameRole(phone: account.Phone, userName: account.Name, kuroUid: null);
            var display = gameRole is not null
                ? LocalGameAccountLabel.Format(gameRole.RoleName, gameRole.RoleId)
                : (string.IsNullOrWhiteSpace(account.Name)
                    ? (string.IsNullOrEmpty(account.Phone) ? account.Id : MaskMobile(account.Phone))
                    : account.Name);
            var isCurrent = AppServices.CloudGacha.HasSavedLogin && account.Id == currentId;
            CloudAccountItems.Add(new InterfaceAccountItem
            {
                Display = display,
                SubText = string.IsNullOrEmpty(account.Phone) || account.Phone == display ? "" : MaskMobile(account.Phone),
                State = isCurrent ? InterfaceLoginState.Ok : InterfaceLoginState.NotLoggedIn,
                IsCurrent = isCurrent,
                // 点选态跨重绘保留;目标被移除后自动失效
                IsSelected = _cloudPickedId == account.Id,
            });
            // 当前项按稳定 Id 匹配(手机号可能为空,不能作匹配键)
            _cloudAccountKeys.Add(account.Id);
        }
        if (_cloudPickedId is not null && !_cloudAccountKeys.Contains(_cloudPickedId))
        {
            _cloudPickedId = null;
        }
        IsCloudLoggedIn = AppServices.CloudGacha.HasSavedLogin;
        CloudAccountText = IsCloudLoggedIn
            ? CloudAccountItems.ElementAtOrDefault(CloudSelectedIndex())?.Display ?? LanguageService.Format("Account.LoggedIn")
            : LanguageService.Format("Sign.NotLoggedIn");
        // 已保存登录先按绿点显示,会话校验失败再转橙点
        CloudLoginState = IsCloudLoggedIn ? InterfaceLoginState.Ok : InterfaceLoginState.NotLoggedIn;
        OnPropertyChanged(nameof(HasCloudAccounts));
        OnPropertyChanged(nameof(CloudAccountDisplay));
        OnPropertyChanged(nameof(CanLogoutCloud));
        OnPropertyChanged(nameof(LogoutCloudTip));
        RefreshSameAccountAuto();
    }

    /// <summary>三张卡片的账号身份文案都依赖本地角色列表,重枚举后统一通知刷新。</summary>
    private void NotifyAccountDisplaysChanged()
    {
        OnPropertyChanged(nameof(KuroAccountDisplay));
        OnPropertyChanged(nameof(CloudAccountDisplay));
        OnPropertyChanged(nameof(GuideAccountDisplay));
    }

    /// <summary>点击云鸣潮账号行:记录为退出目标 + 切换该接口的当前账号(抽卡同步/会话校验随当前账号切换)。</summary>
    public void SelectCloudAccountAt(int index)
    {
        var accounts = AppServices.CloudGacha.GetAccounts();
        if (index < 0 || index >= accounts.Count)
        {
            RefreshCloudState();
            return;
        }
        // 先记点选目标(退出登录用),再切换当前账号(点行即切换的既有语义)
        _cloudPickedId = accounts[index].Id;
        if (!AppServices.CloudGacha.SwitchToIndex(index))
        {
            // 切换失败(索引越界/列表已变):重建列表让行高亮回到实际当前账号
            RefreshCloudState();
            return;
        }
        RefreshCloudState();
        if (index >= 0 && index < CloudAccountItems.Count)
        {
            // 提示里也用游戏角色身份(与列表项同源),而不是接口账号名
            StatusText = LanguageService.Format("Account.Switched", CloudAccountItems[index].Display);
        }
        // 退出按钮的门禁随点选态变化(多账号未点选时禁用)
        OnPropertyChanged(nameof(CanLogoutCloud));
        OnPropertyChanged(nameof(LogoutCloudTip));
    }

    /// <summary>点击库街区账号行:记录为退出目标 + 切换该接口的当前账号。</summary>
    public void SelectKuroAccountAt(int index)
    {
        var accounts = AppServices.KuroAccounts.GetAccounts();
        if (index < 0 || index >= accounts.Count)
        {
            return;
        }
        // 先记点选目标(退出登录用),再切换当前账号
        _kuroPickedUserId = accounts[index].UserId;
        var account = accounts[index];
        var switched = AppServices.KuroAccounts.Current?.UserId != account.UserId;
        AppServices.KuroAccounts.Current = account;
        // 切到该账号视为"重新开始":清除上一次的失效判定,由本次会话校验重新确认
        ClearKuroSessionExpired(account.UserId);
        // 同步 token 到设置:角色数据页自动加载依赖 KujiequToken
        AppServices.Settings.Current.KujiequToken = account.Token;
        if (switched)
        {
            // 角色 ID 属于上一个账号:先清掉,避免角色页 / 导航栏头像拿"新 token + 旧角色 ID"
            // (SyncFirstRoleAsync 拉到新账号的角色后再写回)
            AppServices.Settings.Current.RoleId = "";
        }
        AppServices.Settings.Save();
        RefreshAccounts();
        StatusText = LanguageService.Format("Account.Switched", string.IsNullOrEmpty(account.Nickname) ? account.UserId : account.Nickname);
        // 退出按钮的门禁随点选态变化
        OnPropertyChanged(nameof(CanLogoutKuro));
        OnPropertyChanged(nameof(LogoutKuroTip));
        if (switched)
        {
            // 同步新账号的首个角色 ID,再通知角色数据页 / 签到页按新账号刷新(消息由 SyncFirstRoleAsync 发送)
            _ = SyncFirstRoleAsync(account);
        }
        else
        {
            // 点的仍是当前账号:角色 ID 不变,直接通知刷新
            WeakReferenceMessenger.Default.Send(new RolesRefreshRequestedMessage(account.UserId));
        }
        // 切换后立即校验新账号的会话(切到已失效的账号时,账号页马上显示橙点与提示)
        _ = ValidateSessionsAsync();
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
            var reused = AppServices.CloudGacha.SavedLoginPhone;
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
    /// 退出库街区登录(移除<b>点选</b>的账号;单账号即该账号本身)并切回未登录态。
    /// 门禁:无账号时按钮整体隐藏;多账号时必须先在列表中点选要退出的账号
    /// (退出的是点选项,不再有"默认退出当前账号"的歧义)。
    /// </summary>
    [RelayCommand]
    private void Logout()
    {
        if (!CanLogoutKuro)
        {
            StatusText = LanguageService.Format("Account.LogoutPickFirst");
            return;
        }
        // 退出目标 = 用户点选的行(与 CanLogoutKuro 的门禁同一口径);未点选时(单账号)退当前账号。
        // 此前固定删 Current:短信登录新账号后 Current 变成新号而高亮仍停在旧选择上,点退出删掉的是刚登录的号。
        var accounts = AppServices.KuroAccounts.GetAccounts();
        var targetUserId = _kuroPickedUserId
            ?? AppServices.KuroAccounts.Current?.UserId
            ?? (accounts.Count == 1 ? accounts[0].UserId : null);
        if (string.IsNullOrEmpty(targetUserId) || accounts.All(a => a.UserId != targetUserId))
        {
            // 无账号:按钮已隐藏,理论不可达;防御一次避免空操作
            return;
        }
        // 显示名也按退出目标取(不是当前账号那一行)
        var removedName = KuroAccountItems.ElementAtOrDefault(IndexByKey(_accountOptionKeys, targetUserId))?.Display ?? "";
        var wasCurrent = AppServices.KuroAccounts.Current?.UserId == targetUserId;
        AppServices.KuroAccounts.Remove(targetUserId);
        // 账号已移除:清掉失效标记,避免残留(重新登录同一 UserId 时不应继承旧结论)
        ClearKuroSessionExpired(targetUserId);
        if (_kuroPickedUserId == targetUserId)
        {
            _kuroPickedUserId = null;
        }
        // 设置里的 token / 角色 ID 跟随退出后的当前账号:否则角色页收到消息后继续用已移除账号的 token,
        // token 也一直留在磁盘上。退的是当前账号(Remove 后已无当前账号)→ 清空;
        // 退的是其他账号 → 当前账号不变,token 按它重设,角色 ID 保留。
        var settings = AppServices.Settings.Current;
        var newCurrent = AppServices.KuroAccounts.Current;
        if (newCurrent is null || wasCurrent)
        {
            settings.KujiequToken = "";
            settings.RoleId = "";
        }
        else
        {
            settings.KujiequToken = newCurrent.Token;
        }
        AppServices.Settings.Save();
        // 通知签到/角色等页同步登出态(与登录消息同源)
        WeakReferenceMessenger.Default.Send(new RolesRefreshRequestedMessage(targetUserId));
        RefreshAccounts();
        StatusText = string.IsNullOrEmpty(removedName)
            ? LanguageService.Format("Account.LoggedOut")
            : LanguageService.Format("Account.LoggedOutOf", removedName);
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
                                // 统一走 SafeUrl(只放行 http/https;本地回环验证页 http://127.0.0.1 可通过)
                                if (!SafeUrl.TryOpen(url))
                                {
                                    SmsStatusText = LanguageService.Format("Status.OpenWebFailed");
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
                        if (!SafeUrl.TryOpen(url))
                        {
                            // 抛给 GeetVerifyService:它会立即结束等待(返回 null),不必空等超时
                            throw new InvalidOperationException("无法打开系统浏览器: " + url);
                        }
                    }
                });
            if (string.IsNullOrEmpty(geeTestJson))
            {
                SmsStatusText = geetestWindow is null
                    ? LanguageService.Format("Account.GeetestCancelled")
                    : LanguageService.Format("Account.GeetestTimeout");
                _loginLog?.LogWarning("极验验证未完成或超时(返回 null),手机号: {Mobile}", MaskMobile(mobile));
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
            // 登录的是另一个账号:旧账号的角色 ID 不能沿用(SyncFirstRoleAsync 失败时也不留"新 token + 旧角色")
            var switchedAccount = AppServices.KuroAccounts.Current?.UserId != account.UserId;
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
            if (switchedAccount)
            {
                AppServices.Settings.Current.RoleId = "";
            }
            AppServices.Settings.Save();
            // 重新登录成功 → 清除该账号的失效标记(橙点恢复为绿点)
            ClearKuroSessionExpired(account.UserId);
            RefreshAccounts();
            SmsStatusText = LanguageService.Format("Account.LoginSuccessWith", note);
            StatusText = LanguageService.Format("Account.KuroLoginSuccess", note);

            // 一个接口账号登录成功 → 其他接口账号复用该手机号
            ReusePhoneAcrossLogins(mobile, source: "kuro");

            // 新账号可能对应本地启动器里另一个游戏角色:清缓存重枚举,显示游戏昵称 · 游戏角色 UID
            InvalidateLocalPlayersAndReload();

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

    /// <summary>
    /// 同步首个角色 ID 到设置并发送角色刷新消息(原签到页登录后行为;切换账号时同样调用)。
    /// <para>
    /// 已配置的角色 ID 仍属于该账号时保留(不覆盖用户在角色页选定的同账号其他角色),否则改为首个角色。
    /// await 返回时若当前账号已不是 <paramref name="account"/>(期间又切了号),不写设置也不发消息:
    /// 后一次切换会自己同步并通知。
    /// </para>
    /// </summary>
    private async Task SyncFirstRoleAsync(KuroAccount account)
    {
        try
        {
            var roles = await AppServices.Kuro.GetGamerAsync(account, (int)KuroGameType.Waves);
            if (AppServices.KuroAccounts.Current?.UserId != account.UserId)
            {
                return;
            }
            if (roles is { Code: 200, Data: not null })
            {
                var roleIds = roles.Data
                    .Select(r => r.RoleId)
                    .Where(id => !string.IsNullOrEmpty(id))
                    .Select(id => id!)
                    .ToList();
                var firstRoleId = roleIds.FirstOrDefault();
                var settings = AppServices.Settings.Current;
                if (!string.IsNullOrEmpty(firstRoleId) && !roleIds.Contains(settings.RoleId, StringComparer.Ordinal))
                {
                    settings.RoleId = firstRoleId;
                    AppServices.Settings.Save();
                }
            }
        }
        catch (Exception)
        {
            // 角色 ID 同步失败不阻塞登录流程(签到页刷新时会再次同步)
            if (AppServices.KuroAccounts.Current?.UserId != account.UserId)
            {
                return;
            }
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
                // 新账号可能对应本地启动器里另一个游戏角色:清缓存重枚举
                InvalidateLocalPlayersAndReload();
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

    /// <summary>
    /// 退出云鸣潮登录(从列表移除<b>点选</b>账号的持久化会话;其他已保存账号不受影响)。
    /// 门禁与库街区同款:多账号时必须先在列表中点选要退出的账号。
    /// </summary>
    [RelayCommand]
    private void CloudLogout()
    {
        if (!CanLogoutCloud)
        {
            StatusText = LanguageService.Format("Account.LogoutPickFirst");
            return;
        }
        var removedName = CloudAccountItems.ElementAtOrDefault(CloudSelectedIndex())?.Display ?? "";
        var removedId = AppServices.CloudGacha.SavedLoginId;
        AppServices.CloudGacha.Logout();
        if (_cloudPickedId == removedId)
        {
            _cloudPickedId = null;
        }
        RefreshCloudState();
        CloudStatusText = LanguageService.Format("Account.CloudLoggedOut");
        StatusText = string.IsNullOrEmpty(removedName)
            ? LanguageService.Format("Account.CloudLoggedOut")
            : LanguageService.Format("Account.LoggedOutOf", removedName);
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
                // 新账号可能对应本地启动器里另一个游戏角色:清缓存重枚举,再按游戏角色身份显示
                InvalidateLocalPlayersAndReload();
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
        OnPropertyChanged(nameof(GuideAccountDisplay));
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
            var cloudPhone = AppServices.CloudGacha.SavedLoginPhone;
            var guidePhone = AppServices.Settings.Current.GuidePhone;

            // 已登录的接口(附"是否已知会话失效";失效结论优先于"同一账号"暴露,
            // 否则登录过期后右上角仍显示"均为同一账号",用户看不到要重新登录 —— 用户反馈)
            var probes = new List<AccountLoginStateLogic.InterfaceProbe>();
            if (kuro is not null)
            {
                probes.Add(new AccountLoginStateLogic.InterfaceProbe(
                    LanguageService.Format("Account.IfKuro"), kuro.Mobile ?? "", IsKuroSessionKnownExpired(kuro.UserId)));
            }
            if (AppServices.CloudGacha.HasSavedLogin)
            {
                probes.Add(new AccountLoginStateLogic.InterfaceProbe(
                    LanguageService.Format("Account.IfCloud"), cloudPhone, CloudLoginState == InterfaceLoginState.Error));
            }
            if (AppServices.Guide.HasToken)
            {
                probes.Add(new AccountLoginStateLogic.InterfaceProbe(
                    "mcguide", guidePhone, GuideLoginState == InterfaceLoginState.Error));
            }

            var expired = AccountLoginStateLogic.ExpiredNames(probes);
            if (expired.Count > 0)
            {
                // 有接口失效:优先提示重新登录(不再显示"均为同一账号"掩盖问题)
                SameAccountVerdict = SameAccountVerdict.SessionExpired;
                SameAccountStatus = LanguageService.Format("Account.SessionExpiredInterfaces", string.Join(" / ", expired));
                return;
            }

            if (probes.Count <= 1)
            {
                SameAccountVerdict = SameAccountVerdict.Unknown;
                SameAccountStatus = probes.Count == 0
                    ? LanguageService.Format("Account.NoAccounts")
                    : LanguageService.Format("Account.OnlyOne", probes[0].Name);
                return;
            }

            var missing = probes.Where(p => string.IsNullOrWhiteSpace(p.Phone)).ToList();
            if (missing.Count > 0)
            {
                // 有接口没记录手机号,无法完整判定
                SameAccountVerdict = SameAccountVerdict.Unknown;
                SameAccountStatus = LanguageService.Format("Account.PhonesMissing", string.Join(" / ", missing.Select(m => m.Name)));
                return;
            }

            var verdict = AccountLoginStateLogic.ResolveVerdict(probes);
            SameAccountVerdict = verdict;
            SameAccountStatus = verdict == SameAccountVerdict.Same
                ? LanguageService.Format("Account.AllSame", probes.Count)
                : LanguageService.Format("Account.DifferentPhones");
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
