using System.Net;
using System.Security.Cryptography;
using System.Text;
using McKuro.Core.Models.Game;
using McKuro.Core.Services.Game;

namespace McKuro.Tests;

/// <summary>
/// 下载重试机制(移植自 Haiyu 1.4.3「下载重试机制,不再等待单一 TCP 连接失效」)的行为测试。
/// <para>
/// 背景:HttpClient.Timeout 只作用于「拿到响应头之前」。实测(本地 HttpListener:总超时 2 秒、
/// 服务端在响应头之后分 5 秒吐完 10 字节)客户端仍完整读完 —— 即响应体读取不受总超时约束,
/// 连接被静默中断时会一直挂着。因此必须有「单次读取空闲超时 + 指数退避续传」这一层。
/// </para>
/// </summary>
public sealed class FileDownloaderRetryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "McKuro_retry_" + Guid.NewGuid().ToString("N"));

    public FileDownloaderRetryTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // 忽略清理失败
        }
    }

    /// <summary>读取空闲超时触发后应自动重试并成功(Haiyu 用 90s CancelAfter)。</summary>
    [Fact]
    public async Task 空闲超时触发重试_最终成功()
    {
        var payload = Encoding.UTF8.GetBytes("abcdefghijklmnopqrstuvwxyz");
        var handler = new ScriptedHandler((_, attempt) =>
            // 第一次:响应头正常但响应体永不产出字节(模拟 TCP 静默失效);之后正常返回
            attempt == 0 ? Stalled(declaredLength: payload.Length) : Ok(payload));
        using var http = new HttpClient(handler);
        var destination = Path.Combine(_root, "a.bin");

        var result = await NewDownloader(http, TimeSpan.FromMilliseconds(150))
            .DownloadAsync(Entry("a.bin", payload), "", destination);

        Assert.True(result.Success, result.Error);
        Assert.Equal(payload, await File.ReadAllBytesAsync(destination));
        Assert.Equal(2, handler.Attempts);
    }

    /// <summary>关键不变量:重试必须从「已写入的偏移」续传,而不是退回起点。</summary>
    [Fact]
    public async Task 截断后重试_从已写入偏移续传()
    {
        var payload = Encoding.UTF8.GetBytes("abcdefghijklmnopqrstuvwxyz");
        var handler = new ScriptedHandler((req, attempt) =>
            // 第一次:声明 26 字节,却只吐出前 10 字节就 EOF(连接被掐断的典型表现);之后按续传范围返回剩余字节
            attempt == 0 ? Ok(payload[..10], declaredLength: payload.Length) : RangeResponse(payload, req));
        using var http = new HttpClient(handler);
        var destination = Path.Combine(_root, "b.bin");

        var result = await NewDownloader(http, TimeSpan.FromSeconds(5))
            .DownloadAsync(Entry("b.bin", payload), "", destination);

        Assert.True(result.Success, result.Error);
        // 内容必须完整且没有错位(从 0 重下并追加会得到 36 字节的错内容)
        Assert.Equal(payload, await File.ReadAllBytesAsync(destination));
        Assert.Equal(2, handler.Attempts);
        // 第二次请求必须带 Range,起点正好是已写入的 10;整文件路径的末端开放(end=-1),
        // 因此 RangeTo 为 null(即 "bytes=10-"),这是有意为之:续传到文件末尾。
        var resume = handler.Requests[1];
        Assert.True(resume.RangeFrom == 10L, $"续传起点应为 10,实际 {Describe(handler.Requests)}");
        Assert.True(resume.RangeTo is null, $"续传应为末端开放,实际 {Describe(handler.Requests)}");
    }

    /// <summary>重试次数用尽后应返回失败(而不是静默成功)。</summary>
    [Fact]
    public async Task 重试次数用尽_返回失败()
    {
        var payload = Encoding.UTF8.GetBytes("abcdefghijklmnopqrstuvwxyz");
        var handler = new ScriptedHandler((_, _) => Stalled(declaredLength: payload.Length));
        using var http = new HttpClient(handler);
        var destination = Path.Combine(_root, "c.bin");

        var result = await NewDownloader(http, TimeSpan.FromMilliseconds(100))
            .DownloadAsync(Entry("c.bin", payload), "", destination);

        Assert.False(result.Success);
        // 首次尝试 + MaxRetryCount 次重试
        Assert.Equal(FileDownloader.MaxRetryCount + 1, handler.Attempts);
        Assert.Contains("重试", result.Error);
        Assert.False(File.Exists(destination));
    }

    /// <summary>503 属可重试的瞬时错误:重试后应成功。</summary>
    [Fact]
    public async Task 服务端503_重试后成功()
    {
        var payload = Encoding.UTF8.GetBytes("hello world");
        var handler = new ScriptedHandler((_, attempt) =>
            attempt == 0 ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : Ok(payload));
        using var http = new HttpClient(handler);
        var destination = Path.Combine(_root, "d.bin");

        var result = await NewDownloader(http, TimeSpan.FromSeconds(5))
            .DownloadAsync(Entry("d.bin", payload), "", destination);

        Assert.True(result.Success, result.Error);
        Assert.Equal(2, handler.Attempts);
    }

    /// <summary>404 不是瞬时错误:不得重试,立即失败。</summary>
    [Fact]
    public async Task 服务端404_不重试直接失败()
    {
        var payload = Encoding.UTF8.GetBytes("hello world");
        var handler = new ScriptedHandler((_, _) => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var http = new HttpClient(handler);
        var destination = Path.Combine(_root, "e.bin");

        var result = await NewDownloader(http, TimeSpan.FromSeconds(5))
            .DownloadAsync(Entry("e.bin", payload), "", destination);

        Assert.False(result.Success);
        Assert.Equal(1, handler.Attempts);
    }

    /// <summary>用户主动取消不得被重试吞掉(否则「取消」会变成继续下载)。</summary>
    [Fact]
    public async Task 用户取消_不重试()
    {
        var payload = Encoding.UTF8.GetBytes("abcdefghijklmnopqrstuvwxyz");
        var handler = new ScriptedHandler((_, _) => Stalled(declaredLength: payload.Length));
        using var http = new HttpClient(handler);
        var destination = Path.Combine(_root, "f.bin");
        using var cts = new CancellationTokenSource();

        // 空闲超时给得很长,只有用户取消能让读取结束
        var download = NewDownloader(http, TimeSpan.FromSeconds(30))
            .DownloadAsync(Entry("f.bin", payload), "", destination, ct: cts.Token);
        cts.CancelAfter(TimeSpan.FromMilliseconds(150));
        var result = await download;

        Assert.False(result.Success);
        Assert.Equal("已取消", result.Error);
        Assert.Equal(1, handler.Attempts);
    }

    /// <summary>CDN 返回了错位的分片范围时必须失败,绝不能静默写坏文件。</summary>
    [Fact]
    public async Task 分片范围错位_判为失败不写坏文件()
    {
        var payload = Encoding.UTF8.GetBytes("abcdefghijklmnopqrstuvwxyz");
        // 请求续传 5 起,服务器却回 from=0 的 206(错位)
        var handler = new ScriptedHandler((_, _) =>
        {
            var content = new ByteArrayContent(payload);
            content.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(0, payload.Length - 1, payload.Length);
            return new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = content };
        });
        using var http = new HttpClient(handler);
        var destination = Path.Combine(_root, "g.bin");
        await File.WriteAllBytesAsync(destination + ".part", payload[..5]);

        var result = await NewDownloader(http, TimeSpan.FromSeconds(5))
            .DownloadAsync(Entry("g.bin", payload), "", destination);

        Assert.False(result.Success);
        Assert.False(File.Exists(destination));
    }

    /// <summary>服务器忽略 Range 时(回 200 整份内容)必须从头重写,而不是把整份追加到 .part 后面。</summary>
    [Fact]
    public async Task 服务器忽略续传范围_从头重写内容仍然正确()
    {
        var payload = Encoding.UTF8.GetBytes("abcdefghijklmnopqrstuvwxyz");
        var handler = new ScriptedHandler((_, _) => Ok(payload));
        using var http = new HttpClient(handler);
        var destination = Path.Combine(_root, "h.bin");
        await File.WriteAllBytesAsync(destination + ".part", payload[..10]);

        var result = await NewDownloader(http, TimeSpan.FromSeconds(5))
            .DownloadAsync(Entry("h.bin", payload), "", destination);

        Assert.True(result.Success, result.Error);
        Assert.Equal(payload, await File.ReadAllBytesAsync(destination));
    }

    /// <summary>分片（ChunkInfos）路径同样要走续传重试。</summary>
    [Fact]
    public async Task 分片路径_截断后重试续传()
    {
        var payload = Encoding.UTF8.GetBytes("abcdefghijklmnopqrstuvwxyz");
        var entry = Entry("i.bin", payload);
        entry.ChunkInfos.Add(new GameChunkInfo { Start = 0, End = 12, Md5 = Hash(payload[..13]) });
        entry.ChunkInfos.Add(new GameChunkInfo { Start = 13, End = payload.Length - 1, Md5 = Hash(payload[13..]) });

        var handler = new ScriptedHandler((req, _) =>
            // 第一片的首次请求:只给前 4 字节就断(声明该分片长度 13);其余请求一律正常
            req.RangeFrom == 0 && req.RangeTo == 12 && req.Attempt == 0
                ? Ok(payload[..4], declaredLength: 13)
                : RangeResponse(payload, req));
        using var http = new HttpClient(handler);
        var destination = Path.Combine(_root, "i.bin");

        var result = await NewDownloader(http, TimeSpan.FromSeconds(5))
            .DownloadAsync(entry, "", destination);

        Assert.True(result.Success, result.Error);
        Assert.Equal(payload, await File.ReadAllBytesAsync(destination));
        // 第一片截断后的重试必须从该分片内的偏移 4 续传到分片末尾 12
        Assert.True(
            handler.Requests.Any(r => r.RangeFrom == 4L && r.RangeTo == 12L),
            $"应有一次 [4-12] 的续传请求,实际 {Describe(handler.Requests)}");
    }

    /// <summary>分片文件只校验已完整的分片,不应重复下载已校验通过的分片。</summary>
    [Fact]
    public async Task 分片路径_已通过校验的分片不重复下载()
    {
        var payload = Encoding.UTF8.GetBytes("abcdefghijklmnopqrstuvwxyz");
        var entry = Entry("j.bin", payload);
        entry.ChunkInfos.Add(new GameChunkInfo { Start = 0, End = 12, Md5 = Hash(payload[..13]) });

        var handler = new ScriptedHandler((req, _) => RangeResponse(payload, req));
        using var http = new HttpClient(handler);
        var destination = Path.Combine(_root, "j.bin");
        // 预置完整且正确的 .part:唯一的那个分片应直接通过校验
        await File.WriteAllBytesAsync(destination + ".part", payload);

        var result = await NewDownloader(http, TimeSpan.FromSeconds(5))
            .DownloadAsync(entry, "", destination);

        Assert.True(result.Success, result.Error);
        Assert.Equal(0, handler.Attempts);
    }

    /// <summary>
    /// 分片路径下服务器忽略 Range(回 200 整份内容)属**协议违规**,不是瞬时故障:
    /// 必须立即失败,而不是白重试 4 次(+ 累计最多 15 秒退避)。
    /// </summary>
    [Fact]
    public async Task 分片路径_服务器忽略Range立即失败不重试()
    {
        var payload = Encoding.UTF8.GetBytes("abcdefghijklmnopqrstuvwxyz");
        var entry = Entry("k.bin", payload);
        entry.ChunkInfos.Add(new GameChunkInfo { Start = 0, End = 12, Md5 = Hash(payload[..13]) });
        // 无论请求什么范围都回 200 + 整份内容
        var handler = new ScriptedHandler((_, _) => Ok(payload));
        using var http = new HttpClient(handler);
        var destination = Path.Combine(_root, "k.bin");

        var result = await NewDownloader(http, TimeSpan.FromSeconds(5))
            .DownloadAsync(entry, "", destination);

        Assert.False(result.Success);
        Assert.Equal(1, handler.Attempts);
    }

    /// <summary>CDN 返回错位分片范围同属协议违规:立即失败,不重试。</summary>
    [Fact]
    public async Task 分片范围错位_立即失败不重试()
    {
        var payload = Encoding.UTF8.GetBytes("abcdefghijklmnopqrstuvwxyz");
        var entry = Entry("l.bin", payload);
        entry.ChunkInfos.Add(new GameChunkInfo { Start = 0, End = 12, Md5 = Hash(payload[..13]) });
        // 永远回 from=0 的 206
        var handler = new ScriptedHandler((_, _) =>
        {
            var content = new ByteArrayContent(payload[..13]);
            content.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(0, 12, payload.Length);
            return new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = content };
        });
        using var http = new HttpClient(handler);
        var destination = Path.Combine(_root, "l.bin");

        // 预热 .part:首片已是 0..12,若错位校验失效则会被"再次写入同一位置"而看不出问题
        var result = await NewDownloader(http, TimeSpan.FromSeconds(5))
            .DownloadAsync(entry, "", destination);

        Assert.False(result.Success);
        Assert.Equal(1, handler.Attempts);
    }

    /// <summary>
    /// 空闲超时只应惩罚「读取卡住」,不应惩罚「限速等待」。
    /// <para>
    /// 反例(此测试抓到的真实缺陷):若整个请求共用一个 idle CTS,当限速器让两次读取之间
    /// 间隔超过空闲超时时,计时器会在**没有人在读**的时候触发,把该 CTS 永久置为已取消 ——
    /// 之后同一请求里的每次读取都立刻抛取消(连接其实是好的),下载被误判失败并反复重试。
    /// 正确做法:每次读取各自一个 linked CTS(与 Haiyu 的 ReadWithIdleTimeoutAsync 一致)。
    /// </para>
    /// <para>
    /// 需要 <b>3 个读取缓冲</b>才能打到这个缺陷:限速等待发生在本轮读取之后,
    /// 必须还有「下一轮读取」才能观察到被误取消的后果(2 个缓冲时等待落在最后一轮,测不出来)。
    /// </para>
    /// </summary>
    [Fact]
    public async Task 限速等待超过空闲超时_不应误判失败()
    {
        // 3 个读取缓冲(128 KiB)的载荷:第 3 次读取发生在一次约 1 秒的限速等待之后
        var payload = new byte[3 * 128 * 1024];
        Random.Shared.NextBytes(payload);
        var handler = new ScriptedHandler((_, _) => Ok(payload));
        using var http = new HttpClient(handler);
        var destination = Path.Combine(_root, "slow.bin");

        // 限速 128 KiB + 1 字节/秒:第一轮读取后配额用尽,后续读取之间必须等待窗口滚动
        var limiter = new DownloadRateLimiter(128 * 1024 + 1);
        var downloader = new FileDownloader(http, logger: null, idleReadTimeout: TimeSpan.FromMilliseconds(150), retryDelayScale: 0.001);

        var result = await downloader.DownloadAsync(Entry("slow.bin", payload), "", destination, rateLimiter: limiter);

        Assert.True(result.Success, $"限速等待被误判为空闲超时: {result.Error}");
        Assert.Equal(payload, await File.ReadAllBytesAsync(destination));
        // 不应因误判而产生重试
        Assert.Equal(1, handler.Attempts);
    }

    /// <summary>
    /// 已完整且校验通过的 .part 应直接改名为目标文件,**完全不发网络请求**
    /// (回归保护:改造重试逻辑时不能把这条"零下载"捷径弄丢)。
    /// </summary>
    [Fact]
    public async Task 完整的part_直接落位且不发请求()
    {
        var payload = Encoding.UTF8.GetBytes("abcdefghijklmnopqrstuvwxyz");
        var handler = new ScriptedHandler((_, _) => Ok(payload));
        using var http = new HttpClient(handler);
        var destination = Path.Combine(_root, "m.bin");
        await File.WriteAllBytesAsync(destination + ".part", payload);

        var result = await NewDownloader(http, TimeSpan.FromSeconds(5))
            .DownloadAsync(Entry("m.bin", payload), "", destination);

        Assert.True(result.Success, result.Error);
        Assert.True(result.HashVerified);
        Assert.Equal(payload, await File.ReadAllBytesAsync(destination));
        Assert.Equal(0, handler.Attempts);
    }

    /// <summary>
    /// <b>独立评审发现的真实缺陷(已修)</b>:退避等待原本没有传取消令牌,用户点「取消」后
    /// 还要白等完当前退避(最坏 8 秒 + 抖动,实测取消延迟可达 8.5 秒)UI 才显示已取消。
    /// 修法:退避用 <c>Task.Delay(delayMs, ct)</c>,取消立即生效。
    /// </summary>
    [Fact]
    public async Task 退避期间取消_立即返回而不是等完退避()
    {
        var payload = Encoding.UTF8.GetBytes("abcdefghijklmnopqrstuvwxyz");
        // 永远僵住:每次尝试都只发响应头、不吐 body → 反复重试
        var handler = new ScriptedHandler((_, _) => Stalled(declaredLength: payload.Length));
        using var http = new HttpClient(handler);
        var destination = Path.Combine(_root, "cancel-backoff.bin");
        using var cts = new CancellationTokenSource();

        // retryDelayScale=1.0(生产退避刻度)+ 空闲超时 100ms:
        // 第 1 次退避约 1.0~1.8 秒,我们在它进行中取消
        var downloader = new FileDownloader(http, logger: null, idleReadTimeout: TimeSpan.FromMilliseconds(100), retryDelayScale: 1.0);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var download = downloader.DownloadAsync(Entry("cancel-backoff.bin", payload), "", destination, ct: cts.Token);

        // 等"已经进入退避"再取消(第 1 次尝试失败 ≈100ms 后开始退避)
        await Task.Delay(250);
        var cancelAt = sw.ElapsedMilliseconds;
        cts.Cancel();
        var result = await download;
        var cancelLatency = sw.ElapsedMilliseconds - cancelAt;

        Assert.False(result.Success);
        Assert.Equal("已取消", result.Error);
        // 关键断言:取消延迟必须远小于一次退避(约 1 秒),而不是干等完
        Assert.True(
            cancelLatency < 600,
            $"取消应立即可见,实际等待了 {cancelLatency}ms(说明退避没有传取消令牌)");
    }

    /// <summary>
    /// <b>独立评审发现的真实缺陷(已修)</b>:截断的响应后面紧跟「CDN 忽略 Range」时,
    /// 已写入的字节会被丢弃并从头重下。此前进度仍按每次读取累加,导致上报字节数<b>多于文件本身</b>
    /// (实测 200000 字节的文件报出 250000),百分比提前冲到 100%。
    /// 修法:丢弃时回报负向修正。
    /// </summary>
    [Fact]
    public async Task 忽略Range导致重下时_上报字节数不超过文件大小()
    {
        var payload = new byte[200_000];
        Random.Shared.NextBytes(payload);
        const int partial = 50_000; // 先写入 5 万字节后被掐断

        var handler = new ScriptedHandler((req, attempt) =>
            // 第 1 次:声明全量却只给前 5 万字节;第 2 次:忽略 Range,回 200 + 整份内容
            attempt == 0 ? Ok(payload[..partial], declaredLength: payload.Length) : Ok(payload));
        using var http = new HttpClient(handler);
        var destination = Path.Combine(_root, "overcount.bin");

        long reported = 0;
        var progress = new CollectingProgress(v => Interlocked.Add(ref reported, v));
        var result = await NewDownloader(http, TimeSpan.FromSeconds(5))
            .DownloadAsync(Entry("overcount.bin", payload), "", destination, progress: progress);

        Assert.True(result.Success, result.Error);
        Assert.Equal(payload, await File.ReadAllBytesAsync(destination));
        Assert.Equal(2, handler.Attempts);
        Assert.True(
            reported <= payload.Length,
            $"上报字节数 {reported} 不应超过文件大小 {payload.Length}(丢弃的字节必须回报负向修正)");
    }

    /// <summary>进度回调载荷是 int,单个文件可达数 GB:增量必须按 int 上限分片,不能溢出。</summary>
    [Fact]
    public void 进度增量超过int上限时按上限分片而不溢出()
    {
        var collected = new List<int>();
        var progress = new CollectingProgress(collected.Add);
        var method = typeof(FileDownloader).GetMethod(
            "ReportDelta",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        // 3 GB + 一小段:直接 (int) 转换会变成负数
        long delta = 3L * 1024 * 1024 * 1024 + 123;
        method.Invoke(null, [progress, delta]);

        Assert.All(collected, v => Assert.True(v > 0, "分片后不应出现负值"));
        Assert.Equal(delta, collected.Sum(v => (long)v));
        // 最大值不得超过 int 上限
        Assert.All(collected, v => Assert.True(v <= int.MaxValue));
    }

    /// <summary>负增量(进度回退)同样要按 int 上限分片。</summary>
    [Fact]
    public void 负向修正超过int上限时也分片()
    {
        var collected = new List<int>();
        var progress = new CollectingProgress(collected.Add);
        var method = typeof(FileDownloader).GetMethod(
            "ReportDelta",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        long delta = -(3L * 1024 * 1024 * 1024 + 123);
        method.Invoke(null, [progress, delta]);

        Assert.All(collected, v => Assert.True(v < 0));
        Assert.Equal(delta, collected.Sum(v => (long)v));
    }

    // ---------- 辅助 ----------

    /// <summary>收集进度回调的增量(用于断言总量)。</summary>
    private sealed class CollectingProgress(Action<int> onReport) : IProgress<int>
    {
        public void Report(int value) => onReport(value);
    }

    private FileDownloader NewDownloader(HttpClient http, TimeSpan idleTimeout) =>
        // retryDelayScale 压到极小:生产退避是 1/2/4/8 秒,测试不必真等
        new(http, logger: null, idleReadTimeout: idleTimeout, retryDelayScale: 0.001);

    private static GameFileEntry Entry(string path, byte[] payload) => new()
    {
        Path = path,
        Size = payload.Length,
        Md5 = Hash(payload),
        Url = "https://cdn.example.com/" + path,
    };

    private static string Hash(byte[] payload) => Convert.ToHexStringLower(MD5.HashData(payload));

    /// <summary>把已发生的请求渲染成可读文本,便于断言失败时定位。</summary>
    private static string Describe(List<RecordedRequest> requests) =>
        "[" + string.Join(", ", requests.Select(r => $"#{r.Attempt} {r.RangeFrom?.ToString() ?? "-"}~{r.RangeTo?.ToString() ?? "-"}")) + "]";

    private static HttpResponseMessage Ok(byte[] body, long? declaredLength = null) =>
        new(HttpStatusCode.OK) { Content = new StreamContent(body, declaredLength) };

    /// <summary>按请求的 Range 返回 206;无 Range 时返回整份内容。</summary>
    private static HttpResponseMessage RangeResponse(byte[] payload, RecordedRequest request)
    {
        if (request.RangeFrom is not long from)
        {
            return Ok(payload);
        }

        var to = Math.Min(request.RangeTo ?? payload.Length - 1, payload.Length - 1);
        var slice = payload[(int)from..((int)to + 1)];
        var content = new ByteArrayContent(slice);
        content.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(from, to, payload.Length);
        return new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = content };
    }

    private static HttpResponseMessage Stalled(long declaredLength) =>
        new(HttpStatusCode.OK) { Content = new StallingContent(declaredLength) };

    /// <summary>带「声明长度」的内容:长度可与实际吐出的字节数不一致,用于模拟连接被掐断。</summary>
    private sealed class StreamContent(byte[] body, long? declaredLength) : HttpContent
    {
        protected override Task<Stream> CreateContentReadStreamAsync() =>
            Task.FromResult<Stream>(new MemoryStream(body));

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            throw new NotSupportedException();

        protected override bool TryComputeLength(out long length)
        {
            length = declaredLength ?? body.Length;
            return true;
        }
    }

    /// <summary>响应头正常、响应体永不产出字节(直到被取消)—— 模拟 TCP 静默失效。</summary>
    private sealed class StallingContent : HttpContent
    {
        public StallingContent(long declaredLength) => Headers.ContentLength = declaredLength;

        protected override Task<Stream> CreateContentReadStreamAsync() =>
            Task.FromResult<Stream>(new StallStream());

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            throw new NotSupportedException();

        protected override bool TryComputeLength(out long length)
        {
            length = Headers.ContentLength ?? 0;
            return true;
        }
    }

    private sealed class StallStream : Stream
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed record RecordedRequest(long? RangeFrom, long? RangeTo, int Attempt);

    /// <summary>记录每次请求(含 Range 与第几次请求),由回调决定返回什么。</summary>
    private sealed class ScriptedHandler(Func<RecordedRequest, int, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public int Attempts { get; private set; }
        public List<RecordedRequest> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var range = request.Headers.Range?.Ranges.FirstOrDefault();
            var attempt = Attempts;
            var recorded = new RecordedRequest(range?.From, range?.To, attempt);
            Requests.Add(recorded);
            Attempts++;
            return Task.FromResult(responder(recorded, attempt));
        }
    }
}
