using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace McKuro.Controls;

/// <summary>
/// 图标着色模式(单色线稿图标用)。
/// </summary>
public enum IconTintMode
{
    /// <summary>不着色,原样显示(图片自带颜色的场景)。</summary>
    None,

    /// <summary>主题前景色:浅色主题近黑 <c>#1F2430</c>,暗色主题纯白 <c>#FFFFFF</c>。</summary>
    ThemeForeground,

    /// <summary>主题次级灰(未解锁等非强调场景):浅色主题 <c>#8A8F99</c>,暗色主题 <c>#9AA3B2</c>。</summary>
    ThemeMuted,
}

/// <summary>
/// 单色着色图标(CPU 像素重着色,确定性):把网络/本地图片的 RGB 整体替换为指定纯色,
/// <b>完整保留原 alpha</b>。
/// <para>
/// 用途:攻略站的技能/共鸣链图标是<b>白色线稿</b>(RGB 全 255,图形完全由 alpha 构成),
/// 直接显示在浅色主题的浅背景上几乎不可见。着色后可做到「浅色主题黑图标 / 暗色主题白图标」。
/// </para>
/// <para>
/// 实现:<see cref="AsyncImage"/> 负责下载/解码(复用其 LRU 缓存、并发闸门与线程安全),
/// 解码完成后在<b>后台线程</b>把像素缓冲重着色(<see cref="WriteableBitmap"/> +
/// <see cref="RecolorBgra"/>),再回 UI 线程显示。
/// </para>
/// <para>
/// <b>为什么不用 OpacityMask</b>:旧实现用 <see cref="ImageBrush"/> 作 <c>OpacityMask</c> 填充实色
/// <see cref="Border"/> —— 实测不生效(图标仍显示原白色)。CPU 重着色不依赖合成器行为,结果确定。
/// </para>
/// <para>
/// 注意:重着色只替换 RGB、不改 alpha。若把 alpha 抹成 255,白线稿会退化成"一整块实心方块"。
/// </para>
/// </summary>
public sealed class TintedAsyncImage : Grid
{
    /// <summary>
    /// 重着色结果缓存(URL + tint 色值 → 重着色任务),避免主题切换/滚动复用重复计算。
    /// <para>缓存"任务"而非"结果":同一 key 并发未命中(如主题切换时几十个图标同时重渲染)
    /// 共享同一次后台重着色,不再各起一份、产出互相覆盖的冗余位图(评审反馈)。</para>
    /// <para>淘汰不显式 Dispose 位图:缓存值可能仍被可见控件作为 Source 引用,盲目释放会白图;
    /// 图标尺寸小、条数上限 64,原生内存由 GC 终结兜底,总量有界。</para>
    /// </summary>
    private static readonly LruCache<(string Url, uint TintArgb), Task<Bitmap?>> TintCache =
        new(maxEntries: 64, maxWeight: 32L * 1024 * 1024);

    /// <summary>图片 URL(下载与缓存走 <see cref="AsyncImage"/> 的共享实现)。</summary>
    public static readonly StyledProperty<string> ImageUrlProperty =
        AvaloniaProperty.Register<TintedAsyncImage, string>(nameof(ImageUrl));

    /// <summary>着色模式(见 <see cref="IconTintMode"/>)。</summary>
    public static readonly StyledProperty<IconTintMode> TintProperty =
        AvaloniaProperty.Register<TintedAsyncImage, IconTintMode>(nameof(Tint));

    /// <summary>仅负责下载/解码的加载器:<b>不进视觉树</b>,避免旧图与着色图同时参与布局/合成。</summary>
    private readonly AsyncImage _loader;

    /// <summary>唯一可见的显示层(原图或已着色图)。</summary>
    private readonly Image _render;

    /// <summary>
    /// 重着色请求代号:每次 URL/着色模式/主题变化都自增,后台任务回填前校验代号,
    /// 过期结果(切换角色后旧请求才完成)直接丢弃。
    /// </summary>
    private int _generation;

    public TintedAsyncImage()
    {
        _loader = new AsyncImage
        {
            Stretch = Stretch.Uniform,
            IsHitTestVisible = false,
        };
        _loader.PropertyChanged += OnLoaderPropertyChanged;

        _render = new Image
        {
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            IsHitTestVisible = false,
        };
        Children.Add(_render);

        ActualThemeVariantChanged += OnThemeVariantChanged;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    /// <summary>要显示的图片 URL。</summary>
    public string ImageUrl
    {
        get => GetValue(ImageUrlProperty);
        set => SetValue(ImageUrlProperty, value);
    }

    /// <summary>着色模式。</summary>
    public IconTintMode Tint
    {
        get => GetValue(TintProperty);
        set => SetValue(TintProperty, value);
    }

    /// <summary>
    /// 着色模式 → 纯色(<see cref="IconTintMode.None"/> 返回 null = 不着色)。
    /// 纯函数,便于单测;颜色与项目既有约定一致(见 <c>ThemeIconBrushConverter</c>)。
    /// </summary>
    public static Color? ResolveTint(IconTintMode mode, bool isDarkTheme) => mode switch
    {
        IconTintMode.ThemeForeground => Color.Parse(isDarkTheme ? "#FFFFFF" : "#1F2430"),
        IconTintMode.ThemeMuted => Color.Parse(isDarkTheme ? "#9AA3B2" : "#8A8F99"),
        _ => null,
    };

    /// <summary>
    /// 纯函数:把 BGRA8888 像素缓冲重着色为单一色 —— RGB 全替换为 <paramref name="tint"/>,
    /// <b>alpha 原样保留</b>(白线稿的图形由 alpha 构成,保留 alpha 才能保留形状)。
    /// </summary>
    /// <param name="pixels">BGRA8888 像素缓冲(width/height/stride 描述其布局)。</param>
    /// <param name="width">像素宽;非正数时空操作。</param>
    /// <param name="height">像素高;非正数时空操作。</param>
    /// <param name="stride">每行字节数(可能大于 width*4;小于 width*4 时空操作)。</param>
    /// <param name="tint">目标色(其 A 分量不参与计算,alpha 一律取自输入像素)。</param>
    public static void RecolorBgra(Span<byte> pixels, int width, int height, int stride, Color tint)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var tightRow = (long)width * 4;
        if (stride < tightRow)
        {
            return;
        }

        // 缓冲装不下完整图像时空操作:绝不越界写
        var required = (long)(height - 1) * stride + tightRow;
        if (required > pixels.Length)
        {
            return;
        }

        byte b = tint.B;
        byte g = tint.G;
        byte r = tint.R;
        for (var y = 0; y < height; y++)
        {
            var row = y * stride;
            for (var x = 0; x < width; x++)
            {
                var i = row + x * 4;
                pixels[i] = b;
                pixels[i + 1] = g;
                pixels[i + 2] = r;
                // pixels[i + 3] = alpha:刻意不动 —— 形状来自 alpha
            }
        }
    }

    /// <summary>色彩 → 0xAARRGGBB(缓存键用;不依赖 Color.ToUInt32 的通道顺序约定)。</summary>
    private static uint ToArgb(Color c)
        => ((uint)c.A << 24) | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ImageUrlProperty)
        {
            // 复位全部内部状态:作废在途重着色、清掉上一张图,避免切换角色复用控件时残留旧状态
            Interlocked.Increment(ref _generation);
            _render.Source = null;
            _loader.ImageUrl = change.GetNewValue<string?>() ?? "";
        }
        else if (change.Property == TintProperty)
        {
            RenderCurrent();
        }
    }

    /// <summary>加载器解码完成(或清空)→ 按当前着色模式渲染。</summary>
    private void OnLoaderPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Image.SourceProperty)
        {
            RenderCurrent();
        }
    }

    /// <summary>主题切换(浅色/暗色)→ 重新解析目标色并重着色(缓存按色值分键,切回旧主题命中缓存)。</summary>
    private void OnThemeVariantChanged(object? sender, EventArgs e)
    {
        if (Tint != IconTintMode.None)
        {
            RenderCurrent();
        }
    }

    /// <summary>
    /// 重新挂载(列表虚拟化回收复用控件、页面切回)→ 重渲染一次。
    /// <para>必须补这一步:离开视觉树时作废了在途重着色,若不复算,控件会以"空白"状态回来
    /// (旧 OpacityMask 实现在 URL 变更时未复位 <c>Opacity</c>,正是同类残留问题的来源)。</para>
    /// </summary>
    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (_loader.Source is not null)
        {
            RenderCurrent();
        }
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
        // 离开视觉树:作废在途重着色,避免结果回填到已复用的控件实例
        => Interlocked.Increment(ref _generation);

    /// <summary>
    /// 是否仍有"未就绪的加载/着色"(分享截图前等待用,评审反馈):
    /// 本控件的下载器刻意不进视觉树,外层按 AsyncImage 扫描等图时**扫不到它**,
    /// 不暴露此信号就会把共鸣链等着色图标截成空白。
    /// URL 为空或显示层已有图(着色完成/回退原图)都算就绪;失败则等超时兜底(与 AsyncImage 同语义)。
    /// </summary>
    public bool HasPendingLoad =>
        !string.IsNullOrWhiteSpace(ImageUrl) && _render.Source is null;

    /// <summary>
    /// 按当前 URL / 着色模式 / 主题渲染一次。
    /// <para>只做调度:命中缓存立即上屏,未命中则起后台重着色任务。</para>
    /// </summary>
    private void RenderCurrent()
    {
        var gen = Interlocked.Increment(ref _generation);
        var source = _loader.Source as Bitmap;
        if (source is null)
        {
            _render.Source = null;
            return;
        }

        var url = ImageUrl ?? "";
        var mode = Tint;
        var tint = string.IsNullOrWhiteSpace(url) ? null : ResolveTint(mode, McKuro.ViewModels.ThemeHelper.IsDarkTheme());
        if (tint is null)
        {
            // None / 未知模式:原样显示(也覆盖"模式虽为着色但确实取不到色"的兜底)
            _render.Source = source;
            return;
        }

        var argb = ToArgb(tint.Value);
        var key = (url, argb);
        // 命中缓存(含他人在途任务)直接共享;未命中或旧任务已失败(结果为 null)则新起一次重着色,
        // 并立即入缓存 —— 同 key 并发渲染共享同一任务,不再重复整幅重着色(评审反馈)。
        if (!TintCache.TryGet(key, out var shared) || shared is null ||
            (shared.IsCompletedSuccessfully && shared.Result is null))
        {
            var src = source;
            var tintValue = tint.Value;
            shared = Task.Run(() => CreateTinted(src, tintValue));
            TintCache.Set(key, shared, weight: 0);
        }
        _ = ApplyAsync(gen, source, shared);
    }

    /// <summary>等待(可能是他人发起的)重着色任务并回填;全程不抛,失败回退原图。</summary>
    private async Task ApplyAsync(int gen, Bitmap source, Task<Bitmap?> task)
    {
        Bitmap? tinted;
        try
        {
            // 默认捕获上下文:续体回 UI 线程写 _render.Source
            tinted = await task;
        }
        catch (Exception)
        {
            tinted = null;
        }
        if (IsCurrent(gen))
        {
            _render.Source = tinted ?? source;
        }
    }

    /// <summary>请求代号仍是当前代号(未切换 URL/着色模式/主题、未离开视觉树)。</summary>
    private bool IsCurrent(int gen) => Volatile.Read(ref _generation) == gen;

    /// <summary>
    /// 生成已着色位图:把源位图整幅转成 Bgra8888 后,用纯函数替换 RGB(保留 alpha)。
    /// <para>
    /// 拷贝走 <see cref="Bitmap.CopyPixels(ILockedFramebuffer)"/> —— 由 Avalonia 负责像素格式/Alpha 格式
    /// 转换(源可能是 Rgba8888 或预乘 Alpha),避免手工猜布局。输出固定声明为
    /// <see cref="AlphaFormat.Unpremul"/>:绘制时由合成器按 alpha 现乘,半透明边缘(K线稿抗锯齿边)才不会发白/发黑。
    /// </para>
    /// </summary>
    private static Bitmap? CreateTinted(Bitmap source, Color tint)
    {
        var size = source.PixelSize;
        var width = size.Width;
        var height = size.Height;
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        WriteableBitmap? target = null;
        var keep = false;
        try
        {
            target = new WriteableBitmap(size, source.Dpi, PixelFormats.Bgra8888, AlphaFormat.Unpremul);
            using (var fb = target.Lock())
            {
                // 源 → 目标(整幅,自动转 Bgra8888 + Unpremul)
                source.CopyPixels(fb);

                var stride = fb.RowBytes;
                var length = (long)stride * height;
                if (stride < (long)width * 4 || length > int.MaxValue)
                {
                    return null;
                }

                // 锁定位图缓冲无法在安全代码里直接取 Span,故经托管数组中转
                var buffer = new byte[length];
                Marshal.Copy(fb.Address, buffer, 0, buffer.Length);
                RecolorBgra(buffer.AsSpan(), width, height, stride, tint);
                Marshal.Copy(buffer, 0, fb.Address, buffer.Length);
            }

            keep = true;
            return target;
        }
        catch (Exception)
        {
            // 平台不支持 WriteableBitmap / 拷贝失败:静默失败,由调用方回退原图
            return null;
        }
        finally
        {
            if (!keep)
            {
                target?.Dispose();
            }
        }
    }
}
