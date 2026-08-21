using McKuro.Core.Infrastructure;
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

    [Fact]
    public void ParseSessions_Splits_On_Idle_Gap_Over_60_Minutes()
    {
        // 21:00 → 22:30 空闲 90 分钟(>60),应切分为两个独立会话,空闲不计入时长。
        string log = """
            [2026.08.15-20.00.00:000] 开始
            [2026.08.15-21.00.00:000] 结束
            [2026.08.15-22.30.00:000] 再次开始
            [2026.08.15-23.00.00:000] 再次结束
            """;
        var sessions = PlayTimeService.ParseSessions(log);
        Assert.Equal(2, sessions.Count);
        // 若不切分会把 20:00-23:00 整段(10800s)都算作游玩。
        Assert.Equal(3600, sessions[0].DurationSec);
        Assert.Equal(1800, sessions[1].DurationSec);
        Assert.Equal(20, sessions[0].StartTime.Hour);
        Assert.Equal(22, sessions[1].StartTime.Hour);
    }

    [Fact]
    public void ParseSessions_Keeps_Short_Break_Within_One_Session()
    {
        // 21:00 → 21:30 间隔 30 分钟(≤60),视为同一次游玩,合并为一段。
        string log = """
            [2026.08.15-20.00.00:000] 开始
            [2026.08.15-21.00.00:000] 结束
            [2026.08.15-21.30.00:000] 继续
            [2026.08.15-22.00.00:000] 结束
            """;
        var sessions = PlayTimeService.ParseSessions(log);
        var s = Assert.Single(sessions);
        Assert.Equal(7200, s.DurationSec); // 20:00-22:00
    }

    [Fact]
    public void GetAnalysis_Deduplicates_Repeated_Insertions_From_Same_Session()
    {
        // 历史版本反复解析会重复写入同一行;聚合时应按会话键去重,避免时长翻倍。
        var dir = Path.Combine(Path.GetTempPath(), "McKuro_pt_" + Guid.NewGuid().ToString("N"));
        try
        {
            using var db = new AppDatabase(dir);
            var today = DateTime.Today.ToString("yyyy-MM-dd");
            var start = $"{today} 12:00:00";
            var end = $"{today} 14:00:00";
            for (int i = 0; i < 3; i++)
            {
                using var cmd = db.Connection.CreateCommand();
                cmd.CommandText =
                    """
                    INSERT INTO game_time (role_id, game_date, start_time, end_time, duration_sec)
                    VALUES ('111', $date, $start, $end, 7200)
                    """;
                cmd.Parameters.AddWithValue("$date", today);
                cmd.Parameters.AddWithValue("$start", start);
                cmd.Parameters.AddWithValue("$end", end);
                cmd.ExecuteNonQuery();
            }

            var service = new PlayTimeService(new GamePathResolver(() => null), db);
            var analysis = service.GetAnalysis();

            Assert.Equal(7200, analysis.TodaySeconds); // 仅计一次,而非 21600
            Assert.Equal(7200, analysis.Last7DaysSeconds[6]); // 今天(窗口索引 6)
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (Exception) { }
        }
    }

    [Fact]
    public void GetAnalysis_Keeps_Most_Complete_Record_When_Open_Session_Grows()
    {
        // 游戏运行时反复解析:同一个进行中会话的结束时间会随解析推进而变长,应保留"最完整"的一条而非重复累加。
        var dir = Path.Combine(Path.GetTempPath(), "McKuro_pt_" + Guid.NewGuid().ToString("N"));
        try
        {
            using var db = new AppDatabase(dir);
            var today = DateTime.Today.ToString("yyyy-MM-dd");
            void Insert(string start, string end, int dur)
            {
                using var cmd = db.Connection.CreateCommand();
                cmd.CommandText =
                    """
                    INSERT INTO game_time (role_id, game_date, start_time, end_time, duration_sec)
                    VALUES ('111', $date, $start, $end, $dur)
                    """;
                cmd.Parameters.AddWithValue("$date", today);
                cmd.Parameters.AddWithValue("$start", start);
                cmd.Parameters.AddWithValue("$end", end);
                cmd.Parameters.AddWithValue("$dur", dur);
                cmd.ExecuteNonQuery();
            }
            Insert($"{today} 12:00:00", $"{today} 13:00:00", 3600);
            Insert($"{today} 12:00:00", $"{today} 14:00:00", 7200);

            var service = new PlayTimeService(new GamePathResolver(() => null), db);
            var analysis = service.GetAnalysis();

            Assert.Equal(7200, analysis.TodaySeconds); // 取最完整的一条,而非 3600+7200
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (Exception) { }
        }
    }
}
