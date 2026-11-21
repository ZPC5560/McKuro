using McKuro.Controls;
using McKuro.Services;

namespace McKuro.Tests;

/// <summary>
/// 内存占用优化的结构性回归守卫。
/// <para>
/// 背景:实测(裸 Avalonia 同后端基线 85–100MB)确认 610MB 的专用工作集不是框架泄漏,
/// 而是「页面常驻 + 媒体常驻 + 缓存预算」堆出来的高位平台。这里把三项已经验证有效的
/// 降占用措施固定在源码契约上,防止后续重构无意中把它们撤掉。
/// </para>
/// </summary>
public sealed class MemoryFootprintTests
{
    private static string RepositoryRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && dir is not null; i++)
        {
            if (Directory.Exists(Path.Combine(dir, "src", "McKuro")))
            {
                return dir;
            }
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new DirectoryNotFoundException("未能定位仓库根(src/McKuro 所在目录)");
    }

    private static string Source(string relative)
        => File.ReadAllText(Path.Combine(RepositoryRoot(), relative.Replace('/', Path.DirectorySeparatorChar)));

    [Fact]
    public void 图片缓存预算不得回到128MB()
    {
        // 这份缓存是解码后的 BGRA 位图,常驻私有内存、切页不回落。
        // 实测把它从 200 项/128MB 收到 128 项/64MB 后,稳态专用工作集下降约 40MB。
        Assert.Equal(64L * 1024 * 1024, AsyncImage.DefaultMaxBytes);
        Assert.Equal(128, AsyncImage.DefaultMaxEntries);
        Assert.Equal(AsyncImage.DefaultMaxBytes, AsyncImage.Cache.MaxBytes);
        Assert.Equal(AsyncImage.DefaultMaxEntries, AsyncImage.Cache.MaxEntries);

        // 逐项权重估算必须是 BGRA8888(4 字节/像素),否则字节预算形同虚设
        var cacheSource = Source("src/McKuro/Controls/ImageBitmapLruCache.cs");
        Assert.Contains("PixelSize.Width * bmp.PixelSize.Height * 4", cacheSource);
    }

    [Fact]
    public void 空闲交还工作集只在失焦后触发()
    {
        var source = Source("src/McKuro/Services/IdleWorkingSetTrimmer.cs");

        // 有焦点时裁工作集会让下一次滚动/切页立刻把页读回来(表现为卡顿),
        // 所以只有 Deactivated 起计时、Activated 取消。
        Assert.Contains("public void OnDeactivated()", source);
        Assert.Contains("public void OnActivated()", source);
        Assert.Contains("_idle = false;", source);

        // 必须是「一次性」计时,不能变成周期性裁剪(会反复换页)
        Assert.Contains("_timer.Stop();", source);
        Assert.Contains("DispatcherTimer", source);

        // 冷却期:短时间内反复失焦/激活不得连续裁剪
        Assert.Contains("_cooldown", source);
        Assert.Contains("_lastTrimUtc", source);
    }

    [Fact]
    public void 空闲回收必须可禁用且能被主窗口挂上()
    {
        var trimmer = Source("src/McKuro/Services/IdleWorkingSetTrimmer.cs");
        // 排查/对照实测必须有开关,否则线上出问题只能改代码重发
        Assert.Contains("McKuro_DISABLE_IDLE_TRIM", trimmer);
        Assert.Contains("McKuro_IDLE_TRIM_SECONDS", trimmer);

        var window = Source("src/McKuro/MainWindow.axaml.cs");
        Assert.Contains("HookIdleWorkingSetTrim()", window);
        Assert.Contains("_idleTrimmer.OnDeactivated()", window);
        Assert.Contains("_idleTrimmer.OnActivated()", window);
        // 窗口关闭必须释放计时器,否则 MainWindow 被 DispatcherTimer 钉住
        Assert.Contains("_idleTrimmer?.Dispose()", window);
        // 游戏会话进行中不裁(刚把页让给游戏,再裁只会增加换页)
        Assert.Contains("GameSessionState.Idle", window);
    }

    [Fact]
    public void Avalonia版本必须带原生内存修复()
    {
        // 12.1.6 含 #22757(原生内存/资源管理)、#22281(TextBlock 分离释放文本布局)、
        // #22369(停止的渲染器释放游离 visual)、#22367(样式设 Command 导致控件泄漏)。
        // 本应用 12 个页面 VM 全常驻,这四条都对口,不能退回 12.1.2。
        var csproj = Source("src/McKuro/McKuro.csproj");
        Assert.Contains("\"Avalonia\" Version=\"12.1.6\"", csproj);
        Assert.DoesNotContain("\"Avalonia\" Version=\"12.1.2\"", csproj);

        // 第三方 Live2D 工程必须与主工程同版本,混合版本会在运行期炸绑定
        var live2d = Source("src/ThirdParty/Sparkle.Live2DView/Sparkle.Live2DView.csproj");
        Assert.Contains("\"Avalonia\" Version=\"12.1.6\"", live2d);
    }

    [Fact]
    public void 空闲时长默认值在合理区间()
    {
        // 默认 3 分钟:太短会在 Alt+Tab 来回时反复裁剪,太长则游戏期间让不出内存
        var delay = IdleWorkingSetTrimmer.DefaultIdleDelay;
        Assert.True(delay >= TimeSpan.FromMinutes(1), $"空闲时长过短: {delay}");
        Assert.True(delay <= TimeSpan.FromMinutes(10), $"空闲时长过长: {delay}");
        Assert.True(IdleWorkingSetTrimmer.DefaultCooldown >= TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void 高水位回收必须与焦点无关()
    {
        // 实测高峰是「用户盯着启动页看视频」:窗口一直有焦点,失焦路径永不触发,
        // 但进程里约 380 MB 是可回收页(605 MB 裁完 258 MB)。
        // 因此必须有第二路:不依赖 Activated/Deactivated 的高水位触发。
        var source = Source("src/McKuro/Services/IdleWorkingSetTrimmer.cs");
        Assert.Contains("StartHighWatermarkWatch", source);
        Assert.Contains("HighWatermarkBytes", source);
        Assert.Contains("Environment.WorkingSet", source);

        // 水位触发还必须「持续一段时间」,否则刚切到启动页的峰值会被立刻裁掉又读回
        Assert.Contains("HighWatermarkSustain", source);
        Assert.Contains("_overWatermarkSinceUtc", source);

        // 两条路径共用冷却,不能叠着裁
        Assert.Contains("_lastTrimUtc", source);

        var window = Source("src/McKuro/MainWindow.axaml.cs");
        Assert.Contains("StartHighWatermarkWatch()", window);
        Assert.Contains("ResolveHighWatermark()", window);
    }

    [Fact]
    public void 高水位默认值高于应用底噪且可关闭()
    {
        // 底噪(无视频无 Live2D)约 310 MB;水位必须明显高于它,否则会常态触发
        Assert.True(IdleWorkingSetTrimmer.DefaultHighWatermarkBytes >= 400L * 1024 * 1024);

        var source = Source("src/McKuro/Services/IdleWorkingSetTrimmer.cs");
        // 必须能关:排查/对照实测要能关掉这一路
        Assert.Contains("McKuro_TRIM_WATERMARK_MB", source);
    }
}
