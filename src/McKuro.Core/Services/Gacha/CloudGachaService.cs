using System.Text.Json;
using McKuro.Core.Models.CloudGame;
using McKuro.Core.Models.Gacha;
using McKuro.Core.Services.CloudGame;
using McKuro.Core.Services.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace McKuro.Core.Services.Gacha;

/// <summary>云鸣潮登录状态。</summary>
public enum CloudGachaStatus
{
    NotLoggedIn,
    LoginFailed,
    FetchFailed,
    Success,
}

/// <summary>云鸣潮抽卡同步结果。</summary>
public sealed class CloudGachaResult
{
    public required CloudGachaStatus Status { get; init; }
    public string? Message { get; init; }
    public GachaSyncResult? Sync { get; init; }
    public bool IsSuccess => Status == CloudGachaStatus.Success;
}

/// <summary>
/// 云鸣潮抽卡记录同步服务:
/// 通过云鸣潮(token 会话)拉取抽卡记录,复用 GachaSyncService 合并/分析流水线。
/// 支持多账号:登录数据持久化为账号列表(静默续期,免重复输入验证码),
/// 当前账号由 <c>CurrentCloudPhone</c> 指定,续期轮换的 phoneToken 回存到当前账号条目。
/// </summary>
public sealed class CloudGachaService
{
    private readonly CloudGameService _cloud;
    private readonly IGachaSyncService _sync;
    private readonly ISettingsService _settings;
    private readonly ILogger<CloudGachaService> _logger;

    public CloudGachaService(
        CloudGameService cloud,
        IGachaSyncService sync,
        ISettingsService settings,
        ILogger<CloudGachaService>? logger = null)
    {
        _cloud = cloud;
        _sync = sync;
        _settings = settings;
        _logger = logger ?? NullLogger<CloudGachaService>.Instance;
    }

    /// <summary>所有已保存的云鸣潮账号。</summary>
    public IReadOnlyList<CloudAccount> GetAccounts() => _settings.Current.CloudAccounts;

    /// <summary>
    /// 当前云鸣潮账号(按稳定 Id 定位;Id 缺失/失效时回退第一个已保存账号并落盘)。
    /// 手机号允许为空(旧数据迁移),故不能作定位键,只用于显示与同账号判定。
    /// </summary>
    private CloudAccount? CurrentAccount
    {
        get
        {
            var accounts = _settings.Current.CloudAccounts;
            if (accounts.Count == 0)
            {
                return null;
            }
            var id = _settings.Current.CurrentCloudAccountId;
            var account = accounts.FirstOrDefault(a => a.Id == id);
            if (account is not null)
            {
                return account;
            }
            // 指针失效:回退第一个并落盘(与 KuroAccountService 同款兜底)
            var fallback = accounts[0];
            _settings.Current.CurrentCloudAccountId = fallback.Id;
            _settings.Save();
            return fallback;
        }
    }

    /// <summary>是否已保存云鸣潮登录数据(可尝试静默续会话)。</summary>
    public bool HasSavedLogin => !string.IsNullOrWhiteSpace(CurrentAccount?.LoginDataJson);

    /// <summary>当前云鸣潮账号名。</summary>
    public string SavedLoginName => CurrentAccount?.Name ?? "";

    /// <summary>当前云鸣潮账号手机号(登录表单复用与同账号判定用;可能为空)。</summary>
    public string SavedLoginPhone => CurrentAccount?.Phone ?? "";

    /// <summary>当前云鸣潮账号稳定 Id(账号页下拉定位当前项用)。</summary>
    public string SavedLoginId => CurrentAccount?.Id ?? "";

    /// <summary>按列表索引切换当前云鸣潮账号(账号页下拉用;越界返回 false)。</summary>
    public bool SwitchToIndex(int index)
    {
        var accounts = _settings.Current.CloudAccounts;
        if (index < 0 || index >= accounts.Count)
        {
            return false;
        }
        _settings.Current.CurrentCloudAccountId = accounts[index].Id;
        _settings.Save();
        return true;
    }

    /// <summary>发送云鸣潮登录验证码(手机号)。</summary>
    public async Task<(bool Ok, string? Message)> SendSmsAsync(string phone, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return (false, CoreStrings.T("Account.FillPhone", "请填写手机号"));
        }
        var (result, _) = await _cloud.GetPhoneSMSAsync(phone.Trim(), ct).ConfigureAwait(false);
        return result is not null ? (true, CoreStrings.T("Account.CodeSent", "验证码已发送")) : (false, CoreStrings.T("Account.SendFailedShort", "发送验证码失败"));
    }

    /// <summary>
    /// 云鸣潮手机号登录(支持多账号:同手机号更新旧条目,新手机号追加为新账号并切为当前;
    /// SDK 登录成功即持久化会话,续期留给同步时执行)。
    /// </summary>
    public async Task<(bool Ok, string? Message)> LoginAsync(string phone, string code, CancellationToken ct = default)
    {
        var login = await _cloud.LoginAsync(phone.Trim(), code.Trim(), ct).ConfigureAwait(false);
        if (login is not { Code: 0, Data: not null })
        {
            var msg = login?.Msg;
            return (false, CoreStrings.F("Account.LoginFailed", $"登录失败: {msg}", msg));
        }
        // 持久化登录数据到账号列表(以手机号为键 upsert),并切为当前账号
        var trimmed = phone.Trim();
        var dataJson = JsonSerializer.Serialize(login.Data, CloudGameJsonContext.Default.CloudGameLoginData);
        var name = login.Data.Username ?? login.Data.Phone ?? "";
        var accounts = _settings.Current.CloudAccounts;
        var existing = accounts.FirstOrDefault(a => a.Phone == trimmed);
        if (existing is not null)
        {
            existing.Name = name;
            existing.LoginDataJson = dataJson;
        }
        else
        {
            // Id 用手机号(登录必有手机号,天然唯一);空手机号的历史条目由迁移生成 GUID,不会与之冲突
            accounts.Add(new CloudAccount { Id = trimmed, Phone = trimmed, Name = name, LoginDataJson = dataJson });
        }
        var s = _settings.Current;
        s.CurrentCloudAccountId = (existing ?? accounts[^1]).Id;
        _settings.Save();
        return (true, CoreStrings.F("Core.Cloud.LoggedInAs", $"已登录云鸣潮: {name}", name));
    }

    /// <summary>退出当前云鸣潮账号(从列表移除该账号的持久化会话;其他已保存账号不受影响)。</summary>
    public void Logout()
    {
        var current = CurrentAccount;
        var s = _settings.Current;
        if (current is not null)
        {
            s.CloudAccounts.RemoveAll(a => a.Id == current.Id);
        }
        s.CurrentCloudAccountId = "";
        s.CloudLoginDataJson = "";
        s.CloudLoginName = "";
        s.CloudLoginPhone = "";
        _settings.Save();
    }

    /// <summary>
    /// 校验云鸣潮会话是否仍可静默续期(账号页加载时调用,不做抽卡同步)。
    /// 返回 status: NotLoggedIn=未登录, Success=会话有效, LoginFailed=已失效, FetchFailed=网络等临时失败(不判定过期)。
    /// </summary>
    public async Task<(CloudGachaStatus Status, string? Message)> ValidateSessionAsync(CancellationToken ct = default)
    {
        var account = CurrentAccount;
        var json = account?.LoginDataJson;
        if (string.IsNullOrWhiteSpace(json))
        {
            return (CloudGachaStatus.NotLoggedIn, CoreStrings.T("Core.Cloud.NotLoggedIn", "未登录云鸣潮"));
        }
        try
        {
            var data = JsonSerializer.Deserialize(json, CloudGameJsonContext.Default.CloudGameLoginData);
            if (data is null)
            {
                return (CloudGachaStatus.LoginFailed, CoreStrings.T("Core.Cloud.DataInvalid", "云鸣潮登录数据无效,请重新登录"));
            }
            // 静默续期一次:成功即证明会话仍有效(与同步走同一条续期链路)
            var session = await _cloud.BuildSessionAsync(data, ct).ConfigureAwait(false);
            if (session is null)
            {
                return (CloudGachaStatus.LoginFailed, CoreStrings.T("Account.CloudSessionExpired", "云鸣潮会话已失效,请重新登录"));
            }
            PersistRenewedToken(account!, data, session.PhoneToken);
            return (CloudGachaStatus.Success, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "云鸣潮会话校验失败(不判定过期)");
            return (CloudGachaStatus.FetchFailed, CoreStrings.F("Core.Guide.SessionCheckFailed", $"会话校验失败: {ex.Message}", ex.Message));
        }
    }

    /// <summary>
    /// 把续期后轮换的新 phoneToken 回存到当前账号的持久化登录数据。
    /// phoneToken.lg 每次续期都会签发新 token(旧 token 随之失效);不回存的话,
    /// 下一次续期仍带旧 token 会被判"会话已失效",实际登录却仍是有效状态。
    /// </summary>
    private void PersistRenewedToken(CloudAccount account, CloudGameLoginData data, PhoneTokenData? refreshed)
    {
        var newToken = refreshed?.PhoneToken;
        if (string.IsNullOrWhiteSpace(newToken) || string.Equals(data.PhoneToken, newToken, StringComparison.Ordinal))
        {
            return;
        }
        data.PhoneToken = newToken;
        try
        {
            account.LoginDataJson = JsonSerializer.Serialize(data, CloudGameJsonContext.Default.CloudGameLoginData);
            _settings.Save();
            _logger.LogInformation("已回存云鸣潮轮换 phoneToken(账号: {Name})", account.Name);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "回存云鸣潮轮换 phoneToken 失败");
        }
    }

    /// <summary>拉取云鸣潮抽卡记录并同步;未登录/失败返回对应状态。</summary>
    public async Task<CloudGachaResult> SyncFromCloudAsync(CancellationToken ct = default)
    {
        var account = CurrentAccount;
        var json = account?.LoginDataJson;
        if (string.IsNullOrWhiteSpace(json))
        {
            return new CloudGachaResult { Status = CloudGachaStatus.NotLoggedIn, Message = CoreStrings.T("Core.Cloud.NotLoggedIn", "未登录云鸣潮") };
        }

        try
        {
            var data = JsonSerializer.Deserialize(json, CloudGameJsonContext.Default.CloudGameLoginData);
            if (data is null)
            {
                return new CloudGachaResult { Status = CloudGachaStatus.LoginFailed, Message = CoreStrings.T("Core.Cloud.DataInvalidShort", "云鸣潮登录数据无效") };
            }

            // 静默续会话
            var session = await _cloud.BuildSessionAsync(data, ct).ConfigureAwait(false);
            if (session is null)
            {
                return new CloudGachaResult { Status = CloudGachaStatus.LoginFailed, Message = CoreStrings.T("Core.Cloud.RenewFailed", "云鸣潮会话续期失败(可能已失效,请重新登录)") };
            }
            PersistRenewedToken(account!, data, session.PhoneToken);

            // 拿 recordId/playerId
            var record = await _cloud.GetRecordAsync(session, ct).ConfigureAwait(false);
            if (record?.Data is not { RecordId: { Length: > 0 } recordId })
            {
                return new CloudGachaResult { Status = CloudGachaStatus.FetchFailed, Message = CoreStrings.T("Core.Cloud.FetchInfoFailed", "获取抽卡记录信息失败") };
            }

            // 构造请求 → 复用 GachaSyncService 流水线(gmserver-api 明细查询 + 合并 + 分析)
            var request = new GachaRecordRequest
            {
                PlayerId = record.Data.PlayerId.ToString(),
                RecordId = recordId,
                CardPoolId = CloudGameService.CardPoolId,
                ServerId = CloudGameService.ServerId,
            };
            var sync = await _sync.SyncAsync(request, null, ct).ConfigureAwait(false);
            if (!sync.IsSuccess)
            {
                return new CloudGachaResult { Status = CloudGachaStatus.FetchFailed, Message = sync.Message ?? "同步失败" };
            }
            return new CloudGachaResult { Status = CloudGachaStatus.Success, Sync = sync };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "云鸣潮抽卡同步失败");
            return new CloudGachaResult { Status = CloudGachaStatus.FetchFailed, Message = ex.Message };
        }
    }
}
