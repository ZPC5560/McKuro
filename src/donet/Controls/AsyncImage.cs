using System.Net.Http;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace donet.Controls;

/// <summary>
/// 异步加载网络图片的 <see cref="Avalonia.Controls.Image"/>(AOT 安全,带进程内内存缓存)。
/// 设置 <see cref="ImageUrl"/> 后自动下载并在解码完成后显示;失败或空 URL 时保持空白。
/// </summary>
public sealed class AsyncImage : Avalonia.Controls.Image
{
    private static readonly HttpClient Http = CreateClient();

    private static readonly Dictionary<string, Bitmap> Cache = new(StringComparer.Ordinal);

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };
        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(15),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("donet-launcher/1.0");
        return client;
    }

    public static readonly StyledProperty<string> ImageUrlProperty =
        AvaloniaProperty.Register<AsyncImage, string>(nameof(ImageUrl));

    /// <summary>要异步加载的图片 URL。</summary>
    public string ImageUrl
    {
        get => GetValue(ImageUrlProperty);
        set => SetValue(ImageUrlProperty, value);
    }

    static AsyncImage()
    {
        ImageUrlProperty.Changed.AddClassHandler<AsyncImage>((image, e) => image.OnUrlChanged((string)e.NewValue!));
    }

    private void OnUrlChanged(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            Source = null;
            return;
        }

        if (Cache.TryGetValue(url, out var cached))
        {
            Source = cached;
            return;
        }

        Source = null;
        _ = LoadAsync(url);
    }

    private async Task LoadAsync(string url)
    {
        try
        {
            var bytes = await Http.GetByteArrayAsync(url).ConfigureAwait(false);
            if (bytes.Length == 0)
            {
                return;
            }

            using var ms = new MemoryStream(bytes, writable: false);
            var bitmap = new Bitmap(ms);
            lock (Cache)
            {
                Cache[url] = bitmap;
            }

            if (string.Equals(ImageUrl, url, StringComparison.Ordinal))
            {
                await Dispatcher.UIThread.InvokeAsync(() => Source = bitmap);
            }
        }
        catch (Exception)
        {
            // 网络失败:保持空白,不抛到 UI
        }
    }
}
