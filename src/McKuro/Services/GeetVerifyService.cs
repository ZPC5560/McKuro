using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace McKuro.Services;

/// <summary>
/// 极验(GeeTest)人机验证服务:通过系统默认浏览器打开本地验证页,
/// 用户完成滑块后浏览器 GET 回传极验 JSON,本服务用本地 TcpListener 接收。
///
/// 页面本身也由该 TcpListener 以 http://127.0.0.1 提供
/// (而非 file:// —— file:// 下 location.search 在部分浏览器不返回查询串,
/// 会导致页面拿不到回调地址),回调路径为同一端口的 /cb。
/// 不依赖 WebView2(规避 AOT/单文件发布复杂度),端口随机(普通用户无需 URLACL 权限)。
/// 对齐 Haiyu 的 geet.html(gt4.js)与 WutheringWavesTool 的 H5 极验流程。
/// </summary>
public sealed class GeetVerifyService
{
    private const int TimeoutSeconds = 120;

    private readonly ILogger<GeetVerifyService> _logger;

    public GeetVerifyService(ILogger<GeetVerifyService>? logger = null)
    {
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<GeetVerifyService>.Instance;
    }

    /// <summary>启动一次极验验证,返回极验 validate JSON(用户取消/超时/失败返回 null)。</summary>
    /// <param name="ct">取消令牌。</param>
    /// <param name="openBrowser">打开页面的委托(默认系统默认浏览器;测试可注入)。</param>
    /// <param name="html">验证页 HTML(默认读发布目录 Assets/geetest.html;测试可注入)。</param>
    /// <param name="readJs">读取本地 Js 资源(默认读 Assets/Js/&lt;name&gt;;测试可注入)。</param>
    public async Task<string?> VerifyAsync(
        CancellationToken ct = default,
        Action<string>? openBrowser = null,
        string? html = null,
        Func<string, string?>? readJs = null)
    {
        openBrowser ??= url =>
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        readJs ??= name =>
        {
            var jsPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Js", name);
            return File.Exists(jsPath) ? File.ReadAllText(jsPath) : null;
        };
        // 绑定随机空闲端口(普通用户可监听 127.0.0.1,无需 netsh urlacl)
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        html ??= File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets", "geetest.html"));

        // 回调地址 = 同一监听端口的 /cb;验证页 = 同一端口的 /verify?cb=...
        var cb = $"http://127.0.0.1:{port}/cb";
        // 回调地址直接注入页面(替换占位符),不依赖浏览器解析 location.search
        html = html.Replace("__MCKURO_CB__", cb, StringComparison.Ordinal);
        var pageUrl = $"http://127.0.0.1:{port}/verify?cb={Uri.EscapeDataString(cb)}";
        _logger.LogInformation("极验验证启动: 页面={PageUrl} 回调={Cb}", pageUrl, cb);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));

        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        // 后台 accept 循环:服务验证页 + 接收浏览器回调连接
        _ = Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    var client = await listener.AcceptTcpClientAsync(cts.Token);
                    _ = HandleClientAsync(client, html, readJs!, tcs, cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break; // 超时/取消
                }
                catch (Exception)
                {
                    break; // listener 已释放等致命错误
                }
            }
        }, CancellationToken.None);

        // 打开系统默认浏览器(需在 accept 循环启动后,避免页面先于服务就绪)
        try
        {
            openBrowser(pageUrl);
            _logger.LogInformation("已调用浏览器打开验证页");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "打开浏览器失败");
            tcs.TrySetResult(null);
        }

        try
        {
            var result = await tcs.Task.WaitAsync(cts.Token);
            _logger.LogInformation("极验验证完成: {Success}", result is null ? "无结果" : "成功");
            return result;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("极验验证超时/取消(用户未在 {Timeout}s 内完成)", TimeoutSeconds);
            return null; // 用户未在超时时间内完成验证
        }
    }

    private async Task HandleClientAsync(
        TcpClient client,
        string html,
        Func<string, string?> readJs,
        TaskCompletionSource<string?> tcs,
        CancellationToken ct)
    {
        try
        {
            using var stream = client.GetStream();

            // 读取 HTTP 请求(请求行 + 头,循环读到空行,防止 URL 过长被截断)
            var buffer = new byte[8192];
            var sb = new StringBuilder();
            while (sb.Length < buffer.Length)
            {
                int n = await stream.ReadAsync(buffer.AsMemory(), ct);
                if (n == 0)
                {
                    break;
                }
                sb.Append(Encoding.UTF8.GetString(buffer, 0, n));
                if (sb.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
                {
                    break;
                }
            }
            var request = sb.ToString();
            var path = GetPath(request);
            _logger.LogInformation("极验本地服务收到请求: {Path}", path);

            string body;
            var contentType = "text/html; charset=utf-8";
            if (path.StartsWith("/cb", StringComparison.Ordinal))
            {
                // 极验回调:提取 data 并完成验证
                var data = ExtractData(request);
                if (data is null)
                {
                    _logger.LogWarning("回调 /cb 未提取到 data(请求行: {Line})", request.Split('\n')[0].Trim());
                }
                else
                {
                    _logger.LogInformation("回调 /cb 提取到极验结果: 长度={Len} 摘要={Summary}",
                        data.Length, data.Length <= 80 ? data : data[..80] + "…");
                }
                body = data is null
                    ? "<html><body><h3>验证失败:未收到极验结果,请关闭本页返回启动器重试</h3></body></html>"
                    : "<html><body><h3 style='color:#067647'>验证成功!可关闭此页面并返回启动器</h3></body></html>";
                if (data is not null)
                {
                    tcs.TrySetResult(data);
                }
            }
            else if (path.StartsWith("/verify", StringComparison.Ordinal))
            {
                // 验证页本身
                body = html;
            }
            else if (path.StartsWith("/Js/", StringComparison.OrdinalIgnoreCase))
            {
                // 本地 jquery/gt4.js(对齐 Haiyu 本地脚本)
                var name = Path.GetFileName(path.Split('?')[0]);
                var js = string.IsNullOrEmpty(name) ? null : readJs(name);
                if (js is null)
                {
                    body = "<html><body><h3>404: 脚本不存在</h3></body></html>";
                }
                else
                {
                    body = js;
                    contentType = "text/javascript; charset=utf-8";
                }
            }
            else
            {
                // favicon 等无关请求
                body = "<html><body><h3>404</h3></body></html>";
            }

            var response =
                "HTTP/1.1 200 OK\r\n" +
                $"Content-Type: {contentType}\r\n" +
                $"Content-Length: {Encoding.UTF8.GetByteCount(body)}\r\n" +
                "Connection: close\r\n\r\n" +
                body;
            var respBytes = Encoding.UTF8.GetBytes(response);
            await stream.WriteAsync(respBytes.AsMemory(), ct);
            await stream.FlushAsync(ct);
        }
        catch (Exception ex)
        {
            // 忽略单个连接错误(记录日志便于排查)
            _logger.LogDebug(ex, "极验本地服务处理连接异常");
        }
        finally
        {
            client.Dispose();
        }
    }

    /// <summary>提取请求路径(如 "/cb?data=..." → "/cb")。</summary>
    private static string GetPath(string request)
    {
        var firstLine = request.Split('\n')[0].Trim();
        var parts = firstLine.Split(' ');
        return parts.Length >= 2 ? parts[1] : "/";
    }

    /// <summary>从 HTTP 请求行提取 data 查询参数(URL 解码)。</summary>
    private static string? ExtractData(string request)
    {
        var firstLine = request.Split('\n')[0].Trim();
        var qIndex = firstLine.IndexOf('?');
        if (qIndex < 0)
        {
            return null;
        }
        var query = firstLine[(qIndex + 1)..];
        // [^&\s] 排除空格:请求行中 data 值到 " HTTP/1.1" 前的空格为止
        var m = Regex.Match(query, @"(?:^|&)data=([^&\s]+)");
        if (!m.Success)
        {
            return null;
        }
        return Uri.UnescapeDataString(m.Groups[1].Value);
    }
}
