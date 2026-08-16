using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace McKuro.Services;

/// <summary>
/// 位图主色提取(参照 WutheringWavesTool ImgColorBgTask 的 ColorThief 取色:
/// 降采样 + 量化统计,返回出现最多的前 N 个非黑白主色,用于详情卡片渐变背景)。
/// 纯托管实现,兼容 Native AOT。
/// </summary>
public static class ColorThiefHelper
{
    /// <summary>
    /// 从位图提取前 <paramref name="count"/> 个主色(降采样后按量化色桶出现次数排序)。
    /// </summary>
    /// <returns>按出现次数降序的主色列表(可能少于 count,解析失败返回空)。</returns>
    public static List<Color> GetDominantColors(Bitmap bitmap, int count = 3)
    {
        if (bitmap is null || count <= 0)
        {
            return [];
        }
        try
        {
            int srcW = bitmap.PixelSize.Width;
            int srcH = bitmap.PixelSize.Height;
            if (srcW <= 0 || srcH <= 0)
            {
                return [];
            }

            // 降采样到最大 ~64px,减少取色成本(对齐 ColorThief quality 采样思想)
            const int maxDim = 64;
            double scale = Math.Min(1.0, maxDim / (double)Math.Max(srcW, srcH));
            int w = Math.Max(1, (int)Math.Round(srcW * scale));
            int h = Math.Max(1, (int)Math.Round(srcH * scale));

            using var resized = bitmap.CreateScaledBitmap(new PixelSize(w, h), BitmapInterpolationMode.MediumQuality);

            var wb = new WriteableBitmap(
                new PixelSize(w, h),
                new Avalonia.Vector(96, 96),
                Avalonia.Platform.PixelFormat.Bgra8888,
                Avalonia.Platform.AlphaFormat.Premul);
            try
            {
                using var fb = wb.Lock();
                resized.CopyPixels(new PixelRect(0, 0, w, h), fb.Address, fb.RowBytes * h, fb.RowBytes);
                return FromBgraBytes(fb.Address, w, h, fb.RowBytes, count);
            }
            finally
            {
                wb.Dispose();
            }
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>
    /// 纯算法:从 BGRA 像素缓冲提取主色(可脱离渲染平台单测)。
    /// </summary>
    /// <param name="pixels">BGRA8888 像素缓冲首地址。</param>
    /// <param name="w">宽。</param>
    /// <param name="h">高。</param>
    /// <param name="stride">行字节数。</param>
    public static List<Color> FromBgraBytes(IntPtr pixels, int w, int h, int stride, int count = 3)
    {
        if (pixels == IntPtr.Zero || w <= 0 || h <= 0 || count <= 0)
        {
            return [];
        }

        // 量化:取 RGB 高 4 位作为桶键,统计出现次数,忽略近黑/近白
        var bucketCount = new Dictionary<uint, int>();
        var bucketRgb = new Dictionary<uint, (int R, int G, int B)>();

        var basePtr = pixels.ToInt64();
        for (int y = 0; y < h; y++)
        {
            long row = basePtr + (long)y * stride;
            for (int x = 0; x < w; x++)
            {
                int b = System.Runtime.InteropServices.Marshal.ReadByte((IntPtr)row, x * 4 + 0);
                int g = System.Runtime.InteropServices.Marshal.ReadByte((IntPtr)row, x * 4 + 1);
                int r = System.Runtime.InteropServices.Marshal.ReadByte((IntPtr)row, x * 4 + 2);
                // 近黑/近白忽略(角色图一般不透明,不额外判断 alpha)
                int lum = (r + g + b) / 3;
                if (lum < 20 || lum > 235)
                {
                    continue;
                }
                uint key = ((uint)(r >> 4) << 8) | ((uint)(g >> 4) << 4) | (uint)(b >> 4);
                bucketCount.TryGetValue(key, out var c);
                bucketCount[key] = c + 1;
                if (!bucketRgb.ContainsKey(key))
                {
                    bucketRgb[key] = (r, g, b);
                }
            }
        }

        return bucketCount
            .OrderByDescending(kv => kv.Value)
            .Take(count)
            .Select(kv =>
            {
                var (r, g, b) = bucketRgb[kv.Key];
                return Color.FromRgb((byte)r, (byte)g, (byte)b);
            })
            .ToList();
    }
}
