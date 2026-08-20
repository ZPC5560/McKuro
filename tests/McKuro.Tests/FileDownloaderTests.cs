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
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
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
