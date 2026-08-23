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
    /// <summary>
    /// 从位图提取最鲜明的出现频次主色(甘特条/强调色用):
    /// 取出现次数前 5 的候选,按「饱和度 × 亮度适中权重 − 名次惩罚」评分选最优,
    /// 避免取到大面积暗灰背景而非图片的主题色。失败返回 null。
    /// </summary>
    public static Color? GetVividDominantColor(Bitmap bitmap)
    {
        var candidates = GetDominantColors(bitmap, 5);
        return candidates.Count == 0 ? null : PickVivid(candidates);
    }

    /// <summary>
    /// 从候选主色(已按出现次数降序)中选最鲜明的颜色:
    /// score = HSV 饱和度 × 亮度权重 − 名次惩罚(每退后一名 −0.12)。
    /// 亮度权重 = 1 − 0.35×|2×亮度−1|(对称轻微惩罚极端亮/暗,权重 0.65~1);
    /// 饱和度是第一驱动:高饱和主题色可逆袭大面积暗灰背景色,同饱和时出现次数优先。
    /// </summary>
    public static Color PickVivid(IReadOnlyList<Color> candidates)
    {
        var best = candidates[0];
        double bestScore = double.NegativeInfinity;
        for (int i = 0; i < candidates.Count; i++)
        {
            var c = candidates[i];
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double sat = max <= 0 ? 0 : (max - min) / max;
            double lum = 0.2126 * r + 0.7152 * g + 0.0722 * b;
            double lumWeight = 1.0 - 0.35 * Math.Abs(2 * lum - 1);
            double score = sat * lumWeight - i * 0.12;
            if (score > bestScore)
            {
                bestScore = score;
                best = c;
            }
        }
        return best;
    }

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
