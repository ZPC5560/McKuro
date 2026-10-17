using Live2DCSharpSDK.App;
using Live2DCSharpSDK.Framework.Model;
using Live2DCSharpSDK.Framework.Rendering;
using Live2DCSharpSDK.OpenGL;

namespace Sparkle.Live2DView.Internal;

internal sealed class InteractiveLive2DDelegate : LAppDelegateOpenGL
{
    private float _x;
    private float _y;
    private float _zoom = 1f;
    private float _lookX;
    private float _lookY;
    private float _targetLookX;
    private float _targetLookY;
    private float _fitScale = 1f;
    private float _fitX;
    private float _fitY;

    /// <summary>
    /// 语义参数映射。官方模型为空映射,此时所有取值都退回硬编码的官方参数 Id,
    /// 行为与改造前完全一致;仅当模型缺少官方参数(第三方导出)时才由外部注入映射。
    /// </summary>
    private Live2DParameterMap _parameterMap = Live2DParameterMap.Empty;

    /// <summary>程序化待机(呼吸/轻摆/眨眼)。仅当模型缺官方参数时才启用。</summary>
    private readonly Live2DIdleMotion _idle = new();

    /// <summary>尾巴顶点摆动。未标定尾巴图层时不启用(官方模型零开销)。</summary>
    private readonly Live2DTailDeformer _tail = new();

    /// <summary>该模型是否启用了尾巴顶点摆动(诊断用)。</summary>
    public bool HasTailSway => _tail.IsActive;

    public int TailLayerCount => _tail.LayerCount;

    public InteractiveLive2DDelegate(OpenGLApi gl)
        : base(gl)
    {
    }

    public float X => _x;
    public float Y => _y;
    public float Zoom => _zoom;

    /// <summary>注入语义参数映射(见 <see cref="Live2DParameterMap"/>)。传 null 等同清空。</summary>
    public void SetParameterMap(Live2DParameterMap? parameterMap) =>
        _parameterMap = parameterMap ?? Live2DParameterMap.Empty;

    /// <summary>
    /// 声明要接管的尾巴图层。必须在 <c>LoadModel</c> <b>之前</b>调用 ——
    /// 渲染器是在 <c>LAppModel</c> 构造函数里创建的,那时还没有顶点可标定,
    /// 只能先声明、加载完再标定(渲染器与 <c>_tail</c> 是同一个实例,标定后立即生效)。
    /// </summary>
    public void SetTailDrawableIds(IReadOnlyList<string>? tailDrawableIds) =>
        _tail.SetRequestedDrawableIds(tailDrawableIds);

    /// <summary>
    /// 标定尾巴顶点摆动(<see cref="SetTailDrawableIds"/> 之后调用)。
    /// <paramref name="bodyDrawableIds"/> 是躯干图层,用于求身体质心 ——
    /// 尾巴的"根"取离它最近的顶点,摆动绕根部进行。
    /// </summary>
    public void ConfigureTailSway(LAppModel model, IReadOnlyList<string> bodyDrawableIds) =>
        _tail.Configure(model.Model, bodyDrawableIds);

    /// <summary>
    /// 换掉默认渲染器,以便在「顶点求值完毕、尚未上传 GPU」之间施加尾巴位移。
    /// 未声明尾巴图层时返回官方渲染器,保证官方模型零影响。
    /// </summary>
    public override CubismRenderer CreateRenderer(CubismModel model) =>
        _tail.IsRequested
            ? new TailSwayRenderer(GL, this, model, _tail)
            : base.CreateRenderer(model);

    /// <summary>标注哪些语义角色已由 SDK 自己驱动(这些角色不做程序化待机)。</summary>
    public void SetSdkHandledRoles(IEnumerable<Live2DSemanticParameter> roles) =>
        _idle.MarkHandledBySdk(roles);

    /// <summary>该模型是否走了「语义参数」通道(即它的参数名不是官方命名)。</summary>
    public bool UsesSemanticParameters => _parameterMap.Count > 0;

    public override void OnUpdatePre()
    {
        var matrix = Live2dManager.ViewMatrix;
        var originalX = matrix.GetTranslateX();
        var originalY = matrix.GetTranslateY();

        float scale = _zoom * _fitScale;
        matrix.ScaleRelative(scale, scale);

        matrix.Translate(originalX + _x + _fitX, originalY + _y + _fitY);
    }

    public void AdvancePointer(float elapsed)
    {
        float amount = 1 - MathF.Exp(-12 * elapsed);
        _lookX += (_targetLookX - _lookX) * amount;
        _lookY += (_targetLookY - _lookY) * amount;
    }

    public void SetLookAt(float x, float y)
    {
        _targetLookX = Math.Clamp(x, -1, 1);
        _targetLookY = Math.Clamp(y, -1, 1);
    }

    /// <summary>
    /// 写入「指针跟随 + 程序化待机」参数。每帧由 <c>ParameterController</c> 调用。
    ///
    /// <para>官方模型走 <c>model.IdParamAngleX</c> 等硬编码 Id;第三方导出模型改用注入的
    /// <see cref="Live2DParameterMap"/> 把这些角色映射到真实参数名,否则写入会落到 SDK 的
    /// "不存在参数"旁路表里,完全没有效果(这就是"模型静止"的根因之一)。</para>
    ///
    /// <para>两个通道<b>按角色互斥</b>:某角色若已被 SDK 驱动(官方参数名真实存在),这里就完全不碰它,
    /// 避免呼吸被写两遍、眨眼被抢。</para>
    /// </summary>
    public void ApplyPointerParameters(LAppModel model)
    {
        // 尾巴摆动与待机/跟随共用同一个时间基准(渲染在两者之后发生)。
        _tail.Advance(LAppPal.DeltaTime);

        // 待机偏移先算出来放进字典 —— 它和指针跟随共用同一批参数,必须【合成后只写一次】。
        // 若分两次 AddParameterValue,两者相加会超过量程,SetParameterValue 把值钳在边界,
        // 表现为"跟随到某个方向就卡住不动了"。
        var offsets = new Dictionary<Live2DSemanticParameter, float>();
        foreach (Live2DIdleMotion.Offset offset in _idle.Advance(LAppPal.DeltaTime))
        {
            offsets[offset.Role] = offset.Normalized;
        }

        foreach (Live2DPointerFollow.Gain gain in Live2DPointerFollow.Gains)
        {
            float follow = Live2DPointerFollow.Offset(gain.Role, _lookX, _lookY);
            offsets.TryGetValue(gain.Role, out float idle);
            offsets[gain.Role] = Live2DPointerFollow.Combine(idle, follow);
        }

        foreach ((Live2DSemanticParameter role, float value) in offsets)
        {
            // 官方参数名真实存在时走 SDK 原本的写法与系数,保证官方模型手感不变。
            if (Live2DParameterMap.OfficialId(role) is { } official && IsRealParameter(model, official))
            {
                model.Model.AddParameterValue(official, value * OfficialScale(role));
                continue;
            }

            if (!_parameterMap.TryResolve(role, out string id)
                || !TryGetRange(model, id, out float minimum, out float maximum))
            {
                continue;
            }

            model.Model.AddParameterValue(id, value * (maximum - minimum) * 0.5f);
        }

        // 尾巴/耳朵的次级运动:由 physics3.json 的 Input→Output 链在更早的 physics 阶段求值,
        // 之后才轮到本回调。仅当模型真的有这些参数时才写。
        ApplySecondary(model, Live2DSemanticParameter.TailFollow);
        ApplySecondary(model, Live2DSemanticParameter.EarsFollow);
    }

    /// <summary>把尾巴/耳朵次级运动写成一条小幅摆动(幅度取该参数量程的一半的 35%)。</summary>
    private void ApplySecondary(LAppModel model, Live2DSemanticParameter role)
    {
        if (!_parameterMap.TryResolve(role, out string id)
            || !TryGetRange(model, id, out float minimum, out float maximum))
        {
            return;
        }

        // 用与主波同源但更快的相位,避免与身体摆动完全同步(那样看着像整块板在晃)。
        float phase = role == Live2DSemanticParameter.TailFollow ? 1.9f : 0.4f;
        float period = role == Live2DSemanticParameter.TailFollow ? 5.5f : 4.7f;
        float value = Live2DIdleMotion.Oscillate(_idle.Seconds, period, phase);
        model.Model.AddParameterValue(id, value * 0.35f * (maximum - minimum) * 0.5f);
    }

    /// <summary>
    /// 官方参数名对应的写入幅度(沿用 SDK 原本的系数,保持官方模型手感不变)。
    /// 官方量程是 ±100(角度类)或 ±1(眼球类)。
    /// </summary>
    private static float OfficialScale(Live2DSemanticParameter role) => role switch
    {
        Live2DSemanticParameter.HeadYaw or Live2DSemanticParameter.HeadPitch
            or Live2DSemanticParameter.HeadRoll => 30f,
        Live2DSemanticParameter.BodySway or Live2DSemanticParameter.BodyPitch
            or Live2DSemanticParameter.BodyRoll => 10f,
        _ => 1f,
    };

    private static bool IsRealParameter(LAppModel model, string id)
    {
        CubismModel coreModel = model.Model;
        return coreModel is not null && TryGetRange(coreModel, id, out _, out _);
    }

    private static bool TryGetRange(LAppModel model, string id, out float minimum, out float maximum)
    {
        minimum = 0f;
        maximum = 0f;
        CubismModel? coreModel = model.Model;
        return coreModel is not null && TryGetRange(coreModel, id, out minimum, out maximum);
    }

    private static bool TryGetRange(CubismModel model, string id, out float minimum, out float maximum)
    {
        minimum = 0f;
        maximum = 0f;
        int index = model.GetParameterIndex(id);
        if (index < 0 || index >= model.GetParameterCount())
        {
            // 不是本模型的参数(GetParameterIndex 会为未知 Id 合成一个越界下标,须排除)。
            return false;
        }

        minimum = model.GetParameterMinimumValue(index);
        maximum = model.GetParameterMaximumValue(index);
        return maximum > minimum;
    }

    public void MoveByPixels(double dx, double dy, double viewportHeight)
    {
        if (viewportHeight <= 0) return;

        _x += (float)(dx * 2d / viewportHeight);
        _y -= (float)(dy * 2d / viewportHeight);
    }

    public void SetPosition(float x, float y)
    {
        _x = Math.Clamp(x, -2f, 2f);
        _y = Math.Clamp(y, -2f, 2f);
    }

    public void ZoomBy(double wheelDelta)
    {
        var step = wheelDelta > 0 ? 1.12f : 1f / 1.12f;
        SetZoom(_zoom * step);
    }

    public void SetZoom(float zoom) => _zoom = Math.Clamp(zoom, 0.5f, 3f);

    public void SetAutomaticLayout(float scale, float x, float y)
    {
        _fitScale = scale;
        _fitX = x;
        _fitY = y;
    }

    public void ResetView()
    {
        _x = 0f;
        _y = 0f;
        _zoom = 1f;
        _lookX = 0f;
        _lookY = 0f;
        _targetLookX = 0f;
        _targetLookY = 0f;
    }
}
