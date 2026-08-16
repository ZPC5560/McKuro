using Avalonia.Media.Imaging;

namespace McKuro.Controls;

/// <summary>
/// 进程内图像缓存(基于 <see cref="LruCache{TKey,TValue}"/>)。
/// 默认上限 <b>200 张 / 128 MB</b>(按 BGRA8888 像素估算),超出按 LRU 淘汰。
/// 线程安全。
/// </summary>
public sealed class ImageBitmapLruCache
{
    private readonly LruCache<string, Bitmap> _cache;

    public ImageBitmapLruCache(int maxEntries = 200, long maxBytes = 128L * 1024 * 1024)
    {
        _cache = new LruCache<string, Bitmap>(maxEntries, maxBytes);
    }

    public int MaxEntries => _cache.MaxEntries;
    public long MaxBytes => _cache.MaxWeight;

    public int Count => _cache.Count;
    public long CurrentBytes => _cache.CurrentWeight;

    public bool TryGet(string key, out Bitmap? bitmap) => _cache.TryGet(key, out bitmap);

    /// <summary>加入缓存;权重按像素尺寸估算(width*height*4)。</summary>
    public void Set(string key, Bitmap bitmap)
    {
        _cache.Set(key, bitmap, EstimateBytes(bitmap));
    }

    /// <summary>带显式权重的加入方法,便于测试与自定义权重策略。</summary>
    public void Set(string key, Bitmap bitmap, long weight) => _cache.Set(key, bitmap, weight);

    public void Clear() => _cache.Clear();

    private static long EstimateBytes(Bitmap bmp)
    {
        try
        {
            // BGRA8888 = 4 字节/像素
            return (long)bmp.PixelSize.Width * bmp.PixelSize.Height * 4;
        }
        catch
        {
            return 0;
        }
    }
}
