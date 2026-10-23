using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using McKuro.Core.Models.Game;
using McKuro.Core.Services.Game;

namespace McKuro.Core.Services.Launcher;

/// <summary>启动器信息 JSON 源生成上下文(AOT 安全)。</summary>
[JsonSerializable(typeof(LauncherInfo))]
[JsonSerializable(typeof(Guidance))]
[JsonSerializable(typeof(AnnouncementGroup))]
[JsonSerializable(typeof(AnnouncementItem))]
[JsonSerializable(typeof(SlideshowItem))]
[JsonSerializable(typeof(LauncherBackgroundData))]
[JsonSerializable(typeof(LauncherIndex))]
[JsonSerializable(typeof(LauncherIndexConfig))]
[JsonSerializable(typeof(LauncherFunctionCode))]
public sealed partial class LauncherInfoJsonContext : JsonSerializerContext;

/// <summary>
/// 拉取官方启动器信息(封面轮播图 / 公告 / 新闻 / 活动 / 背景视频封面)。
/// 数据源为 Kuro 官方 gamestarter CDN(与 Haiyu 一致),按服务器渠道切换配置。
/// <para>
/// <b>两代接口</b>:启动器 3.0 改版后内容搬到了 <c>/launcher/app/{appId}_{appKey}/{gameId}/{serverCode}/…</c>
/// (配置里带 serverCode,如 official),旧地址 <c>/launcher/{appId}_{appKey}/{gameId}/information/…</c>
/// 仍在但<b>已停更</b> —— 实测旧地址只有 2 张轮播,新地址有 4 张(改版后官方启动器读的就是新地址),
/// 公告/活动/新闻也一起变全(旧地址 3 条,新地址 7 条)。
/// 因此新接口优先、旧接口兜底:将来接口再改版或新渠道没配 key 时,旧接口还能给出降级数据而不是空白。
/// </para>
/// </summary>
public sealed class LauncherInfoService
{
    private static readonly string[] Hosts =
    [
        "https://prod-cn-alicdn-gamestarter.kurogame.com",
        "https://prod-volcdn-gamestarter.kurogame.xyz",
        "https://prod-alicdn-gamestarter.kurogame.com",
        "https://prod-tencentcdn-gamestarter.kurogame.com",
    ];

    /// <summary>
    /// 单个渠道的 CDN 配置。
    /// <para>
    /// <c>AppKey</c>/<c>ServerCode</c> 是新接口用到的;新 key 与旧 key <b>不同</b>
    /// (旧 key 拼新路径一律 404)。新 key 来自各渠道启动器包里的 KRApp.conf
    /// (base64(XOR(data,0x63)) 解出来就是启动器完整配置,含 appId/appKey/games[].serverCode):
    /// 官服 10003 / B站 10004 / 国际服 50004 各一份,三者的 serverCode 都写在各自包的 games[].serverCode 里
    /// (国际服包写的也是 official,不是 global —— 别按渠道名猜)。
    /// </para>
    /// </summary>
    private readonly record struct ChannelConfig(
        string AppId,
        string AppKey,
        string GameId,
        string ServerCode,
        string LegacyAppId,
        string LegacyAppKey)
    {
        /// <summary>是否已知新接口 appKey(未知则只能走旧接口)。</summary>
        public bool HasNewApi => !string.IsNullOrWhiteSpace(AppKey) && !string.IsNullOrWhiteSpace(ServerCode);
    }

    private static readonly ChannelConfig Official = new(
        AppId: "10003",
        AppKey: "oLNgHF1CESo51DGHN2odtp40e3oI1HfZ",
        GameId: "G152",
        ServerCode: "official",
        LegacyAppId: "10003",
        LegacyAppKey: "Y8xXrXk65DqFHEDgApn3cpK5lfczpFx5");

    private static readonly ChannelConfig Bilibili = new(
        AppId: "10004",
        AppKey: "IY6xmlAusbImBjWrpPj1OwNuirXEO44x",
        GameId: "G152",
        ServerCode: "bilibili",
        LegacyAppId: "10004",
        LegacyAppKey: "j5GWFuUFlb8N31Wi2uS3ZAVHcb7ZGN7y");

    private static readonly ChannelConfig Global = new(
        AppId: "50004",
        AppKey: "P7xcUZnEr1AXIGON25E6KjpOgTlVrg6e",
        GameId: "G153",
        ServerCode: "official",
        LegacyAppId: "50004",
        LegacyAppKey: "obOHXFrFanqsaIEOmuKroCcbZkQRBC7c");

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(8),
        };
        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(12),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("McKuro-launcher/1.0");
        return client;
    }

    private static ChannelConfig GetServerConfig(GameServerType serverType) => serverType switch
    {
        GameServerType.Bilibili => Bilibili,
        GameServerType.Global => Global,
        _ => Official, // 官服 / WeGame / Unknown 默认官服配置
    };

    /// <summary>该渠道是否已知新接口配置(官方启动器 3.0 改版后的 /launcher/app 路径)。</summary>
    public static bool HasNewApi(GameServerType serverType) => GetServerConfig(serverType).HasNewApi;

    /// <summary>启动器内容语言包名:国际服(G153)按界面语言取 en(英文)或 zh-Hant(繁中,实测仅此两种);
    /// 国服/B站服只有 zh-Hans 语言包。语言包不存在时调用方的多主机重试与兜底逻辑负责回退。</summary>
    private static string GetLanguage(GameServerType serverType) => serverType switch
    {
        GameServerType.Global => CoreStrings.CurrentLanguage == "en-US" ? "en" : "zh-Hant",
        _ => "zh-Hans",
    };

    // ---------- URL 构造(生产判定点:新增渠道/接口改版只需改这里,单测直接断言字符串) ----------

    /// <summary>启动器信息 URL(该渠道已知新接口时用新接口,否则用旧接口)。</summary>
    public static string BuildInformationUrl(
        string host, GameServerType serverType, string language, long unixSeconds)
        => HasNewApi(serverType)
            ? BuildNewInformationUrl(host, serverType, language, unixSeconds)
            : BuildLegacyInformationUrl(host, serverType, language, unixSeconds);

    /// <summary>新接口信息 URL:<c>/launcher/app/{appId}_{appKey}/{gameId}/{serverCode}/information/{lang}.json</c>。</summary>
    public static string BuildNewInformationUrl(
        string host, GameServerType serverType, string language, long unixSeconds)
    {
        var cfg = GetServerConfig(serverType);
        return $"{host}/launcher/app/{cfg.AppId}_{cfg.AppKey}/{cfg.GameId}/{cfg.ServerCode}" +
               $"/information/{language}.json?_t={unixSeconds}";
    }

    /// <summary>旧接口信息 URL:<c>/launcher/{appId}_{appKey}/{gameId}/information/{lang}.json</c>(已停更,仅兜底)。</summary>
    public static string BuildLegacyInformationUrl(
        string host, GameServerType serverType, string language, long unixSeconds)
    {
        var cfg = GetServerConfig(serverType);
        return $"{host}/launcher/{cfg.LegacyAppId}_{cfg.LegacyAppKey}/{cfg.GameId}" +
               $"/information/{language}.json?_t={unixSeconds}";
    }

    /// <summary>
    /// 游戏配置 index.json URL。
    /// 新接口:<c>/launcher/game/{appId}_{appKey}/{gameId}/{serverCode}/index.json</c>;
    /// 旧接口:<c>/launcher/launcher/{appId}_{appKey}/{gameId}/index.json</c>(两者不是同一个文件)。
    /// </summary>
    public static string BuildGameIndexUrl(string host, GameServerType serverType, bool useNewApi)
    {
        var cfg = GetServerConfig(serverType);
        return useNewApi
            ? $"{host}/launcher/game/{cfg.AppId}_{cfg.AppKey}/{cfg.GameId}/{cfg.ServerCode}/index.json"
            : $"{host}/launcher/launcher/{cfg.LegacyAppId}_{cfg.LegacyAppKey}/{cfg.GameId}/index.json";
    }

    /// <summary>背景数据 URL(新/旧接口路径不同:新接口少了 launcher 层并多一个 serverCode)。</summary>
    public static string BuildBackgroundUrl(
        string host, GameServerType serverType, string code, string language, bool useNewApi)
    {
        var cfg = GetServerConfig(serverType);
        return useNewApi
            ? $"{host}/launcher/game/{cfg.AppId}_{cfg.AppKey}/{cfg.GameId}/{cfg.ServerCode}/background/{code}/{language}.json"
            : $"{host}/launcher/{cfg.LegacyAppId}_{cfg.LegacyAppKey}/{cfg.GameId}/background/{code}/{language}.json";
    }

    // ---------- 拉取 ----------

    /// <summary>拉取指定服务器的启动器信息;全部失败返回 null。</summary>
    public async Task<LauncherInfo?> GetLauncherInfoAsync(GameServerType serverType, CancellationToken ct = default)
    {
        var language = GetLanguage(serverType);
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // 新接口优先:旧地址虽仍可用但已停更(轮播图只剩 2 张,新地址是 4 张)
        if (HasNewApi(serverType))
        {
            foreach (var host in Hosts)
            {
                var info = await TryGetJsonAsync(
                    BuildNewInformationUrl(host, serverType, language, ts),
                    LauncherInfoJsonContext.Default.LauncherInfo, ct).ConfigureAwait(false);
                // 新接口偶发返回空壳(轮播与公告都空):当成失败继续找,别把页面清空
                if (info is not null && HasContent(info))
                {
                    return info;
                }
            }
        }

        // 旧接口兜底
        foreach (var host in Hosts)
        {
            var info = await TryGetJsonAsync(
                BuildLegacyInformationUrl(host, serverType, language, ts),
                LauncherInfoJsonContext.Default.LauncherInfo, ct).ConfigureAwait(false);
            if (info is not null)
            {
                return info;
            }
        }

        return null;
    }

    /// <summary>信息里是否真有内容(轮播图或任一公告分组有条目)。</summary>
    private static bool HasContent(LauncherInfo info)
    {
        if (info.Slideshow is { Count: > 0 })
        {
            return true;
        }
        var g = info.Guidance;
        return g is not null
               && ((g.Notice?.Contents?.Count ?? 0) > 0
                   || (g.Activity?.Contents?.Count ?? 0) > 0
                   || (g.News?.Contents?.Count ?? 0) > 0);
    }

    /// <summary>
    /// 拉取指定服务器的启动器背景数据(宣传视频/首帧图/版本Logo)。
    /// 链路:index.json 取 functionCode.background 编码 → background 接口;新接口优先、旧接口兜底。
    /// 全部失败返回 null。
    /// </summary>
    public async Task<LauncherBackgroundData?> GetLauncherBackgroundAsync(
        GameServerType serverType, CancellationToken ct = default)
    {
        var language = GetLanguage(serverType);

        if (HasNewApi(serverType))
        {
            foreach (var host in Hosts)
            {
                var index = await TryGetJsonAsync(
                    BuildGameIndexUrl(host, serverType, useNewApi: true),
                    LauncherInfoJsonContext.Default.LauncherIndex, ct).ConfigureAwait(false);
                var code = index?.ResolveBackgroundCode();
                if (string.IsNullOrWhiteSpace(code))
                {
                    continue;
                }

                var background = await TryGetJsonAsync(
                    BuildBackgroundUrl(host, serverType, code, language, useNewApi: true),
                    LauncherInfoJsonContext.Default.LauncherBackgroundData, ct).ConfigureAwait(false);
                if (background is not null)
                {
                    return background;
                }
            }
        }

        // 旧接口兜底
        foreach (var host in Hosts)
        {
            var index = await TryGetJsonAsync(
                BuildGameIndexUrl(host, serverType, useNewApi: false),
                LauncherInfoJsonContext.Default.LauncherIndex, ct).ConfigureAwait(false);
            var code = index?.ResolveBackgroundCode();
            if (string.IsNullOrWhiteSpace(code))
            {
                continue;
            }

            var background = await TryGetJsonAsync(
                BuildBackgroundUrl(host, serverType, code, language, useNewApi: false),
                LauncherInfoJsonContext.Default.LauncherBackgroundData, ct).ConfigureAwait(false);
            if (background is not null)
            {
                return background;
            }
        }

        return null;
    }

    /// <summary>GET + JSON 反序列化(失败/非 2xx 一律返回 null,由调用方换主机或换接口)。</summary>
    private static async Task<T?> TryGetJsonAsync<T>(
        string url, JsonTypeInfo<T> typeInfo, CancellationToken ct) where T : class
    {
        try
        {
            using var resp = await Http.GetAsync(url, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync(stream, typeInfo, ct).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // 换下一个 CDN / 下一套接口
            return null;
        }
    }
}
