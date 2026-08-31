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
/// 最近 7 天游玩分析报告:在逐日数据之上做二次分析,
/// 输出关键指标(日均/最长单次/连续天数/最活跃日)、行为洞察(时段偏好/作息规律/游玩方式/周内趋势)
/// 与一段自动生成的总结文字。所有字段都是可直接绑定的展示文本。
/// </summary>
public sealed class PlayTimeWeeklyReport
{
    public bool HasData { get; init; }
    /// <summary>最近 7 天累计时长(如 7.2h)。</summary>
    public string TotalText { get; init; } = "--";
    /// <summary>日均时长(按有游玩的天平均)。</summary>
    public string AvgPerDayText { get; init; } = "--";
    /// <summary>最长单次时长。</summary>
    public string LongestSessionText { get; init; } = "--";
    /// <summary>最长单次所在日期(MM/dd)。</summary>
    public string LongestSessionDayText { get; init; } = "";
    /// <summary>连续游玩天数(截至今天)。</summary>
    public string StreakText { get; init; } = "--";
    /// <summary>最活跃的一天(MM/dd)。</summary>
    public string PeakDayText { get; init; } = "--";
    /// <summary>最活跃一天的时长。</summary>
    public string PeakDayDetail { get; init; } = "";
    /// <summary>时段偏好标签(夜猫型/清晨型/午后型/深夜型)。</summary>
    public string HabitTag { get; init; } = "--";
    public string HabitDetail { get; init; } = "暂无时段分布数据";
    /// <summary>作息规律度标签(很规律/较规律/随性,按每天首次开场时刻的波动)。</summary>
    public string RegularityTag { get; init; } = "--";
    public string RegularityDetail { get; init; } = "暂无开场数据";
    /// <summary>游玩方式标签(碎片轻玩/中度游玩/长时沉浸,按单次会话平均长度)。</summary>
    public string StyleTag { get; init; } = "--";
    public string StyleDetail { get; init; } = "暂无会话数据";
    /// <summary>周内趋势标签(后程发力/逐步收手/节奏稳定)。</summary>
    public string TrendTag { get; init; } = "--";
    public string TrendDetail { get; init; } = "暂无趋势数据";
    /// <summary>自动生成的整段总结文字。</summary>
    public string SummaryText { get; init; } = "";

    /// <summary>空报告(无任何 7 天数据时使用)。</summary>
    public static PlayTimeWeeklyReport Empty { get; } = new();
}

/// <summary>
/// 游玩统计服务:解析鸣潮客户端日志(Client.log,已 XOR 加密)提取游玩会话,
/// 计算总/今日时长与最近一周的时间区间分布。
/// <para>只统计游玩时长,不统计操作数量(参照 WutheringWavesTool GameTime,按用户要求简化)。</para>
/// </summary>
public sealed partial class PlayTimeService
{
    // [GeneratedRegex]:NativeAOT 下 RegexOptions.Compiled 被静默忽略回退解释器,
    // 源生成才是 AOT 真预编译(且零缓存查找)。日志解析按行循环,是本项目最热的正则路径。
    [GeneratedRegex(@"\[(\d{4})\.(\d{2})\.(\d{2})-(\d{2})\.(\d{2})\.(\d{2}):(\d{3})\]")]
    private static partial Regex TimestampRegex();

    [GeneratedRegex(@"SetUserId\s*\[playerId:\s*(\d+)")]
    private static partial Regex PlayerIdRegex();

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

            // 绝大多数行不含 SetUserId:先用 Ordinal 子串探测做廉价守卫,避免每行跑正则。
            Match? playerMatch = null;
            if (line.Contains("SetUserId", StringComparison.Ordinal))
            {
                var pm = PlayerIdRegex().Match(line);
                if (pm.Success)
                {
                    playerMatch = pm;
                }
            }
            var isLogin = playerMatch is not null;

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
                currentRole = playerMatch!.Groups[1].Value;
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
        var m = TimestampRegex().Match(line);
        if (!m.Success)
        {
            return null;
        }
        // 组内容直接从输入串 span 解析(InvariantCulture + None 样式只认 ASCII 数字):
        // 避免每行 7 次 Groups[n].Value 子串分配 + 文化敏感 int.Parse(string)。
        var s = line.AsSpan();
        if (TryInt(s.Slice(m.Groups[1].Index, m.Groups[1].Length), out var y)
            && TryInt(s.Slice(m.Groups[2].Index, m.Groups[2].Length), out var mo)
            && TryInt(s.Slice(m.Groups[3].Index, m.Groups[3].Length), out var d)
            && TryInt(s.Slice(m.Groups[4].Index, m.Groups[4].Length), out var h)
            && TryInt(s.Slice(m.Groups[5].Index, m.Groups[5].Length), out var mi)
            && TryInt(s.Slice(m.Groups[6].Index, m.Groups[6].Length), out var sec)
            && TryInt(s.Slice(m.Groups[7].Index, m.Groups[7].Length), out var ms))
        {
            try
            {
                return new DateTime(y, mo, d, h, mi, sec, ms);
            }
            catch (ArgumentOutOfRangeException)
            {
                // 形如 2 月 30 日的非法时间戳:与原实现一致,视为无效行。
                return null;
            }
        }
        return null;
    }

    private static bool TryInt(ReadOnlySpan<char> digits, out int value) =>
        int.TryParse(digits, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out value);

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

    /// <summary>
    /// 基于最近 7 天聚合数据生成分析报告(纯计算,便于单元测试):
    /// 关键指标(日均/最长单次/连续天数/最活跃日) + 行为洞察(时段偏好/作息规律/游玩方式/周内趋势) + 自动总结。
    /// </summary>
    public static PlayTimeWeeklyReport BuildWeeklyReport(PlayTimeAnalysis a)
    {
        long total = a.Last7DaysSeconds.Sum();
        int playedDays = a.Last7DaysSeconds.Count(s => s > 0);
        if (total <= 0 || playedDays == 0)
        {
            return PlayTimeWeeklyReport.Empty;
        }

        var dates = new DateTime[7];
        for (int i = 0; i < 7; i++)
        {
            dates[i] = DateTime.TryParse(a.Last7DaysDates[i], out var d) ? d : DateTime.MinValue;
        }

        // ── 关键指标:日均 / 最长单次 / 连续天数 / 最活跃日 ──
        double avgMin = total / 60.0 / playedDays;

        long longestMin = 0;
        DateTime longestDay = DateTime.MinValue;
        for (int i = 0; i < 7; i++)
        {
            foreach (var s in a.Last7DaysSessions[i] ?? [])
            {
                if (s.Minutes > longestMin)
                {
                    longestMin = s.Minutes;
                    longestDay = dates[i];
                }
            }
        }

        int streak = 0;
        for (int i = 6; i >= 0 && a.Last7DaysSeconds[i] > 0; i--)
        {
            streak++;
        }

        int peak = 0;
        for (int i = 1; i < 7; i++)
        {
            if (a.Last7DaysSeconds[i] > a.Last7DaysSeconds[peak])
            {
                peak = i;
            }
        }

        // ── 时段偏好:24 小时折成 4 个时段桶(凌晨/上午/午后/晚间),取占比最高者 ──
        long[] buckets = new long[4];
        for (int d = 0; d < 7; d++)
        {
            for (int h = 0; h < 24; h++)
            {
                buckets[h / 6] += a.Last7DaysHourlyMinutes[d, h];
            }
        }
        int hb = 0;
        for (int i = 1; i < 4; i++)
        {
            if (buckets[i] > buckets[hb])
            {
                hb = i;
            }
        }
        string[] habitTags = ["深夜型", "清晨型", "午后型", "夜猫型"];
        string[] bucketNames = ["凌晨 0-6 点", "上午 6-12 点", "午后 12-18 点", "晚间 18-24 点"];
        long bucketSum = buckets.Sum();
        string habitTag = "--";
        string habitDetail = "暂无时段分布数据";
        if (bucketSum > 0)
        {
            int share = (int)Math.Round(buckets[hb] * 100.0 / bucketSum);
            habitTag = habitTags[hb];
            habitDetail = $"时长占比最高的时段是{bucketNames[hb]},约 {share}%";
        }

        // ── 作息规律度:每天第一次开玩时刻的波动(标准差)──
        var firstStartMin = new List<double>();
        for (int i = 0; i < 7; i++)
        {
            var first = (a.Last7DaysSessions[i] ?? []).FirstOrDefault();
            if (first is not null
                && TimeSpan.TryParseExact(first.Start, @"hh\:mm", null, System.Globalization.TimeSpanStyles.None, out var t))
            {
                firstStartMin.Add(t.TotalMinutes);
            }
        }
        string regTag = "--";
        string regDetail = "暂无开场数据";
        if (firstStartMin.Count >= 2)
        {
            double mean = firstStartMin.Average();
            double sigma = Math.Sqrt(firstStartMin.Average(v => (v - mean) * (v - mean)));
            var meanTime = DateTime.Today.AddMinutes(mean);
            regTag = sigma switch { <= 45 => "很规律", <= 90 => "较规律", _ => "随性" };
            regDetail = $"开场平均 {meanTime:HH:mm},日常波动约 ±{(int)Math.Round(sigma)} 分钟";
        }

        // ── 游玩方式:单次会话的平均长度 ──
        var allSessions = a.Last7DaysSessions.SelectMany(x => x ?? []).ToList();
        string styleTag = "--";
        string styleDetail = "暂无会话数据";
        if (allSessions.Count > 0)
        {
            double avgSession = allSessions.Average(s => (double)s.Minutes);
            styleTag = avgSession switch { < 30 => "碎片轻玩", < 90 => "中度游玩", _ => "长时沉浸" };
            styleDetail = $"共 {allSessions.Count} 次游玩,单次平均 {(int)Math.Round(avgSession)} 分钟";
        }

        // ── 周内趋势:前半周(前 3 天) vs 后半周(后 4 天),按日均对比消除天数不对等 ──
        long firstHalf = a.Last7DaysSeconds.Take(3).Sum();
        long secondHalf = a.Last7DaysSeconds.Skip(3).Sum();
        string trendTag;
        string trendDetail;
        if (firstHalf <= 0 && secondHalf <= 0)
        {
            trendTag = "--";
            trendDetail = "暂无趋势数据";
        }
        else if (firstHalf <= 0)
        {
            trendTag = "渐入状态";
            trendDetail = "前半周未游玩,后半周开始活跃";
        }
        else
        {
            double pct = ((secondHalf / 4.0) - (firstHalf / 3.0)) * 100.0 / (firstHalf / 3.0);
            trendTag = pct switch { >= 15 => "后程发力", <= -15 => "逐步收手", _ => "节奏稳定" };
            trendDetail = $"后半周日均比前半周{(pct >= 0 ? "高" : "低")}{Math.Abs(pct):0}%";
        }

        // ── 自动总结 ──
        var parts = new List<string>
        {
            $"最近 7 天共 {playedDays} 天有游玩,累计 {FormatDuration(total)},日均约 {FormatMinutes((long)Math.Round(avgMin))}",
        };
        if (bucketSum > 0)
        {
            parts.Add($"游玩主要集中在{bucketNames[hb]}({habitTag})");
        }
        if (firstStartMin.Count >= 2)
        {
            parts.Add($"开场时间{regTag}");
        }
        if (allSessions.Count > 0)
        {
            parts.Add($"{styleTag},单次平均 {(int)Math.Round(allSessions.Average(s => (double)s.Minutes))} 分钟");
        }
        if (trendTag != "--")
        {
            parts.Add($"整体{trendTag}");
        }
        string summary = string.Join(";", parts) + "。";

        return new PlayTimeWeeklyReport
        {
            HasData = true,
            TotalText = FormatDuration(total),
            AvgPerDayText = FormatMinutes((long)Math.Round(avgMin)),
            LongestSessionText = longestMin > 0 ? FormatMinutes(longestMin) : "--",
            LongestSessionDayText = longestMin > 0 ? longestDay.ToString("MM/dd") : "",
            StreakText = $"{streak} 天",
            PeakDayText = dates[peak] == DateTime.MinValue ? "--" : dates[peak].ToString("MM/dd"),
            PeakDayDetail = FormatDuration(a.Last7DaysSeconds[peak]),
            HabitTag = habitTag,
            HabitDetail = habitDetail,
            RegularityTag = regTag,
            RegularityDetail = regDetail,
            StyleTag = styleTag,
            StyleDetail = styleDetail,
            TrendTag = trendTag,
            TrendDetail = trendDetail,
            SummaryText = summary,
        };
    }

    /// <summary>秒 → "1.2h"/"38min" 展示文本。</summary>
    private static string FormatDuration(long seconds)
    {
        if (seconds <= 0)
        {
            return "0min";
        }
        return seconds >= 3600 ? $"{seconds / 3600.0:0.#}h" : $"{seconds / 60}min";
    }

    /// <summary>分钟 → "1.2h"/"38min" 展示文本。</summary>
    private static string FormatMinutes(long minutes)
    {
        return FormatDuration(minutes * 60);
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
