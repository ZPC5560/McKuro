using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace McKuro.Core.Services.Notification;

/// <summary>一条已展示提醒的台账记录(同 Key 当日不再重复弹出)。</summary>
public sealed class StoredShownReminder
{
    /// <summary>稳定去重 Key(签到/活跃度按天、周本按周、活动按活动标识、登录按接口+天)。</summary>
    [JsonPropertyName("key")] public string Key { get; set; } = "";

    /// <summary>展示日期("yyyy-MM-dd";过期条目在加载/保存时清理)。</summary>
    [JsonPropertyName("day")] public string Day { get; set; } = "";
}

/// <summary>提醒台账(持久化根对象)。</summary>
public sealed class ReminderLedger
{
    [JsonPropertyName("shown")] public List<StoredShownReminder> Shown { get; set; } = [];
}

/// <summary>
/// 提醒台账持久化(数据目录 notifications.json,原子写入,源生成 JSON 上下文兼容 AOT)。
/// 只记录「哪天弹过哪些提醒」:悬浮通知本身是瞬时的,台账保证同一天内重启/轮询不重复打扰。
/// </summary>
public sealed class NotificationStore
{
    /// <summary>台账保留天数(跨天 Key 自动失效,超过保留期清理)。</summary>
    private const int RetentionDays = 2;

    private readonly string _path;
    private readonly ILogger<NotificationStore> _logger;

    public NotificationStore(string appDataDir, ILogger<NotificationStore>? logger = null)
    {
        _path = Path.Combine(appDataDir, "notifications.json");
        _logger = logger ?? new LoggerFactory().CreateLogger<NotificationStore>();
    }

    /// <summary>读取提醒台账;文件不存在/损坏时返回空台账(不视为错误)。</summary>
    public ReminderLedger Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path);
                var ledger = JsonSerializer.Deserialize(json, NotificationJsonContext.Default.ReminderLedger);
                if (ledger is not null)
                {
                    Prune(ledger);
                    return ledger;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "读取提醒台账失败,使用空台账: {Path}", _path);
        }
        return new ReminderLedger();
    }

    /// <summary>原子写入提醒台账(小文件,低频调用,同步写入即可;失败仅记日志)。</summary>
    public void Save(ReminderLedger ledger)
    {
        Prune(ledger);
        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            var tempPath = _path + ".tmp-" + Guid.NewGuid().ToString("N");
            File.WriteAllText(tempPath, JsonSerializer.Serialize(ledger, NotificationJsonContext.Default.ReminderLedger));
            File.Move(tempPath, _path, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存提醒台账失败: {Path}", _path);
        }
    }

    private static void Prune(ReminderLedger ledger)
    {
        var cutoff = DateTime.Now.AddDays(-RetentionDays).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        ledger.Shown.RemoveAll(s => string.CompareOrdinal(s.Day, cutoff) < 0);
    }
}

[JsonSerializable(typeof(ReminderLedger))]
[JsonSerializable(typeof(StoredShownReminder))]
public sealed partial class NotificationJsonContext : JsonSerializerContext;
