using System.Text.Json;
using System.Text.Json.Serialization;
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
[JsonSerializable(typeof(LauncherFunctionCode))]
public sealed partial class LauncherInfoJsonContext : JsonSerializerContext;

/// <summary>
/// 拉取官方启动器信息(封面轮播图 / 公告 / 新闻 / 活动 / 背景视频封面)。
/// 数据源为 Kuro 官方 gamestarter CDN(与 Haiyu 一致),按服务器渠道切换配置。
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

    private static readonly (string AppId, string AppKey, string GameId, string Language) Official =
        ("10003", "Y8xXrXk65DqFHEDgApn3cpK5lfczpFx5", "G152", "zh-Hans");

    private static readonly (string AppId, string AppKey, string GameId, string Language) Bilibili =
        ("10004", "j5GWFuUFlb8N31Wi2uS3ZAVHcb7ZGN7y", "G152", "zh-Hans");

    private static readonly (string AppId, string AppKey, string GameId, string Language) Global =
        ("50004", "obOHXFrFanqsaIEOmuKroCcbZkQRBC7c", "G153", "zh-Hant");

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

    private static (string AppId, string AppKey, string GameId, string Language) GetServerConfig(
        GameServerType serverType) => serverType switch
    {
        GameServerType.Bilibili => Bilibili,
        GameServerType.Global => Global,
        _ => Official, // 官服 / WeGame / Unknown 默认官服配置
    };

    /// <summary>拉取指定服务器的启动器信息;全部失败返回 null。</summary>
    public async Task<LauncherInfo?> GetLauncherInfoAsync(GameServerType serverType, CancellationToken ct = default)
    {
        var cfg = GetServerConfig(serverType);

        foreach (var host in Hosts)
        {
            var url = $"{host}/launcher/{cfg.AppId}_{cfg.AppKey}/{cfg.GameId}/information/{cfg.Language}.json" +
                      $"?_t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
            try
            {
                using var resp = await Http.GetAsync(url, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    continue;
                }

                await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                var info = await JsonSerializer.DeserializeAsync(
                    stream, LauncherInfoJsonContext.Default.LauncherInfo, ct).ConfigureAwait(false);
                if (info is not null)
                {
                    return info;
                }
            }
            catch (Exception)
            {
                // 尝试下一个 CDN
            }
        }

        return null;
    }

    /// <summary>
    /// 拉取指定服务器的启动器背景数据(宣传视频/首帧图/版本Logo)。
    /// 链路:index.json 取 functionCode.background 编码 → background 接口。
    /// 全部失败返回 null。
    /// </summary>
    public async Task<LauncherBackgroundData?> GetLauncherBackgroundAsync(
        GameServerType serverType, CancellationToken ct = default)
    {
        var cfg = GetServerConfig(serverType);

        foreach (var host in Hosts)
        {
            try
            {
                // 第一层:index.json → backgroundCode
                var indexUrl = $"{host}/launcher/launcher/{cfg.AppId}_{cfg.AppKey}/{cfg.GameId}/index.json";
                LauncherIndex? index = null;
                using (var indexResp = await Http.GetAsync(indexUrl, ct).ConfigureAwait(false))
                {
                    if (!indexResp.IsSuccessStatusCode)
                    {
                        continue;
                    }

                    await using var indexStream = await indexResp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                    index = await JsonSerializer.DeserializeAsync(
                        indexStream, LauncherInfoJsonContext.Default.LauncherIndex, ct).ConfigureAwait(false);
                }

                var code = index?.FunctionCode?.Background;
                if (string.IsNullOrWhiteSpace(code))
                {
                    continue;
                }

                // 第二层:background 接口
                var bgUrl = $"{host}/launcher/{cfg.AppId}_{cfg.AppKey}/{cfg.GameId}/background/{code}/{cfg.Language}.json";
                using var bgResp = await Http.GetAsync(bgUrl, ct).ConfigureAwait(false);
                if (!bgResp.IsSuccessStatusCode)
                {
                    continue;
                }

                await using var bgStream = await bgResp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                var background = await JsonSerializer.DeserializeAsync(
                    bgStream, LauncherInfoJsonContext.Default.LauncherBackgroundData, ct).ConfigureAwait(false);
                if (background is not null)
                {
                    return background;
                }
            }
            catch (Exception)
            {
                // 尝试下一个 CDN
            }
        }

        return null;
    }
}
