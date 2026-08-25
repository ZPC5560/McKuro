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
}
