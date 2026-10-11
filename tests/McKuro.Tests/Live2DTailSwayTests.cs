using Sparkle.Live2DView;
using Xunit;

namespace McKuro.Tests;

/// <summary>
/// 尾巴顶点摆动的纯逻辑测试。
///
/// <para>背景:第三方「分层 PSD → Cubism」导出器丢失了原工程的程序化变形器
/// (见 <c>export-status.json: limitations</c>),导致尾巴上下摆从参考页的 20.6px 掉到 3.1px。
/// 这里改为在顶点级重建该运动,故要把「认图层 / 找根 / 算权重 / 算位移」这些纯函数锁住。</para>
/// </summary>
public class Live2DTailSwayTests
{
    private const string Sample = """
    {
      "limitations": ["原始程序化变形器层级不保留"],
      "drawableIds": {
        "layer-000-原模型部件-native-source-component-parts-tail": "ArtMeshPL_layer_000_______native_source_componen_52b1f8aaa8b8",
        "layer-001-原模型部件-native-source-component-parts-back-hair-ca": "ArtMeshPL_layer_001_______native_source_componen_3d7d4640903f",
        "layer-005-原模型部件-native-source-component-parts-tail-accesso": "ArtMeshPL_layer_005_______native_source_componen_aaaaaaaaaaaa",
        "layer-006-原模型部件-native-source-component-parts-hip-base": "ArtMeshPL_layer_006_______native_source_componen_bbbbbbbbbbbb",
        "layer-009-原模型部件-native-source-component-parts-neck-shoulde": "ArtMeshPL_layer_009_______native_source_componen_cccccccccccc"
      }
    }
    """;

    [Fact]
    public void 能从导出状态里认出尾巴图层()
    {
        (IReadOnlyList<string> tail, _) = Live2DTailSway.ReadLayerIds(Sample);

        // tail-accesso 也算尾巴(它是尾巴上的挂饰,应一起摆动,否则会与尾巴分离)。
        Assert.Equal(2, tail.Count);
        Assert.Contains("ArtMeshPL_layer_000_______native_source_componen_52b1f8aaa8b8", tail);
        Assert.Contains("ArtMeshPL_layer_005_______native_source_componen_aaaaaaaaaaaa", tail);
    }

    [Fact]
    public void 能认出躯干图层用作锚点()
    {
        (_, IReadOnlyList<string> body) = Live2DTailSway.ReadLayerIds(Sample);

        Assert.Contains("ArtMeshPL_layer_006_______native_source_componen_bbbbbbbbbbbb", body);
        Assert.Contains("ArtMeshPL_layer_009_______native_source_componen_cccccccccccc", body);
        // 头发不是躯干,不能被当成锚点。
        Assert.DoesNotContain("ArtMeshPL_layer_001_______native_source_componen_3d7d4640903f", body);
    }

    [Fact]
    public void 尾巴图层不会被同时算作躯干()
    {
        (IReadOnlyList<string> tail, IReadOnlyList<string> body) = Live2DTailSway.ReadLayerIds(Sample);

        Assert.Empty(tail.Intersect(body));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{"drawableIds": []}""")]
    public void 无效或缺失的导出状态返回空(string? json)
    {
        (IReadOnlyList<string> tail, IReadOnlyList<string> body) = Live2DTailSway.ReadLayerIds(json);

        Assert.Empty(tail);
        Assert.Empty(body);
    }

    [Fact]
    public void 找根取离锚点最近的顶点()
    {
        float[] xs = [0.30f, 0.10f, 0.50f];
        float[] ys = [0.00f, 0.00f, 0.00f];

        // 锚点在原点附近 -> 最近的应是 x = 0.10。
        Assert.Equal(1, Live2DTailSway.FindRootIndex(xs, ys, 0f, 0f));
    }

    [Fact]
    public void 空顶点集找不到根()
    {
        Assert.Equal(-1, Live2DTailSway.FindRootIndex([], [], 0f, 0f));
    }

    [Fact]
    public void 根处权重为零尾尖权重为一()
    {
        // 根在 (0,0),尾尖在 (0,1);中间点 (0,0.5) 应得 0.5。
        float[] xs = [0f, 0f, 0f];
        float[] ys = [0f, 1f, 0.5f];

        (float[] weights, float radius) = Live2DTailSway.ComputeWeights(xs, ys, 0);

        Assert.Equal(0f, weights[0], 6);
        Assert.Equal(1f, weights[1], 6);
        Assert.Equal(0.5f, weights[2], 6);
        Assert.Equal(1f, radius, 6);
    }

    [Fact]
    public void 退化网格不会产生非有限权重()
    {
        // 所有顶点重合:半径为 0,不能拿去做除数。
        float[] xs = [0.2f, 0.2f];
        float[] ys = [0.3f, 0.3f];

        (float[] weights, float radius) = Live2DTailSway.ComputeWeights(xs, ys, 0);

        Assert.Equal(0f, radius);
        Assert.All(weights, w => Assert.True(float.IsFinite(w) && w == 0f));
    }

    [Fact]
    public void 根部顶点完全不动的()
    {
        // 根部权重为 0,因此任何时刻位移都必须恰好是 0 —— 否则尾巴会从身体上脱开。
        for (int i = 0; i < 40; i++)
        {
            double t = i * 0.23;
            Assert.Equal(0f, Live2DTailSway.VerticalOffset(0f, 0.36f, t));
            Assert.Equal(0f, Live2DTailSway.HorizontalOffset(0f, 0.36f, t));
        }
    }

    [Fact]
    public void 摆幅与尾巴半径成正比()
    {
        // 归一化到半径,才能让不同大小的形态观感一致。
        double t = 0.4;
        float small = Math.Abs(Live2DTailSway.VerticalOffset(1f, 0.2f, t));
        float large = Math.Abs(Live2DTailSway.VerticalOffset(1f, 0.4f, t));

        Assert.Equal(2f, large / small, 4);
    }

    [Fact]
    public void 纵摆幅度与参考页同量级()
    {
        // 参考页实拍:尾巴质心纵摆 ≈ 模型高的 3.96%(模型高 670px 标定)。
        // 本式子是"质心"位移 —— 权重从根到尖线性递增,故质心约为尾尖的半振幅。
        // 这里用与运行时相同的权重分布复算质心位移,防止后续调参把幅度改到不可见或夸张。
        const float radius = 0.3621f;      // 心-红黑 尾巴实测半径(模型单位)
        const float modelHeight = 541f / 670f;  // 模型高(模型单位),来自顶点复算

        // 权重线性 0..1 的质心均值 = 0.5
        float centroid = 0.5f * radius * Live2DTailSway.AmplitudeFraction * 2f * 670f;
        float fraction = centroid / (modelHeight * 670f);

        // 应落在参考页 3.96% 的合理邻域(标定目标是"观感一致",不必逐像素相同)。
        Assert.InRange(fraction, 0.02f, 0.07f);
    }

    [Fact]
    public void 摆动有正负两端而不是单向漂移()
    {
        var values = new List<float>();
        for (int i = 0; i < 90; i++)
        {
            values.Add(Live2DTailSway.VerticalOffset(1f, 0.36f, i * 0.1));
        }

        Assert.True(values.Max() > 0.005f, "应有正向位移");
        Assert.True(values.Min() < -0.005f, "应有负向位移");
        // 平均值接近 0 —— 尾巴绕原位摆动,不能整体偏移。
        Assert.InRange(values.Average(), -0.004f, 0.004f);
    }

    [Fact]
    public void 周期与参考页一致()
    {
        // 参考页尾巴纵摆周期实测 4.4s。
        Assert.Equal(4.4f, Live2DTailSway.Period);
    }

    [Fact]
    public void 横向幅度小于纵向()
    {
        // 参考页实测 横摆 3.65px / 纵摆 20.59px ≈ 0.18。
        Assert.InRange(Live2DTailSway.HorizontalRatio, 0.05f, 0.5f);
        Assert.True(Live2DTailSway.HorizontalRatio < 1f, "横向不应超过纵向,否则尾巴会左右乱甩");
    }
}
