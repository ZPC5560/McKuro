using System.Diagnostics;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using McKuro.Core.Models.CloudGame;

namespace McKuro.Core.Services.CloudGame;

/// <summary>
/// 云游戏节点测速服务:从 vlinkcloud 拉取节点列表,对每个节点做 TCP 连接延迟测量。
/// 参考 Haiyu 的 CloudNetworkSpeedTestService(WebSocket 测速简化为 TCP 握手延迟)。
/// </summary>
public sealed class CloudNetworkSpeedTestService
{
    public const string DefaultBaseUrl = "https://paas-sdk-config.vlinkcloud.cn";
    public const string FallbackBaseUrl = "https://paas-sdk-config-ks.vlinkcloud.cn";

    private readonly HttpClient _http;
    private readonly string _tenantKey;

    public CloudNetworkSpeedTestService(HttpClient http, string tenantKey = "1853717215719854081")
    {
        _http = http;
        _tenantKey = tenantKey;
    }

    /// <summary>拉取节点列表并按延迟排序(失败返回空列表)。</summary>
    public async Task<List<CloudNetworkDelayItem>> RunSpeedTestAsync(CancellationToken ct = default)
    {
        try
        {
            var config = await GetNodeListAsync(DefaultBaseUrl, ct).ConfigureAwait(false)
                ?? await GetNodeListAsync(FallbackBaseUrl, ct).ConfigureAwait(false);
            if (config?.Lines is not { Count: > 0 })
            {
                return [];
            }

            var nodes = config.Lines;
            var results = new List<CloudNetworkDelayItem>();
            var parallelism = Math.Min(3, Math.Max(1, nodes.Count));

            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = parallelism,
                CancellationToken = ct,
            };
            await Parallel.ForEachAsync(nodes, options, async (node, token) =>
            {
                var delay = await PingSingleNodeAsync(node, token).ConfigureAwait(false);
                if (delay.HasValue)
                {
                    lock (results)
                    {
                        results.Add(new CloudNetworkDelayItem
                        {
                            NodeId = node.NodeId,
                            NodeName = node.NodeName,
                            Addr = node.LineH5Addr,
                            Port = node.LineH5Port,
                            Delay = delay.Value,
                        });
                    }
                }
            }).ConfigureAwait(false);

            return results
                .OrderBy(x => x.Delay)
                .DistinctBy(x => x.NodeId)
                .ToList();
        }
        catch (Exception)
        {
            return [];
        }
    }

    private async Task<CloudNetworkOrgin?> GetNodeListAsync(string baseUrl, CancellationToken ct)
    {
        try
        {
            var hash = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(_tenantKey + "H5"))).ToLowerInvariant();
            var url = $"{baseUrl}/ping/{hash}.html?t={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return JsonSerializer.Deserialize(json, CloudGameJsonContext.Default.CloudNetworkOrgin);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static async Task<int?> PingSingleNodeAsync(CloudNetworkOrginItem node, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(node.LineH5Addr))
        {
            return null;
        }
        try
        {
            var port = int.TryParse(node.LineH5Port, out var p) ? p : 443;
            var sw = Stopwatch.StartNew();
            using var client = new TcpClient();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromMilliseconds(1000));
            await client.ConnectAsync(node.LineH5Addr!, port, timeout.Token).ConfigureAwait(false);
            sw.Stop();
            return (int)sw.ElapsedMilliseconds;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
