namespace Sparkle.Live2DView;

/// <summary>
/// 「程序化待机」动作生成器:不依赖模型自带动作文件,用多条不同周期的正弦叠加出
/// 呼吸、头部轻转与身体轻摆。
///
/// <para><b>为什么需要</b>:官方 Cubism 模型靠三样东西显得"活着" —— 自带 Idle 动作、
/// <c>CubismBreath</c> 呼吸、<c>CubismEyeBlink</c> 眨眼,后两者由 SDK 写死到官方参数名
/// (<c>ParamAngleX</c>/<c>ParamBreath</c>/<c>ParamEyeLOpen</c>)。第三方「分层 PSD → Cubism」
/// 导出器的参数名完全不同(<c>ParamPL_param_head_yaw_…</c>),于是呼吸/视线跟随全部落空,
/// 只剩动作文件里那几条肢体曲线 —— 静止感就是这么来的。</para>
///
/// <para><b>振幅为什么必须接近满量程</b>:这类导出把参数烘焙成<b>局部形变</b>而非骨骼旋转,
/// 满量程的位移小得反常。以 心-红黑 为例(moc3 实测,屏幕模型高约 670px):</para>
/// <list type="bullet">
///   <item><c>head_roll</c> 满量程 = 8.7 px(头部最强驱动,主要推头发)</item>
///   <item><c>head_pitch</c> 满量程 = 4.9 px</item>
///   <item><c>body_sway</c> 满量程 = 8.7 px(推动衣摆、尾巴)</item>
///   <item><c>head_yaw</c> 满量程 = 2.9 px(几乎看不出)</item>
///   <item><c>breath</c> 满量程 = 0.2 px(基本无效)</item>
/// </list>
/// <para>所以"多写一点"不是可选调优,而是让效果可见的必要条件;振幅按每个参数自身量程的比例给,
/// 由调用方缩放。同时周期要短于官方待机习惯(用 4~7 秒而非 13~15 秒),否则每秒位移太小,
/// 观感仍是"慢"。</para>
///
/// <para><b>已到资产上限、无法继续改善的部分</b>:第三方导出器在 <c>export-status.json</c> 里
/// 自述了三条限制,其中一条直接决定了"尾巴上下摆"的幅度上限 ——</para>
/// <list type="bullet">
///   <item>「几何为当前修订参数求值后的可编辑网格关键形,<b>原程序化变形器层级不保留</b>」</item>
///   <item>「运行时行为与物理通过配套 JSON 转换;<b>时间驱动的未参数化运动不等于原播放器</b>」</item>
/// </list>
/// <para>原展示页(网页播放器)的尾巴上下摆幅实测 <b>20.6 px</b>(周期 4.4s),那是原工程里
/// <b>程序化变形器 / 时间驱动</b>产生的;导出成"参数 + 关键形"时这部分自由度丢失了。
/// moc3 里穷举全部 26/28 个参数,能让尾巴<b>纵向</b>移动的只有 <c>body_roll</c>,
/// 且全量程也只有 3.4~4.0 px —— 即参考页的约 <b>19%</b>,这是硬上限。
/// 白金形态更小(全量程 0.17 px),因为它的尾巴绑定到 <c>body_sway</c>(只推横向)。
/// 想真正还原,只能改用原播放器(<c>puppetloom-web.js</c>)渲染,而不是 Cubism 运行时。</para>
///
/// <para><b>为什么不在这里做眨眼</b>:Blink 类参数的"开=0"还是"开=100"没有统一约定。
/// 本项目里 PuppetLoom 导出的 <c>闭眼</c> 表达式是给 blink 参数<b>加 +100</b>,
/// 说明该模型是 <c>0=睁眼 / 100=闭眼</c>,与官方 <c>ParamEyeLOpen</c>(0=闭/1=睁)相反。
/// 猜错极性的后果是"眼睛长期闭着",比不眨眼严重得多,因此这里刻意不接管眨眼,
/// 交给模型的表达式机制。</para>
/// </summary>
public sealed class Live2DIdleMotion
{
    /// <summary>一个待写入的归一化偏移(相对参数量程的一半)。</summary>
    public readonly record struct Offset(Live2DSemanticParameter Role, float Normalized);

    /// <summary>单个角色的待机波:振幅(占半量程比例)、周期(秒)、相位。</summary>
    public readonly record struct Wave(Live2DSemanticParameter Role, float Amplitude, float Period, float Phase);

    /// <summary>
    /// 待机波表。振幅与周期按 moc3 实测的"每角色真实位移"标定:
    /// 位移越小的角色给越大的振幅,避免出现"某部位几乎不动"的观感。
    /// <c>gaze_*</c> 不在此列 —— 实测它是死参数(满量程位移为 0),写了也没有任何效果。
    /// </summary>
    public static IReadOnlyList<Wave> Waves { get; } =
    [
        // 头部:head_roll 是这类模型最强的头部驱动(满量程 8.7px),给足即可明显看出来。
        // 振幅与 Live2DPointerFollow.Gains 配平:两者相加 ≤ 1,否则合成为满量程后
        // 指针跟随会被钳掉(表现为"往某个方向跟不过去")。
        new(Live2DSemanticParameter.HeadRoll,  0.40f,  5.8f, 2.20f),
        new(Live2DSemanticParameter.HeadPitch, 0.40f,  4.2f, 3.40f),
        new(Live2DSemanticParameter.HeadYaw,   0.40f,  5.2f, 0.60f),

        // 身体与衣摆/尾巴。body_roll 是这套导出资产里**唯一**能推动尾巴【纵向】的参数
        // (满量程:纵向 3.38px / 横向 1.30px),而 body_sway 只推横向(dy=0)。
        // 因此想让尾巴上下摆,只能靠 body_roll —— 它不与指针跟随共用参数,预算可给到接近满量程。
        // 周期取 4.4s,与原展示页尾巴实测的纵摆周期一致。
        new(Live2DSemanticParameter.BodyRoll,  0.90f,  4.4f, 1.10f),
        new(Live2DSemanticParameter.BodySway,  0.55f,  6.4f, 0.00f),
        new(Live2DSemanticParameter.BodyPitch, 0.35f,  5.5f, 4.10f),

        // 呼吸:该导出器下位移极微(0.2px/满量程),但周期短,作为细碎抖动仍有价值。
        // 它不与指针跟随共用参数,可以给足。
        new(Live2DSemanticParameter.Breath,    0.85f,  2.8f, 0.90f),
    ];

    // 叠加一个小幅高频分量,避免纯正弦看起来像机械摆动。
    private const float TextureAmplitude = 0.18f;
    private const float TexturePeriodRatio = 0.5f;
    private const float TexturePhase = 1.30f;

    private readonly List<Offset> _buffer = new(Waves.Count);
    private readonly HashSet<Live2DSemanticParameter> _sdkHandled = [];
    private double _seconds;

    /// <summary>当前累计时间(秒)。</summary>
    public double Seconds => _seconds;

    /// <summary>
    /// 标记「已由 SDK 自己驱动」的角色,这些角色不再由本类输出。
    ///
    /// <para>必要性:SDK 的 <c>CubismBreath</c> 每帧都会写官方
    /// <c>ParamAngleX/Y/Z</c>、<c>ParamBodyAngleX</c>、<c>ParamBreath</c>。
    /// 若模型真的具备这些官方参数,再叠一层程序化待机就会重复驱动(呼吸翻倍、幅度失真)。
    /// 反之若模型没有这些参数(第三方导出),SDK 的写入会落进"不存在参数"旁路表,
    /// 此时必须由本类接管。</para>
    /// </summary>
    public void MarkHandledBySdk(IEnumerable<Live2DSemanticParameter> roles)
    {
        _sdkHandled.Clear();
        foreach (Live2DSemanticParameter role in roles)
        {
            _sdkHandled.Add(role);
        }
    }

    /// <summary>推进时间并返回本帧要写入的偏移(归一化,相对参数量程的一半)。</summary>
    public IReadOnlyList<Offset> Advance(double deltaSeconds)
    {
        if (deltaSeconds > 0 && double.IsFinite(deltaSeconds))
        {
            _seconds += deltaSeconds;
        }

        _buffer.Clear();

        foreach (Wave wave in Waves)
        {
            if (_sdkHandled.Contains(wave.Role))
            {
                continue;
            }

            _buffer.Add(new Offset(wave.Role, wave.Amplitude * Oscillate(_seconds, wave.Period, wave.Phase)));
        }

        return _buffer;
    }

    /// <summary>
    /// 纯正弦 + 小幅谐波。返回 <c>-1..1</c>,保证不超过 <c>±1</c>(调用方按半量程缩放后不会越界)。
    /// </summary>
    public static float Oscillate(double seconds, float period, float phase)
    {
        // 注意 NaN:与任何数比较都为 false,必须显式用 IsFinite 拦掉,
        // 否则 NaN 周期会一路算出 NaN 参数值写进模型。
        if (!float.IsFinite(period) || period <= 0 || !double.IsFinite(seconds))
        {
            return 0f;
        }

        double omega = 2 * Math.PI / period;
        double value = Math.Sin(omega * seconds + phase)
            + TextureAmplitude * Math.Sin(omega * TexturePeriodRatio * seconds + phase + TexturePhase);

        // 归一化到 ±1(两个同号峰值的最坏情况是 1 + TextureAmplitude)。
        return (float)(value / (1 + TextureAmplitude));
    }
}
