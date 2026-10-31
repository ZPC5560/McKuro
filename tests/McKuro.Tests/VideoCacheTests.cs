using System.Net;
using McKuro.Services;

namespace McKuro.Tests;

/// <summary>
/// 背景视频缓存:原子落盘(失败/取消不留半截文件)与目录清理(旧格式、过期、总量上限)。
/// </summary>
public sealed class VideoCacheTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "McKuro-videocache-" + Guid.NewGuid().ToString("N"));

    public VideoCacheTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public async Task Download_Writes_Complete_File_And_Leaves_No_Temp()
    {
        var payload = Enumerable.Range(0, 300_000).Select(i => (byte)i).ToArray();
        using var http = new HttpClient(new StubHandler(() => new ByteArrayContent(payload)));
        var dest = VideoCache.PathFor(_dir, "https://cdn.example/a.mp4");

        var ok = await VideoCache.DownloadAsync(http, "https://cdn.example/a.mp4", dest, CancellationToken.None);

        Assert.True(ok);
        Assert.Equal(payload, await File.ReadAllBytesAsync(dest));
        Assert.Single(Directory.GetFiles(_dir));
    }

    [Fact]
    public async Task Download_Failure_Midway_Leaves_No_File()
    {
        using var http = new HttpClient(new StubHandler(() => new StreamContent(new FailingStream(100_000))));
        var dest = VideoCache.PathFor(_dir, "https://cdn.example/b.mp4");

        await Assert.ThrowsAnyAsync<IOException>(
            () => VideoCache.DownloadAsync(http, "https://cdn.example/b.mp4", dest, CancellationToken.None));

        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public async Task Download_Cancelled_Leaves_No_File()
    {
        using var cts = new CancellationTokenSource();
        using var http = new HttpClient(new StubHandler(() => new StreamContent(new FailingStream(100_000, onEnd: cts.Cancel))));
        var dest = VideoCache.PathFor(_dir, "https://cdn.example/c.mp4");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => VideoCache.DownloadAsync(http, "https://cdn.example/c.mp4", dest, cts.Token));

        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public async Task Download_Oversized_Or_Empty_Returns_False_Without_File()
    {
        using var big = new HttpClient(new StubHandler(() => new ByteArrayContent(new byte[2048])));
        var dest = VideoCache.PathFor(_dir, "https://cdn.example/d.mp4");
        Assert.False(await VideoCache.DownloadAsync(big, "https://cdn.example/d.mp4", dest, CancellationToken.None, maxBytes: 1024));

        using var empty = new HttpClient(new StubHandler(() => new ByteArrayContent([])));
        Assert.False(await VideoCache.DownloadAsync(empty, "https://cdn.example/d.mp4", dest, CancellationToken.None));

        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public void Prune_Removes_Legacy_Expired_And_Over_Cap_But_Keeps_Current()
    {
        var now = DateTime.UtcNow;
        var legacy = Write("ABCDEF.mp4", 10, now);                         // 旧格式(可能是截断文件)
        var expired = Write("OLD" + VideoCache.FileSuffix, 10, now.AddDays(-40));
        var staleTmp = Write("X" + VideoCache.FileSuffix + ".tmp-1", 10, now.AddDays(-2));
        var freshTmp = Write("Y" + VideoCache.FileSuffix + ".tmp-2", 10, now);  // 可能正在下载
        var newest = Write("N1" + VideoCache.FileSuffix, 400, now);
        var older = Write("N2" + VideoCache.FileSuffix, 400, now.AddHours(-1));
        var oldest = Write("N3" + VideoCache.FileSuffix, 400, now.AddHours(-2));

        VideoCache.Prune(_dir, now, keepPath: oldest, maxTotalBytes: 900);

        Assert.False(File.Exists(legacy));
        Assert.False(File.Exists(expired));
        Assert.False(File.Exists(staleTmp));
        Assert.True(File.Exists(freshTmp));
        Assert.True(File.Exists(newest));
        Assert.False(File.Exists(older));   // 超出总量上限:按最近使用时间淘汰
        Assert.True(File.Exists(oldest));   // 当前要播放的文件永不删除
    }

    private string Write(string name, int size, DateTime lastWriteUtc)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, new byte[size]);
        File.SetLastWriteTimeUtc(path, lastWriteUtc);
        return path;
    }

    private sealed class StubHandler(Func<HttpContent> content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content() });
    }

    /// <summary>先吐出 <c>bytes</c> 个字节,然后抛 IOException(模拟连接中断);可选在抛出前回调(模拟取消)。</summary>
    private sealed class FailingStream(int bytes, Action? onEnd = null) : Stream
    {
        private int _left = bytes;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_left <= 0)
            {
                if (onEnd is not null)
                {
                    onEnd();
                    throw new OperationCanceledException();
                }
                throw new IOException("connection reset");
            }
            var n = Math.Min(count, _left);
            _left -= n;
            return n;
        }
    }
}
