using System.Net;
using System.Text;
using McKuro.Core.Models.Game;
using McKuro.Core.Services.Game;

namespace McKuro.Tests;

public sealed class FileDownloaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "McKuro_download_" + Guid.NewGuid().ToString("N"));

    public FileDownloaderTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // Ignore test cleanup failures.
        }
    }

    [Fact]
    public async Task DownloadAsync_ResumesPartFileWhenServerHonorsRange()
    {
        var payload = Encoding.UTF8.GetBytes("abcdefghijklmnopqrstuvwxyz");
        using var http = new HttpClient(new RangeHandler(payload));
        var entry = Entry("data.bin", payload);
        var destination = Path.Combine(_root, "data.bin");
        await File.WriteAllBytesAsync(destination + ".part", payload[..10]);

        var result = await new FileDownloader(http).DownloadAsync(entry, "", destination);

        Assert.True(result.Success, result.Error);
        Assert.Equal(payload, await File.ReadAllBytesAsync(destination));
    }

    [Fact]
    public async Task DownloadAsync_OverwritesPartFileWhenServerIgnoresRange()
    {
        var payload = Encoding.UTF8.GetBytes("abcdefghijklmnopqrstuvwxyz");
        using var http = new HttpClient(new RangeHandler(payload, ignoreRange: true));
        var entry = Entry("data.bin", payload);
        var destination = Path.Combine(_root, "data.bin");
        await File.WriteAllBytesAsync(destination + ".part", payload[..10]);

        var result = await new FileDownloader(http).DownloadAsync(entry, "", destination);

        Assert.True(result.Success, result.Error);
        Assert.Equal(payload, await File.ReadAllBytesAsync(destination));
    }

    [Fact]
    public async Task DownloadAsync_RangeChunksVerifyAndComplete()
    {
        var payload = Encoding.UTF8.GetBytes("abcdefghijklmnopqrstuvwxyz");
        using var http = new HttpClient(new RangeHandler(payload));
        var entry = Entry("data.bin", payload);
        entry.ChunkInfos.Add(new GameChunkInfo
        {
            Start = 0,
            End = 12,
            Md5 = Hash(payload[..13]),
        });
        entry.ChunkInfos.Add(new GameChunkInfo
        {
            Start = 13,
            End = payload.Length - 1,
            Md5 = Hash(payload[13..]),
        });
        var destination = Path.Combine(_root, "data.bin");

        var result = await new FileDownloader(http).DownloadAsync(entry, "", destination);

        Assert.True(result.Success, result.Error);
        Assert.Equal(payload, await File.ReadAllBytesAsync(destination));
    }

    /// <summary>
    /// <b>核心回归(对齐 Haiyu 1.4.5):分片完整覆盖时不再整文件复算 MD5。</b>
    /// <para>
    /// 手法:故意把清单里的<b>整文件 md5 写成错的</b>,只让分片哈希正确。
    /// 完整覆盖时应当<b>成功</b>(说明确实没走整文件 MD5);
    /// 覆盖不完整时应当<b>失败</b>(说明仍老老实实走整文件 MD5,没有放宽)。
    /// </para>
    /// <para>
    /// 为什么值得省这一步:实测鸣潮 3.7.1 主清单里 76 个带分片的文件共 <b>78.54 GB</b>,
    /// 且全部满足完整覆盖 —— 每次安装都白读这么多数据。
    /// </para>
    /// </summary>
    [Fact]
    public async Task DownloadAsync_CompleteChunkCoverage_SkipsWholeFileMd5()
    {
        var payload = Encoding.UTF8.GetBytes("abcdefghijklmnopqrstuvwxyz");
        using var http = new HttpClient(new RangeHandler(payload));
        var entry = Entry("data.bin", payload);
        entry.Md5 = "ffffffffffffffffffffffffffffffff"; // 故意错的整文件 md5
        entry.ChunkInfos.Add(new GameChunkInfo { Start = 0, End = 12, Md5 = Hash(payload[..13]) });
        entry.ChunkInfos.Add(new GameChunkInfo { Start = 13, End = payload.Length - 1, Md5 = Hash(payload[13..]) });
        var destination = Path.Combine(_root, "skip-md5.bin");

        var result = await new FileDownloader(http).DownloadAsync(entry, "", destination);

        Assert.True(result.Success, result.Error);
        Assert.True(result.HashVerified);
        Assert.Equal(payload, await File.ReadAllBytesAsync(destination));
    }

    /// <summary>
    /// 对照:分片<b>未完整覆盖</b>时仍必须走整文件 MD5 —— 错误的 md5 要被判出来。
    /// 这条测试守住"跳过"的边界,防止将来把判定放宽成"有分片就跳过"。
    /// </summary>
    [Fact]
    public async Task DownloadAsync_IncompleteChunkCoverage_StillUsesWholeFileMd5()
    {
        var payload = Encoding.UTF8.GetBytes("abcdefghijklmnopqrstuvwxyz");
        using var http = new HttpClient(new RangeHandler(payload));
        var entry = Entry("data.bin", payload);
        entry.Md5 = "ffffffffffffffffffffffffffffffff"; // 错误的整文件 md5
        // 只声明前 13 字节的分片,后 13 字节没有任何分片覆盖 → 不算完整覆盖
        entry.ChunkInfos.Add(new GameChunkInfo { Start = 0, End = 12, Md5 = Hash(payload[..13]) });
        var destination = Path.Combine(_root, "no-skip-md5.bin");

        var result = await new FileDownloader(http).DownloadAsync(entry, "", destination);

        Assert.False(result.Success);
        Assert.False(File.Exists(destination));
    }

    /// <summary>
    /// 完整覆盖但分片内容被篡改时仍必须失败 —— 跳过的是"重复读盘",不是"校验"。
    /// </summary>
    [Fact]
    public async Task DownloadAsync_CompleteCoverageWithBadChunk_StillFails()
    {
        var payload = Encoding.UTF8.GetBytes("abcdefghijklmnopqrstuvwxyz");
        var entry = Entry("data.bin", payload);
        entry.Md5 = "ffffffffffffffffffffffffffffffff";
        entry.ChunkInfos.Add(new GameChunkInfo { Start = 0, End = 12, Md5 = Hash(payload[..13]) });
        entry.ChunkInfos.Add(new GameChunkInfo { Start = 13, End = payload.Length - 1, Md5 = Hash(payload[13..]) });

        // 服务端返回被篡改的内容:分片校验必须发现,不能因为"跳过整文件 md5"而放行
        var tampered = (byte[])payload.Clone();
        tampered[5] ^= 0xFF;
        using var badHttp = new HttpClient(new RangeHandler(tampered));
        var destination = Path.Combine(_root, "bad-chunk.bin");

        var result = await new FileDownloader(badHttp).DownloadAsync(entry, "", destination);

        Assert.False(result.Success);
    }

    /// <summary>
    /// L2:默认仍"目标已匹配即跳过";调用方已用 ComputeDiff 判定过期时传 skipExistingCheck,
    /// 跳过对旧文件的整文件 MD5,直接下载。
    /// </summary>
    [Fact]
    public async Task DownloadAsync_SkipExistingCheck_DownloadsEvenWhenTargetMatches()
    {
        var payload = Encoding.UTF8.GetBytes("abcdefghijklmnopqrstuvwxyz");
        var entry = Entry("data.bin", payload);
        var destination = Path.Combine(_root, "data.bin");
        await File.WriteAllBytesAsync(destination, payload);

        var defaultHandler = new RangeHandler(payload);
        using (var http = new HttpClient(defaultHandler))
        {
            var skipped = await new FileDownloader(http).DownloadAsync(entry, "", destination);
            Assert.True(skipped.Success, skipped.Error);
        }
        Assert.Equal(0, defaultHandler.Requests);

        var forcedHandler = new RangeHandler(payload);
        using (var http = new HttpClient(forcedHandler))
        {
            var forced = await new FileDownloader(http).DownloadAsync(entry, "", destination, skipExistingCheck: true);
            Assert.True(forced.Success, forced.Error);
        }
        Assert.True(forcedHandler.Requests > 0);
        Assert.Equal(payload, await File.ReadAllBytesAsync(destination));
    }

    /// <summary>
    /// L1 不能误伤续传:.part 里已完整写好的分片(落在预分配前的原有长度内)仍要预检并跳过,
    /// 只请求缺的那一片。
    /// </summary>
    [Fact]
    public async Task DownloadAsync_ChunkResume_OnlyFetchesMissingChunk()
    {
        var payload = Encoding.UTF8.GetBytes("abcdefghijklmnopqrstuvwxyz");
        var entry = Entry("data.bin", payload);
        entry.ChunkInfos.Add(new GameChunkInfo { Start = 0, End = 12, Md5 = Hash(payload[..13]) });
        entry.ChunkInfos.Add(new GameChunkInfo { Start = 13, End = payload.Length - 1, Md5 = Hash(payload[13..]) });
        var destination = Path.Combine(_root, "resume.bin");
        await File.WriteAllBytesAsync(destination + ".part", payload[..13]);
        var handler = new RangeHandler(payload);
        using var http = new HttpClient(handler);

        var result = await new FileDownloader(http).DownloadAsync(entry, "", destination);

        Assert.True(result.Success, result.Error);
        Assert.Equal(1, handler.Requests);
        Assert.Equal(payload, await File.ReadAllBytesAsync(destination));
    }

    private static GameFileEntry Entry(string path, byte[] payload) => new()
    {
        Path = path,
        Size = payload.Length,
        Md5 = Hash(payload),
        Url = "https://cdn.example.com/" + path,
    };

    private static string Hash(byte[] payload) => Convert.ToHexStringLower(System.Security.Cryptography.MD5.HashData(payload));

    private sealed class RangeHandler(byte[] payload, bool ignoreRange = false) : HttpMessageHandler
    {
        /// <summary>收到的请求数(用于断言"没有/只有部分"走网络)。</summary>
        public int Requests;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Requests);
            var range = request.Headers.Range?.Ranges.SingleOrDefault();
            if (!ignoreRange && range?.From is long start)
            {
                var end = Math.Min(range.To ?? payload.Length - 1, payload.Length - 1);
                var bytes = payload[(int)start..((int)end + 1)];
                var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
                {
                    Content = new ByteArrayContent(bytes),
                };
                response.Content.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(start, end, payload.Length);
                return Task.FromResult(response);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(payload),
            });
        }
    }
}
