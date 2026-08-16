using System.Text.Json;
using McKuro.Core.Models.Kuro;

namespace McKuro.Core.Services.Kuro;

// KuroClient 的扫码登录部分(partial):云游戏 / 游戏内扫码。

public sealed partial class KuroClient
{
    /// <summary>提交游戏内扫码文本,查询待确认角色(扫码登录游戏第一步)。</summary>
    public async Task<ScanScreenModel?> PostQrValueAsync(KuroAccount account, string qrText, CancellationToken ct = default)
    {
        var json = await SendAndReadAsync(
            BaseUrl + "/user/auth/roleInfos",
            GetDeviceHeader(account),
            new Dictionary<string, string> { { "qrCode", qrText } },
            ct).ConfigureAwait(false);
        return JsonSerializer.Deserialize(json, KuroJsonContext.Default.ScanScreenModel);
    }

    /// <summary>确认扫码登录(扫码登录游戏第二步)。</summary>
    public async Task<QRLoginResult?> QRLoginAsync(
        KuroAccount account, string qrText, string verifyCode, string id, CancellationToken ct = default)
    {
        var json = await SendAndReadAsync(
            BaseUrl + "/user/auth/scanLogin",
            GetDeviceHeader(account),
            new Dictionary<string, string>
            {
                { "autoLogin", "true" },
                { "qrCode", qrText },
                { "id", id },
                { "verifyCode", verifyCode },
            },
            ct).ConfigureAwait(false);
        return JsonSerializer.Deserialize(json, KuroJsonContext.Default.QRLoginResult);
    }

    /// <summary>扫码登录需要短信验证时发送验证码。</summary>
    public async Task<SMSModel?> GetQrCodeAsync(KuroAccount account, string qrCode, CancellationToken ct = default)
    {
        var json = await SendAndReadAsync(
            BaseUrl + "/user/sms/scanSms",
            GetDeviceHeader(account),
            new Dictionary<string, string> { { "geeTestData", "" } },
            ct).ConfigureAwait(false);
        return JsonSerializer.Deserialize(json, KuroJsonContext.Default.SMSModel);
    }
}
