using donet.Core.Services.Gacha;

namespace donet.Tests;

public class GachaUrlExtractorTests
{
    private const string RecordUrl =
        "https://aki-gm-resources.aki-game.com/aki/gacha/index.html#/record?player_id=1000123456&record_id=abc123def&resources_id=1001&gacha_type=1&svr_id=1000&lang=zh-Hans";

    [Fact]
    public void FindRecordUrl_ExtractsLastUrl()
    {
        var log = """
                  2024.07.01-10.00.00:000  [Log] some content
                  https://aki-gm-resources.aki-game.com/aki/gacha/index.html#/record?player_id=1000123456&record_id=abc123def&resources_id=1001&gacha_type=1&svr_id=1000&lang=zh-Hans
                  2024.07.02-12.00.00:000  [Log] more content
                  """;

        var url = GachaUrlExtractor.FindRecordUrl(log);
        Assert.NotNull(url);
        Assert.Contains("record?player_id=1000123456", url);
    }

    [Fact]
    public void FindRecordUrl_NoUrl_ReturnsNull()
    {
        Assert.Null(GachaUrlExtractor.FindRecordUrl("nothing here"));
        Assert.Null(GachaUrlExtractor.FindRecordUrl(null));
    }

    [Fact]
    public void ParseUrl_ExtractsAllParams()
    {
        var request = GachaUrlExtractor.ParseUrl(RecordUrl);
        Assert.NotNull(request);
        Assert.Equal("1000123456", request!.PlayerId);
        Assert.Equal("abc123def", request.RecordId);
        Assert.Equal("1001", request.CardPoolId);
        Assert.Equal("1000", request.ServerId);
        Assert.Equal("zh-Hans", request.Language);
        Assert.True(request.IsValid);
        Assert.True(request.IsChinaServer); // 以 1 开头为国服
    }

    [Fact]
    public void ParseUrl_MissingParams_ReturnsNull()
    {
        Assert.Null(GachaUrlExtractor.ParseUrl("https://example.com/aki/gacha/index.html#/record?player_id=1"));
        Assert.Null(GachaUrlExtractor.ParseUrl("not-a-url"));
        Assert.Null(GachaUrlExtractor.ParseUrl(null));
    }

    [Fact]
    public void IsChinaServer_GlobalPlayerId_IsFalse()
    {
        var request = GachaUrlExtractor.ParseUrl(
            "https://example.com/aki/gacha/index.html#/record?player_id=900000001&record_id=r&resources_id=1&svr_id=1&lang=en");
        Assert.NotNull(request);
        Assert.False(request!.IsChinaServer);
    }
}
