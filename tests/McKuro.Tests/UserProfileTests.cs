using McKuro.Core.Models.User;

namespace McKuro.Tests;

/// <summary>
/// 开服玩家判定(2024-05-23 国服开服,开服当日及之后 10 天内注册视为开服玩家,
/// 对齐 Haiyu StaminaWrapper.IsShowTime 的 10 天窗口)。
/// </summary>
public class UserProfileTests
{
    /// <summary>按本地时区取某日 0 点的 unix 毫秒(与 IsLaunchPlayer 的 LocalDateTime 换算一致)。</summary>
    private static long LocalMidnightMs(int year, int month, int day)
    {
        var dt = new DateTime(year, month, day);
        return new DateTimeOffset(dt, TimeZoneInfo.Local.GetUtcOffset(dt)).ToUnixTimeMilliseconds();
    }

    [Fact]
    public void LaunchDay_Is_LaunchPlayer()
    {
        Assert.True(UserProfile.IsLaunchPlayer(LocalMidnightMs(2024, 5, 23)));
    }

    [Fact]
    public void WithinTenDays_After_Launch_Is_LaunchPlayer()
    {
        Assert.True(UserProfile.IsLaunchPlayer(LocalMidnightMs(2024, 5, 24)));
        Assert.True(UserProfile.IsLaunchPlayer(LocalMidnightMs(2024, 6, 2)));
    }

    [Fact]
    public void ElevenDays_After_Launch_Is_Not_LaunchPlayer()
    {
        Assert.False(UserProfile.IsLaunchPlayer(LocalMidnightMs(2024, 6, 3)));
    }

    [Fact]
    public void Before_Launch_Is_Not_LaunchPlayer()
    {
        Assert.False(UserProfile.IsLaunchPlayer(LocalMidnightMs(2024, 5, 22)));
    }

    [Fact]
    public void Recent_Account_Is_Not_LaunchPlayer()
    {
        Assert.False(UserProfile.IsLaunchPlayer(LocalMidnightMs(2025, 1, 1)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(123L)]
    public void Invalid_CreatTime_Is_Not_LaunchPlayer(long creatTimeMs)
    {
        Assert.False(UserProfile.IsLaunchPlayer(creatTimeMs));
    }
}
