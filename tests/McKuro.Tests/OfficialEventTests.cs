using System.Text.Json;
using McKuro.Core.Models.Wiki;

namespace McKuro.Tests;

/// <summary>库街区官方资讯(/forum/companyEvent/findEventList)模型解析测试。</summary>
public class OfficialEventTests
{
    private const string SampleJson = """
        {
          "code": 200,
          "msg": "成功",
          "data": {
            "list": [
              {
                "postId": "1539943889225334784",
                "postTitle": "3.6版本已知问题及更新说明",
                "coverUrl": "https://prod-alicdn-community.kurobbs.com/forum/abc.webp",
                "eventType": 3,
                "shelveTime": 1787192834000
              },
              {
                "postId": "1540436326624940032",
                "postTitle": "《鸣潮》巡回演唱会幕后影像",
                "coverUrl": "https://prod-alicdn-community.kurobbs.com/forum/def.png",
                "eventType": 2,
                "shelveTime": 1787313600000
              }
            ]
          }
        }
        """;

    [Fact]
    public void Envelope_Parses_Code_And_List()
    {
        var env = JsonSerializer.Deserialize(SampleJson, WikiJsonContext.Default.OfficialEventEnvelope);

        Assert.NotNull(env);
        Assert.Equal(200, env!.Code);
        var list = env.Data?.List;
        Assert.NotNull(list);
        Assert.Equal(2, list!.Count);
    }

    [Fact]
    public void Item_Maps_Fields_For_Card_Binding()
    {
        var env = JsonSerializer.Deserialize(SampleJson, WikiJsonContext.Default.OfficialEventEnvelope);
        var item = env!.Data!.List![0];

        Assert.Equal("1539943889225334784", item.PostId);
        Assert.Equal("3.6版本已知问题及更新说明", item.PostTitle);
        Assert.Equal("https://prod-alicdn-community.kurobbs.com/forum/abc.webp", item.CoverUrl);
        Assert.Equal(3, item.EventType); // 公告
        Assert.True(item.ShelveTime > 0);
    }

    [Fact]
    public void ShelveTime_Converts_To_Local_Date()
    {
        var env = JsonSerializer.Deserialize(SampleJson, WikiJsonContext.Default.OfficialEventEnvelope);
        var item = env!.Data!.List![1];

        var local = DateTimeOffset.FromUnixTimeMilliseconds(item.ShelveTime).LocalDateTime;
        Assert.True(local.Year is >= 2026 and <= 2100); // 合理时间窗
    }
}
