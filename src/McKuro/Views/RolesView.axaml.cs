using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using McKuro.ViewModels;

namespace McKuro.Views;

/// <summary>
/// 角色数据页:详情锚点导航 → 滚动定位;技能演示视频的悬停显隐与重播。
/// <para>
/// 详情数据段全部常显(单一 ScrollViewer 纵向排列),导航条只负责把对应段滚到视野内。
/// 不用「分段隐藏」的原因:分段可见性必须随选中项刷新,而 Avalonia 编译绑定下
/// 索引器 + OnPropertyChanged("Item[]") 不刷新(实测点击无反应、仅首段可见),
/// 且隐藏分段会让未点导航的用户看不到任何数据。
/// </para>
/// </summary>
public partial class RolesView : UserControl
{
    /// <summary>详情段控件(索引与 RolesViewModel.SelectedDetailSection 一致)。</summary>
    private Control?[]? _sections;

    /// <summary>视频区域(悬停显隐控件的判定范围)。</summary>
    private Control? _demoHost;

    /// <summary>播放控件层(播放时自动隐藏,鼠标悬停显示)。</summary>
    private Control? _demoOverlay;

    /// <summary>指针是否在视频区域内(与播放状态共同决定控件层显隐)。</summary>
    private bool _pointerInsideDemo;

    /// <summary>技能演示视频控件(用于同步播放/暂停图标)。</summary>
    private Controls.VideoBackgroundControl? _demoVideo;

    /// <summary>截图分享进行中标记(防重入闸门,见 <see cref="CaptureAndShareAsync"/>)。</summary>
    private bool _shareCapturing;

    public RolesView()
    {
        InitializeComponent();
        // 只在用户真实点击导航条后滚动:PointerReleased 保证是用户操作而非绑定写回
        var nav = this.FindControl<ListBox>("DetailNav");
        if (nav is not null)
        {
            nav.PointerReleased += (_, _) => ScrollToSection(nav.SelectedIndex);
        }
        // 载入后把详情滚到顶部(概览):避免一进来就停在属性/技能段
        Loaded += (_, _) => DetailScrollToTop();

        // 技能演示:控件层可见性 = 指针在区域内 OR 当前未在播放。
        // 为什么不能只靠 PointerExited 隐藏:自动播放时若指针正好停在视频上,
        // 就再也不会收到 PointerExited → 控件层一直显示(用户反馈"播放中控件仍显示")。
        // 改成两个条件共同决定,播放一开始就会自动隐藏。
        _demoHost = this.FindControl<Control>("SkillDemoHost");
        _demoOverlay = this.FindControl<Control>("SkillDemoOverlay");
        _demoVideo = this.FindControl<Controls.VideoBackgroundControl>("SkillDemoVideo");
        if (_demoHost is not null && _demoOverlay is not null)
        {
            _demoHost.PointerEntered += (_, _) =>
            {
                _pointerInsideDemo = true;
                UpdateDemoOverlay();
            };
            _demoHost.PointerExited += (_, _) =>
            {
                _pointerInsideDemo = false;
                UpdateDemoOverlay();
            };
        }
        if (_demoVideo is not null)
        {
            // 播放状态变化 → 同步图标与控件层显隐
            _demoVideo.PropertyChanged += (_, e) =>
            {
                if (e.Property == Controls.VideoBackgroundControl.IsPlayingProperty)
                {
                    UpdateDemoIcon();
                    UpdateDemoOverlay();
                }
            };
            // 播完 → 图标切回"播放",并显示控件层(便于用户重播)
            _demoVideo.PlaybackEnded += (_, _) =>
            {
                UpdateDemoIcon();
                UpdateDemoOverlay();
            };
            UpdateDemoIcon();
            UpdateDemoOverlay();
        }

        // 点击视频区域:播放中 → 暂停;暂停/结束 → 继续(已播完时底层从头重播)
        var replay = this.FindControl<Button>("SkillDemoReplayButton");
        if (replay is not null)
        {
            replay.Click += (_, _) =>
            {
                var video = _demoVideo;
                if (video is null)
                {
                    return;
                }
                if (video.IsPlaying)
                {
                    video.Pause();
                }
                else
                {
                    video.TogglePause();
                }
                UpdateDemoIcon();
            };
        }

        // 分享:截取「概览 → 声骸」区间为长图,弹出预览窗口(可复制/另存)
        var share = this.FindControl<Button>("ShareButton");
        if (share is not null)
        {
            share.Click += async (_, _) => await CaptureAndShareAsync();
        }
    }

    /// <summary>同步技能演示按钮图标与悬停提示(播放中=暂停双竖线+「点击暂停」;否则=播放三角+「点击播放」)。</summary>
    private void UpdateDemoIcon()
    {
        var playing = _demoVideo?.IsPlaying == true;
        var pause = this.FindControl<TextBlock>("DemoPauseIcon");
        var play = this.FindControl<TextBlock>("DemoPlayIcon");
        if (pause is not null)
        {
            pause.IsVisible = playing;
        }
        if (play is not null)
        {
            play.IsVisible = !playing;
        }
        // 悬停提示随状态切换(用户要求:播放时"点击暂停",暂停时"点击播放")
        var button = this.FindControl<Button>("SkillDemoReplayButton");
        if (button is not null)
        {
            ToolTip.SetTip(button, McKuro.Services.LanguageService.Format(
                playing ? "Roles.Guide.PauseHint" : "Roles.Guide.PlayHint"));
        }
    }

    /// <summary>
    /// 截取「概览 → 声骸」区间(不含攻略推荐/队友推荐)为长图并弹出分享窗。
    /// <para>
    /// 关键认识:<b>不需要把控件搬到别处</b>。ScrollViewer 会以"无限高"测量内容,
    /// 所以 <c>DetailContent.Bounds.Height</c> 本来就是完整内容高度(超出部分只是被
    /// 视口裁掉,并未改变布局尺寸)。因此直接渲染 DetailContent,再按各分段的相对位置
    /// 裁出目标区间即可 —— 不移动控件 ⇒ 绑定与图片加载全程不中断。
    /// </para>
    /// <para>
    /// 曾经的错误做法(均已废弃):① 把分段移到"离屏窗口"渲染 —— 异步图片/绑定数据
    /// 全部丢失,截图里数据为空;② 把控制搬到另一个窗口还会让 libmpv 重新初始化 GL 并崩溃。
    /// </para>
    /// <para>
    /// 其余要点:截图期间隐藏视频/演示块(libmpv 跨窗重挂载是崩溃来源,且静态图里无价值);
    /// 总像素上限防 OOM;<b>入口防重入 + 期间禁用按钮</b>(捕获含多个 await,快速双击会并发
    /// 两次捕获,评审反馈);分段隐藏只覆盖"渲染拼图"全程,<b>弹窗前即还原</b>(此前模态期间
    /// 背后页面的技能演示块一直隐身,评审反馈);finally 全程归位,任何失败不影响页面。
    /// </para>
    /// </summary>
    private async Task CaptureAndShareAsync()
    {
        // 防重入闸门(评审反馈):捕获流程含"等图片/等布局/弹模态窗"多个 await,
        // 连点分享按钮会并发两次截图 —— 第二次会在第一次隐藏分段时改回显隐、
        // 并再产出一张数百 MB 量级的位图。同时禁用按钮给用户明确反馈。
        if (_shareCapturing)
        {
            return;
        }
        _shareCapturing = true;
        var shareButton = this.FindControl<Button>("ShareButton");
        if (shareButton is not null)
        {
            shareButton.IsEnabled = false;
        }
        var content = this.FindControl<StackPanel>("DetailContent");
        Control? demoHost = null;
        var demoWasVisible = false;
        Control? demoSection = null;
        var demoSectionWasVisible = false;
        try
        {
            if (content is null)
            {
                return;
            }

            // 技能演示块(视频 + 技能说明 + 基础连招)不进分享图:
            // 静态图里只有"当前选中技能"的说明,信息价值低 —— 保留技能等级/加点即可。
            // 必须在统计段高之前隐藏,段高才会按隐藏后的真实布局计算。
            demoSection = this.FindControl<Control>("SkillDemoSection");
            if (demoSection is not null)
            {
                demoSectionWasVisible = demoSection.IsVisible;
                demoSection.IsVisible = false;
            }

            var wanted = new[]
            {
                this.FindControl<Control>("SecOverview"),
                this.FindControl<Control>("SecAttributes"),
                this.FindControl<Control>("SecSkills"),
                this.FindControl<Control>("SecChains"),
                this.FindControl<Control>("SecEchoes"),
            }.Where(c => c is { IsVisible: true, Bounds.Height: > 1 }).Cast<Control>().ToList();
            if (wanted.Count == 0)
            {
                return;
            }

            // 视频控件不参与截图(其画面逐帧变化,且隐藏可避免把黑帧截进去)
            demoHost = this.FindControl<Control>("SkillDemoHost");
            if (demoHost is not null)
            {
                demoWasVisible = demoHost.IsVisible;
                demoHost.IsVisible = false;
            }

            // 等布局稳定 + 图片解码完成(截图渲染最常见的问题是图片还没加载好就渲染)
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Loaded);
            await WaitForImagesAsync(content);

            var width = content.Bounds.Width;
            if (width < 1)
            {
                return;
            }

            // 按屏幕实际缩放渲染,否则高 DPI 屏上会把 1x 位图放大显示 → 文字发虚。
            // 密度取"所有连接屏幕的最大缩放"(至少当前窗口的):分享图会被粘贴/保存到其他
            // 屏幕上查看,若截图密度低于目标屏缩放,查看时是把 1x 位图**放大**显示,
            // 细笔画周围出现灰色插值晕圈(视觉上就是文字重影);按最高倍率渲染、
            // 低倍屏只下采样才是锐利的。
            var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
            var screens = TopLevel.GetTopLevel(this)?.Screens?.All;
            if (screens is not null)
            {
                foreach (var s in screens)
                {
                    scaling = Math.Max(scaling, s.Scaling);
                }
            }
            if (scaling <= 0 || double.IsNaN(scaling) || double.IsInfinity(scaling))
            {
                scaling = 1.0;
            }
            var dpi = 96.0 * scaling;

            // ===== 逐段渲染后纵向拼接 =====
            // 为什么不渲染整个 DetailContent 再裁剪:整段高度接近 3000 DIP(像素约 4500),
            // 一次性渲染时 Avalonia 对超长内容的布局/绘制会与屏幕上的实际排布不一致,
            // 右侧内容会被推出画布(实测「属性」右列数值消失,而同一段**单独渲染**时完全正常)。
            // 逐段渲染每段都只有几百 DIP,渲染结果与屏幕一致,拼接后完整无缺失。
            // 段间距:与 XAML 中 DetailContent 的 StackPanel Spacing="14" 保持一致。
            const double SectionGap = 14;
            var scaledGap = (int)(SectionGap * scaling);

            var fullW = Math.Max(1, (int)Math.Ceiling(width * scaling));
            // 每段高度向上取整,避免 DIP 小数部分被截掉(否则段内容底部会被裁 ——
            // 实测属性第 4 行、共鸣链描述、技能演示下半部会被切掉)。
            var segHeights = wanted
                .Select(c => Math.Max(1, (int)Math.Ceiling(c.Bounds.Height * scaling)))
                .ToList();
            var totalPixels = segHeights.Sum(h => (long)h) + (long)scaledGap * (wanted.Count - 1);
            totalPixels = Math.Max(1, totalPixels);

            // 超大图保护:超过上限时整体等比缩小(避免 OOM)
            const long maxTotalPixels = 48_000_000;
            double shrink = totalPixels > maxTotalPixels
                ? Math.Sqrt(maxTotalPixels / (double)totalPixels)
                : 1.0;
            var outW = Math.Max(1, (int)(fullW * shrink));
            var outH = Math.Max(1, (int)(totalPixels * shrink));

            // ===== DPI 策略(关键,曾导致"预览宽度不完整/保存图右侧缺失")=====
            // Avalonia 12.1.2 的位图绘制路径不认 DPI≠96 的元数据:
            //  - DrawingContext.DrawImage 的 sourceRect 按**像素**解释(而非 DIP);
            //  - Image 控件按"像素=DIP"渲染(DPI 感知的 Size 只用于布局)。
            // 若目标位图像各段一样用 scaling×96 DPI,DrawImage 会把源段再放大 scaling 倍
            // (实测整图内容被放大 2.25×,每段右侧 1/3 丢失),且预览 Image 按像素尺寸
            // 渲染导致右/下被裁。因此:**各段保持 scaling×96 DPI**(渲染清晰度),
            // **目标位图固定 96 DPI**(DIP=像素),贴图源矩形/目标矩形全部按像素给,
            // 1 像素对 1 像素,无缩放无重采样;下游预览/剪贴板/保存所见的 Size=PixelSize,
            // 与渲染行为一致。若日后升级 Avalonia 且官方修正了 DPI 处理,此处需同步复核。
            var parts = new List<RenderTargetBitmap>(wanted.Count);
            RenderTargetBitmap? cropped = null;
            // 所有权标记(评审反馈:大图可达 ~190MB,不能只靠窗口 Closed 一条路):
            // 移交前任何异常 → 本 finally 释放;移交后由分享窗(Closed)或
            // CompleteShareAsync 的失败路径负责,绝不双路径都不管
            var croppedHandedOff = false;
            try
            {
                // 先按完整宽度显式测量/排布 content,再做一次"预热"渲染(丢弃结果)。
                // 为什么需要:直接逐段渲染时,某段内部的等宽布局(如属性区的 UniformGrid
                // 两列)会按"内容期望宽度"展开,导致右列内容被推到画布之外(实测右列数值消失);
                // 先让父容器在正确宽度下完成一次完整测量,各段内部布局才与屏幕上一致。
                var contentHeight = content.Bounds.Height;
                content.Measure(new Size(width, contentHeight));
                content.Arrange(new Rect(0, 0, width, contentHeight));

                cropped = new RenderTargetBitmap(new PixelSize(outW, outH), new Vector(96, 96));
                // 逐段渲染成独立位图,全部保留到拼接完成后再释放。
                // 元组记录的是**像素**尺寸(贴图坐标全按像素,见上方 DPI 策略)。
                var sections = new List<(RenderTargetBitmap Part, double PxW, double PxH)>(wanted.Count);
                for (var i = 0; i < wanted.Count; i++)
                {
                    var sec = wanted[i];
                    var secW = Math.Max(1, (int)Math.Ceiling(sec.Bounds.Width * scaling));
                    var secH = segHeights[i];
                    var part = new RenderTargetBitmap(new PixelSize(secW, secH), new Vector(dpi, dpi));
                    part.Render(sec);
                    sections.Add((part, secW, secH));
                    parts.Add(part);
                }

                // 拼接:所有源位图仍存活,绘制上下文也在同一 using 内直到 Dispose 时提交
                using (var ctx = cropped.CreateDrawingContext())
                {
                    var yPx = 0.0;
                    foreach (var (part, pxW, pxH) in sections)
                    {
                        ctx.DrawImage(part,
                            new Rect(0, 0, pxW, pxH),
                            new Rect(0, yPx * shrink, pxW * shrink, pxH * shrink));
                        yPx += pxH + scaledGap;
                    }
                }

                // 所有段贴完后,源位图才可释放
                foreach (var p in parts)
                {
                    p.Dispose();
                }
                parts.Clear();

                // 拼图完成 ⇒ 立刻还原被隐藏的技能演示块(评审反馈):
                // 此前还原只在最外层 finally,模态弹窗打开期间背后页面的演示块一直隐身
                RestoreShareHiddenSections(demoHost, demoWasVisible, demoSection, demoSectionWasVisible);

                croppedHandedOff = true;
                await CompleteShareAsync(cropped, scaling, wanted.Count, width);
            }
            finally
            {
                foreach (var p in parts)
                {
                    p.Dispose();
                }
                if (!croppedHandedOff)
                {
                    cropped?.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            // 不静默吞掉:写 stderr 便于诊断(界面不受影响)
            System.Console.Error.WriteLine($"[share] 截图失败: {ex}");
        }
        finally
        {
            // finally 内绝不能抛异常(否则会成为未处理异常直接崩进程)
            try
            {
                RestoreShareHiddenSections(demoHost, demoWasVisible, demoSection, demoSectionWasVisible);
                // 闸门与按钮必须复位(否则分享按钮从此永久失效 —— 评审反馈的防重入另一半)
                if (this.FindControl<Button>("ShareButton") is { } btn)
                {
                    btn.IsEnabled = true;
                }
                _shareCapturing = false;
            }
            catch (Exception)
            {
                // 忽略
            }
        }
    }

    /// <summary>还原截图期间被隐藏的演示块(幂等,拼图完成与 finally 各调一次)。</summary>
    private static void RestoreShareHiddenSections(
        Control? demoHost, bool demoWasVisible, Control? demoSection, bool demoSectionWasVisible)
    {
        if (demoHost is not null)
        {
            demoHost.IsVisible = demoWasVisible;
        }
        if (demoSection is not null)
        {
            demoSection.IsVisible = demoSectionWasVisible;
        }
    }

    /// <summary>
    /// 诊断输出 + 弹出分享窗。<b>位图所有权</b>:构造成功起由窗口负责(关闭时释放);
    /// 构造/显示失败在本方法内释放 —— 大图可达 ~190MB,不能只靠成功路径兜底(评审反馈)。
    /// </summary>
    private async Task CompleteShareAsync(Bitmap cropped, double scaling, int segments, double contentWidth)
    {
        System.Console.Error.WriteLine(
            $"[share] 产出 {cropped.PixelSize.Width}x{cropped.PixelSize.Height} " +
            $"segments={segments} contentW={contentWidth:F0} scaling={scaling}");

        var owner = TopLevel.GetTopLevel(this) as Window;
        SharePreviewWindow preview;
        try
        {
            preview = new SharePreviewWindow(cropped, LanguageServiceShim.ShareTitle());
        }
        catch (Exception)
        {
            // 构造失败 ⇒ Closed 永远不会触发,这里直接释放
            try { cropped.Dispose(); } catch (Exception) { }
            throw;
        }
        try
        {
            if (owner is not null)
            {
                await preview.ShowDialog(owner);
            }
            else
            {
                preview.Show();
            }
        }
        catch (Exception)
        {
            // 显示失败(如 owner 已失效):显式关闭以触发 Closed 释放位图
            try { preview.Close(); } catch (Exception) { }
            throw;
        }
    }

    /// <summary>
    /// 等待内容里的异步图片加载完成(最多等约 1.2 秒)。
    /// <para>不等待就直接渲染,截图里会缺图 —— 这是"截图数据不全"的常见原因之一。</para>
    /// </summary>
    private static async Task WaitForImagesAsync(Visual root)
    {
        // 一次性抓全子孙(两遍枚举合并,避免每轮多次遍历可视树)
        var descendants = root.GetVisualDescendants().ToList();
        for (var attempt = 0; attempt < 12; attempt++)
        {
            // 两类异步图标都要等:
            // ① AsyncImage —— 直接可见,ImageLoaded 标记下载+解码完成;
            // ② TintedAsyncImage —— 共鸣链/着色图标,其下载器刻意不进可视树、且下载后还要
            //    后台重着色,只扫 AsyncImage 会漏掉它 ⇒ 分享图里共鸣链被截成空白(评审反馈)。
            var pending =
                descendants.OfType<McKuro.Controls.AsyncImage>().Count(img => !img.ImageLoaded)
                + descendants.OfType<McKuro.Controls.TintedAsyncImage>().Count(t => t.HasPendingLoad);
            if (pending == 0)
            {
                return;
            }
            await Task.Delay(100);
        }
    }

    /// <summary>分享标题(延迟到 View 层取本地化文本,避免 VM 依赖)。</summary>
    private static class LanguageServiceShim
    {
        public static string ShareTitle() => McKuro.Services.LanguageService.Format("Roles.Share.PreviewTitle");
    }

    /// <summary>
    /// 按"指针在区域内"与"是否正在播放"共同决定控件层显隐。
    /// <para>正在播放且指针不在区域内 → 隐藏(不遮挡画面);其余情况 → 显示(便于暂停/重播)。</para>
    /// </summary>
    private void UpdateDemoOverlay()
    {
        if (_demoOverlay is null)
        {
            return;
        }
        var playing = _demoVideo?.IsPlaying == true;
        var visible = _pointerInsideDemo || !playing;
        _demoOverlay.Opacity = visible ? 1 : 0;
        _demoOverlay.IsHitTestVisible = visible;
    }

    /// <summary>把详情滚动区复位到顶部(概览段)。</summary>
    private void DetailScrollToTop()
    {
        var scroll = this.FindControl<ScrollViewer>("DetailScroll");
        if (scroll is null)
        {
            return;
        }
        // 延后到布局完成:此时内容高度已确定,复位才生效
        Dispatcher.UIThread.Post(() => scroll.Offset = default, DispatcherPriority.Background);
    }

    /// <summary>把指定索引的详情段滚动到视野内(布局完成后再定位,避免段高未定)。</summary>
    private void ScrollToSection(int index)
    {
        _sections ??=
        [
            this.FindControl<Control>("SecOverview"),
            this.FindControl<Control>("SecAttributes"),
            this.FindControl<Control>("SecSkills"),
            this.FindControl<Control>("SecChains"),
            this.FindControl<Control>("SecEchoes"),
            this.FindControl<Control>("SecRecommend"),
            this.FindControl<Control>("SecTeammates"),
        ];
        if (index < 0 || index >= _sections.Length || _sections[index] is not { } target)
        {
            return;
        }
        // 关键:点击导航后焦点会落在 ListBoxItem 上,Avalonia 的 BringIntoView 会被
        // 焦点管理覆盖(表现为点了"技能"却停在属性)。故在 Background 优先级排队执行,
        // 晚于本轮的焦点/布局处理。
        Dispatcher.UIThread.Post(() => target.BringIntoView(), DispatcherPriority.Background);
    }
}



