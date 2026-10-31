using System.Net;
using System.Net.Sockets;
using System.Text;
using McKuro.Services;

namespace McKuro.Tests;

/// <summary>
/// 极验回调本地服务测试:验证 TcpListener 能接收浏览器回调并提取极验 JSON。
/// 通过直接连接本地端口模拟浏览器完成极验后的 GET 回调。
/// </summary>
public class GeetVerifyCallbackTests
{
    private static async Task<string?> RunCallbackReceiver(
        TcpListener listener,
        CancellationToken ct,
        Action<string> onData)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));
        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        _ = Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    var client = await listener.AcceptTcpClientAsync(cts.Token);
                    _ = HandleAsync(client, tcs, cts.Token);
                }
                catch (OperationCanceledException) { break; }
                catch { break; }
            }
        }, CancellationToken.None);

        var result = await tcs.Task.WaitAsync(cts.Token);
        onData(result ?? "");
        return result;
    }

    private static async Task HandleAsync(TcpClient client, TaskCompletionSource<string?> tcs, CancellationToken ct)
    {
        try
        {
            using var stream = client.GetStream();
            var buffer = new byte[8192];
            var sb = new StringBuilder();
            while (sb.Length < buffer.Length)
            {
                int n = await stream.ReadAsync(buffer.AsMemory(), ct);
                if (n == 0) break;
                sb.Append(Encoding.UTF8.GetString(buffer, 0, n));
                if (sb.ToString().Contains("\r\n\r\n", StringComparison.Ordinal)) break;
            }
            var request = sb.ToString();

            var body = "<html>ok</html>";
            var resp = $"HTTP/1.1 200 OK\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}";
            var bytes = Encoding.UTF8.GetBytes(resp);
            await stream.WriteAsync(bytes.AsMemory(), ct);

            var data = ExtractData(request);
            if (data is not null)
            {
                tcs.TrySetResult(data);
            }
        }
        catch { }
        finally { client.Dispose(); }
    }

    /// <summary>与 GeetVerifyService.ExtractData 相同的解析逻辑(测试镜像)。</summary>
    private static string? ExtractData(string request)
    {
        var firstLine = request.Split('\n')[0].Trim();
        var qIndex = firstLine.IndexOf('?');
        if (qIndex < 0) return null;
        var query = firstLine[(qIndex + 1)..];
        var m = System.Text.RegularExpressions.Regex.Match(query, @"(?:^|&)data=([^&\s]+)");
        if (!m.Success) return null;
        return Uri.UnescapeDataString(m.Groups[1].Value);
    }

    [Fact]
    public async Task Callback_Receives_And_Extracts_GeeTest_Json()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var geeTestJson = """{"lot_number":"abc123","captcha_output":"xyz","pass_token":"tok","gen_time":"1700000000","captcha_id":"3f7e2d848ce0cb7e7d019d621e556ce2"}""";
        var url = $"http://127.0.0.1:{port}/cb?data={Uri.EscapeDataString(geeTestJson)}";

        string? received = null;
        var receiver = RunCallbackReceiver(listener, CancellationToken.None, d => received = d);

        using (var client = new HttpClient())
        {
            await client.GetAsync(url);
        }

        var result = await receiver;
        Assert.Equal(geeTestJson, result);
        Assert.Equal(geeTestJson, received);
    }

    [Fact]
    public async Task Callback_Without_Data_Does_Not_Complete()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    var client = await listener.AcceptTcpClientAsync(CancellationToken.None);
                    _ = HandleAsync(client, tcs, CancellationToken.None);
                }
                catch { break; }
            }
        }, CancellationToken.None);

        using (var client = new HttpClient())
        {
            await client.GetAsync($"http://127.0.0.1:{port}/favicon.ico");
        }

        // 无 data 的回调不应完成验证任务(等待真实用户完成极验)
        var completed = await Task.WhenAny(tcs.Task, Task.Delay(500));
        Assert.NotSame(tcs.Task, completed);
    }

    // ---------- 端到端:GeetVerifyService 自身(页面 http 服务 + 回调) ----------

    [Fact]
    public async Task Verify_Serves_Page_Over_Http_And_Completes_On_Callback()
    {
        var service = new GeetVerifyService();
        string? openedUrl = null;
        var task = service.VerifyAsync(
            CancellationToken.None,
            url => openedUrl = url,
            html: "<html><body id='page'>var CALLBACK = \"__MCKURO_CB__\";</body></html>");

        // 等待浏览器打开回调被捕获
        var url = await WaitForUrlAsync(openedUrl);

        // 1) 验证页通过 http 服务返回,且回调地址已被服务端注入(占位符被替换)
        string pageHtml;
        using (var client = new HttpClient())
        {
            pageHtml = await client.GetStringAsync(url);
        }
        var expectedCb = CallbackFromPageUrl(url);
        Assert.Contains(expectedCb, pageHtml);
        Assert.DoesNotContain("__MCKURO_CB__", pageHtml);

        // 2) 模拟滑块完成后的回调 → VerifyAsync 返回极验 JSON
        var geeTestJson = """{"lot_number":"abc123","captcha_output":"xyz","pass_token":"tok","gen_time":"1700000000","captcha_id":"3f7e2d848ce0cb7e7d019d621e556ce2"}""";
        using (var client = new HttpClient())
        {
            await client.GetAsync($"{expectedCb}?data={Uri.EscapeDataString(geeTestJson)}");
        }

        var result = await task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(geeTestJson, result);
    }

    [Fact]
    public async Task Verify_Page_Html_Contains_Callback_Url_Parameter()
    {
        var service = new GeetVerifyService();
        string? openedUrl = null;
        var task = service.VerifyAsync(
            CancellationToken.None,
            url => openedUrl = url,
            html: "<html><body>captcha</body></html>");

        var url = await WaitForUrlAsync(openedUrl);

        // 页面 URL 应带 ?cb= 参数,且 cb 指向同一服务端口的 /cb/{nonce}(32 位十六进制随机串)
        var uri = new Uri(url);
        Assert.StartsWith("/verify", uri.AbsolutePath, StringComparison.Ordinal);
        var cb = System.Web.HttpUtility.ParseQueryString(uri.Query).Get("cb");
        Assert.NotNull(cb);
        Assert.Matches($"^http://127\\.0\\.0\\.1:{uri.Port}/cb/[0-9a-f]{{32}}$", cb);

        // 清理:直接触发回调结束验证(避免后台任务悬挂)
        using (var client = new HttpClient())
        {
            await client.GetAsync($"{cb}?data=%7B%7D");
        }
        await task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Verify_Serves_Local_Js_With_JavaScript_ContentType()
    {
        var service = new GeetVerifyService();
        string? openedUrl = null;
        var task = service.VerifyAsync(
            CancellationToken.None,
            url => openedUrl = url,
            html: "<html><script src='Js/gt4.js'></script></html>",
            readJs: name => name == "gt4.js" ? "window.initGeetest4 = function(){};" : null);

        var url = await WaitForUrlAsync(openedUrl);
        var baseUrl = $"http://127.0.0.1:{new Uri(url).Port}";

        using var client = new HttpClient();
        var resp = await client.GetAsync($"{baseUrl}/Js/gt4.js");
        Assert.True(resp.IsSuccessStatusCode);
        Assert.Contains("text/javascript", resp.Content.Headers.ContentType?.ToString());
        Assert.Equal("window.initGeetest4 = function(){};", await resp.Content.ReadAsStringAsync());

        // 不存在的脚本返回 404 内容(HTTP 仍 200,体内容为 404 提示)
        var missing = await client.GetStringAsync($"{baseUrl}/Js/nope.js");
        Assert.Contains("404", missing);

        // 清理:触发回调结束验证
        await client.GetAsync($"{CallbackFromPageUrl(url)}?data=%7B%7D");
        await task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Verify_Callback_Without_Nonce_Is_Ignored()
    {
        // 回调路径带每次随机的 nonce:猜不到 nonce 的本地进程/网页提交 /cb?data= 不应完成验证
        var service = new GeetVerifyService();
        string? openedUrl = null;
        var task = service.VerifyAsync(CancellationToken.None, url => openedUrl = url, html: "<html>captcha</html>");
        var url = await WaitForUrlAsync(openedUrl);
        var port = new Uri(url).Port;

        using (var client = new HttpClient())
        {
            await client.GetAsync($"http://127.0.0.1:{port}/cb?data=%7B%22forged%22%3A1%7D");
            await client.GetAsync($"http://127.0.0.1:{port}/cb/0000?data=%7B%22forged%22%3A1%7D");
        }
        var completed = await Task.WhenAny(task, Task.Delay(500));
        Assert.NotSame(task, completed);

        // 正确的回调仍然生效
        using (var client = new HttpClient())
        {
            await client.GetAsync($"{CallbackFromPageUrl(url)}?data=%7B%7D");
        }
        Assert.Equal("{}", await task.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task Verify_Rejects_Request_With_Foreign_Host_Header()
    {
        // DNS 重绑定场景:外部网页的请求 Host 不是 127.0.0.1:{port},即使路径正确也不接受
        var service = new GeetVerifyService();
        string? openedUrl = null;
        var task = service.VerifyAsync(CancellationToken.None, url => openedUrl = url, html: "<html>captcha</html>");
        var url = await WaitForUrlAsync(openedUrl);
        var cb = new Uri(CallbackFromPageUrl(url));

        using (var tcp = new TcpClient())
        {
            await tcp.ConnectAsync(IPAddress.Loopback, cb.Port);
            using var stream = tcp.GetStream();
            var req = $"GET {cb.AbsolutePath}?data=%7B%22forged%22%3A1%7D HTTP/1.1\r\nHost: evil.example:{cb.Port}\r\nConnection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(req));
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var resp = await reader.ReadToEndAsync();
            Assert.Contains("403", resp);
        }
        var completed = await Task.WhenAny(task, Task.Delay(500));
        Assert.NotSame(task, completed);

        using (var client = new HttpClient())
        {
            await client.GetAsync($"{cb}?data=%7B%7D");
        }
        Assert.Equal("{}", await task.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    /// <summary>从验证页 URL 的 cb 参数取回调地址(含随机 nonce)。</summary>
    private static string CallbackFromPageUrl(string pageUrl)
    {
        var cb = System.Web.HttpUtility.ParseQueryString(new Uri(pageUrl).Query).Get("cb");
        Assert.NotNull(cb);
        return cb!;
    }

    private static async Task<string> WaitForUrlAsync(string? url)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (string.IsNullOrEmpty(url))
        {
            if (sw.ElapsedMilliseconds > 5000)
            {
                throw new TimeoutException("浏览器打开回调未被触发");
            }
            await Task.Delay(20);
        }
        return url!;
    }
}
