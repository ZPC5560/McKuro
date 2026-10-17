using Live2DCSharpSDK.App;
using Live2DCSharpSDK.Framework.Model;
using Live2DCSharpSDK.Framework.Rendering;
using Live2DCSharpSDK.OpenGL;

namespace Sparkle.Live2DView.Internal;

/// <summary>
/// 在官方 OpenGL 渲染器上插入「尾巴顶点摆动」。
///
/// <para><b>为什么必须在这一层做</b>:尾巴摆动是<b>顶点级</b>效果,而参数写入发生在
/// <c>Model.Update()</c> 之前 —— 参数驱动的顶点会被核心随后重算覆盖,写不进去。
/// 唯一可用的窗口是「<c>Model.Update()</c> 已重算顶点、<c>DoDrawModel</c> 尚未把顶点上传 GPU」,
/// 也就是本类的 <see cref="DoDrawModel"/> 覆写点(官方渲染器在此用
/// <c>model.GetDrawableVertices(index)</c> 读取<b>实时</b>缓冲,故写入立即生效)。</para>
/// </summary>
internal sealed class TailSwayRenderer : CubismRenderer_OpenGLES2
{
    private readonly Live2DTailDeformer _deformer;

    public TailSwayRenderer(
        OpenGLApi gl,
        LAppDelegate lapp,
        CubismModel model,
        Live2DTailDeformer deformer)
        : base(gl, lapp, model)
    {
        _deformer = deformer;
    }

    protected override unsafe void DoDrawModel()
    {
        _deformer.Apply(Model);
        base.DoDrawModel();
    }
}
