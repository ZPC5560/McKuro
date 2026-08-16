using System.Text.Json;
using McKuro.Core.Models.Redeem;

namespace McKuro.Tests;

/// <summary>兑换码模型反序列化测试(用真实接口 sample,确保完整含前瞻码)。</summary>
public class RedeemModelTests
{
    [Fact]
    public void Deserialize_FullSample_IncludesPreviewCodes()
    {
        // 真实接口响应样本(mc1001 9 条含 3.6/3.5 前瞻码)
        string json = """
            {"code":200,"msg":"查询成功","data":{"mc1001":[
              {"key":"SAYCHEESE","startTime":"2026-05-29 19:00:00","endTime":"2026-05-31 23:59:00","description":null,"reward":"星声*100","contributors":"","valid":false,"gameIds":["mc1001"],"gameName":"鸣潮（国服）"},
              {"key":"THEANSWER","startTime":"2026-08-07 00:00:00","endTime":"2026-08-09 23:59:00","description":"3.6前瞻","reward":"星声*100；","contributors":"","valid":false,"gameIds":["mc1001","mc1002"],"gameName":"鸣潮（国服）"},
              {"key":"MECHANISMCITY","startTime":"2026-07-01 00:00:00","endTime":"2026-07-03 23:59:00","description":"3.5前瞻","reward":"星声*100；特级共鸣促剂*4","contributors":"","valid":false,"gameIds":["mc1001"],"gameName":"鸣潮（国服）"},
              {"key":"REUNION","startTime":"2026-07-01 00:00:00","endTime":"2026-07-03 23:59:00","description":"3.5前瞻","reward":"星声*100","contributors":"","valid":false,"gameIds":["mc1001"],"gameName":"鸣潮（国服）"}
            ],"mc1002":[
              {"key":"THEANSWER","startTime":"2026-08-07 00:00:00","endTime":"2026-08-09 23:59:00","description":"3.6前瞻","reward":"星声*100","contributors":"","valid":false,"gameIds":["mc1002"],"gameName":"鸣潮（国际服）"}
            ]}}
            """;
        var env = JsonSerializer.Deserialize(json, RedeemJsonContext.Default.RedemptionCodeEnvelope);
        Assert.NotNull(env);
        Assert.Equal(200, env!.Code);
        Assert.NotNull(env.Data);
        Assert.Equal(4, env.Data!.Mainland!.Count);
        Assert.Single(env.Data.Global!);

        // 前瞻码完整保留
        Assert.Contains(env.Data.Mainland, i => i.Key == "THEANSWER" && i.Description == "3.6前瞻");
        Assert.Contains(env.Data.Mainland, i => i.Key == "MECHANISMCITY" && i.Description == "3.5前瞻");
        Assert.Contains(env.Data.Mainland, i => i.Key == "REUNION");
        // 无效码也保留
        Assert.All(env.Data.Mainland, i => Assert.False(i.Valid));
    }

    [Fact]
    public void Deserialize_Ignores_UnknownFields()
    {
        // gameIds 等未知字段不破坏解析
        string json = """{"code":200,"msg":"ok","data":{"mc1001":[{"key":"X","reward":"y","valid":true,"gameIds":["mc1001"],"extra":"zz"}]}}""";
        var env = JsonSerializer.Deserialize(json, RedeemJsonContext.Default.RedemptionCodeEnvelope);
        var item = Assert.Single(env!.Data!.Mainland!);
        Assert.Equal("X", item.Key);
        Assert.True(item.Valid);
    }
}
