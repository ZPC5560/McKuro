using System.Text.Json;

namespace Sparkle.Live2DView;

/// <summary>
/// 尾巴摆动(顶点级变形)。
///
/// <para><b>为什么需要它</b>:官方 Cubism 只提供「参数 → 关键形插值」这一种形变来源。
/// 但第三方「分层 PSD → Cubism」导出器会在 <c>export-status.json</c> 里自述两条限制 ——</para>
/// <list type="bullet">
///   <item>「几何为当前修订参数求值后的可编辑网格关键形，<b>原程序化变形器层级不保留</b>」</item>
///   <item>「运行时行为与物理通过配套 JSON 转换；<b>时间驱动的未参数化运动不等于原播放器</b>」</item>
/// </list>
/// <para>尾巴那种蓬松团的上下摆正是被丢掉的部分:实测原展示页尾巴质心纵摆 <b>20.6 px</b>
/// (周期 4.4s),而同一时刻头/躯干/腿只动 &lt;0.6 px —— 它是尾巴独立的程序化运动。
/// 导出后穷举该模型全部参数,能让尾巴<b>纵向</b>移动的只有 <c>body_roll</c>,全量程也只有 3.4~4.0 px
/// (约参考页的 19%),所以靠参数无法还原。</para>
///
/// <para><b>本类的做法</b>:绕尾巴根部施加一个「越靠尾尖位移越大」的位移场,直接叠加到顶点上。
/// 之所以可行,是因为已实测:</para>
/// <list type="number">
///   <item>渲染器每帧用 <c>model.GetDrawableVertices(index)</c> 读取<b>实时</b>顶点缓冲
///     (非缓存),写入后立即生效;</item>
///   <item>核心每帧都会重算顶点(写入被 <c>csmUpdateModel</c> 覆盖),因此叠加不会逐帧累积;</item>
///   <item>网格足够密(顶点间距 4~7 px),位移 20 px 时相邻顶点仅拉伸 6~20%,不会撕裂;</item>
///   <item>本类模型 0 个 drawable 使用遮罩,变形不会污染其它图层。</item>
/// </list>
///
/// <para>数值均归一化到「相对尾巴自身半径的比例」,因此在两个形态上自动适配,无需各自调参。</para>
/// </summary>
public static class Live2DTailSway
{
    /// <summary>摆动幅度占尾巴半径的比例。
    ///
    /// <para>标定方法与依据(按「尾巴质心纵摆 ÷ 模型高」比较,参考页实拍为 3.96%):</para>
    /// <para>用与运行时<b>完全相同的变形式</b>在 moc3 顶点上复算,结果可精确复现,
    /// 且两个形态互不相同的几何会收敛到几乎同一个值 —— 这是标定可信的强证据:</para>
    /// <list type="bullet">
    ///   <item>心-红黑 需要 0.0766 才与参考页同比例</item>
    ///   <item>心-白金 需要 0.0749</item>
    /// </list>
    /// <para>取较小值(避免较小尾巴的形态摆幅过大),即本值:质心纵摆约 21px / 模型高 541px。</para>
    ///
    /// <para><b>为什么不按抓帧结果标定</b>:抓帧测量受窗口尺寸漂移与相邻图层干扰 ——
    /// 尾巴框会连带框进不动的深色发丝,把质心位移稀释掉(同一配置曾量出 1.16% 与 2.30% 两种结果)。
    /// 顶点复算没有这些干扰源。</para>
    ///
    /// <para>按<b>尾巴自身半径</b>归一化(而非模型高度),这样尾巴更大的形态摆幅按比例更大,
    /// 观感一致且无需为每个形态单独调参。</para>
    /// </summary>
    public const float AmplitudeFraction = 0.075f;

    /// <summary>摆动周期(秒)。取原展示页尾巴实测的 4.4s,让节奏与参考一致。</summary>
    public const float Period = 4.4f;

    /// <summary>摆动初相位。</summary>
    public const float Phase = 1.9f;

    /// <summary>
    /// 横向分量占纵向的比例。参考页实测尾巴质心横摆 3.65px / 纵摆 20.59px ≈ 0.18,
    /// 按此取值。加少量横向使轨迹成为椭圆,避免看起来像整体升降。
    /// </summary>
    public const float HorizontalRatio = 0.18f;

    /// <summary>横向相对纵向的相位差,使轨迹成为椭圆而非直线。</summary>
    public const float HorizontalPhaseShift = 0.9f;

    /// <summary>
    /// 从导出器的 <c>export-status.json</c> 里挑出尾巴图层,以及用于定位"身体质心"的躯干图层。
    ///
    /// <para>不能用 moc3 自己的 drawable Id 去匹配关键字 —— 导出器把图层名截断并加了哈希
    /// (如 <c>ArtMeshPL_layer_000_______native_source_componen_52b1f8aaa8b8</c>),
    /// 里面既没有 "tail" 也没有可读信息。可读的图层名只存在于这份映射里,键是源图层名、
    /// 值是 moc3 的 drawable Id。</para>
    /// </summary>
    /// <param name="json">export-status.json 的内容;为空或格式不符时返回空结果。</param>
    public static (IReadOnlyList<string> Tail, IReadOnlyList<string> Body) ReadLayerIds(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return ([], []);
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("drawableIds", out JsonElement map)
                || map.ValueKind != JsonValueKind.Object)
            {
                return ([], []);
            }

            var tail = new List<string>();
            var body = new List<string>();
            foreach (JsonProperty entry in map.EnumerateObject())
            {
                if (entry.Value.ValueKind != JsonValueKind.String
                    || entry.Value.GetString() is not { Length: > 0 } artId)
                {
                    continue;
                }

                string name = entry.Name;
                if (name.Contains("tail", StringComparison.OrdinalIgnoreCase))
                {
                    tail.Add(artId);
                }
                else if (BodyLayerTokens.Any(token => name.Contains(token, StringComparison.OrdinalIgnoreCase)))
                {
                    body.Add(artId);
                }
            }

            return (tail, body);
        }
        catch (JsonException)
        {
            return ([], []);
        }
    }

    /// <summary>用于定位躯干的源图层名关键字 —— 尾巴的"根"要取离躯干质心最近的顶点。</summary>
    private static readonly string[] BodyLayerTokens =
        ["hip", "bodice", "neck", "torso", "waist"];

    /// <summary>找出离给定锚点(通常是身体质心)最近的顶点 —— 即尾巴的"根"。没有顶点时返回 -1。</summary>
    public static int FindRootIndex(
        ReadOnlySpan<float> xs,
        ReadOnlySpan<float> ys,
        float anchorX,
        float anchorY)
    {
        if (xs.Length == 0 || xs.Length != ys.Length)
        {
            return -1;
        }

        int best = 0;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < xs.Length; i++)
        {
            float dx = xs[i] - anchorX;
            float dy = ys[i] - anchorY;
            float distance = (dx * dx) + (dy * dy);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        return best;
    }

    /// <summary>
    /// 计算每个顶点的权重(0 = 根部完全不动,1 = 尾尖位移最大),
    /// 以及尾巴的半径(最远顶点到根的距离,模型单位)。半径用于把振幅换算成绝对位移。
    /// </summary>
    public static (float[] Weights, float Radius) ComputeWeights(
        ReadOnlySpan<float> xs,
        ReadOnlySpan<float> ys,
        int rootIndex)
    {
        if (xs.Length == 0 || xs.Length != ys.Length || rootIndex < 0 || rootIndex >= xs.Length)
        {
            return ([], 0f);
        }

        float rootX = xs[rootIndex];
        float rootY = ys[rootIndex];

        var distances = new float[xs.Length];
        float radius = 0f;
        for (int i = 0; i < xs.Length; i++)
        {
            float dx = xs[i] - rootX;
            float dy = ys[i] - rootY;
            distances[i] = MathF.Sqrt((dx * dx) + (dy * dy));
            radius = MathF.Max(radius, distances[i]);
        }

        // 所有顶点重合(退化网格)时无法归一化,全部按 0 处理 —— 不动比乱动安全。
        if (radius <= 0f || !float.IsFinite(radius))
        {
            return (new float[xs.Length], 0f);
        }

        var weights = new float[xs.Length];
        for (int i = 0; i < xs.Length; i++)
        {
            weights[i] = Math.Clamp(distances[i] / radius, 0f, 1f);
        }

        return (weights, radius);
    }

    /// <summary>
    /// 某顶点在本帧的纵向位移(模型单位)。<paramref name="weight"/> 由 <see cref="ComputeWeights"/> 得出。
    /// </summary>
    public static float VerticalOffset(float weight, float radius, double seconds) =>
        weight * radius * AmplitudeFraction * Live2DIdleMotion.Oscillate(seconds, Period, Phase);

    /// <summary>某顶点在本帧的横向位移(模型单位)。</summary>
    public static float HorizontalOffset(float weight, float radius, double seconds) =>
        weight * radius * AmplitudeFraction * HorizontalRatio
        * Live2DIdleMotion.Oscillate(seconds, Period, Phase + HorizontalPhaseShift);
}
