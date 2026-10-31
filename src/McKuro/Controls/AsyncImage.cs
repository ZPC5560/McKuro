using System.IO;
using System.Net.Http;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace McKuro.Controls;

/// <summary>
/// 异步加载网络图片的 <see cref="Avalonia.Controls.Image"/>(AOT 安全,带进程内 LRU 字节上限缓存)。
/// 设置 <see cref="ImageUrl"/> 后自动下载并在解码完成后显示;失败或空 URL 时保持空白。
/// <para>
/// 缓存上限 <b>200 张图 / 128 MB</b>(可在启动期通过 <see cref="ConfigureCache"/> 调整),
/// 超出时按 LRU 淘汰。
/// </para>
/// </summary>
public sealed class AsyncImage : Avalonia.Controls.Image
{
    /// <summary>
    /// 惰性创建:首次下载图片时才探测系统代理(避免静态初始化早于 App 启动、也不在类型加载时跑 scutil)。
    /// </summary>
    private static readonly Lazy<HttpClient> LazyHttp = new(CreateClient);

    private static HttpClient Http => LazyHttp.Value;

    /// <summary>单张图片下载上限(防异常/恶意响应撑爆内存;角色立绘/横幅远小于此)。</summary>
    internal const long MaxImageBytes = 20L * 1024 * 1024;

    /// <summary>并发下载上限,防止角色列表/详情大量图片同时下载造成卡顿(WutheringWavesTool 用本地缓冲 + 虚拟化)。</summary>
    private static readonly SemaphoreSlim DownloadGate = new(initialCount: 6, maxCount: 6);

    /// <summary>
    /// 全局 LRU 缓存(默认 200 项 / 128 MB)。可通过 <see cref="ConfigureCache"/> 替换上限。
    /// 内部使用 <see cref="ImageBitmapLruCache"/>,线程安全。
    /// </summary>
    public static ImageBitmapLruCache Cache { get; private set; } = new(maxEntries: 200, maxBytes: 128L * 1024 * 1024);

    /// <summary>在启动期调整全局缓存上限;运行时改不影响已加载的位图。</summary>
    public static void ConfigureCache(int maxEntries, long maxBytes)
        => Cache = new ImageBitmapLruCache(maxEntries, maxBytes);

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };
        // 系统代理:macOS 上 HttpClient.DefaultProxy 不读系统代理,与 AppServices 的共享客户端一样显式注入
        if (McKuro.Core.Services.Infrastructure.SystemProxyDetector.Detect() is { } proxy)
        {
            handler.Proxy = proxy;
        }
        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(15),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("McKuro-launcher/1.0");
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

    /// <summary>图片是否已成功解码显示(加载中/失败/空 URL 均为 false,供占位层绑定)。
    /// 命名为 ImageLoaded 以避免与基类 Control.IsLoaded(可视树加载状态)隐藏冲突。</summary>
    public static readonly StyledProperty<bool> ImageLoadedProperty =
        AvaloniaProperty.Register<AsyncImage, bool>(nameof(ImageLoaded));

    /// <summary>图片是否已成功解码显示。</summary>
    public bool ImageLoaded
    {
        get => GetValue(ImageLoadedProperty);
        set => SetValue(ImageLoadedProperty, value);
    }

    static AsyncImage()
    {
        ImageUrlProperty.Changed.AddClassHandler<AsyncImage>((image, e) => image.OnUrlChanged((string)e.NewValue!));
        SourceProperty.Changed.AddClassHandler<AsyncImage>((image, _) =>
        {
            // 任意路径(含外部直设 Source)都同步加载状态,占位层据此显隐
            image.ImageLoaded = image.Source is not null;
        });
    }

    private void OnUrlChanged(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            ImageLoaded = false;
            Source = null;
            return;
        }

        if (Cache.TryGet(url, out var cached) && cached is not null)
        {
            Source = cached;
            return;
        }

        ImageLoaded = false;
        Source = null;
        _ = LoadAsync(url);
    }

    private async Task LoadAsync(string url)
    {
        try
        {
            // 本地文件路径(如 Assets/attr/*.png)直接读,不走网络并发闸门
            var isHttp = url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                         || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
            byte[] bytes;
            if (isHttp)
            {
                await DownloadGate.WaitAsync().ConfigureAwait(false);
                try
                {
                    // 闸门内再检一次缓存(避免重复下载)
                    if (Cache.TryGet(url, out var gateCached) && gateCached is not null)
                    {
                        await SetSourceOnUiIfCurrent(url, gateCached);
                        return;
                    }
                    bytes = await DownloadCappedAsync(url).ConfigureAwait(false);
                }
                finally
                {
                    DownloadGate.Release();
                }
            }
            else
            {
                bytes = await Task.Run(() => File.ReadAllBytes(url)).ConfigureAwait(false);
            }

            if (bytes.Length == 0)
            {
                return;
            }

            using var ms = new MemoryStream(bytes, writable: false);
            var bitmap = new Bitmap(ms);
            Cache.Set(url, bitmap);

            // 必须回到 UI 线程再读 ImageUrl 并设置 Source:
            // Avalonia StyledProperty 跨线程读取不安全,后台线程判断会失败导致图片不实时显示
            await SetSourceOnUiIfCurrent(url, bitmap);
        }
        catch (Exception)
        {
            // 网络/文件失败:保持空白,不抛到 UI
        }
    }

    /// <summary>下载图片字节,超过 <see cref="MaxImageBytes"/> 时放弃(返回空数组 = 保持空白)。</summary>
    private static async Task<byte[]> DownloadCappedAsync(string url)
    {
        using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        if (resp.Content.Headers.ContentLength is long declared && declared > MaxImageBytes)
        {
            return [];
        }
        await using var src = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var ms = new MemoryStream();
        var buffer = new byte[81920];
        int n;
        while ((n = await src.ReadAsync(buffer).ConfigureAwait(false)) > 0)
        {
            if (ms.Length + n > MaxImageBytes)
            {
                return [];
            }
            ms.Write(buffer, 0, n);
        }
        return ms.ToArray();
    }

    /// <summary>在 UI 线程校验 ImageUrl 仍是目标 URL 后设置 Source(线程安全 + 避免过期赋值)。</summary>
    private async Task SetSourceOnUiIfCurrent(string url, Bitmap bitmap)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (string.Equals(ImageUrl, url, StringComparison.Ordinal))
            {
                Source = bitmap;
            }
        });
    }
}