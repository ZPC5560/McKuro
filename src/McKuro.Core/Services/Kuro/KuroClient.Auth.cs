using System.Globalization;
using System.Text.Json;
using McKuro.Core.Models.Kuro;

namespace McKuro.Core.Services.Kuro;

// KuroClient 的账号与登录部分(partial)。
// 短信登录对齐 Haiyu 的 LoginGameViewModel 流程:
//   1. 用户通过极验(GeeTest,gt4.js)完成人机验证,得到 validate JSON
//   2. POST /user/getSmsCode  (form: mobile + geeTestData) 发送验证码
//   3. POST /user/sdkLogin    (form: mobile + devCode + code) 登录,返回 token
// 同一设备 ID(devCode)贯穿发码与登录。

public sealed partial class KuroClient
{
    /// <summary>我的主页信息(校验 token 是否有效)。</summary>
    public async Task<AccountMine?> GetWavesMineAsync(KuroAccount account, CancellationToken ct = default)
    {
        if (!long.TryParse(account.UserId, out var userId))
        {
            return null;
        }
        var json = await SendAndReadAsync(
            BaseUrl + "/user/mineV2",
            GetDeviceHeader(account),
            new Dictionary<string, string> { { "otherUserId", userId.ToString(CultureInfo.InvariantCulture) } },
            ct).ConfigureAwait(false);
        return JsonSerializer.Deserialize(json, KuroJsonContext.Default.AccountMine);
    }

    /// <summary>校验登录态。</summary>
    public async Task<bool> IsLoginAsync(KuroAccount account, CancellationToken ct = default)
    {
        var mine = await GetWavesMineAsync(account, ct).ConfigureAwait(false);
        return mine is { Code: 200 };
    }

    /// <summary>生成随机设备标识(32 位 hex)。</summary>
    public static string NewDeviceId() => Guid.NewGuid().ToString("N");

    /// <summary>登录/发码公共请求头(Android 客户端风格,对齐 Haiyu BuildLoginRequest)。</summary>
    private static Dictionary<string, string> LoginHeaders(string devCode) => new()
    {
        { "osVersion", "Android" },
        { "devCode", devCode },
        { "distinct_id", "e0f62c50-4c62-4983-9f6a-bf96f3566095" },
        { "countryCode", "CN" },
        { "model", "23127PN0CC" },
        { "source", "android" },
        { "lang", "zh-Hans" },
        { "version", "3.1.2" },
        { "channelId", "2" },
        { "Accept-Encoding", "gzip" },
        { "User-Agent", "okhttp/3.11.0" },
    };

    /// <summary>发送手机号登录验证码(geeTestData 为极验 validate JSON,对齐 Haiyu SendSMSAsync)。</summary>
    public async Task<SMSResultModel?> SendSMSAsync(string mobile, string geeTestData, string deviceId, CancellationToken ct = default)
    {
        using var request = BuildPost(
            BaseUrl + "/user/getSmsCode",
            LoginHeaders(deviceId),
            new Dictionary<string, string>
            {
                { "mobile", mobile },
                { "geeTestData", geeTestData },
            });
        using var response = await _inner.SendAsync(request, ct).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return JsonSerializer.Deserialize(json, KuroJsonContext.Default.SMSResultModel);
    }

    /// <summary>手机号 + 验证码登录(对齐 Haiyu LoginAsync:sdkLogin + devCode)。</summary>
    public async Task<AccountModel?> LoginAsync(string mobile, string code, string deviceId, CancellationToken ct = default)
    {
        using var request = BuildPost(
            BaseUrl + "/user/sdkLogin",
            LoginHeaders(deviceId),
            new Dictionary<string, string>
            {
                { "mobile", mobile },
                { "devCode", deviceId },
                { "code", code },
            });
        using var response = await _inner.SendAsync(request, ct).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return JsonSerializer.Deserialize(json, KuroJsonContext.Default.AccountModel);
    }
}
