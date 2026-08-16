using McKuro.Core.Services.Guide;

namespace McKuro.Tests;

/// <summary>GuideRoleMap 解析测试(cardRoleId 直通 guide roleGbId)。</summary>
public class GuideRoleMapTests
{
    [Theory]
    [InlineData(1209, "1209")]
    [InlineData(1108, "1108")]
    [InlineData(1304, "1304")]
    public void TryGetRoleGbId_Int_Returns_CardRoleId_String(int cardRoleId, string expected)
    {
        Assert.Equal(expected, GuideRoleMap.TryGetRoleGbId(cardRoleId));
    }

    [Fact]
    public void TryGetRoleGbId_Int_Returns_Null_For_NonPositive()
    {
        Assert.Null(GuideRoleMap.TryGetRoleGbId(0));
        Assert.Null(GuideRoleMap.TryGetRoleGbId(-1));
    }

    [Fact]
    public void TryGetRoleGbId_String_Returns_Null_For_Unknown()
    {
        Assert.Null(GuideRoleMap.TryGetRoleGbId("漂泊者"));
    }
}
