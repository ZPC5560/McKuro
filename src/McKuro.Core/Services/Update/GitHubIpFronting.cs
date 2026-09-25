using System.Net;
using System.Net.Sockets;

namespace McKuro.Core.Services.Update;

/// <summary>
/// GitHub IP 域前置(参考 Haiyu 的 <c>SocketHttpFactory.CreateGithubHandler</c> + <c>GithubIpSettings</c>)。
/// <para>
/// 场景:部分网络对 github.com / api.github.com / *.githubusercontent.com 存在 DNS 污染或解析不可达,
/// 表现为"检查更新一直转圈/超时",而实际 IP 直连是通的。本类把远端 IP 表注入
/// <see cref="SocketsHttpHandler.ConnectCallback"/>,绕过系统 DNS 直接连接指定 IP(仍走 TLS 且
/// SNI/Host 头保持原域名,证书校验不受影响)。
/// </para>
/// <para>
/// 与 Haiyu 的差异:①IP 表内置为常量并提供别名解析(githubusercontent 通配 + release-assets 等互备),
/// 不依赖用户先配置;②仅在 <see cref="Enabled"/> 为真时生效,默认关闭 —— 域前置拿到的是**固定 IP**,
/// GitHub 换 IP 后会失效,所以默认走系统 DNS,只有用户遇到解析问题时才手动开启更安全。
/// </para>
/// </summary>
public static class GitHubIpFronting
{
    /// <summary>.githubusercontent.com 通配键(与 Haiyu 的 <c>GitHubUserContentHost</c> 一致)。</summary>
    private const string UserContentWildcard = "*.githubusercontent.com";

    /// <summary>
    /// 内置 IP 表(host → IP 列表)。取自 Haiyu 的 <c>DefaultGithubIpJson</c> 默认值,
    /// 含 github.com / api.github.com / codeload / githubusercontent 系 / githubassets。
    /// </summary>
    private static readonly Dictionary<string, string[]> Builtin = new(StringComparer.OrdinalIgnoreCase)
    {
        ["github.com"] = ["20.205.243.166", "140.82.112.3", "140.82.113.3", "140.82.114.3", "140.82.121.3"],
        ["api.github.com"] = ["20.205.243.168", "140.82.112.5", "140.82.113.5", "140.82.114.6", "140.82.121.5"],
        ["codeload.github.com"] = ["20.205.243.165", "140.82.112.9", "140.82.113.10", "140.82.114.10", "140.82.121.10"],
        [UserContentWildcard] =
        [
            "185.199.108.133", "185.199.109.133", "185.199.110.133", "185.199.111.133",
            "185.199.108.154", "185.199.109.154", "185.199.110.154", "185.199.111.154",
            "140.82.112.21",
        ],
        ["avatars.githubusercontent.com"] = ["185.199.108.133", "185.199.109.133", "185.199.110.133", "185.199.111.133"],
        ["github.githubassets.com"] = ["185.199.108.215", "185.199.109.215", "185.199.110.215", "185.199.111.215"],
    };

    /// <summary>
    /// 主机别名:发布资产在 github.com 与 githubusercontent 系之间跳转,任一键缺失时用同族 IP 兜底
    /// (对齐 Haiyu <c>APIExtensions.GitHubHostAliases</c>)。顺序即优先级。
    /// </summary>
    private static readonly Dictionary<string, string[]> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["release-assets.githubusercontent.com"] =
            ["release-assets.githubusercontent.com", "objects.githubusercontent.com", "raw.githubusercontent.com", "github.com"],
        ["objects.githubusercontent.com"] =
            ["objects.githubusercontent.com", "release-assets.githubusercontent.com", "raw.githubusercontent.com", "github.com"],
        ["objects-origin.githubusercontent.com"] = ["objects-origin.githubusercontent.com", "github.com"],
        ["github-releases.githubusercontent.com"] =
            ["github-releases.githubusercontent.com", "github-registry-files.githubusercontent.com", UserContentWildcard],
        ["github-registry-files.githubusercontent.com"] =
            ["github-registry-files.githubusercontent.com", "github-releases.githubusercontent.com", UserContentWildcard],
    };

    /// <summary>域前置是否启用(由设置注入;默认 false,走系统 DNS)。</summary>
    public static bool Enabled { get; set; }

    /// <summary>单次连接尝试超时(每个候选 IP 独立计时,对齐 Haiyu 的 5 秒)。</summary>
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 解析主机对应的候选 IP;未命中返回空。仅覆盖 GitHub 相关主机,其它主机交由系统 DNS。
    /// </summary>
    public static IReadOnlyList<IPAddress> Resolve(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return [];
        }
        var raw = LookupRaw(host);
        if (raw.Length == 0)
        {
            return [];
        }

        var list = new List<IPAddress>(raw.Length);
        foreach (var s in raw)
        {
            if (IPAddress.TryParse(s, out var ip) && !list.Contains(ip))
            {
                list.Add(ip);
            }
        }
        return list;
    }

    private static string[] LookupRaw(string host)
    {
        if (Builtin.TryGetValue(host, out var direct))
        {
            return direct;
        }
        if (Aliases.TryGetValue(host, out var aliases))
        {
            foreach (var alias in aliases)
            {
                if (Builtin.TryGetValue(alias, out var viaAlias))
                {
                    return viaAlias;
                }
            }
        }
        if (host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase)
            && Builtin.TryGetValue(UserContentWildcard, out var wildcard))
        {
            return wildcard;
        }
        return [];
    }

    /// <summary>
    /// 构造带域前置的 <see cref="SocketsHttpHandler"/>(对齐 Haiyu <c>CreateGithubHandler</c>)。
    /// 未启用或主机不在 IP 表内时,回退到系统 DNS 解析(与普通 HttpClient 行为一致)。
    /// </summary>
    public static SocketsHttpHandler CreateHandler(IWebProxy? proxy)
    {
        var handler = new SocketsHttpHandler
        {
            UseProxy = true,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(20),
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            ConnectCallback = async (context, token) =>
            {
                var host = context.DnsEndPoint.Host;
                var port = context.DnsEndPoint.Port;
                var addresses = Enabled ? Resolve(host) : [];

                if (addresses.Count == 0)
                {
                    // 未启用 / 非 GitHub 主机:系统 DNS 直连
                    var plain = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                    try
                    {
                        await plain.ConnectAsync(context.DnsEndPoint, token).ConfigureAwait(false);
                        return new NetworkStream(plain, ownsSocket: true);
                    }
                    catch
                    {
                        plain.Dispose();
                        throw;
                    }
                }

                var failures = new List<Exception>();
                foreach (var address in addresses)
                {
                    token.ThrowIfCancellationRequested();
                    // 每个候选 IP 独立 5 秒超时:单个 IP 不通不影响其余候选
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                    timeout.CancelAfter(ConnectTimeout);
                    var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
                    {
                        NoDelay = true,
                    };
                    try
                    {
                        await socket.ConnectAsync(new IPEndPoint(address, port), timeout.Token).ConfigureAwait(false);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch (OperationCanceledException) when (!token.IsCancellationRequested)
                    {
                        socket.Dispose();
                        failures.Add(new TimeoutException($"连接超时: {host}:{port} via {address}"));
                    }
                    catch (Exception ex) when (ex is SocketException or IOException)
                    {
                        socket.Dispose();
                        failures.Add(new IOException($"连接失败: {host}:{port} via {address}", ex));
                    }
                }

                throw new IOException(
                    $"{host}:{port} 全部候选 IP 连接失败(已尝试 {string.Join(", ", addresses)})",
                    new AggregateException(failures));
            },
        };
        if (proxy is not null)
        {
            handler.Proxy = proxy;
        }
        return handler;
    }

    /// <summary>
    /// 规范化镜像/加速模板并套用到下载地址(对齐 Haiyu <c>githubCdn</c> 的 <c>{downloadUrl}</c> 占位)。
    /// 模板为空或不含占位符时返回原地址(无效配置不破坏下载链路)。
    /// </summary>
    public static string ApplyAccelerator(string? template, string downloadUrl)
    {
        if (string.IsNullOrWhiteSpace(template)
            || !template.Contains("{downloadUrl}", StringComparison.OrdinalIgnoreCase))
        {
            return downloadUrl;
        }
        return template.Replace("{downloadUrl}", downloadUrl, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>校验加速模板是否可用(必须含 <c>{downloadUrl}</c> 占位符)。</summary>
    public static bool IsValidAccelerator(string? template) =>
        !string.IsNullOrWhiteSpace(template)
        && template.Contains("{downloadUrl}", StringComparison.OrdinalIgnoreCase);
}
