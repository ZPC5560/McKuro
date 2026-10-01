using McKuro.Core.Services.Notification;

namespace McKuro.Tests;

/// <summary>提醒台账持久化测试:保存 → 加载无损;损坏文件回退空台账;过期台账记录按保留期清理。</summary>
public class NotificationStoreTests : IDisposable
{
    private readonly string _dir;

    public NotificationStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "mckuro-notif-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    private NotificationStore CreateStore() => new(_dir);

    [Fact]
    public void SaveThenLoad_RoundTripsShownRecords()
    {
        // 日期必须相对"今天"取:台账有 2 天保留期,写死绝对日期会让测试
        // 在两天后自行失效(曾写死 2026-09-25,到期后 Save 即被 Prune 掉)。
        var today = DateTime.Now.ToString("yyyy-MM-dd");
        var stamp = DateTime.Now.ToString("yyyyMMdd");
        var store = CreateStore();
        store.Save(new ReminderLedger
        {
            Shown =
            [
                new StoredShownReminder { Key = $"progress:liveness:{stamp}", Day = today },
                new StoredShownReminder { Key = $"login:kuro:{stamp}", Day = today },
            ],
        });

        var loaded = CreateStore().Load();
        Assert.Equal(2, loaded.Shown.Count);
        Assert.Contains(loaded.Shown, s => s.Key == $"progress:liveness:{stamp}" && s.Day == today);
        Assert.Contains(loaded.Shown, s => s.Key == $"login:kuro:{stamp}");
    }

    [Fact]
    public void Load_MissingFile_ReturnsEmptyLedger()
    {
        var ledger = CreateStore().Load();
        Assert.Empty(ledger.Shown);
    }

    [Fact]
    public void Load_CorruptFile_ReturnsEmptyLedger()
    {
        File.WriteAllText(Path.Combine(_dir, "notifications.json"), "{ not-json");
        var ledger = CreateStore().Load();
        Assert.Empty(ledger.Shown);
    }

    [Fact]
    public void Save_PrunesRecordsOlderThanRetention()
    {
        var store = CreateStore();
        store.Save(new ReminderLedger
        {
            Shown =
            [
                new StoredShownReminder { Key = "sign:old", Day = "2020-01-01" },
                new StoredShownReminder { Key = "sign:fresh", Day = DateTime.Now.ToString("yyyy-MM-dd") },
            ],
        });

        var loaded = CreateStore().Load();
        var record = Assert.Single(loaded.Shown);
        Assert.Equal("sign:fresh", record.Key);
    }
}
