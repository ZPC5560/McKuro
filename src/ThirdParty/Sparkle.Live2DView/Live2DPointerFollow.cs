namespace Sparkle.Live2DView;

/// <summary>
/// 指针跟随的增益:鼠标位置(look ∈ [-1,1])乘以增益,得到该角色应写入的<b>归一化偏移</b>
/// (相对参数量程的一半),再与程序化待机偏移相加、统一钳到 ±1 后写一次。
///
/// <para><b>为什么不用官方系数(30/10/1)</b>:官方那套系数按官方模型的骨骼量程标定 ——
/// 官方 <c>ParamAngleX</c> 满量程能让头部明显转动。而第三方「分层 PSD → Cubism」导出把参数
/// 烘焙成<b>局部形变</b>,同样的满量程只让顶点移动几个像素。实测(moc3,屏幕模型高约 670px):</para>
/// <list type="bullet">
///   <item><c>head_roll</c> 满量程 = 8.7 px(头部最强驱动,主要带动头发)</item>
///   <item><c>head_pitch</c> 满量程 = 4.9 px</item>
///   <item><c>head_yaw</c> 满量程 = 2.9 px</item>
///   <item><c>body_sway</c> 满量程 = 8.7 px(带动衣摆与尾巴)</item>
/// </list>
/// <para>沿用官方 30% 的写法只会得到 1 像素级位移,肉眼等同"没有跟随"。因此这里按量程比例给增益,
/// 并由调用方换算到各参数的实际半量程。</para>
///
/// <para><b>预算分配</b>:待机与跟随共用同一批参数,两者相加后必须留有余量 ——
/// 相加超过量程会被 <c>SetParameterValue</c> 钳在边界,表现为"跟随到某个方向就不动了"。
/// 故头部取 待机 0.40 + 跟随 0.60、身体取 待机 0.55 + 跟随 0.45,各自 ≤ 1.0。</para>
/// </summary>
public static class Live2DPointerFollow
{
    /// <summary>一个角色的跟随增益(占该参数半量程的比例)。</summary>
    public readonly record struct Gain(Live2DSemanticParameter Role, float LookX, float LookY);

    /// <summary>
    /// 跟随关系。横向叠加「转动 + 侧倾 + 身体重心」:这类导出模型的 <c>head_yaw</c> 位移很小
    /// (2.9px),而 <c>head_roll</c> 是头部最强驱动(8.7px),只写 yaw 会看不出方向。
    /// 纵向同理配 <c>head_pitch</c> + <c>body_pitch</c>。
    /// </summary>
    public static IReadOnlyList<Gain> Gains { get; } =
    [
        new(Live2DSemanticParameter.HeadYaw,   0.60f, 0.00f),
        new(Live2DSemanticParameter.HeadRoll,  0.60f, 0.00f),
        new(Live2DSemanticParameter.HeadPitch, 0.00f, 0.60f),
        new(Live2DSemanticParameter.BodySway,  0.45f, 0.00f),
        new(Live2DSemanticParameter.BodyPitch, 0.00f, 0.30f),
        new(Live2DSemanticParameter.GazeX,     1.00f, 0.00f),
        new(Live2DSemanticParameter.GazeY,     0.00f, 1.00f),
    ];

    /// <summary>取某角色在本帧应写入的归一化偏移(look 已钳到 ±1;未列出的角色返回 0)。</summary>
    public static float Offset(Live2DSemanticParameter role, float lookX, float lookY)
    {
        lookX = Math.Clamp(lookX, -1f, 1f);
        lookY = Math.Clamp(lookY, -1f, 1f);

        foreach (Gain gain in Gains)
        {
            if (gain.Role == role)
            {
                return gain.LookX * lookX + gain.LookY * lookY;
            }
        }

        return 0f;
    }

    /// <summary>把待机偏移与跟随偏移合成,并钳到 ±1(保证不越过参数量程)。</summary>
    public static float Combine(float idle, float follow) =>
        Math.Clamp(idle + follow, -1f, 1f);
}
