using System.Text.Json;
using McKuro.Core.Models.Tower;
using McKuro.Core.Services.Tower;
using Xunit;

namespace McKuro.Tests;

/// <summary>
/// 终焉矩阵历史相关纯函数测试:
/// endTime 剩余毫秒的数字/字符串双形态解析(FlexibleLongConverter)与
/// 赛季结束绝对时间归整到当天 04:00(对齐 WutheringWavesTool convertToHourlyTimestamp)。
/// </summary>
public class NewTowerHistoryTests
{
    private static NewTowerData Parse(string json)
        => JsonSerializer.Deserialize(json, TowerJsonContext.Default.NewTowerData)!;

    [Fact]
    public void EndTime_Number_Parses()
    {
        var data = Parse("""{"isUnlock":true,"endTime":1724480400000}""");
        Assert.Equal(1_724_480_400_000, data.EndTime);
    }

    [Fact]
    public void EndTime_NumericString_Parses()
    {
        var data = Parse("""{"isUnlock":true,"endTime":"1724480400000"}""");
        Assert.Equal(1_724_480_400_000, data.EndTime);
    }

    [Fact]
    public void EndTime_Missing_Is_Null()
    {
        var data = Parse("""{"isUnlock":false}""");
        Assert.Null(data.EndTime);
    }

    [Fact]
    public void NormalizeToHour4_Floors_To_Same_Day_4AM()
    {
        var local = new DateTime(2026, 8, 25, 21, 34, 56, DateTimeKind.Local);
        var normalized = DateTimeOffset.FromUnixTimeMilliseconds(TowerService.NormalizeToHour4(local)).LocalDateTime;
        Assert.Equal(new DateTime(2026, 8, 25, 4, 0, 0, DateTimeKind.Local), normalized);
    }

    [Fact]
    public void NormalizeToHour4_Before_4AM_Stays_Same_Day()
    {
        // 对齐 Java 实现:直接设 HOUR_OF_DAY=4,不做跨日回退
        var local = new DateTime(2026, 8, 25, 2, 10, 0, DateTimeKind.Local);
        var normalized = DateTimeOffset.FromUnixTimeMilliseconds(TowerService.NormalizeToHour4(local)).LocalDateTime;
        Assert.Equal(new DateTime(2026, 8, 25, 4, 0, 0, DateTimeKind.Local), normalized);
    }

    [Fact]
    public void NormalizeToServerHour4_Uses_Server_Date_Regardless_Of_Input_Timezone()
    {
        // 服务器(UTC+8)2026-08-25 21:34 → 归整到服务器当天 04:00 = 2026-08-24 20:00 UTC
        var expected = new DateTimeOffset(2026, 8, 25, 4, 0, 0, TimeSpan.FromHours(8)).ToUnixTimeMilliseconds();
        Assert.Equal(expected, TowerService.NormalizeToServerHour4(new DateTime(2026, 8, 25, 21, 34, 56)));
        Assert.Equal(expected, TowerService.NormalizeToServerHour4(new DateTime(2026, 8, 25, 13, 34, 56, DateTimeKind.Utc)));
    }

    [Fact]
    public void NormalizeToServerHour4_Is_Stable_Around_Utc_Midnight()
    {
        // 回归(L16):赛季结束于服务器 04:00(= UTC 前一日 20:00)。旧实现按本机时间归整,
        // UTC+4 等时区里结束时刻恰在本机午夜附近,剩余毫秒的秒级抖动会让归整日期翻转 → 同一期两行历史。
        var end = new DateTime(2026, 8, 24, 20, 0, 0, DateTimeKind.Utc);
        Assert.Equal(
            TowerService.NormalizeToServerHour4(end.AddSeconds(-20)),
            TowerService.NormalizeToServerHour4(end.AddSeconds(20)));
    }

    [Fact]
    public void EndTime_RoundTrips_Through_Serializer()
    {
        // FlexibleLongConverter.Write 曾总是写 null:序列化再读回会丢掉 endTime
        var json = JsonSerializer.Serialize(new NewTowerData { EndTime = 1_724_480_400_000 }, TowerJsonContext.Default.NewTowerData);
        Assert.Contains("\"endTime\":1724480400000", json);
        Assert.Equal(1_724_480_400_000, Parse(json).EndTime);
        var empty = JsonSerializer.Serialize(new NewTowerData(), TowerJsonContext.Default.NewTowerData);
        Assert.Null(Parse(empty).EndTime);
    }

    [Fact]
    public void PickRole_Prefers_Configured_RoleId_And_Falls_Back_To_First()
    {
        // 回归(M21):深塔页曾总是取账号绑定的第一个角色,与角色页(按配置的角色 ID)不一致
        var roles = new List<McKuro.Core.Models.Kuro.GameRoilDataItem>
        {
            new() { RoleId = "100001" },
            new() { RoleId = "100002" },
        };
        Assert.Equal("100002", TowerService.PickRole(roles, "100002").RoleId);
        Assert.Equal("100002", TowerService.PickRole(roles, " 100002 ").RoleId);
        Assert.Equal("100001", TowerService.PickRole(roles, "999999").RoleId);
        Assert.Equal("100001", TowerService.PickRole(roles, null).RoleId);
        Assert.Equal("100001", TowerService.PickRole(roles, "").RoleId);
    }

    [Fact]
    public void SeasonCycleDays_Is_28_After_Cutoff()
    {
        // 2025-02-03 起深塔/海墟改为 28 天周期(对齐 WutheringWavesTool SeasonService)
        Assert.Equal(28, TowerSeasonParser.SeasonCycleDays(new DateTime(2026, 9, 30)));
        Assert.Equal(28, TowerSeasonParser.SeasonCycleDays(new DateTime(2025, 2, 3)));
    }

    [Fact]
    public void SeasonCycleDays_Is_14_Before_Cutoff()
    {
        Assert.Equal(14, TowerSeasonParser.SeasonCycleDays(new DateTime(2025, 2, 2)));
        Assert.Equal(14, TowerSeasonParser.SeasonCycleDays(new DateTime(2024, 12, 1)));
    }

    [Fact]
    public void SeasonStartDate_Subtracts_One_Cycle()
    {
        // 往期历史条目的时间区间:起始日 = 结束日 - 周期。实机结束日 2026-09-30 → 起始 2026-09-02。
        Assert.Equal(new DateTime(2026, 9, 2), TowerSeasonParser.SeasonStartDate(new DateTime(2026, 9, 30)));
    }

    [Fact]
    public void SeasonStartDate_Uses_14_Day_Cycle_Before_Cutoff()
    {
        Assert.Equal(new DateTime(2025, 1, 19), TowerSeasonParser.SeasonStartDate(new DateTime(2025, 2, 2)));
    }
}
