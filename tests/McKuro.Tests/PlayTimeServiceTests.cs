using McKuro.Core.Services.Game;

namespace McKuro.Tests;

/// <summary>PlayTimeService 日志解析测试(会话切分/跨天拆分/聚合)。</summary>
public class PlayTimeServiceTests
{
    [Fact]
    public void ParseSessions_Extracts_One_Session_From_Timestamps()
    {
        // 一段日志:无账号登录事件,首条到最后一条 = 一次会话
        string log = """
            [2026.08.15-20.00.00:000] 启动
            [2026.08.15-20.30.00:000] 中间
            [2026.08.15-21.00.00:000] 结束
            """;
        var sessions = PlayTimeService.ParseSessions(log);
        var s = Assert.Single(sessions);
        Assert.Equal("2026-08-15", s.GameDate);
        Assert.Equal(3600, s.DurationSec); // 1 小时
    }

    [Fact]
    public void ParseSessions_Splits_Across_Midnight()
    {
        // 跨天:23:30 → 次日 00:30,拆成两天
        string log = """
            [2026.08.15-23.30.00:000] 开始
            [2026.08.16-00.30.00:000] 结束
            """;
        var sessions = PlayTimeService.ParseSessions(log);
        Assert.Equal(2, sessions.Count);
        Assert.Equal("2026-08-15", sessions[0].GameDate);
        Assert.Equal(1800, sessions[0].DurationSec); // 30 分钟
        Assert.Equal("2026-08-16", sessions[1].GameDate);
        Assert.Equal(1800, sessions[1].DurationSec); // 30 分钟
    }

    [Fact]
    public void ParseSessions_Resets_On_New_Account_Login()
    {
        string log = """
            [2026.08.15-20.00.00:000] SetUserId [playerId:111] 登录
            [2026.08.15-20.30.00:000] 游玩
            [2026.08.15-21.00.00:000] SetUserId [playerId:222] 切号
            [2026.08.15-21.30.00:000] 游玩2
            """;
        var sessions = PlayTimeService.ParseSessions(log);
        // 两个账号登录 → 两个会话(切号前的会话 + 切号后的会话)
        Assert.Equal(2, sessions.Count);
        Assert.Equal("111", sessions[0].RoleId);
        Assert.Equal(3600, sessions[0].DurationSec);
        Assert.Equal("222", sessions[1].RoleId);
        Assert.Equal(1800, sessions[1].DurationSec);
    }

    [Fact]
    public void ParseSessions_Ignores_NonTimestamp_Lines()
    {
        string log = "no timestamp here\nplain text\n[2026.08.15-20.00.00:000] 开始\n[2026.08.15-20.30.00:000] 结束";
        var sessions = PlayTimeService.ParseSessions(log);
        var s = Assert.Single(sessions);
        Assert.Equal(1800, s.DurationSec);
    }

    [Fact]
    public void MergeSessions_Combines_Adjacent_Sessions()
    {
        // 两次会话间隔 20 分钟(≤60) → 合并为一个时段
        var sessions = new List<PlayTimeRecord>
        {
            new() { StartTime = new DateTime(2026, 8, 15, 20, 0, 0), EndTime = new DateTime(2026, 8, 15, 21, 0, 0) },
            new() { StartTime = new DateTime(2026, 8, 15, 21, 20, 0), EndTime = new DateTime(2026, 8, 15, 22, 0, 0) },
        };
        var merged = PlayTimeService.MergeSessions(sessions);
        var s = Assert.Single(merged);
        Assert.Equal("20:00", s.Start);
        Assert.Equal("22:00", s.End);
    }

    [Fact]
    public void MergeSessions_Keeps_Distant_Sessions_Separate()
    {
        // 两次会话间隔 2 小时(>60) → 保持两个独立时段
        var sessions = new List<PlayTimeRecord>
        {
            new() { StartTime = new DateTime(2026, 8, 15, 20, 0, 0), EndTime = new DateTime(2026, 8, 15, 21, 0, 0) },
            new() { StartTime = new DateTime(2026, 8, 15, 23, 0, 0), EndTime = new DateTime(2026, 8, 15, 23, 30, 0) },
        };
        var merged = PlayTimeService.MergeSessions(sessions);
        Assert.Equal(2, merged.Count);
        Assert.Equal("20:00", merged[0].Start);
        Assert.Equal("23:00", merged[1].Start);
    }
}
