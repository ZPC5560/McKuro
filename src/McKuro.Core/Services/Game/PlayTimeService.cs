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
    /// <summary>最近 7 天每天每次游玩的独立时段(索引对齐 Last7DaysSeconds)。</summary>
    public List<PlayTimeSession>[] Last7DaysSessions { get; set; } = new List<PlayTimeSession>[7];
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

    /// <summary>连续日志活动间隔超过该分钟数即判定为空闲,结束当前游玩会话,避免把菜单/挂机/两次非连续游玩之间的空闲计入时长。</summary>
    internal const int IdleMinutes = 60;

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

    /// <summary>从解密后的日志文本提取游玩会话。</summary>
    /// <remarks>
    /// 会话边界规则:
    /// <list type="bullet">
    ///   <item>新账号登录(<c>SetUserId [playerId:…]</c>)结算上一会话(到当前登录时刻)。</item>
    ///   <item>连续日志活动间隔超过 <see cref="IdleMinutes"/> 分钟判定为空闲,结算上一会话(到最后一次活动时刻),
    ///         从而把挂机/菜单/两次非连续游玩之间的空闲排除在游玩时长之外。</item>
    /// </list>
    /// </remarks>
    public static List<PlayTimeRecord> ParseSessions(string plainText)
    {
        var sessions = new List<PlayTimeRecord>();
        using var reader = new StringReader(plainText);

        string currentRole = "";
        DateTime? sessionStart = null;
        DateTime? lastSeen = null;

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var ts = ParseTimestamp(line);
            if (ts is null)
            {
                continue;
            }

            var playerMatch = PlayerIdRegex.Match(line);
            var isLogin = playerMatch.Success;

            // 已有进行中的会话:遇到新账号登录,或日志活动出现长时间空闲,都应先结算当前会话。
            if (sessionStart is not null && lastSeen is not null)
            {
                var idleGap = (ts.Value - lastSeen.Value).TotalMinutes > IdleMinutes;
                if (isLogin || idleGap)
                {
                    // 登录切换结算到"登录时刻";空闲切分结算到"最后一次活动时刻",避免把空闲计入游玩。
                    var closeAt = isLogin ? ts.Value : lastSeen.Value;
                    AddSession(sessions, currentRole, sessionStart.Value, closeAt);
                    sessionStart = null;
                    lastSeen = null;
                }
            }

            if (isLogin)
            {
                currentRole = playerMatch.Groups[1].Value;
                sessionStart = ts.Value;
                lastSeen = ts.Value;
            }
            else
            {
                if (sessionStart is null)
                {
                    sessionStart = ts.Value;
                }
                lastSeen = ts.Value;
            }
        }

        if (sessionStart is not null && lastSeen is not null)
        {
            AddSession(sessions, currentRole, sessionStart.Value, lastSeen.Value);
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
                    INSERT INTO game_time (role_id, game_date, start_time, end_time, duration_sec)
                    SELECT $role, $date, $start, $end, $dur
                    WHERE NOT EXISTS (
                        SELECT 1 FROM game_time
                        WHERE role_id=$role AND game_date=$date AND start_time=$start AND end_time=$end
                    )
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

    /// <summary>从本地库聚合游玩时长分析(总/今日/最近 7 天滚动窗口分布)。</summary>
    public PlayTimeAnalysis GetAnalysis()
    {
        var analysis = new PlayTimeAnalysis();
        var today = DateTime.Today;

        try
        {
            // 按会话起点(role+date+start)聚合:历史版本因反复解析可能累积重复行,或游戏运行时
            // 同一进行中会话的结束时间每次解析都不同。这里按会话身份去重并保留"最完整"的一条(时长最大),
            // 避免把同一次游玩重复计入总时长。
            var bySession = new Dictionary<string, PlayTimeRecord>(StringComparer.Ordinal);
            using (var cmd = _db.Connection.CreateCommand())
            {
                cmd.CommandText = "SELECT role_id, game_date, start_time, end_time, duration_sec FROM game_time";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var role = reader.IsDBNull(0) ? "" : reader.GetString(0);
                    var date = reader.IsDBNull(1) ? "" : reader.GetString(1);
                    var startStr = reader.IsDBNull(2) ? "" : reader.GetString(2);
                    var endStr = reader.IsDBNull(3) ? "" : reader.GetString(3);
                    var start = DateTime.TryParse(startStr, out var s) ? s : DateTime.MinValue;
                    var end = DateTime.TryParse(endStr, out var e) ? e : DateTime.MinValue;
                    var rec = new PlayTimeRecord { RoleId = role, GameDate = date, StartTime = start, EndTime = end };
                    var key = $"{role}|{date}|{startStr}";
                    if (!bySession.TryGetValue(key, out var existing) || rec.DurationSec > existing.DurationSec)
                    {
                        bySession[key] = rec;
                    }
                }
            }
            var rows = bySession.Values.ToList();

            // 总时长 + 记录天数
            analysis.TotalSeconds = rows.Sum(r => r.DurationSec);
            analysis.RecordDays = rows.Select(r => r.GameDate).Where(d => d.Length > 0).Distinct().Count();

            // 今日
            var todayStr = today.ToString("yyyy-MM-dd");
            analysis.TodaySeconds = rows.Where(r => r.GameDate == todayStr).Sum(r => r.DurationSec);

            // 最近 7 天滚动窗口:从今天往前 6 天到今天,索引 0=最早一天,6=今天。
            var rangeStart = today.AddDays(-6);
            for (int i = 0; i < 7; i++)
            {
                var day = rangeStart.AddDays(i);
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

                // 每次开始玩到结束的时间段独立统计(参考睡眠检测:相邻会话间隔≤60min 合并为同一时段)
                var ordered = dayRows
                    .Where(r => r.StartTime > DateTime.MinValue && r.EndTime > r.StartTime)
                    .OrderBy(r => r.StartTime)
                    .ToList();
                analysis.Last7DaysSessions[i] = MergeSessions(ordered);
            }
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
}
