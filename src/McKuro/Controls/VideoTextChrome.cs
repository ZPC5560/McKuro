using Avalonia;
using Avalonia.Media;

namespace McKuro.Controls;

/// <summary>
/// 「视频/壁纸之上的 chrome」统一处理:恒定浅色前景 + <b>反向(深色)投影阴影</b>。
/// <para>
/// 启动页的文字与图标直接压在官方视频上,而视频明暗会随镜头变化:跟着主题取色就会出现
/// 「亮色主题 → 深色字 → 压在黑场景上完全看不见」(2026-10 用户反馈,资源分级切换按键与轮播图
/// 关闭按键都中招)。这里不抓屏采样,而是固定浅色前景,再用一层深色柔和投影把字形从
/// 任意亮度的背景上"抠"出来 —— 官方启动器与 Haiyu 都是这个路子:零采样、零抖动、跨平台。
/// </para>
/// <para>
/// 用法:在<b>只装文字/图标、自身不带面板底</b>的元素(或其容器)上写
/// <c>controls:VideoTextChrome.Shadow="True"</c>。带自有面板底的卡片与弹窗不参与 ——
/// 那里前景跟随主题才是对的。
/// </para>
/// </summary>
public sealed class VideoTextChrome
{
    /// <summary>
    /// 是否为本元素加反向投影阴影。设在容器上会整体作用于其渲染结果(一处设置,子项全覆盖)。
    /// </summary>
    public static readonly AttachedProperty<bool> ShadowProperty =
        AvaloniaProperty.RegisterAttached<VideoTextChrome, Visual, bool>("Shadow");

    /// <summary>
    /// 阴影强度:0.72 的纯黑柔光在「纯白壁纸」与「纯黑场景」之间都能把浅色字形拉开对比,
    /// 又不至于在白底上糊出一圈脏边。
    /// </summary>
    private const double ShadowOpacity = 0.72;

    /// <summary>纯附加属性宿主,不实例化。</summary>
    private VideoTextChrome()
    {
    }

    static VideoTextChrome()
    {
        ShadowProperty.Changed.AddClassHandler<Visual>((visual, e) =>
        {
            if (e.NewValue is true)
            {
                // 每个控件必须持有<b>独立实例</b>:DropShadowEffect 派生自 Animatable(带父级跟踪),
                // 同一个实例挂到多个控件上会互相抢父级,导致只有最后赋值的那个生效。
                // 因此只能在属性变化回调里 new,不能做成共享 XAML 资源或样式 Setter 常量。
                visual.Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 5,
                    OffsetX = 0,
                    OffsetY = 1,
                    Opacity = ShadowOpacity,
                };
            }
            else if (e.NewValue is false)
            {
                visual.Effect = null;
            }
        });
    }

    /// <summary>给元素开启/关闭反向投影阴影(XAML 附加属性写法要求这对方法存在)。</summary>
    public static void SetShadow(Visual visual, bool value) => visual.SetValue(ShadowProperty, value);

    /// <summary>读取元素是否已开启反向投影阴影。</summary>
    public static bool GetShadow(Visual visual) => visual.GetValue(ShadowProperty);
}
