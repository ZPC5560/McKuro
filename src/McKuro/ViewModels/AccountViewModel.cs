using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using McKuro.Services;

namespace McKuro.ViewModels;

/// <summary>
/// 账号页:角色 ID / 库街区账号多账号切换与移除 / mcguide 官方评级登录。
/// 由设置页的账号相关区块迁移而来。
/// </summary>
public sealed partial class AccountViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _roleId;

    [ObservableProperty]
    private string _statusText = "";

    // 库街区账号
    [ObservableProperty]
    private string _accountText = "未登录";

    [ObservableProperty]
    private int _selectedAccountIndex = -1;

    public ObservableCollection<string> AccountOptions { get; } = [];

    // mcguide 官方评级登录
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

    // 验证码重发倒计时(60s,对齐 SignViewModel smsCooldown)
    private readonly DispatcherTimer _smsTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    /// <summary>是否已登录 mcguide 攻略站。</summary>
    public bool GuideLoggedIn => AppServices.Guide.HasToken;

    /// <summary>发送验证码按钮文案(倒计时中显示剩余秒数)。</summary>
    public string GuideSmsButtonText => GuideSmsCountdown > 0 ? $"重新发送 ({GuideSmsCountdown}s)" : "发送验证码";

    /// <summary>发送验证码按钮可用。</summary>
    public bool CanSendGuideSms => !GuideSmsSending && GuideSmsCountdown <= 0;

    partial void OnGuideSmsCountdownChanged(int value)
    {
        OnPropertyChanged(nameof(GuideSmsButtonText));
        OnPropertyChanged(nameof(CanSendGuideSms));
    }

    partial void OnGuideSmsSendingChanged(bool value) => OnPropertyChanged(nameof(CanSendGuideSms));

    public AccountViewModel()
    {
        var s = AppServices.Settings.Current;
        _roleId = s.RoleId;
        RefreshAccounts();
        _guideStatusText = AppServices.Guide.HasToken
            ? $"已登录: {AppServices.Settings.Current.GuideCName}"
            : "未登录(角色页将隐藏官方评级)";
    }

    private void RefreshAccounts()
    {
        AccountOptions.Clear();
        foreach (var account in AppServices.KuroAccounts.GetAccounts())
        {
            var name = string.IsNullOrEmpty(account.Nickname) ? account.UserId : account.Nickname;
            AccountOptions.Add($"{name} (ID: {account.UserId})");
        }
        var current = AppServices.KuroAccounts.Current;
        AccountText = current is null ? "未登录" : AccountOptions.FirstOrDefault(o => o.Contains(current.UserId)) ?? "未登录";
        SelectedAccountIndex = current is null ? -1 : Math.Max(0, AccountOptions.ToList().FindIndex(o => o.Contains(current.UserId)));
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
        // 通知角色数据页按新账号刷新
        WeakReferenceMessenger.Default.Send(new RolesRefreshRequestedMessage(account.UserId));
    }

    /// <summary>跳转「签到」页登录新库街区账号。</summary>
    [RelayCommand]
    private void GoAddAccount()
        => WeakReferenceMessenger.Default.Send(new NavigationRequestedMessage(NavigationKeys.Sign));

    [RelayCommand]
    private void RemoveAccount()
    {
        var current = AppServices.KuroAccounts.Current;
        if (current is null)
        {
            return;
        }
        AppServices.KuroAccounts.Remove(current.UserId);
        RefreshAccounts();
        StatusText = "已移除账号";
    }

    /// <summary>发送 mcguide 官方评级登录验证码。</summary>
    [RelayCommand]
    private async Task SendGuideSmsAsync()
    {
        var mobile = GuideMobile.Trim();
        if (string.IsNullOrWhiteSpace(mobile))
        {
            GuideSmsText = "请填写手机号";
            return;
        }
        if (GuideSmsSending || GuideSmsCountdown > 0)
        {
            return;
        }

        GuideSmsSending = true;
        GuideSmsText = "正在发送…";
        try
        {
            var (ok, msg) = await AppServices.Guide.SendSmsAsync(mobile);
            GuideSmsText = msg ?? (ok ? "验证码已发送" : "发送失败");
            if (ok)
            {
                StartSmsCountdown(60);
            }
        }
        catch (Exception ex)
        {
            GuideSmsText = $"发送失败: {ex.Message}";
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
            GuideStatusText = "请填写手机号与验证码";
            return;
        }

        GuideStatusText = "正在登录攻略站…";
        try
        {
            var (ok, msg) = await AppServices.Guide.LoginAsync(mobile, code);
            GuideStatusText = msg ?? (ok ? "登录成功" : "登录失败");
            if (ok)
            {
                GuideCode = "";
                OnPropertyChanged(nameof(GuideLoggedIn));
            }
        }
        catch (Exception ex)
        {
            GuideStatusText = $"登录失败: {ex.Message}";
        }
    }

    /// <summary>保存角色 ID 到设置。</summary>
    [RelayCommand]
    private void Save()
    {
        var s = AppServices.Settings.Current;
        s.RoleId = RoleId;
        AppServices.Settings.Save();
        StatusText = "设置已保存";
    }

    /// <summary>启动验证码重发倒计时(默认 60 秒)。</summary>
    private void StartSmsCountdown(int seconds = 60)
    {
        GuideSmsCountdown = seconds;
        _smsTimer.Tick -= OnSmsTick;
        _smsTimer.Tick += OnSmsTick;
        _smsTimer.Start();
    }

    private void OnSmsTick(object? sender, EventArgs e)
    {
        if (GuideSmsCountdown <= 1)
        {
            _smsTimer.Stop();
            GuideSmsCountdown = 0;
        }
        else
        {
            GuideSmsCountdown--;
        }
    }
}
