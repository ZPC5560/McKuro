using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using McKuro.Core.Models.CloudGame;

namespace McKuro.Core.Services.CloudGame;

/// <summary>
/// 云游戏(云鸣潮)服务:SDK 登录 → 云游戏登录 → 节点获取 → 启动/排队。
/// 端点与参数参考 Haiyu 的 WavesCloudGameService。
/// </summary>
public sealed class CloudGameService
{
    private const string SdkBaseUrl = "https://sdkapi.kurogame.com/";
    private const string CloudBaseUrl = "https://cloud-game-sh.aki-game.com/";
    private const string GachaApiUrl = "https://gmserver-api.aki-game2.com/gacha/record/query";

    private const string ClientId = "vvkewnskrxxwfo0yi61cy24l";
    private const string ClientSecret = "g9ej0i1jf3y68wchb0ncm266";
    private const string ChannelId = "211";
    private const string GameId = "G152";
    private const string ProductId = "A1493";
    private const string Pkg = "com.kurogame.mingchao";

    // mcguide 攻略站 SDK 参数(与云鸣潮同源登录,channelId=201 / productId=A1496 / h5 / sdk 1.2.3w)
    private const string GuideChannelId = "201";
    private const string GuideProductId = "A1496";
    private const string GuideSdkVersion = "1.2.3w";

    public const string CardPoolId = "5c13a63f85465e9fcc0f24d6efb15083";
    public const string ServerId = "76402e5b20be2c39f095a152090afddc";

    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/139.0.0.0 Safari/537.36 Edg/139.0.0.0";

    public const string WelinkTenantKey = "1853717215719854081";
    public const string WelinkGameId = "1853717365355843585600007";
    public const string WelinkClientVersion = "5.15.2.260605093408-wlweb-release";

    private readonly HttpClient _sdkClient;
    private readonly HttpClient _cloudClient;
    private readonly CloudNetworkSpeedTestService _speedTest;
    private readonly string _deviceId;

    public CloudNetworkSpeedTestService SpeedTest => _speedTest;

    public CloudGameService(HttpClient http, string deviceId)
    {
        _deviceId = string.IsNullOrEmpty(deviceId) ? Guid.NewGuid().ToString("N") : deviceId;
        _sdkClient = CreateClient(http, SdkBaseUrl);
        _cloudClient = CreateClient(http, CloudBaseUrl);
        _speedTest = new CloudNetworkSpeedTestService(http);
    }

    private static HttpClient CreateClient(HttpClient shared, string baseUrl)
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.GZip
                | System.Net.DecompressionMethods.Deflate
                | System.Net.DecompressionMethods.Brotli,
            UseCookies = false,
        };
        var client = new HttpClient(handler) { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        client.DefaultRequestHeaders.TryAddWithoutValidation("Kr-Ver", "1.9.0");
        return client;
    }

    // ---------------- SDK 登录 ----------------

    /// <summary>发送手机号验证码(云游戏 SDK)。</summary>
    public async Task<(CloudSendSMS? Result, string DeviceNum)> GetPhoneSMSAsync(string phone, CancellationToken ct = default)
    {
        var querys = GetClientData();
        querys.Add("phone", phone);
        var json = await PostFormAsync(_sdkClient, "sdkcom/v2/login/getPhoneCode.lg", querys, ct).ConfigureAwait(false);
        var result = JsonSerializer.Deserialize(json, CloudGameJsonContext.Default.CloudSendSMS);
        return (result, _deviceId);
    }

    /// <summary>手机号 + 验证码登录云游戏 SDK。</summary>
    public async Task<CloudApiResponse<CloudGameLoginData>?> LoginAsync(string phone, string code, CancellationToken ct = default)
    {
        var query = GetClientData();
        query.Add("deviceNum", _deviceId);
        query.Add("phone", phone);
        query.Add("code", code);
        var json = await PostFormAsync(_sdkClient, "sdkcom/v2/login/phoneCode.lg", query, ct).ConfigureAwait(false);
        var model = JsonSerializer.Deserialize(json, CloudGameJsonContext.Default.CloudApiResponseCloudGameLoginData);
        if (model?.Data is not null)
        {
            model.Data.LoginDid = _deviceId;
        }
        return model;
    }

    /// <summary>用 phoneToken 刷新登录态。</summary>
    public async Task<CloudApiResponse<PhoneTokenData>?> RefreshPhoneTokenAsync(
        CloudGameLoginData data, CancellationToken ct = default)
    {
        var querys = GetClientData();
        querys.Add("deviceNum", data.LoginDid ?? _deviceId);
        querys.Add("phone", data.Phone ?? "");
        querys.Add("token", data.PhoneToken ?? "");
        var json = await PostFormAsync(_sdkClient, "sdkcom/v2/login/phoneToken.lg", querys, ct).ConfigureAwait(false);
        return JsonSerializer.Deserialize(json, CloudGameJsonContext.Default.CloudApiResponsePhoneTokenData);
    }

    /// <summary>用 refreshToken 换 accessToken。</summary>
    public async Task<CloudApiResponse<AccessData>?> GetAccessTokenAsync(
        CloudGameLoginData data, string refreshPhoneToken, CancellationToken ct = default)
    {
        var query = GetClientData();
        query.Add("deviceNum", data.LoginDid ?? _deviceId);
        query.Add("code", refreshPhoneToken);
        query.Add("grant_type", "authorization_code");
        var json = await PostFormAsync(_sdkClient, "sdkcom/v2/auth/getToken.lg", query, ct).ConfigureAwait(false);
        return JsonSerializer.Deserialize(json, CloudGameJsonContext.Default.CloudApiResponseAccessData);
    }

    /// <summary>云游戏平台登录(EndLogin),获得平台 Token。</summary>
    public async Task<CloudApiResponse<EndLoginData>?> GetTokenAsync(
        CloudGameLoginData data, string accessToken, CancellationToken ct = default)
    {
        var req = new EndLoginRequest
        {
            Token = accessToken,
            LoginType = 1,
            UserId = data.Id.ToString(),
            UserName = data.Username ?? "",
            Platform = "web-pc",
            AppVersion = "1.0.6",
            DeviceId = data.LoginDid ?? _deviceId,
        };
        var json = JsonSerializer.Serialize(req, CloudGameJsonContext.Default.EndLoginRequest);
        var result = await PostJsonAsync(_cloudClient, "Login/Login", json, ct).ConfigureAwait(false);
        return JsonSerializer.Deserialize(result, CloudGameJsonContext.Default.CloudApiResponseEndLoginData);
    }

    /// <summary>完成完整登录流程,返回可用的云游戏会话。</summary>
    public async Task<CloudGameLoginSession?> LoginFullAsync(string phone, string code, CancellationToken ct = default)
    {
        var login = await LoginAsync(phone, code, ct).ConfigureAwait(false);
        if (login is not { Code: 0, Data: not null })
        {
            return null;
        }

        var session = new CloudGameLoginSession { OrginData = login.Data };

        var phoneToken = await RefreshPhoneTokenAsync(login.Data, ct).ConfigureAwait(false);
        if (phoneToken is not { Code: 0, Data: not null })
        {
            return null;
        }
        session.PhoneToken = phoneToken.Data;

        var access = await GetAccessTokenAsync(login.Data, phoneToken.Data.PhoneToken ?? "", ct).ConfigureAwait(false);
        if (access is not { Code: 0, Data: not null })
        {
            return null;
        }
        session.AccessData = access.Data;

        var endLogin = await GetTokenAsync(login.Data, access.Data.AccessToken ?? "", ct).ConfigureAwait(false);
        if (endLogin is not { Code: 0, Data: not null })
        {
            return null;
        }
        session.EndLoginData = endLogin.Data;
        return session;
    }

    // ---------------- 节点与启动 ----------------

    /// <summary>获取节点列表(自动测速并排序)。</summary>
    public async Task<CloudApiResponse<List<CloudGameNode>>?> GetPingGameNodeAsync(
        CloudGameLoginSession session, CancellationToken ct = default)
    {
        var pingNodes = await _speedTest.RunSpeedTestAsync(ct).ConfigureAwait(false);
        var nodeList = pingNodes
            .Select(x => new NodeList { Delay = x.Delay, NodeId = x.NodeId })
            .ToList();

        using var request = BuildClientData(session, "GamePlay/GetRegionToScore", HttpMethod.Post);
        request.Content = new StringContent(
            JsonSerializer.Serialize(nodeList, CloudGameJsonContext.Default.ListNodeList),
            Encoding.UTF8, "application/json");
        using var response = await _cloudClient.SendAsync(request, ct).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return JsonSerializer.Deserialize(json, CloudGameJsonContext.Default.CloudApiResponseListCloudGameNode);
    }

    /// <summary>启动云游戏(返回 0=直接启动,1712=进入排队)。</summary>
    public async Task<CloudApiResponse<CommStartReponse>?> CommonStartGameAsync(
        CloudGameLoginSession session,
        List<CloudGameNode> nodes,
        CloudGameNode node,
        StreamQualityOptions options,
        uint payType,
        CancellationToken ct = default)
    {
        var bizNodes = nodes
            .Where(n => n.NodeList is { Count: > 0 })
            .Select(n => new BizCloudNode
            {
                NodeId = n.NodeList![0].NodeId,
                Result = n.NodeList[0].Delay.ToString(),
            });
        var bizData = new CloudBizData(UserAgent, WelinkClientVersion, bizNodes);
        var bizString = JsonSerializer.Serialize(bizData, CloudGameJsonContext.Default.CloudBizData);

        var resolution = GetPreferredResolution(options.Width, options.Height);
        var model = new CommStartModel
        {
            NodeList = node.NodeList,
            PayType = (int)payType,
            ResourceData = new ResourceData
            {
                WlResourceData = new WlResourceData
                {
                    BizData = bizString,
                    BitRate = options.BitRate,
                    CmdLine = $"-CloudGamePlatform=Windows -fps={options.Fps} -Dpi={options.DPI} -DeviceScreenResolution={resolution} -Device=Windows -SkipSplash -IsWeb=1",
                    CodecType = options.CodecType,
                    Fps = options.Fps,
                    GameId = WelinkGameId,
                    Resolution = resolution,
                    TenantKey = WelinkTenantKey,
                    Version = "v1.0",
                },
            },
        };
        var json = JsonSerializer.Serialize(model, CloudGameJsonContext.Default.CommStartModel);
        var body = await PostJsonAsync(_cloudClient, "GamePlay/CommonStartGame", json, ct).ConfigureAwait(false);
        return JsonSerializer.Deserialize(body, CloudGameJsonContext.Default.CloudApiResponseCommStartReponse);
    }

    /// <summary>查询排队状态。</summary>
    public async Task<CloudApiResponse<CommonQueueInfo>?> CommonQueueInfoAsync(
        CloudGameLoginSession session, CancellationToken ct = default)
    {
        using var request = BuildClientData(session, "GamePlay/CommonQueueInfo", HttpMethod.Get);
        using var response = await _cloudClient.SendAsync(request, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return JsonSerializer.Deserialize(body, CloudGameJsonContext.Default.CloudApiResponseCommonQueueInfo);
    }

    /// <summary>取消排队。</summary>
    public async Task CancelQueueAsync(CloudGameLoginSession session, CancellationToken ct = default)
    {
        using var request = BuildClientData(session, "GamePlay/CancelQueue", HttpMethod.Get);
        using var response = await _cloudClient.SendAsync(request, ct).ConfigureAwait(false);
    }

    /// <summary>获取云游戏抽卡记录信息。</summary>
    public async Task<CloudApiResponse<RecordData>?> GetRecordAsync(CloudGameLoginSession session, CancellationToken ct = default)
    {
        using var request = BuildClientData(session, "Message/GameRecordInfo", HttpMethod.Get);
        using var response = await _cloudClient.SendAsync(request, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return JsonSerializer.Deserialize(body, CloudGameJsonContext.Default.CloudApiResponseRecordData);
    }

    /// <summary>查询云游戏抽卡记录明细。</summary>
    public async Task<PlayerReponse?> GetGameRecordResourceAsync(
        string recordId, string userId, int poolType, CancellationToken ct = default)
    {
        var query = new RecardQuery
        {
            CardPoolId = CardPoolId,
            RecordId = recordId,
            LanguageCode = "zh-Hans",
            PlayerId = userId,
            CardPoolType = poolType,
            ServerId = ServerId,
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, GachaApiUrl);
        request.Content = new StringContent(
            JsonSerializer.Serialize(query, CloudGameJsonContext.Default.RecardQuery),
            Encoding.UTF8, "application/json");
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        using var response = await _cloudClient.SendAsync(request, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return JsonSerializer.Deserialize(body, CloudGameJsonContext.Default.PlayerReponse);
    }

    private Dictionary<string, string> GetClientData()
    {
        return new Dictionary<string, string>
        {
            { "redirect_uri", "1" },
            { "__e__", "1" },
            { "pack_mark", "1" },
            { "projectId", GameId },
            { "productId", ProductId },
            { "channelId", ChannelId },
            { "version", "2.1.2" },
            { "sdkVersion", "2.1.2" },
            { "response_type", "code" },
            { "client_id", ClientId },
            { "deviceModel", "Chrome" },
            { "os", "Windows" },
            { "pkg", Pkg },
            { "client_secret", ClientSecret },
            { "platform", "h5" },
            { "deviceNum", _deviceId },
        };
    }

    // ---------------- mcguide 攻略站登录(同源 SDK,参数不同) ----------------

    /// <summary>mcguide SDK 公共参数(对齐抓包:channelId=201 / productId=A1496 / h5 / 1.2.3w)。</summary>
    private Dictionary<string, string> GetGuideClientData()
    {
        return new Dictionary<string, string>
        {
            { "redirect_uri", "1" },
            { "__e__", "1" },
            { "pack_mark", "1" },
            { "projectId", GameId },
            { "productId", GuideProductId },
            { "platform", "h5" },
            { "channelId", GuideChannelId },
            { "deviceNum", _deviceId },
            { "version", GuideSdkVersion },
            { "sdkVersion", GuideSdkVersion },
            { "response_type", "code" },
            { "client_id", ClientId },
            { "deviceModel", "Firefox" },
            { "os", "Windows" },
        };
    }

    /// <summary>发送手机号验证码(mcguide SDK)。</summary>
    public async Task<(CloudSendSMS? Result, string DeviceNum)> GetGuidePhoneSMSAsync(string phone, CancellationToken ct = default)
    {
        var querys = GetGuideClientData();
        querys.Add("phone", phone);
        var json = await PostFormAsync(_sdkClient, "sdkcom/v2/login/getPhoneCode.lg", querys, ct).ConfigureAwait(false);
        var result = JsonSerializer.Deserialize(json, CloudGameJsonContext.Default.CloudSendSMS);
        return (result, _deviceId);
    }

    /// <summary>手机号 + 验证码登录 mcguide SDK。</summary>
    public async Task<CloudApiResponse<CloudGameLoginData>?> LoginGuideAsync(string phone, string code, CancellationToken ct = default)
    {
        var query = GetGuideClientData();
        query.Add("phone", phone);
        query.Add("code", code);
        var json = await PostFormAsync(_sdkClient, "sdkcom/v2/login/phoneCode.lg", query, ct).ConfigureAwait(false);
        var model = JsonSerializer.Deserialize(json, CloudGameJsonContext.Default.CloudApiResponseCloudGameLoginData);
        if (model?.Data is not null)
        {
            model.Data.LoginDid = _deviceId;
        }
        return model;
    }

    /// <summary>用登录返回的授权码(code)换 access_token(mcguide SDK)。</summary>
    public async Task<CloudApiResponse<AccessData>?> GetGuideAccessTokenAsync(CloudGameLoginData data, string authCode, CancellationToken ct = default)
    {
        var query = GetGuideClientData();
        query.Add("code", authCode);
        query.Add("grant_type", "authorization_code");
        query.Add("client_secret", ClientSecret);
        var json = await PostFormAsync(_sdkClient, "sdkcom/v2/auth/getToken.lg", query, ct).ConfigureAwait(false);
        return JsonSerializer.Deserialize(json, CloudGameJsonContext.Default.CloudApiResponseAccessData);
    }

    private HttpRequestMessage BuildClientData(CloudGameLoginSession session, string path, HttpMethod method)
    {
        var message = new HttpRequestMessage(method, path);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));
        message.Headers.AcceptLanguage.ParseAdd("zh-CN,zh;q=0.9");
        message.Headers.Referrer = new Uri("https://mc.kurogames.com/cloud/index.html");
        message.Headers.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Code/1.120.0 Chrome/142.0.7444.265 Electron/39.8.8 Safari/537.36");
        message.Headers.TryAddWithoutValidation("Origin", "https://mc.kurogames.com");
        message.Headers.TryAddWithoutValidation("Cookie", BuildCookieHeader(session));
        message.Headers.TryAddWithoutValidation("x-os", "web");
        message.Headers.TryAddWithoutValidation("x-token", session.EndLoginData?.Token ?? "");
        message.Headers.TryAddWithoutValidation("x-b3-traceid", session.TraceId);
        return message;
    }

    private static string BuildCookieHeader(CloudGameLoginSession session)
    {
        var items = new List<string>();
        if (session.EndLoginData?.Token is { Length: > 0 } t)
        {
            items.Add($"token={t}");
        }
        if (!string.IsNullOrWhiteSpace(session.OrginData?.AutoToken))
        {
            items.Add($"autoToken={session.OrginData.AutoToken}");
        }
        if (!string.IsNullOrWhiteSpace(session.OrginData?.PhoneToken))
        {
            items.Add($"phoneToken={session.OrginData.PhoneToken}");
        }
        if (!string.IsNullOrWhiteSpace(session.OrginData?.Username))
        {
            items.Add($"username={session.OrginData.Username}");
        }
        return string.Join("; ", items);
    }

    private static string GetPreferredResolution(int maxWidth, int maxHeight)
    {
        var width = ClampEven(maxWidth, 1280, 1920);
        var height = ClampEven(maxHeight, 720, 1080);
        return $"{width}x{height}";
    }

    private static int ClampEven(int value, int minValue, int maxValue)
    {
        var clamped = Math.Max(minValue, Math.Min(maxValue, value));
        return clamped % 2 == 0 ? clamped : Math.Max(minValue, clamped - 1);
    }

    private static async Task<string> PostFormAsync(HttpClient client, string path, Dictionary<string, string> values, CancellationToken ct)
    {
        using var content = new FormUrlEncodedContent(values);
        using var response = await client.PostAsync(path, content, ct).ConfigureAwait(false);
        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    private static async Task<string> PostJsonAsync(HttpClient client, string path, string payload, CancellationToken ct)
    {
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(path, content, ct).ConfigureAwait(false);
        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }
}
