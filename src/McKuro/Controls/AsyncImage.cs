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

    /// <summary>
    /// 解码宽度上限(像素;0 = 按原始尺寸解码)。大幅图只作小图显示时应设置它:
    /// 原尺寸解码的位图同时占用进程内存与 GPU 纹理,一张 4K RGBA 约 31.6 MB,
    /// 还会挤占 128 MB LRU 预算(见 <see cref="Cache"/>)。取色/分享等需要全像素的场景保持 0。
    /// </summary>
    public static readonly StyledProperty<int> DecodeWidthProperty =
        AvaloniaProperty.Register<AsyncImage, int>(nameof(DecodeWidth));

    /// <summary>解码高度上限(像素;0 = 按原始尺寸解码)。与 <see cref="DecodeWidth"/> 同时为正时按更严格的缩放。</summary>
    public static readonly StyledProperty<int> DecodeHeightProperty =
        AvaloniaProperty.Register<AsyncImage, int>(nameof(DecodeHeight));

    /// <summary>解码宽度上限(0 = 原尺寸)。</summary>
    public int DecodeWidth
    {
        get => GetValue(DecodeWidthProperty);
        set => SetValue(DecodeWidthProperty, value);
    }

    /// <summary>解码高度上限(0 = 原尺寸)。</summary>
    public int DecodeHeight
    {
        get => GetValue(DecodeHeightProperty);
        set => SetValue(DecodeHeightProperty, value);
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
        // 解码尺寸变化需要重载:缓存键带尺寸,同 URL 的不同尺寸版本可共存
        DecodeWidthProperty.Changed.AddClassHandler<AsyncImage>((image, _) => image.OnDecodeSizeChanged());
        DecodeHeightProperty.Changed.AddClassHandler<AsyncImage>((image, _) => image.OnDecodeSizeChanged());
        SourceProperty.Changed.AddClassHandler<AsyncImage>((image, _) =>
        {
            // 任意路径(含外部直设 Source)都同步加载状态,占位层据此显隐
            image.ImageLoaded = image.Source is not null;
        });
    }

    /// <summary><see cref="SetDecodeBounds"/> 批量改宽高期间为 true:中途不重载,结束后统一重载一次。</summary>
    private bool _decodeBatch;

    /// <summary>
    /// 同时设置解码宽高上限,只触发一次重载。
    /// <para>
    /// 分开赋值 DecodeWidth、DecodeHeight 会各触发一次加载:中间那次按「新宽 + 旧高」解码,
    /// 白白多解一次大图,还在 LRU 里留下一份没人用的缓存项。
    /// </para>
    /// </summary>
    public void SetDecodeBounds(int width, int height)
    {
        if (width == DecodeWidth && height == DecodeHeight)
        {
            return;
        }
        _decodeBatch = true;
        try
        {
            DecodeWidth = width;
            DecodeHeight = height;
        }
        finally
        {
            _decodeBatch = false;
        }
        OnUrlChanged(ImageUrl);
    }

    private void OnDecodeSizeChanged()
    {
        if (!_decodeBatch)
        {
            OnUrlChanged(ImageUrl);
        }
    }

    private void OnUrlChanged(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            ImageLoaded = false;
            Source = null;
            return;
        }

        // 解码尺寸必须在 UI 线程一次性快照并向下传递:后台线程读 StyledProperty 不安全
        // (见 SetSourceOnUiIfCurrent 的说明),曾经因此在 LoadAsync 内直接读 DecodeWidth
        // 导致全部图片空白 —— 属性读取抛异常被兜底 catch 吞掉,Source 永远是 null。
        var (decodeW, decodeH) = (DecodeWidth, DecodeHeight);
        var key = CacheKey(url, decodeW, decodeH);

        if (Cache.TryGet(key, out var cached) && cached is not null)
        {
            Source = cached;
            return;
        }

        ImageLoaded = false;
        Source = null;
        _ = LoadAsync(url, decodeW, decodeH);
    }

    /// <summary>缓存键:URL + 解码尺寸。同 URL 按不同尺寸解码的版本(大图显示 vs 全尺寸取色)互不污染。</summary>
    private static string CacheKey(string url, int decodeWidth, int decodeHeight)
        => decodeWidth > 0 || decodeHeight > 0 ? $"{url}|decode={decodeWidth}x{decodeHeight}" : url;

    /// <summary>按解码尺寸上限解码位图;均为 0 时按原始尺寸。纯函数式入参,不读任何控件属性。</summary>
    private static Bitmap DecodeBitmap(byte[] bytes, int maxWidth, int maxHeight)
    {
        using var ms = new MemoryStream(bytes, writable: false);
        if (maxWidth > 0 && maxHeight > 0)
        {
            // 按更严格的维度缩放:保证解码结果两个方向都不超上限。
            // 先整解一次拿源尺寸 —— 只能如此:DecodeToWidth/Height 无法从流里直接得知
            // 另一维,而读已解位图的 PixelSize 是唯一可靠来源。
            using (var probe = new Bitmap(ms))
            {
                var srcW = probe.PixelSize.Width;
                var srcH = probe.PixelSize.Height;
                if (srcW <= 0 || srcH <= 0)
                {
                    return new Bitmap(ms);
                }
                var scale = Math.Min((double)maxWidth / srcW, (double)maxHeight / srcH);
                if (scale >= 1.0)
                {
                    // 源图本来就比显示尺寸小:不必放大,原样解码
                    ms.Position = 0;
                    return new Bitmap(ms);
                }
                var targetW = Math.Max(1, (int)Math.Round(srcW * scale));
                ms.Position = 0;
                return Bitmap.DecodeToWidth(ms, targetW);
            }
        }
        if (maxWidth > 0)
        {
            return Bitmap.DecodeToWidth(ms, maxWidth);
        }
        if (maxHeight > 0)
        {
            return Bitmap.DecodeToHeight(ms, maxHeight);
        }
        return new Bitmap(ms);
    }

    private async Task LoadAsync(string url, int decodeWidth, int decodeHeight)
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
                    if (Cache.TryGet(CacheKey(url, decodeWidth, decodeHeight), out var gateCached) && gateCached is not null)
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

            var bitmap = DecodeBitmap(bytes, decodeWidth, decodeHeight);
            Cache.Set(CacheKey(url, decodeWidth, decodeHeight), bitmap);

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