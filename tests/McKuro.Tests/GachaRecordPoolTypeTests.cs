using McKuro.Core.Models.Gacha;

namespace McKuro.Tests;

public class GachaRecordPoolTypeTests
{
    [Theory]
    [InlineData("角色精准调谐", CardPoolType.RoleActivity)]
    [InlineData("武器精准调谐", CardPoolType.WeaponsActivity)]
    [InlineData("角色调谐（常驻池）", CardPoolType.RoleResident)]
    [InlineData("武器调谐（常驻池）", CardPoolType.WeaponsResident)]
    [InlineData("新手调谐", CardPoolType.Beginner)]
    [InlineData("新手自选唤取", CardPoolType.Beginner)]
    [InlineData("新手自选唤取（感恩定向唤取）", CardPoolType.GratitudeOrientation)]
    [InlineData("角色新旅唤取", CardPoolType.CharacterNovice)]
    [InlineData("武器新旅唤取", CardPoolType.WeaponNovice)]
    [InlineData("角色联动唤取", CardPoolType.CharacterCollaboration)]
    [InlineData("武器联动唤取", CardPoolType.WeaponCollaboration)]
    [InlineData("角色忆旅唤取", CardPoolType.CharacterMemoryJourney)]
    [InlineData("武器忆旅唤取", CardPoolType.WeaponMemoryJourney)]
    public void PoolType_MapsOfficialNames(string label, CardPoolType expected)
    {
        var record = new GachaRecord { CardPoolType = label };
        Assert.Equal(expected, record.PoolType);
    }

    [Theory]
    [InlineData("1", CardPoolType.RoleActivity)]
    [InlineData("2", CardPoolType.WeaponsActivity)]
    [InlineData("3", CardPoolType.RoleResident)]
    [InlineData("4", CardPoolType.WeaponsResident)]
    [InlineData("5", CardPoolType.Beginner)]
    [InlineData("6", CardPoolType.BeginnerChoice)]
    [InlineData("7", CardPoolType.GratitudeOrientation)]
    [InlineData("8", CardPoolType.CharacterNovice)]
    [InlineData("9", CardPoolType.WeaponNovice)]
    [InlineData("10", CardPoolType.CharacterCollaboration)]
    [InlineData("11", CardPoolType.WeaponCollaboration)]
    [InlineData("12", CardPoolType.CharacterMemoryJourney)]
    [InlineData("13", CardPoolType.WeaponMemoryJourney)]
    public void PoolType_MapsNumericIds(string label, CardPoolType expected)
    {
        var record = new GachaRecord { CardPoolType = label };
        Assert.Equal(expected, record.PoolType);
    }

    [Fact]
    public void PoolType_UnknownNumericFallsBackToRoleActivity()
    {
        var record = new GachaRecord { CardPoolType = "99" };
        Assert.Equal(CardPoolType.RoleActivity, record.PoolType);
    }

    [Fact]
    public void PoolType_UnknownNameFallsBackToRoleActivity()
    {
        var record = new GachaRecord { CardPoolType = "未知卡池" };
        Assert.Equal(CardPoolType.RoleActivity, record.PoolType);
    }
}
