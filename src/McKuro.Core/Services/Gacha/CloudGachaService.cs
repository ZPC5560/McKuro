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
/// 会话登录数据持久化到 Settings(静默续期,免重复输入验证码)。
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

    /// <summary>是否已保存云鸣潮登录数据(可尝试静默续会话)。</summary>
    public bool HasSavedLogin => !string.IsNullOrWhiteSpace(_settings.Current.CloudLoginDataJson);

    /// <summary>已保存的云鸣潮账号名。</summary>
    public string SavedLoginName => _settings.Current.CloudLoginName ?? "";

    /// <summary>发送云鸣潮登录验证码(手机号)。</summary>
    public async Task<(bool Ok, string? Message)> SendSmsAsync(string phone, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return (false, "请填写手机号");
        }
        var (result, _) = await _cloud.GetPhoneSMSAsync(phone.Trim(), ct).ConfigureAwait(false);
        return result is not null ? (true, "验证码已发送") : (false, "发送验证码失败");
    }

    /// <summary>云鸣潮手机号登录(SDK 登录成功即持久化会话,续期留给同步时执行)。</summary>
    public async Task<(bool Ok, string? Message)> LoginAsync(string phone, string code, CancellationToken ct = default)
    {
        var login = await _cloud.LoginAsync(phone.Trim(), code.Trim(), ct).ConfigureAwait(false);
        if (login is not { Code: 0, Data: not null })
        {
            var msg = login?.Msg;
            return (false, $"登录失败: {msg}");
        }
        // 持久化登录数据 + 账号名
        var s = _settings.Current;
        s.CloudLoginDataJson = JsonSerializer.Serialize(login.Data, CloudGameJsonContext.Default.CloudGameLoginData);
        s.CloudLoginName = login.Data.Username ?? login.Data.Phone ?? "";
        _settings.Save();
        return (true, $"已登录云鸣潮: {s.CloudLoginName}");
    }

    /// <summary>退出云鸣潮登录(清除持久化会话)。</summary>
    public void Logout()
    {
        var s = _settings.Current;
        s.CloudLoginDataJson = "";
        s.CloudLoginName = "";
        _settings.Save();
    }

    /// <summary>拉取云鸣潮抽卡记录并同步;未登录/失败返回对应状态。</summary>
    public async Task<CloudGachaResult> SyncFromCloudAsync(CancellationToken ct = default)
    {
        var json = _settings.Current.CloudLoginDataJson;
        if (string.IsNullOrWhiteSpace(json))
        {
            return new CloudGachaResult { Status = CloudGachaStatus.NotLoggedIn, Message = "未登录云鸣潮" };
        }

        try
        {
            var data = JsonSerializer.Deserialize(json, CloudGameJsonContext.Default.CloudGameLoginData);
            if (data is null)
            {
                return new CloudGachaResult { Status = CloudGachaStatus.LoginFailed, Message = "云鸣潮登录数据无效" };
            }

            // 静默续会话
            var session = await _cloud.BuildSessionAsync(data, ct).ConfigureAwait(false);
            if (session is null)
            {
                return new CloudGachaResult { Status = CloudGachaStatus.LoginFailed, Message = "云鸣潮会话续期失败(可能已失效,请重新登录)" };
            }

            // 拿 recordId/playerId
            var record = await _cloud.GetRecordAsync(session, ct).ConfigureAwait(false);
            if (record?.Data is not { RecordId: { Length: > 0 } recordId })
            {
                return new CloudGachaResult { Status = CloudGachaStatus.FetchFailed, Message = "获取抽卡记录信息失败" };
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
