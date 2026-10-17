using Live2DCSharpSDK.Framework.Model;

namespace Sparkle.Live2DView.Internal;

/// <summary>
/// 尾巴摆动的顶点级变形器(见 <see cref="Live2DTailSway"/> 的原理说明)。
///
/// <para>每帧在「顶点已按参数求值完毕、尚未上传 GPU」之间,绕尾巴根部对尾巴图层顶点
/// 叠加一个「越靠尾尖位移越大」的位移场。</para>
///
/// <para><b>为什么不会累积</b>:核心的 <c>csmUpdateModel</c> 每帧都会重算顶点缓冲,
/// 本类写入的值在下一帧开头即被覆盖(已实测:写入后调用 <c>csmUpdateModel</c> 会恢复原值)。
/// 因此这里对"当前缓冲"施加位移是幂等安全的,无需自行保存静息姿态。</para>
///
/// <para>未配置(官方模型或读不到图层映射)时 <see cref="IsActive"/> 为 false,
/// <see cref="Apply"/> 直接返回,行为与改造前完全一致。</para>
/// </summary>
internal sealed class Live2DTailDeformer
{
    private readonly List<TailLayer> _layers = [];
    private IReadOnlyList<string> _requestedIds = [];
    private double _seconds;

    /// <summary>
    /// 是否已声明要接管尾巴(在模型加载<b>之前</b>设置)。
    ///
    /// <para>渲染器是在 <c>LAppModel</c> 构造函数里创建的,那时还拿不到模型顶点、无法标定;
    /// 因此必须先用本标志让 <c>CreateRenderer</c> 换成可注入的渲染器,加载完再调
    /// <see cref="Configure"/> 标定。渲染器持有的是同一个实例,标定后立即生效。</para>
    /// </summary>
    public bool IsRequested => _requestedIds.Count > 0;

    /// <summary>是否已成功标定(未标定时每帧零开销)。</summary>
    public bool IsActive => _layers.Count > 0;

    /// <summary>已接管的尾巴图层数(诊断用)。</summary>
    public int LayerCount => _layers.Count;

    /// <summary>声明要接管的尾巴图层 artId(见 <see cref="Live2DTailSway.ReadTailDrawableIds"/>)。</summary>
    public void SetRequestedDrawableIds(IReadOnlyList<string>? drawableIds)
    {
        _requestedIds = drawableIds ?? [];
        _layers.Clear();
    }

    /// <summary>推进摆动相位。由每帧的参数回调调用(与渲染共用同一时间基准)。</summary>
    public void Advance(double deltaSeconds)
    {
        if (!double.IsFinite(deltaSeconds) || deltaSeconds <= 0)
        {
            return;
        }

        _seconds += deltaSeconds;
    }

    /// <summary>
    /// 按已声明的 artId 标定尾巴图层的根、权重与半径。找不到任何尾巴图层时不启用。
    ///
    /// <para>根取「离身体锚点最近的顶点」—— 摆动必须绕根部进行,否则整团尾巴会平移,
    /// 观感像飘走而不是甩动。</para>
    /// </summary>
    public unsafe void Configure(CubismModel model, IReadOnlyList<string> bodyDrawableIds)
    {
        _layers.Clear();

        if (model is null || _requestedIds.Count == 0)
        {
            return;
        }

        (float anchorX, float anchorY) = BodyCentroid(model, bodyDrawableIds);

        foreach (string id in _requestedIds)
        {
            int index = model.GetDrawableIndex(id);
            if (index < 0 || index >= model.GetDrawableCount())
            {
                continue;
            }

            int count = model.GetDrawableVertexCount(index);
            float* vertices = model.GetDrawableVertices(index);
            if (count <= 0 || vertices is null)
            {
                continue;
            }

            var xs = new float[count];
            var ys = new float[count];
            for (int i = 0; i < count; i++)
            {
                xs[i] = vertices[(i * 2) + 0];
                ys[i] = vertices[(i * 2) + 1];
            }

            int root = Live2DTailSway.FindRootIndex(xs, ys, anchorX, anchorY);
            (float[] weights, float radius) = Live2DTailSway.ComputeWeights(xs, ys, root);
            if (radius <= 0f || weights.Length != count)
            {
                continue;
            }

            _layers.Add(new TailLayer(index, weights, radius));
        }
    }

    /// <summary>把本帧的摆动位移写到顶点缓冲上。必须在 <c>Model.Update()</c> 之后、绘制之前调用。</summary>
    public unsafe void Apply(CubismModel model)
    {
        if (_layers.Count == 0 || model is null)
        {
            return;
        }

        foreach (TailLayer layer in _layers)
        {
            int count = model.GetDrawableVertexCount(layer.Index);
            float* vertices = model.GetDrawableVertices(layer.Index);
            if (vertices is null || count != layer.Weights.Length)
            {
                continue;
            }

            for (int i = 0; i < count; i++)
            {
                float weight = layer.Weights[i];
                if (weight <= 0f)
                {
                    continue;
                }

                vertices[(i * 2) + 0] += Live2DTailSway.HorizontalOffset(weight, layer.Radius, _seconds);
                vertices[(i * 2) + 1] += Live2DTailSway.VerticalOffset(weight, layer.Radius, _seconds);
            }
        }
    }

    /// <summary>
    /// 躯干图层所有顶点的质心 —— 作为尾巴"根"的搜索锚点。
    /// 找不到躯干图层时退化为模型原点(仍可用:尾巴根部总是比尾尖更靠近身体中线)。
    /// </summary>
    private static unsafe (float X, float Y) BodyCentroid(
        CubismModel model,
        IReadOnlyList<string> bodyDrawableIds)
    {
        double sumX = 0;
        double sumY = 0;
        long count = 0;

        foreach (string id in bodyDrawableIds ?? [])
        {
            int index = model.GetDrawableIndex(id);
            if (index < 0 || index >= model.GetDrawableCount())
            {
                continue;
            }

            int vertexCount = model.GetDrawableVertexCount(index);
            float* vertices = model.GetDrawableVertices(index);
            if (vertices is null)
            {
                continue;
            }

            for (int i = 0; i < vertexCount; i++)
            {
                sumX += vertices[(i * 2) + 0];
                sumY += vertices[(i * 2) + 1];
                count++;
            }
        }

        return count == 0
            ? (0f, 0f)
            : ((float)(sumX / count), (float)(sumY / count));
    }

    private sealed record TailLayer(int Index, float[] Weights, float Radius);
}
