using McKuro.Core.Services.Settings;
using McKuro.ViewModels;

namespace McKuro.Tests;

/// <summary>
/// 「启动页从自定义视频切回官方宣传视频不立刻生效」的服务层回归。
/// <para>
/// <b>用户报障</b>:把视频来源从「自定义动态壁纸」改回「官方宣传视频」后,官方视频没有立即出现,
/// 必须先在设置里关掉背景视频、再打开才生效;而官方宣传视频的<b>自定义文本</b>(版本标语)
/// 却立刻生效了 —— 这个不对称正是定位根因的线索。
/// </para>
/// <para>
/// <b>根因(两处,同一调用链)</b>:
/// <list type="number">
/// <item>
/// <c>LauncherViewModel.OnNavigatedTo</c> 只用 <c>BackgroundVideoEnabled</c>(开关)判断
/// 「是否需要重新加载官方背景」。用户只改「来源」不改开关时判定为"无变化" → 不重载,
/// 于是官方 URL 永远不会被重新取回。
/// </item>
/// <item>
/// <c>ApplyCustomVideoOverride</c> 只处理 <c>customActive == true</c> 这一个方向:
/// 自定义失效时它什么都不做,<c>BackgroundVideoUrl</c> 仍停在上次写入的自定义文件路径。
/// 两个条件叠加 = 撤回官方后启动页继续播放自定义视频,直到"关掉再打开视频"
/// 触发一次完整 <c>LoadLauncherInfoAsync</c>(其间先清空再重设 URL)才恢复。
/// </item>
/// </list>
/// 版本标语走 <c>IsCustomVideoActive</c> 绑定(同一路径里立即翻转),所以<b>文本立刻对、视频不对</b>。
/// </para>
/// <para>
/// 本测试<b>直接驱动生产判定点</b>(<see cref="LauncherViewModel.ResolveBackgroundState"/> /
/// <see cref="LauncherViewModel.VideoSourceFingerprint"/>),不自己复算守卫:
/// 把「自定义失效必须回落官方」改回旧行为(返回保持当前值不动),或用
/// <c>VideoSourceFingerprint</c> 只比较开关,相应用例就会真实失败。
/// </para>
/// </summary>
public class LauncherVideoSourceSwitchTests
{
    private const string OfficialVideo = "https://cdn.kuro/video.mp4";
    private const string OfficialFrame = "https://cdn.kuro/frame.webp";
    private const string CustomVideo = @"E:\wallpapers\custom.mp4";

    /// <summary>官方有视频(backgroundFileType=2)且已成功加载过的常规基线。</summary>
    private static LauncherViewModel.BackgroundState Resolve(
        AppSettings s,
        bool customFileExists = true,
        bool officialKnown = true) =>
        LauncherViewModel.ResolveBackgroundState(
            s,
            customFileExists,
            officialKnown,
            OfficialVideo,
            OfficialFrame,
            officialHasVideo: true);

    private static AppSettings CustomModeOn() => new()
    {
        BackgroundVideoEnabled = true,
        BackgroundVideoMode = 1,
        CustomBackgroundVideoPath = CustomVideo,
    };

    /// <summary>
    /// 核心回归:自定义生效 → 切回官方(只改 mode,开关不动)必须给出官方视频 URL。
    /// <para>
    /// 旧实现里 <c>ApplyCustomVideoOverride</c> 在 mode≠1 时不做任何赋值,
    /// 于是 <c>BackgroundVideoUrl</c> 保持自定义路径 → 本项目失败(Actual 为自定义路径)。
    /// </para>
    /// </summary>
    [Fact]
    public void 撤回官方时必须给出官方视频地址而不是继续用自定义路径()
    {
        var s = CustomModeOn();
        Assert.Equal(CustomVideo, Resolve(s).VideoUrl); // 前置:自定义确实生效

        s.BackgroundVideoMode = 0; // 用户在设置页把来源改回「官方宣传视频」
        var state = Resolve(s);

        Assert.Equal(OfficialVideo, state.VideoUrl);
        Assert.Equal(OfficialFrame, state.ImageUrl);
        Assert.True(state.VideoEnabled);
        Assert.False(state.IsCustomActive);
    }

    /// <summary>
    /// 自定义文件被移动/删除(设置仍是 mode=1)同样必须回落官方 —— 不能让背景空着或卡在旧路径。
    /// </summary>
    [Fact]
    public void 自定义文件缺失时回落到官方视频()
    {
        var s = CustomModeOn();
        var state = Resolve(s, customFileExists: false);

        Assert.Equal(OfficialVideo, state.VideoUrl);
        Assert.True(state.VideoEnabled);
        Assert.False(state.IsCustomActive);
    }

    /// <summary>
    /// 自定义生效时返回的是自定义路径且视频必须开着(该方向是既有行为,不能被修复破坏)。
    /// </summary>
    [Fact]
    public void 自定义生效时使用自定义路径并打开视频()
    {
        var state = Resolve(CustomModeOn());

        Assert.Equal(CustomVideo, state.VideoUrl);
        Assert.True(state.VideoEnabled);
        Assert.True(state.IsCustomActive);
        // 自定义视频没有官方首帧图:返回 null 表示"保持当前静态图不动"
        Assert.Null(state.ImageUrl);
    }

    /// <summary>
    /// 用户把背景视频总开关关掉:即使来源是自定义,也必须回到官方首帧图 + 视频关闭。
    /// </summary>
    [Fact]
    public void 关闭背景视频总开关后不再播放自定义视频()
    {
        var s = CustomModeOn();
        s.BackgroundVideoEnabled = false;
        var state = Resolve(s);

        Assert.False(state.VideoEnabled);
        Assert.False(state.IsCustomActive);
        Assert.Equal(OfficialFrame, state.ImageUrl);
    }

    /// <summary>
    /// 官方背景尚未加载过(没有可恢复的缓存)时,不得把 URL 清空 —— 返回 null 表示保持当前值,
    /// 等异步加载落地。否则冷启动瞬间会白屏。
    /// </summary>
    [Fact]
    public void 官方背景未加载过时不改动当前地址避免白屏()
    {
        var s = new AppSettings { BackgroundVideoEnabled = true, BackgroundVideoMode = 0 };
        var state = Resolve(s, officialKnown: false);

        Assert.Null(state.VideoUrl);
        Assert.Null(state.ImageUrl);
        Assert.False(state.IsCustomActive);
    }

    /// <summary>
    /// <b>来源指纹</b>必须区分「自定义 ⇄ 官方」而不仅仅是开关开/关 ——
    /// 这正是 OnNavigatedTo 漏判"来源变了"的入口。把指纹退化成只比较开关,本用例失败。
    /// </summary>
    [Fact]
    public void 来源指纹必须区分自定义与官方()
    {
        var s = CustomModeOn();
        var customFingerprint = LauncherViewModel.VideoSourceFingerprint(s);

        // 只切来源,开关与自定义路径都不动
        s.BackgroundVideoMode = 0;
        var officialFingerprint = LauncherViewModel.VideoSourceFingerprint(s);

        Assert.NotEqual(customFingerprint, officialFingerprint);

        // 反方向(官方 → 自定义)同样必须被识别
        s.BackgroundVideoMode = 1;
        Assert.Equal(customFingerprint, LauncherViewModel.VideoSourceFingerprint(s));
    }

    /// <summary>
    /// 开关翻转也必须被指纹捕获(既有行为);同时确认改自定义路径也会被识破。
    /// </summary>
    [Fact]
    public void 来源指纹捕获开关与自定义路径变化()
    {
        var s = CustomModeOn();
        var baseline = LauncherViewModel.VideoSourceFingerprint(s);

        s.BackgroundVideoEnabled = false;
        var off = LauncherViewModel.VideoSourceFingerprint(s);
        Assert.NotEqual(baseline, off);

        s.BackgroundVideoEnabled = true;
        s.CustomBackgroundVideoPath = @"E:\wallpapers\another.mp4";
        Assert.NotEqual(baseline, LauncherViewModel.VideoSourceFingerprint(s));
    }

    /// <summary>
    /// 切到官方后必须隐藏官方版本标语 —— 旧行为里标语跟随 <c>IsCustomVideoActive</c>
    /// 立刻生效(用户观察到的"文本立即生效"),修复后两个方向都要自洽。
    /// </summary>
    [Fact]
    public void 切回官方时版本标语重新可见()
    {
        var s = CustomModeOn();
        Assert.True(Resolve(s).IsCustomActive); // 自定义:标语隐藏

        s.BackgroundVideoMode = 0;
        Assert.False(Resolve(s).IsCustomActive); // 官方:标语恢复
    }
}
