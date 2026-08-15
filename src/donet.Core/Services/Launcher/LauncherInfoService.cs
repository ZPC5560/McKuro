using System.Text.Json;
using System.Text.Json.Serialization;
using donet.Core.Models.Game;
using donet.Core.Services.Game;

namespace donet.Core.Services.Launcher;

/// <summary>启动器信息 JSON 源生成上下文(AOT 安全)。</summary>
[JsonSerializable(typeof(LauncherInfo))]
[JsonSerializable(typeof(Guidance))]
[JsonSerializable(typeof(AnnouncementGroup))]
[JsonSerializable(typeof(AnnouncementItem))]
[JsonSerializable(typeof(SlideshowItem))]
public sealed partial class LauncherInfoJsonContext : JsonSerializerContext;

/// <summary>
/// 拉取官方启动器信息(封面轮播图 / 公告 / 新闻 / 活动)。
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
        client.DefaultRequestHeaders.UserAgent.ParseAdd("donet-launcher/1.0");
        return client;
    }

    /// <summary>拉取指定服务器的启动器信息;全部失败返回 null。</summary>
    public async Task<LauncherInfo?> GetLauncherInfoAsync(GameServerType serverType, CancellationToken ct = default)
    {
        var cfg = serverType switch
        {
            GameServerType.Bilibili => Bilibili,
            GameServerType.Global => Global,
            _ => Official, // 官服 / WeGame / Unknown 默认官服配置
        };

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
}
