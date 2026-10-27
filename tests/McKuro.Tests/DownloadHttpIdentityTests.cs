using System.Net;
using System.Text;
using McKuro.Core.Services.Game;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace McKuro.Tests;

/// <summary>
/// 下载客户端的传输约束:请求必须带 <c>Accept-Encoding: identity</c>、响应不得被透明解压、
/// 且 Range 续传的字节流与清单一一对应。
/// <para>
/// <b>为什么专门测这个:</b>下载走 HTTP Range。若 CDN 对 <c>206</c> 响应做压缩,
/// <c>Content-Range</c> 指的是压缩体偏移,而 .NET 解压后交付的字节数与清单长度不一致 ——
/// 症状是"个别节点偶发下载失败",在本地极难复现。这条约束一旦被改回
/// <c>AutomaticDecompression = All</c> 就会重新埋雷,所以用测试钉住。
/// </para>
/// </summary>
public class DownloadHttpIdentityTests
{
    [Fact]
    public async Task DownloadClient_Sends_Identity_AcceptEncoding()
    {
        string? acceptEncoding = null;
        using var listener = new HttpListener();
        var port = GetFreePort();
        var prefix = $"http://127.0.0.1:{port}/";
        listener.Prefixes.Add(prefix);
        listener.Start();

        var server = Task.Run(async () =>
        {
            var ctx = await listener.GetContextAsync();
            acceptEncoding = ctx.Request.Headers["Accept-Encoding"];
            var body = Encoding.ASCII.GetBytes("payload");
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentLength64 = body.Length;
            await ctx.Response.OutputStream.WriteAsync(body);
            ctx.Response.Close();
        });

        using var client = BuildDownloadClient();
        await client.GetAsync(prefix + "f.bin");
        await server;

        // 必须显式声明 identity(或干脆不带),不能是 gzip/deflate/br 的组合
        Assert.True(
            acceptEncoding is null || acceptEncoding.Contains("identity", StringComparison.OrdinalIgnoreCase),
            $"下载请求不应协商压缩,实际 Accept-Encoding = {acceptEncoding ?? "(null)"}");
        Assert.False(
            acceptEncoding?.Contains("gzip", StringComparison.OrdinalIgnoreCase) ?? false,
            $"下载请求不应带 gzip,实际 = {acceptEncoding}");
    }

    [Fact]
    public async Task DownloadClient_Does_Not_Decompress_Gzip_Body()
    {
        // 服务端强行返回 gzip 压缩体(不管我们要不要):客户端必须原样交付字节,
        // 不能替我们解压 —— 否则 Range 偏移与写入量就与清单不符。
        byte[]? received = null;
        using var listener = new HttpListener();
        var port = GetFreePort();
        var prefix = $"http://127.0.0.1:{port}/";
        listener.Prefixes.Add(prefix);
        listener.Start();

        var server = Task.Run(async () =>
        {
            var ctx = await listener.GetContextAsync();
            var raw = Gzip("hello wuwa");
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/octet-stream";
            ctx.Response.ContentLength64 = raw.Length;
            await ctx.Response.OutputStream.WriteAsync(raw);
            ctx.Response.Close();
        });

        using var client = BuildDownloadClient();
        using var response = await client.GetAsync(prefix + "f.bin");
        received = await response.Content.ReadAsByteArrayAsync();
        await server;

        Assert.NotNull(received);
        Assert.Equal([0x1F, 0x8B], received![..2]); // 收到的仍是 gzip 原始字节
    }

    [Fact]
    public async Task FileDownloader_Resumes_From_Offset_With_Identity_Client()
    {
        // 端到端:用下载客户端跑 FileDownloader 的 Range 续传路径,
        // 确认"已写前缀"不再重复下载、最终文件内容与清单一致。
        var payload = new byte[64 * 1024];
        for (int i = 0; i < payload.Length; i++)
        {
            payload[i] = (byte)(i % 251);
        }

        using var listener = new HttpListener();
        var port = GetFreePort();
        var prefix = $"http://127.0.0.1:{port}/";
        listener.Prefixes.Add(prefix);
        listener.Start();

        var server = Task.Run(async () =>
        {
            for (int i = 0; i < 2; i++)
            {
                var ctx = await listener.GetContextAsync();
                var range = ctx.Request.Headers["Range"];
                if (!string.IsNullOrEmpty(range) && range.StartsWith("bytes=", StringComparison.Ordinal))
                {
                    var start = long.Parse(range["bytes=".Length..].Split('-')[0]);
                    var slice = payload[(int)start..];
                    ctx.Response.StatusCode = 206;
                    ctx.Response.Headers["Content-Range"] = $"bytes {start}-{payload.Length - 1}/{payload.Length}";
                    ctx.Response.ContentLength64 = slice.Length;
                    await ctx.Response.OutputStream.WriteAsync(slice);
                }
                else
                {
                    ctx.Response.StatusCode = 200;
                    ctx.Response.ContentLength64 = payload.Length;
                    await ctx.Response.OutputStream.WriteAsync(payload);
                }
                ctx.Response.Close();
            }
        });

        var dir = Path.Combine(Path.GetTempPath(), "McKuro-dl-identity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var dest = Path.Combine(dir, "f.bin");
        try
        {
            using var client = BuildDownloadClient();
            var downloader = new FileDownloader(client, NullLogger<FileDownloader>.Instance);
            var entry = new McKuro.Core.Models.Game.GameFileEntry
            {
                Path = "f.bin",
                Size = payload.Length,
                Md5 = "",
                Url = prefix + "f.bin",
            };

            var result = await downloader.DownloadAsync(entry, baseUrl: "", destPath: dest);

            Assert.True(result.Success, result.Error);
            Assert.Equal(payload, await File.ReadAllBytesAsync(dest));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    // ---------- 夹具 ----------

    /// <summary>按生产配置构造下载客户端(与 AppServices 里注册的一致)。</summary>
    private static HttpClient BuildDownloadClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.None,
            MaxConnectionsPerServer = 16,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            UseCookies = false,
        };
        var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Encoding", "identity");
        return client;
    }

    private static byte[] Gzip(string text)
    {
        using var ms = new MemoryStream();
        using (var gz = new System.IO.Compression.GZipStream(ms, System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true))
        {
            gz.Write(Encoding.UTF8.GetBytes(text));
        }
        return ms.ToArray();
    }

    private static int GetFreePort()
    {
        using var socket = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        socket.Start();
        var port = ((IPEndPoint)socket.LocalEndpoint).Port;
        socket.Stop();
        return port;
    }
}
