using System.Text.RegularExpressions;
using McKuro.Core.Infrastructure;
using McKuro.Core.Services.Gacha;
using McKuro.Core.Services.Game;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace McKuro.Core.Services.Game;

/// <summary>单次游玩会话记录。</summary>
public sealed class PlayTimeRecord
{
    public string RoleId { get; set; } = "";
    public string GameDate { get; set; } = "";   // yyyy-MM-dd
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    /// <summary>时长(秒)。</summary>
    public long DurationSec => (long)(EndTime - StartTime).TotalSeconds;
}

/// <summary>单次游玩时段(独立统计,每次开始玩到结束)。</summary>
public sealed class PlayTimeSession
{
    /// <summary>开始时间(HH:mm)。</summary>
    public required string Start { get; init; }
    /// <summary>结束时间(HH:mm)。</summary>
    public required string End { get; init; }
    /// <summary>时长(分钟)。</summary>
    public required long Minutes { get; init; }
    /// <summary>展示文本(如 20:00-21:30 · 90分钟)。</summary>
    public string Display => $"{Start}-{End}";
}

/// <summary>游玩时长聚合结果。</summary>
public sealed class PlayTimeAnalysis
{
    /// <summary>总游玩时长(秒)。</summary>
    public long TotalSeconds { get; set; }
    /// <summary>今日游玩时长(秒)。</summary>
    public long TodaySeconds { get; set; }
    /// <summary>有游玩记录的天数。</summary>
    public int RecordDays { get; set; }
    /// <summary>最近 7 天每天时长(秒),索引 0=最远一天。</summary>
    public long[] Last7DaysSeconds { get; set; } = new long[7];
    /// <summary>最近 7 天每天时段分布(7×24 小时,每格分钟数)。</summary>
    public long[,] Last7DaysHourlyMinutes { get; set; } = new long[7, 24];
    /// <summary>最近 7 天的日期(索引对齐 Last7DaysSeconds)。</summary>
    public string[] Last7DaysDates { get; set; } = new string[7];
    /// <summary>最近 7 天每天最早开始游玩时间(时:分,无记录为空)。</summary>
    public string[] Last7DaysStartTime { get; set; } = new string[7];
    /// <summary>最近 7 天每天最晚结束游玩时间(时:分,无记录为空)。</summary>
    public string[] Last7DaysEndTime { get; set; } = new string[7];
    /// <summary>最近 7 天每天每次游玩的独立时段(索引对齐 Last7DaysSeconds)。</summary>
    public List<PlayTimeSession>[] Last7DaysSessions { get; set; } = new List<PlayTimeSession>[7];
    /// <summary>本周游玩时间范围报告(参考睡眠检测:合并相邻会话为时段)。</summary>
    public string WeeklyReportText { get; set; } = "";
}

/// <summary>
/// 游玩统计服务:解析鸣潮客户端日志(Client.log,已 XOR 加密)提取游玩会话,
/// 计算总/今日时长与最近一周的时间区间分布。
/// <para>只统计游玩时长,不统计操作数量(参照 WutheringWavesTool GameTime,按用户要求简化)。</para>
/// </summary>
public sealed class PlayTimeService
{
    private static readonly Regex TimestampRegex = new(
        @"\[(\d{4})\.(\d{2})\.(\d{2})-(\d{2})\.(\d{2})\.(\d{2}):(\d{3})\]",
        RegexOptions.Compiled);

    private static readonly Regex PlayerIdRegex = new(
        @"SetUserId\s*\[playerId:\s*(\d+)", RegexOptions.Compiled);

    private readonly GamePathResolver _paths;
    private readonly AppDatabase _db;
    private readonly ILogger<PlayTimeService> _logger;

    public PlayTimeService(GamePathResolver paths, AppDatabase db, ILogger<PlayTimeService>? logger = null)
    {
        _paths = paths;
        _db = db;
        _logger = logger ?? NullLogger<PlayTimeService>.Instance;
    }

    /// <summary>解析日志并把游玩会话写入本地库;返回本次解析出的会话数。</summary>
    public async Task<int> AnalyzeLogAsync(CancellationToken ct = default)
    {
        var logDir = _paths.LogDir;
        if (logDir is null || !Directory.Exists(logDir))
        {
            return 0;
        }

        var records = new List<PlayTimeRecord>();
        // 日志按修改时间排序,逐个处理(参照 WutheringWavesTool getSortedLogFiles)
        var files = Directory.GetFiles(logDir, "Client*.log")
            .OrderBy(f => File.GetLastWriteTime(f))
            .ToList();

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var plain = await Task.Run(() => ClientLogDecryptor.DecryptFile(file), ct).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(plain))
                {
                    continue;
                }
                var sessions = ParseSessions(plain);
                records.AddRange(sessions);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "解析日志失败: {File}", file);
            }
        }

        if (records.Count > 0)
        {
            SaveRecords(records);
        }
        return records.Count;
    }

    /// <summary>从解密后的日志文本提取游玩会话(每次会话 = 从账号登录到日志末尾/下次登录)。</summary>
    public static List<PlayTimeRecord> ParseSessions(string plainText)
    {
        var sessions = new List<PlayTimeRecord>();
        using var reader = new StringReader(plainText);

        string? currentRole = "";
        DateTime? sessionStart = null;
        DateTime? sessionEnd = null;

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var ts = ParseTimestamp(line);
            if (ts is null)
            {
                continue;
            }

            var playerMatch = PlayerIdRegex.Match(line);
            if (playerMatch.Success)
            {
                // 新账号登录:结算上一个会话(到当前登录时刻)
                if (sessionStart is not null && sessionEnd is not null)
                {
                    AddSession(sessions, currentRole, sessionStart.Value, ts.Value);
                }
                currentRole = playerMatch.Groups[1].Value;
                sessionStart = ts.Value;
                sessionEnd = ts.Value;
            }
            else
            {
                if (sessionStart is null)
                {
                    sessionStart = ts.Value;
                }
                sessionEnd = ts.Value;
            }
        }

        if (sessionStart is not null && sessionEnd is not null)
        {
            AddSession(sessions, currentRole, sessionStart.Value, sessionEnd.Value);
        }
        return sessions;
    }

    /// <summary>把一段游玩区间按天拆分成多条记录(对齐 WutheringWavesTool 跨天拆分)。</summary>
    private static void AddSession(List<PlayTimeRecord> sessions, string? roleId, DateTime start, DateTime end)
    {
        if (end <= start)
        {
            return;
        }
        var cursor = start;
        while (cursor < end)
        {
            var dayEnd = cursor.Date.AddDays(1);
            var segEnd = end < dayEnd ? end : dayEnd;
            if (segEnd > cursor)
            {
                sessions.Add(new PlayTimeRecord
                {
                    RoleId = roleId ?? "",
                    GameDate = cursor.ToString("yyyy-MM-dd"),
                    StartTime = cursor,
                    EndTime = segEnd,
                });
            }
            cursor = segEnd;
        }
    }

    private static DateTime? ParseTimestamp(string line)
    {
        var m = TimestampRegex.Match(line);
        if (!m.Success)
        {
            return null;
        }
        try
        {
            int y = int.Parse(m.Groups[1].Value);
            int mo = int.Parse(m.Groups[2].Value);
            int d = int.Parse(m.Groups[3].Value);
            int h = int.Parse(m.Groups[4].Value);
            int mi = int.Parse(m.Groups[5].Value);
            int s = int.Parse(m.Groups[6].Value);
            int ms = int.Parse(m.Groups[7].Value);
            return new DateTime(y, mo, d, h, mi, s, ms);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void SaveRecords(List<PlayTimeRecord> records)
    {
        try
        {
            using var tx = _db.Connection.BeginTransaction();
            foreach (var r in records)
            {
                if (r.DurationSec <= 0)
                {
                    continue;
                }
                using var cmd = _db.Connection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText =
                    """
                    INSERT OR IGNORE INTO game_time (role_id, game_date, start_time, end_time, duration_sec)
                    VALUES ($role, $date, $start, $end, $dur)
                    """;
                cmd.Parameters.AddWithValue("$role", r.RoleId);
                cmd.Parameters.AddWithValue("$date", r.GameDate);
                cmd.Parameters.AddWithValue("$start", r.StartTime.ToString("O"));
                cmd.Parameters.AddWithValue("$end", r.EndTime.ToString("O"));
                cmd.Parameters.AddWithValue("$dur", r.DurationSec);
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "写入游玩时长记录失败");
        }
    }

    /// <summary>从本地库聚合游玩时长分析(总/今日/最近一周分布)。</summary>
    public PlayTimeAnalysis GetAnalysis()
    {
        var analysis = new PlayTimeAnalysis();
        var today = DateTime.Today;

        try
        {
            var rows = new List<PlayTimeRecord>();
            using (var cmd = _db.Connection.CreateCommand())
            {
                cmd.CommandText = "SELECT role_id, game_date, start_time, end_time, duration_sec FROM game_time";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    rows.Add(new PlayTimeRecord
                    {
                        RoleId = reader.IsDBNull(0) ? "" : reader.GetString(0),
                        GameDate = reader.IsDBNull(1) ? "" : reader.GetString(1),
                        StartTime = DateTime.TryParse(reader.IsDBNull(2) ? "" : reader.GetString(2), out var s) ? s : DateTime.MinValue,
                        EndTime = DateTime.TryParse(reader.IsDBNull(3) ? "" : reader.GetString(3), out var e) ? e : DateTime.MinValue,
                    });
                }
            }

            // 总时长 + 记录天数
            analysis.TotalSeconds = rows.Sum(r => r.DurationSec);
            analysis.RecordDays = rows.Select(r => r.GameDate).Where(d => d.Length > 0).Distinct().Count();

            // 今日
            var todayStr = today.ToString("yyyy-MM-dd");
            analysis.TodaySeconds = rows.Where(r => r.GameDate == todayStr).Sum(r => r.DurationSec);

            // 最近 7 天:日期与分布
            for (int i = 0; i < 7; i++)
            {
                var day = today.AddDays(-(6 - i));
                var dayStr = day.ToString("yyyy-MM-dd");
                analysis.Last7DaysDates[i] = dayStr;
                var dayRows = rows.Where(r => r.GameDate == dayStr).ToList();
                analysis.Last7DaysSeconds[i] = dayRows.Sum(r => r.DurationSec);
                foreach (var r in dayRows)
                {
                    // 把会话的游玩分钟按小时摊到 24 格
                    var cursor = r.StartTime;
                    while (cursor < r.EndTime)
                    {
                        var hourEnd = cursor.Date.AddHours(cursor.Hour + 1);
                        var segEnd = r.EndTime < hourEnd ? r.EndTime : hourEnd;
                        var minutes = (long)(segEnd - cursor).TotalMinutes;
                        if (cursor.Day == day.Day && cursor.Month == day.Month && cursor.Year == day.Year)
                        {
                            analysis.Last7DaysHourlyMinutes[i, cursor.Hour] += minutes;
                        }
                        cursor = segEnd;
                    }
                }

                // 每天玩的时间范围:最早开始 ~ 最晚结束(仅取当天时间部分)
                if (dayRows.Count > 0)
                {
                    var startMin = dayRows.Where(r => r.StartTime > DateTime.MinValue).Min(r => r.StartTime);
                    var endMax = dayRows.Where(r => r.EndTime > DateTime.MinValue).Max(r => r.EndTime);
                    analysis.Last7DaysStartTime[i] = startMin.ToString("HH:mm");
                    analysis.Last7DaysEndTime[i] = endMax.ToString("HH:mm");
                }

                // 每次开始玩到结束的时间段独立统计(参考睡眠检测:相邻会话间隔≤60min 合并为同一时段)
                var ordered = dayRows
                    .Where(r => r.StartTime > DateTime.MinValue && r.EndTime > r.StartTime)
                    .OrderBy(r => r.StartTime)
                    .ToList();
                analysis.Last7DaysSessions[i] = MergeSessions(ordered);
            }

            analysis.WeeklyReportText = BuildWeeklyReport(analysis);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "聚合游玩时长失败");
        }
        return analysis;
    }

    /// <summary>把一天内相邻会话合并为时段(参考睡眠检测:间隔 ≤ 60 分钟视为同一时段)。</summary>
    internal static List<PlayTimeSession> MergeSessions(List<PlayTimeRecord> sessions)
    {
        var result = new List<PlayTimeSession>();
        if (sessions.Count == 0)
        {
            return result;
        }
        var currentStart = sessions[0].StartTime;
        var currentEnd = sessions[0].EndTime;
        foreach (var s in sessions.Skip(1))
        {
            if ((s.StartTime - currentEnd).TotalMinutes <= 60)
            {
                // 相邻会话:合并(取更晚的结束)
                if (s.EndTime > currentEnd)
                {
                    currentEnd = s.EndTime;
                }
            }
            else
            {
                AddSession(result, currentStart, currentEnd);
                currentStart = s.StartTime;
                currentEnd = s.EndTime;
            }
        }
        AddSession(result, currentStart, currentEnd);
        return result;
    }

    private static void AddSession(List<PlayTimeSession> list, DateTime start, DateTime end)
    {
        if (end <= start)
        {
            return;
        }
        list.Add(new PlayTimeSession
        {
            Start = start.ToString("HH:mm"),
            End = end.ToString("HH:mm"),
            Minutes = (long)(end - start).TotalMinutes,
        });
    }

    /// <summary>生成每周游玩时间范围报告(参考睡眠检测报告:列出每天时段 + 汇总)。</summary>
    private static string BuildWeeklyReport(PlayTimeAnalysis analysis)
    {
        string[] weekNames = ["周一", "周二", "周三", "周四", "周五", "周六", "周日"];
        var lines = new List<string>();
        var playedDays = 0;
        for (int i = 0; i < 7; i++)
        {
            var sessions = analysis.Last7DaysSessions[i] ?? [];
            if (sessions.Count == 0)
            {
                continue;
            }
            playedDays++;
            var ranges = string.Join("、", sessions.Select(s => s.Display));
            lines.Add($"{weekNames[i]}: {ranges}");
        }
        if (playedDays == 0)
        {
            return "本周暂无游玩记录";
        }
        var report = $"本周共 {playedDays} 天有游玩,每日时段如下:\n" + string.Join("\n", lines);
        return report;
    }
}
